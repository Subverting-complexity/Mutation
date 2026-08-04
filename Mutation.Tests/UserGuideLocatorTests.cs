using System;
using System.IO;
using Mutation.Ui.Services;

namespace Mutation.Tests;

/// <summary>
/// Covers finding the user guide that the User guide button on the main window
/// opens. The guide ships beside the executable, so the lookup has to agree with
/// where the build puts it.
/// </summary>
public class UserGuideLocatorTests
{
	[Fact]
	public void GetIndexPath_points_at_the_guide_folder_beside_the_executable()
	{
		string path = UserGuideLocator.GetIndexPath(@"C:\Program Files\Mutation");

		Assert.Equal(@"C:\Program Files\Mutation\UserGuide\index.html", path);
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	public void GetIndexPath_rejects_a_missing_base_directory(string baseDirectory)
	{
		Assert.Throws<ArgumentException>(() => UserGuideLocator.GetIndexPath(baseDirectory));
	}

	[Fact]
	public void GetIndexPath_rejects_a_null_base_directory()
	{
		Assert.Throws<ArgumentNullException>(() => UserGuideLocator.GetIndexPath(null!));
	}

	[Fact]
	public void Locate_finds_the_guide_when_it_is_installed()
	{
		using TempDirectory root = new();
		string guideFolder = Path.Combine(root.Path, UserGuideLocator.GuideFolderName);
		Directory.CreateDirectory(guideFolder);
		string indexPath = Path.Combine(guideFolder, UserGuideLocator.IndexFileName);
		File.WriteAllText(indexPath, "<p>guide</p>");

		UserGuideLocator.Result result = UserGuideLocator.Locate(root.Path);

		Assert.True(result.Found);
		Assert.Equal(indexPath, result.IndexPath);
		Assert.Null(result.ErrorMessage);
	}

	[Fact]
	public void Locate_explains_itself_when_the_guide_is_not_installed()
	{
		using TempDirectory root = new();

		UserGuideLocator.Result result = UserGuideLocator.Locate(root.Path);

		Assert.False(result.Found);
		Assert.NotNull(result.ErrorMessage);
		// The message is read aloud and shown on screen, so it has to say where it
		// looked and what to do - not just that something went wrong.
		Assert.Contains(result.IndexPath, result.ErrorMessage!, StringComparison.Ordinal);
		Assert.Contains("reinstalling", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void Locate_still_reports_the_path_it_looked_in_when_the_guide_is_missing()
	{
		using TempDirectory root = new();

		UserGuideLocator.Result result = UserGuideLocator.Locate(root.Path);

		Assert.Equal(UserGuideLocator.GetIndexPath(root.Path), result.IndexPath);
	}

	private sealed class TempDirectory : IDisposable
	{
		public string Path { get; }

		public TempDirectory()
		{
			Path = System.IO.Path.Combine(
				System.IO.Path.GetTempPath(),
				"mutation-guide-locator-" + Guid.NewGuid().ToString("n"));
			Directory.CreateDirectory(Path);
		}

		public void Dispose()
		{
			try
			{
				Directory.Delete(Path, recursive: true);
			}
			catch (IOException)
			{
				// A locked temp file must not fail the test run.
			}
		}
	}
}
