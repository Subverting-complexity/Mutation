namespace CognitiveSupport;

public enum LlmChatRole
{
	System,
	User,
	Assistant
}

public class LlmChatMessage
{
	public LlmChatRole Role { get; }
	public string Content { get; }

	public LlmChatMessage(LlmChatRole role, string content)
	{
		Role = role;
		Content = content;
	}
}
