using System;
using System.Globalization;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using mRemoteNG.Config.DatabaseConnectors;
using NUnit.Framework;

namespace mRemoteNGTests.IntegrationTests.Sql;

/// <summary>
/// One SQL Server for the whole <c>...IntegrationTests.Sql</c> namespace.
/// </summary>
/// <remarks>
/// <para>
/// The SQL backend is the least-tested storage path in the application, and it is the one this
/// fork is changing the encryption of. Everything below the connector is unit-tested against
/// substitutes; everything that actually speaks to a database — the schema initialisation, the
/// version upgraders, the metadata row, the transaction the saver wraps its work in — had no
/// coverage at all, because a test may not require a server. Now one can bring its own.
/// </para>
/// <para>
/// <b>A database per test, not a server per test.</b> The container takes tens of seconds to become
/// ready, so it starts once; isolation comes from each test creating its own database on it, which
/// costs milliseconds. That also matches what the code under test does — schema initialisation and
/// version upgrades operate on a whole database, so sharing one between tests would let the first
/// test's upgrade decide the second test's starting state.
/// </para>
/// <para>
/// The namespace is deliberate. <c>test-config.json</c> routes
/// <c>mRemoteNGTests.IntegrationTests</c> minus <c>.Sftp</c> into the <c>Int.Other</c> group, so
/// these are picked up without touching a group definition — files an ordinary change may not edit.
/// </para>
/// </remarks>
[SetUpFixture]
[SupportedOSPlatform("windows")]
public class SqlServerFixture
{
    /// <summary>
    /// Pinned by digest, so the image moving under the suite is a deliberate update and not a
    /// Tuesday.
    /// </summary>
    /// <remarks>
    /// A tag would not do it: <c>2022-latest</c> is a name Microsoft repoints, so pinning to it buys
    /// the appearance of stability and none of the substance — and for a database engine a silent
    /// change of build is exactly the sort of thing that turns a green suite red overnight for
    /// reasons nobody can reproduce.
    /// <para>
    /// Updating it is deliberate: <c>docker pull mcr.microsoft.com/mssql/server:2022-latest</c> then
    /// <c>docker image inspect mcr.microsoft.com/mssql/server:2022-latest --format '{{index .RepoDigests 0}}'</c>,
    /// and read what changed before pasting it here.
    /// </para>
    /// </remarks>
    private const string Image =
        "mcr.microsoft.com/mssql/server@sha256:c1aa8afe9b06eab64c9774a4802dcd032205d1be785b1fd51e1c0151e7586b74";

    /// <summary>Set to <c>1</c> to skip this group on a machine with no Docker.</summary>
    public const string SkipVariable = "MRNG_SKIP_SQL_INTEGRATION";

    public const string Username = "sa";

    /// <summary>
    /// Meets SQL Server's complexity rules — upper, lower, digit — and deliberately contains no
    /// shell metacharacters.
    /// </summary>
    /// <remarks>
    /// An earlier value here was <c>mRemoteNG-Test(!)1</c>, which SQL Server accepts and which works
    /// when passed by hand, but which arrived at the engine as something else through the container
    /// runtime: every test failed with "Login failed for user 'sa'" from a server that had started
    /// perfectly. The password is not the subject of any test here, so it is not worth one
    /// character of cleverness.
    /// </remarks>
    public const string Password = "mRemoteNG_Test_2026";

    private const int SqlServerPort = 1433;

    private IContainer? _container;

    public static string Host { get; private set; } = string.Empty;

    public static int Port { get; private set; }

    /// <summary>Whether the group opted out, so tests can say so rather than fail obscurely.</summary>
    public static bool Skipped { get; private set; }

    /// <summary>
    /// A connector to a database on this server, in the form the application builds for a
    /// user-supplied SQL host.
    /// </summary>
    public static MSSqlDatabaseConnector ConnectorFor(string catalog)
    {
        // Guarded, because the failure this prevents is the worst kind. With no host and no port the
        // connection string names no server, SqlClient falls back to a local default instance, and
        // the test reaches *some other machine's* SQL Server — failing with "Login failed for user
        // 'sa'" from a server that has nothing to do with this suite, or worse, succeeding against
        // one that does. Either way the message sends the reader looking at passwords and
        // containers when the actual fault is that the fixture never ran.
        if (string.IsNullOrEmpty(Host) || Port == 0)
            throw new InvalidOperationException(
                $"The SQL test container is not running, so there is nothing to connect to. "
                + $"{nameof(SqlServerFixture)} is a [SetUpFixture] in this namespace and should have "
                + "started it; if this is reached, it did not.");

        return new($"{Host}:{Port.ToString(CultureInfo.InvariantCulture)}", catalog, Username, Password);
    }

    /// <summary>
    /// Creates an empty database and hands back a connector to it. The caller owns both.
    /// </summary>
    /// <remarks>
    /// Created through <c>master</c>, because a database cannot be created from inside itself. The
    /// name carries the test's own id so a failure leaves behind something identifiable rather than
    /// a numbered stranger.
    /// </remarks>
    public static MSSqlDatabaseConnector CreateDatabase(string name)
    {
        using MSSqlDatabaseConnector master = ConnectorFor("master");
        master.Connect();

        // The name is built from a test id rather than from anything a user supplies, and it is
        // still checked: CREATE DATABASE takes no parameters, so this is the one place in the
        // fixture where a name reaches SQL as text.
        if (!IsSafeDatabaseName(name))
            throw new ArgumentException($"'{name}' is not a usable database name.", nameof(name));

        using System.Data.Common.DbCommand command = master.DbCommand($"CREATE DATABASE [{name}]");
        command.ExecuteNonQuery();

        return ConnectorFor(name);
    }

    private static bool IsSafeDatabaseName(string name) =>
        !string.IsNullOrWhiteSpace(name) && name.Length <= 100 &&
        Array.TrueForAll(name.ToCharArray(), c => char.IsLetterOrDigit(c) || c is '_' or '-');

    [OneTimeSetUp]
    public async Task StartServerAsync()
    {
        if (string.Equals(Environment.GetEnvironmentVariable(SkipVariable), "1", StringComparison.Ordinal))
        {
            Skipped = true;

            // Said out loud. Choosing to skip and not noticing are different things, and only the
            // first should be easy.
            await TestContext.Out.WriteLineAsync(
                $"{SkipVariable}=1 — skipping the SQL integration group. These tests are the only "
                + "coverage the SQL backend has against a real database engine.");
            return;
        }

        try
        {
            // Building is inside the try, not just starting: an unreachable daemon surfaces as a
            // TypeInitializationException the moment ContainerBuilder is first touched, so a catch
            // around StartAsync alone fails the group with a message that never mentions Docker.
            _container = new ContainerBuilder(Image)
                .WithEnvironment("ACCEPT_EULA", "Y")
                .WithEnvironment("MSSQL_SA_PASSWORD", Password)
                // Developer edition explicitly. The image defaults to it, but a licensing default is
                // exactly the kind of thing that changes between builds, and Express has a database
                // size cap that would surface here as an unrelated-looking write failure.
                .WithEnvironment("MSSQL_PID", "Developer")
                .WithPortBinding(SqlServerPort, assignRandomHostPort: true)
                // **Waits for a login to succeed, not for the engine to say it is ready.** Both of
                // the obvious strategies are wrong here and fail the same way. The port is bound
                // before the engine will authenticate anything; and "SQL Server is now ready for
                // client connections" is logged *before* first-boot setup has applied the `sa`
                // password, so a log-message strategy returns while every login is still rejected.
                // The tests then fail with "Login failed for user 'sa'" against a server that
                // started perfectly, which sends the reader after the password and the image and
                // never near the timing.
                //
                // Asking the engine to answer a query is the only condition that means what it
                // needs to mean. -C trusts the container's self-signed certificate, which is the
                // same thing the connection string does.
                .WithWaitStrategy(Wait.ForUnixContainer()
                    .UntilCommandIsCompleted("/opt/mssql-tools18/bin/sqlcmd",
                        "-S", "localhost", "-U", Username, "-P", Password, "-C", "-Q", "SELECT 1"))
                .Build();

            await _container.StartAsync();
        }
        catch (Exception ex)
        {
            // Named, and a failure rather than a skip. A test that passes when its subject never ran
            // reports coverage that does not exist, which is worse than having no test at all.
            throw new InvalidOperationException(
                "Could not start the SQL Server test container. Docker must be running for the SQL "
                + $"integration group. Set {SkipVariable}=1 to skip it deliberately.", ex);
        }

        Host = _container.Hostname;
        Port = _container.GetMappedPublicPort(SqlServerPort);
    }

    [OneTimeTearDown]
    public async Task StopServerAsync()
    {
        if (_container is null)
            return;

        // Ryuk removes it anyway if the run is killed; this is the tidy path.
        await _container.DisposeAsync();
        _container = null;
    }
}
