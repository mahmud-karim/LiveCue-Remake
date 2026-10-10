using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace LiveCue.Relay.Processes;

/// <summary>
/// A Windows job object with the "kill on job close" limit (architecture 3.7). Every process assigned
/// to it, and every process those start, ends when the last handle of the job closes: when this object
/// is disposed, or when the application ends for any reason, a crash included. That is what keeps a
/// Codex process from outliving the relay.
/// </summary>
internal sealed class JobObject : IDisposable
{
    private const int ExtendedLimitInformationClass = 9;
    private const uint KillOnJobClose = 0x2000;

    private readonly SafeFileHandle _handle;

    private JobObject(SafeFileHandle handle)
    {
        _handle = handle;
    }

    /// <summary>A new, unnamed job with the limit set, or null when Windows refuses either step.</summary>
    public static JobObject? TryCreate()
    {
        var handle = CreateJobObjectW(IntPtr.Zero, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            return null;
        }

        var information = default(ExtendedLimitInformation);
        information.BasicLimitInformation.LimitFlags = KillOnJobClose;
        if (!SetInformationJobObject(
                handle,
                ExtendedLimitInformationClass,
                ref information,
                (uint)Marshal.SizeOf<ExtendedLimitInformation>()))
        {
            handle.Dispose();
            return null;
        }

        return new JobObject(handle);
    }

    /// <summary>
    /// Places a running process in the job. False when Windows refuses, which is also what happens
    /// when the process has already ended.
    /// </summary>
    public bool TryAssign(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        try
        {
            return !_handle.IsClosed && AssignProcessToJobObject(_handle, process.SafeHandle);
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or ObjectDisposedException)
        {
            // No process is associated any more.
            return false;
        }
    }

    /// <summary>Closes the job's handle: everything still in the job ends.</summary>
    public void Dispose() => _handle.Dispose();

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern SafeFileHandle CreateJobObjectW(IntPtr jobAttributes, IntPtr name);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(
        SafeFileHandle job,
        int informationClass,
        ref ExtendedLimitInformation information,
        uint length);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(SafeFileHandle job, SafeProcessHandle process);

    // JOBOBJECT_BASIC_LIMIT_INFORMATION, IO_COUNTERS and JOBOBJECT_EXTENDED_LIMIT_INFORMATION of the
    // Windows SDK, field for field.
    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
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
    private struct ExtendedLimitInformation
    {
        public BasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }
}
