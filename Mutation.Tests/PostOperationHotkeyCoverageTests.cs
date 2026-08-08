using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Mutation.Tests;

/// <summary>
/// Every operation that publishes a result to the user also sends the shortcut the user
/// configured to run afterwards.
/// <para>
/// Batch OCR of documents did not. It wrote the combined text to the clipboard, filled the
/// OCR box, and announced "Results copied to the clipboard" — and then sent nothing, on the
/// one OCR path where the user has waited longest for an answer they cannot see. The eight
/// sibling paths all sent it, which is exactly why the gap survived: nothing looked wrong
/// anywhere except the one handler nobody compared (issue #335).
/// </para>
/// <para>
/// Pinned by reading the handler source rather than by running it, because MainWindow needs
/// a WinUI host that the test project cannot start (issue #304). That makes this a weaker
/// test than a behavioural one — it proves the call is written, not that it fires — so it is
/// deliberately narrow: it asks only the question the bug was, which is whether a result
/// reaches the user with no shortcut behind it.
/// </para>
/// </summary>
public class PostOperationHotkeyCoverageTests
{
	private const string OcrSetting = "SendHotkeyAfterOcrOperation";
	private const string TranscriptionSetting = "SendHotkeyAfterTranscriptionOperation";
	private const string Send = "SendHotkeyAfterDelay";

	/// <summary>
	/// How far after a result is published the send may sit — enough for the status branches
	/// an OCR handler ends with. A search is additionally stopped at the next publish, so a
	/// send belonging to the following handler can never stand in for a missing one: the four
	/// hotkey handlers sit about thirteen lines apart, well inside this.
	/// </summary>
	private const int WindowLines = 25;

	/// <summary>
	/// Resolved by the compiler from this file's own location, so it cannot drift when the
	/// tests are run from a published or copied output directory.
	/// </summary>
	private static string MainWindowPath([CallerFilePath] string thisFile = "") =>
		Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "Mutation.Ui", "MainWindow.xaml.cs");

	private static string[] MainWindowSource()
	{
		string path = Path.GetFullPath(MainWindowPath());
		Assert.True(File.Exists(path), $"MainWindow.xaml.cs not found at {path}");
		return File.ReadAllLines(path);
	}

	private static List<int> LinesCalling(string[] source, Func<string, bool> matches) =>
		source.Select((line, index) => (line, index))
			.Where(entry => matches(entry.line))
			.Select(entry => entry.index)
			.ToList();

	/// <summary>
	/// Where an OCR result reaches the user: <c>SetOcrText</c> fills the OCR box and enables
	/// the download button. Matched by call shape rather than by argument name, so a new
	/// handler naming its result something else is still seen. The declaration is excluded.
	/// </summary>
	private static List<int> OcrPublishes(string[] source) =>
		LinesCalling(source, line =>
			line.Contains("SetOcrText(", StringComparison.Ordinal) &&
			!line.Contains("void SetOcrText(", StringComparison.Ordinal));

	/// <summary>
	/// Where a transcript run is decided finished. All three delivery sites — dictation, an
	/// LLM prompt run, and formatting — go through the planner.
	/// </summary>
	private static List<int> TranscriptPlans(string[] source) =>
		LinesCalling(source, line => line.Contains("TranscriptCompletionPlanner.Plan(", StringComparison.Ordinal));

	private static bool SendsWithin(string[] source, int from, int stopBefore, string setting)
	{
		int last = Math.Min(Math.Min(source.Length - 1, from + WindowLines), stopBefore - 1);
		for (int i = from; i <= last; i++)
		{
			if (!source[i].Contains(Send, StringComparison.Ordinal))
				continue;

			// The setting can sit on the call's own line or on the next one, since these
			// calls are written both ways.
			string call = string.Join(' ', source.Skip(i).Take(3));
			if (call.Contains(setting, StringComparison.Ordinal))
				return true;
		}

		return false;
	}

	private static List<string> Unsent(string[] source, List<int> publishes, string setting)
	{
		var missing = new List<string>();
		for (int n = 0; n < publishes.Count; n++)
		{
			int stopBefore = n + 1 < publishes.Count ? publishes[n + 1] : source.Length;
			if (!SendsWithin(source, publishes[n], stopBefore, setting))
				missing.Add($"MainWindow.xaml.cs:{publishes[n] + 1}: {source[publishes[n]].Trim()}");
		}

		return missing;
	}

	[Fact]
	public void Every_OCR_result_shown_to_the_user_is_followed_by_the_configured_shortcut()
	{
		var source = MainWindowSource();
		var publishes = OcrPublishes(source);

		// Not an exact count: a tenth OCR handler that does send is a correct change and
		// should not fail here. This only catches the search itself matching nothing after
		// a rename, which would make the test pass by asking nothing.
		Assert.NotEmpty(publishes);

		var unsent = Unsent(source, publishes, OcrSetting);

		Assert.True(unsent.Count == 0,
			"An OCR result is shown to the user with no configured shortcut sent after it:\n" +
			string.Join("\n", unsent));
	}

	[Fact]
	public void Every_transcript_delivery_is_followed_by_the_configured_shortcut()
	{
		var source = MainWindowSource();
		var plans = TranscriptPlans(source);

		Assert.NotEmpty(plans);

		var unsent = Unsent(source, plans, TranscriptionSetting);

		Assert.True(unsent.Count == 0,
			"A transcript is delivered with no configured shortcut sent after it:\n" +
			string.Join("\n", unsent));
	}

	[Fact]
	public void The_shortcut_is_sent_before_the_beep_and_the_status_that_can_throw()
	{
		var source = MainWindowSource();
		var plans = TranscriptPlans(source);

		Assert.NotEmpty(plans);

		// Ordering, not presence. The send used to be the last statement of each block, after
		// BeepPlayer.Play and ShowStatus — and ShowStatus builds an automation peer and starts
		// a timer, the operation issue #234 was filed about throwing. A throw there took the
		// shortcut with it, after the text had already reached the clipboard.
		foreach (int plan in plans)
		{
			var window = Enumerable.Range(plan, Math.Min(WindowLines, source.Length - plan)).ToList();
			int send = window.FirstOrDefault(i => source[i].Contains(Send, StringComparison.Ordinal), -1);
			int beep = window.FirstOrDefault(i => source[i].Contains("BeepPlayer.Play(plan.Beep)", StringComparison.Ordinal), -1);

			Assert.True(send >= 0 && beep >= 0,
				$"MainWindow.xaml.cs:{plan + 1}: expected both a send and a beep within " +
				$"{WindowLines} lines of the plan, found send={send + 1} beep={beep + 1}.");

			Assert.True(send < beep,
				$"MainWindow.xaml.cs:{plan + 1}: the shortcut is sent after the beep and status, " +
				"so a throw from either loses it.");
		}
	}
}
