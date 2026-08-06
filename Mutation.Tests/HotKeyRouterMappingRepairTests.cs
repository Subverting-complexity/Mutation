using System.Collections.Generic;
using CognitiveSupport;
using Xunit;

namespace Mutation.Tests;

using Map = HotKeyRouterSettings.HotKeyRouterMap;

// A router mapping with a missing hotkey used to throw out of the constructor during
// deserialization, taking the whole settings file down with it (issue #247).
public class HotKeyRouterMappingRepairTests
{
	[Fact]
	public void Repair_NullFromHotKey_BecomesBlankAndIsReported()
	{
		var mappings = new List<Map> { new(null, "CONTROL+SHIFT+ALT+9") };

		var issues = HotKeyRouterMappingRepair.Repair(mappings);

		Assert.Equal(string.Empty, mappings[0].FromHotKey);
		Assert.Equal("CONTROL+SHIFT+ALT+9", mappings[0].ToHotKey);
		Assert.Contains(issues, i => i.Contains("mapping 1") && i.Contains("'from' hotkey"));
	}

	[Fact]
	public void Repair_NullToHotKey_BecomesBlankAndIsReported()
	{
		var mappings = new List<Map> { new("CONTROL+SHIFT+ALT+8", null) };

		var issues = HotKeyRouterMappingRepair.Repair(mappings);

		Assert.Equal(string.Empty, mappings[0].ToHotKey);
		Assert.Contains(issues, i => i.Contains("mapping 1") && i.Contains("'to' hotkey"));
	}

	[Fact]
	public void Repair_MappingWithBothSidesNull_ReportsBoth()
	{
		var mappings = new List<Map> { new(null, null) };

		var issues = HotKeyRouterMappingRepair.Repair(mappings);

		Assert.Equal(2, issues.Count);
		Assert.Equal(string.Empty, mappings[0].FromHotKey);
		Assert.Equal(string.Empty, mappings[0].ToHotKey);
	}

	[Fact]
	public void Repair_NullEntry_IsRemovedAndReported()
	{
		var mappings = new List<Map> { new("A", "B"), null!, new("C", "D") };

		var issues = HotKeyRouterMappingRepair.Repair(mappings);

		Assert.Equal(2, mappings.Count);
		Assert.Equal("A", mappings[0].FromHotKey);
		Assert.Equal("C", mappings[1].FromHotKey);
		Assert.Contains(issues, i => i.Contains("mapping 2") && i.Contains("removed"));
	}

	// A blank mapping is exactly what the Hotkeys page adds when the user clicks Add.
	// Repairing or reporting it would nag about a row they are still typing into.
	[Fact]
	public void Repair_BlankButNotNullMapping_IsLeftAloneAndNotReported()
	{
		var mappings = new List<Map> { new(string.Empty, string.Empty) };

		var issues = HotKeyRouterMappingRepair.Repair(mappings);

		Assert.Empty(issues);
		Assert.Single(mappings);
	}

	[Fact]
	public void Repair_WellFormedMappings_ReportNothingAndAreUnchanged()
	{
		var mappings = new List<Map> { new("CONTROL+1", "CONTROL+2"), new("ALT+1", "ALT+2") };

		var issues = HotKeyRouterMappingRepair.Repair(mappings);

		Assert.Empty(issues);
		Assert.Equal(2, mappings.Count);
		Assert.Equal("CONTROL+1", mappings[0].FromHotKey);
		Assert.Equal("ALT+2", mappings[1].ToHotKey);
	}

	// The positions in the messages are the rows as the user will count them in the
	// file, so a later broken row must not be reported as row 1.
	[Fact]
	public void Repair_ReportsEachMappingByItsPositionInTheFile()
	{
		var mappings = new List<Map> { new("A", "B"), new("C", null), new(null, "F") };

		var issues = HotKeyRouterMappingRepair.Repair(mappings);

		Assert.Collection(issues,
			first => Assert.Contains("mapping 2", first),
			second => Assert.Contains("mapping 3", second));
	}

	[Fact]
	public void Repair_NullList_ReportsNothing() =>
		Assert.Empty(HotKeyRouterMappingRepair.Repair(null));

	[Fact]
	public void Repair_EmptyList_ReportsNothing() =>
		Assert.Empty(HotKeyRouterMappingRepair.Repair(new List<Map>()));

	// The list instance is held by the live settings graph, so repairs have to land
	// in it rather than in a replacement the caller never sees.
	[Fact]
	public void Repair_RemovesInPlace_KeepingTheSameListInstance()
	{
		var mappings = new List<Map> { null!, new("A", "B") };
		var settings = new HotKeyRouterSettings(mappings);

		HotKeyRouterMappingRepair.Repair(settings.Mappings);

		Assert.Same(mappings, settings.Mappings);
		Assert.Single(settings.Mappings);
	}

	[Fact]
	public void ParameterlessConstructor_LeavesBothHotkeysNull()
	{
		var map = new Map();

		Assert.Null(map.FromHotKey);
		Assert.Null(map.ToHotKey);
	}
}
