using CognitiveSupport;

namespace Mutation.Tests;

public class CustomBeepSettingsDataTests
{
	private static AudioSettings.CustomBeepSettingsData New() => new();

	[Fact]
	public void Resolve_NullPath_ReturnsNull()
	{
		Assert.Null(New().ResolveAudioFilePath(null!));
	}

	[Fact]
	public void Resolve_EmptyPath_ReturnsEmpty()
	{
		Assert.Equal(string.Empty, New().ResolveAudioFilePath(string.Empty));
	}

	[Fact]
	public void Resolve_WhitespacePath_ReturnsInputUnchanged()
	{
		Assert.Equal("   ", New().ResolveAudioFilePath("   "));
	}

	[Theory]
	[InlineData(@"\\server\share\beep.wav")]
	[InlineData("//server/share/beep.wav")]
	public void Resolve_UncPath_RejectedAsEmpty(string path)
	{
		Assert.Equal(string.Empty, New().ResolveAudioFilePath(path));
	}

	[Fact]
	public void Resolve_RelativePath_ResolvesUnderBaseDirectory()
	{
		string baseDir = Path.GetFullPath(AppContext.BaseDirectory);
		string resolved = New().ResolveAudioFilePath("beep.wav");

		Assert.False(string.IsNullOrEmpty(resolved));
		Assert.StartsWith(baseDir, resolved, StringComparison.OrdinalIgnoreCase);
		Assert.EndsWith("beep.wav", resolved, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void Resolve_RelativePathWithSubdirectory_ResolvesUnderBaseDirectory()
	{
		string baseDir = Path.GetFullPath(AppContext.BaseDirectory);
		string resolved = New().ResolveAudioFilePath(Path.Combine("Sounds", "beep.wav"));

		Assert.False(string.IsNullOrEmpty(resolved));
		Assert.StartsWith(baseDir, resolved, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void Resolve_AbsolutePathInsideBaseDirectory_Accepted()
	{
		string baseDir = Path.GetFullPath(AppContext.BaseDirectory);
		string absolute = Path.Combine(baseDir, "nested", "beep.wav");

		string resolved = New().ResolveAudioFilePath(absolute);

		Assert.False(string.IsNullOrEmpty(resolved));
		Assert.StartsWith(baseDir, resolved, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void Resolve_AbsolutePathOutsideBaseDirectory_RejectedAsEmpty()
	{
		// C:\Windows is virtually guaranteed to be outside the test runner's BaseDirectory.
		string outsidePath = Path.Combine(@"C:\", "Windows", "System32", "beep.wav");
		Assert.Equal(string.Empty, New().ResolveAudioFilePath(outsidePath));
	}

	[Theory]
	[InlineData(@"..\..\..\Windows\System32\foo.wav")]
	[InlineData("../../../etc/passwd")]
	public void Resolve_PathTraversalAttempt_RejectedAsEmpty(string path)
	{
		Assert.Equal(string.Empty, New().ResolveAudioFilePath(path));
	}

	[Fact]
	public void Resolve_RelativePathThatStaysInsideBase_Accepted()
	{
		string baseDir = Path.GetFullPath(AppContext.BaseDirectory);
		// "x/../beep.wav" resolves back to baseDir + "beep.wav" — inside base
		string resolved = New().ResolveAudioFilePath(Path.Combine("x", "..", "beep.wav"));

		Assert.False(string.IsNullOrEmpty(resolved));
		Assert.StartsWith(baseDir, resolved, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void Resolve_MalformedPath_ReturnsEmpty()
	{
		// Embedded null character makes Path.GetFullPath throw on Windows.
		string malformed = "bad\0path.wav";
		Assert.Equal(string.Empty, New().ResolveAudioFilePath(malformed));
	}
}
