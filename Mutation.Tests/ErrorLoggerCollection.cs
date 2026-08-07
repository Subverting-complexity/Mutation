using Xunit;

namespace Mutation.Tests;

/// <summary>
/// Serialises the test classes that drive <c>ErrorLogger</c>'s process-wide state — the
/// registered-secret snapshot and the log directory redirect. xunit runs each class in
/// its own collection in parallel by default, so two classes replacing the same static
/// would otherwise assert against each other's values.
/// <para>
/// It is not only the classes that call <c>ErrorLogger</c> directly. Every
/// <c>SettingsManager.LoadAndEnsureSettings</c> and <c>SaveSettingsToFile</c> feeds the
/// configured keys to <c>ErrorLogger.RegisterSecretValues</c>, which <em>replaces</em>
/// the whole snapshot — and a settings file with placeholder keys registers an empty
/// one. A class doing that in parallel wipes the key a redaction test just registered
/// (issue #250). <c>ErrorLoggerStaticsCollectionTests</c> fails if a new test class
/// loads or saves settings without joining this collection.
/// </para>
/// </summary>
[CollectionDefinition(Name)]
public sealed class ErrorLoggerCollection
{
	public const string Name = "ErrorLogger statics";
}
