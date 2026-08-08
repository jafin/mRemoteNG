using System;
using mRemoteNG.Connection.Sftp;
using NUnit.Framework;

namespace mRemoteNGTests.Connection.Sftp
{
    [TestFixture]
    public class SftpEntryTests
    {
        [TestCase(".bashrc", true)]
        [TestCase(".ssh", true)]
        [TestCase("notes.txt", false)]
        [TestCase("", false)]
        public void ADotPrefixMarksAnEntryHidden(string name, bool expected)
        {
            Assert.That(Entry(name).IsHidden, Is.EqualTo(expected));
        }

        [TestCase(".")]
        [TestCase("..")]
        public void TheCurrentAndParentEntriesAreNotTreatedAsHidden(string name)
        {
            // They are filtered out for being pseudo-entries, not for being dot-files. Conflating
            // the two would make them reappear whenever hidden entries are shown.
            SftpEntry entry = Entry(name);

            Assert.Multiple(() =>
            {
                Assert.That(entry.IsCurrentOrParentDirectory, Is.True);
                Assert.That(entry.IsHidden, Is.False);
            });
        }

        [TestCase("notes.txt", false)]
        [TestCase("...", false)]
        public void OrdinaryNamesAreNotPseudoEntries(string name, bool expected)
        {
            Assert.That(Entry(name).IsCurrentOrParentDirectory, Is.EqualTo(expected));
        }

        [Test]
        public void EntriesAreComparedByValue()
        {
            DateTime when = new(2026, 8, 9, 12, 0, 0, DateTimeKind.Local);

            Assert.That(new SftpEntry("f", "/f", false, false, 1, when, "-rw-r--r--"),
                        Is.EqualTo(new SftpEntry("f", "/f", false, false, 1, when, "-rw-r--r--")));
        }

        private static SftpEntry Entry(string name) =>
            new(name, "/home/alice/" + name, false, false, 0,
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Local), "----------");
    }
}
