using System.Collections;
using System.Runtime.Versioning;
using BrightIdeasSoftware;
using mRemoteNG.FileTransfer;

namespace mRemoteNG.UI.Controls.FileTransfer;

/// <summary>
/// Keeps the <c>..</c> row above everything, whatever the user sorted by.
/// </summary>
/// <remarks>
/// <see cref="ObjectListView"/> sorts model objects, so a parent row left to itself lands in the
/// middle of a descending sort by name — a navigation control that moves around the list depending
/// on which header was last clicked. Everything that is not the parent row is handed straight to
/// the comparer the list would have used anyway.
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class ParentFirstComparer(IComparer inner) : IComparer
{
    public int Compare(object? x, object? y)
    {
        bool xIsParent = IsParent(x);
        bool yIsParent = IsParent(y);

        if (xIsParent || yIsParent)
            return xIsParent && yIsParent ? 0 : xIsParent ? -1 : 1;

        return inner.Compare(x, y);
    }

    private static bool IsParent(object? item) =>
        item is OLVListItem { RowObject: FileSystemEntry { IsParentNavigation: true } };
}