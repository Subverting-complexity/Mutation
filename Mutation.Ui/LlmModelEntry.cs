using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using CognitiveSupport;

namespace Mutation.Ui;

public sealed class LlmModelEntry : INotifyPropertyChanged
{
	private static readonly IReadOnlyList<LlmProvider> _providerOptions =
		new[] { LlmProvider.OpenAI, LlmProvider.Anthropic };

	private readonly LlmModelConfig _model;

	public LlmModelEntry(LlmModelConfig model)
	{
		_model = model ?? throw new ArgumentNullException(nameof(model));
	}

	internal LlmModelConfig Model => _model;

	public IReadOnlyList<LlmProvider> ProviderOptions => _providerOptions;

	public event PropertyChangedEventHandler? PropertyChanged;

	public string Name
	{
		get => _model.Name;
		set
		{
			string v = value ?? string.Empty;
			if (string.Equals(_model.Name, v, StringComparison.Ordinal)) return;
			_model.Name = v;
			OnPropertyChanged(nameof(Name));
		}
	}

	public LlmProvider Provider
	{
		get => _model.Provider;
		set
		{
			if (_model.Provider == value) return;
			_model.Provider = value;
			OnPropertyChanged(nameof(Provider));
		}
	}

	// Bound to a text box; empty means "no per-model temperature" (null in JSON).
	// An unparseable value leaves the stored temperature unchanged.
	public string TemperatureText
	{
		get => _model.CustomTemperature?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
		set
		{
			string v = (value ?? string.Empty).Trim();
			decimal? parsed;
			if (v.Length == 0)
				parsed = null;
			else if (decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out var d))
				parsed = d;
			else
				return;

			if (_model.CustomTemperature == parsed) return;
			_model.CustomTemperature = parsed;
			OnPropertyChanged(nameof(TemperatureText));
		}
	}

	private void OnPropertyChanged(string propertyName) =>
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
