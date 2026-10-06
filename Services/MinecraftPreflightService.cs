using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace DlssNrManager.Services;

public enum MinecraftPreflightSeverity
{
    Ready,
    Warning,
    Unsupported
}

public sealed record MinecraftPreflightCheck(
    string Name,
    MinecraftPreflightSeverity Severity,
    string Details);

public sealed record MinecraftPreflightResult(
    MinecraftPreflightSeverity Status,
    IReadOnlyList<MinecraftPreflightCheck> Checks)
{
    public bool CanInstall => Status != MinecraftPreflightSeverity.Unsupported;

    public string Summary => Status switch
    {
        MinecraftPreflightSeverity.Ready => "Ready",
        MinecraftPreflightSeverity.Warning => "Warning",
        _ => "Unsupported"
    };
}

public sealed class MinecraftPreflightService
{
    private static readonly Version MinimumFabricLoader = new(0, 19, 3);

    private static readonly string[] ConflictingRendererTokens =
    [
        "sodium",
        "iris",
        "vulkanmod",
        "nvidium",
        "canvas",
        "optifine",
        "optifabric"
    ];

    public async Task<MinecraftPreflightResult> RunAsync(
        MinecraftInstallCandidate instance,
        CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(instance.RootDirectory);
        var checks = new List<MinecraftPreflightCheck>();

        if (!Directory.Exists(root))
        {
            checks.Add(new(
                "Minecraft instance",
                MinecraftPreflightSeverity.Unsupported,
                "The selected instance directory does not exist."));
            return Build(checks);
        }

        if (File.Exists(Path.Combine(root, ".dlss-nr-manager-oneclick.json")))
        {
            checks.Add(new(
                "Existing managed installation",
                MinecraftPreflightSeverity.Unsupported,
                "A previous one-click installation is still tracked. Use Restore original before installing again."));
        }
        else
        {
            checks.Add(new(
                "Existing managed installation",
                MinecraftPreflightSeverity.Ready,
                "No previous one-click installation marker is present."));
        }

        var nvidia = await QueryNvidiaAsync(cancellationToken);
        var gpuName = nvidia.GpuName;

        if (string.IsNullOrWhiteSpace(gpuName))
        {
            gpuName = DetectNvidiaGpuFromRegistry();
        }

        var isRtx = !string.IsNullOrWhiteSpace(gpuName) &&
                    gpuName.Contains("RTX", StringComparison.OrdinalIgnoreCase);

        checks.Add(isRtx
            ? new MinecraftPreflightCheck(
                "NVIDIA RTX GPU",
                MinecraftPreflightSeverity.Ready,
                gpuName!)
            : new MinecraftPreflightCheck(
                "NVIDIA RTX GPU",
                MinecraftPreflightSeverity.Unsupported,
                string.IsNullOrWhiteSpace(gpuName)
                    ? "No NVIDIA RTX GPU was detected."
                    : $"Detected {gpuName}, but Caustica RTX's DLSS path requires an NVIDIA RTX GPU."));

        var vulkan = await ProbeVulkanRayTracingAsync(cancellationToken);
        var vulkanDriverRegistered = HasVulkanDriverRegistration();

        if (vulkan.Supported)
        {
            checks.Add(new(
                "Vulkan ray tracing",
                MinecraftPreflightSeverity.Ready,
                "Required Vulkan RT extensions are available: acceleration structure, ray-tracing pipeline and deferred host operations."));
        }
        else if (vulkan.ProbeAvailable &&
                 isRtx &&
                 (!string.IsNullOrWhiteSpace(nvidia.DriverVersion) ||
                  vulkanDriverRegistered))
        {
            checks.Add(new(
                "Vulkan ray tracing",
                MinecraftPreflightSeverity.Warning,
                "An NVIDIA RTX GPU and active NVIDIA driver are present, but vulkaninfo did not confirm every required RT extension. " +
                $"Missing/unconfirmed: {string.Join(", ", vulkan.MissingExtensions)}. " +
                $"Probe details: {vulkan.Details}. " +
                "This can be a vulkaninfo/ICD selection false negative on multi-GPU systems. Installation may continue; Caustica startup is the final runtime capability test."));
        }
        else if (vulkan.ProbeAvailable)
        {
            checks.Add(new(
                "Vulkan ray tracing",
                MinecraftPreflightSeverity.Unsupported,
                "Vulkan is present, but the required ray-tracing extensions were not confirmed and no trusted RTX + Vulkan-driver fallback was available. " +
                $"Missing/unconfirmed: {string.Join(", ", vulkan.MissingExtensions)}."));
        }
        else if (vulkanDriverRegistered && isRtx)
        {
            checks.Add(new(
                "Vulkan ray tracing",
                MinecraftPreflightSeverity.Warning,
                "An NVIDIA RTX GPU and Vulkan driver are present, but vulkaninfo is unavailable. The installer can continue; Caustica startup is the final runtime capability test."));
        }
        else
        {
            checks.Add(new(
                "Vulkan ray tracing",
                MinecraftPreflightSeverity.Unsupported,
                "No usable Vulkan runtime/driver registration was detected."));
        }

        if (!string.IsNullOrWhiteSpace(nvidia.DriverVersion))
        {
            var driverSeverity = vulkan.Supported
                ? MinecraftPreflightSeverity.Ready
                : MinecraftPreflightSeverity.Warning;

            checks.Add(new(
                "NVIDIA driver",
                driverSeverity,
                $"Driver {nvidia.DriverVersion}. Caustica does not publish a fixed numeric minimum; Vulkan RT capability is used as the compatibility gate."));
        }
        else
        {
            checks.Add(new(
                "NVIDIA driver",
                MinecraftPreflightSeverity.Warning,
                "NVIDIA driver version could not be read with nvidia-smi. Update to a current Game Ready or Studio driver before troubleshooting RTX/DLSS issues."));
        }

        var java = await FindJava25Async(root, cancellationToken);
        checks.Add(java == null
            ? new MinecraftPreflightCheck(
                "Java 25",
                MinecraftPreflightSeverity.Warning,
                "Java 25 was not found. Install DLSS / RTX can provision Eclipse Temurin 25 automatically through WinGet.")
            : new MinecraftPreflightCheck(
                "Java 25",
                MinecraftPreflightSeverity.Ready,
                java));

        var minecraftVersion = DetectMinecraft262(root);
        checks.Add(minecraftVersion == VersionDetection.Detected
            ? new MinecraftPreflightCheck(
                "Minecraft 26.2",
                MinecraftPreflightSeverity.Ready,
                "Minecraft 26.2 was detected in the selected instance.")
            : minecraftVersion == VersionDetection.OtherDetected
                ? new MinecraftPreflightCheck(
                    "Minecraft 26.2",
                    MinecraftPreflightSeverity.Warning,
                    "Other Minecraft versions are present, but 26.2 was not detected locally. For the official launcher the installer can create the 26.2 Fabric profile; for custom launchers confirm the selected instance targets 26.2.")
                : new MinecraftPreflightCheck(
                    "Minecraft 26.2",
                    MinecraftPreflightSeverity.Warning,
                    "Minecraft version could not be verified from this launcher's local metadata. Confirm that this profile targets 26.2 before launch."));

        var fabric = DetectFabricLoader(root);
        var externalLauncher = IsExternallyManagedLauncher(instance);

        if (fabric.Version == null)
        {
            checks.Add(new(
                "Fabric Loader",
                externalLauncher
                    ? MinecraftPreflightSeverity.Unsupported
                    : MinecraftPreflightSeverity.Warning,
                externalLauncher
                    ? $"Fabric Loader {MinimumFabricLoader}+ for Minecraft 26.2 is required. Install/update Fabric from {instance.Source} first; the manager will not rewrite this launcher's instance metadata."
                    : $"Fabric Loader {MinimumFabricLoader} or newer was not detected. The one-click installer will install it."));
        }
        else if (fabric.Version < MinimumFabricLoader || !fabric.ForMinecraft262)
        {
            checks.Add(new(
                "Fabric Loader",
                externalLauncher
                    ? MinecraftPreflightSeverity.Unsupported
                    : MinecraftPreflightSeverity.Warning,
                externalLauncher
                    ? $"Fabric Loader {fabric.Version} is not a verified {MinimumFabricLoader}+ loader for Minecraft 26.2. Update the Fabric component from {instance.Source} first."
                    : $"Fabric Loader {fabric.Version} is not a verified {MinimumFabricLoader}+ loader for Minecraft 26.2. The one-click installer will update it."));
        }
        else
        {
            checks.Add(new(
                "Fabric Loader",
                MinecraftPreflightSeverity.Ready,
                $"Fabric Loader {fabric.Version} for Minecraft 26.2."));
        }

        if (externalLauncher && fabric.ForMinecraft262)
        {
            checks.Add(new(
                "Launcher JVM profile",
                MinecraftPreflightSeverity.Warning,
                $"{instance.Source} manages JVM arguments outside launcher_profiles.json. The manager can install the renderer stack, but verify -Xss16m and --enable-native-access=ALL-UNNAMED in that launcher's Java/JVM settings if Caustica reports native-access or stack issues."));
        }

        var launcherRunning =
            Process.GetProcessesByName("MinecraftLauncher").Length > 0;

        checks.Add(launcherRunning
            ? new MinecraftPreflightCheck(
                "Minecraft Launcher process",
                MinecraftPreflightSeverity.Unsupported,
                "Minecraft Launcher is currently running. Close it completely, then run the preflight again. Fabric and launcher profile files must not be modified while the launcher is active.")
            : new MinecraftPreflightCheck(
                "Minecraft Launcher process",
                MinecraftPreflightSeverity.Ready,
                "Minecraft Launcher is not running."));

        var conflicts = FindConflictingRendererMods(root);
        checks.Add(conflicts.Count == 0
            ? new MinecraftPreflightCheck(
                "Renderer mod conflicts",
                MinecraftPreflightSeverity.Ready,
                "No known conflicting renderer replacement was detected.")
            : new MinecraftPreflightCheck(
                "Renderer mod conflicts",
                MinecraftPreflightSeverity.Warning,
                $"{conflicts.Count} renderer mod(s) will be backed up and disabled automatically: {string.Join(", ", conflicts)}"));

        try
        {
            var probe = Path.Combine(root, $".dlss-nr-manager-write-{Guid.NewGuid():N}.tmp");
            await File.WriteAllTextAsync(probe, "preflight", cancellationToken);
            File.Delete(probe);

            checks.Add(new(
                "Instance write access",
                MinecraftPreflightSeverity.Ready,
                "The selected instance is writable."));
        }
        catch (Exception ex)
        {
            checks.Add(new(
                "Instance write access",
                MinecraftPreflightSeverity.Unsupported,
                $"The manager cannot safely modify this instance: {ex.Message}"));
        }

        return Build(checks);
    }

    public static IReadOnlyList<string> FindConflictingRendererMods(string root)
    {
        var mods = Path.Combine(root, "mods");
        if (!Directory.Exists(mods))
            return [];

        try
        {
            return Directory.EnumerateFiles(mods, "*.jar", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Where(name => ConflictingRendererTokens.Any(token =>
                    name!.Contains(token, StringComparison.OrdinalIgnoreCase)))
                .Cast<string>()
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    public static (Version? Version, bool ForMinecraft262) DetectFabricLoader(string root)
    {
        var versions = Path.Combine(root, "versions");
        Version? best = null;
        var for262 = false;

        try
        {
            if (Directory.Exists(versions))
            {
                foreach (var directory in Directory.EnumerateDirectories(
                             versions,
                             "fabric-loader-*",
                             SearchOption.TopDirectoryOnly))
                {
                    var name = Path.GetFileName(directory);
                    var match = Regex.Match(
                        name,
                        @"^fabric-loader-(?<version>\d+(?:\.\d+){1,3})(?:-[^-]+)*-(?<mc>\d+(?:\.\d+)+)$",
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

                    if (!match.Success ||
                        !Version.TryParse(match.Groups["version"].Value, out var version))
                        continue;

                    if (best == null || version > best)
                        best = version;

                    if (match.Groups["mc"].Value.Equals(
                            MinecraftIntegrationService.MinecraftVersion,
                            StringComparison.OrdinalIgnoreCase) &&
                        version >= MinimumFabricLoader)
                    {
                        for262 = true;
                    }
                }
            }

            foreach (var metadataRoot in CandidateMetadataRoots(root))
            {
                foreach (var metadata in new[]
                {
                    "mmc-pack.json",
                    "instance.json",
                    "profile.json",
                    "minecraftinstance.json",
                    "instance.cfg"
                })
                {
                    var path = Path.Combine(metadataRoot, metadata);
                    if (!File.Exists(path))
                        continue;

                    var text = File.ReadAllText(path);
                    if (!text.Contains("fabric", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var match = Regex.Match(
                        text,
                        @"fabric(?:mc)?(?:[. _-]*loader)?[^0-9]{0,80}(?<version>\d+\.\d+(?:\.\d+)?)",
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

                    if (!match.Success ||
                        !Version.TryParse(match.Groups["version"].Value, out var version))
                        continue;

                    if (best == null || version > best)
                        best = version;

                    if (text.Contains(
                            MinecraftIntegrationService.MinecraftVersion,
                            StringComparison.OrdinalIgnoreCase) &&
                        version >= MinimumFabricLoader)
                    {
                        for262 = true;
                    }
                }
            }
        }
        catch { }

        return (best, for262);
    }

    private static bool IsExternallyManagedLauncher(
        MinecraftInstallCandidate instance)
    {
        if (instance.Source.Contains("Prism", StringComparison.OrdinalIgnoreCase) ||
            instance.Source.Contains("Modrinth", StringComparison.OrdinalIgnoreCase) ||
            instance.Source.Contains("CurseForge", StringComparison.OrdinalIgnoreCase) ||
            instance.Source.Contains("GDLauncher", StringComparison.OrdinalIgnoreCase))
            return true;

        // Manual folder selection must not bypass launcher ownership detection.
        // Common third-party launchers keep their instance metadata either in
        // the selected game directory or its parent.
        foreach (var root in CandidateMetadataRoots(instance.RootDirectory))
        {
            foreach (var marker in new[]
            {
                "mmc-pack.json",
                "instance.cfg",
                "minecraftinstance.json",
                "instance.json"
            })
            {
                if (File.Exists(Path.Combine(root, marker)))
                    return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<string> CandidateMetadataRoots(string root)
    {
        var roots = new List<string> { root };

        try
        {
            var parent = Directory.GetParent(root)?.FullName;
            if (!string.IsNullOrWhiteSpace(parent))
                roots.Add(parent);
        }
        catch { }

        return roots;
    }

    private static MinecraftPreflightResult Build(
        IReadOnlyList<MinecraftPreflightCheck> checks)
    {
        var status = checks.Any(x => x.Severity == MinecraftPreflightSeverity.Unsupported)
            ? MinecraftPreflightSeverity.Unsupported
            : checks.Any(x => x.Severity == MinecraftPreflightSeverity.Warning)
                ? MinecraftPreflightSeverity.Warning
                : MinecraftPreflightSeverity.Ready;

        return new MinecraftPreflightResult(status, checks);
    }

    private static VersionDetection DetectMinecraft262(string root)
    {
        var versions = Path.Combine(root, "versions");
        var sawOtherVersion = false;

        try
        {
            if (Directory.Exists(versions))
            {
                foreach (var directory in Directory.EnumerateDirectories(
                             versions,
                             "*",
                             SearchOption.TopDirectoryOnly))
                {
                    var name = Path.GetFileName(directory);
                    if (name.Equals(
                            MinecraftIntegrationService.MinecraftVersion,
                            StringComparison.OrdinalIgnoreCase) ||
                        name.EndsWith(
                            "-" + MinecraftIntegrationService.MinecraftVersion,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return VersionDetection.Detected;
                    }

                    if (Regex.IsMatch(name, @"\d+\.\d+"))
                        sawOtherVersion = true;
                }
            }

            foreach (var metadataRoot in CandidateMetadataRoots(root))
            {
                foreach (var metadata in new[]
                {
                    "instance.cfg",
                    "mmc-pack.json",
                    "instance.json",
                    "profile.json",
                    "minecraftinstance.json"
                })
                {
                    var path = Path.Combine(metadataRoot, metadata);
                    if (!File.Exists(path))
                        continue;

                    var text = File.ReadAllText(path);
                    if (text.Contains(
                            MinecraftIntegrationService.MinecraftVersion,
                            StringComparison.OrdinalIgnoreCase))
                        return VersionDetection.Detected;

                    if (Regex.IsMatch(text, @"\b\d+\.\d+(?:\.\d+)?\b"))
                        sawOtherVersion = true;
                }
            }
        }
        catch { }

        return sawOtherVersion
            ? VersionDetection.OtherDetected
            : VersionDetection.Unknown;
    }

    private static async Task<(string? GpuName, string? DriverVersion)> QueryNvidiaAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await RunProcessAsync(
                "nvidia-smi.exe",
                [
                    "--query-gpu=name,driver_version",
                    "--format=csv,noheader"
                ],
                cancellationToken);

            if (result.ExitCode != 0)
                return (null, null);

            var line = result.Output
                .Replace("\r", "")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(line))
                return (null, null);

            var parts = line.Split(',', 2);
            return (
                parts.ElementAtOrDefault(0)?.Trim(),
                parts.ElementAtOrDefault(1)?.Trim());
        }
        catch
        {
            return (null, null);
        }
    }

    private static string? DetectNvidiaGpuFromRegistry()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Video");

            if (key == null)
                return null;

            foreach (var sub in key.GetSubKeyNames())
            {
                using var gpu = key.OpenSubKey(sub + @"\0000");
                var name = gpu?.GetValue("DriverDesc") as string;
                if (!string.IsNullOrWhiteSpace(name) &&
                    name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
                    return name;
            }
        }
        catch { }

        return null;
    }

    private static async Task<VulkanProbeResult> ProbeVulkanRayTracingAsync(
        CancellationToken cancellationToken)
    {
        var required = new[]
        {
            "VK_KHR_acceleration_structure",
            "VK_KHR_ray_tracing_pipeline",
            "VK_KHR_deferred_host_operations"
        };

        var vulkanInfo = FindExecutable("vulkaninfo.exe");
        if (vulkanInfo == null)
            return new VulkanProbeResult(false, false, required, "vulkaninfo.exe not found");

        try
        {
            var result = await RunProcessAsync(
                vulkanInfo,
                [],
                cancellationToken,
                timeout: TimeSpan.FromSeconds(20));

            var output = result.Output + "\n" + result.Error;
            if (result.ExitCode != 0 && string.IsNullOrWhiteSpace(output))
            {
                return new VulkanProbeResult(
                    true,
                    false,
                    required,
                    $"vulkaninfo exited with code {result.ExitCode} and produced no output");
            }

            var missing = required
                .Where(extension =>
                    !output.Contains(
                        extension,
                        StringComparison.OrdinalIgnoreCase))
                .ToArray();

            return new VulkanProbeResult(
                true,
                missing.Length == 0,
                missing,
                $"vulkaninfo exit code {result.ExitCode}");
        }
        catch (Exception ex)
        {
            return new VulkanProbeResult(
                true,
                false,
                required,
                $"vulkaninfo probe failed: {ex.Message}");
        }
    }

    private static bool HasVulkanDriverRegistration()
    {
        foreach (var registryPath in new[]
        {
            @"SOFTWARE\Khronos\Vulkan\Drivers",
            @"SOFTWARE\WOW6432Node\Khronos\Vulkan\Drivers"
        })
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(registryPath);
                if (key != null && key.GetValueNames().Length > 0)
                    return true;
            }
            catch { }
        }

        return false;
    }

    private static async Task<string?> FindJava25Async(
        string minecraftRoot,
        CancellationToken cancellationToken)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? path)
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                candidates.Add(path);
        }

        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(javaHome))
            Add(Path.Combine(javaHome, "bin", "java.exe"));

        var onPath = FindExecutable("java.exe");
        Add(onPath);

        foreach (var runtimeRoot in new[]
        {
            Path.Combine(minecraftRoot, "runtime"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                ".minecraft",
                "runtime")
        })
        {
            if (!Directory.Exists(runtimeRoot))
                continue;

            try
            {
                foreach (var candidate in Directory.EnumerateFiles(
                             runtimeRoot,
                             "java.exe",
                             SearchOption.AllDirectories))
                    Add(candidate);
            }
            catch { }
        }

        foreach (var programFiles in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        })
        {
            foreach (var vendor in new[]
            {
                "Java",
                "Eclipse Adoptium",
                "Microsoft",
                "Zulu",
                "Amazon Corretto"
            })
            {
                var vendorRoot = Path.Combine(programFiles, vendor);
                if (!Directory.Exists(vendorRoot))
                    continue;

                try
                {
                    foreach (var candidate in Directory.EnumerateFiles(
                                 vendorRoot,
                                 "java.exe",
                                 SearchOption.AllDirectories))
                        Add(candidate);
                }
                catch { }
            }
        }

        foreach (var candidate in candidates)
        {
            try
            {
                var result = await RunProcessAsync(
                    candidate,
                    ["-version"],
                    cancellationToken,
                    timeout: TimeSpan.FromSeconds(5));

                var versionText = result.Output + "\n" + result.Error;
                var match = Regex.Match(
                    versionText,
                    "\\\"(?<major>\\d+)",
                    RegexOptions.CultureInvariant);

                if (match.Success &&
                    int.TryParse(match.Groups["major"].Value, out var major) &&
                    major == 25)
                    return candidate;
            }
            catch { }
        }

        return null;
    }

    private static string? FindExecutable(string name)
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "")
                     .Split(
                         Path.PathSeparator,
                         StringSplitOptions.RemoveEmptyEntries |
                         StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Combine(directory, name);
                if (File.Exists(candidate))
                    return candidate;
            }
            catch { }
        }

        foreach (var root in new[]
        {
            Environment.GetEnvironmentVariable("VULKAN_SDK"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "VulkanRT")
        })
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                continue;

            try
            {
                var candidate = Directory.EnumerateFiles(
                        root,
                        name,
                        SearchOption.AllDirectories)
                    .FirstOrDefault();

                if (candidate != null)
                    return candidate;
            }
            catch { }
        }

        return null;
    }

    private static async Task<ProcessResult> RunProcessAsync(
        string executable,
        IEnumerable<string> arguments,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null)
    {
        using var timeoutCts = timeout.HasValue
            ? new CancellationTokenSource(timeout.Value)
            : null;
        using var linkedCts = timeoutCts == null
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutCts.Token);

        var psi = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in arguments)
            psi.ArgumentList.Add(argument);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException(
                $"Could not start {Path.GetFileName(executable)}.");

        var stdout = process.StandardOutput.ReadToEndAsync(linkedCts.Token);
        var stderr = process.StandardError.ReadToEndAsync(linkedCts.Token);
        await process.WaitForExitAsync(linkedCts.Token);

        return new ProcessResult(
            process.ExitCode,
            await stdout,
            await stderr);
    }

    private enum VersionDetection
    {
        Unknown,
        Detected,
        OtherDetected
    }

    private sealed record VulkanProbeResult(
        bool ProbeAvailable,
        bool Supported,
        IReadOnlyList<string> MissingExtensions,
        string Details);

    private sealed record ProcessResult(
        int ExitCode,
        string Output,
        string Error);
}
