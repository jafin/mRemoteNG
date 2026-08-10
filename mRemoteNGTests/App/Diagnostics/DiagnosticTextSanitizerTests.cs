using System;
using mRemoteNG.App.Diagnostics;
using NUnit.Framework;

namespace mRemoteNGTests.App.Diagnostics;

[TestFixture]
public class DiagnosticTextSanitizerTests
{
    private static string UserProfile => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    [Test]
    public void RedactUserPathsReplacesTheProfileDirectory()
    {
        string value = $"Command Line: mRemoteNG.exe /cons:{UserProfile}\\Documents\\confCons.xml";

        string sanitized = DiagnosticTextSanitizer.RedactUserPaths(value);

        Assert.Multiple(() =>
        {
            Assert.That(sanitized, Does.Contain("%USERPROFILE%"));
            Assert.That(sanitized, Does.Not.Contain(UserProfile));
        });
    }

    [Test]
    public void RedactUserPathsIsCaseInsensitive()
    {
        string sanitized = DiagnosticTextSanitizer.RedactUserPaths(UserProfile.ToUpperInvariant() + "\\x.xml");

        Assert.That(sanitized, Does.StartWith("%USERPROFILE%"));
    }

    /// <summary>
    /// The startup log keeps hostnames on purpose. Stripping them from one line while every
    /// connection message below still carries them would cost the log its use and hide nothing.
    /// </summary>
    [Test]
    public void RedactUserPathsLeavesHostnamesAlone()
    {
        string sanitized = DiagnosticTextSanitizer.RedactUserPaths("mRemoteNG.exe -qc:alice@server.example");

        Assert.That(sanitized, Does.Contain("alice@server.example"));
    }

    [Test]
    public void RedactRemovesIdentifiersForSharedReports()
    {
        string sanitized = DiagnosticTextSanitizer.Redact("connect alice@server.example 10.1.2.3 CORP\\bob");

        Assert.Multiple(() =>
        {
            Assert.That(sanitized, Does.Not.Contain("alice@server.example"));
            Assert.That(sanitized, Does.Not.Contain("10.1.2.3"));
            Assert.That(sanitized, Does.Not.Contain("CORP\\bob"));
            Assert.That(sanitized, Does.Contain(DiagnosticTextSanitizer.RedactedValue));
        });
    }

    /// <summary>
    /// A connection string in a debug report carries both halves of a credential in one token.
    /// </summary>
    [TestCase("ssh://admin:hunter2@server.example.com", "admin", "hunter2")]
    [TestCase("https://svcacct:p@ssw0rd!@intranet.example.com/path", "svcacct", "p@ssw0rd!")]
    [TestCase("rdp://CORPUSER:Tr0ub4dor&3@10.1.2.3:3389", "CORPUSER", "Tr0ub4dor&3")]
    public void RedactRemovesBothHalvesOfUriUserinfo(string value, string user, string password)
    {
        string sanitized = DiagnosticTextSanitizer.Redact(value);

        Assert.Multiple(() =>
        {
            Assert.That(sanitized, Does.Not.Contain(password), "the password must not survive");
            Assert.That(sanitized, Does.Not.Contain(user), "the username must not survive");
        });
    }

    [Test]
    public void RedactAlsoReplacesTheProfileDirectory()
    {
        string sanitized = DiagnosticTextSanitizer.Redact($"{UserProfile}\\notes.txt");

        Assert.That(sanitized, Does.Not.Contain(UserProfile));
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase(null)]
    public void EmptyInputIsReturnedUnchanged(string? value)
    {
        Assert.Multiple(() =>
        {
            Assert.That(DiagnosticTextSanitizer.RedactUserPaths(value!), Is.EqualTo(value));
            Assert.That(DiagnosticTextSanitizer.Redact(value!), Is.EqualTo(value));
        });
    }
}
