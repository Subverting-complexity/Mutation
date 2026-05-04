using CognitiveSupport;
using Microsoft.UI.Xaml.Controls;
using Mutation.Ui.Views;
using System;
using System.Linq;

namespace Mutation.Ui.Services;

internal sealed class PromptLibraryController
{
	private readonly Settings _settings;
	private readonly ISettingsManager _settingsManager;
	private readonly TranscriptFormatter _transcriptFormatter;
	private readonly ListView _promptListView;
	private readonly Action<LlmSettings.LlmPrompt> _executePrompt;

	private HotkeyManager? _hotkeyManager;

	public PromptLibraryController(
		Settings settings,
		ISettingsManager settingsManager,
		TranscriptFormatter transcriptFormatter,
		ListView promptListView,
		Action<LlmSettings.LlmPrompt> executePrompt)
	{
		_settings = settings ?? throw new ArgumentNullException(nameof(settings));
		_settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
		_transcriptFormatter = transcriptFormatter ?? throw new ArgumentNullException(nameof(transcriptFormatter));
		_promptListView = promptListView ?? throw new ArgumentNullException(nameof(promptListView));
		_executePrompt = executePrompt ?? throw new ArgumentNullException(nameof(executePrompt));
	}

	public void Initialize()
	{
		if (_settings.LlmSettings != null)
			_promptListView.ItemsSource = _settings.LlmSettings.Prompts;
	}

	public void AttachHotkeyManager(HotkeyManager hotkeyManager)
	{
		_hotkeyManager = hotkeyManager ?? throw new ArgumentNullException(nameof(hotkeyManager));
		if (_settings.LlmSettings?.Prompts != null)
			_hotkeyManager.RegisterPromptHotkeys(_settings.LlmSettings.Prompts, _executePrompt);
	}

	public string GetAutoRunPromptContent()
	{
		var prompt = _settings.LlmSettings?.Prompts.FirstOrDefault(p => p.AutoRun);
		return prompt?.Content ?? string.Empty;
	}

	public void OpenAddDialog()
	{
		if (_settings.LlmSettings == null)
			return;

		var dialog = new PromptEditorWindow(null, _transcriptFormatter);
		dialog.Activate();
		dialog.Closed += (_, _) =>
		{
			if (dialog.IsSaved && dialog.Prompt != null && !string.IsNullOrWhiteSpace(dialog.Prompt.Name))
			{
				dialog.Prompt.Id = (_settings.LlmSettings.Prompts.Max(p => (int?)p.Id) ?? 0) + 1;

				if (dialog.Prompt.AutoRun)
					foreach (var p in _settings.LlmSettings.Prompts) p.AutoRun = false;

				_settings.LlmSettings.Prompts.Add(dialog.Prompt);
				SaveAndRefresh();
			}
		};
	}

	public void OpenEditDialog(LlmSettings.LlmPrompt prompt)
	{
		if (prompt == null || _settings.LlmSettings == null)
			return;

		var dialog = new PromptEditorWindow(prompt, _transcriptFormatter);
		dialog.Activate();
		dialog.Closed += (_, _) =>
		{
			if (!dialog.IsSaved)
				return;

			if (prompt.AutoRun)
			{
				foreach (var p in _settings.LlmSettings.Prompts)
				{
					if (p != prompt) p.AutoRun = false;
				}
			}

			SaveAndRefresh();
		};
	}

	public void DeletePrompt(LlmSettings.LlmPrompt prompt)
	{
		if (prompt == null || _settings.LlmSettings == null)
			return;

		_settings.LlmSettings.Prompts.Remove(prompt);
		SaveAndRefresh();
	}

	private void SaveAndRefresh()
	{
		if (_settings.LlmSettings == null)
			return;

		_settingsManager.SaveSettingsToFile(_settings);

		_promptListView.ItemsSource = null;
		_promptListView.ItemsSource = _settings.LlmSettings.Prompts;

		_hotkeyManager?.RegisterPromptHotkeys(_settings.LlmSettings.Prompts, _executePrompt);
	}
}
