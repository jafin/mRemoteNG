using System;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using mRemoteNG.Connection;
using mRemoteNG.Connection.Protocol;
using mRemoteNG.UI.Tabs;
using NUnit.Framework;

namespace mRemoteNGTests.Connection.Protocol;

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class ProtocolBaseCloseTests
{
    [Test]
    public void ACloseThatRunsWhileTheTabDisposesLeavesTheSessionToTheTab()
    {
        StubProtocol protocol = new();
        using ConnectionTab tab = new();
        bool closedRaised = false;
        bool? sessionDisposedByClose = null;
        bool? sessionStillInTab = null;

        // Tearing down an RDP control pumps messages, which is how a close queued before the tab
        // began disposing ends up running inside that Dispose. The probe stands in for it: it is
        // the tab's first child, so it is disposed while the session is still in place.
        using DisposeProbe probe = new(() =>
        {
            InvokeCloseBG(protocol);
            sessionDisposedByClose = protocol.InterfaceControl.IsDisposed;
            sessionStillInTab = protocol.InterfaceControl.Parent == tab;
        });
        tab.Controls.Add(probe);

        InterfaceControl session = new(tab, protocol, new ConnectionInfo { Name = "Connection Name" });
        protocol.InterfaceControl = session;
        tab.Tag = session;
        protocol.Closed += _ => closedRaised = true;

        tab.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(closedRaised, Is.True, "The close should still be reported.");
            Assert.That(sessionDisposedByClose, Is.False,
                "Disposing the session here releases the RDP control the tab's Dispose is still using.");
            Assert.That(sessionStillInTab, Is.True,
                "The close must not pull the session out of a tab that is disposing it.");
            Assert.That(session.IsDisposed, Is.True, "The tab's own Dispose should dispose the session.");
        });
    }

    private static void InvokeCloseBG(ProtocolBase protocol)
    {
        MethodInfo closeBG = typeof(ProtocolBase).GetMethod("CloseBG", BindingFlags.Instance | BindingFlags.NonPublic)
                             ?? throw new AssertionException("Failed to resolve ProtocolBase.CloseBG.");

        closeBG.Invoke(protocol, null);
    }

    private sealed class StubProtocol : ProtocolBase
    {
    }

    /// <summary>
    /// Runs an action the first time it is disposed.
    /// </summary>
    private sealed class DisposeProbe(Action onDispose) : Control
    {
        private Action? _onDispose = onDispose;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Action? onDisposeOnce = _onDispose;
                _onDispose = null;
                onDisposeOnce?.Invoke();
            }

            base.Dispose(disposing);
        }
    }
}
