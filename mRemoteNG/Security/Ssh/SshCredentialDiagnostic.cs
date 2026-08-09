using mRemoteNG.Connection;

namespace mRemoteNG.Security.Ssh
{
    /// <summary>
    /// How a diagnostic raised during credential resolution must be surfaced by the caller.
    /// </summary>
    /// <remarks>
    /// The resolver cannot raise these itself. <c>Event_ErrorOccured</c> is a protected member of
    /// <c>ProtocolBase</c> that raises the protocol's <c>ErrorOccured</c> event, and callers may be
    /// subscribed to it; the message collector is a different channel again. Today's code uses both,
    /// inconsistently and per provider. Rather than silently normalise that — which would be a
    /// behaviour change smuggled into a refactor — the resolver records which channel each message
    /// belongs on and the caller replays it exactly as before.
    /// </remarks>
    public enum SshCredentialDiagnosticSeverity
    {
        /// <summary>Write to the message collector as <c>MessageClass.InformationMsg</c>.</summary>
        Information = 0,

        /// <summary>Write to the message collector as <c>MessageClass.ErrorMsg</c>.</summary>
        Error = 1,

        /// <summary>Raise through the protocol's <c>ErrorOccured</c> event.</summary>
        ProtocolError = 2,
    }

    /// <summary>
    /// A message produced while resolving credentials, carrying enough context for the caller to
    /// replay it on the correct channel.
    /// </summary>
    /// <param name="Provider">The credential source the message relates to.</param>
    /// <param name="Severity">Which channel the caller must use.</param>
    /// <param name="Message">The rendered message. Never contains a secret or key material.</param>
    public sealed record SshCredentialDiagnostic(
        ExternalCredentialProvider Provider,
        SshCredentialDiagnosticSeverity Severity,
        string Message);
}
