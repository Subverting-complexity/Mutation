namespace CognitiveSupport;

/// <summary>
/// Which side of a dictated symbol the words around it close up against.
/// </summary>
public enum SymbolAttachment
{
	/// <summary>
	/// Hugs the word on its left and leaves the gap on its right — "fifty percent last year"
	/// wants "fifty% last year", not "fifty %last year".
	/// </summary>
	Left,

	/// <summary>
	/// Hugs the word on its right and leaves the gap on its left — "issue hash 42" wants
	/// "issue #42", not "issue# 42".
	/// </summary>
	Right,

	/// <summary>
	/// Hugs both, welding its neighbours together — "state dash of dash the dash art" wants
	/// "state-of-the-art". Also the answer for anything unrecognised, because closing both
	/// sides is what a Smart rule has always done with a symbol.
	/// </summary>
	Both,
}

/// <summary>
/// Works out which side a symbol replacement attaches to, from the symbol itself.
/// <para>
/// Kept separate from the formatter because it is a question about punctuation rather than
/// about matching, and because the classes are the part worth reading and testing on their
/// own: get a symbol into the wrong class and the spacing is wrong in every transcript that
/// uses that rule.
/// </para>
/// </summary>
public static class SymbolAttachments
{
	/// <summary>
	/// Punctuation that finishes something and so sits hard against the word it finishes.
	/// <para>
	/// The quote characters that are left out are left out on purpose. The straight <c>"</c>
	/// and <c>'</c> do not say which end of a quotation they are. Neither, in practice, does
	/// the curly <c>’</c>: it is the right single quotation mark and the typographic
	/// apostrophe, the same character, so classifying it as closing would turn a perfectly
	/// ordinary <c>apostrophe</c> → <c>’</c> rule into "John’ s". All three fall through to
	/// <see cref="SymbolAttachment.Both"/>, which is the behaviour they have always had.
	/// </para>
	/// </summary>
	private const string ClosingSymbols = ".,;:!?)]}%”»";

	/// <summary>Punctuation that starts something and so leans onto what follows it.</summary>
	private const string OpeningSymbols = "([{$#‘“«";

	/// <summary>
	/// Classifies a replacement by its first meaningful character. Leading whitespace is
	/// skipped so a replacement that supplies its own spacing — "\r\n- " — is still judged
	/// on the punctuation it carries.
	/// </summary>
	public static SymbolAttachment Classify(string? replaceWith)
	{
		if (string.IsNullOrEmpty(replaceWith))
			return SymbolAttachment.Both;

		foreach (char candidate in replaceWith)
		{
			if (char.IsWhiteSpace(candidate))
				continue;

			if (ClosingSymbols.Contains(candidate))
				return SymbolAttachment.Left;

			if (OpeningSymbols.Contains(candidate))
				return SymbolAttachment.Right;

			return SymbolAttachment.Both;
		}

		return SymbolAttachment.Both;
	}
}
