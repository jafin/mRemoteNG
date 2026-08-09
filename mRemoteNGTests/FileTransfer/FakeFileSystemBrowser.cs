using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using mRemoteNG.FileTransfer;

namespace mRemoteNGTests.FileTransfer;

/// <summary>
/// A scripted filesystem, so the expander can be exercised without a server or a disk.
/// </summary>
/// <remarks>
/// The whole reason <see cref="IFileSystemBrowser"/> exists is that both sides of the file manager
/// fit behind it. That makes every rule the expander applies — depth, links, collisions, recovery
/// from an unreadable directory — assertable here rather than only against a real host.
/// </remarks>
internal sealed class FakeFileSystemBrowser : IFileSystemBrowser
{
    private readonly Dictionary<string, List<FileSystemEntry>> _contents;
    private readonly HashSet<string> _directories;

    public FakeFileSystemBrowser(char separator = '/', bool caseSensitive = true)
    {
        DirectorySeparator = separator;
        PathsAreCaseSensitive = caseSensitive;

        StringComparer comparer = caseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
        _contents = new Dictionary<string, List<FileSystemEntry>>(comparer);
        _directories = new HashSet<string>(comparer);
    }

    public string HomePath { get; set; } = "/";

    public bool SupportsPermissions => false;

    public char DirectorySeparator { get; }

    public bool PathsAreCaseSensitive { get; }

    /// <summary>Paths whose listing throws, standing in for a permission failure.</summary>
    public HashSet<string> UnreadablePaths { get; } = new(StringComparer.Ordinal);

    /// <summary>Paths whose creation throws.</summary>
    public HashSet<string> UncreatablePaths { get; } = new(StringComparer.Ordinal);

    /// <summary>Link paths that resolve to a directory.</summary>
    public HashSet<string> LinksToDirectories { get; } = new(StringComparer.Ordinal);

    /// <summary>Every path passed to <see cref="EnsureDirectoryAsync"/>, in order.</summary>
    public List<string> EnsuredDirectories { get; } = [];

    /// <summary>Every path passed to <see cref="ListAsync"/>, in order.</summary>
    public List<string> ListedPaths { get; } = [];

    public FakeFileSystemBrowser WithDirectory(string path)
    {
        _directories.Add(path);
        _contents.TryAdd(path, []);
        return this;
    }

    public FakeFileSystemBrowser WithFile(string directory, string name, long length = 10, DateTime? modified = null)
    {
        WithDirectory(directory);
        _contents[directory].Add(new FileSystemEntry(name, Combine(directory, name), IsDirectory: false,
            length, modified ?? new DateTime(2026, 1, 1),
            string.Empty, IsHidden: false));
        return this;
    }

    public FakeFileSystemBrowser WithSubdirectory(string parent, string name)
    {
        WithDirectory(parent);
        string full = Combine(parent, name);
        _contents[parent].Add(new FileSystemEntry(name, full, IsDirectory: true, 0,
            new DateTime(2026, 1, 1), string.Empty, IsHidden: false));
        return WithDirectory(full);
    }

    /// <param name="targetIsDirectory">
    /// What a following stat would report. A listing cannot tell the difference, which is exactly
    /// the case the expander has to handle.
    /// </param>
    public FakeFileSystemBrowser WithLink(string directory, string name, bool targetIsDirectory)
    {
        WithDirectory(directory);
        string full = Combine(directory, name);

        // Listed as a non-directory, as SFTP reports a link: the listing describes the link itself.
        _contents[directory].Add(new FileSystemEntry(name, full, IsDirectory: false, 10,
            new DateTime(2026, 1, 1), string.Empty,
            IsHidden: false, IsSymbolicLink: true));

        if (targetIsDirectory)
            LinksToDirectories.Add(full);

        return this;
    }

    public FileSystemEntry Entry(string directory, string name) =>
        _contents[directory].Single(e => string.Equals(e.Name, name, StringComparison.Ordinal));

    public FileSystemEntry DirectoryEntry(string parent, string name) => Entry(parent, name);

    public Task<IReadOnlyList<FileSystemEntry>> ListAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ListedPaths.Add(path);

        if (UnreadablePaths.Contains(path))
            throw new UnauthorizedAccessException($"Access to {path} is denied.");

        return Task.FromResult<IReadOnlyList<FileSystemEntry>>(
            _contents.TryGetValue(path, out List<FileSystemEntry>? entries) ? entries : []);
    }

    public string GetParentPath(string path)
    {
        int index = path.LastIndexOf(DirectorySeparator);
        return index <= 0 ? path : path[..index];
    }

    public string Combine(string directory, string name) =>
        directory.EndsWith(DirectorySeparator) ? directory + name : directory + DirectorySeparator + name;

    public Task RenameAsync(string fromPath, string toPath, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task DeleteAsync(FileSystemEntry entry, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default)
    {
        WithDirectory(path);
        return Task.CompletedTask;
    }

    public Task<bool> EnsureDirectoryAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsuredDirectories.Add(path);

        if (UncreatablePaths.Contains(path))
            throw new UnauthorizedAccessException($"Cannot create {path}.");

        if (_directories.Contains(path))
            return Task.FromResult(false);

        WithDirectory(path);
        return Task.FromResult(true);
    }

    public Task CreateFileAsync(string path, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<bool> LinkTargetIsDirectoryAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(LinksToDirectories.Contains(path));
    }

    public Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default) =>
        Task.FromResult<Stream>(new MemoryStream());

    public Task WriteAsync(Stream source,
        string path,
        long? totalBytes = null,
        IProgress<long>? bytesTransferred = null,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

/// <summary>
/// Answers the overwrite question with a scripted result, and counts how often it was asked.
/// </summary>
internal sealed class FakeTransferConflictResolver(TransferConflictResolution answer) : ITransferConflictResolver
{
    public int TimesAsked { get; private set; }

    public Task<TransferConflictResolution> ResolveAsync(FileSystemEntry source,
        FileSystemEntry destination,
        CancellationToken cancellationToken = default)
    {
        TimesAsked++;
        return Task.FromResult(answer);
    }
}