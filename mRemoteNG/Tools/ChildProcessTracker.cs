using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace mRemoteNG.Tools;

/// <summary>
/// Ensures child processes are terminated when the parent process exits,
/// even on a crash, by using a Windows Job Object with the
/// JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE flag.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class ChildProcessTracker
{
    private static readonly IntPtr JobHandle;

    static ChildProcessTracker()
    {
        JobHandle = CreateJobObject(IntPtr.Zero, null);
        if (JobHandle == IntPtr.Zero)
            return;

        var info = new JobobjectExtendedLimitInformation
        {
            BasicLimitInformation = new JobobjectBasicLimitInformation
            {
                LimitFlags = JobObjectLimitKillOnJobClose
            }
        };

        int length = Marshal.SizeOf<JobobjectExtendedLimitInformation>();
        IntPtr infoPtr = Marshal.AllocHGlobal(length);
        try
        {
            Marshal.StructureToPtr(info, infoPtr, false);
            SetInformationJobObject(JobHandle, JobObjectInfoType.ExtendedLimitInformation, infoPtr, (uint)length);
        }
        finally
        {
            Marshal.FreeHGlobal(infoPtr);
        }
    }

    /// <summary>
    /// Adds a process to the job object so it will be killed when this process exits.
    /// </summary>
    public static void AddProcess(Process process)
    {
        if (JobHandle != IntPtr.Zero && process != null && !process.HasExited)
        {
            AssignProcessToJobObject(JobHandle, process.Handle);
        }
    }

    private const uint JobObjectLimitKillOnJobClose = 0x2000;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr hJob, JobObjectInfoType infoType, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    private enum JobObjectInfoType
    {
        ExtendedLimitInformation = 9
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobobjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public long Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobobjectExtendedLimitInformation
    {
        public JobobjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }
}