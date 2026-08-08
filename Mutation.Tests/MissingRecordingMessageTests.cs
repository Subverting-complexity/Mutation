using System;
using System.IO;
using Mutation.Ui;
using Mutation.Ui.Core;

namespace Mutation.Tests;

/// <summary>
/// Covers <see cref="AudioSessionManager.DescribeMissingRecording"/> — what Play says when the
/// selected recording is no longer on disk.
/// <para>
/// The refresh that follows lands the selection on a different recording, and Retry
/// transcription would then act on one the user never chose. Both halves have to arrive in a
/// single message: status announcements supersede rather than queue, so saying it in two calls
/// means the second talks over the first and only one is heard (issue #303).
/// </para>
/// </summary>
public class MissingRecordingMessageTests
{
	private static SpeechSession Session(string fileName) =>
		new(Path.Combine(Path.GetTempPath(), fileName), new DateTime(2026, 1, 1));

	[Fact]
	public void The_recording_that_replaced_it_is_named()
	{
		string message = AudioSessionManager.DescribeMissingRecording(
			Session("gone.ogg"),
			Session("newest.ogg"));

		Assert.Equal("Audio file not found. Selected newest.ogg instead.", message);
	}

	[Fact]
	public void An_emptied_list_just_reports_the_missing_file()
	{
		// Nothing was selected in its place, so there is nothing to name.
		Assert.Equal(
			"Audio file not found.",
			AudioSessionManager.DescribeMissingRecording(Session("gone.ogg"), nowSelected: null));
	}

	[Fact]
	public void A_selection_that_did_not_move_is_not_announced_as_a_move()
	{
		// The file is gone but the entry is still listed — telling the user it "selected
		// gone.ogg instead" would be a move that did not happen.
		var stillSelected = Session("gone.ogg");

		Assert.Equal(
			"Audio file not found.",
			AudioSessionManager.DescribeMissingRecording(stillSelected, stillSelected));
	}

	[Fact]
	public void A_selection_spelled_differently_is_not_announced_as_a_move()
	{
		var missing = Session("gone.ogg");
		var sameFileOtherSpelling = new SpeechSession(
			Path.Combine(Path.GetTempPath(), "sub", "..", "GONE.ogg"),
			new DateTime(2026, 1, 1));

		Assert.Equal(
			"Audio file not found.",
			AudioSessionManager.DescribeMissingRecording(missing, sameFileOtherSpelling));
	}
}
