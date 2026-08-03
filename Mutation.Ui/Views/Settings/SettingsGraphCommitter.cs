using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace Mutation.Ui.Views.SettingsUi;

/// <summary>
/// Copies an object graph onto an existing one property-by-property, reusing the
/// destination's sub-objects and list instances instead of swapping references.
///
/// This replaces the hand-maintained field list the Settings dialog used to commit
/// with. That list silently dropped every property nobody remembered to add to it —
/// which is how the seven speech-preprocessing toggles (issue #218) and the prompt
/// list (issue #219) both ended up being discarded on Save. Reflection cannot drift
/// away from the model the way a hand-written list can.
/// </summary>
internal static class SettingsGraphCommitter
{
	public static void Commit(object destination, object source)
	{
		ArgumentNullException.ThrowIfNull(destination);
		ArgumentNullException.ThrowIfNull(source);

		CopyProperties(destination, source, new HashSet<object>(ReferenceEqualityComparer.Instance));
	}

	private static void CopyProperties(object destination, object source, HashSet<object> visited)
	{
		if (!visited.Add(source))
			return; // Already committed on this walk; a cycle would otherwise recurse forever.

		foreach (var property in destination.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
		{
			if (property.GetIndexParameters().Length > 0)
				continue;
			if (!property.CanRead || property.GetMethod is null || !property.GetMethod.IsPublic)
				continue;

			var sourceProperty = source.GetType().GetProperty(property.Name, BindingFlags.Public | BindingFlags.Instance);
			if (sourceProperty is null || !sourceProperty.CanRead)
				continue;

			object? sourceValue = sourceProperty.GetValue(source);
			object? destinationValue = property.GetValue(destination);

			// A collection with no setter can still be committed by refilling it.
			if (TryCommitList(destinationValue, sourceValue, visited))
				continue;

			if (!property.CanWrite || property.SetMethod is null || !property.SetMethod.IsPublic)
				continue;

			// Reuse the destination's instance when both sides hold the same concrete
			// type, so references held elsewhere in the app keep pointing at live data.
			if (sourceValue is not null
				&& destinationValue is not null
				&& !IsDirectlyAssignable(sourceValue.GetType())
				&& destinationValue.GetType() == sourceValue.GetType())
			{
				CopyProperties(destinationValue, sourceValue, visited);
				continue;
			}

			property.SetValue(destination, sourceValue);
		}
	}

	/// <summary>
	/// Refills <paramref name="destinationValue"/> in place when both sides are
	/// non-array lists, preserving the list instance and — index by index — the element
	/// instances. Returns false when the pair is not a list pair, leaving the caller to
	/// assign normally.
	/// </summary>
	private static bool TryCommitList(object? destinationValue, object? sourceValue, HashSet<object> visited)
	{
		if (destinationValue is not IList destinationList || sourceValue is not IList sourceList)
			return false;
		if (destinationValue is Array || sourceValue is Array)
			return false; // Arrays are fixed-size; the caller assigns the reference instead.
		if (destinationList.IsReadOnly || destinationList.IsFixedSize)
			return false;

		for (int i = 0; i < sourceList.Count; i++)
		{
			object? sourceItem = sourceList[i];

			if (i >= destinationList.Count)
			{
				destinationList.Add(sourceItem);
				continue;
			}

			object? destinationItem = destinationList[i];
			if (sourceItem is not null
				&& destinationItem is not null
				&& !IsDirectlyAssignable(sourceItem.GetType())
				&& destinationItem.GetType() == sourceItem.GetType())
			{
				CopyProperties(destinationItem, sourceItem, visited);
				continue;
			}

			destinationList[i] = sourceItem;
		}

		for (int i = destinationList.Count - 1; i >= sourceList.Count; i--)
			destinationList.RemoveAt(i);

		return true;
	}

	// Types whose value is copied wholesale rather than recursed into: anything without
	// meaningful sub-object identity to preserve.
	private static bool IsDirectlyAssignable(Type type)
	{
		var underlying = Nullable.GetUnderlyingType(type) ?? type;
		return underlying.IsValueType
			|| underlying == typeof(string)
			|| underlying.IsArray;
	}
}
