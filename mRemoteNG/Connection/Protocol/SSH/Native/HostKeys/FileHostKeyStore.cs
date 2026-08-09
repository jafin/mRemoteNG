using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Versioning;
using System.Threading;
using mRemoteNG.App;
using mRemoteNG.App.Info;
using mRemoteNG.Messages;

namespace mRemoteNG.Connection.Protocol.SSH.Native.HostKeys;

/// <summary>
/// Accepted host keys, in a tab-separated file mRemoteNG owns.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately <b>not</b> OpenSSH's <c>known_hosts</c>. Reusing it would be friendlier — a host
/// already trusted in a terminal would not be asked about again — but it means writing a file
/// another tool owns, in a format with hashed hostnames, certificate authority markers and
/// revocation entries, any of which we could corrupt while meaning well. Owning a small file is
/// the reversible choice; adopting someone else's is not.
/// </para>
/// <para>
/// Reading <c>known_hosts</c> to avoid a first-connection prompt is a strictly additive follow-up
/// and does not require touching this decision.
/// </para>
/// <para>
/// The format is one entry per line, tab separated, so it can be inspected and edited by hand:
/// <c>host	port	algorithm	fingerprint</c>. Fingerprints are not secret; the file needs no
/// encryption, only integrity, and a plain text file a user can audit is worth more here than an
/// opaque one they cannot.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class FileHostKeyStore : IHostKeyStore
{
    private const string FileName = "hostkeys.txt";

    private readonly string _path;
    private readonly Lock _gate = new();

    public FileHostKeyStore()
        : this(Path.Combine(SettingsFileInfo.SettingsPath, FileName))
    {
    }

    public FileHostKeyStore(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        _path = path;
    }

    public string? Find(string host, int port, string keyAlgorithm)
    {
        lock (_gate)
        {
            foreach ((string Host, int Port, string Algorithm, string Fingerprint) entry in ReadAll())
            {
                if (Matches(entry, host, port, keyAlgorithm))
                    return entry.Fingerprint;
            }
        }

        return null;
    }

    public void Save(string host, int port, string keyAlgorithm, string fingerprint)
    {
        lock (_gate)
        {
            List<(string Host, int Port, string Algorithm, string Fingerprint)> entries = [.. ReadAll()];

            // A changed key replaces its predecessor rather than being appended. Two entries for
            // one host would make Find order-dependent, and the older one would keep winning.
            entries.RemoveAll(e => Matches(e, host, port, keyAlgorithm));
            entries.Add((host, port, keyAlgorithm, fingerprint));

            try
            {
                string? directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                List<string> lines =
                [
                    "# mRemoteNG accepted SSH host keys. One entry per line, tab separated:",
                    "# host<TAB>port<TAB>algorithm<TAB>fingerprint",
                    "# Deleting a line makes mRemoteNG ask about that host again.",
                ];

                foreach ((string Host, int Port, string Algorithm, string Fingerprint) e in entries)
                {
                    lines.Add(string.Join('\t',
                        e.Host, e.Port.ToString(CultureInfo.InvariantCulture), e.Algorithm, e.Fingerprint));
                }

                File.WriteAllLines(_path, lines);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Losing the record means being asked again next time, which is safe. Failing the
                // connection over it would not be.
                Runtime.MessageCollector.AddExceptionStackTrace(
                    "Could not record the accepted SSH host key.", ex);
            }
        }
    }

    private static bool Matches(
        (string Host, int Port, string Algorithm, string Fingerprint) entry,
        string host, int port, string keyAlgorithm) =>
        // Host names are case-insensitive; the algorithm name is a protocol identifier and is not.
        string.Equals(entry.Host, host, StringComparison.OrdinalIgnoreCase) &&
        entry.Port == port &&
        string.Equals(entry.Algorithm, keyAlgorithm, StringComparison.Ordinal);

    private IEnumerable<(string Host, int Port, string Algorithm, string Fingerprint)> ReadAll()
    {
        string[] lines;
        try
        {
            if (!File.Exists(_path))
                yield break;

            lines = File.ReadAllLines(_path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Runtime.MessageCollector.AddMessage(MessageClass.WarningMsg,
                "Could not read the SSH host key store; hosts will be presented for confirmation again.");
            yield break;
        }

        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                continue;

            string[] parts = line.Split('\t');
            if (parts.Length != 4)
                continue;

            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int port))
                continue;

            yield return (parts[0], port, parts[2], parts[3]);
        }
    }
}
