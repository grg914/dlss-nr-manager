using System.Diagnostics;
using DlssNrManager.Services;

if (!OperatingSystem.IsWindows() || args.Length != 1)
    return 2;

var ping = Path.Combine(Environment.SystemDirectory, "ping.exe");
if (!File.Exists(ping))
    return 3;

using var helper = ExternalProcessTracker.Start(new ProcessStartInfo
{
    FileName = ping,
    Arguments = "127.0.0.1 -n 180",
    UseShellExecute = false,
    CreateNoWindow = true,
    RedirectStandardOutput = true,
    RedirectStandardError = true
});

try
{
    // Signals the parent xUnit test only AFTER production tracking started.
    // Publish the ready marker atomically after closing the PID writer.
    var stagingMarker = args[0] + ".writing";
    File.WriteAllText(stagingMarker, helper.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
    File.Move(stagingMarker, args[0]);
    await Task.Delay(TimeSpan.FromMinutes(2));
    return 0;
}
finally
{
    ExternalProcessTracker.Shutdown();
}
