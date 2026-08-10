using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace mRemoteNGTests.Security;

/// <summary>
/// Guards which code is allowed to reach for the legacy cryptography provider.
/// </summary>
/// <remarks>
/// Six settings secrets ended up on an unsalted-MD5 provider because every call site constructed one
/// inline and nothing said not to. A review found them once; this keeps them found. A new settings
/// secret added the same way fails here rather than shipping.
///
/// These read the source tree, so they need the test assembly to sit inside the repository — which
/// it does for the normal build and for CI. A build redirected elsewhere (for instance to a temp
/// <c>BaseOutputPath</c>, which is how you build while mRemoteNG is running and holding <c>bin\</c>)
/// fails them with "could not locate the repository root". That is deliberate: a guard that cannot
/// run should say so rather than pass quietly, because a security check that silently skips is worse
/// than one that does not exist.
/// </remarks>
[TestFixture]
public class SettingsSecretCallSiteTests
{
    /// <summary>
    /// Files permitted to construct <c>LegacyRijndaelCryptographyProvider</c>, and why.
    /// </summary>
    private static readonly Dictionary<string, string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Security/SymmetricEncryption/LegacyRijndaelCryptographyProvider.cs"] = "the provider itself",
        ["Security/SettingsSecretProtector.cs"] = "owns the decrypt-only fallback for unmarked values",
        ["Security/Factories/CryptoProviderFactoryFromXml.cs"] = "connection file, chosen by what the file records",
        ["Security/Factories/LegacyInsecureCryptoProviderFactory.cs"] = "connection file factory",
        ["Config/Serializers/XmlConnectionsDecryptor.cs"] = "connection file; replace-default-connection-file-key owns it",
        ["Config/Connections/SqlConnectionsSaver.cs"] = "SQL backend; encrypt-sql-backend-with-aead owns it",
        ["Config/Serializers/ConnectionSerializers/Sql/SqlDatabaseMetaDataRetriever.cs"] = "SQL backend",
        ["Connection/ConnectionsService.cs"] = "SQL backend loader",
        ["Config/Settings/Registry/OptRegistryCredentialsPage.cs"] = "registry provisioning, a documented external format",
        ["Config/Settings/Registry/OptRegistrySqlServerPage.cs"] = "registry provisioning",
        ["Config/Settings/Registry/OptRegistryUpdatesPage.cs"] = "registry provisioning",
        ["UI/Forms/OptionsPages/SecurityPage.cs"] = "password generator that produces the provisioning format",
    };

    private static DirectoryInfo ProjectDirectory
    {
        get
        {
            DirectoryInfo? directory = new(TestContext.CurrentContext.TestDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "mRemoteNG.sln")))
                directory = directory.Parent;

            Assert.That(directory, Is.Not.Null, "could not locate the repository root from the test directory");
            return new DirectoryInfo(Path.Combine(directory!.FullName, "mRemoteNG"));
        }
    }

    [Test]
    public void OnlyApprovedCallSitesConstructTheLegacyProvider()
    {
        List<string> offenders = [];

        foreach (string file in Directory.EnumerateFiles(ProjectDirectory.FullName, "*.cs", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(ProjectDirectory.FullName, file).Replace('\\', '/');

            if (relative.StartsWith("bin/", StringComparison.OrdinalIgnoreCase) ||
                relative.StartsWith("obj/", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!File.ReadAllText(file).Contains("LegacyRijndaelCryptographyProvider", StringComparison.Ordinal))
                continue;

            if (!Allowed.ContainsKey(relative))
                offenders.Add(relative);
        }

        Assert.That(offenders, Is.Empty,
            "these reach the legacy provider directly. A settings secret belongs on SettingsSecretProtector; " +
            "if the file genuinely owns a connection file, the SQL backend or the provisioning format, add it " +
            "to the allow list with the reason.");
    }

    [Test]
    public void EveryApprovedCallSiteStillExists()
    {
        // Keeps the list honest: an entry left behind after a file moves would silently widen it.
        List<string> missing = Allowed.Keys
            .Where(relative => !File.Exists(Path.Combine(ProjectDirectory.FullName, relative)))
            .ToList();

        Assert.That(missing, Is.Empty, "allow-list entries that no longer name a real file");
    }

    /// <summary>
    /// Settings secrets travel with a portable installation and are read during startup and during
    /// a connection attempt — points where no recovery prompt belongs. A machine-bound protector
    /// would break the portable edition on the second machine it reached, silently.
    /// </summary>
    [Test]
    public void SettingsSecretsAreNotBoundToTheMachine()
    {
        string protector = File.ReadAllText(Path.Combine(ProjectDirectory.FullName, "Security/SettingsSecretProtector.cs"));

        Assert.That(protector, Does.Not.Contain("ProtectedData"),
            "replace-default-connection-file-key introduces per-user data protection for the connection " +
            "file; extending it to settings secrets would break the portable edition");
    }
}
