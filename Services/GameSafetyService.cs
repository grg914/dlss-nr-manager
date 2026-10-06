namespace DlssNrManager.Services;

public sealed record GameSafetyAssessment(
    bool AntiCheatDetected,
    IReadOnlyList<string> Signals,
    string Message);

public static class GameSafetyService
{
    private static readonly string[] AntiCheatNames =
    [
        "EasyAntiCheat", "EasyAntiCheat_EOS", "EasyAntiCheat.exe", "EasyAntiCheat_EOS.exe",
        "BattlEye", "BEService.exe", "BEService_x64.exe", "BEClient_x64.dll",
        "EAAntiCheat", "EAAntiCheat.GameServiceLauncher.exe",
        "FACEIT", "faceitclient.exe",
        "RiotClientServices.exe", "vgc.exe", "vgk.sys",
        "Ricochet", "randgrid.sys",
        "equ8", "equ8_client", "XIGNCODE", "xhunter1.sys", "nProtect", "GameGuard"
    ];

    public static GameSafetyAssessment Assess(string gameDirectory)
    {
        if (string.IsNullOrWhiteSpace(gameDirectory) || !Directory.Exists(gameDirectory))
            return new(false, [], "No anti-cheat scan was possible for the selected folder.");

        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<(string Path, int Depth)>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        queue.Enqueue((gameDirectory, 0));

        while (queue.Count > 0 && visited.Count < 700)
        {
            var (directory, depth) = queue.Dequeue();
            if (!visited.Add(directory))
                continue;

            try
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    var name = Path.GetFileName(entry);
                    foreach (var signal in AntiCheatNames)
                    {
                        if (name.Contains(signal, StringComparison.OrdinalIgnoreCase))
                            found.Add(signal);
                    }
                }
            }
            catch
            {
                continue;
            }

            if (depth >= 3)
                continue;

            try
            {
                foreach (var child in Directory.EnumerateDirectories(directory))
                {
                    var name = Path.GetFileName(child);
                    if (name.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("_CommonRedist", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("redist", StringComparison.OrdinalIgnoreCase))
                        continue;

                    queue.Enqueue((child, depth + 1));
                }
            }
            catch { }
        }

        var signals = found.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        var message = signals.Count == 0
            ? "No known anti-cheat files were detected. This does NOT prove that online use is safe."
            : "Known anti-cheat signal(s) detected: " + string.Join(", ", signals);

        return new(signals.Count > 0, signals, message);
    }
}
