using CognitiveSupport;
using Mutation.Ui;
using static CognitiveSupport.LlmSettings;
using static CognitiveSupport.LlmSettings.TranscriptFormatRule;

namespace Mutation.Tests;

public class TranscriptFormatterTests
{
	private static Settings BuildSettings(LlmSettings? llmSettings = null)
		=> new()
		{
			LlmSettings = llmSettings,
		};

	private static TranscriptFormatRule Rule(string find, string replace, MatchTypeEnum matchType, bool caseSensitive = false)
		=> new TranscriptFormatRule(find, replace, caseSensitive, matchType);

	// ----- Constructor -----

	[Fact]
	public void Ctor_NullSettings_Throws()
	{
		Assert.Throws<ArgumentNullException>(() => new TranscriptFormatter(null!, new StubLlmService("ignored")));
	}

	[Fact]
	public void Ctor_NullLlmService_Throws()
	{
		Assert.Throws<ArgumentNullException>(() => new TranscriptFormatter(BuildSettings(), null!));
	}

	// ----- ApplyRules -----

	[Fact]
	public void ApplyRules_NullTranscript_ReturnsNull()
	{
		var formatter = new TranscriptFormatter(BuildSettings(), new StubLlmService("ignored"));
		string? result = formatter.ApplyRules(null!, manualPunctuation: false);
		Assert.Null(result);
	}

	[Fact]
	public void ApplyRules_ManualPunctuationTrue_StripsPunctuation()
	{
		var formatter = new TranscriptFormatter(BuildSettings(), new StubLlmService("ignored"));
		string result = formatter.ApplyRules("Hello, world. How are you? I am fine!", manualPunctuation: true);

		Assert.DoesNotContain(",", result);
		Assert.DoesNotContain(".", result);
		Assert.DoesNotContain("?", result);
		Assert.DoesNotContain("!", result);
		Assert.Contains("Hello", result);
		Assert.Contains("world", result);
	}

	[Fact]
	public void ApplyRules_ManualPunctuationTrue_StripsEllipsis()
	{
		var formatter = new TranscriptFormatter(BuildSettings(), new StubLlmService("ignored"));
		string result = formatter.ApplyRules("Wait… let me think...", manualPunctuation: true);

		Assert.DoesNotContain("…", result);
		Assert.DoesNotContain("...", result);
		Assert.DoesNotContain(".", result);
	}

	[Fact]
	public void ApplyRules_ManualPunctuationTrue_CollapsesDoubleSpaces()
	{
		var formatter = new TranscriptFormatter(BuildSettings(), new StubLlmService("ignored"));
		// After stripping the comma and period, the surrounding spaces collapse via the "  " → " " replace.
		string result = formatter.ApplyRules("Hello , world . today", manualPunctuation: true);

		Assert.DoesNotContain("  ", result);
	}

	[Fact]
	public void ApplyRules_ManualPunctuationFalse_KeepsPunctuation()
	{
		var formatter = new TranscriptFormatter(BuildSettings(), new StubLlmService("ignored"));
		string result = formatter.ApplyRules("Hello, world.", manualPunctuation: false);

		Assert.Contains(",", result);
		Assert.Contains(".", result);
	}

	[Fact]
	public void ApplyRules_AppliesRulesFromSettings()
	{
		var llm = new LlmSettings();
		llm.TranscriptFormatRules.Add(Rule("foo", "bar", MatchTypeEnum.Plain));
		var formatter = new TranscriptFormatter(BuildSettings(llm), new StubLlmService("ignored"));

		string result = formatter.ApplyRules("foo baz", manualPunctuation: false);
		Assert.Equal("bar baz", result);
	}

	[Fact]
	public void ApplyRules_AppliesRulesInOrder()
	{
		var llm = new LlmSettings();
		llm.TranscriptFormatRules.Add(Rule("a", "b", MatchTypeEnum.Plain));
		llm.TranscriptFormatRules.Add(Rule("b", "c", MatchTypeEnum.Plain));
		var formatter = new TranscriptFormatter(BuildSettings(llm), new StubLlmService("ignored"));

		Assert.Equal("c", formatter.ApplyRules("a", manualPunctuation: false));
	}

	[Fact]
	public void ApplyRules_NullLlmSettings_FallsBackToEmptyRules()
	{
		var formatter = new TranscriptFormatter(BuildSettings(llmSettings: null), new StubLlmService("ignored"));
		string result = formatter.ApplyRules("hello world", manualPunctuation: false);
		Assert.Equal("hello world", result);
	}

	[Fact]
	public void ApplyRules_RunsCleanupPunctuationLast()
	{
		var formatter = new TranscriptFormatter(BuildSettings(), new StubLlmService("ignored"));
		// Doubled punctuation between words should be deduped by CleanupPunctuation step
		string result = formatter.ApplyRules("hello.. world", manualPunctuation: false);
		Assert.Equal("hello. world", result);
	}

	// ----- FormatWithLlmAsync -----

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	public async Task FormatWithLlmAsync_NullEmptyOrWhitespace_ReturnsInputUnchanged(string? input)
	{
		var stub = new StubLlmService("should-not-be-called");
		var formatter = new TranscriptFormatter(BuildSettings(), stub);

		string? result = await formatter.FormatWithLlmAsync(input!, "system prompt", "gpt-4");

		Assert.Equal(input, result);
		Assert.Equal(0, stub.CallCount);
	}

	[Fact]
	public async Task FormatWithLlmAsync_BuildsSystemAndUserMessages()
	{
		var stub = new StubLlmService("formatted output");
		var formatter = new TranscriptFormatter(BuildSettings(), stub);

		await formatter.FormatWithLlmAsync("hello world", "you are a formatter", "gpt-4");

		Assert.Equal(1, stub.CallCount);
		Assert.NotNull(stub.LastMessages);
		Assert.Equal(2, stub.LastMessages!.Count);
		Assert.Equal(LlmChatRole.System, stub.LastMessages[0].Role);
		Assert.Equal("you are a formatter", stub.LastMessages[0].Content);
		Assert.Equal(LlmChatRole.User, stub.LastMessages[1].Role);
		Assert.Contains("Reformat the following transcript:", stub.LastMessages[1].Content);
		Assert.Contains("hello world", stub.LastMessages[1].Content);
		Assert.Equal("gpt-4", stub.LastModel);
	}

	[Fact]
	public async Task FormatWithLlmAsync_RunsFixNewLinesOnResult()
	{
		var stub = new StubLlmService("line1\r\nline2\rline3\nline4");
		var formatter = new TranscriptFormatter(BuildSettings(), stub);

		string result = await formatter.FormatWithLlmAsync("input", "system", "model");

		string expected = string.Join(Environment.NewLine, new[] { "line1", "line2", "line3", "line4" });
		Assert.Equal(expected, result);
	}

	private sealed class StubLlmService : ILlmService
	{
		private readonly string _response;

		public StubLlmService(string response)
		{
			_response = response;
		}

		public int CallCount { get; private set; }
		public IList<LlmChatMessage>? LastMessages { get; private set; }
		public string? LastModel { get; private set; }

		public Task<string> CreateChatCompletion(IList<LlmChatMessage> messages, string llmModelName, decimal temperature = 0.7M)
		{
			CallCount++;
			LastMessages = messages;
			LastModel = llmModelName;
			return Task.FromResult(_response);
		}
	}
}
