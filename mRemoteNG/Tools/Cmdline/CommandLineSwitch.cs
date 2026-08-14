namespace mRemoteNG.Tools.Cmdline;

/// <summary>
/// The single answer to "is this argument a switch, and where does its inline value begin?".
/// </summary>
/// <remarks>
/// <para>
/// Two components used to answer this independently. <see cref="App.CommandLineParser"/> asked the
/// two questions in order and got it right; <see cref="CmdArgumentsInterpreter"/> asked them with one
/// regex — <c>^-{1,2}|^/|=|:</c> — whose alternation is anchored for the prefixes and not for the
/// separators. So <c>C:\stores\confCons.xml</c> split at the drive letter and became a parameter
/// named <c>\stores\confCons.xml</c>, and the switch waiting for it was given the string "true".
/// No absolute path could be passed to any switch in the space-separated form.
/// </para>
/// <para>
/// Asking the prefix question first is the whole fix: a bare argument is then never a switch,
/// whatever it contains, and a drive letter's colon is just a character inside a value.
/// </para>
/// </remarks>
internal static class CommandLineSwitch
{
    /// <summary>
    /// Whether <paramref name="argument"/> is a switch, and if so its name and any inline value.
    /// </summary>
    /// <param name="name">The switch name, without prefix and without any inline value.</param>
    /// <param name="inlineValue">
    /// The text after the first <c>:</c> or <c>=</c>, or <see langword="null"/> when the argument
    /// carries no separator — which means the value, if there is one, is the next argument.
    /// </param>
    public static bool TryParse(string? argument, out string name, out string? inlineValue)
    {
        name = string.Empty;
        inlineValue = null;

        if (string.IsNullOrEmpty(argument))
            return false;

        int prefixLength = argument.StartsWith("--", System.StringComparison.Ordinal) ? 2
            : argument[0] is '-' or '/' ? 1
            : 0;

        if (prefixLength == 0)
            return false;

        string withoutPrefix = argument[prefixLength..];
        if (string.IsNullOrWhiteSpace(withoutPrefix))
            return false;

        // The first separator after the name, and only the first: `--cons:C:\stores` is the switch
        // `cons` with the value `C:\stores`, never the switch `cons` with the value `C`. The old
        // parser got this right by passing 3 to Regex.Split, an accident that read like an
        // optimisation and was load-bearing.
        int separatorIndex = withoutPrefix.IndexOfAny(['=', ':']);
        if (separatorIndex < 0)
        {
            name = withoutPrefix;
            return true;
        }

        // A separator with no name in front of it — `--=value` — names nothing. Treated as a value
        // rather than as a switch called "", which is what the old parser invented.
        if (separatorIndex == 0)
            return false;

        name = withoutPrefix[..separatorIndex];
        if (separatorIndex + 1 < withoutPrefix.Length)
            inlineValue = withoutPrefix[(separatorIndex + 1)..];

        return true;
    }
}
