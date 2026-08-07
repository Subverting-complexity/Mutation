using CognitiveSupport.Extensions;
using System.Text.RegularExpressions;
using static CognitiveSupport.TranscriptFormatRule;

namespace CognitiveSupport;

public static class TextFormatter
{
	public static string FormatWithRules(
		this string text,
		List<TranscriptFormatRule> rules)
	{
		if (text is null) return text!;
		if (rules is null) throw new ArgumentNullException(nameof(rules));

		text = text.FixNewLines();

		foreach (var rule in rules)
			text = FormatWithRule(text, rule);

		string[] lines = text.Split(Environment.NewLine, StringSplitOptions.TrimEntries);
		lines = CleanLines(lines);
		text = string.Join(Environment.NewLine, lines);

		return text;
	}

	public static string CleanupPunctuation(
		this string text)
	{
		if (text is null) return text!;

		string[] deduplications = new[]
		{
			".",
			",",
			"?",
			"!",
			":",
			";",
		};

		text = text.FixNewLines();
		int counter = 0;
		var lines = text.Split(Environment.NewLine, StringSplitOptions.TrimEntries)
			.ToDictionary(k => counter++, v => v);

		foreach (var dup in deduplications)
		{
			// Replace a doubled-punctuation cluster (optionally separated by space, ".", ",",
			// or another copy of the dup char) with a single instance of `dup`. The cluster
			// must follow a word char and be followed by a word char, whitespace, or end-of-line.
			// A trailing space is appended only when the next char is a word char — otherwise
			// (whitespace or end-of-line) we leave the existing context untouched so the
			// final period in "Hello, world." stays "Hello, world." rather than "Hello, world. ".
			string pattern = @$"(?<=\w)[.,]?[{dup}][ ,.{dup}]?[{dup}]?(?=\w|\s|$)";

			foreach (int key in lines.Keys)
			{
				string line = lines[key];
				lines[key] = Regex.Replace(line, pattern, match =>
				{
					int after = match.Index + match.Length;
					bool nextIsWord = after < line.Length && char.IsLetterOrDigit(line[after]);
					return nextIsWord ? $"{dup} " : $"{dup}";
				});
			}
		}

		text = string.Join(Environment.NewLine, lines.Values);

		return text;
	}

	public static string[] CleanLines(
		this string[] input)
	{
		List<string> output = new(input.Length);
		foreach (string inLine in input)
		{
			string outLine = CleanLine(inLine);
			output.Add(outLine);
		}
		return output.ToArray();
	}

	private static string CleanLine(
		string line)
	{
		if (line is null)
			return line!;

		string output = line.Trim();

		output = Regex.Replace(output, "^[,.;:]\\s+", string.Empty);

		output = Regex.Replace(output, "^-\\s*[,.;:]\\s*", "- ");

		output = output.Replace(", : ,", ":")
					 .Replace(". : .", ":")
					 .Replace(". : ,", ":")
					 .Replace(", : .", ":");

		return output;
	}

	public static string FormatWithRule(
		string text,
		TranscriptFormatRule rule)
	{
		if (text is null) return text!;
		if (rule is null) throw new ArgumentNullException(nameof(rule));
		if (rule.Find is null) return text;

		RegexOptions regexOptions = RegexOptions.None;
		if (!rule.CaseSensitive)
			regexOptions = RegexOptions.IgnoreCase;

		string find = rule.Find;
		string replaceWith = rule.ReplaceWith ?? string.Empty;

		switch (rule.MatchType)
		{
			case MatchTypeEnum.Plain:
				var comparison = StringComparison.InvariantCultureIgnoreCase;
				if (rule.CaseSensitive)
					comparison = StringComparison.InvariantCulture;

				text = text.Replace(find, replaceWith, comparison);
				break;
			case MatchTypeEnum.RegEx:
				text = Regex.Replace(text, find, replaceWith, regexOptions);
				break;
			case MatchTypeEnum.Smart:
				text = ApplySmartRule(text, find, replaceWith, regexOptions);
				break;
			default:
				throw new NotImplementedException($"The MatchType {rule.MatchType}: {(int)rule.MatchType} is not implemented.");
		}

		return text;
	}

	/// <summary>
	/// Applies a Smart rule: a whole-word match on literal text, unlike the RegEx match type.
	/// <para>
	/// "Literal" is the whole point of the mode, so <c>Find</c> is escaped before it reaches
	/// the engine — a rule finding <c>C++</c> used to compile to a nested quantifier and throw,
	/// which aborted the entire formatting run, not just that one rule. <c>ReplaceWith</c> goes
	/// in through a match evaluator for the same reason: a replacement containing <c>$1</c> is
	/// the literal text the user typed, not a capture group.
	/// </para>
	/// <para>
	/// The spacing around the match is captured rather than swallowed, so <c>ai</c> → <c>AI</c>
	/// turns "contain ai also" into "contain AI also" instead of welding it into "containAIalso".
	/// It is emitted back only where the replacement does not supply its own, because the shipped
	/// rules are mostly dictated punctuation — "full stop" → ". " — where re-emitting the original
	/// gap would push the period away from the word it belongs to.
	/// </para>
	/// </summary>
	private static string ApplySmartRule(
		string text,
		string find,
		string replaceWith,
		RegexOptions regexOptions)
	{
		string pattern = $@"(\b|^)([.,]?)([ ]*)({Regex.Escape(find)})([.,]?)([ ]*)(\b|$)";

		bool opensAttached = OpensAttachedToPrecedingWord(replaceWith);

		// A replacement closing with whitespace already separates itself from what follows, and
		// carries its own terminator — so the punctuation the match consumed on that side goes
		// with the gap rather than being stranded after it.
		bool closesSeparated = replaceWith.Length > 0
			&& char.IsWhiteSpace(replaceWith[^1]);

		return Regex.Replace(text, pattern, match =>
		{
			string leadingPunctuation = match.Groups[2].Value;
			string leadingSpaces = match.Groups[3].Value;
			string trailingPunctuation = match.Groups[5].Value;
			string trailingSpaces = match.Groups[6].Value;

			string head = opensAttached ? string.Empty : leadingSpaces;
			string tail = closesSeparated ? string.Empty : trailingPunctuation + trailingSpaces;

			return leadingPunctuation + head + replaceWith + tail;
		}, regexOptions);
	}

	/// <summary>
	/// Whether the replacement wants to sit hard against the word before it, rather than after
	/// the gap the match consumed. That is true of a replacement opening with its own spacing,
	/// and of one that is nothing but sentence punctuation and the spacing after it — the
	/// dictated "full stop" → ". ". A replacement that merely starts with punctuation and then
	/// carries text — "dotnet" → ".NET" — is a word, and still needs the gap.
	/// An empty replacement deletes a word, and takes the gap on this side with it so the two
	/// neighbours end up one space apart rather than none.
	/// </summary>
	private static bool OpensAttachedToPrecedingWord(string replaceWith)
	{
		int index = 0;
		while (index < replaceWith.Length && IsSentencePunctuation(replaceWith[index]))
			index++;

		return index == replaceWith.Length || char.IsWhiteSpace(replaceWith[index]);
	}

	private static bool IsSentencePunctuation(char value) =>
		value is '.' or ',' or ';' or ':' or '!' or '?';

	public static string RemoveSubstrings(
		this string text,
		params string[] substringsToRemove)
	{
		if (text is null) return text!;
		if (substringsToRemove is null || !substringsToRemove.Any())
			throw new ArgumentNullException(nameof(substringsToRemove));

		foreach (var substring in substringsToRemove)
		{
			text = text.Replace(substring, "");
		}

		return text;
	}
}
