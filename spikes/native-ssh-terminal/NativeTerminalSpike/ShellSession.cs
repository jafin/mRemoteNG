using System.Text;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace NativeTerminalSpike;

/// <summary>
/// Minimal SSH.NET shell transport for the spike: connect, open a PTY-backed shell, and pump
/// bytes out of it. The one thing here that is not throwaway is the decoder — see <see cref="ReadLoop"/>.
/// </summary>
public sealed class ShellSession : IDisposable
{
    private readonly SshClient _client;
    private ShellStream? _stream;
    private Thread? _reader;
    private volatile bool _stopping;

    /// <summary>Decoded text, plus the raw byte count that produced it.</summary>
    public event Action<string, int>? DataReceived;

    public event Action<string>? Closed;

    public ShellSession(SpikeOptions options)
    {
        AuthenticationMethod auth = options.PrivateKeyPath is { Length: > 0 } keyPath
            ? new PrivateKeyAuthenticationMethod(options.User, new PrivateKeyFile(keyPath))
            : new PasswordAuthenticationMethod(options.User, options.Password ?? string.Empty);

        ConnectionInfo info = new(options.Host, options.Port, options.User, auth);
        _client = new SshClient(info);

        // Spike only. The real implementation must present this for confirmation (spec: host key
        // is presented for confirmation). Auto-trusting here keeps the measurement unattended.
        _client.HostKeyReceived += (_, e) => e.CanTrust = true;
    }

    public void Connect(uint columns, uint rows)
    {
        _client.Connect();
        _stream = _client.CreateShellStream("xterm-256color", columns, rows, 0, 0, 64 * 1024);
        _reader = new Thread(ReadLoop) { IsBackground = true, Name = "spike-ssh-read" };
        _reader.Start();
    }

    public void Write(string data)
    {
        ShellStream? stream = _stream;
        if (stream is null) return;

        byte[] bytes = Encoding.UTF8.GetBytes(data);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush();
    }

    public void Resize(uint columns, uint rows)
    {
        // Spec: resizing while disconnected is a no-op and raises no error.
        _stream?.ChangeWindowSize(columns, rows, 0, 0);
    }

    private void ReadLoop()
    {
        ShellStream stream = _stream!;
        byte[] buffer = new byte[32 * 1024];

        // THE point of this loop. A UTF-8 sequence can straddle two reads, so the decoder is
        // retained across iterations and fed incrementally. Encoding.UTF8.GetString(buffer) per
        // read would emit U+FFFD at every chunk boundary that lands mid-sequence — invisible
        // against ASCII output, immediate for anyone writing a non-Latin language.
        Decoder decoder = Encoding.UTF8.GetDecoder();
        char[] chars = new char[buffer.Length + 1];

        try
        {
            while (!_stopping)
            {
                int read = stream.Read(buffer, 0, buffer.Length);
                if (read <= 0)
                {
                    Closed?.Invoke("remote shell closed the stream");
                    return;
                }

                int decoded = decoder.GetChars(buffer, 0, read, chars, 0, flush: false);
                if (decoded > 0)
                    DataReceived?.Invoke(new string(chars, 0, decoded), read);
            }
        }
        catch (ObjectDisposedException) when (_stopping)
        {
            // Normal teardown.
        }
        catch (Exception ex)
        {
            Closed?.Invoke(ex.Message);
        }
    }

    public void Dispose()
    {
        _stopping = true;
        try { _stream?.Dispose(); } catch { /* teardown */ }
        try { if (_client.IsConnected) _client.Disconnect(); } catch { /* teardown */ }
        _client.Dispose();
    }
}
