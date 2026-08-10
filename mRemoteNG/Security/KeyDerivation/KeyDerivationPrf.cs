using System;
using System.Security.Cryptography;

namespace mRemoteNG.Security.KeyDerivation;

/// <summary>
/// The PBKDF2 pseudo-random functions a connection file may record, and how they are written.
/// </summary>
/// <remarks>
/// <para>
/// The file already records its iteration count, because a file outlives the build that wrote it and
/// a parameter read from configuration at open time cannot be changed without making every existing
/// file unreadable. The PRF is the other half of the same pair and was left implicit.
/// </para>
/// <para>
/// <b>SHA-1 is the default, deliberately.</b> Every file written before this was recorded used it,
/// and this fork shares its file with upstream mRemoteNG, which ignores the attribute and would
/// derive with SHA-1 regardless. A caller that does not think about this therefore produces a file
/// everything can still read; only the hardened path opts into SHA-256, where the 600,000 iterations
/// already configured is the figure OWASP gives for that function rather than roughly half of what
/// SHA-1 asks for.
/// </para>
/// </remarks>
public static class KeyDerivationPrf
{
    /// <summary>The root attribute recording the function.</summary>
    public const string AttributeName = "KdfPrf";

    /// <summary>What a file that records nothing was written with.</summary>
    public static readonly HashAlgorithmName Default = HashAlgorithmName.SHA1;

    /// <summary>What the hardened format uses.</summary>
    public static readonly HashAlgorithmName Hardened = HashAlgorithmName.SHA256;

    public static bool IsSupported(HashAlgorithmName function) =>
        function == HashAlgorithmName.SHA1 ||
        function == HashAlgorithmName.SHA256 ||
        function == HashAlgorithmName.SHA512;

    /// <summary>
    /// Reads a recorded function. Absent, empty or unrecognised means SHA-1.
    /// </summary>
    /// <remarks>
    /// Unrecognised is not an error. Absence has to keep meaning SHA-1 or every file written before
    /// this change stops opening, and a value this build does not know is in the same position: the
    /// safest reading is the one that was true for fifteen years.
    /// </remarks>
    public static HashAlgorithmName Parse(string? recordedValue)
    {
        if (string.IsNullOrWhiteSpace(recordedValue))
            return Default;

        HashAlgorithmName parsed = new(recordedValue.Trim().ToUpperInvariant());
        return IsSupported(parsed) ? parsed : Default;
    }

    /// <summary>The value to record, or null when nothing should be written.</summary>
    /// <remarks>
    /// SHA-1 records nothing, so a classic file comes out byte-compatible with what upstream writes.
    /// </remarks>
    public static string? ToRecordedValue(HashAlgorithmName function) =>
        function == Default ? null : function.Name;
}
