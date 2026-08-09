using System;
using mRemoteNG.Connection.Sftp;
using NUnit.Framework;

namespace mRemoteNGTests.Connection.Sftp;

/// <summary>
/// Remote paths are POSIX whatever the client runs on, so none of this can defer to
/// <see cref="System.IO.Path"/> — which on Windows would join with a backslash and treat a drive
/// letter as a root.
/// </summary>
[TestFixture]
public class SftpPathTests
{
    // ---- Combine ---------------------------------------------------------------

    [TestCase("/home/alice", "file.txt", "/home/alice/file.txt")]
    [TestCase("/", "file.txt", "/file.txt")]
    [TestCase("/home/alice/", "file.txt", "/home/alice/file.txt")]
    [TestCase("/home/alice", "sub/file.txt", "/home/alice/sub/file.txt")]
    public void CombineJoinsWithAForwardSlash(string directory, string name, string expected)
    {
        Assert.That(SftpPath.Combine(directory, name), Is.EqualTo(expected));
    }

    [Test]
    public void CombineNeverProducesABackslash()
    {
        Assert.That(SftpPath.Combine("/home/alice", "file.txt"), Does.Not.Contain("\\"));
    }

    [Test]
    public void AnAbsoluteNameReplacesTheDirectory()
    {
        Assert.That(SftpPath.Combine("/home/alice", "/etc/hosts"), Is.EqualTo("/etc/hosts"));
    }

    // ---- GetParent -------------------------------------------------------------

    [TestCase("/home/alice/file.txt", "/home/alice")]
    [TestCase("/home/alice", "/home")]
    [TestCase("/home", "/")]
    [TestCase("/home/alice/", "/home")]
    public void GetParentDropsTheLastSegment(string path, string expected)
    {
        Assert.That(SftpPath.GetParent(path), Is.EqualTo(expected));
    }

    [Test]
    public void TheRootIsItsOwnParent()
    {
        // Otherwise navigating up repeatedly walks off the top of the filesystem.
        Assert.That(SftpPath.GetParent("/"), Is.EqualTo("/"));
    }

    [Test]
    public void NavigatingUpRepeatedlyTerminatesAtTheRoot()
    {
        string path = "/a/b/c/d";
        for (int i = 0; i < 10; i++)
            path = SftpPath.GetParent(path);

        Assert.That(path, Is.EqualTo("/"));
    }

    // ---- GetName ---------------------------------------------------------------

    [TestCase("/home/alice/file.txt", "file.txt")]
    [TestCase("/home/alice", "alice")]
    [TestCase("/", "")]
    public void GetNameReturnsTheLastSegment(string path, string expected)
    {
        Assert.That(SftpPath.GetName(path), Is.EqualTo(expected));
    }

    // ---- Normalize -------------------------------------------------------------

    [TestCase("/home//alice", "/home/alice")]
    [TestCase("/home/alice/", "/home/alice")]
    [TestCase("//", "/")]
    [TestCase("", "/")]
    [TestCase("   ", "/")]
    [TestCase("/", "/")]
    public void NormalizeCollapsesSeparators(string path, string expected)
    {
        Assert.That(SftpPath.Normalize(path), Is.EqualTo(expected));
    }

    [Test]
    public void BackslashesTypedByAWindowsUserAreAccepted()
    {
        // The path box takes typed input and a Windows user will reach for a backslash. A unix
        // server can legally have one in a name, so this trades a rare correct case for a
        // common mistake.
        Assert.That(SftpPath.Normalize(@"\home\alice"), Is.EqualTo("/home/alice"));
    }

    /// <summary>
    /// The same trade applied to a path the server reported is not a trade, only damage: the entry
    /// would be recorded under a path that does not exist.
    /// </summary>
    [Test]
    public void AServerPathKeepsItsBackslashes()
    {
        Assert.That(SftpPath.NormalizeServerPath(@"/home/alice/a\b.txt"),
            Is.EqualTo(@"/home/alice/a\b.txt"));
    }

    [TestCase("/home//alice", "/home/alice")]
    [TestCase("/home/alice/", "/home/alice")]
    [TestCase("", "/")]
    [TestCase("   ", "/")]
    public void AServerPathIsStillTidied(string path, string expected)
    {
        Assert.That(SftpPath.NormalizeServerPath(path), Is.EqualTo(expected));
    }

    [Test]
    public void AServerPathRejectsNull() =>
        Assert.Throws<ArgumentNullException>(() => SftpPath.NormalizeServerPath(null!));

    // ---- IsRoot / argument validation -------------------------------------------

    [TestCase("/", true)]
    [TestCase("//", true)]
    [TestCase("/home", false)]
    public void IsRootRecognisesTheRoot(string path, bool expected)
    {
        Assert.That(SftpPath.IsRoot(path), Is.EqualTo(expected));
    }

    [Test]
    public void NullArgumentsAreRejected()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentNullException>(() => SftpPath.Combine(null!, "x"));
            Assert.Throws<ArgumentNullException>(() => SftpPath.Combine("/", null!));
            Assert.Throws<ArgumentNullException>(() => SftpPath.GetParent(null!));
            Assert.Throws<ArgumentNullException>(() => SftpPath.GetName(null!));
            Assert.Throws<ArgumentNullException>(() => SftpPath.Normalize(null!));
        });
    }
}