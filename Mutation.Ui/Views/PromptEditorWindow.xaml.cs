using CognitiveSupport;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
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

    public PromptEditorWindow(LlmSettings.LlmPrompt? prompt, TranscriptFormatter formatter, IReadOnlyList<string> availableModels)
    {
        this.InitializeComponent();
        _formatter = formatter;

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
            TxtHotkey.Text = Prompt.Hotkey;
            TxtContent.Text = Prompt.Content;
            ChkAutoRun.IsChecked = Prompt.AutoRun;

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

        // Update object
        Prompt.Name = TxtName.Text;
        Prompt.Hotkey = TxtHotkey.Text; // Basic text for now, could implement validation later
        Prompt.Content = TxtContent.Text;
        Prompt.AutoRun = ChkAutoRun.IsChecked ?? false;
        Prompt.ModelName = CmbModel.SelectedItem as string ?? LlmSettings.DefaultModel;

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
        try
        {
            var dataPackageView = Clipboard.GetContent();
            if (dataPackageView.Contains(StandardDataFormats.Text))
            {
                string text = await dataPackageView.GetTextAsync();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    // Use the CURRENT text in the content box, not the saved one
                    string currentContent = TxtContent.Text;
                    string testModel = CmbModel.SelectedItem as string ?? LlmSettings.DefaultModel;
                    string result = await _formatter.ProcessWithLlmAsync(text, currentContent, testModel);
                    
                    // Show result in a dialog or just a message box?
                    // WinUI 3 MessageDialog or ContentDialog requires XamlRoot.
                    // This is a Window, so we have a root.
                    
                    var dialog = new ContentDialog
                    {
                        Title = "Test Result",
                        Content = new ScrollViewer { Content = new TextBlock { Text = result, TextWrapping = TextWrapping.Wrap } },
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
        catch (Exception ex)
        {
            ShowError($"Test failed: {ex.Message}");
        }
    }

    private async void ShowError(string message)
    {
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
