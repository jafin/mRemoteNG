using System.Runtime.Versioning;
using mRemoteNG.Config.Settings.Providers;
using mRemoteNGTests.TestHelpers;
using NUnit.Framework;

/// <summary>
/// Settles the test process's settings — which provider serves them, and how hard the key
/// derivation behind them works — once, before anything else runs.
/// </summary>
/// <remarks>
/// <para>
/// <b>The provider.</b> Without this, the first test to read a user-scoped setting sends the
/// framework looking for <c>testhost</c>'s <c>user.config</c> under the developer's profile — a path
/// every testhost on the machine derives identically. The runner runs several groups at once, so two
/// of them reach it together and the loser fails with <c>The process cannot access the file ...
/// because it is being used by another process</c>, attributed to whichever test happened to be
/// holding the door. It is a flake with no relationship to the code under test, and it gets likelier
/// the wider the runner's window opens. The fix is the one the application already applies at
/// startup: every settings class onto <c>PortableSettingsProvider</c>, which reads the file the
/// application actually writes. Nothing then opens <c>user.config</c> at all, and the suite reads
/// settings through the same provider the shipped product does.
/// </para>
/// <para>
/// <b>The iteration count.</b> Everything that encrypts through
/// <c>CryptoProviderFactoryFromSettings</c> — saving a connection file, most of all — takes its
/// iteration count from here, and the shipped 600,000 costs about a quarter of a second per derived
/// key by design. Tests that save and reload a file are not about that cost and were paying it
/// repeatedly. Set to the floor the product accepts, so those paths stay honest about what they do
/// and stop being the slowest thing in the suite. A test that is about the count sets its own, which
/// is what the property is for. See <see cref="CryptoTestSpeed"/> for providers built directly,
/// which never consult this setting.
/// </para>
/// <para>
/// No namespace, deliberately: NUnit applies a <see cref="SetUpFixtureAttribute"/> to its own
/// namespace and below, and this one has to cover every test in the assembly whichever group filter
/// selected it.
/// </para>
/// </remarks>
[SetUpFixture]
[SupportedOSPlatform("windows")]
public class TestProcessSettingsSetUpFixture
{
    [OneTimeSetUp]
    public void SettleTheProcessSettings()
    {
        // Order matters: wiring the provider first is what keeps the assignment below from being
        // the very read that opens user.config.
        PortableSettingsInitializer.EnsureInitialized();

        mRemoteNG.Properties.OptionsSecurityPage.Default.EncryptionKeyDerivationIterations =
            CryptoTestSpeed.Iterations;
    }
}
