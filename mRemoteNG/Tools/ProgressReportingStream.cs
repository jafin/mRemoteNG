using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace mRemoteNG.Tools
{
    /// <summary>
    /// Passes a stream through unchanged while reporting how much of it has been read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>SftpClient.UploadFileAsync</c> takes no progress callback — the APM
    /// <c>BeginUploadFile</c> it replaces exposed progress through
    /// <c>SftpUploadAsyncResult.UploadedBytes</c>, which had to be polled. Counting bytes as the
    /// uploader reads them gives the same information without the poll, and updates exactly when
    /// something is actually transferred rather than on a timer.
    /// </para>
    /// <para>
    /// Never disposes the stream it wraps: the caller owns that and disposes it separately.
    /// </para>
    /// </remarks>
    internal sealed class ProgressReportingStream : Stream
    {
        private readonly Stream _inner;
        private readonly Action<long, long> _report;
        private long _transferred;

        /// <param name="inner">The stream to read through.</param>
        /// <param name="report">
        /// Called with (bytes read so far, total bytes) after every read. Total is -1 when the
        /// wrapped stream cannot report a length.
        /// </param>
        public ProgressReportingStream(Stream inner, Action<long, long> report)
        {
            ArgumentNullException.ThrowIfNull(inner);
            ArgumentNullException.ThrowIfNull(report);

            _inner = inner;
            _report = report;
        }

        /// <summary>Bytes read through this stream so far.</summary>
        public long Transferred => Interlocked.Read(ref _transferred);

        /// <summary>The total size, or -1 if the wrapped stream cannot report one.</summary>
        public long Total => _inner.CanSeek ? _inner.Length : -1;

        public override bool CanRead => _inner.CanRead;

        public override bool CanSeek => _inner.CanSeek;

        public override bool CanWrite => false;

        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            Advance(_inner.Read(buffer, offset, count));

        public override int Read(Span<byte> buffer) =>
            Advance(_inner.Read(buffer));

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            Advance(await _inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false));

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            Advance(await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false));

        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);

        public override void Flush() => _inner.Flush();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private int Advance(int read)
        {
            if (read <= 0)
                return read;

            _report(Interlocked.Add(ref _transferred, read), Total);
            return read;
        }
    }
}
