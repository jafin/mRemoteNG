using System;
using System.Globalization;
using mRemoteNG.FileTransfer;
using NUnit.Framework;

namespace mRemoteNGTests.FileTransfer;

/// <summary>
/// Covers the presentation requirements in
/// <c>specs/file-manager-presentation/spec.md</c>: kind, size, and date alignment.
/// </summary>
[TestFixture]
public class EntryPresentationTests
{
    private static FileSystemEntry Entry(string name = "file.txt",
        bool isDirectory = false,
        bool isLink = false,
        bool isParent = false,
        long length = 100) =>
        new(name, "/" + name, isDirectory, length, new DateTime(2026, 1, 2, 3, 4, 0),
            string.Empty, IsHidden: false, IsSymbolicLink: isLink, IsParentNavigation: isParent);

    // ---- kind --------------------------------------------------------------------

    [Test]
    public void ADirectoryIsAFolder() =>
        Assert.That(EntryPresentation.KindOf(Entry(isDirectory: true)), Is.EqualTo(EntryKind.Folder));

    [Test]
    public void APlainEntryIsAFile() =>
        Assert.That(EntryPresentation.KindOf(Entry()), Is.EqualTo(EntryKind.File));

    [Test]
    public void ALinkIsALink() =>
        Assert.That(EntryPresentation.KindOf(Entry(isLink: true)), Is.EqualTo(EntryKind.Link));

    /// <summary>
    /// A link is a link whichever it points at. Telling those apart costs a following stat per
    /// entry, which is a round trip each to answer a question the column does not ask.
    /// </summary>
    [Test]
    public void ALinkToADirectoryIsStillReportedAsALink() =>
        Assert.That(EntryPresentation.KindOf(Entry(isDirectory: true, isLink: true)),
            Is.EqualTo(EntryKind.Link));

    [Test]
    public void TheParentRowHasNoKind() =>
        Assert.That(EntryPresentation.KindOf(Entry("..", isDirectory: true, isParent: true)), Is.Null);

    // ---- size --------------------------------------------------------------------

    /// <summary>
    /// SFTP reports zero for a directory. Rendering that zero would state something false: that
    /// the directory was measured and found empty.
    /// </summary>
    [Test]
    public void ADirectoryHasNoSize() =>
        Assert.That(EntryPresentation.SizeOf(Entry(isDirectory: true)), Is.Null);

    [Test]
    public void TheParentRowHasNoSize() =>
        Assert.That(EntryPresentation.SizeOf(Entry("..", isDirectory: true, isParent: true)), Is.Null);

    [Test]
    public void AFileHasItsLength() =>
        Assert.That(EntryPresentation.SizeOf(Entry(length: 4096)), Is.EqualTo(4096L));

    [Test]
    public void NoSizeRendersAsNothing() =>
        Assert.That(EntryPresentation.DescribeSize(null), Is.Empty);

    /// <summary>An empty file really is zero bytes, and says so — unlike a directory.</summary>
    [Test]
    public void AnEmptyFileRendersAsZeroBytes() =>
        Assert.That(EntryPresentation.DescribeSize(0), Is.EqualTo("0 B"));

    [Test]
    public void LargerSizesUseLargerUnits() =>
        Assert.That(EntryPresentation.DescribeSize(2048), Does.Contain("KB"));

    // ---- dates -------------------------------------------------------------------

    [Test]
    public void ABritishPatternPadsAndKeepsDayFirst()
    {
        string pattern = EntryPresentation.AlignedDateTimePattern(new CultureInfo("en-GB"));

        Assert.Multiple(() =>
        {
            Assert.That(pattern, Does.Contain("dd"));
            Assert.That(pattern, Does.Contain("MM"));
            Assert.That(pattern.IndexOf("dd", StringComparison.Ordinal),
                Is.LessThan(pattern.IndexOf("MM", StringComparison.Ordinal)));
        });
    }

    /// <summary>
    /// Padding must not reorder anything. Aligning a column is not worth showing an American user
    /// a British date.
    /// </summary>
    [Test]
    public void AnAmericanPatternPadsAndKeepsMonthFirst()
    {
        string pattern = EntryPresentation.AlignedDateTimePattern(new CultureInfo("en-US"));

        Assert.That(pattern.IndexOf("MM", StringComparison.Ordinal),
            Is.LessThan(pattern.IndexOf("dd", StringComparison.Ordinal)));
    }

    [Test]
    public void AlreadyPaddedFieldsAreLeftAlone()
    {
        CultureInfo culture = new("en-GB");
        string once = EntryPresentation.AlignedDateTimePattern(culture);
        string twice = EntryPresentation.AlignedDateTimePattern(culture);

        Assert.Multiple(() =>
        {
            Assert.That(once, Is.EqualTo(twice));
            Assert.That(once, Does.Not.Contain("ddd"));
            Assert.That(once, Does.Not.Contain("MMM"));
        });
    }

    [Test]
    public void ASingleDigitDayAndMonthAreRenderedPadded()
    {
        string text = EntryPresentation.DescribeModified(new DateTime(2026, 1, 2, 3, 4, 0),
            new CultureInfo("en-GB"));

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("02"));
            Assert.That(text, Does.Contain("01"));
        });
    }

    [Test]
    public void NoDateRendersAsNothing() =>
        Assert.That(EntryPresentation.DescribeModified(null, CultureInfo.InvariantCulture), Is.Empty);

    [Test]
    public void TheParentRowHasNoDate() =>
        Assert.That(EntryPresentation.ModifiedOf(Entry("..", isDirectory: true, isParent: true)), Is.Null);

    /// <summary>
    /// A quoted literal in a culture's pattern is text, not a field. Rewriting a quoted 'd' would
    /// change what the date says rather than how it is spaced.
    /// </summary>
    [Test]
    public void QuotedLiteralsAreNotTreatedAsFields()
    {
        CultureInfo culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.DateTimeFormat.ShortDatePattern = "d 'de' M yyyy";
        culture.DateTimeFormat.ShortTimePattern = "HH:mm";

        string pattern = EntryPresentation.AlignedDateTimePattern(culture);

        Assert.Multiple(() =>
        {
            Assert.That(pattern, Does.Contain("'de'"));
            Assert.That(pattern, Does.StartWith("dd"));
            Assert.That(pattern, Does.Contain("MM"));
        });
    }

    // ---- icons -------------------------------------------------------------------

    [Test]
    public void ADirectoryGetsTheFolderIcon() =>
        Assert.That(EntryPresentation.ImageKeyOf(Entry(isDirectory: true)),
            Is.EqualTo(EntryPresentation.FolderImageKey));

    [Test]
    public void AFileGetsTheFileIcon() =>
        Assert.That(EntryPresentation.ImageKeyOf(Entry()), Is.EqualTo(EntryPresentation.FileImageKey));

    [Test]
    public void ALinkStillGetsAnIcon() =>
        Assert.That(EntryPresentation.ImageKeyOf(Entry(isLink: true)), Is.Not.Null);
}