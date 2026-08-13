using System;
using System.IO;
using mRemoteNG.App;
using mRemoteNG.App.Info;
using NUnit.Framework;

namespace mRemoteNGTests.App.Info;

/// <summary>
/// The edition is decided by a marker file beside the executable rather than by how the assembly was
/// compiled.
/// </summary>
/// <remarks>
/// The compile constant it replaces was defined by every configuration except `Release Installer`,
/// which is built by nothing — so every artefact this project shipped ran as the portable edition,
/// and everything gated on that was unreachable for every user. A switch nobody can see without
/// reading the csproj is the failure this file exists to keep from coming back.
/// </remarks>
[TestFixture]
public class PortableEditionTests
{
    private string _directory = "";

    [SetUp]
    public void Setup()
    {
        _directory = Path.Combine(Path.GetTempPath(), "mrng-edition-" + TestContext.CurrentContext.Test.ID);
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void Teardown()
    {
        PortableEdition.OverrideForTests(null);
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, true);
    }

    [Test]
    public void ADirectoryWithTheMarkerIsPortable()
    {
        File.WriteAllText(Path.Combine(_directory, PortableEdition.MarkerFileName), "");

        Assert.That(PortableEdition.HasMarker(_directory));
    }

    [Test]
    public void ADirectoryWithoutTheMarkerIsNot()
    {
        Assert.That(PortableEdition.HasMarker(_directory), Is.False);
    }

    [Test]
    public void AnEmptyMarkerIsEnough()
    {
        // Presence is the whole signal. Requiring content would mean a file that looks right and is
        // ignored, and the person who created it would have no way to tell which.
        File.WriteAllText(Path.Combine(_directory, PortableEdition.MarkerFileName), "");

        Assert.That(PortableEdition.HasMarker(_directory));
    }

    [Test]
    public void TheMarkerIsMatchedWithoutRegardToCase()
    {
        // Windows paths are case-insensitive, so PORTABLE.FLAG has to work. Asserted because the
        // check is File.Exists rather than a comparison anyone chose, and it would be easy to
        // replace it later with one that is not.
        File.WriteAllText(Path.Combine(_directory, "PORTABLE.FLAG"), "");

        Assert.That(PortableEdition.HasMarker(_directory));
    }

    [Test]
    public void ADirectoryOfADifferentNameIsNotAMarker()
    {
        File.WriteAllText(Path.Combine(_directory, "portable.txt"), "");
        File.WriteAllText(Path.Combine(_directory, "portable"), "");

        Assert.That(PortableEdition.HasMarker(_directory), Is.False);
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase(null)]
    public void AnUnknownDirectoryIsNotPortable(string? directory)
    {
        // Installed is the recoverable way to be wrong: settings land under the user's profile,
        // which is writable and visible. The other direction has an installed edition writing beside
        // an executable that may sit in Program Files and failing well after the cause.
        Assert.That(PortableEdition.HasMarker(directory), Is.False);
    }

    [Test]
    public void TheMarkerIsLookedForBesideTheExecutable()
    {
        // Not in the settings folder, which is one of the things this decides — looking there first
        // would be circular.
        Assert.That(Path.GetFileName(PortableEdition.MarkerPath), Is.EqualTo(PortableEdition.MarkerFileName));
        Assert.That(Path.GetDirectoryName(PortableEdition.MarkerPath), Is.Not.Empty);
    }

    [Test]
    public void RuntimeReadsTheEditionFromHere()
    {
        // The seam every other portable-dependent behaviour goes through. If these two ever disagree
        // there are two answers to one question, which is what the compile constant already was.
        PortableEdition.OverrideForTests(true);
        Assert.That(Runtime.IsPortableEdition);

        PortableEdition.OverrideForTests(false);
        Assert.That(Runtime.IsPortableEdition, Is.False);
    }
}
