using System.Runtime.Versioning;
using mRemoteNG.Config.Settings.Providers;
using NUnit.Framework;

/// <summary>
/// Wires the test process onto the application's own settings provider, once, before anything else
/// runs.
/// </summary>
/// <remarks>
/// <para>
/// Without this, the first test to read a user-scoped setting sends the framework looking for
/// <c>testhost</c>'s <c>user.config</c> under the developer's profile — a path every testhost on the
/// machine derives identically. The runner runs several groups at once, so two of them reach it
/// together and the loser fails with <c>The process cannot access the file ... because it is being
/// used by another process</c>, attributed to whichever test happened to be holding the door. It is
/// a flake with no relationship to the code under test, and it gets likelier the wider the runner's
/// window opens.
/// </para>
/// <para>
/// The fix is the one the application already applies at startup: every settings class onto
/// <c>PortableSettingsProvider</c>, which reads the file the application actually writes. Nothing
/// then opens <c>user.config</c> at all, so there is no shared file left to contend for — and the
/// suite reads settings through the same provider the shipped product does, which is the more
/// faithful arrangement regardless of concurrency.
/// </para>
/// <para>
/// No namespace, deliberately: NUnit applies a <see cref="SetUpFixtureAttribute"/> to its own
/// namespace and below, and this one has to cover every test in the assembly whichever group filter
/// selected it.
/// </para>
/// </remarks>
[SetUpFixture]
[SupportedOSPlatform("windows")]
public class SettingsProviderSetUpFixture
{
    [OneTimeSetUp]
    public void WireTheApplicationsSettingsProvider() => PortableSettingsInitializer.EnsureInitialized();
}
