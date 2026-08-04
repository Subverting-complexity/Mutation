using System;
using System.IO;

namespace Mutation.Ui.Services;

/// <summary>
/// Finds the user guide's contents page on disk.
///
/// The guide ships beside the executable, under a UserGuide folder, copied there
/// from Documentation\UserGuide\html by the build. Keeping the lookup separate
/// from the launching means the interesting part - deciding where the guide is
/// and what to say when it is missing - can be tested without opening a browser.
/// </summary>
public static class UserGuideLocator
{
	/// <summary>Folder beside the executable that holds the generated pages.</summary>
	public const string GuideFolderName = "UserGuide";

	/// <summary>The page the button opens.</summary>
	public const string IndexFileName = "index.html";

	/// <summary>Where the guide was looked for, and whether it was there.</summary>
	/// <param name="Found">True when the contents page exists.</param>
	/// <param name="IndexPath">Full path to index.html, whether or not it exists.</param>
	/// <param name="ErrorMessage">
	/// A sentence explaining what to do about it, or null when <paramref name="Found"/> is true.
	/// </param>
	public sealed record Result(bool Found, string IndexPath, string? ErrorMessage);

	/// <summary>
	/// Works out the path to the guide's contents page relative to the folder the
	/// application is running from.
	/// </summary>
	public static string GetIndexPath(string baseDirectory)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);

		return Path.Combine(baseDirectory, GuideFolderName, IndexFileName);
	}

	/// <summary>
	/// Locates the guide, reporting a message a non-technical user can act on if it
	/// is not there.
	/// </summary>
	public static Result Locate(string baseDirectory)
	{
		string indexPath = GetIndexPath(baseDirectory);

		if (File.Exists(indexPath))
		{
			return new Result(Found: true, indexPath, ErrorMessage: null);
		}

		return new Result(
			Found: false,
			indexPath,
			$"The user guide could not be found at {indexPath}. It is installed alongside Mutation, " +
			"so reinstalling should restore it.");
	}
}
