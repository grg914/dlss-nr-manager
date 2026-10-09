using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace DlssNrManager.Services;

/// <summary>
/// On-demand, read-only observations. A known NVIDIA issue notice is not a
/// diagnosis of the user's PC or permission to alter driver settings.
/// </summary>
public sealed record NvidiaDriverHealthReport(
    DateTimeOffset CapturedAtUtc,
    string GpuName,
    string DriverVersion,
    double? GpuUtilization,
    double? UsedVramMiB,
    double? GraphicsClockMhz,
    double? TemperatureC,
    double? PowerWatts,
    double? NvidiaContainerCpuPercent,
    bool AmbiguousGpu,
    bool NvidiaSmiAvailable,
    IReadOnlyList<string> Notes)
{
    public bool HasVendorNotice => NvidiaDriverHealthService.HasDocumented61742Issue(DriverVersion);
}

public sealed class NvidiaDriverHealthService
{
    public const string NvidiaSupportUrl = "https://www.nvidia.com/en-us/geforce/drivers/";
    public const string KnownIssueUrl =
        "https://www.nvidia.com/en-us/geforce/forums/game-ready-drivers/13/591391/geforce-grd-61742-feedback-thread-released-10626/";

    // Only literal read-only queries; never accept arguments from the UI.
    private const string IdentityQuery = "--query-gpu=uuid,name,driver_version";
    private const string MetricsQuery =
        "--query-gpu=uuid,utilization.gpu,memory.used,clocks.gr,temperature.gpu,power.draw";
    private const string CsvFormat = "--format=csv,noheader,nounits";
    private const int MaxOutputCharacters = 32768;

    public static bool HasDocumented61742Issue(string? driver) =>
        string.Equals(driver?.Trim(), "617.42", StringComparison.Ordinal);

    public static double? CpuPercent(TimeSpan cpuDelta, TimeSpan elapsed, int logicalProcessors)
    {
        if (logicalProcessors <= 0 || elapsed <= TimeSpan.Zero || cpuDelta < TimeSpan.Zero)
            return null;

        var result = 100d * cpuDelta.TotalSeconds /
                     (elapsed.TotalSeconds * logicalProcessors);
        return double.IsFinite(result) && result >= 0 && result <= 100.01
            ? Math.Min(100, result)
            : null;
    }

    public static IReadOnlyList<string[]> ReadCsv(string? output, int fieldCount)
    {
        if (string.IsNullOrWhiteSpace(output) ||
            output.Length > MaxOutputCharacters || fieldCount < 1)
            return [];

        var result = new List<string[]>();
        foreach (var line in output.Split(['\r', '\n'],
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var fields = new List<string>();
            var current = new StringBuilder();
            var quoted = false;
            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (c == '"')
                {
                    if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                        quoted = !quoted;
                }
                else if (c == ',' && !quoted)
                {
                    fields.Add(current.ToString().Trim());
                    current.Clear();
                }
                else
                    current.Append(c);
            }
            if (quoted)
                return [];
            fields.Add(current.ToString().Trim());
            if (fields.Count != fieldCount || result.Count >= 16)
                return [];
            result.Add(fields.ToArray());
        }

        return result;
    }

    public static double? ReadMetric(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Equals("N/A", StringComparison.OrdinalIgnoreCase))
            return null;
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture,
                   out var parsed) && double.IsFinite(parsed) && parsed >= 0
            ? parsed
            : null;
    }

    /// <summary>
    /// The UI supplies its existing hardware snapshot; this method never updates
    /// cached GPU preferences and never opens an Internet connection.
    /// </summary>
    public async Task<NvidiaDriverHealthReport> DiagnoseAsync(
        HardwareSnapshot? hardware, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var notes = new List<string>();
        var gpuName = hardware?.Gpu.Name ?? "Unknown GPU";
        var driver = hardware?.NvidiaDriverVersion ?? "Unknown";
        double? utilization = null, vram = null, clock = null, temperature = null, watts = null;
        var ambiguous = false;
        var smi = false;

        if (!OperatingSystem.IsWindows())
            notes.Add("Windows-only NVIDIA diagnostics are unavailable on this OS.");
        else
        {
            // A stale registry snapshot must not prevent a fresh read-only
            // NVIDIA-SMI probe; unknown GPU identity remains fail-closed.
            var executable = NvidiaSmiLocator.FindInstalled();
            if (executable == null)
                notes.Add("Trusted-location nvidia-smi.exe was not found. GPU telemetry is unavailable.");
            else
            {
                var identity = ReadCsv(
                    await QueryAsync(executable, IdentityQuery, cancellationToken), 3);
                smi = identity.Count > 0;
                if (!smi)
                {
                    notes.Add("NVIDIA-SMI identity query failed; cached hardware details may be incomplete.");
                    if (hardware?.Gpu.IsNvidia != true)
                        notes.Add("A supported NVIDIA GPU has not been confirmed.");
                }
                else
                {
                    var candidates = identity.Where(row =>
                        string.Equals(row[1], gpuName, StringComparison.OrdinalIgnoreCase) ||
                        row[1].EndsWith(gpuName, StringComparison.OrdinalIgnoreCase) ||
                        gpuName.EndsWith(row[1], StringComparison.OrdinalIgnoreCase)).ToArray();
                    var selected = candidates.Length == 1 ? candidates[0] :
                        identity.Count == 1 ? identity[0] : null;
                    ambiguous = selected == null;
                    if (ambiguous)
                        notes.Add("Multiple GPUs or unmatched identity: per-GPU data cannot be attributed safely.");
                    else
                    {
                        gpuName = selected![1];
                        driver = selected[2];
                        if (!string.IsNullOrWhiteSpace(selected[0]) &&
                            !selected[0].Equals("N/A", StringComparison.OrdinalIgnoreCase))
                        {
                            var measured = ReadCsv(
                                await QueryAsync(executable, MetricsQuery, cancellationToken), 6);
                            var matching = measured.Where(row => row[0] == selected[0]).ToArray();
                            if (matching.Length == 1)
                            {
                                utilization = ReadMetric(matching[0][1]);
                                vram = ReadMetric(matching[0][2]);
                                clock = ReadMetric(matching[0][3]);
                                temperature = ReadMetric(matching[0][4]);
                                watts = ReadMetric(matching[0][5]);
                            }
                            else
                                notes.Add("Optional GPU measurements are missing or cannot be tied to one device.");
                        }
                        else
                            notes.Add("GPU UUID unavailable; optional measurements were not attributed.");
                    }
                }
            }
        }

        // A brief observation is not a sustained CPU-usage diagnosis and the
        // process name alone is not proof of executable publisher identity.
        double? containerCpu = null;
        if (OperatingSystem.IsWindows())
        {
            var before = CaptureNvidiaContainerCpu();
            if (before.Count > 0)
            {
                var started = Stopwatch.GetTimestamp();
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                var after = CaptureNvidiaContainerCpu();
                var elapsed = Stopwatch.GetElapsedTime(started);
                var stable = before.Join(after,
                    a => (a.Pid, a.StartTimeUtc),
                    z => (z.Pid, z.StartTimeUtc),
                    (a, z) => z.CpuTime - a.CpuTime).ToArray();
                if (stable.Length > 0)
                    containerCpu = CpuPercent(
                        TimeSpan.FromTicks(stable.Sum(x => x.Ticks)), elapsed,
                        Environment.ProcessorCount);
            }
        }

        return new NvidiaDriverHealthReport(
            DateTimeOffset.UtcNow, gpuName, driver, utilization, vram, clock,
            temperature, watts, containerCpu, ambiguous, smi, notes);
    }

    private sealed record CpuSample(int Pid, DateTime StartTimeUtc, TimeSpan CpuTime);

    private static List<CpuSample> CaptureNvidiaContainerCpu()
    {
        var samples = new List<CpuSample>();
        try
        {
            foreach (var process in Process.GetProcessesByName("NVDisplay.Container"))
            {
                using (process)
                {
                    try
                    {
                        samples.Add(new CpuSample(
                            process.Id, process.StartTime.ToUniversalTime(),
                            process.TotalProcessorTime));
                    }
                    catch (Exception e) when (e is Win32Exception or
                                              InvalidOperationException or
                                              UnauthorizedAccessException)
                    {
                        // Inaccessible or exited process: do not speculate.
                    }
                }
            }
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            // Enumeration is optional.
        }
        return samples;
    }

    private static async Task<string?> QueryAsync(
        string executable, string query, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        Process? process = null;
        try
        {
            var info = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            info.ArgumentList.Add(query);
            info.ArgumentList.Add(CsvFormat);
            process = ExternalProcessTracker.Start(info);
            var stdout = ReadLimitedAsync(process.StandardOutput, timeout.Token);
            var stderr = ReadLimitedAsync(process.StandardError, timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var output = await stdout;
            _ = await stderr;
            return process.ExitCode == 0 ? output : null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or
                                  InvalidOperationException or UnauthorizedAccessException or
                                  InvalidDataException)
        {
            return null;
        }
        finally
        {
            if (process != null)
            {
                if (!process.HasExited)
                    ExternalProcessTracker.Kill(process);
                process.Dispose();
            }
        }
    }

    private static async Task<string> ReadLimitedAsync(
        StreamReader reader, CancellationToken cancellationToken)
    {
        var buffer = new char[1024];
        var text = new StringBuilder();
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (count == 0)
                break;
            if (text.Length + count > MaxOutputCharacters)
                throw new InvalidDataException("NVIDIA-SMI output is too large.");
            text.Append(buffer, 0, count);
        }
        return text.ToString();
    }

    private static string TranslateNote(string note) => note switch
    {
        "Windows-only NVIDIA diagnostics are unavailable on this OS." =>
            "Le diagnostic NVIDIA est réservé à Windows.",
        "A supported NVIDIA GPU has not been confirmed." =>
            "Aucune carte NVIDIA compatible n'a été confirmée.",
        "Trusted-location nvidia-smi.exe was not found. GPU telemetry is unavailable." =>
            "NVIDIA-SMI introuvable dans les emplacements autorisés ; mesures GPU indisponibles.",
        "NVIDIA-SMI identity query failed; cached hardware details may be incomplete." =>
            "Identification NVIDIA-SMI impossible ; les données matérielles précédentes peuvent être incomplètes.",
        "Multiple GPUs or unmatched identity: per-GPU data cannot be attributed safely." =>
            "Plusieurs GPU ou identité différente : attribution des mesures impossible.",
        "Optional GPU measurements are missing or cannot be tied to one device." =>
            "Mesures GPU facultatives indisponibles ou non attribuables à une seule carte.",
        "GPU UUID unavailable; optional measurements were not attributed." =>
            "UUID GPU indisponible ; mesures facultatives non attribuées.",
        _ => note
    };

    public static string Format(NvidiaDriverHealthReport report, bool french)
    {
        string Label(string en, string fr) => french ? fr : en;
        string Num(double? value, string units) =>
            value.HasValue ? value.Value.ToString("0.0", CultureInfo.InvariantCulture) +
                             " " + units : Label("Unavailable", "Indisponible");

        var rows = new List<string>
        {
            Label("Read-only NVIDIA driver diagnosis", "Diagnostic NVIDIA en lecture seule"),
            Label("Captured (UTC): ", "Mesuré (UTC) : ") +
                report.CapturedAtUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            Label("NVIDIA-SMI available: ", "NVIDIA-SMI disponible : ") +
                (report.NvidiaSmiAvailable ? Label("Yes", "Oui") : Label("No", "Non")),
            Label("GPU: ", "GPU : ") + report.GpuName,
            Label("Driver: ", "Pilote : ") + report.DriverVersion,
            Label("GPU utilization: ", "Utilisation GPU : ") + Num(report.GpuUtilization, "%"),
            Label("VRAM in use: ", "VRAM utilisée : ") + Num(report.UsedVramMiB, "MiB"),
            Label("Graphics clock: ", "Fréquence graphique : ") + Num(report.GraphicsClockMhz, "MHz"),
            Label("GPU temperature: ", "Température GPU : ") + Num(report.TemperatureC, "°C"),
            Label("GPU power draw: ", "Consommation GPU : ") + Num(report.PowerWatts, "W"),
            Label("NVIDIA Container CPU (short sample): ", "CPU NVIDIA Container (échantillon court) : ") +
                Num(report.NvidiaContainerCpuPercent, "%")
        };
        rows.Add(report.HasVendorNotice
            ? Label(
                "NVIDIA lists issue #6007998 for driver 617.42 (maximum-performance mode may not apply). Impact on this PC is unconfirmed.",
                "NVIDIA signale le problème #6007998 pour le pilote 617.42 (mode performances maximales potentiellement non appliqué). Impact sur ce PC non confirmé.")
            : Label(
                "No matching known issue in the offline catalogue; this does NOT prove the driver is healthy.",
                "Aucun problème correspondant dans le catalogue hors ligne ; cela ne prouve PAS que le pilote fonctionne parfaitement."));
        if (report.AmbiguousGpu)
            rows.Add(Label("Multiple GPU identity ambiguous.", "Attribution ambiguë entre plusieurs GPU."));
        foreach (var note in report.Notes)
            rows.Add(Label("Note: ", "Note : ") + (french ? TranslateNote(note) : note));
        rows.Add(Label(
            "Suggested manual steps: compare the same game scene and graphics settings using NVIDIA App's FPS/1% low overlay; consult NVIDIA's official driver guidance if regression persists.",
            "Conseils manuels : comparer la même scène de jeu et les mêmes réglages avec les FPS/1 % low de NVIDIA App ; consulter l'aide officielle NVIDIA si la baisse persiste."));
        rows.Add(Label(
            "Container CPU observations are based on process name, not verified NVIDIA publisher identity or driver causality.",
            "Les mesures CPU Container reposent sur le nom du processus, sans vérifier sa signature NVIDIA ni la cause du problème."));
        rows.Add(Label(
            "No driver settings, profiles, clocks, services or caches were changed. A short idle snapshot cannot measure gaming FPS.",
            "Aucun réglage pilote, profil, fréquence, service ou cache modifié. Une mesure au repos ne mesure pas les FPS en jeu."));
        return string.Join(Environment.NewLine, rows);
    }
}
