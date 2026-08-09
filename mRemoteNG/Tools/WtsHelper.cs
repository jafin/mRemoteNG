using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace mRemoteNG.Tools;

/// <summary>
/// Wraps the Windows Terminal Services (WTS) API for querying and managing
/// RDP sessions on local or remote machines.
/// </summary>
[SupportedOSPlatform("windows")]
public static class WtsHelper
{
    private static readonly IntPtr WtsCurrentServer = IntPtr.Zero;

    #region P/Invoke declarations

    [DllImport("wtsapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr WTSOpenServer(string pServerName);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSCloseServer(IntPtr hServer);

    [DllImport("wtsapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool WTSEnumerateSessions(
        IntPtr hServer,
        int reserved,
        int version,
        ref IntPtr ppSessionInfo,
        ref int pCount);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr pMemory);

    [DllImport("wtsapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool WTSQuerySessionInformation(
        IntPtr hServer,
        int sessionId,
        WtsInfoClass wtsInfoClass,
        out IntPtr ppBuffer,
        out uint pBytesReturned);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSDisconnectSession(IntPtr hServer, int sessionId, bool bWait);

    [StructLayout(LayoutKind.Sequential)]
    private struct WtsSessionInfo
    {
        public int SessionId;
        // Keep as IntPtr to avoid CLR freeing strings individually;
        // the entire block is freed with a single WTSFreeMemory call.
        public IntPtr pWinStationName;
        public WtsConnectStateClass State;
    }

    private enum WtsConnectStateClass
    {
        WtsActive,
        WtsConnected,
        WtsConnectQuery,
        WtsShadow,
        WtsDisconnected,
        WtsIdle,
        WtsListen,
        WtsReset,
        WtsDown,
        WtsInit
    }

    private enum WtsInfoClass
    {
        WtsInitialProgram = 0,
        WtsApplicationName = 1,
        WtsWorkingDirectory = 2,
        WtsoemId = 3,
        WtsSessionId = 4,
        WtsUserName = 5,
        WtsWinStationName = 6,
        WtsDomainName = 7,
        WtsConnectState = 8,
        WtsClientBuildNumber = 9,
        WtsClientName = 10,
        WtsClientDirectory = 11,
        WtsClientProductId = 12,
        WtsClientHardwareId = 13,
        WtsClientAddress = 14,
        WtsClientDisplay = 15,
        WtsClientProtocolType = 16,
    }

    #endregion

    /// <summary>
    /// Enumerates all RDP sessions on the specified server.
    /// </summary>
    /// <param name="serverName">Hostname or IP. Use <c>null</c> or <c>"."</c> for the local machine.</param>
    /// <returns>List of session entries.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the WTS API call fails.</exception>
    public static IList<RdpSessionEntry> EnumerateSessions(string serverName)
    {
        bool isLocal = IsLocalMachine(serverName);
        IntPtr hServer = IntPtr.Zero;

        try
        {
            hServer = isLocal ? WtsCurrentServer : WTSOpenServer(serverName);
            if (!isLocal && hServer == IntPtr.Zero)
                throw new InvalidOperationException(
                    $"Cannot connect to '{serverName}'. Error code: {Marshal.GetLastWin32Error()}");

            IntPtr sessionInfoPtr = IntPtr.Zero;
            int sessionCount = 0;

            if (!WTSEnumerateSessions(hServer, 0, 1, ref sessionInfoPtr, ref sessionCount))
                throw new InvalidOperationException(
                    $"WTSEnumerateSessions failed. Error code: {Marshal.GetLastWin32Error()}");

            try
            {
                return ParseSessionInfo(hServer, sessionInfoPtr, sessionCount);
            }
            finally
            {
                WTSFreeMemory(sessionInfoPtr);
            }
        }
        finally
        {
            if (!isLocal && hServer != IntPtr.Zero)
                WTSCloseServer(hServer);
        }
    }

    /// <summary>
    /// Disconnects the specified session on the server.
    /// </summary>
    /// <param name="serverName">Hostname or IP. Use <c>null</c> or <c>"."</c> for the local machine.</param>
    /// <param name="sessionId">The session ID to disconnect.</param>
    /// <exception cref="InvalidOperationException">Thrown when the WTS API call fails.</exception>
    public static void DisconnectSession(string serverName, int sessionId)
    {
        bool isLocal = IsLocalMachine(serverName);
        IntPtr hServer = IntPtr.Zero;

        try
        {
            hServer = isLocal ? WtsCurrentServer : WTSOpenServer(serverName);
            if (!isLocal && hServer == IntPtr.Zero)
                throw new InvalidOperationException(
                    $"Cannot connect to '{serverName}'. Error code: {Marshal.GetLastWin32Error()}");

            if (!WTSDisconnectSession(hServer, sessionId, false))
                throw new InvalidOperationException(
                    $"WTSDisconnectSession failed. Error code: {Marshal.GetLastWin32Error()}");
        }
        finally
        {
            if (!isLocal && hServer != IntPtr.Zero)
                WTSCloseServer(hServer);
        }
    }

    private static bool IsLocalMachine(string? serverName)
    {
        return string.IsNullOrEmpty(serverName)
               || string.Equals(serverName, ".", StringComparison.Ordinal)
               || serverName.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase);
    }

    private static List<RdpSessionEntry> ParseSessionInfo(
        IntPtr hServer, IntPtr sessionInfoPtr, int sessionCount)
    {
        var sessions = new List<RdpSessionEntry>(sessionCount);
        int structSize = Marshal.SizeOf<WtsSessionInfo>();

        for (int i = 0; i < sessionCount; i++)
        {
            IntPtr current = IntPtr.Add(sessionInfoPtr, i * structSize);
            var info = Marshal.PtrToStructure<WtsSessionInfo>(current);

            string stationName = Marshal.PtrToStringUni(info.pWinStationName) ?? string.Empty;
            string userName = QuerySessionString(hServer, info.SessionId, WtsInfoClass.WtsUserName);
            string clientName = QuerySessionString(hServer, info.SessionId, WtsInfoClass.WtsClientName);
            string domainName = QuerySessionString(hServer, info.SessionId, WtsInfoClass.WtsDomainName);

            sessions.Add(new RdpSessionEntry
            {
                SessionId = info.SessionId,
                SessionName = stationName,
                UserName = string.IsNullOrEmpty(domainName) ? userName : $"{domainName}\\{userName}",
                ClientName = clientName,
                State = info.State.ToString(),
                StateText = GetStateText(info.State)
            });
        }

        return sessions;
    }

    private static string QuerySessionString(IntPtr hServer, int sessionId, WtsInfoClass infoClass)
    {
        if (!WTSQuerySessionInformation(hServer, sessionId, infoClass, out IntPtr buffer, out _))
            return string.Empty;

        try
        {
            return Marshal.PtrToStringUni(buffer) ?? string.Empty;
        }
        finally
        {
            WTSFreeMemory(buffer);
        }
    }

    private static string GetStateText(WtsConnectStateClass state) => state switch
    {
        WtsConnectStateClass.WtsActive => "Active",
        WtsConnectStateClass.WtsConnected => "Connected",
        WtsConnectStateClass.WtsConnectQuery => "Connect Query",
        WtsConnectStateClass.WtsShadow => "Shadow",
        WtsConnectStateClass.WtsDisconnected => "Disconnected",
        WtsConnectStateClass.WtsIdle => "Idle",
        WtsConnectStateClass.WtsListen => "Listen",
        WtsConnectStateClass.WtsReset => "Reset",
        WtsConnectStateClass.WtsDown => "Down",
        WtsConnectStateClass.WtsInit => "Init",
        _ => state.ToString()
    };
}

/// <summary>
/// Represents a single RDP session entry returned by the WTS API.
/// </summary>
public class RdpSessionEntry
{
    public int SessionId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string SessionName { get; set; } = string.Empty;
    public string ClientName { get; set; } = string.Empty;
    /// <summary>Raw state enum name (e.g. "WTSActive").</summary>
    public string State { get; set; } = string.Empty;
    /// <summary>Human-readable state text (e.g. "Active").</summary>
    public string StateText { get; set; } = string.Empty;
}