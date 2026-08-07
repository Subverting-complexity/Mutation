using CognitiveSupport;
using Microsoft.UI.Xaml;
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

    // True once this editor has closed, so a test run unwinding afterwards knows to stay
    // silent rather than reaching for a dialog on a window that has gone.
    private bool _closed;

    // The main window's shutdown token. Closing the app has to stop a test run too, and
    // this editor's Closed handler alone does not cover that.
    private readonly CancellationToken _shutdownToken;

    public PromptEditorWindow(
        LlmSettings.LlmPrompt? prompt,
        TranscriptFormatter formatter,
        IReadOnlyList<string> availableModels,
        CancellationToken shutdownToken = default)
    {
        this.InitializeComponent();
        _formatter = formatter;
        _shutdownToken = shutdownToken;
        this.Closed += PromptEditorWindow_Closed;

        // Set window size
        IntPtr hWnd = WindowNative.GetWindowHandle(this);
        WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
        AppWindow appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.Resize(new Windows.Graphics.SizeInt32(600, 540));

        // Center the window
        var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
        if (displayArea != null)
        {
            var centeredX = (displayArea.WorkArea.Width - 600) / 2;
            var centeredY = (displayArea.WorkArea.Height - 540) / 2;
            appWindow.Move(new Windows.Graphics.PointInt32(displayArea.WorkArea.X + centeredX, displayArea.WorkArea.Y + centeredY));
        }

        // Make it effective modal (always on top)
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            // presenter.IsModal = true; // CRASH FIX: IsModal requires an owner to be set via P/Invoke, which we aren't doing accurately enough. checking IsAlwaysOnTop is sufficient.
        }

        var modelList = (availableModels ?? Array.Empty<string>()).ToList();
        if (!modelList.Contains(LlmSettings.DefaultModel))
        {
            modelList.Insert(0, LlmSettings.DefaultModel);
        }
        CmbModel.ItemsSource = modelList;

        if (prompt == null)
        {
            Prompt = new LlmSettings.LlmPrompt { ModelName = LlmSettings.DefaultModel };
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

            string desiredModel = !string.IsNullOrWhiteSpace(Prompt.ModelName) ? Prompt.ModelName : LlmSettings.DefaultModel;
            if (!modelList.Contains(desiredModel))
            {
                modelList.Insert(0, desiredModel);
                CmbModel.ItemsSource = modelList;
            }
            CmbModel.SelectedItem = desiredModel;
        }
    }

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
            if (_testRun.Running)
            {
                if (!_testRun.CancelRequested)
                {
                    _testRun.Cancel();
                    // The beep is the immediate acknowledgement; the run announces its own
                    // outcome in a dialog when it lets go, so nothing is said twice.
                    BeepPlayer.Play(BeepType.Failure);
                }
                return;
            }

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
                    var requestOptions = new LlmRequestOptions
                    {
                        FastMode = ChkFastMode.IsChecked ?? false,
                        OnFastModeFallback = f => fallback = f,
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

                    string body = fallback is null
                        ? result
                        : $"{FastModeMessages.Describe(fallback.Reason)}{Environment.NewLine}{Environment.NewLine}{result}";

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
            // Stopped from the Test Run button while the editor is still open. Said out
            // loud: this window has no status line, so the dialog is the only channel it
            // has, and a button that produces silence reads as one that did not register.
            ShowInfo("Test Run", "Test run cancelled.");
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
        _closed = true;
        // Signal only. Disposal belongs to the run's own handle, which is the one place
        // that knows every retry has finished. A no-op when nothing is running.
        _testRun.Cancel();
    }

    // The plain-titled sibling of ShowError, for outcomes that are not failures. Same
    // channel because it is the only one this window has.
    private async void ShowInfo(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = this.Content.XamlRoot
        };
        await dialog.ShowAsync();
    }

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
