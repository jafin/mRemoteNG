using System;
using System.Text.RegularExpressions;

namespace mRemoteNG.App.Diagnostics;

/// <summary>
/// Redaction for text that leaves the user's own control, at two strengths.
/// </summary>
/// <remarks>
/// <para>
/// The two exist because the destinations differ. A debug report is written to be handed to someone
/// else, so it redacts everything that identifies the user or their estate. The application log is
/// the user's own file, read to work out what went wrong, and full redaction would take out the
/// hostnames that make it worth reading.
/// </para>
/// <para>
/// Path redaction applies to both: a Windows profile path carries the account name, and it appears
/// in text that has no other reason to identify anybody.
/// </para>
/// </remarks>
public static class DiagnosticTextSanitizer
{
    public const string RedactedValue = "<redacted>";

    private static readonly Regex CredentialPairRegex = new(
        @"\b(password|passphrase|pwd|token|secret|api[-_ ]?key|private[-_ ]?key|username|user|login|hostname|host|server|domain)\b\s*[:=]\s*([^\s,;]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// The userinfo of a URI — <c>scheme://user:password@host</c> — both halves at once.
    /// </summary>
    /// <remarks>
    /// Run before the account and hostname matchers, because it is the only one that can see where
    /// the credential ends. <see cref="UserAtHostRegex"/> catches the password in the easy case, by
    /// mistaking it for the local part of an address, but it stops at the first character outside
    /// <c>[\w.-]</c> — so a password with punctuation in it survived, and the username always did.
    /// <para>
    /// The <c>@</c> is matched greedily so a password containing one is consumed rather than
    /// treated as the separator.
    /// </para>
    /// </remarks>
    private static readonly Regex UriUserInfoRegex = new(
        @"([a-zA-Z][a-zA-Z0-9+.\-]*://)[^/\s]*@",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex Ipv4AddressRegex = new(
        @"\b(?:\d{1,3}\.){3}\d{1,3}\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex DomainUserRegex = new(
        @"\b[\w.-]+\\[\w.-]+\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex UserAtHostRegex = new(
        @"\b[\w.-]+@[\w.-]+\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex HostnameRegex = new(
        @"\b(?:[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?\.)+[A-Za-z]{2,63}\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Replaces the current user's profile directory with <c>%USERPROFILE%</c>.
    /// </summary>
    /// <remarks>
    /// The minimum that should apply to anything written to disk. The profile path contains the
    /// Windows account name, which nothing being logged needs.
    /// </remarks>
    public static string RedactUserPaths(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        string userProfilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        return string.IsNullOrWhiteSpace(userProfilePath)
            ? value
            : value.Replace(userProfilePath, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Full redaction for text intended to be shared: paths, credential pairs, accounts, addresses
    /// and hostnames.
    /// </summary>
    public static string Redact(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        string sanitized = RedactUserPaths(value);

        sanitized = CredentialPairRegex.Replace(sanitized, match => $"{match.Groups[1].Value}={RedactedValue}");
        sanitized = UriUserInfoRegex.Replace(sanitized, $"$1{RedactedValue}@");
        sanitized = DomainUserRegex.Replace(sanitized, RedactedValue);
        sanitized = UserAtHostRegex.Replace(sanitized, RedactedValue);
        sanitized = Ipv4AddressRegex.Replace(sanitized, RedactedValue);
        sanitized = HostnameRegex.Replace(sanitized, RedactedValue);

        return sanitized;
    }
}
