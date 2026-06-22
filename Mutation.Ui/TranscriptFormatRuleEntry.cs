using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.RegularExpressions;
using CognitiveSupport;
using Microsoft.UI.Xaml;
using static CognitiveSupport.TranscriptFormatRule;

namespace Mutation.Ui;

// Row wrapper for a single TranscriptFormatRule, mirroring LlmModelEntry.
// Wraps the live rule so two-way edits flow straight into the working-copy list.
public sealed class TranscriptFormatRuleEntry : INotifyPropertyChanged
{
	private static readonly IReadOnlyList<MatchTypeEnum> _matchTypeOptions =
		new[] { MatchTypeEnum.Plain, MatchTypeEnum.RegEx, MatchTypeEnum.Smart };

	private readonly TranscriptFormatRule _rule;
	private string? _regexError;

	public TranscriptFormatRuleEntry(TranscriptFormatRule rule)
	{
		_rule = rule ?? throw new ArgumentNullException(nameof(rule));
		ValidateRegex();
	}

	internal TranscriptFormatRule Rule => _rule;

	public IReadOnlyList<MatchTypeEnum> MatchTypeOptions => _matchTypeOptions;

	public event PropertyChangedEventHandler? PropertyChanged;

	public string Find
	{
		get => _rule.Find ?? string.Empty;
		set
		{
			string v = value ?? string.Empty;
			if (string.Equals(_rule.Find, v, StringComparison.Ordinal)) return;
			_rule.Find = v;
			OnPropertyChanged(nameof(Find));
			ValidateRegex();
		}
	}

	public string ReplaceWith
	{
		get => _rule.ReplaceWith ?? string.Empty;
		set
		{
			string v = value ?? string.Empty;
			if (string.Equals(_rule.ReplaceWith, v, StringComparison.Ordinal)) return;
			_rule.ReplaceWith = v;
			OnPropertyChanged(nameof(ReplaceWith));
		}
	}

	public bool CaseSensitive
	{
		get => _rule.CaseSensitive;
		set
		{
			if (_rule.CaseSensitive == value) return;
			_rule.CaseSensitive = value;
			OnPropertyChanged(nameof(CaseSensitive));
		}
	}

	public MatchTypeEnum MatchType
	{
		get => _rule.MatchType;
		set
		{
			if (_rule.MatchType == value) return;
			_rule.MatchType = value;
			OnPropertyChanged(nameof(MatchType));
			ValidateRegex();
		}
	}

	// Inline validation: only RegEx (and the regex-backed Smart) patterns can be malformed.
	public string? RegexError => _regexError;

	public bool HasRegexError => !string.IsNullOrEmpty(_regexError);

	public Visibility RegexErrorVisibility => HasRegexError ? Visibility.Visible : Visibility.Collapsed;

	private void ValidateRegex()
	{
		string? error = null;
		if (_rule.MatchType == MatchTypeEnum.RegEx && !string.IsNullOrEmpty(_rule.Find))
		{
			try
			{
				_ = new Regex(_rule.Find);
			}
			catch (ArgumentException ex)
			{
				error = ex.Message;
			}
		}

		if (string.Equals(_regexError, error, StringComparison.Ordinal)) return;
		_regexError = error;
		OnPropertyChanged(nameof(RegexError));
		OnPropertyChanged(nameof(HasRegexError));
		OnPropertyChanged(nameof(RegexErrorVisibility));
	}

	private void OnPropertyChanged(string propertyName) =>
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
