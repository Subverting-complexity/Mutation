using System;
using System.Collections.Generic;
using CognitiveSupport;
using Xunit;

namespace Mutation.Tests;

/// <summary>
/// The timeline written between a transcript landing and its confirmation being heard
/// (issue #411). It is written from the audio device's thread and the keystroke path, so the
/// rules that matter are that it reaches the log and that it can never throw at either.
/// <para>
/// The sink is static and other test classes write through it while they run, so each test
/// looks only for lines carrying its own marker, and puts the sink back afterwards.
/// </para>
/// </summary>
public class DeliveryTraceTests
{
	[Fact]
	public void A_line_reaches_the_sink_with_the_prefix_in_front()
	{
		var lines = new List<string>();
		DeliveryTrace.SetSink(line => { lock (lines) lines.Add(line); });
		try
		{
			DeliveryTrace.Write("marker-reaches-sink");
		}
		finally
		{
			DeliveryTrace.SetSink(null);
		}

		lock (lines)
			Assert.Contains(DeliveryTrace.Prefix + "marker-reaches-sink", lines);
	}

	[Fact]
	public void Writing_with_no_sink_does_nothing_and_does_not_throw()
	{
		DeliveryTrace.SetSink(null);

		Assert.Null(Record.Exception(() => DeliveryTrace.Write("marker-no-sink")));
	}

	[Fact]
	public void A_sink_that_throws_does_not_reach_the_caller()
	{
		DeliveryTrace.SetSink(_ => throw new InvalidOperationException("the log file is locked"));
		try
		{
			Assert.Null(Record.Exception(() => DeliveryTrace.Write("marker-throwing-sink")));
		}
		finally
		{
			DeliveryTrace.SetSink(null);
		}
	}

	[Fact]
	public void Clearing_the_sink_stops_lines_reaching_it()
	{
		var lines = new List<string>();
		DeliveryTrace.SetSink(line => { lock (lines) lines.Add(line); });
		DeliveryTrace.SetSink(null);

		DeliveryTrace.Write("marker-after-clear");

		lock (lines)
			Assert.DoesNotContain(lines, l => l.Contains("marker-after-clear", StringComparison.Ordinal));
	}
}
