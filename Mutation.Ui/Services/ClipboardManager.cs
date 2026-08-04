using System;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
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
	public virtual async Task<(ClipboardKind Kind, string Text)> InspectAsync()
	{
		var (success, result) = await ClipboardRetry.TryAsync(async () =>
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
				var content = Clipboard.GetContent();
				if (content.Contains(StandardDataFormats.Bitmap))
				{
					IRandomAccessStreamReference? streamRef = await content.GetBitmapAsync();
					if (streamRef != null)
					{
						using var stream = await streamRef.OpenReadAsync();
						var decoder = await BitmapDecoder.CreateAsync(stream);
						return await decoder.GetSoftwareBitmapAsync();
					}
				}
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

		return ClipboardRetry.TryAsync(() =>
		{
			var data = new DataPackage();
			data.SetText(text);
			Clipboard.SetContent(data);
			return Task.CompletedTask;
		});
	}

	public virtual async Task<string> GetTextAsync()
	{
		var (success, text) = await ClipboardRetry.TryAsync(async () =>
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

		bool read = await ClipboardRetry.TryAsync(async () =>
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

		return ClipboardRetry.TryAsync(async () =>
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
		});
	}

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

	public async Task SetImageAsync(SoftwareBitmap bitmap)
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
