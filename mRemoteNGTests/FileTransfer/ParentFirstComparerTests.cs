using System;
using System.Runtime.Versioning;
using System.Windows.Forms;
using BrightIdeasSoftware;
using mRemoteNG.FileTransfer;
using mRemoteNG.UI.Controls.FileTransfer;
using NUnit.Framework;

namespace mRemoteNGTests.FileTransfer;

/// <summary>
/// Covers "the parent row stays at the top" in
/// <c>specs/file-manager-presentation/spec.md</c>.
/// </summary>
/// <remarks>
/// Builds list items directly rather than a populated control: an <see cref="OLVListItem"/> needs
/// no window handle, so the ordering rule can be asserted without a message pump.
/// </remarks>
[TestFixture]
[SupportedOSPlatform("windows")]
public class ParentFirstComparerTests
{
    private static OLVListItem Row(string name, bool isParent = false) =>
        new(new FileSystemEntry(name, "/" + name, IsDirectory: false, Length: 0,
            LastWriteTime: new DateTime(2026, 1, 1), Permissions: string.Empty,
            IsHidden: false, IsSymbolicLink: false, IsParentNavigation: isParent));

    private static ParentFirstComparer Comparer(SortOrder order)
    {
        OLVColumn column = new("Name", nameof(FileSystemEntry.Name))
        {
            AspectGetter = o => ((FileSystemEntry)o).Name
        };

        return new ParentFirstComparer(new ColumnComparer(column, order));
    }

    [Test]
    public void TheParentRowSortsBeforeAnEntryAscending()
    {
        ParentFirstComparer comparer = Comparer(SortOrder.Ascending);

        Assert.That(comparer.Compare(Row("..", isParent: true), Row("aaa")), Is.LessThan(0));
    }

    /// <summary>
    /// The direction that would otherwise bury it: descending by name puts <c>..</c> among the
    /// entries, so the navigation row moves depending on which header was last clicked.
    /// </summary>
    [Test]
    public void TheParentRowSortsBeforeAnEntryDescending()
    {
        ParentFirstComparer comparer = Comparer(SortOrder.Descending);

        Assert.That(comparer.Compare(Row("..", isParent: true), Row("zzz")), Is.LessThan(0));
    }

    [Test]
    public void AnEntrySortsAfterTheParentRow()
    {
        ParentFirstComparer comparer = Comparer(SortOrder.Ascending);

        Assert.That(comparer.Compare(Row("aaa"), Row("..", isParent: true)), Is.GreaterThan(0));
    }

    [Test]
    public void OrdinaryEntriesAreLeftToTheInnerComparer()
    {
        ParentFirstComparer comparer = Comparer(SortOrder.Ascending);

        Assert.That(comparer.Compare(Row("aaa"), Row("bbb")), Is.LessThan(0));
    }

    [Test]
    public void TwoParentRowsCompareEqual()
    {
        ParentFirstComparer comparer = Comparer(SortOrder.Ascending);

        Assert.That(comparer.Compare(Row("..", isParent: true), Row("..", isParent: true)), Is.Zero);
    }
}