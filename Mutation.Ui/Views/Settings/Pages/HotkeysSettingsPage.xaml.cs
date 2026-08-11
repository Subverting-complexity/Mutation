using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CognitiveSupport;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Mutation.Ui.Core;
using Mutation.Ui.Services;
using Mutation.Ui.Views.SettingsUi.Controls;

namespace Mutation.Ui.Views.SettingsUi.Pages;

public sealed partial class HotkeysSettingsPage : UserControl
{
	private const string DuplicateBadgeText = "Duplicate hotkey";

	private readonly Settings _settings;
	private readonly List<HotkeyRow> _rows = new();
	private readonly HotkeyRouterController _routerController;

	/// <summary>
	/// Whether the user has used this page yet. Every error on it is written the moment it
	/// exists and read out only after this has been touched, so opening the page on a settings
	/// file full of empty required rows is quiet instead of a queue of interruptions about rows
	/// nobody has been near (issue #350).
	/// <para>
	/// One gate for the page rather than one per row, and shared with the router list below, so
	/// the two halves of this screen have the same manners. It also means a clash that appears
	/// in one list because the user edited the other is still announced — the row it lands on
	/// was never touched, and it is precisely the news worth interrupting for.
	/// </para>
	/// </summary>
	private readonly FirstTouchGate _announcementGate = new();

	public ObservableCollection<Mutation.Ui.HotkeyRouterEntry> HotkeyRouterEntries { get; } = new();

	public HotkeysSettingsPage(Settings settings)
		: this(settings, null)
	{
	}

	/// <param name="liveRouterRoutes">
	/// The router routes the running app currently holds, so each row can say whether it is live
	/// or still waiting for a save. Read-only — see <see cref="HotkeyRouterController"/>. Null
	/// when there is nothing to ask, and then the rows say nothing about it (issue #343).
	/// </param>
	public HotkeysSettingsPage(
		Settings settings,
		Func<IReadOnlyList<RegisteredRouterRoute>?>? liveRouterRoutes)
	{
		_settings = settings;
		InitializeComponent();
		BuildRows();

		_routerController = new HotkeyRouterController(
			HotkeyRouterEntries,
			_settings,
			settingsManager: null,
			DispatcherQueue.GetForCurrentThread(),
			HotkeyRouterList,
			autoPersist: false,
			// This page holds both lists, so it is the only place that can see a core hotkey
			// and a route claiming the same chord (issue #321).
			crossListDuplicateCheck: RecomputeDuplicates,
			liveRoutes: liveRouterRoutes,
			announcementGate: _announcementGate);

		// Also runs the first duplicate check, once the router rows exist to take part in it.
		_routerController.Initialize();
	}

	private sealed class HotkeyRow
	{
		public required HotkeySpec Spec { get; init; }
		public required HotkeyEditor Editor { get; init; }
		public required Border Container { get; init; }
		public required TextBlock DuplicateBadge { get; init; }
	}


	private void BuildRows()
	{
		HotkeyList.Children.Clear();
		_rows.Clear();

		foreach (var spec in CoreHotkeys.All)
		{
			var editor = new HotkeyEditor
			{
				Header = spec.Label,
				AllowEmpty = spec.AllowEmpty,
				AllowSendKeysSyntax = spec.AllowsSendKeysSyntax,
				// Before Hotkey is assigned, because assigning it validates straight away and a
				// required row that is empty would announce "Enter a hotkey." as the page is
				// still being built (issue #350).
				AnnouncementGate = _announcementGate,
				Hotkey = spec.Getter(_settings) ?? string.Empty,
			};
			var dupBadge = new TextBlock
			{
				Text = string.Empty,
				Foreground = new SolidColorBrush(Microsoft.UI.Colors.OrangeRed),
				Visibility = Visibility.Collapsed,
				Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
			};
			// Announced the moment it appears — LiveMessage does the raising, this only marks
			// the text as worth announcing. A warning only a sighted user notices is no
			// warning here.
			AutomationProperties.SetLiveSetting(dupBadge, AutomationLiveSetting.Assertive);

			Grid editorRow = new() { ColumnSpacing = 8 };
			editorRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
			editorRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

			Grid.SetColumn(editor, 0);
			editorRow.Children.Add(editor);

			if (spec.Default is not null)
			{
				var resetBtn = SettingsResetButton.Create($"Reset to default ({spec.Default})", () =>
				{
					_announcementGate.Touch();
					editor.Hotkey = spec.Default!;
					spec.Setter(_settings, spec.Default);
					RecomputeDuplicates();
				});
				resetBtn.VerticalAlignment = VerticalAlignment.Bottom;
				resetBtn.Margin = new Thickness(0, 0, 0, 4);
				Grid.SetColumn(resetBtn, 1);
				editorRow.Children.Add(resetBtn);
			}

			var stack = new StackPanel { Spacing = 2 };
			stack.Children.Add(editorRow);
			stack.Children.Add(dupBadge);
			var border = new Border
			{
				Padding = new Thickness(8),
				CornerRadius = new CornerRadius(8),
				Child = stack,
			};

			var row = new HotkeyRow { Spec = spec, Editor = editor, Container = border, DuplicateBadge = dupBadge };
			editor.HotkeyCommitted += (_, value) =>
			{
				spec.Setter(_settings, string.IsNullOrWhiteSpace(value) ? null : value);
				RecomputeDuplicates();
			};

			HotkeyList.Children.Add(border);
			_rows.Add(row);
		}
	}

	/// <summary>
	/// One duplicate check for every shortcut this settings session can reach, not one per
	/// list. <see cref="HotkeyRegistrationTable"/> is a single table for the whole app, so a
	/// chord any list holds is a chord no other list can also have; checking each list only
	/// against itself let a core hotkey and a route both claim <c>CTRL+ALT+G</c> with no badge
	/// on either row, and the user found out from the failure dialog after saving (issue #321).
	/// </summary>
	private void RecomputeDuplicates()
	{
		var coreRows = new List<HotkeyConflictFinder.ConfiguredHotkey>(_rows.Count);
		foreach (var row in _rows)
			coreRows.Add(new(row.Spec.Getter(_settings), row.Spec.Registers));

		var routerRows = _routerController.ConfiguredFromHotkeys();

		var prompts = NamedPromptHotkeys();

		// Two passes rather than one, so a row can say which kind of clash it has. The prompt
		// shortcuts take part in the check but have no rows on this page to flag, and a badge
		// reading "Duplicate hotkey" beside seventeen unflagged rows would be a hunt with no
		// quarry.
		var onThisPage = HotkeyConflictFinder.DuplicateIndexesAcross([coreRows, routerRows]);
		var everywhere = HotkeyConflictFinder.DuplicateIndexesAcross(
			[coreRows, routerRows, ConfiguredHotkeysOf(prompts)]);

		for (int i = 0; i < _rows.Count; i++)
		{
			// Shown always, announced once the page has been used. The first check of all runs
			// while the page is still being built, and a settings file with two rows already
			// sharing a chord would otherwise read the badge out before the user saw the page
			// (issue #350).
			LiveMessage.Show(
				_rows[i].DuplicateBadge,
				onThisPage[0].Contains(i) ? DuplicateBadgeText
					: everywhere[0].Contains(i) ? PromptClashBadge(_rows[i], prompts)
					: null,
				AnnouncementAllowed);
		}

		// A route says "Duplicate 'From' hotkey." either way. The wording is true of a prompt
		// clash too, and the row has one message to spend.
		_routerController.ApplyDuplicates(everywhere[1]);
	}

	/// <summary>
	/// For a row whose only clash is with a prompt's own shortcut. The prompt is edited in its
	/// own window rather than in a list on this page, so there is no second row here to look at
	/// — the badge names the prompt instead, which is the difference between a warning the user
	/// can act on and one that only says something is wrong somewhere (issue #336).
	/// </summary>
	/// <remarks>
	/// Asked with the same two things the cross-list check was given — the saved value, not
	/// what is in the box, and the row's own <see cref="HotkeySpec.Registers"/> flag — so the
	/// two cannot disagree about what this row holds. The flag matters on the two "Send key
	/// after…" rows: their value may be a comma-separated sequence or SendKeys shorthand, and
	/// asking about it as though it were a single claimed chord parses <c>^{F5}</c> as nothing
	/// and comes back with no name to show.
	/// </remarks>
	private string PromptClashBadge(HotkeyRow row, IReadOnlyList<NamedHotkey> prompts)
	{
		var names = HotkeyClashDescription.NamesHolding(
			row.Spec.Getter(_settings), prompts, row.Spec.Registers);

		// A guard rather than an expected branch: the caller only reaches here because the
		// cross-list check found a prompt clash on this row, and this asks the same question of
		// the same values. A badge that named nothing would still be better than a blank one.
		return names.Count == 0
			? "Also used by an LLM prompt"
			: $"Also used by {string.Join(", ", names)}";
	}

	/// <summary>
	/// The shortcut on each LLM prompt, with the prompt's name. They are registered from the
	/// same table as everything on this page, so they clash with it, but they are edited in the
	/// prompt window rather than here — they take part in the check without being flagged by it.
	/// </summary>
	private IReadOnlyList<NamedHotkey> NamedPromptHotkeys()
	{
		var prompts = _settings.LlmSettings?.Prompts;
		if (prompts is null)
			return Array.Empty<NamedHotkey>();

		var named = new List<NamedHotkey>(prompts.Count);
		foreach (var prompt in prompts)
			named.Add(new(ClaimedHotkeys.NameOf(prompt), prompt.Hotkey, ClaimsTheChord: true));

		return named;
	}

	private static IReadOnlyList<HotkeyConflictFinder.ConfiguredHotkey> ConfiguredHotkeysOf(
		IReadOnlyList<NamedHotkey> named)
	{
		var configured = new List<HotkeyConflictFinder.ConfiguredHotkey>(named.Count);
		foreach (var entry in named)
			configured.Add(entry.Configured);

		return configured;
	}

	/// <summary>
	/// Whether this page may read its errors out yet. Asked at the moment a message would be
	/// raised rather than when it is set, so a check that runs while the page is being built is
	/// silent while the very next one, after the user has typed, is not.
	/// </summary>
	private bool AnnouncementAllowed() => _announcementGate.HasBeenTouched;

	private void BtnAddHotkeyRoute_Click(object sender, RoutedEventArgs e) =>
		_routerController.AddNewMapping();

	private void HotkeyRouterDelete_Click(object sender, RoutedEventArgs e) =>
		_routerController.DeleteMapping(sender);

	private void HotkeyRouterFrom_GotFocus(object sender, RoutedEventArgs e) =>
		_routerController.ClearFromCommitAnnouncement(sender);

	private void HotkeyRouterTo_GotFocus(object sender, RoutedEventArgs e) =>
		_routerController.ClearToCommitAnnouncement(sender);

	private void HotkeyRouterFrom_LostFocus(object sender, RoutedEventArgs e) =>
		_routerController.CommitFromLostFocus(sender);

	private void HotkeyRouterTo_LostFocus(object sender, RoutedEventArgs e) =>
		_routerController.CommitToLostFocus(sender);
}
