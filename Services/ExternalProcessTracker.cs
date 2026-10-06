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

    public static Process Start(ProcessStartInfo startInfo)
    {
        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                $"Could not start {Path.GetFileName(startInfo.FileName)}.");

        Active[process.Id] = process;
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => Untrack(process);

        AssignToJob(process);
        return process;
    }

    public static void Untrack(Process process)
    {
        try { Active.TryRemove(process.Id, out _); } catch { }
    }

    public static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch { }
        finally
        {
            Untrack(process);
        }
    }

    public static void KillAll()
    {
        foreach (var process in Active.Values.ToArray())
            Kill(process);

        KillStaleManagerHelpers();
    }

    public static void Shutdown()
    {
        KillAll();

        lock (JobLock)
        {
            if (_jobHandle == IntPtr.Zero)
                return;

            CloseHandle(_jobHandle);
            _jobHandle = IntPtr.Zero;
        }
    }

    private static void KillStaleManagerHelpers()
    {
        var root = Path.GetFullPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DlssNrManager"));

        foreach (var name in new[]
                 {
                     "realesrgan-ncnn-vulkan",
                     "video2dlssnr",
                     "ffmpeg",
                     "ffprobe"
                 })
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                try
                {
                    var path = process.MainModule?.FileName;
                    if (!string.IsNullOrWhiteSpace(path) &&
                        Path.GetFullPath(path).StartsWith(
                            root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        Kill(process);
                    }
                }
                catch { }
                finally
                {
                    try { process.Dispose(); } catch { }
                }
            }
        }
    }

    private static void AssignToJob(Process process)
    {
        lock (JobLock)
        {
            if (_jobHandle == IntPtr.Zero)
                return;

            try
            {
                if (!AssignProcessToJobObject(_jobHandle, process.Handle))
                {
                    AppLogger.Warn(
                        $"Unable to attach helper process {process.ProcessName} ({process.Id}) to the kill-on-close job. Win32={Marshal.GetLastWin32Error()}.");
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn(
                    $"Unable to attach helper process {process.Id} to the kill-on-close job: {ex.Message}");
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
