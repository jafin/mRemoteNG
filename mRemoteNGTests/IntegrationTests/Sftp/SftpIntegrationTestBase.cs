using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using mRemoteNG.Connection.Protocol.SSH.Native.HostKeys;
using mRemoteNG.Connection.Sftp;
using mRemoteNG.Security.Ssh;
using NUnit.Framework;

namespace mRemoteNGTests.IntegrationTests.Sftp;

/// <summary>
/// A working directory of one's own, and a connected session to reach it with.
/// </summary>
/// <remarks>
/// Isolation is per directory rather than per container. Each test gets a subdirectory under the
/// writable mount, made through <c>docker exec</c> rather than through the code under test — a test
/// whose setup runs on <c>CreateDirectoryAsync</c> reports a broken <c>CreateDirectoryAsync</c> as a
/// failure of whatever it was actually checking.
/// </remarks>
[SupportedOSPlatform("windows")]
public abstract class SftpIntegrationTestBase
{
    private static int _directories;

    private readonly List<SftpSession> _sessions = [];

    /// <summary>This test's own directory, as the session addresses it.</summary>
    protected string RemoteDirectory { get; private set; } = string.Empty;

    /// <summary>The same directory as <c>docker exec</c> addresses it.</summary>
    protected string ContainerDirectory => SftpServerFixture.ToContainerPath(RemoteDirectory);

    [SetUp]
    public async Task CreateWorkingDirectoryAsync()
    {
        RequireServer();

        // The test name is not usable as a path — parameterised cases carry brackets, commas and
        // quotes — so identity comes from a counter the runner cannot repeat within a run.
        RemoteDirectory = string.Create(CultureInfo.InvariantCulture,
            $"{SftpServerFixture.WritableRoot}/t{Interlocked.Increment(ref _directories)}");

        // Created through the container's own path, not the session's. See ChrootRoot: they differ,
        // and setting up at the session's path would silently build the directory somewhere the
        // session cannot see.
        await ExecAsync("mkdir", "-p", ContainerDirectory);
        await ExecAsync("chown", $"{SftpServerFixture.Username}:users", ContainerDirectory);
    }

    [TearDown]
    public void DisposeSessions()
    {
        foreach (SftpSession session in _sessions)
            session.Dispose();

        _sessions.Clear();
    }

    /// <summary>A session pointed at the fixture's server, disposed when the test ends.</summary>
    protected SftpSession NewSession(string? password = null)
    {
        RequireServer();

        SftpSession session = new(
            SftpServerFixture.Host,
            SftpServerFixture.Port,
            new ResolvedSshCredential(
                SftpServerFixture.Username, secret: password ?? SftpServerFixture.Password),
            // The container's host key is fresh and is not the subject here. Accepting it outright
            // keeps these tests about SFTP; the gate's own behaviour is covered without a server in
            // HostKeyGateTests, and refusing here would only test the fixture.
            new HostKeyGate(new NothingRemembered(), new AcceptAnyHostKey()));

        _sessions.Add(session);
        return session;
    }

    protected async Task<SftpSession> ConnectedSessionAsync()
    {
        SftpSession session = NewSession();
        await session.ConnectAsync();
        return session;
    }

    /// <summary>Runs a command in the container, failing the test if it does not succeed.</summary>
    protected static async Task<string> ExecAsync(params string[] command)
    {
        RequireServer();

        var result = await SftpServerFixture.Container!.ExecAsync(command).ConfigureAwait(false);

        Assert.That(result.ExitCode, Is.Zero,
            $"`{string.Join(' ', command)}` failed in the container: {result.Stderr}");

        return result.Stdout;
    }

    /// <summary>A path inside this test's directory, as the session addresses it.</summary>
    protected string RemotePath(string name) => $"{RemoteDirectory}/{name}";

    /// <summary>The same path as <c>docker exec</c> addresses it.</summary>
    protected string ContainerPath(string name) => $"{ContainerDirectory}/{name}";

    private static void RequireServer()
    {
        if (SftpServerFixture.Skipped)
            Assert.Ignore($"{SftpServerFixture.SkipVariable}=1");

        Assert.That(SftpServerFixture.Container, Is.Not.Null,
            "the SFTP container is not running; the fixture should have failed before this test ran");
    }

    /// <summary>
    /// Reports on the thread that raised the progress, unlike <see cref="System.Progress{T}"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="System.Progress{T}"/> posts to a synchronisation context or the thread pool, so
    /// its callbacks can land after the operation that raised them has already finished. That makes
    /// a test either sleep and hope, or assert against a queue that is still filling — and it makes
    /// "cancel on first report" unable to cancel anything, because the transfer is over before the
    /// handler runs. Reporting inline removes the race rather than waiting it out.
    /// </remarks>
    protected sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private sealed class NothingRemembered : IHostKeyStore
    {
        public string? Find(string host, int port, string keyAlgorithm) => null;

        public void Save(string host, int port, string keyAlgorithm, string fingerprint)
        {
        }
    }

    private sealed class AcceptAnyHostKey : IHostKeyVerifier
    {
        public bool Accept(HostKeyPresentation presentation) => true;
    }
}
