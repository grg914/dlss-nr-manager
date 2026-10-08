using System.Diagnostics;
using DlssNrManager.Services;
using Xunit;

namespace DlssNrManager.Tests;

/// <summary>
/// An actual OS-level forced kill of a separate manager surrogate process.
/// Unlike a normal Shutdown() unit test, finally blocks cannot clean up the
/// child; the Windows KILL_ON_JOB_CLOSE job object must terminate the helper.
/// </summary>
public sealed class ForcedManagerTerminationTests
{
    [Fact]
    public void Abrupt_manager_termination_kills_owned_helper_on_windows()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var assembly = Path.Combine(AppContext.BaseDirectory, "DlssNrManager.CrashProbe.dll");
        var runtimeconfig = Path.Combine(AppContext.BaseDirectory, "DlssNrManager.Tests.runtimeconfig.json");
        Assert.True(File.Exists(assembly), "Crash probe assembly was not copied to test output.");
        Assert.True(File.Exists(runtimeconfig), "Test runtime configuration is missing.");

        var marker = Path.Combine(Path.GetTempPath(),
            "dlssnr-crash-probe-" + Guid.NewGuid().ToString("N") + ".pid");

        Process? host = null;
        Process? helper = null;

        try
        {
            var dotnet = new ProcessStartInfo
            {
                FileName = "dotnet",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                WorkingDirectory = AppContext.BaseDirectory
            };
            dotnet.ArgumentList.Add("exec");
            dotnet.ArgumentList.Add("--depsfile");
            dotnet.ArgumentList.Add(Path.Combine(
                AppContext.BaseDirectory, "DlssNrManager.Tests.deps.json"));
            dotnet.ArgumentList.Add("--runtimeconfig");
            dotnet.ArgumentList.Add(runtimeconfig);
            dotnet.ArgumentList.Add(assembly);
            dotnet.ArgumentList.Add(marker);

            host = Process.Start(dotnet) ??
                throw new InvalidOperationException("Unable to start the crash probe.");

            var ready = Stopwatch.StartNew();
            while (!File.Exists(marker) && !host.HasExited &&
                   ready.Elapsed < TimeSpan.FromSeconds(25))
                Thread.Sleep(100);

            Assert.True(File.Exists(marker),
                "Crash probe failed to start a tracked Windows helper.");
            var text = File.ReadAllText(marker).Trim();
            Assert.True(int.TryParse(text, out var helperPid));
            helper = Process.GetProcessById(helperPid);
            Assert.False(helper.HasExited, "Crash probe helper exited before forced termination.");

            // Do not kill the whole tree: doing so would mask a missing
            // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE assignment.
            host.Kill(entireProcessTree: false);
            Assert.True(host.WaitForExit(10000), "Crash probe did not terminate.");
            Assert.True(helper.WaitForExit(10000),
                "Tracked helper survived its manager's abrupt process death.");
        }
        finally
        {
            if (host is { HasExited: false })
                host.Kill(entireProcessTree: false);
            if (helper is { HasExited: false })
                helper.Kill(entireProcessTree: true);
            helper?.Dispose();
            host?.Dispose();
            try { File.Delete(marker); } catch (IOException) { }
        }
    }
}
