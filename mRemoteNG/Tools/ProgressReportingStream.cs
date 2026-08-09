using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace mRemoteNG.Tools;

/// <summary>
/// Passes a stream through unchanged while reporting how much has moved through it.
/// </summary>
/// <remarks>
/// <para>
/// Neither <c>SftpClient.UploadFileAsync</c> nor <c>DownloadFileAsync</c> takes a progress
/// callback. The APM <c>BeginUploadFile</c> that upload replaces exposed progress through
/// <c>SftpUploadAsyncResult.UploadedBytes</c>, which had to be polled. Counting bytes as they
/// pass gives the same information without the poll, and updates exactly when something is
/// actually transferred rather than on a timer.
/// </para>
/// <para>
/// Counts both directions, because the two transfer directions read and write opposite ends:
/// an upload reads from this stream, a download writes to it.
/// </para>
/// <para>
/// Never disposes the stream it wraps: the caller owns that and disposes it separately.
/// </para>
/// </remarks>
internal sealed class ProgressReportingStream : Stream
{
    private readonly Stream _inner;
    private readonly Action<long, long> _report;
    private readonly long? _declaredTotal;
    private long _transferred;

    /// <param name="inner">The stream to pass through.</param>
    /// <param name="report">
    /// Called with (bytes moved so far, total bytes) after every read or write. Total is -1 when
    /// it is not known.
    /// </param>
    /// <param name="totalBytes">
    /// The expected total, when the wrapped stream cannot supply it. A download writes into an
    /// empty file whose length says nothing about the size of the transfer, so the caller passes
    /// the remote file's size here.
    /// </param>
    public ProgressReportingStream(Stream inner, Action<long, long> report, long? totalBytes = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(report);

        _inner = inner;
        _report = report;
        _declaredTotal = totalBytes;
    }

    /// <summary>Bytes moved through this stream so far.</summary>
    public long Transferred => Interlocked.Read(ref _transferred);

    /// <summary>The total size, or -1 if it is not known.</summary>
    public long Total => _declaredTotal ?? (_inner.CanSeek ? _inner.Length : -1);

    public override bool CanRead => _inner.CanRead;

    public override bool CanSeek => _inner.CanSeek;

    public override bool CanWrite => _inner.CanWrite;

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

    public override void SetLength(long value) => _inner.SetLength(value);

    public override void Write(byte[] buffer, int offset, int count)
    {
        _inner.Write(buffer, offset, count);
        Advance(count);
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        _inner.Write(buffer);
        Advance(buffer.Length);
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        await _inner.WriteAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
        Advance(count);
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await _inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        Advance(buffer.Length);
    }

    private int Advance(int read)
    {
        if (read <= 0)
            return read;

        _report(Interlocked.Add(ref _transferred, read), Total);
        return read;
    }
}