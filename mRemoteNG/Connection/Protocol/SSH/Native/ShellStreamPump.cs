using System;
using System.Buffers;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace mRemoteNG.Connection.Protocol.SSH.Native;

/// <summary>
/// Reads a shell stream and turns it into text for the terminal emulator.
/// </summary>
/// <remarks>
/// <para>
/// The decoder is retained across reads and fed incrementally. This is the whole point of the
/// class. A read returns whatever bytes happen to have arrived, so a multi-byte UTF-8 sequence is
/// routinely split across two of them; <c>Encoding.UTF8.GetString(buffer, 0, read)</c> per read
/// would emit U+FFFD at every such boundary. Against ASCII output that defect is invisible, and
/// for anyone writing a non-Latin language it is immediate and constant.
/// </para>
/// <para>
/// Invalid bytes still decode to U+FFFD rather than throwing: a terminal shows whatever the
/// remote host sent, and a binary file catted into it must not take the session down.
/// </para>
/// </remarks>
public sealed class ShellStreamPump
{
    /// <summary>Reported through <see cref="Closed"/> when the remote end finishes the stream.</summary>
    public const string RemoteClosedReason = "The remote shell closed the connection.";

    private readonly Stream _stream;
    private readonly int _bufferSize;
    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();

    /// <summary>Decoded output, in the order it arrived. Never raised with an empty string.</summary>
    public event Action<string> TextReceived;

    /// <summary>
    /// Raised once, when the stream ends or fails. The argument is the reason, suitable for a
    /// disconnect message.
    /// </summary>
    public event Action<string> Closed;

    public ShellStreamPump(Stream stream, int bufferSize = 32 * 1024)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentOutOfRangeException.ThrowIfLessThan(bufferSize, 1);

        _stream = stream;
        _bufferSize = bufferSize;
    }

    /// <summary>
    /// Pumps until the stream ends, fails, or <paramref name="cancellationToken"/> is signalled.
    /// Cancellation is a caller-initiated teardown and does not raise <see cref="Closed"/>.
    /// </summary>
    public async Task PumpAsync(CancellationToken cancellationToken = default)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(_bufferSize);

        // One byte can never produce more than one char, and a trailing partial sequence is held
        // by the decoder rather than emitted, so read-length chars is always enough.
        char[] chars = ArrayPool<char>.Shared.Rent(_bufferSize);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                int read;
                try
                {
                    read = await _stream.ReadAsync(buffer.AsMemory(0, _bufferSize), cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    // The stream was torn down under us. If that was our own teardown the token is
                    // set and this is not a disconnect; otherwise it is.
                    if (!cancellationToken.IsCancellationRequested)
                        Closed?.Invoke(RemoteClosedReason);
                    return;
                }
                catch (Exception ex)
                {
                    Closed?.Invoke(ex.Message);
                    return;
                }

                if (read <= 0)
                {
                    Closed?.Invoke(RemoteClosedReason);
                    return;
                }

                int decoded = _decoder.GetChars(buffer, 0, read, chars, 0, flush: false);
                if (decoded > 0)
                    TextReceived?.Invoke(new string(chars, 0, decoded));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            ArrayPool<char>.Shared.Return(chars);
        }
    }
}
