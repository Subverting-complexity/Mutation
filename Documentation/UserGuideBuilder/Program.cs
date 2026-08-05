using Mutation.UserGuideBuilder;

// Command line:
//   UserGuideBuilder [--markdown <dir>] [--html <dir>] [--site-title <text>]
//
// With no arguments it finds the guide by walking up from the executable, so it
// works the same whether it is run by Build-UserGuide.cmd, from an IDE, or from
// a prompt in any folder.

try
{
	Dictionary<string, string> options = ParseArguments(args);

	string guideRoot = FindGuideRoot();
	string markdownDirectory = options.GetValueOrDefault("markdown", Path.Combine(guideRoot, "UserGuide", "markdown"));
	string htmlDirectory = options.GetValueOrDefault("html", Path.Combine(guideRoot, "UserGuide", "html"));
	string siteTitle = options.GetValueOrDefault("site-title", "Mutation User Guide");

	GuideBuilder.BuildResult result = GuideBuilder.Build(
		markdownDirectory,
		htmlDirectory,
		siteTitle,
		DateTimeOffset.Now);

	foreach (string stale in result.StalePagesRemoved)
	{
		Console.WriteLine($"  removed stale {stale}");
	}

	foreach (string page in result.PagesWritten)
	{
		Console.WriteLine($"  {Path.GetFileNameWithoutExtension(page)}.md -> {page}");
	}

	Console.WriteLine();
	Console.WriteLine($"Done. {result.PagesWritten.Count} page(s) written to {htmlDirectory}");
	Console.WriteLine($"Open {Path.Combine(htmlDirectory, GuideChapter.ContentsFileName)} to read the guide.");
	return 0;
}
catch (Exception ex) when (ex is DirectoryNotFoundException or InvalidOperationException or IOException or ArgumentException)
{
	// The failures a person is actually likely to hit deserve a plain sentence
	// rather than a stack trace.
	Console.Error.WriteLine();
	Console.Error.WriteLine(ex.Message);
	Console.Error.WriteLine();
	return 1;
}

static Dictionary<string, string> ParseArguments(string[] args)
{
	Dictionary<string, string> options = new(StringComparer.OrdinalIgnoreCase);

	for (int i = 0; i < args.Length; i++)
	{
		if (!args[i].StartsWith("--", StringComparison.Ordinal))
		{
			throw new ArgumentException($"Unexpected argument '{args[i]}'. Expected --markdown, --html or --site-title.");
		}

		string name = args[i][2..];
		if (i + 1 >= args.Length)
		{
			throw new ArgumentException($"Option --{name} needs a value after it.");
		}

		options[name] = args[++i];
	}

	return options;
}

// Walks up from the executable looking for the folder that holds the guide.
// The build output sits several levels below Documentation\, so this finds it
// without anyone having to care about the working directory.
static string FindGuideRoot()
{
	DirectoryInfo? directory = new(AppContext.BaseDirectory);

	for (int depth = 0; depth < 8 && directory is not null; depth++)
	{
		if (Directory.Exists(Path.Combine(directory.FullName, "UserGuide", "markdown")))
		{
			return directory.FullName;
		}

		directory = directory.Parent;
	}

	throw new DirectoryNotFoundException(
		"Could not find the UserGuide\\markdown folder by searching upwards from " +
		$"{AppContext.BaseDirectory}. Pass --markdown and --html explicitly.");
}
