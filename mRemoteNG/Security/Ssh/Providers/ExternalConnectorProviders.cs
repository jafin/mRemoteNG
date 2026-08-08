using System;
using System.Runtime.Versioning;
using mRemoteNG.Connection;
using mRemoteNG.Resources.Language;

namespace mRemoteNG.Security.Ssh.Providers
{
    // Thin adapters over the static ExternalConnectors interfaces. Each one reproduces the
    // exception handling and message text that PuttyBase.Connect() uses today, including the
    // inconsistencies: Delinea, Passwordstate and Vault report through the protocol's
    // ErrorOccured event while 1Password, PasswordSafe and LAPS write to the message collector,
    // and Vault's failure is mislabelled "Secret Server Interface Error". Those are preserved
    // deliberately — normalising them here would be a behaviour change hidden inside a refactor.

    [SupportedOSPlatform("windows")]
    public sealed class DelineaSecretServerCredentialProvider : ISshCredentialProvider
    {
        public ExternalCredentialProvider Kind => ExternalCredentialProvider.DelineaSecretServer;

        public SshProviderResult Fetch(SshProviderRequest request)
        {
            try
            {
                ExternalConnectors.DSS.SecretServerInterface.FetchSecretFromServer(
                    $"{request.UserViaApi}", out string username, out string password, out _, out string privateKey);

                return new SshProviderResult(username ?? string.Empty, password ?? string.Empty,
                                             privateKey ?? string.Empty, []);
            }
            catch (Exception ex)
            {
                return SshProviderResult.Failed(new SshCredentialDiagnostic(
                    Kind, SshCredentialDiagnosticSeverity.ProtocolError,
                    "Secret Server Interface Error: " + ex.Message));
            }
        }
    }

    [SupportedOSPlatform("windows")]
    public sealed class PasswordstateCredentialProvider : ISshCredentialProvider
    {
        public ExternalCredentialProvider Kind => ExternalCredentialProvider.ClickstudiosPasswordState;

        public SshProviderResult Fetch(SshProviderRequest request)
        {
            try
            {
                ExternalConnectors.CPS.PasswordstateInterface.FetchSecretFromServer(
                    $"{request.UserViaApi}", out string username, out string password, out _, out string privateKey);

                return new SshProviderResult(username ?? string.Empty, password ?? string.Empty,
                                             privateKey ?? string.Empty, []);
            }
            catch (Exception ex)
            {
                return SshProviderResult.Failed(new SshCredentialDiagnostic(
                    Kind, SshCredentialDiagnosticSeverity.ProtocolError,
                    "Passwordstate Interface Error: " + ex.Message));
            }
        }
    }

    [SupportedOSPlatform("windows")]
    public sealed class OnePasswordCredentialProvider : ISshCredentialProvider
    {
        public ExternalCredentialProvider Kind => ExternalCredentialProvider.OnePassword;

        public SshProviderResult Fetch(SshProviderRequest request)
        {
            try
            {
                ExternalConnectors.OP.OnePasswordCli.ReadPassword(
                    $"{request.UserViaApi}", out string username, out string password, out _, out string privateKey);

                return new SshProviderResult(username ?? string.Empty, password ?? string.Empty,
                                             privateKey ?? string.Empty, []);
            }
            catch (ExternalConnectors.OP.OnePasswordCliException ex)
            {
                return SshProviderResult.Failed(
                    new SshCredentialDiagnostic(Kind, SshCredentialDiagnosticSeverity.Information,
                        Language.ECPOnePasswordCommandLine + ": " + ex.Arguments),
                    new SshCredentialDiagnostic(Kind, SshCredentialDiagnosticSeverity.Error,
                        Language.ECPOnePasswordReadFailed + Environment.NewLine + ex.Message));
            }
        }
    }

    [SupportedOSPlatform("windows")]
    public sealed class PasswordSafeCredentialProvider : ISshCredentialProvider
    {
        public ExternalCredentialProvider Kind => ExternalCredentialProvider.PasswordSafe;

        public SshProviderResult Fetch(SshProviderRequest request)
        {
            try
            {
                ExternalConnectors.PasswordSafe.PasswordSafeCli.ReadPassword(
                    $"{request.UserViaApi}", out string username, out string password, out _, out string privateKey);

                return new SshProviderResult(username ?? string.Empty, password ?? string.Empty,
                                             privateKey ?? string.Empty, []);
            }
            catch (ExternalConnectors.PasswordSafe.PasswordSafeCliException ex)
            {
                return SshProviderResult.Failed(
                    new SshCredentialDiagnostic(Kind, SshCredentialDiagnosticSeverity.Information,
                        Language.ECPPasswordSafeCommandLine + ": " + ex.Arguments),
                    new SshCredentialDiagnostic(Kind, SshCredentialDiagnosticSeverity.Error,
                        Language.ECPPasswordSafeReadFailed + Environment.NewLine + ex.Message));
            }
        }
    }

    [SupportedOSPlatform("windows")]
    public sealed class VaultOpenbaoCredentialProvider : ISshCredentialProvider
    {
        public ExternalCredentialProvider Kind => ExternalCredentialProvider.VaultOpenbao;

        public SshProviderResult Fetch(SshProviderRequest request)
        {
            try
            {
                string password;
                if (request.VaultSecretEngine == VaultOpenbaoSecretEngine.SSHOTP)
                {
                    ExternalConnectors.VO.VaultOpenbao.ReadOtpSSH(
                        $"{request.VaultMount}", $"{request.VaultRole}",
                        $"{request.Username}", $"{request.Hostname}", out password);
                }
                else
                {
                    // Preserves today's "root" default when the connection has no username.
                    ExternalConnectors.VO.VaultOpenbao.ReadPasswordSSH(
                        (int)request.VaultSecretEngine, request.VaultMount, request.VaultRole,
                        string.IsNullOrEmpty(request.Username) ? "root" : request.Username, out password);
                }

                return new SshProviderResult(string.Empty, password ?? string.Empty, string.Empty, []);
            }
            catch (ExternalConnectors.VO.VaultOpenbaoException ex)
            {
                // Message text intentionally matches today's output, mislabelling included.
                return SshProviderResult.Failed(new SshCredentialDiagnostic(
                    Kind, SshCredentialDiagnosticSeverity.ProtocolError,
                    "Secret Server Interface Error: " + ex.Message));
            }
        }
    }

    [SupportedOSPlatform("windows")]
    public sealed class LapsCredentialProvider : ISshCredentialProvider
    {
        public ExternalCredentialProvider Kind => ExternalCredentialProvider.LAPS;

        public SshProviderResult Fetch(SshProviderRequest request)
        {
            try
            {
                ExternalConnectors.LAPS.LAPSHelper.QueryLAPSPassword(
                    request.Hostname, out string username, out string password, out _);

                return new SshProviderResult(username ?? string.Empty, password ?? string.Empty,
                                             string.Empty, []);
            }
            catch (ExternalConnectors.LAPS.LAPSException ex)
            {
                return SshProviderResult.Failed(new SshCredentialDiagnostic(
                    Kind, SshCredentialDiagnosticSeverity.Error,
                    Language.ECPLAPSQueryFailed + Environment.NewLine + ex.Message));
            }
        }
    }
}
