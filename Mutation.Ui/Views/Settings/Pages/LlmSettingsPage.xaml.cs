using System.Collections.ObjectModel;
using CognitiveSupport;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Mutation.Ui.Views.SettingsUi.Pages;

public sealed partial class LlmSettingsPage : UserControl
{
	private readonly Settings _settings;
	private bool _suppressEvents;

	public ObservableCollection<LlmModelEntry> ModelEntries { get; } = new();

	public LlmSettingsPage(Settings settings)
	{
		_settings = settings;
		InitializeComponent();
		LoadValues();
	}

	private void LoadValues()
	{
		_suppressEvents = true;
		try
		{
			var llm = _settings.LlmSettings ??= new LlmSettings();
			NbTimeout.Value = llm.TimeoutSeconds > 0 ? llm.TimeoutSeconds : SettingsDefaults.Llm.TimeoutSeconds;
			NbRetryCount.Value = llm.RetryCount >= 0 ? llm.RetryCount : SettingsDefaults.Llm.RetryCount;

			ModelEntries.Clear();
			foreach (var model in llm.Models)
				ModelEntries.Add(new LlmModelEntry(model));
		}
		finally { _suppressEvents = false; }
	}

	private void BtnAddModel_Click(object sender, RoutedEventArgs e)
	{
		var model = new LlmModelConfig();
		(_settings.LlmSettings ??= new LlmSettings()).Models.Add(model);
		ModelEntries.Add(new LlmModelEntry(model));
	}

	private void ModelDelete_Click(object sender, RoutedEventArgs e)
	{
		if (sender is not Button { Tag: LlmModelEntry entry }) return;
		_settings.LlmSettings?.Models.Remove(entry.Model);
		ModelEntries.Remove(entry);
	}

	private void NbTimeout_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
	{
		if (_suppressEvents) return;
		if (double.IsNaN(args.NewValue)) return;
		(_settings.LlmSettings ??= new LlmSettings()).TimeoutSeconds = (int)args.NewValue;
	}

	private void NbRetryCount_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
	{
		if (_suppressEvents) return;
		if (double.IsNaN(args.NewValue)) return;
		(_settings.LlmSettings ??= new LlmSettings()).RetryCount = (int)args.NewValue;
	}

	private void BtnResetTimeout_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
		NbTimeout.Value = SettingsDefaults.Llm.TimeoutSeconds;

	private void BtnResetRetryCount_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
		NbRetryCount.Value = SettingsDefaults.Llm.RetryCount;
}
