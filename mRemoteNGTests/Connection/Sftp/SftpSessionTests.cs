using System;
using System.Threading.Tasks;
using mRemoteNG.Connection.Sftp;
using mRemoteNG.Security.Ssh;
using NSubstitute;
using NUnit.Framework;
using Renci.SshNet.Sftp;

namespace mRemoteNGTests.Connection.Sftp
{
    /// <summary>
    /// Covers what can be asserted without a server: how library entries map onto the neutral
    /// model, and that a session which is not connected fails rather than serving stale results.
    /// No test here may require an SFTP server.
    /// </summary>
    [TestFixture]
    public class SftpSessionTests
    {
        // ---- mapping ---------------------------------------------------------------

        [Test]
        public void AFileMapsOntoTheNeutralModel()
        {
            DateTime modified = new(2026, 8, 9, 10, 30, 0, DateTimeKind.Local);
            ISftpFile file = FakeFile("notes.txt", "/home/alice/notes.txt", isDirectory: false,
                                      length: 4096, lastWrite: modified);

            SftpEntry entry = SftpSession.Describe(file);

            Assert.Multiple(() =>
            {
                Assert.That(entry.Name, Is.EqualTo("notes.txt"));
                Assert.That(entry.FullName, Is.EqualTo("/home/alice/notes.txt"));
                Assert.That(entry.IsDirectory, Is.False);
                Assert.That(entry.Length, Is.EqualTo(4096));
                Assert.That(entry.LastWriteTime, Is.EqualTo(modified));
            });
        }

        [Test]
        public void ADirectoryReportsNoLength()
        {
            // Servers report a size for directories and it means nothing useful; showing it in a
            // size column would just be noise.
            ISftpFile file = FakeFile("docs", "/home/alice/docs", isDirectory: true, length: 4096);

            Assert.That(SftpSession.Describe(file).Length, Is.Zero);
        }

        [Test]
        public void ATrailingSlashIsNormalisedAwayFromTheFullName()
        {
            ISftpFile file = FakeFile("docs", "/home/alice/docs/", isDirectory: true);

            Assert.That(SftpSession.Describe(file).FullName, Is.EqualTo("/home/alice/docs"));
        }

        // ---- permission rendering ---------------------------------------------------

        [Test]
        public void PermissionsAreRenderedTheWayLsDoes()
        {
            ISftpFile file = FakeFile("script.sh", "/home/alice/script.sh", isDirectory: false);
            file.OwnerCanRead.Returns(true);
            file.OwnerCanWrite.Returns(true);
            file.OwnerCanExecute.Returns(true);
            file.GroupCanRead.Returns(true);
            file.GroupCanExecute.Returns(true);
            file.OthersCanRead.Returns(true);

            Assert.That(SftpSession.FormatPermissions(file), Is.EqualTo("-rwxr-xr--"));
        }

        [Test]
        public void ADirectoryIsMarkedWithD()
        {
            ISftpFile file = FakeFile("docs", "/docs", isDirectory: true);
            file.OwnerCanRead.Returns(true);
            file.OwnerCanWrite.Returns(true);
            file.OwnerCanExecute.Returns(true);

            Assert.That(SftpSession.FormatPermissions(file), Does.StartWith("d"));
        }

        [Test]
        public void ASymbolicLinkIsMarkedWithL()
        {
            ISftpFile file = FakeFile("link", "/link", isDirectory: false);
            file.IsSymbolicLink.Returns(true);

            Assert.That(SftpSession.FormatPermissions(file), Does.StartWith("l"));
        }

        [Test]
        public void NoPermissionsRendersAsAllDashes()
        {
            ISftpFile file = FakeFile("locked", "/locked", isDirectory: false);

            Assert.That(SftpSession.FormatPermissions(file), Is.EqualTo("----------"));
        }

        [Test]
        public void ThePermissionStringIsAlwaysTenCharacters()
        {
            ISftpFile file = FakeFile("x", "/x", isDirectory: false);
            file.GroupCanWrite.Returns(true);

            Assert.That(SftpSession.FormatPermissions(file), Has.Length.EqualTo(10));
        }

        // ---- not connected -----------------------------------------------------------

        [Test]
        public void AFreshSessionIsNotConnected()
        {
            using SftpSession session = NewSession();

            Assert.That(session.IsConnected, Is.False);
        }

        [Test]
        public void ListingWhileDisconnectedFailsRatherThanReturningStaleContents()
        {
            // Serving a listing from a dropped session would present it as the current state of the
            // remote host when it is not, which is worse than an error.
            using SftpSession session = NewSession();

            Assert.ThrowsAsync<SftpSessionNotConnectedException>(
                async () => await session.ListDirectoryAsync("/home"));
        }

        [Test]
        public void EveryOperationRefusesWhileDisconnected()
        {
            using SftpSession session = NewSession();
            SftpEntry entry = new("f", "/f", false, false, 0, DateTime.Now, "----------");

            Assert.Multiple(() =>
            {
                Assert.ThrowsAsync<SftpSessionNotConnectedException>(async () => await session.RenameAsync("/a", "/b"));
                Assert.ThrowsAsync<SftpSessionNotConnectedException>(async () => await session.DeleteAsync(entry));
                Assert.ThrowsAsync<SftpSessionNotConnectedException>(async () => await session.CreateDirectoryAsync("/d"));
                Assert.ThrowsAsync<SftpSessionNotConnectedException>(async () => await session.CreateFileAsync("/f"));
                Assert.ThrowsAsync<SftpSessionNotConnectedException>(
                    async () => await session.UploadAsync(new System.IO.MemoryStream(), "/f"));
                Assert.ThrowsAsync<SftpSessionNotConnectedException>(
                    async () => await session.DownloadAsync(entry, new System.IO.MemoryStream()));
            });
        }

        [Test]
        public void TheHomeDirectoryDefaultsToTheRootBeforeConnecting()
        {
            using SftpSession session = NewSession();

            Assert.That(session.HomeDirectory, Is.EqualTo("/"));
        }

        // ---- lifetime -----------------------------------------------------------------

        [Test]
        public void DisposingScrubsTheCredentialItWasGiven()
        {
            ResolvedSshCredential credential = new("alice", secret: "secret123");
            char[] secret = credential.SecretBuffer!;

            using (SftpSession session = new("example-host", 22, credential))
            {
                Assert.That(session.IsConnected, Is.False);
            }

            Assert.That(secret, Is.All.EqualTo('\0'));
        }

        [Test]
        public void DisposingTwiceIsHarmless()
        {
            SftpSession session = NewSession();
            session.Dispose();

            Assert.DoesNotThrow(session.Dispose);
        }

        [Test]
        public void UsingADisposedSessionIsRejected()
        {
            SftpSession session = NewSession();
            session.Dispose();

            Assert.ThrowsAsync<ObjectDisposedException>(async () => await session.ListDirectoryAsync("/"));
        }

        [Test]
        public void NullArgumentsAreRejected()
        {
            Assert.Multiple(() =>
            {
                Assert.Throws<ArgumentNullException>(
                    () => new SftpSession(null!, 22, new ResolvedSshCredential("alice")));
                Assert.Throws<ArgumentNullException>(() => new SftpSession("host", 22, null!));
                Assert.Throws<ArgumentNullException>(() => SftpSession.Describe(null!));
            });
        }

        // ---- helpers -------------------------------------------------------------------

        private static SftpSession NewSession() =>
            new("example-host", 22, new ResolvedSshCredential("alice", secret: "secret123"));

        private static ISftpFile FakeFile(string name,
                                          string fullName,
                                          bool isDirectory,
                                          long length = 0,
                                          DateTime? lastWrite = null)
        {
            ISftpFile file = Substitute.For<ISftpFile>();
            file.Name.Returns(name);
            file.FullName.Returns(fullName);
            file.IsDirectory.Returns(isDirectory);
            file.IsRegularFile.Returns(!isDirectory);
            file.Length.Returns(length);
            file.LastWriteTime.Returns(lastWrite ?? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Local));
            return file;
        }
    }
}
