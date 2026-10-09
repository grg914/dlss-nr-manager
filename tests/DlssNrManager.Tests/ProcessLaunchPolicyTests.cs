using System.Text.RegularExpressions;
using DlssNrManager.Services;
using Xunit;

namespace DlssNrManager.Tests;

/// <summary>
/// New manager-owned modules must not bypass the Windows Job Object via raw
/// Process.Start. Explicit UI hand-offs have a reviewed, narrow allowlist.
/// </summary>
public sealed class ProcessLaunchPolicyTests
{
    [Fact]
    public void New_service_process_launches_must_use_the_owned_process_tracker()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null &&
               !File.Exists(Path.Combine(directory.FullName, "DlssNrManager.csproj")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        var root = directory!.FullName;
        var services = Path.Combine(root, "Services");
        Assert.True(Directory.Exists(services));

        // Reviewed exceptions are intentionally independent user/system tasks:
        // updater must survive restart; Windows repair is elevated; Minecraft Launcher is user-controlled; ReShade
        // installer is user-controlled; PC updater opens external official URL.
        var directStartAllowlist = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["ExternalProcessTracker.cs"] = 1,
            ["AppUpdateService.cs"] = 1,
            ["MinecraftIntegrationService.cs"] = 2,
            ["WindowsRepairService.cs"] = 1,
            ["ReShadeService.cs"] = 1,
            ["PcUpdateService.cs"] = 1
        };

        foreach (var file in Directory.EnumerateFiles(services, "*.cs"))
        {
            var source = File.ReadAllText(file);
            var count = Regex.Matches(source, @"\bProcess\s*\.\s*Start\s*\(").Count;
            var name = Path.GetFileName(file);
            directStartAllowlist.TryGetValue(name, out var expected);
            Assert.True(count == expected,
                $"Unexpected raw Process.Start in {name}: observed {count}, expected {expected}. " +
                "Use ExternalProcessTracker.Start for app-owned subprocesses; " +
                "review any intentional detached launcher explicitly.");
        }

        // These are user-visible game/folder launches and two detached tasks
        // (app-data deletion, opened folders), never managed GPU helpers.
        var windowCode = File.ReadAllText(Path.Combine(root, "MainWindow.xaml.cs"));
        Assert.Equal(7, Regex.Matches(windowCode, @"\bProcess\s*\.\s*Start\s*\(").Count);
    }

    [Fact]
    public void Tracker_rejects_shell_launched_child_processes()
    {
        // UseShellExecute can detach a process from this manager; it must not
        // be used for owned GPU/media/helper processes.
        var info = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "explorer.exe",
            UseShellExecute = true
        };
        var error = Assert.Throws<InvalidOperationException>(
            () => ExternalProcessTracker.Start(info));
        Assert.Contains("UseShellExecute=false", error.Message);
    }
}
