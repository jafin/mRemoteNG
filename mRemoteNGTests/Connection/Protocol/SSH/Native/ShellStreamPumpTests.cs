using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using mRemoteNG.Connection.Protocol.SSH.Native;
using NUnit.Framework;

namespace mRemoteNGTests.Connection.Protocol.SSH.Native;

[TestFixture]
public class ShellStreamPumpTests
{
    /// <summary>
    /// Hands back exactly the chunks it was given, one per read, so a test can put a byte boundary
    /// anywhere it likes. A MemoryStream cannot: it satisfies a read from whatever remains.
    /// </summary>
    private sealed class ChunkedStream(params byte[][] chunks) : Stream
    {
        private int _index;

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_index >= chunks.Length)
                return 0;

            byte[] chunk = chunks[_index++];
            Buffer.BlockCopy(chunk, 0, buffer, offset, chunk.Length);
            return chunk.Length;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class ThrowingStream(Exception exception) : Stream
    {
        public override int Read(byte[] buffer, int offset, int count) => throw exception;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static async Task<(string Text, List<string> Closed)> PumpAsync(params byte[][] chunks)
    {
        ShellStreamPump pump = new(new ChunkedStream(chunks));
        StringBuilder text = new();
        List<string> closed = [];

        pump.TextReceived += t => text.Append(t);
        pump.Closed += r => closed.Add(r);

        await pump.PumpAsync();
        return (text.ToString(), closed);
    }

    [Test]
    public async Task AMultiByteSequenceSplitAcrossTwoReadsIsNotCorrupted()
    {
        // U+00E9 is two bytes; the split puts one in each read. Decoding per-read would emit
        // U+FFFD twice here and never produce the character at all.
        byte[] encoded = Encoding.UTF8.GetBytes("é");
        Assert.That(encoded, Has.Length.EqualTo(2), "precondition: the test character is two bytes");

        (string text, _) = await PumpAsync([encoded[0]], [encoded[1]]);

        Assert.Multiple(() =>
        {
            Assert.That(text, Is.EqualTo("é"));
            Assert.That(text, Does.Not.Contain("�"));
        });
    }

    [Test]
    public async Task AFourByteSequenceSplitAcrossEveryByteIsNotCorrupted()
    {
        // An emoji is four bytes and a surrogate pair once decoded — the worst case for a decoder
        // that does not carry state, and the one a user notices first.
        byte[] encoded = Encoding.UTF8.GetBytes("😀");
        Assert.That(encoded, Has.Length.EqualTo(4), "precondition: the test character is four bytes");

        (string text, _) = await PumpAsync([encoded[0]], [encoded[1]], [encoded[2]], [encoded[3]]);

        Assert.That(text, Is.EqualTo("😀"));
    }

    [Test]
    public async Task ASequenceSplitInsideALongerRunIsNotCorrupted()
    {
        byte[] all = Encoding.UTF8.GetBytes("before 日本語テキスト after");

        // Split at every position in turn; a stateful decoder is correct at all of them.
        for (int split = 1; split < all.Length; split++)
        {
            byte[] head = all[..split];
            byte[] tail = all[split..];

            (string text, _) = await PumpAsync(head, tail);

            Assert.That(text, Is.EqualTo("before 日本語テキスト after"),
                $"split after byte {split} of {all.Length}");
        }
    }

    [Test]
    public async Task InvalidBytesBecomeReplacementCharactersRatherThanThrowing()
    {
        // A binary file catted into a terminal must not take the session down.
        (string text, List<string> closed) = await PumpAsync([0xC3, 0x28], Encoding.UTF8.GetBytes("ok"));

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("�"));
            Assert.That(text, Does.EndWith("ok"));
            Assert.That(closed, Has.Count.EqualTo(1), "the stream still ended normally");
        });
    }

    [Test]
    public async Task TheEndOfTheStreamIsReportedAsAClose()
    {
        (_, List<string> closed) = await PumpAsync(Encoding.UTF8.GetBytes("bye"));

        Assert.Multiple(() =>
        {
            Assert.That(closed, Has.Count.EqualTo(1));
            Assert.That(closed[0], Is.EqualTo(ShellStreamPump.RemoteClosedReason));
        });
    }

    [Test]
    public async Task ATransportFailureIsReportedAsACloseCarryingItsReason()
    {
        ShellStreamPump pump = new(new ThrowingStream(new IOException("connection reset")));
        List<string> closed = [];
        pump.Closed += r => closed.Add(r);

        await pump.PumpAsync();

        Assert.Multiple(() =>
        {
            Assert.That(closed, Has.Count.EqualTo(1));
            Assert.That(closed[0], Is.EqualTo("connection reset"));
        });
    }

    [Test]
    public async Task CancellationIsATeardownAndNotADisconnect()
    {
        ShellStreamPump pump = new(new ChunkedStream(Encoding.UTF8.GetBytes("data")));
        List<string> closed = [];
        pump.Closed += r => closed.Add(r);

        using CancellationTokenSource cts = new();
        await cts.CancelAsync();

        await pump.PumpAsync(cts.Token);

        Assert.That(closed, Is.Empty, "the caller tore the session down; that is not a disconnect");
    }

    [Test]
    public async Task EmptyDecodedOutputIsNotRaised()
    {
        // The first byte of a two-byte sequence decodes to nothing. Raising an empty string would
        // make every consumer handle a case that carries no information.
        byte[] encoded = Encoding.UTF8.GetBytes("é");
        List<string> raised = [];

        ShellStreamPump pump = new(new ChunkedStream([encoded[0]], [encoded[1]]));
        pump.TextReceived += t => raised.Add(t);

        await pump.PumpAsync();

        Assert.Multiple(() =>
        {
            Assert.That(raised, Has.Count.EqualTo(1));
            Assert.That(raised[0], Is.EqualTo("é"));
        });
    }
}
