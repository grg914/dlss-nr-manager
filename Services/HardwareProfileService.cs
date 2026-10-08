using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;
using DlssNrManager.Models;

namespace DlssNrManager.Services;

public enum HardwareProfileChoice
{
    Auto = 0,
    Rtx5060Ti9700X = 1
}

public enum HardwareOperatingMode
{
    Normal = 0,
    Compatible = 1
}

public sealed record HardwareSnapshot(
    GpuInfo Gpu,
    string CpuName,
    ulong InstalledRamBytes,
    long? GpuVramMiB,
    string NvidiaDriverVersion,
    bool DirectX12RuntimePresent,
    bool VulkanLoaderPresent,
    string CpuArchitecture)
{
    public string GpuArchitecture => Gpu.Generation switch
    {
        "RTX 50" => "Blackwell",
        "RTX 40" => "Ada Lovelace",
        "RTX 30" => "Ampere",
        "RTX 20" => "Turing",
        _ => "Unknown"
    };

    // A driver or hardware change invalidates the previous profile assessment.
    public string Fingerprint => string.Join("|",
        Gpu.Name, Gpu.Generation, CpuName, InstalledRamBytes,
        GpuVramMiB, NvidiaDriverVersion, DirectX12RuntimePresent,
        VulkanLoaderPresent, CpuArchitecture);
}

public sealed record HardwareProfileDecision(
    RtxCapabilities Capabilities,
    bool AllowsFrameGeneration,
    bool AllowsNeuralRendering,
    bool MatchesRequestedPreset,
    bool UsesAutomaticFallback);

public sealed record HardwareProfilePreferences(
    HardwareProfileChoice Profile = HardwareProfileChoice.Auto,
    HardwareOperatingMode Mode = HardwareOperatingMode.Normal);

public sealed class HardwareProfileService
{
    private static readonly string PreferencesPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DlssNrManager", "hardware-profile.json");

    // Deliberately read-only probing. Presence of a runtime/loader is not proof
    // that a specific GPU/API feature is supported in the currently selected game.
    public HardwareSnapshot Detect()
    {
        var gpu = new GpuDetectionService().Detect();
        var (driver, vram) = ReadNvidiaSmi(gpu.Name);
        var cpu = ReadCpuName();
        var ram = ReadInstalledRam();
        var directX = OperatingSystem.IsWindows() &&
                      File.Exists(Path.Combine(Environment.SystemDirectory, "d3d12.dll"));
        var vulkan = OperatingSystem.IsWindows() &&
                     File.Exists(Path.Combine(Environment.SystemDirectory, "vulkan-1.dll")) &&
                     HasVulkanDriverManifest();

        return new HardwareSnapshot(
            gpu, cpu, ram, vram, driver, directX, vulkan,
            RuntimeInformation.OSArchitecture.ToString());
    }

    public static HardwareProfileDecision Evaluate(
        HardwareSnapshot snapshot,
        HardwareProfilePreferences preferences)
    {
        var capabilities = GpuCapabilityService.Evaluate(snapshot.Gpu);
        var matched = snapshot.Gpu.Generation == "RTX 50" &&
                      snapshot.Gpu.Name.Contains("5060 Ti", StringComparison.OrdinalIgnoreCase) &&
                      snapshot.CpuName.Contains("9700X", StringComparison.OrdinalIgnoreCase) &&
                      snapshot.GpuVramMiB is >= 15000;
        var fallback = preferences.Profile == HardwareProfileChoice.Rtx5060Ti9700X &&
                       !matched;
        var compatible = preferences.Mode == HardwareOperatingMode.Compatible;

        // A manually selected preset NEVER upgrades the actually detected GPU.
        return new HardwareProfileDecision(
            capabilities,
            capabilities.FrameGeneration && !compatible,
            capabilities.NeuralRendering && !compatible,
            matched && preferences.Profile == HardwareProfileChoice.Rtx5060Ti9700X,
            fallback);
    }

    public static HardwareProfilePreferences LoadPreferences()
    {
        try
        {
            var saved = JsonSerializer.Deserialize<HardwareProfilePreferences>(
                File.ReadAllText(PreferencesPath));
            if (saved != null &&
                Enum.IsDefined(saved.Profile) &&
                Enum.IsDefined(saved.Mode))
                return saved;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (JsonException) { }
        return new HardwareProfilePreferences();
    }

    public static void SavePreferences(HardwareProfilePreferences preferences)
    {
        if (!Enum.IsDefined(preferences.Profile) ||
            !Enum.IsDefined(preferences.Mode))
            throw new ArgumentOutOfRangeException(nameof(preferences));

        Directory.CreateDirectory(Path.GetDirectoryName(PreferencesPath)!);
        AtomicFile.WriteAllText(PreferencesPath,
            JsonSerializer.Serialize(preferences, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
    }

    private static string ReadCpuName()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            if (key?.GetValue("ProcessorNameString") is string name &&
                !string.IsNullOrWhiteSpace(name))
                return name.Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLogger.Warn("CPU registry probe unavailable: " + ex.Message);
        }
        return Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER")
               ?? "Unknown CPU";
    }

    private static (string Driver, long? VramMiB) ReadNvidiaSmi(string selectedGpu)
    {
        try
        {
            // Never resolve an executable from the current directory or PATH.
            // Only trusted Windows NVIDIA installation locations are probed.
            var candidates = new[]
            {
                Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe")
            };
            var executable = candidates.FirstOrDefault(File.Exists);
            if (executable == null)
                return ("Unknown", null);

            using var process = ExternalProcessTracker.Start(new ProcessStartInfo
            {
                FileName = executable,
                Arguments = "--query-gpu=name,driver_version,memory.total --format=csv,noheader,nounits",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });
            if (!process.WaitForExit(3000))
            {
                ExternalProcessTracker.Kill(process);
                return ("Unknown", null);
            }
            if (process.ExitCode != 0)
                return ("Unknown", null);

            var rows = process.StandardOutput.ReadToEnd().Split(
                ['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var parsed = rows.Select(row => row.Split(',').Select(part => part.Trim()).ToArray())
                .Where(parts => parts.Length >= 3).ToArray();
            var selected = parsed.FirstOrDefault(parts =>
                parts[0].Equals(selectedGpu, StringComparison.OrdinalIgnoreCase));
            if (selected == null && parsed.Length == 1)
                selected = parsed[0];
            if (selected == null)
                return ("Unknown", null);

            var vram = long.TryParse(selected[2], NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var mib) && mib > 0 ? mib : (long?)null;
            return (selected[1], vram);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or
                                    InvalidOperationException or IOException or
                                    UnauthorizedAccessException)
        {
            return ("Unknown", null);
        }
    }

    private static bool HasVulkanDriverManifest()
    {
        foreach (var location in new[]
                 {
                     @"SOFTWARE\Khronos\Vulkan\Drivers",
                     @"SOFTWARE\WOW6432Node\Khronos\Vulkan\Drivers"
                 })
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(location);
                if (key?.GetValueNames().Length > 0)
                    return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLogger.Warn("Vulkan registry probe unavailable: " + ex.Message);
            }
        }
        return false;
    }

    private static ulong ReadInstalledRam()
    {
        if (!OperatingSystem.IsWindows())
            return 0;

        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        return GlobalMemoryStatusEx(ref memory) ? memory.TotalPhysical : 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus memory);
}
