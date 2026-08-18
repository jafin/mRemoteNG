using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Security;
using System.Windows.Forms;
using mRemoteNG.Config.DatabaseConnectors;
using mRemoteNG.Config.Serializers.ConnectionSerializers.Sql;
using mRemoteNG.Config.Serializers.Versioning;
using mRemoteNG.Resources.Language;
using mRemoteNG.Security;
using mRemoteNG.Security.Factories;
using mRemoteNG.Security.SymmetricEncryption;
using mRemoteNG.Tools;
using mRemoteNG.Tree.Root;
using mRemoteNG.UI;
using NSubstitute;
using NUnit.Framework;

namespace mRemoteNGTests.UI;

/// <summary>
/// What the SQL encryption upgrade says, and what it does when the answer is no.
/// </summary>
/// <remarks>
/// <para>
/// The upgrade itself is tested against a real database. What is tested here is the half that
/// decides whether it runs at all — and the text, because the text is most of the value. This
/// operation cannot be undone from inside mRemoteNG and locks out every client that has not taken
/// the change, so a confirmation that fails to say so is a defect in the same way a wrong query is.
/// </para>
/// <para>
/// <b>Everything that reaches a database goes through a substitute here.</b> The point is to prove
/// that the refusal paths never get that far: a test needing a server to find out that the user said
/// no would not be run often enough to catch a regression on the path where they said no.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
[TestFixture]
public class SqlDatabaseEncryptionUpgradePromptTests
{
    private Func<IDatabaseConnector> _originalConnector = null!;
    private ISqlDatabaseMetaDataRetriever _originalRetriever = null!;
    private Func<string, Optional<SecureString>> _originalPasswordPrompt = null!;
    private Func<string, Optional<SecureString>> _originalNewPasswordPrompt = null!;
    private Action<Control?, string, string> _originalShowMessage = null!;
    private Func<Control?, string, bool> _originalConfirm = null!;

    private ISqlDatabaseMetaDataRetriever _retriever = null!;
    private readonly List<string> _messagesShown = [];
    private int _confirmationsAsked;
    private int _passwordsAsked;
    private int _newPasswordsAsked;

    [SetUp]
    public void Setup()
    {
        _originalConnector = SqlDatabaseEncryptionUpgradePrompt.Connector;
        _originalRetriever = SqlDatabaseEncryptionUpgradePrompt.MetaDataRetriever;
        _originalPasswordPrompt = SqlDatabaseEncryptionUpgradePrompt.PasswordPrompt;
        _originalNewPasswordPrompt = SqlDatabaseEncryptionUpgradePrompt.NewPasswordPrompt;
        _originalShowMessage = SqlDatabaseEncryptionUpgradePrompt.ShowMessage;
        _originalConfirm = SqlDatabaseEncryptionUpgradePrompt.Confirm;

        _messagesShown.Clear();
        _confirmationsAsked = 0;
        _passwordsAsked = 0;
        _newPasswordsAsked = 0;

        _retriever = Substitute.For<ISqlDatabaseMetaDataRetriever>();

        SqlDatabaseEncryptionUpgradePrompt.Connector = () => Substitute.For<IDatabaseConnector>();
        SqlDatabaseEncryptionUpgradePrompt.MetaDataRetriever = _retriever;
        SqlDatabaseEncryptionUpgradePrompt.ShowMessage = (_, text, _) => _messagesShown.Add(text);

        SqlDatabaseEncryptionUpgradePrompt.Confirm = (_, _) =>
        {
            _confirmationsAsked++;
            return true;
        };

        SqlDatabaseEncryptionUpgradePrompt.PasswordPrompt = _ =>
        {
            _passwordsAsked++;
            return Optional<SecureString>.Empty;
        };

        SqlDatabaseEncryptionUpgradePrompt.NewPasswordPrompt = _ =>
        {
            _newPasswordsAsked++;
            return Optional<SecureString>.Empty;
        };
    }

    [TearDown]
    public void Teardown()
    {
        SqlDatabaseEncryptionUpgradePrompt.Connector = _originalConnector;
        SqlDatabaseEncryptionUpgradePrompt.MetaDataRetriever = _originalRetriever;
        SqlDatabaseEncryptionUpgradePrompt.PasswordPrompt = _originalPasswordPrompt;
        SqlDatabaseEncryptionUpgradePrompt.NewPasswordPrompt = _originalNewPasswordPrompt;
        SqlDatabaseEncryptionUpgradePrompt.ShowMessage = _originalShowMessage;
        SqlDatabaseEncryptionUpgradePrompt.Confirm = _originalConfirm;
    }

    [Test]
    public void TheConfirmationSaysWhoCanNoLongerOpenTheDatabase()
    {
        // Task 4.4, asserted on the words rather than on the resource keys. A rewording that drops
        // any of these is exactly the change this test exists to stop: nobody can discover for
        // themselves, before acting, that an upgrade here reaches colleagues who never installed
        // this fork and cannot be walked back.
        string explanation = SqlDatabaseEncryptionUpgradePrompt.BuildExplanation(settingMasterPassword: false);

        Assert.Multiple(() =>
        {
            Assert.That(explanation, Does.Contain("mRemoteNG"),
                "the clients that stop working are named");
            Assert.That(explanation, Does.Contain("colleagues"),
                "and that this decides for other people, not only for the person clicking");
            Assert.That(explanation, Does.Contain("cannot be undone"),
                "and that there is no way back from inside the application");
            Assert.That(explanation, Does.Contain("Back the database up"),
                "and what to do about that before continuing");
        });
    }

    [Test]
    public void SettingAMasterPasswordSaysWhatThatCostsBeforeItIsSet()
    {
        // A database with no master password gets one here, and that lands on people who are not in
        // the room: everyone who uses it needs the password, a human has to hand it out, and losing
        // it loses the connections outright — where today they are recoverable precisely because the
        // key is public. Said at the confirmation and not at the password box, because by the time a
        // box is on screen the decision has already been taken.
        string explanation = SqlDatabaseEncryptionUpgradePrompt.BuildExplanation(settingMasterPassword: true);

        Assert.Multiple(() =>
        {
            Assert.That(explanation, Does.Contain("give it to them"),
                "that distributing the password is the administrator's job");
            Assert.That(explanation, Does.Contain("nobody can decrypt"),
                "and that a lost master password is not recoverable");
            Assert.That(explanation, Does.Contain("Back the database up"),
                "without having lost anything the other warning said");
        });
    }

    [Test]
    public void ADatabaseWithNoMasterPasswordIsAskedToSetOne()
    {
        // The upgrade sets a master password rather than re-encrypting under the published default
        // key. Which prompt appears is the whole difference: one recalls a password, the other
        // chooses one and verifies it by re-entry.
        _retriever.GetDatabaseMetaData(Arg.Any<IDatabaseConnector>())
            .Returns(MetaDataAt(SqlDatabaseVersionVerifier.SchemaVersion));

        SqlDatabaseEncryptionUpgradePrompt.Ask(null);

        Assert.Multiple(() =>
        {
            Assert.That(_newPasswordsAsked, Is.EqualTo(1), "asked to choose one");
            Assert.That(_passwordsAsked, Is.Zero, "and not asked to recall one it does not have");
        });
    }

    [Test]
    public void RefusingToSetAMasterPasswordAbandonsTheUpgrade()
    {
        // Not the generic decline message. Somebody who expected this to proceed without a password
        // has to be told that it cannot: upgrading the cipher while keeping the published key would
        // leave every secret exactly as readable as it is today.
        _retriever.GetDatabaseMetaData(Arg.Any<IDatabaseConnector>())
            .Returns(MetaDataAt(SqlDatabaseVersionVerifier.SchemaVersion));

        SqlDatabaseEncryptionUpgradePrompt.Ask(null);

        Assert.Multiple(() =>
        {
            Assert.That(_messagesShown, Is.EqualTo(new[] { Language.SqlUpgradePasswordNotSet }));
            _retriever.DidNotReceive().WriteDatabaseMetaData(Arg.Any<RootNodeInfo>(),
                Arg.Any<IDatabaseConnector>(), Arg.Any<System.Data.Common.DbTransaction?>(),
                Arg.Any<Version?>());
        });
    }

    [Test]
    public void AnAlreadyUpgradedDatabaseIsNeverOfferedTheChoice()
    {
        _retriever.GetDatabaseMetaData(Arg.Any<IDatabaseConnector>())
            .Returns(MetaDataAt(CryptoProviderFactoryFromSqlVersion.AuthenticatedEncryptionVersion));

        SqlDatabaseEncryptionUpgradePrompt.Ask(null);

        Assert.Multiple(() =>
        {
            Assert.That(_confirmationsAsked, Is.Zero, "no warning about a change with nothing to change");
            Assert.That(_messagesShown, Is.EqualTo(new[] { Language.SqlUpgradeNotNeeded }));
        });
    }

    [Test]
    public void ADatabaseWithNoConnectionsYetIsNeverOfferedTheChoice()
    {
        // Null metadata is a database this application has not written to. There is nothing to
        // re-encrypt, and the next save creates it at the authenticated version anyway.
        _retriever.GetDatabaseMetaData(Arg.Any<IDatabaseConnector>()).Returns((SqlConnectionListMetaData?)null);

        SqlDatabaseEncryptionUpgradePrompt.Ask(null);

        Assert.Multiple(() =>
        {
            Assert.That(_confirmationsAsked, Is.Zero);
            Assert.That(_messagesShown, Is.EqualTo(new[] { Language.SqlUpgradeNoDatabase }));
        });
    }

    [Test]
    public void DecliningTheConfirmationTouchesNothing()
    {
        _retriever.GetDatabaseMetaData(Arg.Any<IDatabaseConnector>())
            .Returns(MetaDataAt(SqlDatabaseVersionVerifier.SchemaVersion));
        SqlDatabaseEncryptionUpgradePrompt.Confirm = (_, _) =>
        {
            _confirmationsAsked++;
            return false;
        };

        SqlDatabaseEncryptionUpgradePrompt.Ask(null);

        Assert.Multiple(() =>
        {
            Assert.That(_confirmationsAsked, Is.EqualTo(1), "the question was put");
            Assert.That(_passwordsAsked, Is.Zero, "and answering no ends it there");
            Assert.That(_messagesShown, Is.EqualTo(new[] { Language.SqlUpgradeDeclined }));
            _retriever.DidNotReceive().WriteDatabaseMetaData(Arg.Any<RootNodeInfo>(),
                Arg.Any<IDatabaseConnector>(), Arg.Any<System.Data.Common.DbTransaction?>(),
                Arg.Any<Version?>());
        });
    }

    [Test]
    public void DecliningThePasswordTouchesNothing()
    {
        // The last moment at which backing out is still free. A password box dismissed with Escape
        // is a person who changed their mind, not one asking to upgrade under an empty password.
        _retriever.GetDatabaseMetaData(Arg.Any<IDatabaseConnector>())
            .Returns(ProtectedMetaData());

        SqlDatabaseEncryptionUpgradePrompt.Ask(null);

        Assert.Multiple(() =>
        {
            Assert.That(_passwordsAsked, Is.EqualTo(1), "a protected database is asked about");
            Assert.That(_messagesShown, Is.EqualTo(new[] { Language.SqlUpgradeDeclined }));
            _retriever.DidNotReceive().WriteDatabaseMetaData(Arg.Any<RootNodeInfo>(),
                Arg.Any<IDatabaseConnector>(), Arg.Any<System.Data.Common.DbTransaction?>(),
                Arg.Any<Version?>());
        });
    }

    [Test]
    public void TheStatusLineSaysWhichFormatTheDatabaseIsIn()
    {
        // What keeps the question in front of the one person who can answer it. A legacy database is
        // still written — with one warning per session — so without this line the state would be
        // invisible to whoever administers the database and never opens the notifications panel.
        _retriever.GetDatabaseMetaData(Arg.Any<IDatabaseConnector>())
            .Returns(MetaDataAt(SqlDatabaseVersionVerifier.SchemaVersion));

        (bool legacyOffered, string legacyStatus) = SqlDatabaseEncryptionUpgradePrompt.ReadStatus();

        _retriever.GetDatabaseMetaData(Arg.Any<IDatabaseConnector>())
            .Returns(MetaDataAt(CryptoProviderFactoryFromSqlVersion.AuthenticatedEncryptionVersion));

        (bool upgradedOffered, string upgradedStatus) = SqlDatabaseEncryptionUpgradePrompt.ReadStatus();

        Assert.Multiple(() =>
        {
            Assert.That(legacyOffered, "the button appears only where there is something to upgrade");
            Assert.That(legacyStatus, Is.EqualTo(Language.SqlUpgradeStatusDefaultKey),
                "this one has no master password, which is the worse of the two legacy states");
            Assert.That(upgradedOffered, Is.False);
            Assert.That(upgradedStatus, Is.EqualTo(Language.SqlUpgradeStatusCurrent));
        });
    }

    [Test]
    public void ADatabaseOnTheBuiltInKeyIsDescribedAsUnprotectedRatherThanAsOld()
    {
        // Two states hide behind "legacy", and the weaker of them is the one that reads as fine. A
        // database with no master password is encrypted under a constant published in this
        // application's source, so there is nothing to break — and an administrator who does not
        // already know what that key is will not act on a sentence about cipher strength.
        _retriever.GetDatabaseMetaData(Arg.Any<IDatabaseConnector>())
            .Returns(MetaDataAt(SqlDatabaseVersionVerifier.SchemaVersion));

        (_, string defaultKeyStatus) = SqlDatabaseEncryptionUpgradePrompt.ReadStatus();

        _retriever.GetDatabaseMetaData(Arg.Any<IDatabaseConnector>()).Returns(ProtectedMetaData());

        (bool stillOffered, string protectedStatus) = SqlDatabaseEncryptionUpgradePrompt.ReadStatus();

        Assert.Multiple(() =>
        {
            Assert.That(defaultKeyStatus, Does.Contain("not protected"),
                "said as what it means for the passwords");
            Assert.That(protectedStatus, Is.EqualTo(Language.SqlUpgradeStatusLegacy),
                "while a database that does have a master password is merely out of date");
            Assert.That(stillOffered, "and both are still offered the upgrade");
        });
    }

    [Test]
    public void AnUnreachableDatabaseSaysSoRatherThanGoingBlank()
    {
        // A status line is not worth a dialog, and a server that is merely down must not read as
        // "already fine" or offer a button that cannot work. It must not go **blank** either: an
        // empty line is indistinguishable from "not looked yet", so a failure here presented as the
        // feature simply not working, with the reason visible only to somebody who thought to open
        // the notifications panel.
        _retriever.GetDatabaseMetaData(Arg.Any<IDatabaseConnector>())
            .Returns(_ => throw new InvalidOperationException("the server is not there"));

        (bool offered, string status) = SqlDatabaseEncryptionUpgradePrompt.ReadStatus();

        Assert.Multiple(() =>
        {
            Assert.That(offered, Is.False, "no button for a database that cannot be reached");
            Assert.That(status, Is.EqualTo(Language.SqlUpgradeStatusUnknown));
            Assert.That(status, Is.Not.Empty, "and the user is not left looking at nothing");
        });
    }

    private static SqlConnectionListMetaData MetaDataAt(Version version) =>
        new()
        {
            Name = "test",
            Protected = new LegacyRijndaelCryptographyProvider().Encrypt(
                ConnectionFileDefaults.NotProtectedSentinel,
                new RootNodeInfo(RootNodeType.Connection).DefaultPassword.ConvertToSecureString()),
            ConfVersion = version
        };

    private static SqlConnectionListMetaData ProtectedMetaData() =>
        new()
        {
            Name = "test",
            Protected = new LegacyRijndaelCryptographyProvider().Encrypt(
                ConnectionFileDefaults.ProtectedSentinel, "the master password".ConvertToSecureString()),
            ConfVersion = SqlDatabaseVersionVerifier.SchemaVersion
        };
}
