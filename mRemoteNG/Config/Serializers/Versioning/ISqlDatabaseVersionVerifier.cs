using System;

namespace mRemoteNG.Config.Serializers.Versioning;

public interface ISqlDatabaseVersionVerifier
{
    bool VerifyDatabaseVersion(Version dbVersion);

    /// <summary>
    /// Whether the database was written by a build newer than this one.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="VerifyDatabaseVersion"/> returning false, which also covers a
    /// database too old to upgrade. Those two need opposite treatment: an old database can be
    /// attempted, a newer one must not be. Reading a newer database with an older client's
    /// assumptions is how a version mismatch becomes what looks like data loss.
    /// </remarks>
    bool IsNewerThanSupported(Version dbVersion);
}