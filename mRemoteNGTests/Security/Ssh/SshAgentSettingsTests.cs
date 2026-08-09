using mRemoteNG.Security.Ssh;
using mRemoteNG.Security.Ssh.Agent;
using NUnit.Framework;

namespace mRemoteNGTests.Security.Ssh;

/// <summary>
/// Covers the "configurable by a single global setting" requirement in
/// <c>specs/ssh-agent-authentication/spec.md</c>.
/// </summary>
[TestFixture]
[NonParallelizable]
public class SshAgentSettingsTests
{
    private bool _original;

    [SetUp]
    public void Setup() => _original = mRemoteNG.Properties.OptionsCredentialsPage.Default.UseSshAgent;

    [TearDown]
    public void TearDown() => mRemoteNG.Properties.OptionsCredentialsPage.Default.UseSshAgent = _original;

    [TestCase(true)]
    [TestCase(false)]
    public void TheEffectiveSettingTracksTheGlobalSetting(bool enabled)
    {
        mRemoteNG.Properties.OptionsCredentialsPage.Default.UseSshAgent = enabled;

        Assert.That(SshAgentSettings.Default.IsEnabled, Is.EqualTo(enabled));
    }

    [Test]
    public void TheSettingRoundTripsThroughTheSettingsStore()
    {
        mRemoteNG.Properties.OptionsCredentialsPage.Default.UseSshAgent = true;
        Assert.That(SshAgentSettings.Default.IsEnabled, Is.True);

        mRemoteNG.Properties.OptionsCredentialsPage.Default.UseSshAgent = false;
        Assert.That(SshAgentSettings.Default.IsEnabled, Is.False);
    }

    [Test]
    public void TheShippedDefaultIsEnabled()
    {
        // Shipped off until the sk-key spike (task 10.1) established that SSH.NET cannot fault
        // on a hardware-backed identity's null Key. An agent is the standard way to authenticate
        // without storing a secret, and both external clients mRemoteNG wraps already consult
        // one unconditionally. Reads the declared default rather than the current user value.
        var property = mRemoteNG.Properties.OptionsCredentialsPage.Default
            .Properties["UseSshAgent"];

        Assert.That(property!.DefaultValue, Is.EqualTo("True"));
    }

    // ---- the setting reaches resolution ------------------------------------------

    [TestCase(true)]
    [TestCase(false)]
    public void SshNetResolutionCarriesTheAgentSetting(bool enabled)
    {
        SshCredentialResolutionOptions options = SshCredentialResolutionOptions.ForSshNet(enabled);

        Assert.That(options.ConsultAgent, Is.EqualTo(enabled));
    }

    [Test]
    public void BackendsWithANativeAgentNeverConsultTheProvider()
    {
        // PuTTY talks to Pageant natively and ssh.exe talks to the Windows agent natively.
        // The setting must not appear to disable agent support mRemoteNG does not control.
        Assert.Multiple(() =>
        {
            Assert.That(SshCredentialResolutionOptions.ForPutty.ConsultAgent, Is.False);
            Assert.That(SshCredentialResolutionOptions.ForOpenSsh.ConsultAgent, Is.False);
        });
    }

    [Test]
    public void DiscoveryModeIsUnaffectedByTheAgentSetting()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SshCredentialResolutionOptions.ForSshNet(true).DefaultKeyDiscovery,
                Is.EqualTo(DefaultKeyDiscoveryMode.OpenSsh));
            Assert.That(SshCredentialResolutionOptions.ForSshNet(false).DefaultKeyDiscovery,
                Is.EqualTo(DefaultKeyDiscoveryMode.OpenSsh));
        });
    }
}