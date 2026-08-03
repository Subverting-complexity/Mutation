using System.Collections.Generic;

namespace Mutation.Ui.Core;

/// <summary>
/// Remembers the accessible label each hotkey button currently carries, without the
/// hotkey suffix.
///
/// This is the part that was broken in issue #214. The old cache stored the first name
/// it ever read back off the button and re-derived from that value forever, so a
/// hotkey-only refresh resurrected the button's startup name and undid whatever the
/// last state transition had set — the mic button announced "Mute microphone" while
/// muted, and the Stop button announced "Record". State transitions now push their
/// label in here, and refreshes read the current one back.
///
/// Keyed on <see cref="object"/> by reference so it is exercisable without a XAML tree.
/// </summary>
internal sealed class HotkeyButtonLabelCache
{
	private readonly Dictionary<object, string> _labels = new(ReferenceEqualityComparer.Instance);

	/// <param name="stateLabel">
	/// The label for the button's current state, from a caller that knows the state.
	/// Null or blank means "this is a hotkey-only refresh": keep the label the button
	/// already has.
	/// </param>
	/// <param name="nameFromMarkup">The button's declared name, used only to seed the cache.</param>
	/// <param name="fallback">Used when the markup carries no name either.</param>
	public string Resolve(object button, string? nameFromMarkup, string fallback, string? stateLabel)
	{
		if (!string.IsNullOrWhiteSpace(stateLabel))
		{
			_labels[button] = stateLabel;
			return stateLabel;
		}

		if (_labels.TryGetValue(button, out var known))
			return known;

		// First sight of this button: its declared name is still the bare label.
		string seed = string.IsNullOrWhiteSpace(nameFromMarkup) ? fallback : nameFromMarkup;
		_labels[button] = seed;
		return seed;
	}

	/// <summary>
	/// Records a state label for a button whose hotkey affordances are not being
	/// touched, so a later refresh does not undo it.
	/// </summary>
	public void Set(object button, string label) => _labels[button] = label;
}
