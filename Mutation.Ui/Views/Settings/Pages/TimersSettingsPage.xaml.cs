using System;
using System.Collections.Generic;
using CognitiveSupport;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Mutation.Ui.Views.SettingsUi.Pages;

/// <summary>
/// The Timers settings page (issue #456). Edits <see cref="Settings.IntervalTimers"/> on the
/// dialog's working copy, so nothing here takes effect until the user presses Save.
/// <para>
/// The cards are built in code rather than from a data template because every control's
/// accessible name carries the timer's name ("Stretch timer hours"), and has to follow it as
/// the user renames the timer. A screen reader user tabbing through three timers would
/// otherwise hear "Hours" three times with nothing to tell the timers apart.
/// </para>
/// </summary>
public sealed partial class TimersSettingsPage : UserControl
{
	private readonly Settings _settings;
	private readonly List<TimerCard> _cards = new();

	public TimersSettingsPage(Settings settings)
	{
		_settings = settings;
		_settings.IntervalTimers ??= new List<IntervalTimerSettings>();
		InitializeComponent();
		Rebuild();
	}

	private List<IntervalTimerSettings> Timers => _settings.IntervalTimers;

	private void Rebuild()
	{
		TimersHost.Children.Clear();
		_cards.Clear();

		for (int i = 0; i < Timers.Count; i++)
		{
			var card = new TimerCard(this, Timers[i], i, Timers.Count);
			_cards.Add(card);
			TimersHost.Children.Add(card.Root);
		}

		NoTimersText.Visibility = Timers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
	}

	private void AddTimerButton_Click(object sender, RoutedEventArgs e)
	{
		var timer = new IntervalTimerSettings
		{
			Name = IntervalTimerText.NextDefaultName(Timers.ConvertAll(t => t.Name)),
			Enabled = true,
		};
		Timers.Add(timer);
		Rebuild();
		_cards[^1].NameBox.Focus(FocusState.Keyboard);
	}

	private void Move(int index, int offset)
	{
		int target = index + offset;
		if (target < 0 || target >= Timers.Count)
			return;

		(Timers[index], Timers[target]) = (Timers[target], Timers[index]);
		Rebuild();

		// Keep focus on the same button of the timer that moved, so pressing Move down three
		// times moves one timer three places. At the end of the list that button is disabled,
		// and a disabled button cannot take focus, so focus goes to the other one.
		TimerCard moved = _cards[target];
		Button preferred = offset < 0 ? moved.MoveUpButton : moved.MoveDownButton;
		Button fallback = offset < 0 ? moved.MoveDownButton : moved.MoveUpButton;
		(preferred.IsEnabled ? preferred : fallback.IsEnabled ? fallback : (Control)moved.NameBox).Focus(FocusState.Keyboard);
	}

	private void Remove(int index)
	{
		if (index < 0 || index >= Timers.Count)
			return;

		Timers.RemoveAt(index);
		Rebuild();

		// Focus the Remove button of the timer that took this one's place, so focus never falls
		// back to the top of the dialog after a removal.
		if (_cards.Count == 0)
			AddTimerButton.Focus(FocusState.Keyboard);
		else
			_cards[Math.Min(index, _cards.Count - 1)].RemoveButton.Focus(FocusState.Keyboard);
	}

	private void PlaySound(string? rawPath)
	{
		if (string.IsNullOrWhiteSpace(rawPath))
		{
			ShowPreviewError("No sound file is chosen for this timer.");
			return;
		}

		if (BeepPlayer.PreviewFile(AudioFilePathResolver.Resolve(rawPath)))
			PreviewInfoBar.IsOpen = false;
		else
			ShowPreviewError($"Could not play this sound file: {rawPath}");
	}

	private void ShowPreviewError(string message)
	{
		PreviewInfoBar.Message = message;
		PreviewInfoBar.IsOpen = true;
	}

	private static async System.Threading.Tasks.Task<string?> PickWavAsync()
	{
		try
		{
			var picker = new FileOpenPicker();
			picker.FileTypeFilter.Add(".wav");

			var window = (Application.Current as App)?.MainAppWindow;
			if (window is null)
				return null;
			InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));
			var file = await picker.PickSingleFileAsync();
			return file?.Path;
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"Timer sound browse failed: {ex.Message}");
			return null;
		}
	}

	/// <summary>The controls for one timer, and the handlers that write them back to it.</summary>
	private sealed class TimerCard
	{
		private readonly TimersSettingsPage _page;
		private readonly IntervalTimerSettings _timer;
		private readonly int _index;
		private readonly TextBlock _heading;
		private readonly ToggleSwitch _toggle;
		private readonly NumberBox _hours;
		private readonly NumberBox _minutes;
		private readonly TextBox _soundBox;
		private readonly Button _playButton;
		private readonly Button _browseButton;
		private bool _suppress;

		public Border Root { get; }
		public TextBox NameBox { get; }
		public Button MoveUpButton { get; }
		public Button MoveDownButton { get; }
		public Button RemoveButton { get; }

		public TimerCard(TimersSettingsPage page, IntervalTimerSettings timer, int index, int count)
		{
			_page = page;
			_timer = timer;
			_index = index;

			_heading = new TextBlock { Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] };
			AutomationProperties.SetHeadingLevel(_heading, Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level3);

			NameBox = new TextBox { Header = "Name", Text = timer.Name ?? string.Empty, MinWidth = 220 };
			NameBox.TextChanged += (_, _) =>
			{
				if (_suppress) return;
				_timer.Name = NameBox.Text;
				ApplyNames();
			};
			// The name cannot be left empty: the main window and the screen reader both need one.
			NameBox.LostFocus += (_, _) =>
			{
				if (!string.IsNullOrWhiteSpace(NameBox.Text))
					return;
				var others = new List<string?>();
				foreach (var t in _page.Timers)
					if (!ReferenceEquals(t, _timer)) others.Add(t.Name);
				string name = IntervalTimerText.NextDefaultName(others);
				// Written to the timer here as well: TextChanged is raised later, and a Save that
				// commits first would otherwise keep the empty name.
				_timer.Name = name;
				NameBox.Text = name;
			};

			_toggle = new ToggleSwitch { Header = "On or off", OnContent = "On", OffContent = "Off", IsOn = timer.Enabled };
			_toggle.Toggled += (_, _) => { if (!_suppress) _timer.Enabled = _toggle.IsOn; };

			int interval = IntervalTimerSettings.ClampInterval(timer.IntervalMinutes);
			_hours = NewNumberBox("Hours", interval / 60, 0, IntervalTimerSettings.MaxIntervalMinutes / 60);
			_minutes = NewNumberBox("Minutes", interval % 60, 0, 59);
			_hours.ValueChanged += (_, _) => CommitInterval();
			_minutes.ValueChanged += (_, _) => CommitInterval();

			_soundBox = new TextBox { Header = "Sound file", Text = timer.SoundFile ?? string.Empty };
			_soundBox.TextChanged += (_, _) => { if (!_suppress) _timer.SoundFile = _soundBox.Text; };

			_playButton = new Button { Content = "Play", VerticalAlignment = VerticalAlignment.Bottom };
			_playButton.Click += (_, _) => _page.PlaySound(_soundBox.Text);
			_browseButton = new Button { Content = "Browse...", VerticalAlignment = VerticalAlignment.Bottom };
			_browseButton.Click += async (_, _) =>
			{
				string? path = await PickWavAsync();
				if (path is not null)
					_soundBox.Text = path;
			};

			MoveUpButton = new Button { Content = "Move up", IsEnabled = index > 0 };
			MoveUpButton.Click += (_, _) => _page.Move(_index, -1);
			MoveDownButton = new Button { Content = "Move down", IsEnabled = index < count - 1 };
			MoveDownButton.Click += (_, _) => _page.Move(_index, +1);
			RemoveButton = new Button { Content = "Remove" };
			RemoveButton.Click += (_, _) => _page.Remove(_index);

			var topRow = Row(NameBox, _toggle);
			var intervalRow = Row(_hours, _minutes);

			var soundRow = new Grid { ColumnSpacing = 8 };
			soundRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
			soundRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
			soundRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
			soundRow.Children.Add(_soundBox);
			Grid.SetColumn(_playButton, 1);
			soundRow.Children.Add(_playButton);
			Grid.SetColumn(_browseButton, 2);
			soundRow.Children.Add(_browseButton);

			var actionsRow = Row(MoveUpButton, MoveDownButton, RemoveButton);

			var body = new StackPanel { Spacing = 10 };
			body.Children.Add(_heading);
			body.Children.Add(topRow);
			body.Children.Add(intervalRow);
			body.Children.Add(soundRow);
			body.Children.Add(actionsRow);

			Root = new Border
			{
				Child = body,
				Padding = new Thickness(16),
				CornerRadius = new CornerRadius(8),
				BorderThickness = new Thickness(1),
				BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
				Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"],
			};

			ApplyNames();
		}

		// Whole numbers only: a typed 1.5 is rounded in the box, so it never shows a value that
		// is not what will be saved.
		private static NumberBox NewNumberBox(string header, int value, int min, int max) => new()
		{
			Header = header,
			Value = value,
			Minimum = min,
			Maximum = max,
			SmallChange = 1,
			LargeChange = 10,
			SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
			MinWidth = 140,
			NumberFormatter = new Windows.Globalization.NumberFormatting.DecimalFormatter
			{
				FractionDigits = 0,
				IntegerDigits = 1,
				NumberRounder = new Windows.Globalization.NumberFormatting.IncrementNumberRounder { Increment = 1 },
			},
		};

		private static StackPanel Row(params UIElement[] children)
		{
			var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
			foreach (var child in children)
				row.Children.Add(child);
			return row;
		}

		// Interval rules: 1 minute to 24 hours. A cleared box counts as 0. The boxes are
		// corrected in place when the total falls outside that range, so what the user sees is
		// always what will be saved.
		private void CommitInterval()
		{
			if (_suppress) return;

			int hours = double.IsNaN(_hours.Value) ? 0 : (int)Math.Round(_hours.Value);
			int minutes = double.IsNaN(_minutes.Value) ? 0 : (int)Math.Round(_minutes.Value);
			int total = IntervalTimerSettings.ClampInterval(hours * 60 + minutes);
			_timer.IntervalMinutes = total;

			// Also catches a fraction or a cleared box: both make the box differ from what is saved.
			if (_hours.Value != total / 60 || _minutes.Value != total % 60)
			{
				_suppress = true;
				try
				{
					_hours.Value = total / 60;
					_minutes.Value = total % 60;
				}
				finally { _suppress = false; }
			}
		}

		private void ApplyNames()
		{
			string label = IntervalTimerText.AccessibleName(_timer.Name);
			_heading.Text = label;
			AutomationProperties.SetName(NameBox, $"{label} name");
			AutomationProperties.SetName(_toggle, label);
			AutomationProperties.SetHelpText(_toggle, "Turns this timer on or off.");
			AutomationProperties.SetName(_hours, $"{label} hours");
			AutomationProperties.SetName(_minutes, $"{label} minutes");
			AutomationProperties.SetHelpText(_hours, "How often the timer plays, from 1 minute to 24 hours, together with the minutes.");
			AutomationProperties.SetHelpText(_minutes, "How often the timer plays, from 1 minute to 24 hours, together with the hours.");
			AutomationProperties.SetName(_soundBox, $"{label} sound file");
			AutomationProperties.SetHelpText(_soundBox, "The .wav file the timer plays when it is due.");
			AutomationProperties.SetName(_playButton, $"Play {label} sound");
			AutomationProperties.SetName(_browseButton, $"Browse for {label} sound file");
			AutomationProperties.SetName(MoveUpButton, $"Move {label} up");
			AutomationProperties.SetName(MoveDownButton, $"Move {label} down");
			AutomationProperties.SetName(RemoveButton, $"Remove {label}");
		}
	}
}
