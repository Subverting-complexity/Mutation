using CognitiveSupport;
using Mutation.Ui.Core;
using Mutation.Ui.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Microsoft.UI;

// HotkeyBindingState moved to Mutation.Ui.Core so the rule that decides it —
// RouterBindingStatus — can live next to the app's other pure units instead of depending on a
// file that pulls in WinUI (issue #343).

namespace Mutation.Ui;

public sealed class HotkeyRouterEntry : INotifyPropertyChanged
{
        private static readonly Brush TransparentBrush = new SolidColorBrush(Colors.Transparent);
        private static readonly Brush InvalidBackgroundBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xB2, 0x1E, 0x1E));

        private string _fromHotkeyText = string.Empty;
        private string _toHotkeyText = string.Empty;
        private string? _formattedFrom;
        private string? _formattedTo;
        private bool _isFromValid;
        private bool _isToValid;
        private bool _isDuplicate;
        private string? _fromValidationMessage;
        private string? _toValidationMessage;
        private HotkeyBindingState _bindingState = HotkeyBindingState.Unknown;
        private string? _bindingError;
        private string? _combinedError;
        private string? _fromCommitAnnouncement;
        private string? _toCommitAnnouncement;
        private bool _announcementsMuted;

        /// <summary>
        /// What the two boxes are called to a screen reader, matching the
        /// <c>AutomationProperties.Name</c> each one carries in the row template. The
        /// announcement names the box it is about because by the time it lands the focus has
        /// already moved on, and a router row holds two boxes that tidy up the same way.
        /// </summary>
        private const string FromBoxLabel = "Shortcut to listen for";
        private const string ToBoxLabel = "Shortcut to send when triggered";

        private const string DuplicateFromMessage = "Duplicate 'From' hotkey.";

        private HotKeyRouterSettings.HotKeyRouterMap _map;

        internal HotKeyRouterSettings.HotKeyRouterMap Map => _map;

        public HotkeyRouterEntry(HotKeyRouterSettings.HotKeyRouterMap map)
        {
                _map = map ?? throw new ArgumentNullException(nameof(map));

                _fromHotkeyText = map.FromHotKey ?? string.Empty;
                _toHotkeyText = map.ToHotKey ?? string.Empty;

                // Silently: this is settings arriving from disk, not the user leaving a box.
                // A page that read seventeen rows' worth of tidying aloud on the way in would
                // be unusable.
                EvaluateFrom(commit: true, announce: false);
                EvaluateTo(commit: true, announce: false);
        }

        internal void ReplaceBackingMap(HotKeyRouterSettings.HotKeyRouterMap map)
        {
                _map = map ?? throw new ArgumentNullException(nameof(map));

                _fromHotkeyText = map.FromHotKey ?? string.Empty;
                _toHotkeyText = map.ToHotKey ?? string.Empty;

                // Silently: this is settings arriving from disk, not the user leaving a box.
                // A page that read seventeen rows' worth of tidying aloud on the way in would
                // be unusable.
                EvaluateFrom(commit: true, announce: false);
                EvaluateTo(commit: true, announce: false);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string FromHotkey
        {
                get => _fromHotkeyText;
                set
                {
                        if (SetField(ref _fromHotkeyText, value ?? string.Empty, nameof(FromHotkey)))
                        {
                                EvaluateFrom(commit: false, announce: false);
                                SetBindingResult(HotkeyBindingState.Unknown, null);
                        }
                }
        }

        public string ToHotkey
        {
                get => _toHotkeyText;
                set
                {
                        if (SetField(ref _toHotkeyText, value ?? string.Empty, nameof(ToHotkey)))
                        {
                                EvaluateTo(commit: false, announce: false);
                                SetBindingResult(HotkeyBindingState.Unknown, null);
                        }
                }
        }

        public bool IsFromValid => _isFromValid;

        public bool IsToValid => _isToValid;

        public bool IsDuplicate => _isDuplicate;

        public bool IsFromInputValid => _isFromValid && !_isDuplicate;

        public bool IsValid => IsFromInputValid && _isToValid;

        public string? NormalizedFromHotkey => string.IsNullOrWhiteSpace(_formattedFrom) ? null : _formattedFrom;

        public string? NormalizedToHotkey => string.IsNullOrWhiteSpace(_formattedTo) ? null : _formattedTo;

        public Brush FromBackgroundBrush => IsFromInputValid ? TransparentBrush : InvalidBackgroundBrush;

        public Brush ToBackgroundBrush => _isToValid ? TransparentBrush : InvalidBackgroundBrush;

        public HotkeyBindingState BindingState => _bindingState;

        /// <summary>
        /// Whether the running app is listening for this route, in words, or null when there is
        /// nothing worth saying \u2014 the row is half filled in, it has an error of its own that says
        /// more than this would, or nobody has told us what the app holds.
        /// <para>
        /// A written line rather than a tick beside the box. The tick was the whole of the old
        /// design and none of it worked: a glyph carries no text, and the tooltip that explained
        /// it announced nothing until you knew it was there and went looking. This sits in the
        /// row's reading order, so going down the page reads it out, and it is a live region as
        /// well so acting on the row is answered rather than leaving the user to press the
        /// shortcut and find out (issue #343).
        /// </para>
        /// <para>
        /// It names the shortcut, for the same reason the rewrite notices name their box: it can
        /// be announced out of a row the user is not in. Pressing <b>Delete</b> re-evaluates every
        /// remaining row, so deleting one of two rows that were flagged as duplicates of each
        /// other leaves the survivor announcing its new state while focus sits on a button in a
        /// row that no longer exists. "Not active yet" on its own would give the user no way to
        /// tell which mapping was meant, and a chord that three rows shared would say it three
        /// times.
        /// </para>
        /// </summary>
        public string? RouteStatusText
        {
                get
                {
                        // The chord is always present in these two states: the controller only asks
                        // about a row that is valid, and a valid row has a normalized "From".
                        string chord = NormalizedFromHotkey ?? string.Empty;

                        return _bindingState switch
                        {
                                HotkeyBindingState.Bound => $"{chord} is live.",
                                HotkeyBindingState.NotYetApplied =>
                                        $"{chord} is not active yet \u2014 press Save to apply.",
                                // Failed says why in the error region below, which is assertive and
                                // reaches the user without being hunted for. Two lines saying the
                                // same thing is one too many.
                                _ => null,
                        };
                }
        }

        /// <summary>
        /// While true, this row's error and status still appear on screen but are not read out.
        /// The page sets it for rows built straight from settings, which can arrive already
        /// carrying "Enter a hotkey." \u2014 one assertive interruption per stored bad row, about
        /// settings the user has not touched (issue #350).
        /// </summary>
        public bool AnnouncementsMuted => _announcementsMuted;

        /// <summary>
        /// Lets this row speak, or stops it. Called for every row at once, because the page is
        /// what has or has not been used \u2014 a row-by-row rule would silence a duplicate that
        /// appears in this list because the user edited a core hotkey higher up the page, which
        /// is exactly the news worth interrupting for.
        /// </summary>
        public void SetAnnouncementsMuted(bool muted)
        {
                if (_announcementsMuted == muted)
                        return;

                _announcementsMuted = muted;
                OnPropertyChanged(nameof(AnnouncementsMuted));
        }

        public bool HasBindingError => !string.IsNullOrEmpty(_combinedError);

        public Visibility BindingErrorVisibility => HasBindingError ? Visibility.Visible : Visibility.Collapsed;

        public string? BindingErrorMessage => _combinedError;

        /// <summary>
        /// What to tell a screen-reader user about the "From" box after it tidied itself up, or
        /// null when there is nothing worth interrupting for.
        /// <para>
        /// Bound to a live region in the row template through
        /// <see cref="Mutation.Ui.Views.LiveText"/>, because a row in a list has no code-behind
        /// to call <c>LiveMessage.Show</c> from (issue #332).
        /// </para>
        /// <para>
        /// One per box rather than one per row. The two boxes are tabbed through one after the
        /// other, so a shared notice would be taken down by entering the second box before the
        /// first box's notice had been read out at all.
        /// </para>
        /// </summary>
        public string? FromCommitAnnouncement => _fromCommitAnnouncement;

        /// <summary>The same, for the "To" box. See <see cref="FromCommitAnnouncement"/>.</summary>
        public string? ToCommitAnnouncement => _toCommitAnnouncement;

        public void CommitFromHotkey()
        {
                EvaluateFrom(commit: true, announce: true);
        }

        public void CommitToHotkey()
        {
                EvaluateTo(commit: true, announce: true);
        }

        /// <summary>
        /// Commits both boxes without saying anything about it. This is the bookkeeping commit
        /// the controller runs over every row whenever anything on the page changes — it is
        /// idempotent for the row the user actually edited, and for the rest of them it is not
        /// about anything the user just did.
        /// <para>
        /// It also has to stay silent to leave the notice from the real commit standing: the
        /// row the user edited is committed once by them and then again by this, and an
        /// announcing second pass sees nothing left to change and would take the first one's
        /// notice down before it had been spoken.
        /// </para>
        /// </summary>
        internal void CommitSilently()
        {
                EvaluateFrom(commit: true, announce: false);
                EvaluateTo(commit: true, announce: false);
        }

        /// <summary>
        /// Takes down the "From" box's last notice on the way back into it. It described
        /// something that happened when the user left, and left standing it becomes a line of
        /// ordinary-looking text under the row — read out as current content by anyone going
        /// down the page afterwards. Clearing it also means the same rewrite happening twice is
        /// announced twice, rather than the second one being swallowed as an unchanged message.
        /// </summary>
        public void ClearFromCommitAnnouncement() => SetFromCommitAnnouncement(null);

        /// <summary>The same, for the "To" box.</summary>
        public void ClearToCommitAnnouncement() => SetToCommitAnnouncement(null);

        public void SetDuplicate(bool isDuplicate)
        {
                if (_isDuplicate == isDuplicate)
                        return;

                _isDuplicate = isDuplicate;
                OnPropertyChanged(nameof(IsDuplicate));
                OnPropertyChanged(nameof(IsFromInputValid));
                OnPropertyChanged(nameof(IsValid));
                OnPropertyChanged(nameof(FromBackgroundBrush));
                // A row whose chord is already taken is not a route and cannot be one, so it has
                // nothing to say about being live. The duplicate message says what is wrong.
                if (isDuplicate)
                        SetBindingResult(HotkeyBindingState.Unknown, null);
                else
                        UpdateCombinedError();
        }

        public void SetBindingResult(HotkeyBindingState state, string? message)
        {
                if (_bindingState == state && string.Equals(_bindingError, message, StringComparison.Ordinal))
                {
                        UpdateCombinedError();
                        return;
                }

                _bindingState = state;
                _bindingError = message;
                OnPropertyChanged(nameof(BindingState));
                OnPropertyChanged(nameof(RouteStatusText));
                UpdateCombinedError();
        }

        /// <param name="announce">
        /// True only when the user has just left this box. False for the row being built or
        /// rebuilt from settings, and false for <see cref="CommitSilently"/>: those are not
        /// about anything the user just did, and announcing them would read every stored row
        /// aloud on the way into the page. It is also the only thing that clears a notice, so a
        /// silent pass leaves the last one standing rather than swallowing it.
        /// </param>
        private void EvaluateFrom(bool commit, bool announce)
        {
                string typedBeforeCommit = _fromHotkeyText;

                _formattedFrom = FormatHotkey(_fromHotkeyText);
                (_isFromValid, _fromValidationMessage) = ValidateFormattedHotkey(_formattedFrom, true);

                if (commit)
                {
                        ApplyFormattedValue(ref _fromHotkeyText, _formattedFrom, nameof(FromHotkey));

                        // Only update underlying map if valid; do NOT clear an existing persisted value here
                        // to avoid wiping settings when a parse hiccup occurs (e.g., at startup before full init).
                        if (_isFromValid)
                                _map.FromHotKey = _formattedFrom;
                }
                else if (!_isFromValid)
                {
                        // Blank, not null. Half-typed text is a routine state that gets
                        // auto-persisted, and a null on disk is what the load-time repair
                        // reports at the next launch — a notice about the user's own
                        // typing (issue #247).
                        _map.FromHotKey = string.Empty;
                }

                OnPropertyChanged(nameof(IsFromValid));
                OnPropertyChanged(nameof(IsFromInputValid));
                OnPropertyChanged(nameof(IsValid));
                OnPropertyChanged(nameof(FromBackgroundBrush));
                UpdateCombinedError();

                // Last, after UpdateCombinedError, for the same reason HotkeyEditor.Commit
                // announces last: the row's error is an assertive live region and this is a
                // polite one, and both are raised on the same dispatcher pass in the order they
                // were set, so a polite notice set first would be cut off by the assertive one
                // behind it.
                //
                // No reachable flow actually sets both in one call — the error settles while the
                // user types, a commit cannot change validity that the setter has not already
                // seen, and the one thing that can (a duplicate found through the controller)
                // suppresses the notice anyway. Ordered this way so the two conventions on this
                // page agree, not to fix an audible clash (issue #346).
                if (commit && announce)
                {
                        SetFromCommitAnnouncement(HotkeyCommitAnnouncement.For(
                                FromBoxLabel, typedBeforeCommit, _fromHotkeyText, FromErrorMessage));
                }
        }

        /// <param name="announce">See <see cref="EvaluateFrom"/>.</param>
        private void EvaluateTo(bool commit, bool announce)
        {
                string typedBeforeCommit = _toHotkeyText;

                _formattedTo = FormatHotkey(_toHotkeyText);
                (_isToValid, _toValidationMessage) = ValidateFormattedHotkey(_formattedTo, false);

                if (commit)
                {
                        ApplyFormattedValue(ref _toHotkeyText, _formattedTo, nameof(ToHotkey));

                        // Only update underlying map if valid; avoid clearing persisted value on transient invalid state.
                        if (_isToValid)
                                _map.ToHotKey = _formattedTo;
                }
                else if (!_isToValid)
                {
                        // Blank, not null — see EvaluateFrom.
                        _map.ToHotKey = string.Empty;
                }

                OnPropertyChanged(nameof(IsToValid));
                OnPropertyChanged(nameof(IsValid));
                OnPropertyChanged(nameof(ToBackgroundBrush));
                UpdateCombinedError();

                // Last, after UpdateCombinedError — see EvaluateFrom.
                if (commit && announce)
                {
                        SetToCommitAnnouncement(HotkeyCommitAnnouncement.For(
                                ToBoxLabel, typedBeforeCommit, _toHotkeyText, _toValidationMessage));
                }
        }

        /// <summary>
        /// What is wrong with the "From" box, or null when nothing is. The rewrite notice loses
        /// to this, the way it already loses to a validation message in the hotkey editor: being
        /// told the shortcut is unusable matters more than being told how it is now spelled.
        /// It is answered per box rather than from the row's combined error, so a "From" that
        /// tidied up is still announced while the "To" beside it is empty — which is every
        /// newly added row.
        /// </summary>
        private string? FromErrorMessage => _isDuplicate ? DuplicateFromMessage : _fromValidationMessage;

        private void SetFromCommitAnnouncement(string? message)
        {
                if (string.Equals(_fromCommitAnnouncement, message, StringComparison.Ordinal))
                        return;

                _fromCommitAnnouncement = message;
                OnPropertyChanged(nameof(FromCommitAnnouncement));
        }

        private void SetToCommitAnnouncement(string? message)
        {
                if (string.Equals(_toCommitAnnouncement, message, StringComparison.Ordinal))
                        return;

                _toCommitAnnouncement = message;
                OnPropertyChanged(nameof(ToCommitAnnouncement));
        }

        private void ApplyFormattedValue(ref string storage, string? formatted, string propertyName)
        {
                string newValue = formatted ?? string.Empty;
                if (!string.Equals(storage, newValue, StringComparison.Ordinal))
                {
                        storage = newValue;
                        OnPropertyChanged(propertyName);
                }
        }

        private (bool isValid, string? message) ValidateFormattedHotkey(string? formatted, bool isFrom)
        {
                if (string.IsNullOrWhiteSpace(formatted))
                        return (false, "Enter a hotkey.");

                try
                {
                        _ = Hotkey.Parse(formatted);
                        return (true, null);
                }
                catch (Exception ex)
                {
                        return (false, ex.Message);
                }
        }

        /// <summary>
        /// The text this row shows and persists. Routed through
        /// <see cref="Hotkey.Canonicalize"/> so a chord typed as <c>SHIFT+CTRL+A</c> is written
        /// the way the rest of the app spells it, rather than in the order it was typed
        /// (issue #323). Text that is still half-typed comes back upper-cased and otherwise
        /// untouched, which is what <see cref="ValidateFormattedHotkey"/> then complains about.
        /// </summary>
        private string? FormatHotkey(string? value) => Hotkey.Canonicalize(value);

        private void UpdateCombinedError()
        {
                string? message = null;
                if (!IsFromInputValid)
                        message = FromErrorMessage;
                else if (!_isToValid)
                        message = _toValidationMessage;
                else if (_bindingState == HotkeyBindingState.Failed)
                        message = _bindingError;

                if (!string.Equals(_combinedError, message, StringComparison.Ordinal))
                {
                        _combinedError = message;
                        OnPropertyChanged(nameof(BindingErrorMessage));
                        OnPropertyChanged(nameof(HasBindingError));
                        OnPropertyChanged(nameof(BindingErrorVisibility));
                }
        }

        private bool SetField<T>(ref T storage, T value, string propertyName)
        {
                if (EqualityComparer<T>.Default.Equals(storage, value))
                        return false;

                storage = value;
                OnPropertyChanged(propertyName);
                return true;
        }

        private void OnPropertyChanged(string propertyName) =>
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
