using CognitiveSupport.Extensions;
using System.Text.RegularExpressions;
using static CognitiveSupport.LlmSettings;
using static CognitiveSupport.LlmSettings.TranscriptFormatRule;

namespace CognitiveSupport
{
	public static class TextFormatter
	{
		public static string FormatWithRules(
			this string text,
			List<LlmSettings.TranscriptFormatRule> rules)
		{
			if (text is null) return text;
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
			if (text is null) return text;

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
				return line;

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
			if (text is null) return text;
			if (rule is null) throw new ArgumentNullException(nameof(rule));

			RegexOptions regexOptions = RegexOptions.None;
			if (!rule.CaseSensitive)
				regexOptions = RegexOptions.IgnoreCase;

			switch (rule.MatchType)
			{
				case MatchTypeEnum.Plain:
					var comparison = StringComparison.InvariantCultureIgnoreCase;
					if (rule.CaseSensitive)
						comparison = StringComparison.InvariantCulture;

					text = text.Replace(rule.Find, rule.ReplaceWith, comparison);
					break;
				case MatchTypeEnum.RegEx:
					text = Regex.Replace(text, rule.Find, rule.ReplaceWith, regexOptions);
					break;
				case MatchTypeEnum.Smart:
					string pattern = $@"(\b|^)([.,]?)([ ]*{rule.Find}[.,]?[ ]*)(\b|$)";
					string replacement = $"$1$2{rule.ReplaceWith}$4";
					text = Regex.Replace(text, pattern, replacement, regexOptions);

					break;
				default:
					throw new NotImplementedException($"The MatchType {rule.MatchType}: {(int)rule.MatchType} is not implemented.");
			}

			return text;
		}

		public static string RemoveSubstrings(
			this string text,
			params string[] substringsToRemove)
		{
			if (text is null) return text;
			if (substringsToRemove is null || !substringsToRemove.Any())
				throw new ArgumentNullException(nameof(substringsToRemove));

			foreach (var substring in substringsToRemove)
			{
				text = text.Replace(substring, "");
			}

			return text;
		}
	}
}
