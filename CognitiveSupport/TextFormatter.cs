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
	/// The match also consumes the spacing either side of it, and what happens to that spacing
	/// depends on what the replacement is. There are three kinds, and conflating them is what
	/// the old single rule got wrong.
	/// </para>
	/// <list type="bullet">
	/// <item>A <b>word</b> — "ai" → "AI" — needs the gap back, or the neighbours weld into
	/// "containAIalso".</item>
	/// <item>A <b>symbol</b> — "full stop" → ". ", "dash" → "-" — supplies its own spacing or
	/// deliberately has none, so the gap goes. Every rule Mutation ships is one of these.</item>
	/// <item>A <b>deletion</b> — "um" → "" — closes the gap to a single space, however many
	/// copies of the word were said in a row.</item>
	/// </list>
	/// </summary>
	private static string ApplySmartRule(
		string text,
		string find,
		string replaceWith,
		RegexOptions regexOptions)
	{
		string pattern = $@"(\b|^)([.,]?)([ ]*)({Regex.Escape(find)})([.,]?)([ ]*)(\b|$)";

		// Letters and digits are what makes a replacement a word rather than punctuation. A
		// replacement without any is spacing-neutral by construction — "dash" → "-" wants
		// "state-of-the-art", not "state - of - the - art" — and dropping the gap for it is
		// also exactly what this rule did before it learned to keep spacing at all, so no
		// existing rule can be made worse by this branch.
		bool isSymbol = replaceWith.Length > 0 && !replaceWith.Any(char.IsLetterOrDigit);
		bool isDeletion = replaceWith.Length == 0;
		bool opensAttached = OpensAttachedToPrecedingWord(replaceWith);

		// A replacement closing with whitespace already separates itself from what follows, and
		// carries its own terminator — so the punctuation the match consumed on that side goes
		// with the gap rather than being stranded after it.
		bool closesSeparated = replaceWith.Length > 0
			&& char.IsWhiteSpace(replaceWith[^1]);

		// Deletions are the one branch that has to look outside its own match. Fillers arrive in
		// runs — "um, um, I think" — and consecutive matches abut, so the second one's preceding
		// word is whatever the first one left behind, which may be nothing. Regex.Replace
		// evaluates matches left to right, so carrying that forward is enough.
		int previousEnd = -1;
		bool wordPrecedes = false;

		return Regex.Replace(text, pattern, match =>
		{
			string leadingPunctuation = match.Groups[2].Value;
			string leadingSpaces = match.Groups[3].Value;
			string trailingPunctuation = match.Groups[5].Value;
			string trailingSpaces = match.Groups[6].Value;

			if (isSymbol)
				return leadingPunctuation + replaceWith;

			if (!isDeletion)
			{
				string head = opensAttached ? string.Empty : leadingSpaces;
				string tail = closesSeparated ? string.Empty : trailingPunctuation + trailingSpaces;

				return leadingPunctuation + head + replaceWith + tail;
			}

			int end = match.Index + match.Length;
			bool hasWordBefore = match.Index == previousEnd
				? wordPrecedes
				: match.Index > 0 && char.IsLetterOrDigit(text[match.Index - 1]);

			string deleted;
			if (trailingPunctuation.Length > 0 && hasWordBefore)
				// The deleted word was carrying the sentence's punctuation. That belongs hard
				// against the word before it, and takes the place of the gap rather than
				// following it. With no word before it there is nothing to attach to, so it
				// goes — which is what keeps a line opening "um, um, I think" from starting
				// with a stray comma.
				deleted = trailingPunctuation + trailingSpaces;
			else if (end < text.Length && char.IsWhiteSpace(text[end]))
				// The text resumes with a space of its own; ours would double it.
				deleted = string.Empty;
			else
				// Keep the gap that was in front of the deleted word. A second filler straight
				// after the first has none left to keep, which is how a run of them closes to
				// one space rather than one space per word.
				deleted = leadingSpaces;

			string emitted = leadingPunctuation + deleted;
			previousEnd = end;
			wordPrecedes = emitted.Length == 0 && hasWordBefore;

			return emitted;
		}, regexOptions);
	}

	/// <summary>
	/// Whether a word replacement still wants to sit hard against the word before it. True of
	/// one opening with its own spacing — "newline plus text" → "\r\nHere". A replacement that
	/// merely starts with punctuation and then carries text — "dotnet" → ".NET" — is a word,
	/// and still needs the gap.
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
