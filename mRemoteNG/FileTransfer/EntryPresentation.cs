using System;
using System.Globalization;
using System.Text;

namespace mRemoteNG.FileTransfer;

/// <summary>
/// How a listed entry is rendered: its kind, its size and its date.
/// </summary>
/// <remarks>
/// Outside the control so it can be tested directly. Every rule here is one somebody will get
/// wrong eventually — a directory reporting <c>0 B</c> as though it had been measured, a date
/// format that quietly reorders a British user's day and month — and none of them are worth
/// discovering through a message pump.
/// </remarks>
/// <summary>What a listed entry is.</summary>
public enum EntryKind
{
    Folder = 0,
    File = 1,

    /// <summary>A symbolic link or a reparse point, whatever it points at.</summary>
    Link = 2
}

public static class EntryPresentation
{
    /// <summary>
    /// What an entry is, or <see langword="null"/> for the parent row, which is not one.
    /// </summary>
    /// <remarks>
    /// A link is reported as a link whether it lands on a file or a directory. Telling those apart
    /// needs a following stat per entry, which is a round trip each to answer a question the
    /// column does not ask.
    /// </remarks>
    public static EntryKind? KindOf(FileSystemEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.IsParentNavigation)
            return null;

        return entry.IsSymbolicLink ? EntryKind.Link
            : entry.IsDirectory ? EntryKind.Folder
            : EntryKind.File;
    }

    /// <summary>The image key for an entry, matching the pane's image list.</summary>
    public static string? ImageKeyOf(FileSystemEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.IsParentNavigation)
            return ParentImageKey;

        return entry.IsDirectory ? FolderImageKey : FileImageKey;
    }

    public const string FolderImageKey = "folder";
    public const string FileImageKey = "file";
    public const string ParentImageKey = "parent";

    /// <summary>Renders a byte count, or an empty string when there is no size to show.</summary>
    /// <remarks>
    /// <see langword="null"/> and a zero-byte file are different answers. A directory has no size
    /// in any sense the column means, and SFTP reports zero for one, so rendering that zero would
    /// state something false: that the directory was measured and found empty.
    /// </remarks>
    public static string DescribeSize(long? bytes)
    {
        if (bytes is not { } value)
            return string.Empty;

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double scaled = value;
        int unit = 0;

        while (scaled >= 1024 && unit < units.Length - 1)
        {
            scaled /= 1024;
            unit++;
        }

        return unit == 0
            ? string.Format(CultureInfo.CurrentCulture, "{0} {1}", value, units[unit])
            : string.Format(CultureInfo.CurrentCulture, "{0:0.#} {1}", scaled, units[unit]);
    }

    /// <summary>The size to show for an entry, or <see langword="null"/> where none applies.</summary>
    public static long? SizeOf(FileSystemEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return entry.IsDirectory || entry.IsParentNavigation ? null : entry.Length;
    }

    /// <summary>
    /// A date and time pattern in the culture's own field order, with every field padded.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Widens a lone <c>d</c>, <c>M</c>, <c>h</c> or <c>H</c> to its doubled form. An unpadded
    /// column is ragged — <c>1/2/2026</c> and <c>11/12/2026</c> start their year in different
    /// places — which is what makes a column of dates hard to read.
    /// </para>
    /// <para>
    /// The culture's pattern is upgraded rather than replaced. A fixed <c>yyyy-MM-dd</c> aligns and
    /// sorts beautifully and shows a German user a date format they do not use; alignment is not
    /// worth overriding somebody's locale for.
    /// </para>
    /// </remarks>
    public static string AlignedDateTimePattern(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        string date = PadFields(culture.DateTimeFormat.ShortDatePattern);
        string time = PadFields(culture.DateTimeFormat.ShortTimePattern);

        return string.IsNullOrEmpty(time) ? date : date + " " + time;
    }

    /// <summary>
    /// The modified time to show for an entry, or <see langword="null"/> where none applies.
    /// </summary>
    /// <remarks>
    /// The parent row is a destination, not a thing with attributes of its own. Its date would be
    /// the default, which renders as year one.
    /// </remarks>
    public static DateTime? ModifiedOf(FileSystemEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return entry.IsParentNavigation ? null : entry.LastWriteTime;
    }

    /// <summary>Renders a modified time, or an empty string when there is none.</summary>
    public static string DescribeModified(DateTime? modified, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        return modified is { } value
            ? value.ToString(AlignedDateTimePattern(culture), culture)
            : string.Empty;
    }

    /// <summary>
    /// Doubles single occurrences of the padding-sensitive format characters.
    /// </summary>
    /// <remarks>
    /// Quoted literals are copied through untouched. A culture whose pattern contains a quoted
    /// <c>'d'</c> — as a day-name abbreviation or a separator word — would otherwise have that
    /// literal rewritten into a date field, which changes what the date says rather than how it is
    /// spaced.
    /// </remarks>
    private static string PadFields(string pattern)
    {
        if (string.IsNullOrEmpty(pattern))
            return string.Empty;

        StringBuilder result = new(pattern.Length + 4);
        char quote = '\0';

        for (int i = 0; i < pattern.Length; i++)
        {
            char current = pattern[i];

            if (quote != '\0')
            {
                result.Append(current);
                if (current == quote)
                    quote = '\0';
                continue;
            }

            if (current is '\'' or '"')
            {
                quote = current;
                result.Append(current);
                continue;
            }

            // Escapes the next character, whatever it is, so it must be carried across verbatim.
            if (current == '\\' && i + 1 < pattern.Length)
            {
                result.Append(current).Append(pattern[i + 1]);
                i++;
                continue;
            }

            int run = RunLength(pattern, i, current);

            if (run == 1 && current is 'd' or 'M' or 'h' or 'H')
                result.Append(current);

            result.Append(current, run);
            i += run - 1;
        }

        return result.ToString();
    }

    private static int RunLength(string pattern, int start, char character)
    {
        int end = start;
        while (end < pattern.Length && pattern[end] == character)
            end++;

        return end - start;
    }
}