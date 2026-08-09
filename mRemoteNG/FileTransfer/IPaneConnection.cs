using System;
using System.Threading;
using System.Threading.Tasks;

namespace mRemoteNG.FileTransfer
{
    /// <summary>
    /// The connection behind a pane, for the pane to check and re-establish.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately says nothing about SSH or sessions. A pane needs two facts — whether it is usable
    /// and how to ask for it back — and giving it any more would put connection handling in a control
    /// that is otherwise a view over a controller.
    /// </para>
    /// <para>
    /// A pane with no connection to speak of is given <see langword="null"/> rather than a stub. That
    /// is what makes reconnection remote-only without a single test for which side a pane is: the local
    /// filesystem has nothing to reconnect to, and the absent dependency says so.
    /// </para>
    /// </remarks>
    public interface IPaneConnection
    {
        /// <summary>Whether the connection is currently usable.</summary>
        bool IsConnected { get; }

        /// <summary>
        /// Attempts to re-establish the connection.
        /// </summary>
        /// <returns>
        /// Whether the connection is usable afterwards. Returns rather than throws because the caller
        /// has to decide whether to go on and list; reporting why it failed belongs to the
        /// implementation, which knows what was being connected to.
        /// </returns>
        Task<bool> ReconnectAsync(CancellationToken cancellationToken = default);

        /// <summary>Raised when <see cref="IsConnected"/> may have changed.</summary>
        event EventHandler? ConnectionChanged;
    }
}
