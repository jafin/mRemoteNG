using System;
using System.Collections.Generic;

namespace mRemoteNG.FileTransfer
{
    /// <summary>
    /// Back/forward history for one pane.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Browser semantics: navigating somewhere new discards anything ahead of the current position.
    /// Deliberately UI-free and shared by both panes, because the behaviour is identical on each
    /// side and it is the part of navigation with real edge cases — the panes then keep separate
    /// instances, which is what makes them navigate independently.
    /// </para>
    /// <para>
    /// Navigating to where you already are is not recorded. Otherwise pressing refresh, or
    /// re-selecting the current directory, would fill the history with duplicates and make Back do
    /// nothing visible.
    /// </para>
    /// </remarks>
    public sealed class NavigationHistory
    {
        private readonly List<string> _entries = [];
        private readonly StringComparison _comparison;
        private int _position = -1;

        /// <param name="caseSensitive">
        /// Whether two paths differing only in case are the same place. True for a remote unix
        /// filesystem, false for local Windows.
        /// </param>
        public NavigationHistory(bool caseSensitive)
        {
            _comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        }

        /// <summary>The path currently shown, or <see langword="null"/> before the first navigation.</summary>
        public string? Current => _position >= 0 ? _entries[_position] : null;

        public bool CanGoBack => _position > 0;

        public bool CanGoForward => _position >= 0 && _position < _entries.Count - 1;

        /// <summary>Where <see cref="Back"/> would go, without going there.</summary>
        /// <remarks>
        /// Navigation can fail — the directory may have been removed since it was visited. Peeking
        /// lets the caller try the listing first and only commit the move once it succeeds, so a
        /// failed Back leaves the history where it was rather than needing to be undone.
        /// </remarks>
        public string? PeekBack => CanGoBack ? _entries[_position - 1] : null;

        /// <summary>Where <see cref="Forward"/> would go, without going there.</summary>
        public string? PeekForward => CanGoForward ? _entries[_position + 1] : null;

        /// <summary>The recorded paths, oldest first. For tests and diagnostics.</summary>
        public IReadOnlyList<string> Entries => _entries;

        /// <summary>
        /// Records a move to <paramref name="path"/>, discarding any forward history.
        /// </summary>
        /// <returns><see langword="false"/> if this is already the current path and nothing changed.</returns>
        public bool Navigate(string path)
        {
            ArgumentNullException.ThrowIfNull(path);

            if (Current is not null && string.Equals(Current, path, _comparison))
                return false;

            if (CanGoForward)
                _entries.RemoveRange(_position + 1, _entries.Count - _position - 1);

            _entries.Add(path);
            _position = _entries.Count - 1;
            return true;
        }

        /// <summary>Steps back and returns the path now current, or <see langword="null"/> if it cannot.</summary>
        public string? Back()
        {
            if (!CanGoBack)
                return null;

            _position--;
            return _entries[_position];
        }

        /// <summary>Steps forward and returns the path now current, or <see langword="null"/> if it cannot.</summary>
        public string? Forward()
        {
            if (!CanGoForward)
                return null;

            _position++;
            return _entries[_position];
        }

        public void Clear()
        {
            _entries.Clear();
            _position = -1;
        }
    }
}
