using System;
using mRemoteNG.FileTransfer;
using NUnit.Framework;

namespace mRemoteNGTests.FileTransfer;

/// <summary>
/// Covers the back/forward scenarios in <c>specs/sftp-browser-panel/spec.md</c>. Each pane keeps
/// its own instance, which is what makes them navigate independently.
/// </summary>
[TestFixture]
public class NavigationHistoryTests
{
    private static readonly string[] AbdTrail = ["/a", "/b", "/d"];

    private NavigationHistory _history = null!;

    [SetUp]
    public void Setup() => _history = new NavigationHistory(caseSensitive: true);

    [Test]
    public void AFreshHistoryHasNowhereToGo()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_history.Current, Is.Null);
            Assert.That(_history.CanGoBack, Is.False);
            Assert.That(_history.CanGoForward, Is.False);
        });
    }

    [Test]
    public void NavigatingSetsTheCurrentPath()
    {
        _history.Navigate("/home");

        Assert.That(_history.Current, Is.EqualTo("/home"));
    }

    [Test]
    public void BackReturnsToThePreviousPath()
    {
        _history.Navigate("/home");
        _history.Navigate("/home/alice");

        Assert.Multiple(() =>
        {
            Assert.That(_history.Back(), Is.EqualTo("/home"));
            Assert.That(_history.Current, Is.EqualTo("/home"));
        });
    }

    [Test]
    public void ForwardReturnsToWhereBackCameFrom()
    {
        _history.Navigate("/home");
        _history.Navigate("/home/alice");
        _history.Back();

        Assert.That(_history.Forward(), Is.EqualTo("/home/alice"));
    }

    [Test]
    public void BackAtTheStartDoesNothing()
    {
        _history.Navigate("/home");

        Assert.Multiple(() =>
        {
            Assert.That(_history.Back(), Is.Null);
            Assert.That(_history.Current, Is.EqualTo("/home"));
        });
    }

    [Test]
    public void ForwardAtTheEndDoesNothing()
    {
        _history.Navigate("/home");

        Assert.That(_history.Forward(), Is.Null);
    }

    [Test]
    public void NavigatingAfterGoingBackDiscardsTheForwardTrail()
    {
        // Browser semantics. Without this, Forward would offer a branch the user has left.
        _history.Navigate("/a");
        _history.Navigate("/b");
        _history.Navigate("/c");
        _history.Back();

        _history.Navigate("/d");

        Assert.Multiple(() =>
        {
            Assert.That(_history.CanGoForward, Is.False);
            Assert.That(_history.Entries, Is.EqualTo(AbdTrail));
        });
    }

    [Test]
    public void NavigatingToTheCurrentPathIsNotRecorded()
    {
        // Otherwise refresh, or re-selecting the current directory, fills the history with
        // duplicates and Back appears to do nothing.
        _history.Navigate("/home");

        bool changed = _history.Navigate("/home");

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.False);
            Assert.That(_history.Entries, Has.Count.EqualTo(1));
            Assert.That(_history.CanGoBack, Is.False);
        });
    }

    [Test]
    public void ACaseSensitiveHistoryTreatsCaseAsADifferentPlace()
    {
        _history.Navigate("/Home");
        _history.Navigate("/home");

        Assert.That(_history.Entries, Has.Count.EqualTo(2));
    }

    [Test]
    public void ACaseInsensitiveHistoryDoesNot()
    {
        // Local Windows paths: C:\Users and C:\users are the same directory, and recording both
        // would make Back step to a place the user never left.
        NavigationHistory local = new(caseSensitive: false);
        local.Navigate(@"C:\Users");
        local.Navigate(@"C:\users");

        Assert.That(local.Entries, Has.Count.EqualTo(1));
    }

    [Test]
    public void ClearingResetsEverything()
    {
        _history.Navigate("/a");
        _history.Navigate("/b");

        _history.Clear();

        Assert.Multiple(() =>
        {
            Assert.That(_history.Current, Is.Null);
            Assert.That(_history.CanGoBack, Is.False);
            Assert.That(_history.Entries, Is.Empty);
        });
    }

    [Test]
    public void TwoHistoriesAreIndependent()
    {
        NavigationHistory other = new(caseSensitive: true);

        _history.Navigate("/local");
        other.Navigate("/remote");

        Assert.Multiple(() =>
        {
            Assert.That(_history.Current, Is.EqualTo("/local"));
            Assert.That(other.Current, Is.EqualTo("/remote"));
        });
    }

    [Test]
    public void ANullPathIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => _history.Navigate(null!));
    }
}