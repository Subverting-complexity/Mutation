using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using CognitiveSupport;
using Microsoft.UI.Xaml;

namespace Mutation.Ui;

/// <summary>
/// One row of the Timers strip on the main window (issue #456): a toggle, the timer's name and
/// its countdown.
/// <para>
/// The countdown changes every second near the end, and the user's screen reader must not
/// speak it as it changes. The visible text is a plain text block, not a live region, so
/// changing it raises nothing a screen reader announces. The toggle's help text carries the
/// countdown too, so tabbing onto the toggle reads it once — but help text that changed while
/// the toggle had focus could be read out again, so it is frozen for as long as the toggle is
/// focused and catches up when focus leaves.
/// </para>
/// </summary>
public sealed class IntervalTimerRow : INotifyPropertyChanged
{
	private readonly Action<IntervalTimerRow, bool> _onToggled;
	private bool _isOn;
	private bool _hasFocus;
	private string _countdownText = IntervalTimerText.Off;
	private string? _warningText;
	private string _focusHelpText = IntervalTimerText.Off;

	public IntervalTimerRow(IntervalTimerSettings timer, Action<IntervalTimerRow, bool> onToggled)
	{
		Timer = timer ?? throw new ArgumentNullException(nameof(timer));
		_onToggled = onToggled ?? throw new ArgumentNullException(nameof(onToggled));
		_isOn = timer.Enabled;
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	public IntervalTimerSettings Timer { get; }

	public string Name => Timer.Name;

	public string AccessibleName => IntervalTimerText.AccessibleName(Timer.Name);

	public bool IsOn
	{
		get => _isOn;
		set
		{
			if (_isOn == value)
				return;
			_isOn = value;
			Raise();
			_onToggled(this, value);
		}
	}

	public string CountdownText => _countdownText;

	public string FocusHelpText => _focusHelpText;

	public string WarningText => _warningText ?? string.Empty;

	public Visibility WarningVisibility => _warningText is null ? Visibility.Collapsed : Visibility.Visible;

	/// <summary>Called on every tick with the current countdown and warning (null for none).</summary>
	public void Update(string countdown, string? warning)
	{
		if (_countdownText != countdown)
		{
			_countdownText = countdown;
			Raise(nameof(CountdownText));
		}

		if (_warningText != warning)
		{
			_warningText = warning;
			Raise(nameof(WarningText));
			Raise(nameof(WarningVisibility));
		}

		if (!_hasFocus)
			RefreshHelpText();
	}

	/// <summary>
	/// Brings the help text up to date even while the toggle has focus. Used right after the
	/// user flips the toggle, when a stale "off" would be wrong and a fresh reading is wanted.
	/// </summary>
	public void RefreshHelpText()
	{
		string help = _warningText is null ? _countdownText : $"{_countdownText}. {_warningText}";
		if (_focusHelpText == help)
			return;
		_focusHelpText = help;
		Raise(nameof(FocusHelpText));
	}

	public void NoteFocused() => _hasFocus = true;

	public void NoteUnfocused()
	{
		_hasFocus = false;
		RefreshHelpText();
	}

	private void Raise([CallerMemberName] string? name = null) =>
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
