using System;
using System.Runtime.Versioning;
using System.Threading;
using System.Windows.Forms;
using mRemoteNG.App;
using mRemoteNG.Config.DatabaseConnectors;
using mRemoteNG.Config.Serializers.ConnectionSerializers.Sql;
using mRemoteNG.Config.Serializers.Versioning;
using mRemoteNG.Resources.Language;
using mRemoteNG.Security;
using mRemoteNG.Security.Factories;
using mRemoteNG.Security.SymmetricEncryption;
using mRemoteNG.Tree.Root;
using mRemoteNG.UI;
using mRemoteNG.UI.Forms.OptionsPages;
using NSubstitute;
using NUnit.Framework;

namespace mRemoteNGTests.UI.Forms;

/// <summary>
/// That opening the SQL options page is enough to be told how the database stores its passwords.
/// </summary>
/// <remarks>
/// <para>
/// <b>Written because it shipped broken.</b> The status was refreshed only from the successful tail
/// of the Apply path, so a page that was merely opened said nothing — and nobody presses Apply on a
/// page they have not changed. Every unit test around the status passed, because they all called
/// <c>ReadStatus</c> directly and none of them asked whether anything ever called it.
/// </para>
/// <para>
/// So this drives the real page: construct it, call <c>LoadSettings</c> as the options window does,
/// and read the label and the button. The database is a substitute — the point here is the wiring,
/// and a test that needed a SQL Server to prove a label gets populated would not be run often enough
/// to protect it.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
[TestFixture]
public class SqlServerPageEncryptionStatusTests
{
    private Func<IDatabaseConnector> _originalConnector = null!;
    private ISqlDatabaseMetaDataRetriever _originalRetriever = null!;
    private ISqlDatabaseMetaDataRetriever _retriever = null!;

    private bool _originalUseSql;
    private string _originalPass = "";

    [SetUp]
    public void Setup()
    {
        _originalConnector = SqlDatabaseEncryptionUpgradePrompt.Connector;
        _originalRetriever = SqlDatabaseEncryptionUpgradePrompt.MetaDataRetriever;

        _retriever = Substitute.For<ISqlDatabaseMetaDataRetriever>();
        SqlDatabaseEncryptionUpgradePrompt.Connector = () => Substitute.For<IDatabaseConnector>();
        SqlDatabaseEncryptionUpgradePrompt.MetaDataRetriever = _retriever;

        mRemoteNG.Properties.OptionsDBsPage settings = mRemoteNG.Properties.OptionsDBsPage.Default;
        _originalUseSql = settings.UseSQLServer;
        _originalPass = settings.SQLPass;

        // The page unprotects this on load, and an unmarked value is not passed through — the
        // no-marker fallback legacy-*decrypts*, so a plain string fails as "not a valid Base-64
        // string" before the page has finished loading.
        settings.SQLPass = SettingsSecretProtector.Default.Protect("irrelevant", Runtime.EncryptionKey);
        settings.UseSQLServer = true;
    }

    [TearDown]
    public void Teardown()
    {
        SqlDatabaseEncryptionUpgradePrompt.Connector = _originalConnector;
        SqlDatabaseEncryptionUpgradePrompt.MetaDataRetriever = _originalRetriever;

        mRemoteNG.Properties.OptionsDBsPage.Default.UseSQLServer = _originalUseSql;
        mRemoteNG.Properties.OptionsDBsPage.Default.SQLPass = _originalPass;
    }

    [Test]
    public void OpeningThePageIsEnoughToBeToldTheDatabaseIsWeak()
    {
        _retriever.GetDatabaseMetaData(Arg.Any<IDatabaseConnector>())
            .Returns(MetaDataAt(SqlDatabaseVersionVerifier.SchemaVersion));

        RunWithMessagePump(page =>
        {
            page.LoadSettings();

            Label status = Find<Label>(page, "lblEncryptionStatus");
            Control upgrade = Find<Control>(page, "btnUpgradeEncryption");

            PumpUntil(() => !string.IsNullOrEmpty(status.Text),
                "the status line should populate from LoadSettings alone, with no Apply");

            Assert.Multiple(() =>
            {
                Assert.That(status.Text, Is.EqualTo(Language.SqlUpgradeStatusLegacy));
                Assert.That(upgrade.Visible, "and the upgrade is offered");
            });
        });
    }

    [Test]
    public void AnUpgradedDatabaseSaysSoAndOffersNothing()
    {
        _retriever.GetDatabaseMetaData(Arg.Any<IDatabaseConnector>())
            .Returns(MetaDataAt(CryptoProviderFactoryFromSqlVersion.AuthenticatedEncryptionVersion));

        RunWithMessagePump(page =>
        {
            page.LoadSettings();

            Label status = Find<Label>(page, "lblEncryptionStatus");
            Control upgrade = Find<Control>(page, "btnUpgradeEncryption");

            PumpUntil(() => !string.IsNullOrEmpty(status.Text), "the status line should populate");

            Assert.Multiple(() =>
            {
                Assert.That(status.Text, Is.EqualTo(Language.SqlUpgradeStatusCurrent));
                Assert.That(upgrade.Visible, Is.False,
                    "an irreversible, team-wide change must not be offered where there is nothing to change");
            });
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

    private static T Find<T>(Control parent, string name) where T : Control
    {
        Control[] found = parent.Controls.Find(name, searchAllChildren: true);

        Assert.That(found, Is.Not.Empty,
            $"'{name}' is not on the page at all — if it was renamed, this test is looking for the wrong thing");

        return (T)found[0];
    }

    /// <summary>
    /// Pumps until the condition holds. The refresh is deliberately off the UI thread, so its
    /// continuation only lands when messages are being processed.
    /// </summary>
    private static void PumpUntil(Func<bool> condition, string because)
    {
        for (int i = 0; i < 500 && !condition(); i++)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }

        Assert.That(condition(), because);
    }

    private static void RunWithMessagePump(Action<SqlServerPage> testAction)
    {
        Exception? caught = null;

        Thread thread = new(() =>
        {
            Form form = new()
            {
                Width = 900,
                Height = 800,
                ShowInTaskbar = false,
                StartPosition = FormStartPosition.Manual,
                Location = new System.Drawing.Point(-10000, -10000)
            };

            form.Load += (_, _) =>
            {
                try
                {
                    SqlServerPage page = new() { Dock = DockStyle.Fill };
                    form.Controls.Add(page);
                    Application.DoEvents();
                    testAction(page);
                }
                catch (Exception ex)
                {
                    caught = ex;
                }
                finally
                {
                    form.Close();
                }
            };

            Application.Run(form);
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        if (!thread.Join(TimeSpan.FromSeconds(30)))
        {
            thread.Interrupt();
            Assert.Fail("Test timed out after 30 seconds (message pump deadlock)");
        }

        if (caught != null)
            throw caught;
    }
}
