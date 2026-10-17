using CognitiveSupport;

namespace Mutation.Tests;

public class CutShortMessagesTests
{
	[Fact]
	public void NotCutShort_LeavesTheOutcomeUntouched()
	{
		Assert.Equal("Transcript ready.", CutShortMessages.AppendTo("Transcript ready.", cutShort: false));
	}

	[Fact]
	public void CutShort_AppendsTheNoticeAfterTheOutcome()
	{
		Assert.Equal(
			"Transcript ready. " + CutShortMessages.Notice,
			CutShortMessages.AppendTo("Transcript ready.  ", cutShort: true));
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	public void CutShort_WithNoOutcome_IsJustTheNotice(string outcome)
	{
		Assert.Equal(CutShortMessages.Notice, CutShortMessages.AppendTo(outcome, cutShort: true));
	}

	[Fact]
	public void Notice_SaysTheEndIsMissing()
	{
		Assert.Contains("cut short", CutShortMessages.Notice);
		Assert.Contains("end of the text is missing", CutShortMessages.Notice);
	}
}
