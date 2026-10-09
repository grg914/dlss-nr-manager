using System.Diagnostics;
using Xunit;

namespace DlssNrManager.Tests;

/// <summary>
/// A real child-process integration test of the production tracker shutdown.
/// The probe is separate so calling Shutdown cannot affect other xUnit tests.
/// </summary>
public sealed class NormalManagerShutdownTests
{
    [Fact]
    public void Closing_manager_kills_owned_helper_and_rejects_late_launch()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var probe = Path.Combine(AppContext.BaseDirectory, "DlssNrManager.CrashProbe.dll");
        var runtimeConfig = Path.Combine(AppContext.BaseDirectory, "DlssNrManager.Tests.runtimeconfig.json");
        var deps = Path.Combine(AppContext.BaseDirectory, "DlssNrManager.Tests.deps.json");
        Assert.True(File.Exists(probe), "Independent process probe must be built.");
        Assert.True(File.Exists(runtimeConfig), "Test runtime configuration must exist.");
        Assert.True(File.Exists(deps), "Test dependencies must exist.");

        var marker = Path.Combine(Path.GetTempPath(),
            "dlssnr-normal-exit-" + Guid.NewGuid().ToString("N") + ".pid");
        Process? host = null;
        Process? helper = null;

        try
        {
            var start = new ProcessStartInfo
            {
                FileName = "dotnet",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = AppContext.BaseDirectory
            };
            start.ArgumentList.Add("exec");
            start.ArgumentList.Add("--depsfile");
            start.ArgumentList.Add(deps);
            start.ArgumentList.Add("--runtimeconfig");
            start.ArgumentList.Add(runtimeConfig);
            start.ArgumentList.Add(probe);
            start.ArgumentList.Add(marker);
            start.ArgumentList.Add("shutdown");

            host = Process.Start(start)
                ?? throw new InvalidOperationException("Could not start manager exit probe.");

            var deadline = DateTime.UtcNow.AddSeconds(25);
            var pid = 0;
            while (!host.HasExited && DateTime.UtcNow < deadline)
            {
                try
                {
                    if (File.Exists(marker) &&
                        int.TryParse(File.ReadAllText(marker).Trim(), out pid))
                        break;
                }
                catch (IOException) { }
                Thread.Sleep(100);
            }

            Assert.True(pid > 0, "Manager exit probe did not publish its helper PID.");
            helper = Process.GetProcessById(pid);
            Assert.False(helper.HasExited, "Owned helper must be running before closing manager.");

            File.WriteAllText(marker + ".shutdown", "close");
            Assert.True(host.WaitForExit(15000), "Manager probe did not shut down.");
            Assert.Equal(0, host.ExitCode);
            Assert.True(helper.WaitForExit(10000), "Owned helper survived a normal manager shutdown.");
            Assert.True(File.Exists(marker + ".blocked"),
                "Manager tried to launch a new helper after the shutdown gate was closed.");
        }
        finally
        {
            if (host is { HasExited: false })
            {
                try { host.Kill(entireProcessTree: false); } catch { }
            }
            if (helper is { HasExited: false })
            {
                try { helper.Kill(entireProcessTree: true); } catch { }
            }
            host?.Dispose();
            helper?.Dispose();
            foreach (var path in new[] { marker, marker + ".shutdown", marker + ".blocked", marker + ".writing" })
            {
                try { File.Delete(path); } catch (IOException) { }
            }
        }
    }
}
