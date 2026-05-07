namespace CognitiveSupport.Extensions;

public static class StringExtensions
{
	public static string FixNewLines(
		this string text)
	{
		if (text is null) return text!;

		// Replace isolated "\r" with "\n"
		text = text.Replace("\r\n", "\n");
		text = text.Replace("\r", "\n");

		text = text.Replace("\n", Environment.NewLine);

		return text;
	}

}
