using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace DlssNrManager.Services;

public sealed record WindowsRepairResult(
    bool Started,
    bool Cancelled,
    int ExitCode)
{
    public bool Success => Started && !Cancelled && ExitCode == 0;
}

public sealed class WindowsRepairService
{
    private const int ErrorCancelled = 1223;

    internal static string CreateRepairScript()
        => """
           $ErrorActionPreference = 'Continue'
           $Host.UI.RawUI.WindowTitle = 'DLSS NR Manager - Windows repair'

           Write-Host ''
           Write-Host 'DLSS NR Manager - Windows system repair' -ForegroundColor Cyan
           Write-Host 'Step 1/2: DISM /Online /Cleanup-Image /RestoreHealth' -ForegroundColor Cyan
           Write-Host ''

           & "$env:WINDIR\System32\Dism.exe" /Online /Cleanup-Image /RestoreHealth
           $dismExit = $LASTEXITCODE
           $dismOk = ($dismExit -eq 0 -or $dismExit -eq 3010)

           Write-Host ''
           Write-Host "DISM exit code: $dismExit"
           Write-Host ''
           Write-Host 'Step 2/2: SFC /scannow' -ForegroundColor Cyan
           Write-Host ''

           & "$env:WINDIR\System32\sfc.exe" /scannow
           $sfcExit = $LASTEXITCODE

           Write-Host ''
           Write-Host "SFC exit code: $sfcExit"

           if ($dismOk -and $sfcExit -eq 0) {
               if ($dismExit -eq 3010) {
                   Write-Host 'Repair completed successfully. Windows requested a restart.' -ForegroundColor Yellow
               }
               else {
                   Write-Host 'Repair completed successfully.' -ForegroundColor Green
               }
               exit 0
           }

           Write-Host 'One or more Windows repair tools reported an error. Review the output above.' -ForegroundColor Red
           exit 1
           """;

    internal static ProcessStartInfo CreateStartInfo()
    {
        var script = CreateRepairScript();
        var encodedCommand = Convert.ToBase64String(
            Encoding.Unicode.GetBytes(script));

        var powerShell = Path.Combine(
            Environment.SystemDirectory,
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");

        if (!File.Exists(powerShell))
            powerShell = "powershell.exe";

        return new ProcessStartInfo
        {
            FileName = powerShell,
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encodedCommand}",
            WorkingDirectory = Environment.SystemDirectory,
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Normal
        };
    }

    public async Task<WindowsRepairResult> RunDismAndSfcAsync()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(
                "DISM and SFC repair is available only on Windows.");

        try
        {
            using var process = Process.Start(CreateStartInfo())
                ?? throw new InvalidOperationException(
                    "Unable to start the elevated Windows repair process.");

            await process.WaitForExitAsync();

            return new WindowsRepairResult(
                Started: true,
                Cancelled: false,
                ExitCode: process.ExitCode);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return new WindowsRepairResult(
                Started: false,
                Cancelled: true,
                ExitCode: ErrorCancelled);
        }
    }
}
