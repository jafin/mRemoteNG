using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using mRemoteNG.Connection.Sftp;
using NUnit.Framework;

namespace mRemoteNGTests.IntegrationTests.Sftp;

/// <summary>
/// What a real server returns for a directory, and what the session makes of it.
/// </summary>
/// <remarks>
/// <c>Describe</c> and <c>FormatPermissions</c> are unit-tested against a faked <c>ISftpFile</c>,
/// which proves the mapping is self-consistent and nothing about whether it matches what a server
/// actually sends. That is what these add.
/// </remarks>
[TestFixture]
[SupportedOSPlatform("windows")]
public class SftpSessionListingTests : SftpIntegrationTestBase
{
    private static readonly string[] EveryEntry = ["file.txt", "sub", ".hidden"];

    [Test]
    public async Task TheWorkingDirectoryIsTheOneTheSessionCanSee()
    {
        // Guards the fixture rather than the session. The container and the chrooted session
        // address the same directory by different paths, and a setup that built the directory in
        // the wrong space would leave every other test in this file passing or failing for reasons
        // unrelated to what it claims to check.
        await ExecAsync("touch", ContainerPath("proof.txt"));

        SftpSession session = await ConnectedSessionAsync();
        IReadOnlyList<SftpEntry> entries = await session.ListDirectoryAsync(RemoteDirectory);

        Assert.That(entries.Select(e => e.Name), Does.Contain("proof.txt"));
    }

    [Test]
    public async Task EveryEntryComesBackWithItsKindAndPath()
    {
        await ExecAsync("sh", "-c",
            $"cd {ContainerDirectory} && echo -n 'twelve bytes' > file.txt && mkdir sub && touch .hidden");

        SftpSession session = await ConnectedSessionAsync();
        Dictionary<string, SftpEntry> byName =
            (await session.ListDirectoryAsync(RemoteDirectory)).ToDictionary(e => e.Name, StringComparer.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(byName.Keys, Is.EquivalentTo(EveryEntry),
                "a dot-file is an entry like any other; hiding it is the panel's decision, not the session's");

            Assert.That(byName["file.txt"].IsDirectory, Is.False);
            Assert.That(byName["file.txt"].Length, Is.EqualTo(12));
            Assert.That(byName["file.txt"].FullName, Is.EqualTo(RemotePath("file.txt")),
                "the full name is the server's path, which is what every later operation addresses");

            Assert.That(byName["sub"].IsDirectory, Is.True);
            Assert.That(byName["sub"].Length, Is.Zero,
                "servers report a size for directories and it means nothing worth showing");

            // Unix convention, applied to names a real server sent. SFTP carries no "hidden"
            // attribute, so the leading dot is the whole of the rule.
            Assert.That(byName[".hidden"].IsHidden, Is.True);
            Assert.That(byName["file.txt"].IsHidden, Is.False);
            Assert.That(byName["sub"].IsHidden, Is.False);
        });
    }

    [Test]
    public async Task TheCurrentAndParentDirectoriesAreNotReturned()
    {
        SftpSession session = await ConnectedSessionAsync();
        IReadOnlyList<SftpEntry> entries = await session.ListDirectoryAsync(RemoteDirectory);

        Assert.That(entries.Select(e => e.Name), Has.No.Member(".").And.No.Member(".."),
            "the panel navigates with its own controls, so the server's own entries are noise");
    }

    [Test]
    public async Task ThePermissionStringMatchesTheModeTheServerReports()
    {
        // FormatPermissions assembles the ls -l form from individual booleans, so it can be
        // self-consistently wrong. This is the only check that it agrees with the server.
        await ExecAsync("sh", "-c",
            $"cd {ContainerDirectory} && touch readable.txt && chmod 640 readable.txt "
            + "&& mkdir walkable && chmod 755 walkable");

        SftpSession session = await ConnectedSessionAsync();
        Dictionary<string, SftpEntry> byName =
            (await session.ListDirectoryAsync(RemoteDirectory)).ToDictionary(e => e.Name, StringComparer.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(byName["readable.txt"].Permissions, Is.EqualTo("-rw-r-----"));
            Assert.That(byName["walkable"].Permissions, Is.EqualTo("drwxr-xr-x"));
        });
    }

    [Test]
    public void ListingAPathThatIsNotThereSurfacesTheFailure()
    {
        // The session's contract is to report the server's refusal rather than an empty directory,
        // which would read as "this folder exists and holds nothing".
        SftpSession session = NewSession();

        Assert.ThrowsAsync<Renci.SshNet.Common.SftpPathNotFoundException>(async () =>
        {
            await session.ConnectAsync();
            await session.ListDirectoryAsync(RemotePath("no-such-directory"));
        });
    }
}
