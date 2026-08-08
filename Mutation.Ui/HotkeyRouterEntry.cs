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

namespace Mutation.Ui;

public enum HotkeyBindingState
{
        Inactive,
        Bound,
        Failed
}

public sealed class HotkeyRouterEntry : INotifyPropertyChanged
{
        private static readonly Brush TransparentBrush = new SolidColorBrush(Colors.Transparent);
        private static readonly Brush InvalidBackgroundBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xB2, 0x1E, 0x1E));
        private static readonly Brush NeutralStatusBrush = ResolveThemeBrush("SystemFillColorNeutralBrush") ?? new SolidColorBrush(Color.FromArgb(0xFF, 0x6B, 0x6B, 0x6B));
        private static readonly Brush SuccessStatusBrush = ResolveThemeBrush("SystemFillColorSuccessBrush") ?? new SolidColorBrush(Color.FromArgb(0xFF, 0x0B, 0x8A, 0x00));
        private static readonly Brush FailureStatusBrush = ResolveThemeBrush("SystemFillColorCriticalBrush") ?? new SolidColorBrush(Color.FromArgb(0xFF, 0xD1, 0x42, 0x42));

        /// <summary>
        /// What each box is called when a rewrite is read out. These are the names the
        /// screen reader already gives the two text boxes (<c>AutomationProperties.Name</c>
        /// in the row template), so the notice names the field the same way the reader just
        /// did. The visible column headers, "From" and "To", read as the start of a sentence
        /// instead — "From now reads CTRL+SHIFT+A" is heard as "from now on".
        /// </summary>
        private const string FromBoxName = "Shortcut to listen for";
        private const string ToBoxName = "Shortcut to send when triggered";

        private string _fromHotkeyText = string.Empty;
        private string _toHotkeyText = string.Empty;
        private string? _fromCommitAnnouncement;
        private string? _toCommitAnnouncement;
        private string? _formattedFrom;
        private string? _formattedTo;
        private bool _isFromValid;
        private bool _isToValid;
        private bool _isDuplicate;
        private string? _fromValidationMessage;
        private string? _toValidationMessage;
        private HotkeyBindingState _bindingState = HotkeyBindingState.Inactive;
        private string? _bindingError;
        private string? _combinedError;

        private HotKeyRouterSettings.HotKeyRouterMap _map;

        internal HotKeyRouterSettings.HotKeyRouterMap Map => _map;

        public HotkeyRouterEntry(HotKeyRouterSettings.HotKeyRouterMap map)
        {
                _map = map ?? throw new ArgumentNullException(nameof(map));

                _fromHotkeyText = map.FromHotKey ?? string.Empty;
                _toHotkeyText = map.ToHotKey ?? string.Empty;

                // Silent: a row being built from what is already on disk has not been edited
                // by anyone, and a settings file written before the app canonicalised these
                // values would otherwise read every tidied row aloud at startup.
                EvaluateFrom(commit: true, announce: false);
                EvaluateTo(commit: true, announce: false);
        }

        internal void ReplaceBackingMap(HotKeyRouterSettings.HotKeyRouterMap map)
        {
                _map = map ?? throw new ArgumentNullException(nameof(map));

                _fromHotkeyText = map.FromHotKey ?? string.Empty;
                _toHotkeyText = map.ToHotKey ?? string.Empty;

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
                                SetBindingResult(HotkeyBindingState.Inactive, null);
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
                                SetBindingResult(HotkeyBindingState.Inactive, null);
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

        public string BindingStatusGlyph => _bindingState switch
        {
                HotkeyBindingState.Bound => "\uE73E", // CheckMark
                HotkeyBindingState.Failed => "\uEA39", // StatusErrorFull
                _ => "\uF142" // Record
        };

        public Brush BindingStatusBrush => _bindingState switch
        {
                HotkeyBindingState.Bound => SuccessStatusBrush,
                HotkeyBindingState.Failed => FailureStatusBrush,
                _ => NeutralStatusBrush
        };

        public string BindingStatusTooltip
        {
                get
                {
                        if (_bindingState == HotkeyBindingState.Bound)
                                return "Hotkey is bound.";
                        if (HasBindingError && !string.IsNullOrEmpty(_combinedError))
                                return _combinedError!;
                        if (_bindingState == HotkeyBindingState.Failed)
                                return "Hotkey binding failed.";
                        return "Hotkey is not currently bound.";
                }
        }

        public bool HasBindingError => !string.IsNullOrEmpty(_combinedError);

        public Visibility BindingErrorVisibility => HasBindingError ? Visibility.Visible : Visibility.Collapsed;

        public string? BindingErrorMessage => _combinedError;

        /// <summary>
        /// What to read out after the "From" box tidied up what was typed into it, or null
        /// when there is nothing worth interrupting for. Bound to a live region in the row
        /// template, which is what turns it into something a screen reader says (issue #332).
        /// </summary>
        public string? FromCommitAnnouncement => _fromCommitAnnouncement;

        /// <summary>The same, for the "To" box.</summary>
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
        /// Commits without saying anything. Saving the page commits every row on the way out,
        /// and the rows the user never touched have nothing to report — while the one they
        /// were still in has already reported it on the way past.
        /// </summary>
        internal void CommitSilently()
        {
                EvaluateFrom(commit: true, announce: false);
                EvaluateTo(commit: true, announce: false);
        }

        /// <summary>
        /// Takes down the last commit's notice on the way back into a box. It described
        /// something that happened when the user left, and left standing it becomes a line of
        /// ordinary-looking text under the row — read out as current content by anyone going
        /// down the page afterwards. Clearing it also means the same rewrite happening twice
        /// is announced twice, rather than the second one being swallowed as an unchanged
        /// message.
        /// </summary>
        public void ClearFromCommitAnnouncement() => SetFromCommitAnnouncement(null);

        /// <summary>The same, for the "To" box.</summary>
        public void ClearToCommitAnnouncement() => SetToCommitAnnouncement(null);

        public void SetDuplicate(bool isDuplicate)
        {
                if (_isDuplicate == isDuplicate)
                        return;

                _isDuplicate = isDuplicate;
                // The row error outranks a tidy-up notice. Duplicates are recomputed after the
                // commit that caused them, so the notice can already be standing by the time
                // the clash is known — take it down rather than let the two talk over each
                // other about the same box.
                if (isDuplicate)
                        SetFromCommitAnnouncement(null);
                OnPropertyChanged(nameof(IsDuplicate));
                OnPropertyChanged(nameof(IsFromInputValid));
                OnPropertyChanged(nameof(IsValid));
                OnPropertyChanged(nameof(FromBackgroundBrush));
                if (isDuplicate)
                        SetBindingResult(HotkeyBindingState.Inactive, null);
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
                OnPropertyChanged(nameof(BindingStatusGlyph));
                OnPropertyChanged(nameof(BindingStatusBrush));
                OnPropertyChanged(nameof(BindingStatusTooltip));
                UpdateCombinedError();
        }

        private void EvaluateFrom(bool commit, bool announce)
        {
                string typed = _fromHotkeyText;
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

                if (announce)
                        SetFromCommitAnnouncement(HotkeyCommitAnnouncement.For(
                                FromBoxName, typed, _fromHotkeyText, FromRowMessage));
        }

        private void EvaluateTo(bool commit, bool announce)
        {
                string typed = _toHotkeyText;
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

                if (announce)
                        SetToCommitAnnouncement(HotkeyCommitAnnouncement.For(
                                ToBoxName, typed, _toHotkeyText, _toValidationMessage));
        }

        /// <summary>
        /// What the row has to say about its "From" box, in the order
        /// <see cref="UpdateCombinedError"/> already arbitrates them. Passed to
        /// <see cref="HotkeyCommitAnnouncement"/> as the thing a tidy-up notice must lose to:
        /// being told the shortcut is unusable, or is already taken, matters more than being
        /// told how it is now spelled.
        /// </summary>
        private string? FromRowMessage => IsFromInputValid
                ? null
                : _isDuplicate ? DuplicateFromMessage : _fromValidationMessage;

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

        private const string DuplicateFromMessage = "Duplicate 'From' hotkey.";

        private void UpdateCombinedError()
        {
                string? message;
                if (!IsFromInputValid)
                        message = FromRowMessage;
                else if (!_isToValid)
                        message = _toValidationMessage;
                else if (_bindingState == HotkeyBindingState.Failed)
                        message = _bindingError;
                else
                        message = null;

                if (!string.Equals(_combinedError, message, StringComparison.Ordinal))
                {
                        _combinedError = message;
                        OnPropertyChanged(nameof(BindingErrorMessage));
                        OnPropertyChanged(nameof(HasBindingError));
                        OnPropertyChanged(nameof(BindingErrorVisibility));
                        OnPropertyChanged(nameof(BindingStatusTooltip));
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

        private static Brush? ResolveThemeBrush(string key)
        {
                if (Application.Current?.Resources.TryGetValue(key, out object? value) == true && value is Brush brush)
                        return brush;
                return null;
        }

        private void OnPropertyChanged(string propertyName) =>
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
