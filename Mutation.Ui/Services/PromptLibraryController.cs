using CognitiveSupport;
using Microsoft.UI.Xaml.Controls;
using Mutation.Ui.Core;
using Mutation.Ui.Views;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Mutation.Ui.Services;

internal sealed class PromptLibraryController
{
	private readonly Settings _settings;
	private readonly ISettingsManager _settingsManager;
	private readonly TranscriptFormatter _transcriptFormatter;
	private readonly ListView _promptListView;
	private readonly Action<LlmSettings.LlmPrompt> _executePrompt;
	private readonly Action<IReadOnlyList<HotkeyManager.HotkeyBindingFailure>>? _reportHotkeyFailures;
	// Handed to every prompt editor this controller opens, so closing the app stops a
	// Test Run in flight instead of leaving it climbing the retry ladder (issue #256).
	private readonly CancellationToken _shutdownToken;

	private HotkeyManager? _hotkeyManager;

	public PromptLibraryController(
		Settings settings,
		ISettingsManager settingsManager,
		TranscriptFormatter transcriptFormatter,
		ListView promptListView,
		Action<LlmSettings.LlmPrompt> executePrompt,
		Action<IReadOnlyList<HotkeyManager.HotkeyBindingFailure>>? reportHotkeyFailures = null,
		CancellationToken shutdownToken = default)
	{
		_settings = settings ?? throw new ArgumentNullException(nameof(settings));
		_settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
		_transcriptFormatter = transcriptFormatter ?? throw new ArgumentNullException(nameof(transcriptFormatter));
		_promptListView = promptListView ?? throw new ArgumentNullException(nameof(promptListView));
		_executePrompt = executePrompt ?? throw new ArgumentNullException(nameof(executePrompt));
		_reportHotkeyFailures = reportHotkeyFailures;
		_shutdownToken = shutdownToken;
	}

	public void Initialize()
	{
		RebindPromptList();
	}

	/// <summary>
	/// Re-points the ListView at the current prompt collection. Needed after a Settings
	/// save, which rewrites the prompts in place: a plain List raises no change
	/// notifications, so the rows would otherwise keep rendering pre-save content
	/// (issue #219).
	/// </summary>
	public void RebindPromptList()
	{
		if (_settings.LlmSettings == null)
			return;

		_promptListView.ItemsSource = null;
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
		return PromptLibraryMutations.AutoRunPrompt(_settings.LlmSettings?.Prompts);
	}

	public void OpenAddDialog()
	{
		if (_settings.LlmSettings == null)
			return;

		var dialog = new PromptEditorWindow(
			null, _transcriptFormatter, GetAvailableModelNames(), _shutdownToken,
			ClaimedHotkeys.Excluding(_settings, excludedPrompt: null));
		dialog.Activate();
		dialog.Closed += (_, _) =>
		{
			if (dialog.IsSaved && dialog.Prompt != null && !string.IsNullOrWhiteSpace(dialog.Prompt.Name))
			{
				// Appending, taking AutoRun off whichever prompt held it, and handing out
				// the new prompt's Id are all one unit now, shared with the edit path below
				// and exercisable without opening a window (issue #304). The Id rule is the
				// same one settings loading uses: lowest free number, because
				// highest-plus-one would overflow to int.MinValue against a hand-written Id
				// of int.MaxValue and then hand every later prompt that same value.
				PromptLibraryMutations.Add(_settings.LlmSettings.Prompts, dialog.Prompt);

				SaveAndRefresh();
			}
		};
	}

	public void OpenEditDialog(LlmSettings.LlmPrompt prompt)
	{
		if (prompt == null || _settings.LlmSettings == null)
			return;

		// This prompt is left out of its own check: its saved shortcut is the one in the box,
		// and a prompt cannot be a duplicate of itself.
		var dialog = new PromptEditorWindow(
			prompt, _transcriptFormatter, GetAvailableModelNames(), _shutdownToken,
			ClaimedHotkeys.Excluding(_settings, prompt));
		dialog.Activate();
		dialog.Closed += (_, _) =>
		{
			if (!dialog.IsSaved)
				return;

			// The editor writes straight into the prompt object, so there is nothing to copy
			// back — the only thing left to settle is whether it has just taken AutoRun off
			// another prompt.
			PromptLibraryMutations.CommitEdit(_settings.LlmSettings.Prompts, prompt);

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
		if (_settings.LlmSettings == null)
			return false;

		if (!PromptLibraryMutations.Delete(_settings.LlmSettings.Prompts, prompt))
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

		RebindPromptList();

		if (_hotkeyManager is null)
			return;

		var failures = _hotkeyManager.RegisterPromptHotkeys(_settings.LlmSettings.Prompts, _executePrompt);
		if (failures.Count > 0)
			_reportHotkeyFailures?.Invoke(failures);
	}
}
