using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;

namespace Mutation.Ui;

/// <summary>
/// One entry in the third-party interaction dropdown: the option, paired with the human
/// wording for it.
/// <para>
/// The dropdown used to be bound to the bare enum, so a screen reader read the identifier out
/// — "DoNotInsert", "SendKeys" — while the sentence that actually explains each choice sat
/// unread in a <see cref="DescriptionAttribute"/> nothing looked at (issue #243).
/// </para>
/// </summary>
public sealed record DictationInsertOptionItem(DictationInsertOption Option, string Description)
{
	/// <summary>
	/// What a reader falls back to if it is ever handed the item rather than the bound
	/// display property, which is the failure this type exists to prevent.
	/// </summary>
	public override string ToString() => Description;

	/// <summary>Every option, in declaration order, ready to bind.</summary>
	public static IReadOnlyList<DictationInsertOptionItem> All() =>
		Enum.GetValues<DictationInsertOption>()
			.Select(option => new DictationInsertOptionItem(option, Describe(option)))
			.ToList();

	/// <summary>
	/// The option's description, falling back to its identifier. An option added later
	/// without a description reads badly, but it still reads — which beats an empty row in
	/// a list a blind user is navigating by ear.
	/// </summary>
	public static string Describe(DictationInsertOption option)
	{
		FieldInfo? field = typeof(DictationInsertOption).GetField(option.ToString());
		string? description = field?.GetCustomAttribute<DescriptionAttribute>()?.Description;

		return string.IsNullOrWhiteSpace(description)
			? option.ToString()
			: description;
	}
}
