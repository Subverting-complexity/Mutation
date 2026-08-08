using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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
	/// How far after the result is published the send may sit. Wide enough for the status
	/// branches an OCR handler ends with, narrow enough that a send belonging to the next
	/// handler cannot be mistaken for this one's.
	/// </summary>
	private const int WindowLines = 25;

	private static string[] MainWindowSource()
	{
		string relative = Path.Combine("Mutation.Ui", "MainWindow.xaml.cs");

		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory is not null && !File.Exists(Path.Combine(directory.FullName, relative)))
			directory = directory.Parent;

		Assert.True(directory is not null, $"{relative} not found above {AppContext.BaseDirectory}");
		return File.ReadAllLines(Path.Combine(directory!.FullName, relative));
	}

	private static IEnumerable<int> LinesCalling(string[] source, string fragment) =>
		source.Select((line, index) => (line, index))
			.Where(entry => entry.line.Contains(fragment, StringComparison.Ordinal))
			.Select(entry => entry.index);

	private static bool SendsWithin(string[] source, int from, string setting)
	{
		int last = Math.Min(source.Length - 1, from + WindowLines);
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

	[Fact]
	public void Every_OCR_result_shown_to_the_user_is_followed_by_the_configured_shortcut()
	{
		var source = MainWindowSource();

		// SetOcrText is how an OCR result reaches the user: it fills the OCR box and enables
		// the download button. Its own declaration is not a call, so it is excluded by the
		// parenthesis-and-argument shape.
		var publishes = LinesCalling(source, "SetOcrText(result.")
			.ToList();

		Assert.Equal(9, publishes.Count);

		var unsent = publishes
			.Where(line => !SendsWithin(source, line, OcrSetting))
			.Select(line => $"MainWindow.xaml.cs:{line + 1}: {source[line].Trim()}")
			.ToList();

		Assert.True(unsent.Count == 0,
			"An OCR result is shown to the user with no configured shortcut sent after it:\n" +
			string.Join("\n", unsent));
	}

	[Fact]
	public void Every_transcript_delivery_is_followed_by_the_configured_shortcut()
	{
		var source = MainWindowSource();

		// The planner is what decides a transcript run is finished, and all three delivery
		// sites — dictation, an LLM prompt run, and formatting — go through it.
		var plans = LinesCalling(source, "TranscriptCompletionPlanner.Plan(").ToList();

		Assert.Equal(3, plans.Count);

		var unsent = plans
			.Where(line => !SendsWithin(source, line, TranscriptionSetting))
			.Select(line => $"MainWindow.xaml.cs:{line + 1}: {source[line].Trim()}")
			.ToList();

		Assert.True(unsent.Count == 0,
			"A transcript is delivered with no configured shortcut sent after it:\n" +
			string.Join("\n", unsent));
	}

	[Fact]
	public void The_shortcut_is_sent_before_the_beep_and_the_status_that_can_throw()
	{
		var source = MainWindowSource();

		// Ordering, not presence. The send used to be the last statement of each block, after
		// BeepPlayer.Play and ShowStatus — and ShowStatus builds an automation peer and starts
		// a timer, the operation issue #234 was filed about throwing. A throw there took the
		// shortcut with it, after the text had already reached the clipboard.
		foreach (int plan in LinesCalling(source, "TranscriptCompletionPlanner.Plan("))
		{
			int send = Enumerable.Range(plan, Math.Min(WindowLines, source.Length - plan))
				.First(i => source[i].Contains(Send, StringComparison.Ordinal));
			int beep = Enumerable.Range(plan, Math.Min(WindowLines, source.Length - plan))
				.First(i => source[i].Contains("BeepPlayer.Play(plan.Beep)", StringComparison.Ordinal));

			Assert.True(send < beep,
				$"MainWindow.xaml.cs:{plan + 1}: the shortcut is sent after the beep and status, " +
				"so a throw from either loses it.");
		}
	}
}
