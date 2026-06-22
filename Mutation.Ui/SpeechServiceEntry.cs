using System;
using System.Collections.Generic;
using CognitiveSupport;

namespace Mutation.Ui;

// INotifyPropertyChanged row wrapper around a single SpeechToTextServiceSettings,
// bound by the Speech settings page's per-service expander list. Field setters
// mutate the wrapped model in place, so the array the page hands back to settings
// stays current without rebuilding on every keystroke (only add/delete rebuild it).
public sealed class SpeechServiceEntry : System.ComponentModel.INotifyPropertyChanged
{
	private static readonly IReadOnlyList<SpeechToTextProviders> _providerOptions =
		new[] { SpeechToTextProviders.None, SpeechToTextProviders.OpenAi, SpeechToTextProviders.Deepgram };

	private readonly SpeechToTextServiceSettings _model;

	public SpeechServiceEntry(SpeechToTextServiceSettings model)
	{
		_model = model ?? throw new ArgumentNullException(nameof(model));
	}

	internal SpeechToTextServiceSettings Model => _model;

	public IReadOnlyList<SpeechToTextProviders> ProviderOptions => _providerOptions;

	public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

	// Shown in the expander header; falls back to a placeholder when unnamed so the
	// header never collapses to an empty, unfocusable strip for screen-reader users.
	public string HeaderText =>
		string.IsNullOrWhiteSpace(_model.Name) ? "(unnamed service)" : _model.Name!;

	public string Name
	{
		get => _model.Name ?? string.Empty;
		set
		{
			string v = value ?? string.Empty;
			if (string.Equals(_model.Name, v, StringComparison.Ordinal)) return;
			_model.Name = v;
			OnPropertyChanged(nameof(Name));
			OnPropertyChanged(nameof(HeaderText));
		}
	}

	public SpeechToTextProviders Provider
	{
		get => _model.Provider;
		set
		{
			if (_model.Provider == value) return;
			_model.Provider = value;
			OnPropertyChanged(nameof(Provider));
		}
	}

	public string ApiKey
	{
		get => _model.ApiKey ?? string.Empty;
		set
		{
			string v = value ?? string.Empty;
			if (string.Equals(_model.ApiKey, v, StringComparison.Ordinal)) return;
			_model.ApiKey = v;
			OnPropertyChanged(nameof(ApiKey));
		}
	}

	public string BaseDomain
	{
		get => _model.BaseDomain ?? string.Empty;
		set
		{
			string v = value ?? string.Empty;
			if (string.Equals(_model.BaseDomain, v, StringComparison.Ordinal)) return;
			_model.BaseDomain = v;
			OnPropertyChanged(nameof(BaseDomain));
		}
	}

	public string ModelId
	{
		get => _model.ModelId ?? string.Empty;
		set
		{
			string v = value ?? string.Empty;
			if (string.Equals(_model.ModelId, v, StringComparison.Ordinal)) return;
			_model.ModelId = v;
			OnPropertyChanged(nameof(ModelId));
		}
	}

	public string SpeechToTextPrompt
	{
		get => _model.SpeechToTextPrompt ?? string.Empty;
		set
		{
			string v = value ?? string.Empty;
			if (string.Equals(_model.SpeechToTextPrompt, v, StringComparison.Ordinal)) return;
			_model.SpeechToTextPrompt = v;
			OnPropertyChanged(nameof(SpeechToTextPrompt));
		}
	}

	// NumberBox.Value is a double; the model stores an int.
	public double TimeoutSeconds
	{
		get => _model.TimeoutSeconds;
		set
		{
			if (double.IsNaN(value)) return;
			int v = (int)value;
			if (_model.TimeoutSeconds == v) return;
			_model.TimeoutSeconds = v;
			OnPropertyChanged(nameof(TimeoutSeconds));
		}
	}

	private void OnPropertyChanged(string propertyName) =>
		PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
}
