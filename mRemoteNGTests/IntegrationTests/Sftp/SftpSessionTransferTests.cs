using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using mRemoteNG.Connection.Sftp;
using NUnit.Framework;

namespace mRemoteNGTests.IntegrationTests.Sftp;

/// <summary>
/// Bytes in both directions, the progress reported while they move, and stopping part-way.
/// </summary>
[TestFixture]
[SupportedOSPlatform("windows")]
public class SftpSessionTransferTests : SftpIntegrationTestBase
{
    private static readonly byte[] Content = Encoding.UTF8.GetBytes(
        "The quick brown fox jumps over the lazy dog.\nSecond line.\n");

    [Test]
    public async Task ContentRoundTripsThroughUploadAndDownload()
    {
        SftpSession session = await ConnectedSessionAsync();

        using MemoryStream source = new(Content);
        await session.UploadAsync(source, RemotePath("round-trip.txt"), Content.Length);

        using MemoryStream destination = new();
        SftpEntry uploaded = (await session.ListDirectoryAsync(RemoteDirectory))
            .Single(e => e.Name == "round-trip.txt");
        await session.DownloadAsync(uploaded, destination);

        Assert.Multiple(() =>
        {
            Assert.That(destination.ToArray(), Is.EqualTo(Content));
            Assert.That(uploaded.Length, Is.EqualTo(Content.Length));
        });
    }

    [Test]
    public async Task ProgressIsReportedInBothDirectionsAndReachesTheTotal()
    {
        // DownloadFileAsync has no progress callback, so progress is counted from the stream it
        // writes into and the total comes from the listing. Both halves are worth checking against
        // a server: a total taken from the wrong place reads as a progress bar that never fills.
        SftpSession session = await ConnectedSessionAsync();

        ConcurrentQueue<SftpTransferProgress> uploadProgress = new();
        using MemoryStream source = new(Content);
        await session.UploadAsync(source, RemotePath("progress.txt"), Content.Length,
            new Progress<SftpTransferProgress>(uploadProgress.Enqueue));

        SftpEntry uploaded = (await session.ListDirectoryAsync(RemoteDirectory))
            .Single(e => e.Name == "progress.txt");

        ConcurrentQueue<SftpTransferProgress> downloadProgress = new();
        using MemoryStream destination = new();
        await session.DownloadAsync(uploaded, destination,
            new Progress<SftpTransferProgress>(downloadProgress.Enqueue));

        // Progress<T> posts asynchronously, so the last report can arrive after the await returns.
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        Assert.Multiple(() =>
        {
            Assert.That(uploadProgress, Is.Not.Empty, "an upload reported no progress at all");
            Assert.That(uploadProgress.Max(p => p.Transferred), Is.EqualTo(Content.Length));

            Assert.That(downloadProgress, Is.Not.Empty, "a download reported no progress at all");
            Assert.That(downloadProgress.Max(p => p.Transferred), Is.EqualTo(Content.Length));
            Assert.That(downloadProgress.Last().Total, Is.EqualTo(Content.Length),
                "the total comes from the listing; the destination is a new file and says nothing");
        });
    }

    [Test]
    public async Task CancellingATransferStopsIt()
    {
        // Large enough that cancellation lands mid-flight rather than after the whole thing has
        // already gone out.
        byte[] large = new byte[8 * 1024 * 1024];
        Random.Shared.NextBytes(large);

        SftpSession session = await ConnectedSessionAsync();

        using CancellationTokenSource cancellation = new();
        using MemoryStream source = new(large);

        Task upload = session.UploadAsync(source, RemotePath("cancelled.bin"), large.Length,
            new Progress<SftpTransferProgress>(_ => cancellation.Cancel()),
            cancellation.Token);

        Assert.CatchAsync<OperationCanceledException>(async () => await upload);
    }

    [Test]
    public async Task UploadingOverAnExistingFileReplacesIt()
    {
        // What the file manager's overwrite prompt promises once the user says yes. A server that
        // appended instead would leave the first file's tail behind the second's content.
        SftpSession session = await ConnectedSessionAsync();

        byte[] longer = Encoding.UTF8.GetBytes(new string('a', 400));
        byte[] shorter = Encoding.UTF8.GetBytes("short");

        using (MemoryStream first = new(longer))
            await session.UploadAsync(first, RemotePath("replaced.txt"), longer.Length);

        using (MemoryStream second = new(shorter))
            await session.UploadAsync(second, RemotePath("replaced.txt"), shorter.Length);

        using MemoryStream destination = new();
        SftpEntry entry = (await session.ListDirectoryAsync(RemoteDirectory))
            .Single(e => e.Name == "replaced.txt");
        await session.DownloadAsync(entry, destination);

        Assert.Multiple(() =>
        {
            Assert.That(entry.Length, Is.EqualTo(shorter.Length),
                "the replacement is shorter; a truncation that did not happen shows up here");
            Assert.That(destination.ToArray(), Is.EqualTo(shorter));
        });
    }
}
