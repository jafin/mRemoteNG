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
                // The fixture's database has no master password, so this is the stronger of the
                // two sentences: not "old encryption" but "these passwords are not protected".
                Assert.That(status.Text, Is.EqualTo(Language.SqlUpgradeStatusDefaultKey));
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

    [Test]
    public void TheUpgradeSurvivesSwitchingToAdvancedView()
    {
        // **The bug this replaces.** Both controls were placed at absolute coordinates below the
        // test-connection row — correct at 100% scaling and wrong at any other, because the
        // designer's controls are scaled for the display while controls added after
        // InitializeComponent keep their raw coordinates. They drifted into the scaled tab control's
        // area and behind it in z-order, so the upgrade showed in Simple view, where the tab is
        // hidden, and disappeared in Advanced view.
        _retriever.GetDatabaseMetaData(Arg.Any<IDatabaseConnector>())
            .Returns(MetaDataAt(SqlDatabaseVersionVerifier.SchemaVersion));

        RunWithMessagePump(page =>
        {
            page.LoadSettings();

            Label status = Find<Label>(page, "lblEncryptionStatus");
            Control upgrade = Find<Control>(page, "btnUpgradeEncryption");
            Control row = Find<Control>(page, "pnlEncryptionStatus");
            Control tabs = Find<Control>(page, "tabCtrlSQL");

            PumpUntil(() => !string.IsNullOrEmpty(status.Text), "the status line should populate");

            Find<Button>(page, "btnExpandOptions").PerformClick();
            Application.DoEvents();

            Assert.Multiple(() =>
            {
                Assert.That(tabs.Visible, "precondition: the click switched to Advanced view");
                Assert.That(upgrade.Visible, "the upgrade must not vanish when the tabs appear");

                // The geometric invariant, asserted rather than assumed. Absolute coordinates made
                // this true at one scaling factor and false at others; a docked row cannot overlap
                // the tab control at any of them.
                //
                // Compared on screen, not as Bounds. The two live at different depths, so their
                // Bounds are relative to different parents — comparing those directly would be
                // comparing coordinate spaces, and would keep passing however the page was nested.
                Assert.That(OnScreen(tabs).IntersectsWith(OnScreen(row)), Is.False,
                    "the status row must not sit under the tab control");
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

    /// <summary>A control's rectangle in screen coordinates, so controls at different depths compare.</summary>
    private static System.Drawing.Rectangle OnScreen(Control control) =>
        control.RectangleToScreen(control.ClientRectangle);

    [Test]
    public void TheStatusLineWrapsRatherThanLosingItsEnd()
    {
        // **Found by hand, at 150% scaling: the sentence was cut off mid-word.** The label was
        // docked and a fixed height, so a status too long for the width simply lost its tail — and
        // the tail is where the consequence is. What was on screen read "...so they are", which is
        // worse than saying nothing, because it looks like a complete thought.
        //
        // Asserted as "the whole text is present and inside its row" rather than on a line count,
        // which would depend on the width this harness happens to give the page.
        _retriever.GetDatabaseMetaData(Arg.Any<IDatabaseConnector>())
            .Returns(MetaDataAt(SqlDatabaseVersionVerifier.SchemaVersion));

        RunWithMessagePump(page =>
        {
            page.LoadSettings();

            Label status = Find<Label>(page, "lblEncryptionStatus");
            Control row = Find<Control>(page, "pnlEncryptionStatus");

            PumpUntil(() => !string.IsNullOrEmpty(status.Text), "the status line should populate");

            Assert.Multiple(() =>
            {
                Assert.That(status.Text, Is.EqualTo(Language.SqlUpgradeStatusDefaultKey),
                    "the whole sentence, not as much of it as fits");
                Assert.That(status.MaximumSize.Width, Is.GreaterThan(0),
                    "the label was told what width to wrap at");
                Assert.That(status.Right, Is.LessThanOrEqualTo(row.ClientSize.Width),
                    "and it does not run off the end of its row");

                // The precondition, asserted rather than assumed: on one line this sentence is
                // wider than the space it has, so if the label is still a single line the end of it
                // is not on screen. The harness fixes the form at 900px, which is what makes that
                // deterministic — if that ever changes, this fails here rather than passing
                // vacuously below.
                Assert.That(TextRenderer.MeasureText(status.Text, status.Font).Width,
                    Is.GreaterThan(status.MaximumSize.Width),
                    "this asserts nothing unless the text is too long for the row");
                Assert.That(status.Height, Is.GreaterThan(status.Font.Height * 3 / 2),
                    "so it took a second line instead of losing its end");
            });
        });
    }

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
