using System.Runtime.Serialization;
using CognitiveSupport.Extensions;

namespace Mutation.Tests;

public class EnumExtensionsTests
{
	private enum SampleEnum
	{
		[EnumMember(Value = "first-value")]
		First,

		[EnumMember(Value = "second-value")]
		Second,

		Plain,
	}

	[Fact]
	public void ToEnumMemberValue_WithEnumMemberAttribute_ReturnsAttributeValue()
	{
		Assert.Equal("first-value", SampleEnum.First.ToEnumMemberValue());
		Assert.Equal("second-value", SampleEnum.Second.ToEnumMemberValue());
	}

	[Fact]
	public void ToEnumMemberValue_WithoutEnumMemberAttribute_ReturnsEnumName()
	{
		Assert.Equal("Plain", SampleEnum.Plain.ToEnumMemberValue());
	}

	[Fact]
	public void ToEnumMemberValue_UndefinedValue_Throws()
	{
		var undefined = (SampleEnum)9999;
		Assert.Throws<ArgumentException>(() => undefined.ToEnumMemberValue());
	}
}
