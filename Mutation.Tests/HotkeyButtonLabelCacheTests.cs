using Mutation.Ui.Core;

namespace Mutation.Tests;

// Issue #214: the defect was the label cache, not the string composition. The old cache
// stored the name it first read back off the button and re-derived from that value on
// every later call, so a hotkey-only refresh resurrected the startup name and undid the
// state transition. Composition was always correct, so these tests drive the cache.
public class HotkeyButtonLabelCacheTests
{
	// Stands in for a Button; the cache keys on reference identity.
	private sealed class FakeButton
	{
		public FakeButton(string nameFromMarkup) => NameFromMarkup = nameFromMarkup;
		public string NameFromMarkup { get; }
	}

	private static string Refresh(HotkeyButtonLabelCache cache, FakeButton button, string? hotkey, string tooltip, string? stateLabel = null)
	{
		string label = cache.Resolve(button, button.NameFromMarkup, tooltip, stateLabel);
		return HotkeyAccessibleText.ComposeName(label, hotkey);
	}

	[Fact]
	public void AHotkeyOnlyRefreshAfterAStateTransition_KeepsTheNewState()
	{
		// The exact failure from the issue: startup seeds "Record", recording sets
		// "Stop", and then a hotkey refresh must not put "Record" back.
		var cache = new HotkeyButtonLabelCache();
		var button = new FakeButton("Record");

		Assert.Equal("Record, SHIFT+ALT+U", Refresh(cache, button, "SHIFT+ALT+U", "Start or stop speech capture"));

		Assert.Equal("Stop, SHIFT+ALT+U", Refresh(cache, button, "SHIFT+ALT+U", "Stop", stateLabel: "Stop"));

		// Settings saved mid-recording: ApplyLiveSettings re-runs the hotkey refresh.
		Assert.Equal("Stop, SHIFT+ALT+U", Refresh(cache, button, "SHIFT+ALT+U", "Start or stop speech capture"));
	}

	[Fact]
	public void TheMicrophoneToggle_AnnouncesTheStateItIsActuallyIn()
	{
		var cache = new HotkeyButtonLabelCache();
		var button = new FakeButton("Mute microphone");

		Refresh(cache, button, "ALT+Q", "Toggle microphone mute state");
		Refresh(cache, button, "ALT+Q", "Unmute microphone", stateLabel: "Unmute microphone");

		Assert.Equal("Unmute microphone, ALT+Q", Refresh(cache, button, "ALT+Q", "Toggle microphone mute state"));
	}

	[Fact]
	public void AStateLabelRecordedWithoutAHotkeyRefresh_SurvivesTheNextRefresh()
	{
		// The disabled "Transcribing…" state sets a name without touching the hotkey.
		var cache = new HotkeyButtonLabelCache();
		var button = new FakeButton("Record");
		Refresh(cache, button, "SHIFT+ALT+U", "Start or stop speech capture");

		cache.Set(button, "Transcribing…");

		Assert.Equal("Transcribing…, SHIFT+ALT+U", Refresh(cache, button, "SHIFT+ALT+U", "Start or stop speech capture"));
	}

	[Fact]
	public void AChangedHotkeyIsPickedUpWithoutDisturbingTheLabel()
	{
		var cache = new HotkeyButtonLabelCache();
		var button = new FakeButton("Record and Format");
		Refresh(cache, button, "SHIFT+ALT+I", "Record and Format", stateLabel: "Stop and Format");

		Assert.Equal("Stop and Format, CTRL+ALT+J", Refresh(cache, button, "CTRL+ALT+J", "Record and Format"));
	}

	[Fact]
	public void TheMarkupNameOnlyEverSeedsTheCache()
	{
		var cache = new HotkeyButtonLabelCache();
		var button = new FakeButton("Record");
		Refresh(cache, button, null, "Start or stop speech capture", stateLabel: "Stop");

		// Even asked again with the markup name available, the state label wins.
		Assert.Equal("Stop", cache.Resolve(button, button.NameFromMarkup, "Start or stop speech capture", null));
	}

	[Fact]
	public void AButtonWithNoMarkupName_FallsBackToTheTooltip()
	{
		var cache = new HotkeyButtonLabelCache();
		var button = new FakeButton("");

		Assert.Equal("Send transcript through the configured language model",
			cache.Resolve(button, button.NameFromMarkup, "Send transcript through the configured language model", null));
	}

	// Issue #309: a busy state cached the name it had just set, and that name already had
	// the shortcut composed into it. The next hotkey-only refresh read it back and composed
	// a second one — "Stop LLM processing, SHIFT+ALT+U, SHIFT+ALT+U" — which is the stale-
	// name class of defect #214 established the cache to prevent.
	//
	// This states the contract; it cannot enforce it. The caller lives in MainWindow and
	// needs a XAML tree, so nothing here would catch a call site that started pre-composing
	// again. What prevents that is structural: MainWindow.SetButtonAccessibleLabel is now
	// the only place a hotkey is composed into a name, and it takes the label and the
	// hotkey separately. If a second composition site ever appears, this test is the note
	// explaining why it must not.
	[Fact]
	public void AStateLabelIsStoredBare_SoARefreshComposesTheHotkeyExactlyOnce()
	{
		var cache = new HotkeyButtonLabelCache();
		var button = new FakeButton("Record");
		const string Hotkey = "SHIFT+ALT+U";

		// What a busy state pushes in. It must be the bare label, never the composed name.
		cache.Set(button, "Stop LLM processing");

		// What a later hotkey-only refresh reads back, and then composes from.
		string resolved = cache.Resolve(button, button.NameFromMarkup, "Start or stop speech capture", null);
		string composed = HotkeyAccessibleText.ComposeName(resolved, Hotkey);

		Assert.Equal("Stop LLM processing", resolved);
		Assert.Equal("Stop LLM processing, SHIFT+ALT+U", composed);
		Assert.DoesNotContain("SHIFT+ALT+U, SHIFT+ALT+U", composed);
	}

	[Fact]
	public void ButtonsDoNotShareLabels()
	{
		var cache = new HotkeyButtonLabelCache();
		var record = new FakeButton("Record");
		var recordAndFormat = new FakeButton("Record and Format");

		cache.Resolve(record, record.NameFromMarkup, "x", "Stop");

		Assert.Equal("Record and Format", cache.Resolve(recordAndFormat, recordAndFormat.NameFromMarkup, "x", null));
	}
}
