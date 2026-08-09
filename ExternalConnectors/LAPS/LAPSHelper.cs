using System.DirectoryServices;
using System.Runtime.Versioning;
using System.Text.Json;

namespace ExternalConnectors.LAPS;

/// <summary>
/// Queries Active Directory for LAPS (Local Administrator Password Solution) credentials.
/// Supports both legacy LAPS (ms-Mcs-AdmPwd) and Windows LAPS (ms-LAPS-Password).
/// </summary>
[SupportedOSPlatform("windows")]
public static class LAPSHelper
{
    private const string LegacyLapsAttribute = "ms-Mcs-AdmPwd";
    private const string WindowsLapsAttribute = "ms-LAPS-Password";
    private const string WindowsLapsEncryptedAttribute = "ms-LAPS-EncryptedPassword";

    /// <summary>
    /// Queries AD for the LAPS-managed local administrator password of the specified computer.
    /// </summary>
    /// <param name="hostname">The hostname (computer name) to look up in AD. Can also be an LDAP path override.</param>
    /// <param name="userName">On return, set to the local Administrator account name (hostname\Administrator).</param>
    /// <param name="password">On return, the LAPS-managed password.</param>
    /// <param name="domain">On return, set to the computer hostname (local account domain).</param>
    public static void QueryLAPSPassword(string hostname, out string userName, out string password, out string domain)
    {
        userName = string.Empty;
        password = string.Empty;
        domain = string.Empty;

        if (string.IsNullOrWhiteSpace(hostname))
            throw new LapsException("Hostname is empty. Cannot query LAPS without a target computer name.");

        var computerName = hostname.Trim();

        // Search for the computer object in AD
        using DirectorySearcher searcher = new();
        searcher.Filter = $"(&(objectClass=computer)(cn={EscapeLdapFilter(computerName)}))";
        searcher.PropertiesToLoad.AddRange([LegacyLapsAttribute, WindowsLapsAttribute, WindowsLapsEncryptedAttribute, "cn", "dNSHostName"]);
        searcher.SearchScope = SearchScope.Subtree;

        var result = searcher.FindOne();
        if (result == null)
            throw new LapsException($"Computer '{computerName}' not found in Active Directory.");

        // Try Windows LAPS first (ms-LAPS-Password), then legacy LAPS (ms-Mcs-AdmPwd)
        string? lapsPassword = null;
        var lapsUserName = "Administrator";

        // Windows LAPS stores JSON: {"n":"Administrator","t":"...","p":"password"}
        if (result.Properties[WindowsLapsAttribute].Count > 0)
        {
            var jsonValue = result.Properties[WindowsLapsAttribute][0].ToString();
            if (!string.IsNullOrEmpty(jsonValue))
            {
                (lapsUserName, lapsPassword) = ParseWindowsLapsJson(jsonValue);
            }
        }

        // Fall back to legacy LAPS (plain text password in ms-Mcs-AdmPwd)
        if (string.IsNullOrEmpty(lapsPassword) && result.Properties[LegacyLapsAttribute].Count > 0)
        {
            lapsPassword = result.Properties[LegacyLapsAttribute][0].ToString();
        }

        if (string.IsNullOrEmpty(lapsPassword))
        {
            // Check if encrypted LAPS attribute exists (we can't decrypt it, but we can inform the user)
            if (result.Properties[WindowsLapsEncryptedAttribute].Count > 0)
                throw new LapsException($"Computer '{computerName}' has encrypted LAPS password (ms-LAPS-EncryptedPassword). Only unencrypted LAPS passwords are supported. Check your LAPS policy configuration.");

            throw new LapsException($"No LAPS password found for computer '{computerName}'. Ensure LAPS is configured and the current user has permission to read the LAPS password attribute.");
        }

        userName = lapsUserName;
        password = lapsPassword;
        domain = computerName;
    }

    /// <summary>
    /// Parses the Windows LAPS JSON format: {"n":"AccountName","t":"hex-timestamp","p":"password"}
    /// </summary>
    private static (string userName, string password) ParseWindowsLapsJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var name = root.TryGetProperty("n", out var nProp) ? nProp.GetString() ?? "Administrator" : "Administrator";
            var pwd = root.TryGetProperty("p", out var pProp) ? pProp.GetString() ?? string.Empty : string.Empty;

            return (name, pwd);
        }
        catch (JsonException)
        {
            // If JSON parsing fails, treat the whole string as the password
            return ("Administrator", json);
        }
    }

    /// <summary>
    /// Escapes special characters in LDAP filter values per RFC 4515.
    /// </summary>
    private static string EscapeLdapFilter(string value)
    {
        return value
            .Replace("\\", "\\5c")
            .Replace("*", "\\2a")
            .Replace("(", "\\28")
            .Replace(")", "\\29")
            .Replace("\0", "\\00");
    }
}

public class LapsException(string message) : Exception(message);
