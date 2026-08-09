using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace mRemoteNG.FileTransfer;

/// <summary>
/// The behaviour of one file pane, with no UI attached.
/// </summary>
/// <remarks>
/// <para>
/// Both panes use this, and it is where the rules that are easy to get wrong live: a failed
/// listing must leave the previous one on screen, the hidden toggle must not need a round trip,
/// and an overtaken navigation must not repaint the pane with a directory the user has already
/// navigated away from.
/// </para>
/// <para>
/// Separated from the control so those rules are testable. A pane driven through
/// <c>ObjectListView</c> could only be tested with a message pump, which is a poor way to assert
/// "the listing did not change".
/// </para>
/// </remarks>
public sealed class FilePaneController : IDisposable
{
    private readonly IFileSystemBrowser _browser;
    private readonly NavigationHistory _history;
    private readonly Lock _gate = new();

    private IReadOnlyList<FileSystemEntry> _allEntries = [];
    private CancellationTokenSource? _inFlight;
    private bool _showHidden;
    private bool _disposed;

    /// <param name="browser">The filesystem this pane shows.</param>
    /// <param name="caseSensitivePaths">
    /// Whether two paths differing only in case are different places. True for a remote unix
    /// filesystem, false for local Windows.
    /// </param>
    public FilePaneController(IFileSystemBrowser browser, bool caseSensitivePaths)
    {
        ArgumentNullException.ThrowIfNull(browser);

        _browser = browser;
        _history = new NavigationHistory(caseSensitivePaths);
    }

    /// <summary>The directory currently shown, or empty before the first successful listing.</summary>
    public string CurrentPath { get; private set; } = string.Empty;

    /// <summary>The entries to display: sorted, and filtered by <see cref="ShowHidden"/>.</summary>
    public IReadOnlyList<FileSystemEntry> Entries =>
        _allEntries.Where(e => _showHidden || !e.IsHidden).ToArray();

    /// <summary>
    /// The synthetic <c>..</c> row, or <see langword="null"/> at the root and before the first
    /// listing.
    /// </summary>
    /// <remarks>
    /// Kept out of <see cref="Entries"/> on purpose. That property feeds the item count, the size
    /// total and everything downstream of them; a synthetic row inside it would have to be
    /// subtracted back out at every one of those sites, and the first one anybody forgot would
    /// report a directory as holding one more file than it does.
    /// </remarks>
    public FileSystemEntry? ParentEntry { get; private set; }

    public bool CanGoBack => _history.CanGoBack;

    public bool CanGoForward => _history.CanGoForward;

    public bool SupportsPermissions => _browser.SupportsPermissions;

    /// <summary>The filesystem behind this pane, for the transfer queue to read and write.</summary>
    public IFileSystemBrowser Browser => _browser;

    public string HomePath => _browser.HomePath;

    /// <summary>Whether an operation is in flight.</summary>
    public bool IsBusy { get; private set; }

    /// <summary>
    /// Whether hidden entries are listed. Toggling filters what is already loaded rather than
    /// re-listing: the server was already asked for everything.
    /// </summary>
    public bool ShowHidden
    {
        get => _showHidden;
        set
        {
            if (_showHidden == value)
                return;

            _showHidden = value;
            EntriesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Raised when <see cref="Entries"/> or <see cref="CurrentPath"/> has changed.</summary>
    public event EventHandler? EntriesChanged;

    /// <summary>
    /// Raised when an operation failed. The pane keeps showing what it had.
    /// </summary>
    public event EventHandler<string>? OperationFailed;

    public event EventHandler? BusyChanged;

    public Task NavigateAsync(string path) => NavigateCoreAsync(path, commitHistory: true);

    public Task NavigateHomeAsync() => NavigateAsync(_browser.HomePath);

    public Task RefreshAsync() =>
        string.IsNullOrEmpty(CurrentPath) ? Task.CompletedTask : NavigateCoreAsync(CurrentPath, commitHistory: false);

    public Task NavigateUpAsync()
    {
        if (string.IsNullOrEmpty(CurrentPath))
            return Task.CompletedTask;

        string parent = _browser.GetParentPath(CurrentPath);
        return string.Equals(parent, CurrentPath, StringComparison.Ordinal)
            ? Task.CompletedTask
            : NavigateAsync(parent);
    }

    /// <summary>Opens a directory entry. Files are the caller's business.</summary>
    public Task OpenAsync(FileSystemEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return entry.IsDirectory ? NavigateAsync(entry.FullPath) : Task.CompletedTask;
    }

    public async Task GoBackAsync()
    {
        if (_history.PeekBack is not { } target)
            return;

        // List first, commit the history move only on success: the directory may have been
        // removed since it was visited, and a failed Back must not silently consume a history
        // step.
        if (await NavigateCoreAsync(target, commitHistory: false).ConfigureAwait(false))
            _history.Back();
    }

    public async Task GoForwardAsync()
    {
        if (_history.PeekForward is not { } target)
            return;

        if (await NavigateCoreAsync(target, commitHistory: false).ConfigureAwait(false))
            _history.Forward();
    }

    public string Combine(string directory, string name) => _browser.Combine(directory, name);

    public async Task<bool> CreateDirectoryAsync(string name) =>
        await MutateAsync(ct => _browser.CreateDirectoryAsync(Combine(CurrentPath, name), ct)).ConfigureAwait(false);

    public async Task<bool> CreateFileAsync(string name) =>
        await MutateAsync(ct => _browser.CreateFileAsync(Combine(CurrentPath, name), ct)).ConfigureAwait(false);

    public async Task<bool> RenameAsync(FileSystemEntry entry, string newName)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return await MutateAsync(ct => _browser.RenameAsync(entry.FullPath, Combine(CurrentPath, newName), ct))
            .ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(FileSystemEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return await MutateAsync(ct => _browser.DeleteAsync(entry, ct)).ConfigureAwait(false);
    }

    private async Task<bool> NavigateCoreAsync(string path, bool commitHistory)
    {
        ArgumentNullException.ThrowIfNull(path);
        ObjectDisposedException.ThrowIf(_disposed, this);

        CancellationTokenSource cancellation = StartOperation();

        try
        {
            IReadOnlyList<FileSystemEntry> entries =
                await _browser.ListAsync(path, cancellation.Token).ConfigureAwait(false);

            // An overtaken navigation must not repaint the pane: the user has already asked for
            // somewhere else, and the late result would look like the pane jumping back.
            if (cancellation.IsCancellationRequested)
                return false;

            _allEntries = Sort(entries);
            CurrentPath = path;
            ParentEntry = BuildParentEntry(path);

            if (commitHistory)
                _history.Navigate(path);

            EntriesChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            // Keeps CurrentPath and the loaded entries: a pane that empties itself on a failed
            // listing tells the user the directory is empty, which is a different and wrong
            // statement from "it could not be read".
            OperationFailed?.Invoke(this, ex.Message);
            return false;
        }
        finally
        {
            EndOperation(cancellation);
        }
    }

    private async Task<bool> MutateAsync(Func<CancellationToken, Task> operation)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        CancellationTokenSource cancellation = StartOperation();

        try
        {
            await operation(cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            OperationFailed?.Invoke(this, ex.Message);
            return false;
        }
        finally
        {
            EndOperation(cancellation);
        }

        await RefreshAsync().ConfigureAwait(false);
        return true;
    }

    private CancellationTokenSource StartOperation()
    {
        CancellationTokenSource cancellation = new();

        CancellationTokenSource? previous;
        lock (_gate)
        {
            previous = _inFlight;
            _inFlight = cancellation;
        }

        previous?.Cancel();
        previous?.Dispose();

        SetBusy(true);
        return cancellation;
    }

    private void EndOperation(CancellationTokenSource cancellation)
    {
        bool wasCurrent;
        lock (_gate)
        {
            wasCurrent = ReferenceEquals(_inFlight, cancellation);
            if (wasCurrent)
                _inFlight = null;
        }

        if (wasCurrent)
        {
            cancellation.Dispose();
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        if (IsBusy == busy)
            return;

        IsBusy = busy;
        BusyChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The <c>..</c> row for a directory, or <see langword="null"/> when it has no parent.
    /// </summary>
    /// <remarks>
    /// "No parent" is whatever the browser says: both implementations already return the path
    /// itself for a root, so neither POSIX's <c>/</c> nor a Windows drive root needs special
    /// handling here.
    /// </remarks>
    private FileSystemEntry? BuildParentEntry(string path)
    {
        string parent = _browser.GetParentPath(path);

        return string.Equals(parent, path, StringComparison.Ordinal)
            ? null
            : new FileSystemEntry("..", parent, IsDirectory: true, Length: 0,
                LastWriteTime: default, Permissions: string.Empty,
                IsHidden: false, IsSymbolicLink: false, IsParentNavigation: true);
    }

    /// <summary>
    /// Directories first, then by name. Matches what every file manager does, and doing it here
    /// rather than in the list control keeps the two panes consistent and the ordering testable.
    /// </summary>
    private static IReadOnlyList<FileSystemEntry> Sort(IReadOnlyList<FileSystemEntry> entries) =>
    [.. entries.OrderByDescending(e => e.IsDirectory)
        .ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)];

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        CancellationTokenSource? inFlight;
        lock (_gate)
        {
            inFlight = _inFlight;
            _inFlight = null;
        }

        inFlight?.Cancel();
        inFlight?.Dispose();
    }
}