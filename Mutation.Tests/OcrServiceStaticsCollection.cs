using Xunit;

namespace Mutation.Tests;

/// <summary>
/// Serialises the test classes that reach into <c>OcrService</c>'s private static
/// <c>SharedRateLimiter</c>. Only one class does today, and it restores the field in a
/// <c>finally</c> — but a restore is worthless against a class running in parallel, and
/// those tests assert the real production limit, which a leaked swap breaks. Joining
/// this collection is what makes the second such class safe to write (issue #250).
/// </summary>
[CollectionDefinition(Name)]
public sealed class OcrServiceStaticsCollection
{
	public const string Name = "OcrService statics";
}
