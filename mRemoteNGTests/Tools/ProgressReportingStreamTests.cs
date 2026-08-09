using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using mRemoteNG.Tools;
using NUnit.Framework;

namespace mRemoteNGTests.Tools;

[TestFixture]
public class ProgressReportingStreamTests
{
    private static readonly byte[] Payload = Encoding.ASCII.GetBytes("0123456789");

    [Test]
    public void ReadsReportCumulativeProgress()
    {
        List<long> reported = [];
        using MemoryStream source = new(Payload);
        using ProgressReportingStream stream = new(source, (transferred, _) => reported.Add(transferred));

        byte[] buffer = new byte[4];
        while (stream.Read(buffer, 0, buffer.Length) > 0)
        {
        }

        Assert.That(reported, Is.EqualTo(new long[] { 4, 8, 10 }));
    }

    [Test]
    public async Task AsynchronousReadsReportProgressToo()
    {
        List<long> reported = [];
        using MemoryStream source = new(Payload);
        using ProgressReportingStream stream = new(source, (transferred, _) => reported.Add(transferred));

        byte[] buffer = new byte[5];
        while (await stream.ReadAsync(buffer) > 0)
        {
        }

        Assert.That(reported, Is.EqualTo(new long[] { 5, 10 }));
    }

    [Test]
    public void TheTotalIsTheWrappedStreamsLength()
    {
        using MemoryStream source = new(Payload);
        long total = -1;
        using ProgressReportingStream stream = new(source, (_, t) => total = t);

        _ = stream.Read(new byte[4], 0, 4);

        Assert.That(total, Is.EqualTo(Payload.Length));
    }

    [Test]
    public void AnUnseekableStreamReportsAnUnknownTotal()
    {
        long total = 0;
        using UnseekableStream source = new(Payload);
        using ProgressReportingStream stream = new(source, (_, t) => total = t);

        _ = stream.Read(new byte[4], 0, 4);

        Assert.Multiple(() =>
        {
            Assert.That(total, Is.EqualTo(-1));
            Assert.That(stream.Total, Is.EqualTo(-1));
        });
    }

    [Test]
    public void ReadingToTheEndReportsNothingExtra()
    {
        int reports = 0;
        using MemoryStream source = new(Payload);
        using ProgressReportingStream stream = new(source, (_, _) => reports++);

        byte[] buffer = new byte[Payload.Length];
        _ = stream.Read(buffer, 0, buffer.Length);
        _ = stream.Read(buffer, 0, buffer.Length);

        Assert.Multiple(() =>
        {
            Assert.That(reports, Is.EqualTo(1));
            Assert.That(stream.Transferred, Is.EqualTo(Payload.Length));
        });
    }

    [Test]
    public void TheWrappedStreamIsNotDisposedWithTheWrapper()
    {
        MemoryStream source = new(Payload);

        using (ProgressReportingStream stream = new(source, (_, _) => { }))
        {
            _ = stream.Read(new byte[4], 0, 4);
        }

        // Disposing the caller-owned stream is the caller's job; touching it here must work.
        Assert.That(source.Position, Is.EqualTo(4));
        source.Dispose();
    }

    // ---- writes, for the download direction ------------------------------------

    [Test]
    public void WritesReportCumulativeProgress()
    {
        // A download writes into this stream rather than reading from it, so both directions
        // have to count.
        List<long> reported = [];
        using MemoryStream destination = new();
        using ProgressReportingStream stream = new(destination, (transferred, _) => reported.Add(transferred));

        stream.Write(Payload, 0, 4);
        stream.Write(Payload, 4, 6);

        Assert.That(reported, Is.EqualTo(new long[] { 4, 10 }));
    }

    [Test]
    public async Task AsynchronousWritesReportProgressToo()
    {
        List<long> reported = [];
        using MemoryStream destination = new();
        using ProgressReportingStream stream = new(destination, (transferred, _) => reported.Add(transferred));

        await stream.WriteAsync(Payload.AsMemory(0, 5));
        await stream.WriteAsync(Payload.AsMemory(5, 5));

        Assert.That(reported, Is.EqualTo(new long[] { 5, 10 }));
    }

    [Test]
    public void WrittenBytesReachTheWrappedStream()
    {
        using MemoryStream destination = new();
        using ProgressReportingStream stream = new(destination, (_, _) => { });

        stream.Write(Payload, 0, Payload.Length);

        Assert.That(destination.ToArray(), Is.EqualTo(Payload));
    }

    [Test]
    public void ADeclaredTotalOverridesTheWrappedStreamsLength()
    {
        // A download writes into an empty file whose length says nothing about the size of the
        // transfer, so the caller supplies the remote file's size.
        long total = 0;
        using MemoryStream destination = new();
        using ProgressReportingStream stream = new(destination, (_, t) => total = t, totalBytes: 5000);

        stream.Write(Payload, 0, 4);

        Assert.Multiple(() =>
        {
            Assert.That(total, Is.EqualTo(5000));
            Assert.That(stream.Total, Is.EqualTo(5000));
        });
    }

    [Test]
    public void WritabilityFollowsTheWrappedStream()
    {
        using MemoryStream writable = new();
        using MemoryStream readOnly = new(Payload, writable: false);

        Assert.Multiple(() =>
        {
            Assert.That(new ProgressReportingStream(writable, (_, _) => { }).CanWrite, Is.True);
            Assert.That(new ProgressReportingStream(readOnly, (_, _) => { }).CanWrite, Is.False);
        });
    }

    [Test]
    public void NullArgumentsAreRejected()
    {
        using MemoryStream source = new(Payload);

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentNullException>(() => new ProgressReportingStream(null!, (_, _) => { }));
            Assert.Throws<ArgumentNullException>(() => new ProgressReportingStream(source, null!));
        });
    }

    private sealed class UnseekableStream(byte[] contents) : MemoryStream(contents)
    {
        public override bool CanSeek => false;
    }
}