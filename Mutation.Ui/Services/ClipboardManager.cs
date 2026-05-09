using System;
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
}

public class ClipboardManager
{
	public async Task<(ClipboardKind Kind, string Text)> InspectAsync()
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
	}

	public async Task<SoftwareBitmap?> TryGetImageAsync(int attempts = 5, int delayMs = 150)
	{
		while (attempts-- > 0)
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

	public async Task<string> GetTextAsync()
	{
		var content = Clipboard.GetContent();
		return content.Contains(StandardDataFormats.Text) ? await content.GetTextAsync() : string.Empty;
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
