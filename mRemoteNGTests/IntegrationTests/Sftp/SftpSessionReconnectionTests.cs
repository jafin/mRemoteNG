using System;
using System.Collections.Concurrent;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using mRemoteNG.Connection.Sftp;
using NUnit.Framework;

namespace mRemoteNGTests.IntegrationTests.Sftp;

/// <summary>
/// Reconnecting: that it works repeatedly, that a real drop is reported, and that a reconnected
/// session is not haunted by the connection it replaced.
/// </summary>
/// <remarks>
/// <para>
/// <c>ConnectAsync</c> once replaced <c>_client</c> and <c>_authentication</c> without disposing
/// them — correct exactly once, which is all it was ever called, until reconnect called it again.
/// Reconnecting is what these cover, and none of it is reachable without a server: a client that was
/// replaced without being released still reports <c>IsConnected</c> while its socket belongs to a
/// corpse.
/// </para>
/// <para>
/// The change proposing this file expected these tests to pin the unsubscribe-before-dispose
/// ordering as well. They cannot, and the individual test says why — the failure mode that ordering
/// guards against does not occur with SSH.NET 2025.1.0.
/// </para>
/// </remarks>
[TestFixture]
[SupportedOSPlatform("windows")]
public class SftpSessionReconnectionTests : SftpIntegrationTestBase
{
    [Test]
    public async Task ASessionIsUsableAfterReconnectingSeveralTimes()
    {
        SftpSession session = NewSession();

        for (int attempt = 0; attempt < 4; attempt++)
        {
            await session.ConnectAsync();

            Assert.That(session.IsConnected, Is.True, $"not connected after attempt {attempt + 1}");

            // Usable, not merely connected. A client that was replaced without being released can
            // still report IsConnected while its socket belongs to a corpse.
            await session.ListDirectoryAsync(RemoteDirectory);
        }
    }

    [Test]
    public async Task ADroppedConnectionIsReportedOnceAndReconnectingDoesNotReportItAgain()
    {
        // What this does *not* assert, and why. The ordering constraint above — unsubscribe before
        // disposing — turns out not to be observable against SSH.NET 2025.1.0. Measured: killing the
        // connection at the server raises ErrorOccurred immediately, from the message-listener
        // thread, while the old client is still the current one; disposing that client afterwards
        // raises nothing at all, and disposing a *healthy* one is quiet too. Inverting the two lines
        // in ReleaseClient and re-running this file changes no result. The ordering stays as
        // insurance against a library version that does raise on dispose, but a test claiming to
        // catch it would be decoration.
        //
        // So this asserts what is real and was untested: the drop is reported, exactly once, for the
        // connection that genuinely died — and reconnecting over it does not report it again.
        SftpSession session = NewSession();
        ConcurrentQueue<string> dropped = new();
        session.Dropped += (_, reason) => dropped.Enqueue(reason ?? string.Empty);

        await session.ConnectAsync();
        await session.ListDirectoryAsync(RemoteDirectory);

        for (int attempt = 0; attempt < 3; attempt++)
        {
            await KillTheConnectionAtTheServerAsync();

            // ErrorOccurred arrives on SSH.NET's own thread, so the drop has to be waited for
            // rather than assumed to have landed by the time the kill returns.
            await Task.Delay(TimeSpan.FromMilliseconds(750));

            Assert.That(dropped, Has.Count.EqualTo(1),
                $"round {attempt + 1}: a connection died at the server and the session did not "
                + "report it — the panel would show a listing it can no longer refresh");

            dropped.Clear();

            await session.ConnectAsync();
            await session.ListDirectoryAsync(RemoteDirectory);
            await Task.Delay(TimeSpan.FromMilliseconds(750));

            Assert.That(dropped, Is.Empty,
                $"round {attempt + 1}: a session that has just reconnected reported itself dropped "
                + $"({string.Join("; ", dropped)})");
        }

        Assert.That(session.IsConnected, Is.True);
    }

    /// <summary>
    /// Breaks the session's connection from the server end.
    /// </summary>
    /// <remarks>
    /// The bracket in the pattern is not a typo. <c>pkill -f</c> matches whole command lines,
    /// including the shell running the pkill, so the literal pattern kills its own invoker and the
    /// exec comes back as SIGTERM. <c>[s]shd</c> matches "sshd" without the pattern containing it.
    /// Only the per-user child matches — the master sshd's command line carries no username.
    /// </remarks>
    private static Task<string> KillTheConnectionAtTheServerAsync() =>
        ExecAsync("sh", "-c", $"pkill -f '[s]shd.*{SftpServerFixture.Username}' || true");

    [Test]
    public async Task ASessionKilledAtTheServerReconnects()
    {
        // Broken rather than closed politely. A client-side Disconnect tears down in an order the
        // library controls; killing sshd's child leaves the socket dead under the session, which is
        // what a dropped VPN or a rebooted host actually does.
        SftpSession session = NewSession();
        await session.ConnectAsync();
        await session.ListDirectoryAsync(RemoteDirectory);

        await KillTheConnectionAtTheServerAsync();

        // The session may not notice immediately, and it does not have to: what matters is that
        // reconnecting from here produces a working session rather than inheriting the dead one.
        await session.ConnectAsync();

        Assert.Multiple(() =>
        {
            Assert.That(session.IsConnected, Is.True);
            Assert.DoesNotThrowAsync(async () => await session.ListDirectoryAsync(RemoteDirectory));
        });
    }
}
