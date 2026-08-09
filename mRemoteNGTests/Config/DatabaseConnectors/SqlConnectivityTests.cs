using System;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using mRemoteNG.Config.DatabaseConnectors;
using NUnit.Framework;

namespace mRemoteNGTests.Config.DatabaseConnectors;

/// <summary>
/// Integration tests that verify Microsoft.Data.SqlClient.SNI native DLL
/// loads correctly and SQL connectivity works end-to-end.
/// Requires a local SQL Server Express instance (SQLEXPRESS).
/// Automatically skipped on hosts without SQLEXPRESS (e.g. CI runners).
/// </summary>
[TestFixture]
[SupportedOSPlatform("windows")]
public class SqlConnectivityTests
{
    private static bool _sqlExpressAvailable;

    [OneTimeSetUp]
    public void ProbeSqlExpress()
    {
        try
        {
            using var conn = new SqlConnection(
                @"Server=localhost\SQLEXPRESS;Database=master;Integrated Security=true;" +
                "TrustServerCertificate=true;Connect Timeout=3");
            conn.Open();
            _sqlExpressAvailable = true;
        }
        catch (Exception)
        {
            _sqlExpressAvailable = false;
        }
    }

    [SetUp]
    public void RequireSqlExpress()
    {
        if (!_sqlExpressAvailable)
            Assert.Ignore("localhost\\SQLEXPRESS not available on this host — skipping SQL integration tests.");
    }

    [Test]
    public async Task TestConnectivity_LocalSqlExpress_WindowsAuth_Succeeds()
    {
        var result = await DatabaseConnectionTester.TestConnectivity(
            DatabaseConnectorFactory.MsSqlType,
            @"localhost\SQLEXPRESS",
            "master",
            "",
            "",
            DatabaseConnectorFactory.WindowsAuthentication);

        Assert.That(result, Is.EqualTo(ConnectionTestResult.ConnectionSucceded),
            "SNI native DLL must load correctly for SQL connection to succeed");
    }

    [Test]
    public async Task TestConnectivity_NonExistentDatabase_ReturnsUnknownDatabase()
    {
        var result = await DatabaseConnectionTester.TestConnectivity(
            DatabaseConnectorFactory.MsSqlType,
            @"localhost\SQLEXPRESS",
            "mRemoteNG_nonexistent_test_db",
            "",
            "",
            DatabaseConnectorFactory.WindowsAuthentication);

        Assert.That(result, Is.EqualTo(ConnectionTestResult.UnknownDatabase));
    }

    [Test]
    public async Task TestConnectivity_NonExistentServer_ReturnsServerNotAccessible()
    {
        var result = await DatabaseConnectionTester.TestConnectivity(
            DatabaseConnectorFactory.MsSqlType,
            @"localhost\NONEXISTENT_INSTANCE_12345",
            "master",
            "",
            "",
            DatabaseConnectorFactory.WindowsAuthentication);

        Assert.That(result, Is.EqualTo(ConnectionTestResult.ServerNotAccessible));
    }

    [Test]
    public async Task TestConnectivity_BadCredentials_ReturnsCredentialsRejectedOrError()
    {
        var result = await DatabaseConnectionTester.TestConnectivity(
            DatabaseConnectorFactory.MsSqlType,
            @"localhost\SQLEXPRESS",
            "master",
            "nonexistent_user",
            "bad_password",
            "SQL Server Authentication");

        // SQL Express with Windows-only auth may return CredentialsRejected or UnknownError
        Assert.That(result, Is.AnyOf(
            ConnectionTestResult.CredentialsRejected,
            ConnectionTestResult.UnknownError));
    }

    [Test]
    public async Task MSSqlDatabaseConnector_ConnectAndDisconnect_Works()
    {
        using var connector = new MSSqlDatabaseConnector(
            @"localhost\SQLEXPRESS",
            "master",
            "",
            "");

        await connector.ConnectAsync();
        Assert.That(connector.IsConnected, Is.True);

        connector.Disconnect();
        Assert.That(connector.IsConnected, Is.False);
    }
}