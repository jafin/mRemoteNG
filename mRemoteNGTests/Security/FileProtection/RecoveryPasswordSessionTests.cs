using System.Security;
using mRemoteNG.Security;
using mRemoteNG.Security.FileProtection;
using NUnit.Framework;

namespace mRemoteNGTests.Security.FileProtection;

/// <summary>
/// The recovery password kept for the run, so a store that is read more than once is not asked about
/// more than once.
/// </summary>
/// <remarks>
/// The store is re-read on more paths than a user would expect — after an external change, and on the
/// automatic backup-recovery path. Prompting each time turns a password meant to be typed rarely into
/// one typed constantly, which is how a user ends up choosing a short one.
/// </remarks>
[TestFixture]
public class RecoveryPasswordSessionTests
{
    [SetUp]
    [TearDown]
    public void Reset() => RecoveryPasswordSession.Clear();

    [Test]
    public void NothingIsRememberedUntilSomethingOpensTheStore()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RecoveryPasswordSession.HasRemembered, Is.False);
            Assert.That(RecoveryPasswordSession.Peek(), Is.Null);
        });
    }

    [Test]
    public void APasswordThatOpenedTheStoreComesBack()
    {
        RecoveryPasswordSession.Remember("recovery".ConvertToSecureString());

        using SecureString? remembered = RecoveryPasswordSession.Peek();

        Assert.That(remembered?.ConvertToUnsecureString(), Is.EqualTo("recovery"));
    }

    [Test]
    public void TheCallerKeepsOwnershipOfWhatItHandedOver()
    {
        // The requestor's contract everywhere else in this codebase is that the caller owns what it
        // returned, so remembering has to copy rather than capture.
        SecureString original = "recovery".ConvertToSecureString();
        RecoveryPasswordSession.Remember(original);
        original.Dispose();

        using SecureString? remembered = RecoveryPasswordSession.Peek();

        Assert.That(remembered?.ConvertToUnsecureString(), Is.EqualTo("recovery"));
    }

    [Test]
    public void EachPeekHandsBackSomethingTheCallerMayDispose()
    {
        RecoveryPasswordSession.Remember("recovery".ConvertToSecureString());

        RecoveryPasswordSession.Peek()!.Dispose();

        using SecureString? second = RecoveryPasswordSession.Peek();
        Assert.That(second?.ConvertToUnsecureString(), Is.EqualTo("recovery"),
            "disposing one copy does not empty the session");
    }

    [Test]
    public void LockingTheStoreForgetsIt()
    {
        // What stops this defeating AutoLockOnMinimize, whose whole purpose is that walking away
        // requires re-authentication.
        RecoveryPasswordSession.Remember("recovery".ConvertToSecureString());

        RecoveryPasswordSession.Clear();

        Assert.Multiple(() =>
        {
            Assert.That(RecoveryPasswordSession.HasRemembered, Is.False);
            Assert.That(RecoveryPasswordSession.Peek(), Is.Null);
        });
    }

    [Test]
    public void AnEmptyPasswordIsNotWorthRemembering()
    {
        RecoveryPasswordSession.Remember(new SecureString());

        Assert.That(RecoveryPasswordSession.HasRemembered, Is.False);
    }

    [Test]
    public void ALaterPasswordReplacesTheEarlierOne()
    {
        RecoveryPasswordSession.Remember("first".ConvertToSecureString());
        RecoveryPasswordSession.Remember("second".ConvertToSecureString());

        using SecureString? remembered = RecoveryPasswordSession.Peek();

        Assert.That(remembered?.ConvertToUnsecureString(), Is.EqualTo("second"));
    }
}
