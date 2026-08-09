using System;
using System.Runtime.Versioning;
using System.Windows.Forms;
using BrightIdeasSoftware;
using mRemoteNG.FileTransfer;
using mRemoteNG.UI.Controls.FileTransfer;
using NUnit.Framework;

namespace mRemoteNGTests.FileTransfer;

/// <summary>
/// Guards the icons in <c>specs/file-manager-presentation/spec.md</c>.
/// </summary>
/// <remarks>
/// Written after shipping a pane whose icons never appeared. The column had always asked for them
/// by key and the image list was built correctly; the list was attached through the inherited
/// <c>ListView.SmallImageList</c> property, which this <see cref="ObjectListView"/> shadows behind
/// <c>Get</c>/<c>SetSmallImageList</c>. Every key then resolved against a null shadow and silently
/// produced no image — code that reads as correct and draws nothing.
/// </remarks>
[TestFixture]
[SupportedOSPlatform("windows")]
public class PaneIconTests
{
    private static FileSystemEntry Entry(bool isDirectory = false, bool isParent = false) =>
        new("x", "/x", isDirectory, 0, new DateTime(2026, 1, 1), string.Empty,
            IsHidden: false, IsSymbolicLink: false, IsParentNavigation: isParent);

    /// <summary>
    /// The assertion that would have caught the bug: attached where the control actually looks,
    /// not merely where it was assigned.
    /// </summary>
    [Test]
    public void TheImageListIsAttachedWhereTheListLooksForIt()
    {
        using ObjectListView list = new();
        using ImageList icons = new();

        FilePaneControl.ConfigureIcons(list, icons);

        Assert.That(list.GetSmallImageList(), Is.SameAs(icons));
    }

    [Test]
    public void EveryImageKeyTheEntriesUseIsRegistered()
    {
        using ObjectListView list = new();
        using ImageList icons = new();

        FilePaneControl.ConfigureIcons(list, icons);

        Assert.Multiple(() =>
        {
            foreach (FileSystemEntry entry in new[]
                     {
                         Entry(isDirectory: true),
                         Entry(),
                         Entry(isDirectory: true, isParent: true)
                     })
            {
                string? key = EntryPresentation.ImageKeyOf(entry);

                Assert.That(key, Is.Not.Null);
                Assert.That(icons.Images.ContainsKey(key!), Is.True,
                    $"no image registered for key '{key}'");
            }
        });
    }

    /// <summary>
    /// A key the list cannot resolve draws nothing, which is exactly how the original bug looked.
    /// </summary>
    [Test]
    public void EveryImageKeyResolvesToARealIndex()
    {
        using ObjectListView list = new();
        using ImageList icons = new();

        FilePaneControl.ConfigureIcons(list, icons);

        Assert.Multiple(() =>
        {
            Assert.That(icons.Images.IndexOfKey(EntryPresentation.FolderImageKey), Is.GreaterThanOrEqualTo(0));
            Assert.That(icons.Images.IndexOfKey(EntryPresentation.FileImageKey), Is.GreaterThanOrEqualTo(0));
            Assert.That(icons.Images.IndexOfKey(EntryPresentation.ParentImageKey), Is.GreaterThanOrEqualTo(0));
        });
    }
}