using System;
using System.IO;
using System.Reflection;

namespace mRemoteNG.App.Info;

/// <summary>
/// Whether this installation is the portable edition, decided by a marker file beside the
/// executable rather than by how the assembly was compiled.
/// </summary>
/// <remarks>
/// <para>
/// A compile constant made the edition a property of the <i>build</i>, and the build that everything
/// ships is <c>Release</c> — which defined <c>PORTABLE</c>. So every artefact this project produced,
/// including the MSI, ran as the portable edition, and anything that turns on
/// <see cref="Runtime.IsPortableEdition"/> was unreachable for every user. That is a poor place for a
/// switch nobody can see: telling the two editions apart meant reading the csproj.
/// </para>
/// <para>
/// A marker file is visible, checkable, and reversible without a compiler. It also lets one build
/// serve both editions, which is what packaging actually wants — the zip carries the marker and the
/// installer does not.
/// </para>
/// <para>
/// <b>Resolved once.</b> The answer decides where settings, logs and layouts are read from and
/// written to, so a value that changed mid-run would split a session's state across two locations.
/// Creating or deleting the marker takes effect at the next start, which is also the only point at
/// which a user could sensibly mean it.
/// </para>
/// <para>
/// <b>This is not a security boundary.</b> It selects where files live and whether a connection file
/// is given a machine-bound protector; it decrypts nothing and unlocks nothing. Someone who can write
/// the marker beside the executable can also replace the executable, so nothing here is worth
/// defending against them. What it does deserve is the note in
/// <see cref="Security.FileProtection.MachineProtectorPolicy"/>: a store that has already been
/// protected keeps the protectors it was given, so adding the marker cannot take an existing
/// machine protector away.
/// </para>
/// </remarks>
public static class PortableEdition
{
    /// <summary>
    /// The marker. Beside the executable, not in the settings folder — the settings folder's own
    /// location is one of the things this decides, so looking there first would be circular.
    /// </summary>
    public const string MarkerFileName = "portable.flag";

    private static readonly Lazy<bool> Detected = new(Detect);

    private static bool? _overrideForTests;

    /// <summary>
    /// True when the marker is present beside the executable, or when the assembly was compiled with
    /// the legacy <c>PORTABLE</c> constant.
    /// </summary>
    /// <remarks>
    /// The compiled constant is kept deliberately, and only as a transition. `build.ps1 -Portable`
    /// and the `Release Portable` configuration still define it, so dropping it here before
    /// packaging learns to write the marker would turn every portable artefact into an installed one
    /// — silently, and in the direction that moves a user's settings out from under them. It comes
    /// out once a portable zip ships with <see cref="MarkerFileName"/> in it.
    /// </remarks>
    public static bool IsPortable => _overrideForTests ?? Detected.Value;

    /// <summary>Where the marker is looked for, whether or not one is there.</summary>
    public static string MarkerPath =>
        Path.Combine(ExecutableDirectory(), MarkerFileName);

    private static bool Detect()
    {
#if PORTABLE
        return true;
#else
        return HasMarker(ExecutableDirectory());
#endif
    }

    /// <summary>
    /// Whether a directory carries the marker. Separate from <see cref="Detect"/> so the rule can be
    /// tested against a directory of the test's choosing — the real one is the test runner's, which
    /// no test should be dropping marker files into.
    /// </summary>
    internal static bool HasMarker(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return false;

        try
        {
            return File.Exists(Path.Combine(directory, MarkerFileName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or NotSupportedException or ArgumentException)
        {
            // Unreadable resolves to installed, which is the recoverable direction: a portable user
            // who lands here finds their settings in %APPDATA% and can say so, where the reverse
            // would have an installed user writing beside an executable in Program Files and failing
            // later, further from the cause.
            return false;
        }
    }

    private static string ExecutableDirectory() =>
        Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location
                              ?? Assembly.GetExecutingAssembly().Location) ?? string.Empty;

    /// <summary>
    /// Forces the answer for the duration of a test. Null restores real detection.
    /// </summary>
    /// <remarks>
    /// Needed because the real answer depends on the test runner's own directory, which no test
    /// should be writing marker files into.
    /// </remarks>
    internal static void OverrideForTests(bool? isPortable) => _overrideForTests = isPortable;
}
