using Xunit;

namespace Mutation.Tests;

/// <summary>
/// Serialises the test classes that drive <c>ErrorLogger</c>'s process-wide state — the
/// registered-secret snapshot and the log directory redirect. xunit runs each class in
/// its own collection in parallel by default, so two classes replacing the same static
/// would otherwise assert against each other's values.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ErrorLoggerCollection
{
	public const string Name = "ErrorLogger statics";
}
