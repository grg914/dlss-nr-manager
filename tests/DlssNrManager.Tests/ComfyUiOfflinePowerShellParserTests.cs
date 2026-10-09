using System.Diagnostics;
using Xunit;

namespace DlssNrManager.Tests;

public sealed class ComfyUiOfflinePowerShellParserTests
{
    [Fact]
    public void ComfyUiArchiveInspector_is_valid_Windows_PowerShell_syntax()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        string? script = null;
        while (dir is not null)
        {
            var candidate = Path.Combine(
                dir.FullName, "tools", "inspect-comfyui-portable-offline.ps1");
            if (File.Exists(candidate))
            {
                script = candidate;
                break;
            }

            dir = dir.Parent;
        }

        Assert.NotNull(script);
        var escaped = script!.Replace("'", "''", StringComparison.Ordinal);
        var command =
            "$t=$null; $e=$null; " +
            "[System.Management.Automation.Language.Parser]::ParseFile('" +
            escaped + "',[ref]$t,[ref]$e)|Out-Null; " +
            "if($e.Count -gt 0) { $e|Out-String|Write-Error; exit 1 }";
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-Command", command })
            process.StartInfo.ArgumentList.Add(arg);
        Assert.True(process.Start(), "Windows PowerShell parser did not start.");
        try
        {
            if (!process.WaitForExit(20_000))
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException("Windows PowerShell syntax check timed out.");
            }

            var output = process.StandardOutput.ReadToEnd();
            var errors = process.StandardError.ReadToEnd();
            Assert.True(process.ExitCode == 0,
                $"PowerShell archive inspector parse failed:{Environment.NewLine}{output}{errors}");
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
    }
}
