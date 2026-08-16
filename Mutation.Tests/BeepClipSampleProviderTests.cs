using System;
using System.Linq;
using CognitiveSupport;
using Xunit;

namespace Mutation.Tests;

/// <summary>
/// What the mixer pulls out of one beep. The repeat counts here are the ones the transcription
/// retry ladders ask for — one beep per attempt, so a listener can tell a third retry from a
/// first (issue #216) — and they now have to survive being mixed alongside other sounds rather
/// than played on their own (issue #386).
/// </summary>
public class BeepClipSampleProviderTests
{
	private static BeepClip Clip(int frames, float value = 0.5f)
	{
		var samples = new float[frames * 2];
		for (var i = 0; i < samples.Length; i++)
			samples[i] = value;
		return new BeepClip(samples, BeepAudioOutput.SampleRate, 2);
	}

	// Reads until the provider says it is finished, in pieces of the given size, and hands back
	// everything it produced. The buffer size matters: a real device asks in whatever size its
	// buffers happen to be, never in whole clips.
	private static float[] DrainInChunks(BeepClipSampleProvider provider, int chunk)
	{
		var all = new System.Collections.Generic.List<float>();
		var buffer = new float[chunk];
		int read;
		while ((read = provider.Read(buffer, 0, buffer.Length)) > 0)
			all.AddRange(buffer.Take(read));
		return all.ToArray();
	}

	[Fact]
	public void One_beep_gives_back_exactly_the_clip()
	{
		var provider = new BeepClipSampleProvider(Clip(frames: 100), repeatCount: 1);

		var played = DrainInChunks(provider, chunk: 64);

		Assert.Equal(200, played.Length);
		Assert.All(played, s => Assert.Equal(0.5f, s));
	}

	[Theory]
	[InlineData(2)]
	[InlineData(3)]
	[InlineData(10)]
	public void A_repeat_plays_the_clip_that_many_times_over(int repeatCount)
	{
		var provider = new BeepClipSampleProvider(Clip(frames: 100), repeatCount);

		var played = DrainInChunks(provider, chunk: 64);

		Assert.Equal(200 * repeatCount, played.Length);
	}

	// A device never asks in clip-sized pieces, so the repeat must survive a read landing in the
	// middle of one. This is the case the old back-to-back synchronous playback never had.
	[Theory]
	[InlineData(1)]
	[InlineData(7)]
	[InlineData(199)]
	[InlineData(1024)]
	public void The_length_is_the_same_however_the_reads_fall(int chunk)
	{
		var provider = new BeepClipSampleProvider(Clip(frames: 100), repeatCount: 3);

		var played = DrainInChunks(provider, chunk);

		Assert.Equal(600, played.Length);
	}

	[Fact]
	public void A_finished_beep_reports_nothing_left_so_the_mixer_drops_it()
	{
		var provider = new BeepClipSampleProvider(Clip(frames: 10), repeatCount: 1);
		DrainInChunks(provider, chunk: 64);

		Assert.Equal(0, provider.Read(new float[64], 0, 64));
	}

	// Guards a loop that would otherwise never end: an empty clip copies nothing, so the
	// position never reaches the end and the repeat is never counted off.
	[Fact]
	public void An_empty_clip_ends_instead_of_spinning()
	{
		var provider = new BeepClipSampleProvider(new BeepClip(Array.Empty<float>(), BeepAudioOutput.SampleRate, 2), repeatCount: 4);

		Assert.Equal(0, provider.Read(new float[64], 0, 64));
	}

	[Fact]
	public void A_beep_is_offered_to_the_mixer_in_the_format_the_mixer_runs_at()
	{
		var provider = new BeepClipSampleProvider(Clip(frames: 10), repeatCount: 1);

		Assert.Equal(BeepAudioOutput.SampleRate, provider.WaveFormat.SampleRate);
		Assert.Equal(BeepAudioOutput.Channels, provider.WaveFormat.Channels);
	}

	[Fact]
	public void Writing_starts_where_the_caller_asked_and_leaves_the_rest_alone()
	{
		var provider = new BeepClipSampleProvider(Clip(frames: 5), repeatCount: 1);
		var buffer = new float[20];

		int read = provider.Read(buffer, 4, 10);

		Assert.Equal(10, read);
		Assert.Equal(0f, buffer[3]);
		Assert.All(buffer.Skip(4).Take(10), s => Assert.Equal(0.5f, s));
		Assert.Equal(0f, buffer[14]);
	}
}
