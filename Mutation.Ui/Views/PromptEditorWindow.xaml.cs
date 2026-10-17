using CognitiveSupport;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Mutation.Ui.Core;
using Mutation.Ui.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Windows.ApplicationModel.DataTransfer;
using Microsoft.UI.Windowing;
using WinRT.Interop;
using Microsoft.UI;

namespace Mutation.Ui.Views;

public sealed partial class PromptEditorWindow : Window
{
    public LlmSettings.LlmPrompt Prompt { get; private set; }
    public bool IsSaved { get; private set; }
    private readonly TranscriptFormatter _formatter;

    // Closing this editor is the user's way out of a test run they no longer want. A model
    // call climbs an escalating retry ladder and can take minutes, so without this the
    // window would go and the request would carry on with nobody left to read it
    // (issue #256). The state object owns the source's lifetime — cancelling never
    // disposes it, and the run's own finally does — so a close landing mid-retry cannot
    // turn into an ObjectDisposedException.
    private readonly LlmOperationState _testRun = new();

    // Closes the status bar after a few seconds, the way MainWindow's does, so a stopped
    // test run does not leave "Test run cancelled." standing over the editor for the rest
    // of the session — and does not keep stealing height from the prompt box.
    private readonly DispatcherTimer _statusDismissTimer;

    // Matches MainWindow's status bar, so the two windows behave the same way.
    private static readonly TimeSpan StatusDismissDelay = TimeSpan.FromSeconds(6);

    // True once this editor has closed, so a test run unwinding afterwards knows to stay
    // silent rather than reaching for a dialog on a window that has gone.
    private bool _closed;

    // The main window's shutdown token. Closing the app has to stop a test run too, and
    // this editor's Closed handler alone does not cover that.
    private readonly CancellationToken _shutdownToken;

    // Every shortcut the rest of the app has already claimed, with the name each one goes by.
    // Snapshotted when the editor opens, which is when it is true: this window is effectively
    // modal, so nothing else can take a shortcut while it is up.
    private readonly IReadOnlyList<NamedHotkey> _claimedElsewhere;

    // The configured models by name, so the reasoning level list can show the levels ticked
    // for whichever model is chosen. Ordinal, like the services that look a model up by name.
    private readonly Dictionary<string, LlmModelConfig> _modelsByName = new(StringComparer.Ordinal);

    // The level the user last chose, or the saved one. Kept apart from what the list shows:
    // moving to a model without this level ticked changes the list, but moving back to a
    // model that has it shows it again instead of leaving the stand-in level behind.
    private ReasoningLevel _wantedLevel;

    // The levels the list shows now, lightest first, in the list's order. Empty when the
    // chosen model does not reason and the list holds only its "does not reason" item.
    private IReadOnlyList<ReasoningLevel> _shownLevels = Array.Empty<ReasoningLevel>();

    // Set while the list is refilled, so the selection that refill makes is not taken for
    // the user's own choice.
    private bool _refillingReasoningLevels;

    // Internal rather than public because of the last argument: the shortcuts already claimed
    // are described with an internal type, and everything that opens this window is in here.
    internal PromptEditorWindow(
        LlmSettings.LlmPrompt? prompt,
        TranscriptFormatter formatter,
        IReadOnlyList<LlmModelConfig> availableModels,
        CancellationToken shutdownToken = default,
        IReadOnlyList<NamedHotkey>? claimedElsewhere = null)
    {
        this.InitializeComponent();
        _formatter = formatter;
        _shutdownToken = shutdownToken;
        _claimedElsewhere = claimedElsewhere ?? Array.Empty<NamedHotkey>();
        this.Closed += PromptEditorWindow_Closed;
        HkPromptHotkey.HotkeyCommitted += HkPromptHotkey_HotkeyCommitted;

        _statusDismissTimer = new DispatcherTimer { Interval = StatusDismissDelay };
        _statusDismissTimer.Tick += StatusDismissTimer_Tick;

        // Set window size
        IntPtr hWnd = WindowNative.GetWindowHandle(this);
        WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
        AppWindow appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.Resize(new Windows.Graphics.SizeInt32(600, 600));

        // Center the window
        var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
        if (displayArea != null)
        {
            var centeredX = (displayArea.WorkArea.Width - 600) / 2;
            var centeredY = (displayArea.WorkArea.Height - 600) / 2;
            appWindow.Move(new Windows.Graphics.PointInt32(displayArea.WorkArea.X + centeredX, displayArea.WorkArea.Y + centeredY));
        }

        // Make it effective modal (always on top)
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            // presenter.IsModal = true; // CRASH FIX: IsModal requires an owner to be set via P/Invoke, which we aren't doing accurately enough. checking IsAlwaysOnTop is sufficient.
        }

        foreach (var model in availableModels ?? Array.Empty<LlmModelConfig>())
        {
            // First one wins, as a duplicate name can only come from a hand-edited file.
            if (!string.IsNullOrWhiteSpace(model?.Name))
                _modelsByName.TryAdd(model.Name, model);
        }

        var modelList = _modelsByName.Keys.ToList();
        if (!modelList.Contains(LlmSettings.DefaultModel))
        {
            modelList.Insert(0, LlmSettings.DefaultModel);
        }
        CmbModel.ItemsSource = modelList;

        if (prompt == null)
        {
            Prompt = new LlmSettings.LlmPrompt { ModelName = LlmSettings.DefaultModel };
            _wantedLevel = Prompt.ReasoningLevel;
            Title = "Add New Prompt";
            CmbModel.SelectedItem = LlmSettings.DefaultModel;
        }
        else
        {
            Prompt = prompt;
            Title = "Edit Prompt";

            // Populate fields
            TxtName.Text = Prompt.Name;
            HkPromptHotkey.Hotkey = Prompt.Hotkey ?? string.Empty;
            TxtContent.Text = Prompt.Content;
            ChkAutoRun.IsChecked = Prompt.AutoRun;
            ChkFastMode.IsChecked = Prompt.FastMode;
            _wantedLevel = Prompt.ReasoningLevel;

            string desiredModel = !string.IsNullOrWhiteSpace(Prompt.ModelName) ? Prompt.ModelName : LlmSettings.DefaultModel;
            if (!modelList.Contains(desiredModel))
            {
                modelList.Insert(0, desiredModel);
                CmbModel.ItemsSource = modelList;
            }
            CmbModel.SelectedItem = desiredModel;
        }

        // Filled once the model is chosen, and subscribed only then, so opening the editor
        // fills the list once. Said aloud at open too, since a saved level the model no
        // longer has ticked is exactly what the user opening this prompt needs to hear
        // about — but only once the window's content has loaded, because a notification
        // raised from the constructor comes before the window exists and is heard by nobody.
        string? openNotice = RefreshReasoningLevels();
        if (openNotice is not null && Content is FrameworkElement root)
        {
            void OnLoaded(object s, RoutedEventArgs e)
            {
                root.Loaded -= OnLoaded;
                ShowStatus("Reasoning level", openNotice, InfoBarSeverity.Informational);
            }
            root.Loaded += OnLoaded;
        }
        CmbModel.SelectionChanged += CmbModel_SelectionChanged;
        CmbReasoningLevel.SelectionChanged += CmbReasoningLevel_SelectionChanged;

        // Checked as the window opens, not only after an edit. A prompt saved before the other
        // end existed is already clashing, and the user opening it is exactly who needs to know.
        ShowHotkeyClash();
    }

    private void HkPromptHotkey_HotkeyCommitted(object? sender, string value) => ShowHotkeyClash();

    private string SelectedModelName => CmbModel.SelectedItem as string ?? LlmSettings.DefaultModel;

    private void CmbModel_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        string? notice = RefreshReasoningLevels();
        if (notice is not null)
            ShowStatus("Reasoning level", notice, InfoBarSeverity.Informational);
    }

    private void CmbReasoningLevel_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refillingReasoningLevels)
            return;
        int index = CmbReasoningLevel.SelectedIndex;
        if (index >= 0 && index < _shownLevels.Count)
            _wantedLevel = _shownLevels[index];
    }

    /// <summary>
    /// Fills the reasoning level list for the chosen model. The levels and the selection come
    /// from <see cref="ReasoningLevelPick"/>, which settles them the way a run does, so the
    /// level shown is the level sent.
    /// </summary>
    /// <returns>
    /// What to say on the status line when the list cannot show the wanted level, or null.
    /// Without it a screen-reader user would save a level they never chose.
    /// </returns>
    private string? RefreshReasoningLevels()
    {
        string modelName = SelectedModelName;
        _modelsByName.TryGetValue(modelName, out var model);
        var pick = ReasoningLevelPick.For(model, _wantedLevel);

        _refillingReasoningLevels = true;
        try
        {
            _shownLevels = pick.Levels;
            if (pick.Reasons)
            {
                CmbReasoningLevel.ItemsSource = pick.Levels.Select(ReasoningLevelCatalog.DisplayName).ToList();
                // -1 when a run sends no level: nothing is selected, and saving keeps the
                // prompt's own level, unless the user picks one.
                CmbReasoningLevel.SelectedIndex = pick.SelectedIndex;
                CmbReasoningLevel.PlaceholderText = pick.SendsNoLevel ? ReasoningMessages.NoLevelSelected : string.Empty;
                CmbReasoningLevel.IsEnabled = true;
            }
            else
            {
                // One item rather than placeholder text: the item is the control's value,
                // which is what a screen reader reads for a list that is turned off.
                CmbReasoningLevel.ItemsSource = new List<string> { ReasoningMessages.NoReasoning };
                CmbReasoningLevel.SelectedIndex = 0;
                CmbReasoningLevel.PlaceholderText = string.Empty;
                CmbReasoningLevel.IsEnabled = false;
            }
        }
        finally
        {
            _refillingReasoningLevels = false;
        }

        if (pick.SendsNoLevel)
            return ReasoningMessages.NoLevelSent(modelName, _wantedLevel);
        if (pick.MovedFrom(_wantedLevel) && pick.Selected is { } shown)
            return ReasoningMessages.Moved(modelName, _wantedLevel, shown);
        return null;
    }

    /// <summary>
    /// The level saving would store and a test run sends: the one the list shows, or, while
    /// the list selects nothing (the model does not reason, or a run sends no level), the
    /// wanted level, so the prompt keeps its level and a test run resolves it exactly as a
    /// real run does.
    /// </summary>
    private ReasoningLevel CurrentReasoningLevel()
    {
        int index = CmbReasoningLevel.SelectedIndex;
        return index >= 0 && index < _shownLevels.Count
            ? _shownLevels[index]
            : _wantedLevel;
    }

    /// <summary>
    /// Says whether this shortcut is already claimed — by another prompt, by one of the app's
    /// own shortcuts, or by a hotkey route.
    /// <para>
    /// The Hotkeys page checks its own rows against each other and against the prompts, so a
    /// prompt clashing with a core hotkey or a route is flagged there. Nothing checked a prompt
    /// against another prompt: two of them on <c>CTRL+ALT+P</c> looked right in both editors,
    /// and whichever registered second came back in the "Some hotkeys could not be registered"
    /// dialog after saving — the wrong moment to find out, and the last list where finding out
    /// late was still possible (issue #340).
    /// </para>
    /// <para>
    /// A warning, not a refusal. Registration is what actually fails, one shortcut is still
    /// perfectly usable while the other is not, and taking the Save button away from someone
    /// mid-edit is a worse answer than telling them plainly.
    /// </para>
    /// </summary>
    private void ShowHotkeyClash() =>
        LiveMessage.Show(
            TxtHotkeyClash,
            HotkeyClashDescription.For(HkPromptHotkey.Hotkey, _claimedElsewhere));

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        // Validation
        if (string.IsNullOrWhiteSpace(TxtName.Text))
        {
            ShowError("Name is required.");
            return;
        }

        // Reject an invalid hotkey before saving so a typo cannot silently fail to bind later.
        string hotkey = (HkPromptHotkey.Hotkey ?? string.Empty).Trim();
        if (!string.IsNullOrEmpty(hotkey))
        {
            string? hotkeyError = HotkeyValidator.Validate(hotkey, allowSendKeysSyntax: false);
            if (hotkeyError is not null)
            {
                ShowError($"Hotkey is not valid: {hotkeyError}");
                return;
            }
        }

        // Update object
        Prompt.Name = TxtName.Text;
        Prompt.Hotkey = hotkey;
        Prompt.Content = TxtContent.Text;
        Prompt.AutoRun = ChkAutoRun.IsChecked ?? false;
        Prompt.ModelName = CmbModel.SelectedItem as string ?? LlmSettings.DefaultModel;
        Prompt.FastMode = ChkFastMode.IsChecked ?? false;
        Prompt.ReasoningLevel = CurrentReasoningLevel();

        IsSaved = true;

        // Set DialogResult logic (using Tag or similar if Window doesn't support DialogResult natively like WPF)
        // Since this is WinUI 3 Window, we don't have DialogResult. 
        // We can just close and the caller checks the object properties or we use an event.
        // But commonly checking if properties were set is enough if we handle "Cancel" by not updating.
        // Wait, I updated only the object on Save. 
        // If the caller passed a reference to an existing object, I modified it in place. 
        // If "Cancel" is clicked, I should probably have cloned it first?
        // Correct approach: Clone on entry, apply to original on Save. OR just modify properties on Save.
        // Since I am modifying `Prompt` which is a reference, if I modify it on Save, that is fine.
        // If I modify it as I type (bindings), Cancel is harder. I am not using bindings here, just direct set on Save.
        
        this.Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
         this.Close();
    }

    private async void BtnTest_Click(object sender, RoutedEventArgs e)
    {
        // Declared out here so the catch filters below can tell a cancel from a provider
        // that never answered.
        CancellationToken testToken = default;
        try
        {
            // A second press while a run is in flight means "stop". The button is
            // deliberately not disabled instead: disabling the control the user is standing
            // on destroys their focus and leaves a screen-reader user with no idea where
            // they are (the reasoning issue #227 settled for the OCR Cancel button).
            // Without this the second click started a second run, whose Begin cancelled the
            // first, and the button silently ate both.
            switch (CancellablePressPlanner.For(_testRun.Running, _testRun.CancelRequested))
            {
                case CancellablePressAction.AlreadyStopping:
                    // A repeat press on a stop already asked for. Answered rather than
                    // ignored: an enabled button that produces silence reads as one that
                    // did not register (#309). No second beep — the first press already
                    // acknowledged audibly, and the stop it refers to has not changed.
                    ShowStatus("Test Run", CancellationMessages.AlreadyStopping, InfoBarSeverity.Informational);
                    return;

                case CancellablePressAction.Cancel:
                    _testRun.Cancel();
                    // Progress, then completion — the same split the rest of the app makes.
                    // The beep lands immediately; the run says it has actually let go when
                    // it unwinds, which can be seconds later.
                    BeepPlayer.Play(BeepType.Failure);
                    ShowStatus("Test Run", "Cancelling the test run...", InfoBarSeverity.Informational);
                    return;
            }

            // Starting afresh, so last run's outcome stops standing over this one.
            HideStatus();

            var dataPackageView = Clipboard.GetContent();
            if (dataPackageView.Contains(StandardDataFormats.Text))
            {
                string text = await dataPackageView.GetTextAsync();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    // Use the CURRENT values in the editor, not the saved ones, so the
                    // test run reflects exactly what saving would produce.
                    string currentContent = TxtContent.Text;
                    string testModel = CmbModel.SelectedItem as string ?? LlmSettings.DefaultModel;

                    // The user asked for this run and is waiting on its dialog, so a Fast
                    // mode fallback is reported in the result they already opened rather
                    // than through the once-per-session announcement channel.
                    FastModeFallback? fallback = null;
                    ReasoningLevel? reasoningFallback = null;
                    bool cutShort = false;
                    var requestOptions = new LlmRequestOptions
                    {
                        FastMode = ChkFastMode.IsChecked ?? false,
                        OnFastModeFallback = f => fallback = f,
                        ReasoningLevel = CurrentReasoningLevel(),
                        OnReasoningFallback = level => reasoningFallback = level,
                        OnCutShort = () => cutShort = true,
                    };
                    // Linked to the window's shutdown token, so closing the *main* window
                    // abandons a test run too — this editor's own Closed handler only
                    // covers closing the editor.
                    using var run = _testRun.Begin(_shutdownToken);
                    testToken = run.Token;
                    string result = await _formatter.ProcessWithLlmAsync(text, currentContent, testModel, requestOptions, run.Token);

                    // Show result in a dialog or just a message box?
                    // WinUI 3 MessageDialog or ContentDialog requires XamlRoot.
                    // This is a Window, so we have a root.

                    // Notices go above the result, so a screen reader reaches them before
                    // the text they qualify.
                    var notices = new List<string>();
                    if (cutShort)
                        notices.Add(CutShortMessages.Notice);
                    if (fallback is not null)
                        notices.Add(FastModeMessages.Describe(fallback.Reason));
                    if (reasoningFallback is { } refused)
                        notices.Add(ReasoningMessages.Fallback(testModel, refused));
                    notices.Add(result);
                    string body = string.Join(Environment.NewLine + Environment.NewLine, notices);

                    var dialog = new ContentDialog
                    {
                        Title = "Test Result",
                        Content = new ScrollViewer { Content = new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap } },
                        CloseButtonText = "Close",
                        XamlRoot = this.Content.XamlRoot
                    };
                    await dialog.ShowAsync();
                }
                else
                {
                    ShowError("Clipboard is empty.");
                }
            }
            else
            {
                ShowError("Clipboard does not contain text.");
            }
        }
        catch (OperationCanceledException) when (_closed)
        {
            // The window has gone, so there is nothing left to show the result in and
            // nobody to show an error to. Closing it was the answer.
        }
        catch (OperationCanceledException) when (testToken.IsCancellationRequested)
        {
            // Stopped from the Test Run button while the editor is still open. On the
            // status line rather than in a dialog: the user asked for this, so there is no
            // reason to take their focus away from the prompt they were editing.
            ShowStatus("Test Run", "Test run cancelled.", InfoBarSeverity.Informational);
        }
        catch (Exception ex)
        {
            // Keep the full chain in the log and show the reader the short form,
            // for the same reason as MainWindow's error dialog (issue #242).
            ErrorLogger.LogError("Prompt test", ex);
            ShowError(ErrorDialogMessage.ForException(ex, ErrorLogger.PrimaryLogPath));
        }
    }

    private void PromptEditorWindow_Closed(object sender, WindowEventArgs args)
    {
        this.Closed -= PromptEditorWindow_Closed;
        HkPromptHotkey.HotkeyCommitted -= HkPromptHotkey_HotkeyCommitted;
        CmbModel.SelectionChanged -= CmbModel_SelectionChanged;
        CmbReasoningLevel.SelectionChanged -= CmbReasoningLevel_SelectionChanged;
        _closed = true;
        _statusDismissTimer.Stop();
        _statusDismissTimer.Tick -= StatusDismissTimer_Tick;
        // Signal only. Disposal belongs to the run's own handle, which is the one place
        // that knows every retry has finished. A no-op when nothing is running.
        _testRun.Cancel();
    }

    /// <summary>
    /// This window's status line, for outcomes that are not failures. Mirrors
    /// MainWindow.ShowStatus, including the explicit UIA notification: WinUI announces an
    /// InfoBar only on its closed-to-open transition, so a second message while the bar is
    /// already open would never be spoken (issue #164).
    /// </summary>
    private void ShowStatus(string title, string message, InfoBarSeverity severity)
    {
        message = ErrorLogger.RedactSecrets(message ?? string.Empty);

        StatusInfoBar.Title = title;
        StatusInfoBar.Message = message;
        StatusInfoBar.Severity = severity;
        StatusInfoBar.IsOpen = true;
        AutomationProperties.SetName(StatusInfoBar, $"{title} status");
        AutomationProperties.SetHelpText(StatusInfoBar, message);

        _statusDismissTimer.Stop();
        _statusDismissTimer.Start();

        string announcement = StatusAnnouncement.ComposeText(title, message);
        if (announcement.Length == 0)
            return;

        try
        {
            // CreatePeerForElement, not FromElement, so the very first status announces too.
            var peer = FrameworkElementAutomationPeer.CreatePeerForElement(StatusInfoBar);
            peer?.RaiseNotificationEvent(
                StatusAnnouncement.GetKind(severity),
                StatusAnnouncement.GetProcessing(severity),
                announcement,
                PromptEditorActivityId);
        }
        catch (Exception ex)
        {
            // One call site is inside a catch block of an async void handler, where an
            // escaping exception reaches the global handler and the user is told nothing
            // about the failure they were being told about. The status bar itself is
            // already updated by this point, so the announcement is the only casualty.
            ErrorLogger.LogError("Prompt editor status announcement", ex);
        }
    }

    /// <summary>
    /// Its own activity id, not the window-wide one. Notifications are deduplicated per
    /// activity, and the main window keeps raising its own while this editor is open —
    /// sharing the id would let one supersede the other. RegionSelectionWindow, the other
    /// separate window that announces, does the same.
    /// </summary>
    private const string PromptEditorActivityId = "Mutation.PromptEditor.Status";

    private void HideStatus()
    {
        _statusDismissTimer.Stop();
        StatusInfoBar.IsOpen = false;
    }

    private void StatusDismissTimer_Tick(object? sender, object e) => HideStatus();

    // The user dismissed it themselves; the timer has nothing left to close.
    private void StatusInfoBar_CloseButtonClick(InfoBar sender, object args) => _statusDismissTimer.Stop();

    private async void ShowError(string message)
    {
        // This text is read aloud and pasted into bug reports, so it goes through the
        // same redactor the error log uses. A no-op for the validation messages above.
        message = ErrorDialogMessage.ForMessage(message);

        var dialog = new ContentDialog
        {
            Title = "Error",
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = this.Content.XamlRoot
        };
        await dialog.ShowAsync();
    }
}
