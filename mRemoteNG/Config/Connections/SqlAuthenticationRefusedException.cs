using System;

namespace mRemoteNG.Config.Connections;

/// <summary>
/// The database was reached and answered; the master password was not supplied, or was not accepted.
/// </summary>
/// <remarks>
/// <para>
/// Distinct from every other load failure because it is the one that must <b>not</b> fall back to
/// the local copy. That copy exists for a database nobody can reach — a VPN that is down, a server
/// that is off — where showing what was last read costs nothing, since anyone opening the
/// application already had it. A refused password is the opposite situation: the database is fine
/// and the person at the keyboard could not prove they are entitled to read it. Substituting the
/// cached tree hands them every name, hostname, username and port in the store, which is most of
/// what the master password is there to withhold.
/// </para>
/// <para>
/// Raised for an unreadable sentinel too, and deliberately. A database at the authenticated version
/// with no sentinel cannot check any password, so nobody can prove entitlement to it either — and
/// "we could not verify you" is not a reason to show the contents anyway.
/// </para>
/// </remarks>
public class SqlAuthenticationRefusedException : Exception
{
    public SqlAuthenticationRefusedException()
        : this("The master password for this database was not supplied, or was not accepted.")
    {
    }

    public SqlAuthenticationRefusedException(string message)
        : base(message)
    {
    }

    public SqlAuthenticationRefusedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
