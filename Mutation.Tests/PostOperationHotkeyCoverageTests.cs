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
	/// LLM prompt run, and formatting — build their confirmation through one factory, and the
	/// confirmation sends the shortcut itself (issue #411). The declaration is excluded.
	/// </summary>
	private static List<int> TranscriptConfirmations(string[] source) =>
		LinesCalling(source, line =>
			line.Contains("CreateTranscriptConfirmation(", StringComparison.Ordinal) &&
			!line.Contains("TranscriptConfirmation CreateTranscriptConfirmation(", StringComparison.Ordinal));

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
	public void Every_single_image_OCR_handler_publishes_through_the_one_method_that_sends()
	{
		var source = MainWindowSource();

		// The four hotkey handlers and the four button handlers. Counted so that a ninth path
		// added without going through PublishOcrResult is a failure here rather than a shortcut
		// that silently never fires.
		int publishes = LinesCalling(source,
			line => line.Contains("PublishOcrResult(result", StringComparison.Ordinal)).Count;

		Assert.Equal(8, publishes);
	}

	/// <summary>
	/// The paste and the configured shortcut leave together, on every OCR path.
	/// <para>
	/// Split into two sends they would be two timers racing, and the loser decides whether the
	/// screen-reader command reads the pasted text or what was on screen before it. Composing
	/// them is also the only place that asks whether there is anything worth pasting, so a path
	/// that passed the setting straight through would paste after a failed read (issue #355).
	/// </para>
	/// </summary>
	[Fact]
	public void Every_OCR_send_composes_the_paste_and_the_shortcut_as_one_sequence()
	{
		var source = MainWindowSource();

		// Every read of the setting, wherever it sits relative to the send — the two OCR paths
		// write the call differently, and a third would be free to write it a third way.
		var reads = LinesCalling(source, line => line.Contains(OcrSetting, StringComparison.Ordinal));

		Assert.NotEmpty(reads);

		var bare = reads
			.Where(line => !source[line].Contains("PostOperationHotkey.AfterOcr(", StringComparison.Ordinal))
			.Select(line => $"MainWindow.xaml.cs:{line + 1}: {source[line].Trim()}")
			.ToList();

		Assert.True(bare.Count == 0,
			"An OCR path sends the configured shortcut without composing the paste with it:\n" +
			string.Join("\n", bare));
	}

	[Fact]
	public void Every_transcript_delivery_is_followed_by_the_configured_shortcut()
	{
		var source = MainWindowSource();

		// Dictation, an LLM prompt run, and formatting. Not an exact count, for the same
		// reason as the OCR test: this only catches the search matching nothing after a rename.
		Assert.NotEmpty(TranscriptConfirmations(source));

		// Every site goes through the factory, so the factory is where the shortcut is wired.
		// A second construction site would be free to leave the shortcut out.
		var constructions = LinesCalling(source, line => line.Contains("new TranscriptConfirmation(", StringComparison.Ordinal));
		int factory = Assert.Single(constructions);

		var body = string.Join(' ', source.Skip(Math.Max(0, factory - 5)).Take(10));
		Assert.True(
			body.Contains(TranscriptionSetting, StringComparison.Ordinal) && body.Contains(Send, StringComparison.Ordinal),
			$"MainWindow.xaml.cs:{factory + 1}: the transcript confirmation is built without sending " +
			$"the configured shortcut ({TranscriptionSetting}).");
	}

	[Fact]
	public void Every_transcript_site_confirms_before_the_status_that_can_throw()
	{
		var source = MainWindowSource();
		var sites = TranscriptConfirmations(source);

		Assert.NotEmpty(sites);

		// Confirm sends the shortcut and plays the beep. ShowStatus builds an automation peer
		// and starts a timer, the operation issue #234 was filed about throwing; a throw there
		// before Confirm took the shortcut with it, after the text had already reached the
		// clipboard (issue #335). The order inside Confirm, shortcut before beep, is pinned by
		// running it in TranscriptConfirmationTests.
		foreach (int site in sites)
		{
			var window = Enumerable.Range(site, Math.Min(WindowLines, source.Length - site)).ToList();
			int confirm = window.FirstOrDefault(i => source[i].Contains(".Confirm(", StringComparison.Ordinal), -1);
			int status = window.FirstOrDefault(i => source[i].Contains("ShowStatus(", StringComparison.Ordinal), -1);

			Assert.True(confirm >= 0,
				$"MainWindow.xaml.cs:{site + 1}: no Confirm within {WindowLines} lines of the confirmation, " +
				"so the shortcut and the beep may never go.");
			Assert.True(status < 0 || confirm < status,
				$"MainWindow.xaml.cs:{site + 1}: the status is shown before Confirm, " +
				"so a throw from it loses the shortcut.");
		}
	}
}
