using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using CognitiveSupport;

namespace Mutation.Ui;

public sealed class LlmModelEntry : INotifyPropertyChanged
{
	private static readonly IReadOnlyList<LlmProvider> _providerOptions =
		new[] { LlmProvider.OpenAI, LlmProvider.Anthropic };

	private readonly LlmModelConfig _model;

	/// <summary>
	/// True for a model added on this page whose levels the user has not set yet. Its
	/// levels then follow its service: a change of service ticks the new service's full
	/// list, so a new model never ends up with half a list it did not choose.
	/// </summary>
	private bool _levelsFollowService;

	public LlmModelEntry(LlmModelConfig model)
	{
		_model = model ?? throw new ArgumentNullException(nameof(model));
	}

	/// <summary>
	/// A model the user is adding, with its service's full list of reasoning levels
	/// ticked, the same as starter models and models upgraded from an older settings file.
	/// The list is set here rather than in <see cref="LlmModelConfig"/>, which must start
	/// empty because the settings dialog copies settings through JSON.
	/// </summary>
	public static LlmModelEntry CreateNew()
	{
		var model = new LlmModelConfig();
		model.ReasoningLevels = ReasoningLevelCatalog.FullList(model.Provider).ToList();
		return new LlmModelEntry(model) { _levelsFollowService = true };
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
			OnReasoningLevelsChanged();
		}
	}

	public LlmProvider Provider
	{
		get => _model.Provider;
		set
		{
			if (_model.Provider == value) return;
			_model.Provider = value;
			if (_levelsFollowService)
				_model.ReasoningLevels = ReasoningLevelCatalog.FullList(value).ToList();
			OnPropertyChanged(nameof(Provider));
			OnReasoningLevelsChanged();
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

	/// <summary>
	/// The reasoning levels button's text, for example "Reasoning levels: Low to Max".
	/// Only levels the model's current service offers count, as only those are sent.
	/// </summary>
	public string ReasoningLevelsText =>
		$"Reasoning levels: {ReasoningLevelSummary.Describe(_model.Provider, _model.ReasoningLevels)}";

	/// <summary>
	/// The button's accessible name. The visible text alone would read the same on
	/// every row, so a screen reader user would not know which model it belongs to.
	/// </summary>
	public string ReasoningLevelsAccessibleName =>
		$"Reasoning levels for {ModelLabel}: {ReasoningLevelSummary.Describe(_model.Provider, _model.ReasoningLevels)}";

	/// <summary>The heading of the picker the button opens.</summary>
	public string ReasoningLevelsHeading => $"Reasoning levels for {ModelLabel}";

	private string ModelLabel =>
		string.IsNullOrWhiteSpace(_model.Name) ? "unnamed model" : _model.Name.Trim();

	/// <summary>
	/// One choice for each level in the model's current service list, lightest first,
	/// ticked where the model has it ticked. A model moved to the other service shows
	/// that service's list; levels both services share stay ticked.
	/// </summary>
	public IReadOnlyList<ReasoningLevelChoice> GetReasoningLevelChoices()
	{
		var ticked = new HashSet<ReasoningLevel>(_model.ReasoningLevels ?? new List<ReasoningLevel>());
		return ReasoningLevelCatalog.FullList(_model.Provider)
			.Select(level => new ReasoningLevelChoice(level, ReasoningLevelCatalog.DisplayName(level), ticked.Contains(level)))
			.ToList();
	}

	/// <summary>
	/// Replaces the model's ticked levels with <paramref name="ticked"/>, kept in the
	/// service's order, each once, and only those the service offers. A level from the
	/// other service's list, left over from before the model moved, is dropped here,
	/// because the picker did not show it and the request code would ignore it.
	/// </summary>
	/// <param name="ticked">The levels ticked in the picker.</param>
	public void SetReasoningLevels(IEnumerable<ReasoningLevel> ticked)
	{
		ArgumentNullException.ThrowIfNull(ticked);
		var levels = ReasoningLevelCatalog.InServiceOrder(_model.Provider, ticked).ToList();

		// The user chose these levels, so a later change of service must not replace them.
		_levelsFollowService = false;

		// A new list rather than editing the old one, so nothing else holding the old
		// list sees it change underneath it.
		_model.ReasoningLevels = levels;
		OnReasoningLevelsChanged();
	}

	private void OnReasoningLevelsChanged()
	{
		OnPropertyChanged(nameof(ReasoningLevelsText));
		OnPropertyChanged(nameof(ReasoningLevelsAccessibleName));
		OnPropertyChanged(nameof(ReasoningLevelsHeading));
	}

	private void OnPropertyChanged(string propertyName) =>
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
