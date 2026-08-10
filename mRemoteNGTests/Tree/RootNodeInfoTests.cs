using mRemoteNG.Security;
using mRemoteNG.Tree;
using mRemoteNG.Tree.Root;
using NUnit.Framework;


namespace mRemoteNGTests.Tree;

public class RootNodeInfoTests
{
    private RootNodeInfo _rootNodeInfo;

    [SetUp]
    public void Setup()
    {
        _rootNodeInfo = new RootNodeInfo(RootNodeType.Connection);
    }

    [Test]
    public void AutoLockOnMinimizeIsDisabledByDefault()
    {
        Assert.That(_rootNodeInfo.AutoLockOnMinimize, Is.False);
    }

    [Test]
    public void DefaultPasswordReturnsExpectedValue()
    {
        var defaultPassword = _rootNodeInfo.DefaultPassword;
        Assert.That(defaultPassword, Is.EqualTo("mR3m"));
    }

    [TestCase("a", true)]
    [TestCase("mR3m", false)]
    [TestCase("", false)]
    [TestCase(null, false)]
    public void PasswordPropertyReflectsWhetherACustomPasswordIsInUse(string password, bool expected)
    {
        _rootNodeInfo.PasswordString = password;
        Assert.That(_rootNodeInfo.Password, Is.EqualTo(expected));
    }

    [TestCase("")]
    [TestCase(null)]
    public void PasswordStringReturnsDefaultPasswordWhenNoCustomOneIsSet(string password)
    {
        _rootNodeInfo.PasswordString = password;
        Assert.That(_rootNodeInfo.PasswordString, Is.EqualTo(_rootNodeInfo.DefaultPassword));
    }

    [TestCase("a")]
    [TestCase("1234")]
    public void PasswordStringReturnsCustomPassword(string password)
    {
        _rootNodeInfo.PasswordString = password;
        Assert.That(_rootNodeInfo.PasswordString, Is.EqualTo(password));
    }

    [Test]
    public void PasswordStringReturnsDefaultWhenPasswordPropertySetWithoutPasswordString()
    {
        // Edge case: Password property set to true directly without setting PasswordString
        _rootNodeInfo.Password = true;
        Assert.That(_rootNodeInfo.PasswordString, Is.EqualTo(_rootNodeInfo.DefaultPassword));
    }

    [Test]
    public void IsPasswordMatchReturnsTrueForDefaultPasswordWhenNoCustomPasswordSet()
    {
        Assert.That(_rootNodeInfo.IsPasswordMatch(_rootNodeInfo.DefaultPassword.ConvertToSecureString()), Is.True);
    }

    [Test]
    public void IsPasswordMatchReturnsTrueForCustomPasswordEvenWhenPasswordFlagIsFalse()
    {
        _rootNodeInfo.PasswordString = "custom";
        _rootNodeInfo.Password = false;

        Assert.That(_rootNodeInfo.IsPasswordMatch("custom".ConvertToSecureString()), Is.True);
    }

    [Test]
    public void IsPasswordMatchReturnsFalseForWrongPassword()
    {
        _rootNodeInfo.PasswordString = "custom";

        Assert.That(_rootNodeInfo.IsPasswordMatch("wrong".ConvertToSecureString()), Is.False);
    }

    [Test]
    public void IsPasswordMatchReturnsFalseForNullPassword()
    {
        Assert.That(_rootNodeInfo.IsPasswordMatch(null), Is.False);
    }

    /// <summary>
    /// These properties are persisted, so an edit to one has to reach SaveConnectionsOnEdit
    /// like any other. They were plain auto-properties — <c>Password</c> hiding the base
    /// property that does notify — so setting or clearing the master password changed the
    /// model and saved nothing, leaving the file encrypted under the previous key.
    /// </summary>
    [Test]
    public void SettingPasswordProtectionRaisesPropertyChanged()
    {
        var raised = new System.Collections.Generic.List<string?>();
        _rootNodeInfo.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        _rootNodeInfo.Password = true;

        Assert.That(raised, Does.Contain(nameof(RootNodeInfo.Password)));
    }

    [Test]
    public void ClearingPasswordProtectionRaisesPropertyChanged()
    {
        _rootNodeInfo.PasswordString = "custom";

        var raised = new System.Collections.Generic.List<string?>();
        _rootNodeInfo.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        _rootNodeInfo.PasswordString = "";

        Assert.That(raised, Does.Contain(nameof(RootNodeInfo.Password)));
        Assert.That(raised, Does.Contain(nameof(RootNodeInfo.PasswordString)));
    }

    [Test]
    public void ChangingThePasswordItselfRaisesPropertyChanged()
    {
        _rootNodeInfo.PasswordString = "first";

        var raised = new System.Collections.Generic.List<string?>();
        _rootNodeInfo.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        // Still protected either way, so Password does not change — only the key does, and
        // the file has to be rewritten under it.
        _rootNodeInfo.PasswordString = "second";

        Assert.That(raised, Does.Contain(nameof(RootNodeInfo.PasswordString)));
    }

    [Test]
    public void SettingAPropertyToItsCurrentValueRaisesNothing()
    {
        _rootNodeInfo.Password = true;

        var raised = new System.Collections.Generic.List<string?>();
        _rootNodeInfo.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        _rootNodeInfo.Password = true;

        Assert.That(raised, Is.Empty);
    }

    [Test]
    public void RenamingTheRootRaisesPropertyChanged()
    {
        var raised = new System.Collections.Generic.List<string?>();
        _rootNodeInfo.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        _rootNodeInfo.Name = "Renamed";

        Assert.That(raised, Does.Contain(nameof(RootNodeInfo.Name)));
    }

    [Test]
    public void EnablingTotpRaisesPropertyChanged()
    {
        var raised = new System.Collections.Generic.List<string?>();
        _rootNodeInfo.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        _rootNodeInfo.TotpEnabled = true;
        _rootNodeInfo.TotpSecret = "ABCDEF";
        _rootNodeInfo.AutoLockOnMinimize = true;

        Assert.That(raised, Does.Contain(nameof(RootNodeInfo.TotpEnabled)));
        Assert.That(raised, Does.Contain(nameof(RootNodeInfo.TotpSecret)));
        Assert.That(raised, Does.Contain(nameof(RootNodeInfo.AutoLockOnMinimize)));
    }

    [TestCase(RootNodeType.Connection, TreeNodeType.Root)]
    [TestCase(RootNodeType.PuttySessions, TreeNodeType.PuttyRoot)]
    public void RootNodeHasCorrectTreeNodeType(RootNodeType rootNodeType, TreeNodeType expectedTreeNodeType)
    {
        var rootNode = new RootNodeInfo(rootNodeType);
        Assert.That(rootNode.GetTreeNodeType(), Is.EqualTo(expectedTreeNodeType));
    }
}