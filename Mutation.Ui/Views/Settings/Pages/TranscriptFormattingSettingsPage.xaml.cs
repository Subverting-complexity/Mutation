using System.Collections.Generic;
using System.Collections.ObjectModel;
using CognitiveSupport;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Mutation.Ui.Views.SettingsUi.Pages;

public sealed partial class TranscriptFormattingSettingsPage : UserControl
{
	private readonly Settings _settings;

	public ObservableCollection<TranscriptFormatRuleEntry> RuleEntries { get; } = new();

	public TranscriptFormattingSettingsPage(Settings settings)
	{
		_settings = settings;
		InitializeComponent();
		LoadValues();
	}

	private List<TranscriptFormatRule> Rules =>
		_settings.TranscriptFormatRules ??= new List<TranscriptFormatRule>();

	private void LoadValues()
	{
		RuleEntries.Clear();
		foreach (var rule in Rules)
			RuleEntries.Add(new TranscriptFormatRuleEntry(rule));
	}

	private void BtnAddRule_Click(object sender, RoutedEventArgs e)
	{
		var rule = new TranscriptFormatRule();
		Rules.Add(rule);
		RuleEntries.Add(new TranscriptFormatRuleEntry(rule));
	}

	private void RuleDelete_Click(object sender, RoutedEventArgs e)
	{
		if (sender is not Button { Tag: TranscriptFormatRuleEntry entry }) return;
		Rules.Remove(entry.Rule);
		RuleEntries.Remove(entry);
	}

	private void RuleMoveUp_Click(object sender, RoutedEventArgs e)
	{
		if (sender is not Button { Tag: TranscriptFormatRuleEntry entry }) return;
		int index = RuleEntries.IndexOf(entry);
		if (index <= 0) return;
		MoveRule(index, index - 1);
	}

	private void RuleMoveDown_Click(object sender, RoutedEventArgs e)
	{
		if (sender is not Button { Tag: TranscriptFormatRuleEntry entry }) return;
		int index = RuleEntries.IndexOf(entry);
		if (index < 0 || index >= RuleEntries.Count - 1) return;
		MoveRule(index, index + 1);
	}

	// Keep the backing list and the displayed collection in lockstep so the saved
	// order matches what the user sees; TranscriptFormatter applies rules in list order.
	private void MoveRule(int fromIndex, int toIndex)
	{
		var rules = Rules;
		var rule = rules[fromIndex];
		rules.RemoveAt(fromIndex);
		rules.Insert(toIndex, rule);
		RuleEntries.Move(fromIndex, toIndex);
	}
}
