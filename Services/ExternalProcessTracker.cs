using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DlssNrManager.Services;

/// <summary>
/// Tracks external helper processes launched by DLSS NR Manager. Helpers are
/// assigned to a Windows Job Object with KILL_ON_JOB_CLOSE so they cannot outlive
/// the manager even after an abnormal process termination.
/// </summary>
public static class ExternalProcessTracker
{
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;
    private const int JobObjectExtendedLimitInformationClass = 9;

    private static readonly ConcurrentDictionary<int, Process> Active = new();
    private static readonly object JobLock = new();
    private static IntPtr _jobHandle = CreateKillOnCloseJob();
    private static bool _shuttingDown;

    /// <summary>
    /// Starts only manager-owned helpers. Shell-launched user applications
    /// (games, Explorer and elevated Windows repair) have different lifetimes.
    /// All starts and shutdowns are serialized to prevent late orphan children.
    /// </summary>
    public static Process Start(ProcessStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        if (startInfo.UseShellExecute)
            throw new InvalidOperationException(
                "Manager-owned helpers must use UseShellExecute=false.");

        lock (JobLock)
        {
            if (_shuttingDown)
                throw new InvalidOperationException(
                    "DLSS NR Manager is shutting down; no new helper can start.");

            // An unprotected child could survive an abrupt manager crash.
            // Failing closed is preferable to running untracked GPU helpers.
            if (OperatingSystem.IsWindows() && _jobHandle == IntPtr.Zero)
                throw new InvalidOperationException(
                    "Windows process job could not be initialized. Helper launch refused.");

            var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException(
                    $"Could not start {Path.GetFileName(startInfo.FileName)}.");

            try
            {
                // Attach before exposing the helper to callers. If the job
                // rejects the process, terminate it instead of merely logging.
                if (OperatingSystem.IsWindows() && !process.HasExited &&
                    !AssignProcessToJobObject(_jobHandle, process.Handle))
                {
                    var error = Marshal.GetLastWin32Error();
                    throw new InvalidOperationException(
                        $"Failed to attach manager helper PID {process.Id} to Windows job (Win32={error}).");
                }

                Active[process.Id] = process;
                process.EnableRaisingEvents = true;
                process.Exited += (_, _) => Untrack(process);
                if (process.HasExited)
                    Untrack(process);

                return process;
            }
            catch
            {
                // No background task is allowed to escape without lifecycle
                // ownership, even if event registration or job assignment fails.
                Kill(process);
                process.Dispose();
                throw;
            }
        }
    }

    public static void Untrack(Process process)
    {
        lock (JobLock)
        {
            try
            {
                // An exited PID must not untrack a different, later process
                // that happens to reuse the numeric PID.
                if (Active.TryGetValue(process.Id, out var current) &&
                    ReferenceEquals(current, process))
                    Active.TryRemove(process.Id, out _);
            }
            catch { }
        }
    }

    public static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Unable to terminate manager-owned helper: {ex.Message}");
        }
        finally
        {
            Untrack(process);
        }
    }

    public static void KillAll()
    {
        lock (JobLock)
        {
            foreach (var process in Active.Values.ToArray())
                Kill(process);
        }

        // Never scan helper names or installed paths: another program can
        // legitimately run its own FFmpeg, VLC, Python or Real-ESRGAN.
    }

    public static void Shutdown()
    {
        lock (JobLock)
        {
            _shuttingDown = true;
            KillAll();

            if (_jobHandle != IntPtr.Zero)
            {
                // The job closes even if a descendant is not in Active.
                // KILL_ON_JOB_CLOSE terminates the remaining owned tree.
                CloseHandle(_jobHandle);
                _jobHandle = IntPtr.Zero;
            }
        }
    }

    private static IntPtr CreateKillOnCloseJob()
    {
        if (!OperatingSystem.IsWindows())
            return IntPtr.Zero;

        var job = CreateJobObject(IntPtr.Zero, null);
        if (job == IntPtr.Zero)
            return IntPtr.Zero;

        var info = new JobObjectExtendedLimitInformation
        {
            BasicLimitInformation = new JobObjectBasicLimitInformation
            {
                LimitFlags = JobObjectLimitKillOnJobClose
            }
        };

        var length = Marshal.SizeOf<JobObjectExtendedLimitInformation>();
        var pointer = Marshal.AllocHGlobal(length);

        try
        {
            Marshal.StructureToPtr(info, pointer, false);
            if (SetInformationJobObject(
                    job,
                    JobObjectExtendedLimitInformationClass,
                    pointer,
                    (uint)length))
            {
                return job;
            }
        }
        catch
        {
            // Fall through to CloseHandle and the regular tracked-process fallback.
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }

        CloseHandle(job);
        return IntPtr.Zero;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(
        IntPtr lpJobAttributes,
        string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(
        IntPtr hJob,
        int jobObjectInfoClass,
        IntPtr lpJobObjectInfo,
        uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(
        IntPtr hJob,
        IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

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
    private struct JobObjectBasicLimitInformation
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
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }
}
