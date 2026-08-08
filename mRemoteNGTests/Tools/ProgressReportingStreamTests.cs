using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using mRemoteNG.Tools;
using NUnit.Framework;

namespace mRemoteNGTests.Tools
{
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

        [Test]
        public void TheWrapperIsReadOnly()
        {
            using MemoryStream source = new(Payload);
            using ProgressReportingStream stream = new(source, (_, _) => { });

            Assert.Multiple(() =>
            {
                Assert.That(stream.CanWrite, Is.False);
                Assert.Throws<NotSupportedException>(() => stream.Write(new byte[1], 0, 1));
                Assert.Throws<NotSupportedException>(() => stream.SetLength(1));
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
}
