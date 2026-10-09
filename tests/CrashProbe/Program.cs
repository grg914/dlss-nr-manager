using System.Diagnostics;
using DlssNrManager.Services;

if (!OperatingSystem.IsWindows() || (args.Length != 1 && args.Length != 2))
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

    if (args.Length == 2 && args[1] == "shutdown")
    {
        // Wait for the parent to confirm that the owned child really runs
        // before triggering a normal app-exit cleanup.
        var signal = args[0] + ".shutdown";
        var deadline = DateTime.UtcNow.AddSeconds(25);
        while (!File.Exists(signal) && DateTime.UtcNow < deadline)
            await Task.Delay(100);
        if (!File.Exists(signal))
            return 4;

        ExternalProcessTracker.Shutdown();

        // Prove late tasks cannot spawn another helper after window close.
        try
        {
            using var unexpected = ExternalProcessTracker.Start(new ProcessStartInfo
            {
                FileName = ping,
                Arguments = "127.0.0.1 -n 180",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            return 5;
        }
        catch (InvalidOperationException)
        {
            File.WriteAllText(args[0] + ".blocked", "late-start-refused");
        }
        return 0;
    }

    await Task.Delay(TimeSpan.FromMinutes(2));
    return 0;
}
finally
{
    ExternalProcessTracker.Shutdown();
}
