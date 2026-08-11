using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using CognitiveSupport;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Mutation.Ui.Services;

public enum ClipboardKind
{
	Empty,
	Text,
	Image,
	Unsupported,
	Unavailable,
}

public class ClipboardManager
{
	private readonly IUiThreadDispatcher? _uiThread;

	/// <param name="uiThread">
	/// Where the retrying calls on this class are made. Null means "make them wherever the
	/// caller is", which is what the tests that do not care about threading get.
	/// <para>
	/// It covers the methods that retry, not every method here. <see cref="SetText"/> and
	/// <see cref="SetImageAsync"/> are the single attempts the retrying methods are built from,
	/// so they are made wherever their caller already is — which, going through
	/// <see cref="TrySetTextAsync"/> or <see cref="TrySetImageAsync"/>, is the UI thread.
	/// </para>
	/// </param>
	public ClipboardManager(IUiThreadDispatcher? uiThread = null)
	{
		_uiThread = uiThread;
	}

	public virtual async Task<(ClipboardKind Kind, string Text)> InspectAsync()
	{
		var (success, result) = await RetryOnUiThreadAsync(async () =>
		{
			var content = Clipboard.GetContent();

			if (content.Contains(StandardDataFormats.Text))
			{
				string text = await content.GetTextAsync();
				if (!string.IsNullOrWhiteSpace(text))
					return (ClipboardKind.Text, text);
			}

			if (content.Contains(StandardDataFormats.Bitmap))
				return (ClipboardKind.Image, string.Empty);

			var formats = content.AvailableFormats;
			if (formats == null || formats.Count == 0)
				return (ClipboardKind.Empty, string.Empty);

			return (ClipboardKind.Unsupported, string.Empty);
		});

		return success ? result : (ClipboardKind.Unavailable, string.Empty);
	}

	// Virtual for the same reason SetText is: it reaches the real Windows clipboard, so
	// a test that wants to drive the OCR path has to be able to hand an image in.
	public virtual async Task<SoftwareBitmap?> TryGetImageAsync(int attempts = 5, int delayMs = 150)
	{
		while (attempts-- > 0)
		{
			try
			{
				// The hop is inside the loop, so every attempt is made on the UI thread rather
				// than only the ones that happen to start there. This loop used to depend on
				// the caller being on the UI thread and on the delay's continuation returning
				// to it — true today, and nothing enforced it.
				var bitmap = await OnUiThreadAsync<SoftwareBitmap?>(async () =>
				{
					var content = Clipboard.GetContent();
					if (!content.Contains(StandardDataFormats.Bitmap))
						return null;

					IRandomAccessStreamReference? streamRef = await content.GetBitmapAsync();
					if (streamRef is null)
						return null;

					using var stream = await streamRef.OpenReadAsync();
					var decoder = await BitmapDecoder.CreateAsync(stream);
					return await decoder.GetSoftwareBitmapAsync();
				});

				if (bitmap is not null)
					return bitmap;
			}
			catch
			{
				// Clipboard held by another process; fall through to the delay and retry.
			}

			await Task.Delay(delayMs);
		}
		return null;
	}

	public virtual void SetText(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
			return;

		var data = new DataPackage();
		data.SetText(text);
		Clipboard.SetContent(data);
	}

	/// <summary>
	/// Puts <paramref name="text"/> on the clipboard, retrying while another
	/// process holds the clipboard open. Returns false when it stayed
	/// unavailable after retries (or the text was blank).
	/// </summary>
	public virtual Task<bool> TrySetTextAsync(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
			return Task.FromResult(false);

		// Through the virtual SetText rather than a second copy of the DataPackage code, so a
		// test double substituting one substitutes both.
		return RetryOnUiThreadAsync(() =>
		{
			SetText(text);
			return Task.CompletedTask;
		});
	}

	public virtual async Task<string> GetTextAsync()
	{
		var (success, text) = await RetryOnUiThreadAsync(async () =>
		{
			var content = Clipboard.GetContent();
			return content.Contains(StandardDataFormats.Text) ? await content.GetTextAsync() : string.Empty;
		});

		return success ? text ?? string.Empty : string.Empty;
	}

	/// <summary>
	/// Captures the current clipboard content (text and/or bitmap) so it can
	/// be restored later. Returns null when nothing capturable is present or
	/// the clipboard could not be read.
	/// </summary>
	public virtual async Task<ClipboardSnapshot?> TryCaptureSnapshotAsync()
	{
		string? text = null;
		byte[]? pngBytes = null;

		bool read = await RetryOnUiThreadAsync(async () =>
		{
			var content = Clipboard.GetContent();

			if (content.Contains(StandardDataFormats.Text))
				text = await content.GetTextAsync();

			if (content.Contains(StandardDataFormats.Bitmap))
			{
				IRandomAccessStreamReference? streamRef = await content.GetBitmapAsync();
				if (streamRef != null)
				{
					using var stream = await streamRef.OpenReadAsync();
					var decoder = await BitmapDecoder.CreateAsync(stream);
					using var bitmap = await decoder.GetSoftwareBitmapAsync();
					pngBytes = await EncodeToPngAsync(bitmap);
				}
			}
		});

		if (!read)
			return null;

		var snapshot = new ClipboardSnapshot { Text = text, PngImageBytes = pngBytes };
		return snapshot.HasContent ? snapshot : null;
	}

	/// <summary>
	/// Puts a previously captured snapshot back on the clipboard.
	/// Returns false when the clipboard stayed unavailable after retries.
	/// </summary>
	public virtual Task<bool> TryRestoreSnapshotAsync(ClipboardSnapshot snapshot)
	{
		if (snapshot is null || !snapshot.HasContent)
			return Task.FromResult(false);

		return RetryOnUiThreadAsync(() => RestoreSnapshotAsync(snapshot));
	}

	/// <summary>
	/// One attempt at putting a snapshot back. Split out and virtual for the same reason
	/// <see cref="SetText"/> is: this is the write either side of a paste, and a test cannot
	/// reach the real clipboard to check that a busy one is retried rather than reported.
	/// </summary>
	protected virtual async Task RestoreSnapshotAsync(ClipboardSnapshot snapshot)
	{
		var data = new DataPackage();

		if (!string.IsNullOrEmpty(snapshot.Text))
			data.SetText(snapshot.Text);

		if (snapshot.PngImageBytes is { Length: > 0 })
		{
			var stream = new InMemoryRandomAccessStream();
			await stream.WriteAsync(snapshot.PngImageBytes.AsBuffer());
			stream.Seek(0);
			data.SetBitmap(RandomAccessStreamReference.CreateFromStream(stream));
		}

		Clipboard.SetContent(data);
	}

	/// <summary>
	/// Retries <paramref name="operation"/> while another process holds the clipboard open,
	/// making every attempt on the UI thread.
	/// <para>
	/// The retry wraps the thread hop, not the other way round, and that ordering is the whole
	/// point. <see cref="ClipboardRetry"/> waits with <c>ConfigureAwait(false)</c>, so a ladder
	/// that starts on the UI thread resumes its second attempt on the thread pool. The
	/// clipboard refuses a call from there outright, with an error no further attempt gets
	/// past, so attempts two to five were guaranteed to fail and these were one-attempt
	/// operations wearing a retry ladder (issue #352).
	/// </para>
	/// </summary>
	private async Task<bool> RetryOnUiThreadAsync(Func<Task> operation, [CallerMemberName] string operationName = "")
	{
		var (success, _) = await RetryOnUiThreadAsync<object?>(async () =>
		{
			await operation();
			return null;
		}, operationName);

		return success;
	}

	/// <inheritdoc cref="RetryOnUiThreadAsync(Func{Task}, string)"/>
	private async Task<(bool Success, T? Value)> RetryOnUiThreadAsync<T>(
		Func<Task<T>> operation, [CallerMemberName] string operationName = "")
	{
		// The last thing that went wrong is kept so a ladder that runs out has something to
		// report. ClipboardRetry swallows the exceptions, which is right for retrying and would
		// otherwise mean a permanent failure — a full disk, an encode that ran out of memory —
		// reaches the user as the same "the clipboard is busy" as a passing one, with nothing
		// written down anywhere.
		Exception? lastFailure = null;

		var result = await ClipboardRetry.TryAsync(async () =>
		{
			try
			{
				return await OnUiThreadAsync(operation);
			}
			catch (Exception ex)
			{
				lastFailure = ex;
				throw;
			}
		});

		if (!result.Success)
			OnClipboardGaveUp(operationName, lastFailure);

		return result;
	}

	/// <summary>
	/// Called once when a clipboard operation has used up its attempts, with whatever went wrong
	/// on the last of them. Writes it to the error log.
	/// <para>
	/// Virtual so the tests that deliberately exhaust a ladder can watch this instead of writing
	/// to the real log file, which other tests in this assembly are reading.
	/// </para>
	/// </summary>
	protected virtual void OnClipboardGaveUp(string operationName, Exception? lastFailure) =>
		ErrorLogger.LogError($"ClipboardManager.{operationName}", lastFailure);

	/// <summary>
	/// Runs one clipboard call on the UI thread. Runs it where it stands when there is no
	/// dispatcher, which is how the tests that do not care about threading see it.
	/// </summary>
	private Task<T> OnUiThreadAsync<T>(Func<Task<T>> operation) =>
		_uiThread is null ? operation() : _uiThread.RunAsync(operation);

	private static async Task<byte[]> EncodeToPngAsync(SoftwareBitmap bitmap)
	{
		using var stream = new InMemoryRandomAccessStream();
		var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
		encoder.SetSoftwareBitmap(bitmap);
		await encoder.FlushAsync();

		var bytes = new byte[stream.Size];
		stream.Seek(0);
		await stream.ReadAsync(bytes.AsBuffer(), (uint)stream.Size, InputStreamOptions.None);
		return bytes;
	}

	/// <summary>
	/// Puts <paramref name="bitmap"/> on the clipboard, retrying while another process holds the
	/// clipboard open. Returns false when it stayed unavailable after retries.
	/// <para>
	/// This is the screenshot write, and it opens the widest race in the app: it is the moment a
	/// clipboard manager or a screen reader notices something arrived and opens the clipboard to
	/// look at it. It used to be the one asynchronous write here with neither a retry nor a
	/// thread hop, and it reported failure by throwing, which reached the hotkey handler as an
	/// error dialog (issue #360). It now answers with a bool like every sibling, leaving the
	/// caller to decide what a busy clipboard is worth saying.
	/// </para>
	/// </summary>
	public virtual Task<bool> TrySetImageAsync(SoftwareBitmap bitmap)
	{
		if (bitmap is null)
			return Task.FromResult(false);

		return RetryOnUiThreadAsync(() => SetImageAsync(bitmap));
	}

	/// <summary>
	/// One attempt at putting an image on the clipboard. Split out and virtual for the same
	/// reason <see cref="SetText"/> is: nothing in a test can reach the real Windows clipboard,
	/// so without a seam here no test could check that a busy clipboard is retried.
	/// </summary>
	protected virtual async Task SetImageAsync(SoftwareBitmap bitmap)
	{
		var data = new DataPackage();
		var stream = new InMemoryRandomAccessStream();
		var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
		encoder.SetSoftwareBitmap(bitmap);
		await encoder.FlushAsync();
		stream.Seek(0);
		data.SetBitmap(RandomAccessStreamReference.CreateFromStream(stream));
		Clipboard.SetContent(data);
	}
}
