using System;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using NUnit.Framework;

namespace mRemoteNGTests.IntegrationTests.Sftp;

/// <summary>
/// One SFTP server for the whole <c>...IntegrationTests.Sftp</c> namespace.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SftpSession"/> is the one layer nothing ran. Below it <c>Describe</c> and
/// <c>FormatPermissions</c> are tested against a faked <c>ISftpFile</c>; above it everything fakes
/// <c>ISftpSession</c> wholesale. The class that opens a socket and speaks the protocol had no
/// coverage at all, because a test may not require a server — and now one can bring its own.
/// </para>
/// <para>
/// One container per run rather than per test. It starts in a couple of seconds, and per-test would
/// pay that on every case for no isolation gain: isolation comes from each test working in its own
/// subdirectory, which <see cref="SftpIntegrationTestBase"/> creates. A test needing the server in a
/// particular state builds that state itself.
/// </para>
/// <para>
/// The namespace is deliberate. <c>test-config.json</c> puts anything under
/// <c>mRemoteNGTests.IntegrationTests</c> in the Integration group and excludes that prefix from
/// Remaining, so these are picked up with no group definition touched — files an ordinary change may
/// not edit. It also means <c>IntegrationSetUpFixture</c>'s <c>TestScope</c> wraps these tests too,
/// NUnit applying a setup fixture to its namespace and below. That is harmless: the scope snapshots
/// and restores shared singletons none of this touches.
/// </para>
/// </remarks>
[SetUpFixture]
[SupportedOSPlatform("windows")]
public class SftpServerFixture
{
    /// <summary>
    /// Pinned rather than tracking <c>latest</c>, so the image moving under the suite is a
    /// deliberate update and not a Tuesday.
    /// </summary>
    private const string Image = "atmoz/sftp:alpine";

    /// <summary>Set to <c>1</c> to skip this group on a machine with no Docker.</summary>
    public const string SkipVariable = "MRNG_SKIP_SFTP_INTEGRATION";

    public const string Username = "tester";
    public const string Password = "password";

    /// <summary>
    /// The writable mount <b>as the session sees it</b>. Everything above it is root-owned and
    /// refuses writes, which is what makes the permission-denied case reachable.
    /// </summary>
    public const string WritableRoot = "/upload";

    /// <summary>
    /// Where the user's chroot actually lives on the container filesystem.
    /// </summary>
    /// <remarks>
    /// The two path spaces are not the same and confusing them is silent. <c>sshd</c> chroots the
    /// user here, so the session's <c>/upload</c> is really <c>/home/tester/upload</c>, while
    /// <c>docker exec</c> sees the real filesystem. A test that sets up through exec at the
    /// session's path creates a directory nobody is looking at, and then passes or fails for
    /// reasons that have nothing to do with the code under test.
    /// </remarks>
    public const string ChrootRoot = "/home/" + Username;

    /// <summary>Translates a path the session would use into one <c>docker exec</c> can reach.</summary>
    public static string ToContainerPath(string sessionPath) => ChrootRoot + sessionPath;

    private IContainer? _container;

    public static string Host { get; private set; } = string.Empty;

    public static int Port { get; private set; }

    /// <summary>Whether the group opted out, so tests can say so rather than fail obscurely.</summary>
    public static bool Skipped { get; private set; }

    internal static IContainer? Container { get; private set; }

    [OneTimeSetUp]
    public async Task StartServerAsync()
    {
        if (Environment.GetEnvironmentVariable(SkipVariable) == "1")
        {
            Skipped = true;

            // Said out loud. Choosing to skip and not noticing are different things, and only the
            // first should be easy.
            await TestContext.Out.WriteLineAsync(
                $"{SkipVariable}=1 — skipping the SFTP integration group. These tests are the only "
                + "coverage SftpSession has against a real server.");
            return;
        }

        try
        {
            // Building is inside the try, not just starting. An unreachable daemon surfaces as a
            // TypeInitializationException from Testcontainers' own settings, thrown the moment
            // ContainerBuilder is first touched — so a catch around StartAsync alone still fails
            // the group, but with a message that never mentions Docker and sends the reader into
            // the wrong investigation entirely.
            //
            // atmoz/sftp's documented form: user:pass:::dir. The trailing directory is created
            // inside the user's chroot and is the only writable place, which is what makes the
            // permission-denied case reachable without contriving anything.
            _container = new ContainerBuilder(Image)
                .WithCommand($"{Username}:{Password}:::{WritableRoot.TrimStart('/')}")
                .WithPortBinding(22, assignRandomHostPort: true)
                // Both conditions, not just the port. The port is bound before sshd is ready to
                // authenticate, so a port-only strategy races the server and fails somewhere less
                // obvious than here.
                .WithWaitStrategy(Wait.ForUnixContainer()
                    .UntilInternalTcpPortIsAvailable(22)
                    .UntilMessageIsLogged("Server listening on"))
                .Build();

            await _container.StartAsync();
        }
        catch (Exception ex)
        {
            // Named, and a failure rather than a skip. A test that passes when its subject never ran
            // reports coverage that does not exist, which is worse than having no test. In CI an
            // unreachable daemon is a broken runner and should look like one.
            throw new InvalidOperationException(
                "Could not start the SFTP test container. Docker must be running for the SFTP "
                + $"integration group. Set {SkipVariable}=1 to skip it deliberately.", ex);
        }

        Container = _container;
        Host = _container.Hostname;
        Port = _container.GetMappedPublicPort(22);
    }

    [OneTimeTearDown]
    public async Task StopServerAsync()
    {
        if (_container is null)
            return;

        // Ryuk removes it anyway if the run is killed; this is the tidy path.
        await _container.DisposeAsync();
        _container = null;
        Container = null;
    }
}
