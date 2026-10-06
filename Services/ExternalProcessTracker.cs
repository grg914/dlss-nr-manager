using System.Collections.Concurrent;
using System.Diagnostics;

namespace DlssNrManager.Services;

/// <summary>
/// Tracks external helper processes launched by DLSS NR Manager so they cannot
/// outlive the operation or the application.
/// </summary>
public static class ExternalProcessTracker
{
    private static readonly ConcurrentDictionary<int, Process> Active = new();

    public static Process Start(ProcessStartInfo startInfo)
    {
        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                $"Could not start {Path.GetFileName(startInfo.FileName)}.");

        Active[process.Id] = process;
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => Untrack(process);

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

        // Last-resort cleanup for known media helpers that were launched by a
        // previous manager operation but escaped tracking due to an abrupt exit.
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
                    // Only kill helper binaries living under this app's LocalAppData tree.
                    var path = process.MainModule?.FileName;
                    var root = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "DlssNrManager");

                    if (!string.IsNullOrWhiteSpace(path) &&
                        Path.GetFullPath(path).StartsWith(
                            Path.GetFullPath(root),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        Kill(process);
                    }
                }
                catch
                {
                    try { process.Dispose(); } catch { }
                }
            }
        }
    }
}
