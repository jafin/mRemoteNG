using System;

namespace mRemoteNG.Connection.Sftp
{
    /// <summary>
    /// Remote path arithmetic.
    /// </summary>
    /// <remarks>
    /// <see cref="System.IO.Path"/> cannot be used for these. It applies the <i>local</i> platform's
    /// rules, so on Windows it would join with a backslash, treat <c>C:</c> as a root and normalise
    /// separators the wrong way. SFTP paths are POSIX regardless of what the client runs on.
    /// </remarks>
    public static class SftpPath
    {
        public const string Root = "/";

        /// <summary>Joins a directory and an entry name into an absolute remote path.</summary>
        public static string Combine(string directory, string name)
        {
            ArgumentNullException.ThrowIfNull(directory);
            ArgumentNullException.ThrowIfNull(name);

            if (name.StartsWith('/'))
                return Normalize(name);

            string prefix = Normalize(directory);
            return prefix.EndsWith('/') ? prefix + name : prefix + "/" + name;
        }

        /// <summary>
        /// The parent of <paramref name="path"/>, or <see cref="Root"/> when it has none. The root
        /// is its own parent, so repeatedly navigating up terminates rather than producing nonsense.
        /// </summary>
        public static string GetParent(string path)
        {
            ArgumentNullException.ThrowIfNull(path);

            string normalized = Normalize(path);
            if (normalized == Root)
                return Root;

            int lastSlash = normalized.LastIndexOf('/');
            return lastSlash switch
            {
                < 0 => Root,
                0 => Root,
                _ => normalized[..lastSlash]
            };
        }

        /// <summary>The last segment of a path, or an empty string for the root.</summary>
        public static string GetName(string path)
        {
            ArgumentNullException.ThrowIfNull(path);

            string normalized = Normalize(path);
            if (normalized == Root)
                return string.Empty;

            int lastSlash = normalized.LastIndexOf('/');
            return lastSlash < 0 ? normalized : normalized[(lastSlash + 1)..];
        }

        /// <summary>
        /// Collapses repeated separators, converts backslashes, and strips a trailing separator.
        /// </summary>
        /// <remarks>
        /// Backslashes are converted because the path box accepts typed input and a Windows user
        /// will type them out of habit. Path <i>segments</i> may legally contain a backslash on a
        /// unix server, so this trades a rare correct case for a common mistake — deliberately, and
        /// only for input that has already been through the user's hands.
        /// </remarks>
        public static string Normalize(string path)
        {
            ArgumentNullException.ThrowIfNull(path);

            string working = path.Replace('\\', '/').Trim();
            if (working.Length == 0)
                return Root;

            while (working.Contains("//", StringComparison.Ordinal))
                working = working.Replace("//", "/", StringComparison.Ordinal);

            if (working.Length > 1 && working.EndsWith('/'))
                working = working.TrimEnd('/');

            if (working.Length == 0)
                return Root;

            return working;
        }

        /// <summary>Whether <paramref name="path"/> is the filesystem root.</summary>
        public static bool IsRoot(string path) => Normalize(path) == Root;
    }
}
