using CognitiveSupport;
using Microsoft.UI.Xaml.Controls;
using Mutation.Ui.Views;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Mutation.Ui.Services;

internal sealed class PromptLibraryController
{
	private readonly Settings _settings;
	private readonly ISettingsManager _settingsManager;
	private readonly TranscriptFormatter _transcriptFormatter;
	private readonly ListView _promptListView;
	private readonly Action<LlmSettings.LlmPrompt> _executePrompt;
	private readonly Action<IReadOnlyList<HotkeyManager.HotkeyBindingFailure>>? _reportHotkeyFailures;

	private HotkeyManager? _hotkeyManager;

	public PromptLibraryController(
		Settings settings,
		ISettingsManager settingsManager,
		TranscriptFormatter transcriptFormatter,
		ListView promptListView,
		Action<LlmSettings.LlmPrompt> executePrompt,
		Action<IReadOnlyList<HotkeyManager.HotkeyBindingFailure>>? reportHotkeyFailures = null)
	{
		_settings = settings ?? throw new ArgumentNullException(nameof(settings));
		_settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
		_transcriptFormatter = transcriptFormatter ?? throw new ArgumentNullException(nameof(transcriptFormatter));
		_promptListView = promptListView ?? throw new ArgumentNullException(nameof(promptListView));
		_executePrompt = executePrompt ?? throw new ArgumentNullException(nameof(executePrompt));
		_reportHotkeyFailures = reportHotkeyFailures;
	}

	public void Initialize()
	{
		if (_settings.LlmSettings != null)
			_promptListView.ItemsSource = _settings.LlmSettings.Prompts;
	}

	public IReadOnlyList<HotkeyManager.HotkeyBindingFailure> AttachHotkeyManager(HotkeyManager hotkeyManager)
	{
		_hotkeyManager = hotkeyManager ?? throw new ArgumentNullException(nameof(hotkeyManager));
		if (_settings.LlmSettings?.Prompts != null)
			return _hotkeyManager.RegisterPromptHotkeys(_settings.LlmSettings.Prompts, _executePrompt);
		return Array.Empty<HotkeyManager.HotkeyBindingFailure>();
	}

	public LlmSettings.LlmPrompt? GetAutoRunPrompt()
	{
		return _settings.LlmSettings?.Prompts.FirstOrDefault(p => p.AutoRun);
	}

	public void OpenAddDialog()
	{
		if (_settings.LlmSettings == null)
			return;

		var dialog = new PromptEditorWindow(null, _transcriptFormatter, GetAvailableModelNames());
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

		var dialog = new PromptEditorWindow(prompt, _transcriptFormatter, GetAvailableModelNames());
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

	/// <summary>
	/// Removes the prompt, persists, and refreshes the list. Returns
	/// <c>true</c> when a prompt was actually removed so the caller can
	/// announce an accurate outcome; <c>false</c> when there was nothing to
	/// remove (null prompt, no settings, or the prompt was already gone).
	/// </summary>
	public bool DeletePrompt(LlmSettings.LlmPrompt prompt)
	{
		if (prompt == null || _settings.LlmSettings == null)
			return false;

		if (!_settings.LlmSettings.Prompts.Remove(prompt))
			return false;

		SaveAndRefresh();
		return true;
	}

	private IReadOnlyList<string> GetAvailableModelNames()
	{
		return _settings.LlmSettings?.Models?.Select(m => m.Name).ToList() ?? new List<string>();
	}

	private void SaveAndRefresh()
	{
		if (_settings.LlmSettings == null)
			return;

		_settingsManager.SaveSettingsToFile(_settings);

		_promptListView.ItemsSource = null;
		_promptListView.ItemsSource = _settings.LlmSettings.Prompts;

		if (_hotkeyManager is null)
			return;

		var failures = _hotkeyManager.RegisterPromptHotkeys(_settings.LlmSettings.Prompts, _executePrompt);
		if (failures.Count > 0)
			_reportHotkeyFailures?.Invoke(failures);
	}
}
