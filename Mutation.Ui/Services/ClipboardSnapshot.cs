namespace Mutation.Ui.Services;

/// <summary>
/// A saved copy of the clipboard's content, captured before an operation
/// that overwrites the clipboard so it can be put back afterwards.
/// </summary>
public sealed class ClipboardSnapshot
{
	public string? Text { get; init; }

	/// <summary>Clipboard bitmap encoded as PNG, when the clipboard held an image.</summary>
	public byte[]? PngImageBytes { get; init; }

	/// <summary>True when there is anything worth restoring.</summary>
	public bool HasContent =>
		!string.IsNullOrEmpty(Text) || (PngImageBytes is { Length: > 0 });
}
