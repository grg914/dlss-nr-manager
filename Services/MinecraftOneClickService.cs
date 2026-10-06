using System.Text.Json;
using System.Text.Json.Nodes;

namespace DlssNrManager.Services;

public sealed record MinecraftOneClickResult(
    MinecraftSetupResult Setup,
    bool FabricInstalledByManager,
    IReadOnlyList<string> DisabledConflictingMods,
    string BackupDirectory,
    IReadOnlyList<string> Notes);

public sealed class MinecraftOneClickService
{
    private readonly MinecraftIntegrationService _integration;

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

    public MinecraftOneClickService(MinecraftIntegrationService integration)
        => _integration = integration;

    public async Task<MinecraftOneClickResult> InstallAsync(
        MinecraftInstallCandidate instance,
        bool installFabricApi,
        bool allowPrereleaseCaustica,
        bool installRtxPerformancePack,
        bool installLabPbrResourcePack,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(instance.RootDirectory);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException(root);

        var markerPath = Path.Combine(root, ".dlss-nr-manager-oneclick.json");
        if (File.Exists(markerPath))
        {
            throw new InvalidOperationException(
                "A one-click Minecraft DLSS / RTX installation is already tracked for this instance. " +
                "Use Restore original before installing again.");
        }

        var backup = Path.Combine(
            root,
            ".dlss-nr-manager-backups",
            "minecraft-one-click",
            DateTimeOffset.Now.ToString("yyyyMMdd-HHmmssfff"));

        Directory.CreateDirectory(backup);

        var optionsPath = Path.Combine(root, "options.txt");
        var launcherProfilesPath = Path.Combine(root, "launcher_profiles.json");
        var optionsExisted = File.Exists(optionsPath);
        var launcherProfilesExisted = File.Exists(launcherProfilesPath);

        BackupFile(optionsPath, Path.Combine(backup, "options.txt"));
        BackupFile(launcherProfilesPath, Path.Combine(backup, "launcher_profiles.json"));

        BackupDirectoryIfExists(
            Path.Combine(root, "caustica-streamline"),
            Path.Combine(backup, "caustica-streamline"));

        BackupMatchingConfig(root, backup);

        var disabled = new List<string>();
        var fabricWasPresent = instance.FabricDetected;
        var existingFabricVersions = SnapshotFabricVersionDirectories(root);
        var fabricBefore = MinecraftPreflightService.DetectFabricLoader(root);
        MinecraftSetupResult? completedSetup = null;

        try
        {
            progress?.Report("Verifying Java 25 runtime…");
            await _integration.EnsureJava25RuntimeAsync(
                root,
                progress,
                cancellationToken);

            disabled = DisableConflictingRendererMods(root, backup, progress);

            progress?.Report("Forcing Minecraft 26.2 to prefer the Vulkan graphics backend…");
            SetPreferredGraphicsBackend(root, "vulkan");

            if (fabricBefore.Version == null ||
                fabricBefore.Version < Version.Parse(MinecraftIntegrationService.MinimumFabricLoader) ||
                !fabricBefore.ForMinecraft262)
            {
                progress?.Report(
                    $"Installing/updating Fabric Loader {MinecraftIntegrationService.MinimumFabricLoader}+ for Minecraft {MinecraftIntegrationService.MinecraftVersion}…");

                await _integration.LaunchFabricInstallerAsync(
                    root,
                    progress,
                    cancellationToken);
            }

            var refreshed = _integration.CreateManualCandidate(root);
            var fabricAfter = MinecraftPreflightService.DetectFabricLoader(root);
            if (!fabricAfter.ForMinecraft262 ||
                fabricAfter.Version == null ||
                fabricAfter.Version < Version.Parse(MinecraftIntegrationService.MinimumFabricLoader))
            {
                throw new InvalidOperationException(
                    $"Fabric Loader {MinecraftIntegrationService.MinimumFabricLoader}+ for Minecraft {MinecraftIntegrationService.MinecraftVersion} was not detected after installation.");
            }

            PatchFabricLauncherProfile(
                launcherProfilesPath,
                progress);

            progress?.Report("Installing Fabric API and the Caustica RTX production bundle…");
            var setup = await _integration.InstallMinecraftRtxAsync(
                refreshed,
                installFabricApi,
                allowPrereleaseCaustica,
                installRtxPerformancePack,
                installLabPbrResourcePack,
                progress,
                cancellationToken);

            completedSetup = setup;

            var createdFabricVersions = SnapshotFabricVersionDirectories(root)
                .Except(existingFabricVersions, StringComparer.OrdinalIgnoreCase)
                .Select(path => Path.GetRelativePath(root, path))
                .ToList();

            var marker = new OneClickManifest(
                DateTimeOffset.UtcNow,
                fabricWasPresent,
                optionsExisted,
                launcherProfilesExisted,
                Path.GetRelativePath(root, backup),
                Path.GetRelativePath(root, setup.BackupDirectory),
                disabled
                    .Select(path => Path.GetRelativePath(root, path))
                    .ToList(),
                createdFabricVersions);

            File.WriteAllText(
                markerPath,
                JsonSerializer.Serialize(
                    marker,
                    new JsonSerializerOptions { WriteIndented = true }));

            var notes = new List<string>
            {
                "Minecraft graphics backend preference set to Vulkan.",
                "Caustica RTX provides path tracing, DLSS Ray Reconstruction, Frame Generation/MFG and NVIDIA Reflex through its own renderer.",
                "Open Video Settings → Ray Tracing after first launch to choose DLSS quality, Frame Generation multiplier and Reflex mode."
            };

            if (installRtxPerformancePack)
            {
                var requestedPerformanceMods = new[]
                {
                    "Lithium",
                    "FerriteCore",
                    "Krypton",
                    "Dynamic FPS"
                };

                var installedPerformanceMods = requestedPerformanceMods
                    .Where(name =>
                        setup.Components.Any(component =>
                            component.Component.Equals(
                                name,
                                StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                if (installedPerformanceMods.Count > 0)
                {
                    notes.Add(
                        "Performance mods installed: " +
                        string.Join(", ", installedPerformanceMods) +
                        ". These avoid replacing the world renderer; Caustica remains experimental, so validate the first launch.");
                }

                var skipped = requestedPerformanceMods
                    .Except(
                        installedPerformanceMods,
                        StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (skipped.Count > 0)
                {
                    notes.Add(
                        "Optional performance mods skipped: " +
                        string.Join(", ", skipped) +
                        ". The core Caustica RTX installation remains valid.");
                }
            }

            if (installLabPbrResourcePack)
            {
                var spbrInstalled = setup.Components.Any(component =>
                    component.Component.Equals(
                        "SPBR LabPBR",
                        StringComparison.OrdinalIgnoreCase));

                notes.Add(
                    spbrInstalled
                        ? "SPBR LabPBR resource pack installed. Enable SPBR in Minecraft Resource Packs to use its PBR materials with Caustica RTX."
                        : "Optional SPBR LabPBR resource pack was skipped. The core Caustica RTX installation remains valid.");
            }

            if (disabled.Count > 0)
            {
                notes.Add(
                    $"{disabled.Count} potentially conflicting renderer mod(s) were moved into the manager backup and will be restored by Restore original.");
            }

            progress?.Report(
                "Minecraft DLSS / RTX stack is installed. Launch the Fabric profile, then use Video Settings → Ray Tracing for DLSS/FG/Reflex controls.");

            return new MinecraftOneClickResult(
                setup,
                !fabricWasPresent,
                disabled,
                backup,
                notes);
        }
        catch
        {
            try
            {
                if (completedSetup != null)
                {
                    try
                    {
                        _integration.UninstallManagedMinecraftRtx(root);
                    }
                    catch { }

                    RestoreTopLevelFiles(
                        completedSetup.BackupDirectory,
                        Path.Combine(root, "mods"));

                    RestoreDirectoryContents(
                        Path.Combine(
                            completedSetup.BackupDirectory,
                            "extra",
                            "mods"),
                        Path.Combine(root, "mods"));

                    RestoreDirectoryContents(
                        Path.Combine(
                            completedSetup.BackupDirectory,
                            "extra",
                            "resourcepacks"),
                        Path.Combine(root, "resourcepacks"));
                }

                RestoreFromBackup(
                    root,
                    backup,
                    optionsExisted,
                    launcherProfilesExisted);

                foreach (var created in SnapshotFabricVersionDirectories(root)
                             .Except(existingFabricVersions, StringComparer.OrdinalIgnoreCase))
                {
                    TryDeleteDirectory(created);
                }
            }
            catch { }

            throw;
        }
    }

    public void RestoreOriginal(
        string minecraftRoot,
        IProgress<string>? progress = null)
    {
        var root = Path.GetFullPath(minecraftRoot);
        var markerPath = Path.Combine(root, ".dlss-nr-manager-oneclick.json");

        if (!File.Exists(markerPath))
        {
            throw new InvalidOperationException(
                "No one-click Minecraft DLSS / RTX installation is tracked for this instance.");
        }

        var marker = JsonSerializer.Deserialize<OneClickManifest>(
            File.ReadAllText(markerPath))
            ?? throw new InvalidDataException("Minecraft one-click restore manifest is invalid.");

        progress?.Report("Removing manager-installed Caustica RTX / Fabric API files…");

        try
        {
            _integration.UninstallManagedMinecraftRtx(root);
        }
        catch
        {
            // Continue restoration even if the managed component marker was
            // already removed manually.
        }

        var causticaNatives = Path.Combine(root, "caustica-streamline");
        TryDeleteDirectory(causticaNatives);

        foreach (var relative in marker.CreatedFabricVersionDirectories)
        {
            var path = SafePath(root, relative);
            if (path != null)
                TryDeleteDirectory(path);
        }

        var backup = SafePath(root, marker.BackupDirectory)
            ?? throw new InvalidDataException("Backup path is outside the Minecraft instance.");

        var componentBackup = SafePath(root, marker.ComponentBackupDirectory);

        progress?.Report("Restoring the original Minecraft options, launcher profile and renderer mods…");

        RestoreFile(
            Path.Combine(backup, "options.txt"),
            Path.Combine(root, "options.txt"),
            marker.OptionsExisted);

        RestoreFile(
            Path.Combine(backup, "launcher_profiles.json"),
            Path.Combine(root, "launcher_profiles.json"),
            marker.LauncherProfilesExisted);

        RestoreDirectoryContents(
            Path.Combine(backup, "disabled-mods"),
            Path.Combine(root, "mods"));

        RestoreDirectory(
            Path.Combine(backup, "caustica-streamline"),
            Path.Combine(root, "caustica-streamline"));

        RestoreMatchingConfig(root, backup);

        if (componentBackup != null && Directory.Exists(componentBackup))
        {
            RestoreTopLevelFiles(
                componentBackup,
                Path.Combine(root, "mods"));

            RestoreDirectoryContents(
                Path.Combine(componentBackup, "extra", "mods"),
                Path.Combine(root, "mods"));

            RestoreDirectoryContents(
                Path.Combine(componentBackup, "extra", "resourcepacks"),
                Path.Combine(root, "resourcepacks"));
        }

        TryDeleteFile(markerPath);

        progress?.Report("Minecraft instance restored to its pre-install state.");
    }

    private static List<string> DisableConflictingRendererMods(
        string root,
        string backup,
        IProgress<string>? progress)
    {
        var mods = Path.Combine(root, "mods");
        var disabledRoot = Path.Combine(backup, "disabled-mods");
        var moved = new List<string>();

        if (!Directory.Exists(mods))
            return moved;

        foreach (var file in Directory.EnumerateFiles(mods, "*.jar", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(file);
            if (!ConflictingRendererTokens.Any(
                    token => name.Contains(token, StringComparison.OrdinalIgnoreCase)))
                continue;

            Directory.CreateDirectory(disabledRoot);
            var destination = Path.Combine(disabledRoot, name);

            progress?.Report($"Backing up conflicting renderer mod: {name}");
            File.Move(file, destination, true);
            moved.Add(file);
        }

        return moved;
    }

    private static void SetPreferredGraphicsBackend(string root, string backend)
    {
        var path = Path.Combine(root, "options.txt");
        var lines = File.Exists(path)
            ? File.ReadAllLines(path).ToList()
            : new List<string>();

        var setting = $"preferredGraphicsBackend:\"{backend}\"";
        var index = lines.FindIndex(
            line => line.StartsWith(
                "preferredGraphicsBackend:",
                StringComparison.OrdinalIgnoreCase));

        if (index >= 0)
            lines[index] = setting;
        else
            lines.Add(setting);

        File.WriteAllLines(path, lines);
    }

    private static void PatchFabricLauncherProfile(
        string launcherProfilesPath,
        IProgress<string>? progress)
    {
        if (!File.Exists(launcherProfilesPath))
        {
            AppLogger.Warn(
                "launcher_profiles.json was not found; Fabric JVM arguments could not be patched automatically.");
            progress?.Report(
                "Warning: launcher_profiles.json was not found. Verify -Xss16m and --enable-native-access=ALL-UNNAMED manually in the launcher.");
            return;
        }

        try
        {
            var root = JsonNode.Parse(File.ReadAllText(launcherProfilesPath)) as JsonObject;
            var profiles = root?["profiles"] as JsonObject;

            if (root == null || profiles == null)
                return;

            var changed = false;

            foreach (var profile in profiles)
            {
                if (profile.Value is not JsonObject obj)
                    continue;

                var lastVersionId = obj["lastVersionId"]?.GetValue<string>() ?? "";
                if (!lastVersionId.Contains("fabric-loader", StringComparison.OrdinalIgnoreCase) ||
                    !lastVersionId.EndsWith(
                        $"-{MinecraftIntegrationService.MinecraftVersion}",
                        StringComparison.OrdinalIgnoreCase))
                    continue;

                var javaArgs = obj["javaArgs"]?.GetValue<string>() ?? "";

                foreach (var required in new[]
                {
                    "-Xss16m",
                    "--enable-native-access=ALL-UNNAMED"
                })
                {
                    if (!javaArgs.Contains(required, StringComparison.OrdinalIgnoreCase))
                        javaArgs = string.IsNullOrWhiteSpace(javaArgs)
                            ? required
                            : javaArgs + " " + required;
                }

                obj["javaArgs"] = javaArgs;
                changed = true;
            }

            if (changed)
            {
                File.WriteAllText(
                    launcherProfilesPath,
                    root.ToJsonString(
                        new JsonSerializerOptions { WriteIndented = true }));

                progress?.Report(
                    "Fabric launcher profile updated with native-access and renderer-safe stack settings.");
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn(
                $"Fabric launcher profile could not be patched automatically: {ex.Message}");
            progress?.Report(
                "Warning: Fabric launcher JVM arguments could not be patched automatically. Verify -Xss16m and --enable-native-access=ALL-UNNAMED manually.");
        }
    }

    private static HashSet<string> SnapshotFabricVersionDirectories(string root)
    {
        var versions = Path.Combine(root, "versions");
        if (!Directory.Exists(versions))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            return Directory.EnumerateDirectories(
                    versions,
                    "fabric-loader-*",
                    SearchOption.TopDirectoryOnly)
                .Select(Path.GetFullPath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void BackupMatchingConfig(string root, string backup)
    {
        var config = Path.Combine(root, "config");
        if (!Directory.Exists(config))
            return;

        foreach (var entry in Directory.EnumerateFileSystemEntries(
                     config,
                     "*caustica*",
                     SearchOption.TopDirectoryOnly))
        {
            var destination = Path.Combine(
                backup,
                "config",
                Path.GetFileName(entry));

            if (Directory.Exists(entry))
                CopyDirectory(entry, destination);
            else
                BackupFile(entry, destination);
        }
    }

    private static void RestoreMatchingConfig(string root, string backup)
    {
        var config = Path.Combine(root, "config");

        if (Directory.Exists(config))
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(
                         config,
                         "*caustica*",
                         SearchOption.TopDirectoryOnly))
            {
                if (Directory.Exists(entry))
                    TryDeleteDirectory(entry);
                else
                    TryDeleteFile(entry);
            }
        }

        RestoreDirectoryContents(
            Path.Combine(backup, "config"),
            config);
    }

    private static void RestoreFromBackup(
        string root,
        string backup,
        bool optionsExisted,
        bool launcherProfilesExisted)
    {
        RestoreFile(
            Path.Combine(backup, "options.txt"),
            Path.Combine(root, "options.txt"),
            optionsExisted);

        RestoreFile(
            Path.Combine(backup, "launcher_profiles.json"),
            Path.Combine(root, "launcher_profiles.json"),
            launcherProfilesExisted);

        RestoreDirectoryContents(
            Path.Combine(backup, "disabled-mods"),
            Path.Combine(root, "mods"));

        RestoreDirectory(
            Path.Combine(backup, "caustica-streamline"),
            Path.Combine(root, "caustica-streamline"));

        RestoreMatchingConfig(root, backup);
    }

    private static void BackupFile(string source, string destination)
    {
        if (!File.Exists(source))
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, true);
    }

    private static void BackupDirectoryIfExists(string source, string destination)
    {
        if (Directory.Exists(source))
            CopyDirectory(source, destination);
    }

    private static void RestoreFile(
        string backup,
        string destination,
        bool originallyExisted)
    {
        if (File.Exists(backup))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(backup, destination, true);
        }
        else if (!originallyExisted)
        {
            TryDeleteFile(destination);
        }
    }

    private static void RestoreDirectory(string backup, string destination)
    {
        if (!Directory.Exists(backup))
            return;

        TryDeleteDirectory(destination);
        CopyDirectory(backup, destination);
    }

    private static void RestoreTopLevelFiles(
        string source,
        string destination)
    {
        if (!Directory.Exists(source))
            return;

        Directory.CreateDirectory(destination);

        foreach (var file in Directory.EnumerateFiles(
                     source,
                     "*",
                     SearchOption.TopDirectoryOnly))
        {
            var target = Path.Combine(
                destination,
                Path.GetFileName(file));
            File.Copy(file, target, true);
        }
    }

    private static void RestoreDirectoryContents(string source, string destination)
    {
        if (!Directory.Exists(source))
            return;

        Directory.CreateDirectory(destination);

        foreach (var file in Directory.EnumerateFiles(
                     source,
                     "*",
                     SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.EnumerateFiles(
                     source,
                     "*",
                     SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }
    }

    private static string? SafePath(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative))
            return null;

        var normalizedRoot =
            Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        var full = Path.GetFullPath(Path.Combine(root, relative));

        return full.StartsWith(
            normalizedRoot,
            StringComparison.OrdinalIgnoreCase)
            ? full
            : null;
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { }
    }

    private sealed record OneClickManifest(
        DateTimeOffset InstalledAt,
        bool FabricWasPresent,
        bool OptionsExisted,
        bool LauncherProfilesExisted,
        string BackupDirectory,
        string ComponentBackupDirectory,
        List<string> DisabledRendererMods,
        List<string> CreatedFabricVersionDirectories);
}
