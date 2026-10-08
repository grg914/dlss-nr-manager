using System.Diagnostics;
using DlssNrManager.Services;
using Xunit;

namespace DlssNrManager.Tests;

/// <summary>
/// Verifies the managed STOP path on Windows. A separate forced-manager-crash
/// integration test is still needed to prove KILL_ON_JOB_CLOSE behavior.
/// </summary>
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
}
