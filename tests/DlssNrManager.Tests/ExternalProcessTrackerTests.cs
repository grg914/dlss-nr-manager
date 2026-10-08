using System.Diagnostics;
using DlssNrManager.Services;
using Xunit;

namespace DlssNrManager.Tests;

[CollectionDefinition("ExternalProcessTracker", DisableParallelization = true)]
public sealed class ExternalProcessTrackerCollection { }

/// <summary>
/// Verifies the managed STOP path on Windows. A separate forced-manager-crash
/// integration test is still needed to prove KILL_ON_JOB_CLOSE behavior.
/// </summary>
[Collection("ExternalProcessTracker")]
public sealed class ExternalProcessTrackerTests
{
    [Fact]
    public void Stop_terminates_owned_windows_helper()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var ping = Path.Combine(Environment.SystemDirectory, "ping.exe");
        Assert.True(File.Exists(ping));

        using var process = ExternalProcessTracker.Start(new ProcessStartInfo
        {
            FileName = ping,
            Arguments = "127.0.0.1 -n 30",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        });

        try
        {
            Assert.False(process.HasExited);
            ExternalProcessTracker.Kill(process);
            Assert.True(process.WaitForExit(5000), "Tracked helper remained alive after STOP.");
            Assert.True(process.HasExited);
        }
        finally
        {
            // Never leave a helper running even when an assertion fails.
            ExternalProcessTracker.Kill(process);
        }
    }

    [Fact]
    public void Stop_does_not_kill_untracked_helper_from_shared_manager_install_path()
    {
        if (!OperatingSystem.IsWindows())
            return;

        // Simulate a helper owned by another manager instance. The old global
        // executable-name/path sweep incorrectly killed this unrelated process.
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DlssNrManager", "tracker-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var helperPath = Path.Combine(directory, "ffmpeg.exe");
        Process? untracked = null;
        try
        {
            File.Copy(Path.Combine(Environment.SystemDirectory, "ping.exe"), helperPath);
            untracked = Process.Start(new ProcessStartInfo
            {
                FileName = helperPath,
                Arguments = "127.0.0.1 -n 30",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            Assert.NotNull(untracked);
            Assert.False(untracked.HasExited);

            ExternalProcessTracker.KillAll();
            Assert.False(untracked.HasExited,
                "STOP must not terminate a helper that this manager instance did not start.");
        }
        finally
        {
            if (untracked is not null)
            {
                try
                {
                    if (!untracked.HasExited)
                        untracked.Kill(entireProcessTree: true);
                    untracked.WaitForExit(5000);
                }
                catch (InvalidOperationException) { }
                finally { untracked.Dispose(); }
            }

            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
