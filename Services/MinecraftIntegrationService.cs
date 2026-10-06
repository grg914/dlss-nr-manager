using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;

namespace DlssNrManager.Services;

public sealed record MinecraftInstallCandidate(
    string Name,
    string RootDirectory,
    string Source,
    bool HasModsDirectory,
    bool FabricDetected,
    string? DetectedMinecraftVersion = null,
    bool HasTargetMinecraftVersion = false)
{
    public string DisplayName =>
        string.IsNullOrWhiteSpace(DetectedMinecraftVersion)
            ? Name
            : HasTargetMinecraftVersion
                ? $"{Name} — Minecraft {DetectedMinecraftVersion} (target)"
                : $"{Name} — Minecraft {DetectedMinecraftVersion}";
}

public sealed record MinecraftComponentResult(
    string Component,
    string Version,
    string InstalledPath,
    string SourceRepository);

public sealed record MinecraftSetupResult(
    MinecraftInstallCandidate Instance,
    IReadOnlyList<MinecraftComponentResult> Components,
    string BackupDirectory);

public sealed class MinecraftIntegrationService
{
    public const string MinecraftVersion = "26.2";
    public const string MinimumFabricLoader = "0.19.3";

    private const string FabricApiRepo = "FabricMC/fabric-api";
    private const string FabricInstallerMavenBase =
        "https://maven.fabricmc.net/net/fabricmc/fabric-installer";
    private const string CausticaRtxRepo = "AriesAlex/Caustica-RTX";

    private readonly HttpClient _http = new();

    public MinecraftIntegrationService()
    {
        _http.Timeout = TimeSpan.FromMinutes(10);
        _http.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("DlssNrManager", "1.2"));
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public IReadOnlyList<MinecraftInstallCandidate> DetectInstances()
    {
        var result = new List<MinecraftInstallCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string name, string root, string source)
        {
            if (string.IsNullOrWhiteSpace(root))
                return;

            try { root = Path.GetFullPath(root); }
            catch { return; }

            if (!Directory.Exists(root) || !seen.Add(root))
                return;

            var versions = DetectMinecraftVersions(root);
            var hasTarget = versions.Contains(
                MinecraftVersion,
                StringComparer.OrdinalIgnoreCase);

            var detectedVersion = hasTarget
                ? MinecraftVersion
                : SelectBestMinecraftVersion(versions);

            result.Add(new MinecraftInstallCandidate(
                name,
                root,
                source,
                Directory.Exists(Path.Combine(root, "mods")),
                DetectFabric(root),
                detectedVersion,
                hasTarget));
        }

        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        Add("Minecraft Launcher", Path.Combine(roaming, ".minecraft"), "Vanilla launcher");

        AddChildren(Path.Combine(roaming, "PrismLauncher", "instances"), "Prism Launcher", Add);
        AddChildren(Path.Combine(roaming, "com.modrinth.theseus", "profiles"), "Modrinth App", Add);

        var curse = Path.Combine(roaming, "CurseForge", "minecraft", "Instances");
        AddChildren(curse, "CurseForge", Add);

        var gd = Path.Combine(roaming, "gdlauncher_carbon", "data", "instances");
        AddChildren(gd, "GDLauncher", Add);

        // Some Microsoft Store launcher installations keep data in Packages.
        try
        {
            foreach (var package in Directory.EnumerateDirectories(Path.Combine(local, "Packages"), "*Minecraft*", SearchOption.TopDirectoryOnly))
            {
                var candidate = Path.Combine(package, "LocalCache", "Roaming", ".minecraft");
                Add(Path.GetFileName(package), candidate, "Microsoft Store");
            }
        }
        catch { }

        return result
            .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public MinecraftInstallCandidate CreateManualCandidate(string root)
    {
        var full = Path.GetFullPath(root);
        ValidateInstance(full);

        var versions = DetectMinecraftVersions(full);
        var hasTarget = versions.Contains(
            MinecraftVersion,
            StringComparer.OrdinalIgnoreCase);

        return new MinecraftInstallCandidate(
            Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar)),
            full,
            "Manual",
            Directory.Exists(Path.Combine(full, "mods")),
            DetectFabric(full),
            hasTarget
                ? MinecraftVersion
                : SelectBestMinecraftVersion(versions),
            hasTarget);
    }

    public static IReadOnlyList<string> DetectMinecraftVersions(
        string root)
    {
        var versions = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        try
        {
            var versionsRoot = Path.Combine(root, "versions");
            if (Directory.Exists(versionsRoot))
            {
                foreach (var directory in Directory.EnumerateDirectories(
                             versionsRoot,
                             "*",
                             SearchOption.TopDirectoryOnly))
                {
                    var name = Path.GetFileName(directory);

                    if (name.Equals(
                            MinecraftVersion,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        versions.Add(MinecraftVersion);
                        continue;
                    }

                    var fabricMatch = System.Text.RegularExpressions.Regex.Match(
                        name,
                        @"fabric-loader-[^-]+-(?<mc>\d+\.\d+(?:\.\d+)?)$",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase |
                        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

                    if (fabricMatch.Success)
                    {
                        versions.Add(
                            fabricMatch.Groups["mc"].Value);
                        continue;
                    }

                    if (System.Text.RegularExpressions.Regex.IsMatch(
                            name,
                            @"^\d+\.\d+(?:\.\d+)?(?:[-+].*)?$",
                            System.Text.RegularExpressions.RegexOptions.CultureInvariant))
                    {
                        var match =
                            System.Text.RegularExpressions.Regex.Match(
                                name,
                                @"^\d+\.\d+(?:\.\d+)?",
                                System.Text.RegularExpressions.RegexOptions.CultureInvariant);

                        if (match.Success)
                            versions.Add(match.Value);
                    }
                }
            }

            foreach (var metadataRoot in new[]
            {
                root,
                Directory.GetParent(root)?.FullName
            }.Where(path => !string.IsNullOrWhiteSpace(path)))
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
                    var path = Path.Combine(metadataRoot!, metadata);
                    if (!File.Exists(path))
                        continue;

                    var text = File.ReadAllText(path);

                    // Prefer the exact target when it is referenced anywhere
                    // in launcher metadata.
                    if (System.Text.RegularExpressions.Regex.IsMatch(
                            text,
                            $@"(?<!\d){System.Text.RegularExpressions.Regex.Escape(MinecraftVersion)}(?!\d)",
                            System.Text.RegularExpressions.RegexOptions.CultureInvariant))
                    {
                        versions.Add(MinecraftVersion);
                    }

                    // MultiMC/Prism mmc-pack.json exposes the Minecraft
                    // component as uid net.minecraft with its own version.
                    var minecraftComponent =
                        System.Text.RegularExpressions.Regex.Match(
                            text,
                            @"""uid""\s*:\s*""net\.minecraft""[\s\S]{0,300}?""version""\s*:\s*""(?<version>\d+\.\d+(?:\.\d+)?)""",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase |
                            System.Text.RegularExpressions.RegexOptions.CultureInvariant);

                    if (minecraftComponent.Success)
                    {
                        versions.Add(
                            minecraftComponent.Groups["version"].Value);
                    }
                }
            }
        }
        catch { }

        return versions
            .OrderByDescending(
                version => version.Equals(
                    MinecraftVersion,
                    StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(
                version => ParseLooseVersion(version))
            .ThenByDescending(
                version => version,
                StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? SelectBestMinecraftVersion(
        IReadOnlyList<string> versions)
        => versions
            .OrderByDescending(
                version => version.Equals(
                    MinecraftVersion,
                    StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(ParseLooseVersion)
            .ThenByDescending(
                version => version,
                StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

    private static Version ParseLooseVersion(string value)
    {
        var numeric = value.Split(
            '-',
            '+',
            StringSplitOptions.RemoveEmptyEntries)[0];

        if (Version.TryParse(numeric, out var version))
            return version;

        return new Version(0, 0);
    }

    public Task<string> EnsureJava25RuntimeAsync(
        string minecraftRoot,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
        => EnsureJava25Async(
            minecraftRoot,
            progress,
            cancellationToken);

    public async Task LaunchFabricInstallerAsync(
        string minecraftRoot,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateInstance(minecraftRoot);

        var java = await EnsureJava25Async(
            minecraftRoot,
            progress,
            cancellationToken);

        var installerPackage = await GetLatestFabricInstallerAsync(
            progress,
            cancellationToken);

        var loaderVersion = await GetRecommendedFabricLoaderAsync(
            progress,
            cancellationToken);

        var installerRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DlssNrManager",
            "minecraft",
            "fabric-installer");

        Directory.CreateDirectory(installerRoot);

        var installer = Path.Combine(
            installerRoot,
            installerPackage.FileName);

        await DownloadAndVerifyAsync(
            installerPackage.DownloadUrl,
            installer,
            installerPackage.Sha256,
            progress,
            $"Fabric Installer {installerPackage.Version}",
            cancellationToken);

        progress?.Report(
            $"Installing Fabric Loader {loaderVersion} for Minecraft {MinecraftVersion} using Fabric Installer {installerPackage.Version}…");

        var psi = new ProcessStartInfo(java)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var arg in new[]
        {
            "-jar", installer,
            "client",
            "-dir", minecraftRoot,
            "-mcversion", MinecraftVersion,
            "-loader", loaderVersion
        })
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Could not start Fabric Installer.");

        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                "Fabric Installer failed.\n" + Tail(await stderr, 3000));

        progress?.Report("Fabric Loader installation finished. Restart Minecraft Launcher before continuing.");
    }

    public async Task<MinecraftSetupResult> InstallMinecraftRtxAsync(
        MinecraftInstallCandidate instance,
        bool installFabricApi,
        bool allowPrereleaseCaustica,
        bool installRtxPerformancePack,
        bool installLabPbrResourcePack,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateInstance(instance.RootDirectory);

        var fabric = MinecraftPreflightService.DetectFabricLoader(instance.RootDirectory);
        if (!fabric.ForMinecraft262 ||
            fabric.Version == null ||
            fabric.Version < Version.Parse(MinimumFabricLoader))
        {
            throw new InvalidOperationException(
                $"Fabric Loader {MinimumFabricLoader}+ for Minecraft {MinecraftVersion} was not detected.");
        }

        var mods = Path.Combine(instance.RootDirectory, "mods");
        Directory.CreateDirectory(mods);

        var backup = Path.Combine(
            instance.RootDirectory,
            ".dlss-nr-manager-backups",
            "minecraft",
            DateTimeOffset.Now.ToString("yyyyMMdd-HHmmssfff"));
        Directory.CreateDirectory(backup);

        var installed = new List<MinecraftComponentResult>();
        var managedDestinations = new List<string>();

        try
        {
            if (installFabricApi)
            {
                progress?.Report(
                    $"Finding the latest Fabric API build for Minecraft {MinecraftVersion}…");

                var fabricApi = await InstallModrinthProjectAsync(
                    instance.RootDirectory,
                    mods,
                    backup,
                    new ModrinthProject(
                        "Fabric API",
                        "fabric-api",
                        "fabric-api"),
                    loader: "fabric",
                    progress,
                    cancellationToken,
                    requireReleaseBuild: true);

                managedDestinations.Add(fabricApi.InstalledPath);
                installed.Add(fabricApi);
            }

            progress?.Report("Finding latest compatible Caustica RTX release…");

            var caustica = await FindReleaseAsync(
                CausticaRtxRepo,
                r => ReleaseTargetsMinecraftVersion(
                    r,
                    MinecraftVersion),
                allowPrereleaseCaustica,
                cancellationToken);

            var causticaAsset = SelectAsset(
                caustica,
                name => name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
                        && name.Contains("caustica", StringComparison.OrdinalIgnoreCase)
                        && !ContainsAny(name, "sources", "dev", "javadoc"));

            await BackupMatchingAsync(mods, backup, "caustica", cancellationToken);

            var causticaDestination = Path.Combine(mods, causticaAsset.Name);
            managedDestinations.Add(causticaDestination);
            await DownloadAssetAsync(
                causticaAsset,
                causticaDestination,
                progress,
                cancellationToken);

            installed.Add(new MinecraftComponentResult(
                "Caustica RTX", caustica.Tag, causticaDestination, CausticaRtxRepo));

            if (installRtxPerformancePack)
            {
                progress?.Report("Installing RTX-safe Minecraft performance mods…");

                foreach (var project in new[]
                {
                    new ModrinthProject("Lithium", "lithium", "lithium"),
                    new ModrinthProject("FerriteCore", "ferrite-core", "ferritecore"),
                    new ModrinthProject("Krypton", "krypton", "krypton"),
                    new ModrinthProject("Dynamic FPS", "dynamic-fps", "dynamic-fps")
                })
                {
                    try
                    {
                        var component = await InstallModrinthProjectAsync(
                            instance.RootDirectory,
                            mods,
                            backup,
                            project,
                            loader: "fabric",
                            progress,
                            cancellationToken,
                            requireReleaseBuild: true);

                        managedDestinations.Add(component.InstalledPath);
                        installed.Add(component);
                    }
                    catch (Exception ex)
                    {
                        RestoreMatchingBackupFiles(
                            backup,
                            instance.RootDirectory,
                            mods,
                            project.FileToken);

                        AppLogger.Warn(
                            $"Optional Minecraft mod {project.Name} was skipped: {ex.Message}");
                        progress?.Report(
                            $"Optional {project.Name} skipped: {ex.Message}");
                    }
                }
            }

            if (installLabPbrResourcePack)
            {
                progress?.Report("Installing SPBR LabPBR resource pack for Caustica RTX…");

                var resourcePacks = Path.Combine(
                    instance.RootDirectory,
                    "resourcepacks");
                Directory.CreateDirectory(resourcePacks);

                var spbrProject =
                    new ModrinthProject(
                        "SPBR LabPBR",
                        "spbr",
                        "spbr");

                try
                {
                    var spbr = await InstallModrinthProjectAsync(
                        instance.RootDirectory,
                        resourcePacks,
                        backup,
                        spbrProject,
                        loader: null,
                        progress,
                        cancellationToken,
                        requireReleaseBuild: true);

                    managedDestinations.Add(spbr.InstalledPath);
                    installed.Add(spbr);
                }
                catch (Exception ex)
                {
                    RestoreMatchingBackupFiles(
                        backup,
                        instance.RootDirectory,
                        resourcePacks,
                        spbrProject.FileToken);

                    AppLogger.Warn(
                        $"Optional Minecraft resource pack SPBR was skipped: {ex.Message}");
                    progress?.Report(
                        $"Optional SPBR LabPBR skipped: {ex.Message}");
                }
            }

            WriteManagedManifest(instance.RootDirectory, installed, backup);

            progress?.Report(
                "Minecraft RTX components installed. Start the Fabric profile and enable the renderer's Vulkan/RTX option if required.");

            return new MinecraftSetupResult(instance, installed, backup);
        }
        catch
        {
            foreach (var destination in managedDestinations)
                TryDelete(destination);

            RestoreBackupJars(backup, mods);
            RestoreBackupDirectory(
                Path.Combine(backup, "extra", "mods"),
                mods);
            RestoreBackupDirectory(
                Path.Combine(backup, "extra", "resourcepacks"),
                Path.Combine(instance.RootDirectory, "resourcepacks"));

            TryDelete(Path.Combine(instance.RootDirectory, ".dlss-nr-manager-minecraft.json"));
            throw;
        }
    }

    public void UninstallManagedMinecraftRtx(string minecraftRoot)
    {
        ValidateInstance(minecraftRoot);

        var markerPath = Path.Combine(minecraftRoot, ".dlss-nr-manager-minecraft.json");
        if (!File.Exists(markerPath))
            throw new InvalidOperationException("No DLSS NR Manager Minecraft install marker was found.");

        var marker = JsonSerializer.Deserialize<ManagedManifest>(File.ReadAllText(markerPath))
            ?? throw new InvalidDataException("Minecraft install marker is invalid.");

        var root = Path.GetFullPath(minecraftRoot)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        foreach (var relative in marker.ManagedFiles)
        {
            var path = Path.GetFullPath(Path.Combine(minecraftRoot, relative));
            if (path.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(path))
                File.Delete(path);
        }

        File.Delete(markerPath);
    }

    public void OpenMinecraftLauncher()
    {
        foreach (var candidate in new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Minecraft Launcher", "MinecraftLauncher.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Minecraft Launcher", "MinecraftLauncher.exe")
        })
        {
            if (File.Exists(candidate))
            {
                Process.Start(new ProcessStartInfo(candidate) { UseShellExecute = true });
                return;
            }
        }

        Process.Start(new ProcessStartInfo(
            "https://www.minecraft.net/download")
        {
            UseShellExecute = true
        });
    }

    private async Task<GitHubRelease> FindReleaseAsync(
        string repository,
        Func<GitHubRelease, bool> predicate,
        bool includePrerelease,
        CancellationToken cancellationToken)
    {
        using var json = await GetJsonAsync(
            $"https://api.github.com/repos/{repository}/releases?per_page=40",
            cancellationToken);

        foreach (var item in json.RootElement.EnumerateArray())
        {
            var release = ParseRelease(item);
            if (release.Draft || (!includePrerelease && release.Prerelease))
                continue;

            if (predicate(release))
                return release;
        }

        throw new InvalidOperationException($"No compatible release was found in {repository}.");
    }

    private static GitHubAsset SelectAsset(
        GitHubRelease release,
        Func<string, bool> predicate)
        => release.Assets.FirstOrDefault(x => predicate(x.Name))
           ?? throw new InvalidOperationException(
               $"Release {release.Tag} has no compatible asset.");

    private async Task DownloadAssetAsync(
        GitHubAsset asset,
        string destination,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temp = destination + ".download";
        progress?.Report($"Downloading {asset.Name}…");

        using var response = await _http.GetAsync(
            asset.Url,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var output = new FileStream(
                         temp,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         128 * 1024,
                         useAsync: true))
        {
            await input.CopyToAsync(output, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(asset.Sha256))
        {
            var actual = await Sha256Async(temp, cancellationToken);
            if (!actual.Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(temp);
                throw new InvalidDataException(
                    $"SHA-256 mismatch for {asset.Name}. Expected {asset.Sha256}, got {actual}.");
            }
        }

        File.Move(temp, destination, true);
    }

    private async Task<MinecraftComponentResult> InstallModrinthProjectAsync(
        string minecraftRoot,
        string destinationDirectory,
        string backup,
        ModrinthProject project,
        string? loader,
        IProgress<string>? progress,
        CancellationToken cancellationToken,
        bool requireReleaseBuild = false)
    {
        var versionsUrl =
            $"https://api.modrinth.com/v2/project/{project.Slug}/version" +
            $"?game_versions={Uri.EscapeDataString("[\"" + MinecraftVersion + "\"]")}" +
            (string.IsNullOrWhiteSpace(loader)
                ? ""
                : $"&loaders={Uri.EscapeDataString("[\"" + loader + "\"]")}");

        using var versions = await GetJsonAsync(
            versionsUrl,
            cancellationToken);

        if (versions.RootElement.ValueKind != JsonValueKind.Array ||
            versions.RootElement.GetArrayLength() == 0)
        {
            throw new InvalidOperationException(
                $"No Modrinth build of {project.Name} was found for Minecraft {MinecraftVersion}" +
                (string.IsNullOrWhiteSpace(loader) ? "." : $" / {loader}."));
        }

        JsonElement? selectedVersion = null;
        JsonElement? selectedFile = null;

        var versionCandidates = versions.RootElement
            .EnumerateArray()
            .Where(version =>
                !requireReleaseBuild ||
                !version.TryGetProperty("version_type", out var typeElement) ||
                string.Equals(
                    typeElement.GetString(),
                    "release",
                    StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (versionCandidates.Count == 0 && requireReleaseBuild)
        {
            throw new InvalidOperationException(
                $"No stable Modrinth release of {project.Name} was found for Minecraft {MinecraftVersion}" +
                (string.IsNullOrWhiteSpace(loader) ? "." : $" / {loader}."));
        }

        foreach (var version in versionCandidates)
        {
            if (!version.TryGetProperty("files", out var files) ||
                files.ValueKind != JsonValueKind.Array)
                continue;

            var candidates = files.EnumerateArray()
                .Where(file =>
                {
                    var filename = file.TryGetProperty("filename", out var filenameElement)
                        ? filenameElement.GetString() ?? ""
                        : "";

                    return !string.IsNullOrWhiteSpace(filename) &&
                           !filename.Contains("sources", StringComparison.OrdinalIgnoreCase);
                })
                .ToList();

            var primary = candidates.FirstOrDefault(file =>
                file.TryGetProperty("primary", out var primaryElement) &&
                primaryElement.ValueKind == JsonValueKind.True);

            var chosen = primary.ValueKind != JsonValueKind.Undefined
                ? primary
                : candidates.FirstOrDefault();

            if (chosen.ValueKind == JsonValueKind.Undefined)
                continue;

            selectedVersion = version;
            selectedFile = chosen;
            break;
        }

        if (selectedVersion == null || selectedFile == null)
        {
            throw new InvalidOperationException(
                $"Modrinth returned no downloadable file for {project.Name}.");
        }

        var file = selectedFile.Value;
        var filename = file.GetProperty("filename").GetString()
            ?? throw new InvalidDataException(
                $"Modrinth file name is missing for {project.Name}.");

        var url = file.GetProperty("url").GetString()
            ?? throw new InvalidDataException(
                $"Modrinth download URL is missing for {project.Name}.");

        var versionNumber = selectedVersion.Value.TryGetProperty(
                "version_number",
                out var versionElement)
            ? versionElement.GetString() ?? "unknown"
            : "unknown";

        string? sha512 = null;
        if (file.TryGetProperty("hashes", out var hashes) &&
            hashes.TryGetProperty("sha512", out var sha512Element))
        {
            sha512 = sha512Element.GetString();
        }

        var destination = Path.Combine(destinationDirectory, filename);
        var temp = destination + ".download";
        progress?.Report($"Downloading {project.Name} {versionNumber}…");

        using (var response = await _http.GetAsync(
                   url,
                   HttpCompletionOption.ResponseHeadersRead,
                   cancellationToken))
        {
            response.EnsureSuccessStatusCode();

            await using var input =
                await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = new FileStream(
                temp,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                128 * 1024,
                useAsync: true);

            await input.CopyToAsync(output, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(sha512))
        {
            await using var stream = File.OpenRead(temp);
            var actual = Convert.ToHexString(
                await SHA512.HashDataAsync(stream, cancellationToken));

            if (!actual.Equals(sha512, StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(temp);
                throw new InvalidDataException(
                    $"SHA-512 mismatch for {project.Name}. " +
                    $"Expected {sha512}, got {actual}.");
            }
        }

        await BackupMatchingFileAsync(
            minecraftRoot,
            destinationDirectory,
            backup,
            project.FileToken,
            cancellationToken);

        File.Move(temp, destination, true);

        return new MinecraftComponentResult(
            project.Name,
            versionNumber,
            destination,
            $"Modrinth:{project.Slug}");
    }

    private static async Task BackupMatchingFileAsync(
        string minecraftRoot,
        string directory,
        string backup,
        string token,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(directory))
            return;

        foreach (var file in Directory.EnumerateFiles(
                     directory,
                     "*",
                     SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(file);
            if (!name.Contains(token, StringComparison.OrdinalIgnoreCase))
                continue;

            var relativeDirectory = Path.GetRelativePath(
                minecraftRoot,
                directory);

            var destination = Path.Combine(
                backup,
                "extra",
                relativeDirectory,
                name);

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

            await using var input = File.OpenRead(file);
            await using var output = File.Create(destination);
            await input.CopyToAsync(output, cancellationToken);

            File.Delete(file);
        }
    }

    private static void RestoreMatchingBackupFiles(
        string backup,
        string minecraftRoot,
        string destinationDirectory,
        string token)
    {
        var relativeDirectory = Path.GetRelativePath(
            minecraftRoot,
            destinationDirectory);

        var source = Path.Combine(
            backup,
            "extra",
            relativeDirectory);

        if (!Directory.Exists(source))
            return;

        Directory.CreateDirectory(destinationDirectory);

        foreach (var file in Directory.EnumerateFiles(
                     source,
                     "*",
                     SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(file);
            if (!name.Contains(
                    token,
                    StringComparison.OrdinalIgnoreCase))
                continue;

            File.Copy(
                file,
                Path.Combine(destinationDirectory, name),
                true);
        }
    }

    private async Task<string> GetRecommendedFabricLoaderAsync(
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(
            $"Checking Fabric Meta for the recommended loader for Minecraft {MinecraftVersion}…");

        try
        {
            using var json = await GetJsonAsync(
                $"https://meta.fabricmc.net/v2/versions/loader/{MinecraftVersion}",
                cancellationToken);

            if (json.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in json.RootElement.EnumerateArray())
                {
                    if (!entry.TryGetProperty("loader", out var loader) ||
                        !loader.TryGetProperty("version", out var versionElement))
                        continue;

                    var version = versionElement.GetString();
                    var stable =
                        loader.TryGetProperty("stable", out var stableElement) &&
                        stableElement.ValueKind == JsonValueKind.True;

                    if (!stable ||
                        string.IsNullOrWhiteSpace(version) ||
                        !Version.TryParse(version, out var parsed) ||
                        parsed < Version.Parse(MinimumFabricLoader))
                        continue;

                    progress?.Report(
                        $"Fabric Loader {version} selected for Minecraft {MinecraftVersion}.");
                    return version;
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn(
                $"Fabric Meta loader lookup failed; falling back to {MinimumFabricLoader}: {ex.Message}");
        }

        progress?.Report(
            $"Fabric Meta lookup unavailable; using minimum compatible Fabric Loader {MinimumFabricLoader}.");
        return MinimumFabricLoader;
    }

    private async Task<FabricInstallerPackage> GetLatestFabricInstallerAsync(
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report("Checking the official Fabric Maven for the latest installer…");

        var metadataUrl =
            $"{FabricInstallerMavenBase}/maven-metadata.xml";

        using var response = await _http.GetAsync(
            metadataUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var metadataStream =
            await response.Content.ReadAsStreamAsync(cancellationToken);

        var metadata = XDocument.Load(metadataStream);

        var version =
            metadata.Root?
                .Element("versioning")?
                .Element("release")?
                .Value?
                .Trim();

        if (string.IsNullOrWhiteSpace(version))
        {
            version =
                metadata.Root?
                    .Element("versioning")?
                    .Element("latest")?
                    .Value?
                    .Trim();
        }

        if (string.IsNullOrWhiteSpace(version))
        {
            version =
                metadata.Root?
                    .Element("versioning")?
                    .Element("versions")?
                    .Elements("version")
                    .Select(element => element.Value.Trim())
                    .Where(value => Version.TryParse(value, out _))
                    .OrderByDescending(value => Version.Parse(value))
                    .FirstOrDefault();
        }

        if (string.IsNullOrWhiteSpace(version))
        {
            throw new InvalidOperationException(
                "The official Fabric Maven metadata did not contain an installer version.");
        }

        var fileName = $"fabric-installer-{version}.jar";
        var downloadUrl =
            $"{FabricInstallerMavenBase}/{version}/{fileName}";
        var sha256Url = downloadUrl + ".sha256";

        string? sha256 = null;

        try
        {
            using var hashResponse = await _http.GetAsync(
                sha256Url,
                cancellationToken);

            if (hashResponse.IsSuccessStatusCode)
            {
                sha256 = (
                    await hashResponse.Content.ReadAsStringAsync(
                        cancellationToken))
                    .Trim()
                    .Split(
                        new[] { ' ', '\t', '\r', '\n' },
                        StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault();

                if (string.IsNullOrWhiteSpace(sha256) ||
                    sha256.Length != 64 ||
                    !sha256.All(Uri.IsHexDigit))
                {
                    sha256 = null;
                }
            }
        }
        catch
        {
            // Hash verification remains best-effort only if the sidecar
            // cannot be fetched. The HTTPS Maven origin is still trusted.
        }

        return new FabricInstallerPackage(
            version,
            fileName,
            downloadUrl,
            sha256);
    }

    private async Task DownloadAndVerifyAsync(
        string url,
        string destination,
        string? expectedSha256,
        IProgress<string>? progress,
        string componentName,
        CancellationToken cancellationToken)
    {
        var temp = destination + ".download";

        try
        {
            progress?.Report($"Downloading {componentName}…");

            using (var response = await _http.GetAsync(
                       url,
                       HttpCompletionOption.ResponseHeadersRead,
                       cancellationToken))
            {
                response.EnsureSuccessStatusCode();

                await using var input =
                    await response.Content.ReadAsStreamAsync(
                        cancellationToken);
                await using var output = new FileStream(
                    temp,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    128 * 1024,
                    useAsync: true);

                await input.CopyToAsync(
                    output,
                    cancellationToken);
            }

            if (new FileInfo(temp).Length < 100 * 1024)
            {
                throw new InvalidDataException(
                    $"{componentName} download is unexpectedly small.");
            }

            if (!string.IsNullOrWhiteSpace(expectedSha256))
            {
                await using var stream = File.OpenRead(temp);
                var actual = Convert.ToHexString(
                    await SHA256.HashDataAsync(
                        stream,
                        cancellationToken));

                if (!actual.Equals(
                        expectedSha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"{componentName} SHA-256 mismatch. " +
                        $"Expected {expectedSha256}, got {actual}.");
                }

                progress?.Report(
                    $"{componentName} SHA-256 verified.");
            }

            File.Move(
                temp,
                destination,
                overwrite: true);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    private async Task<JsonDocument> GetJsonAsync(
        string url,
        CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(
            url,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static GitHubRelease ParseRelease(JsonElement element)
    {
        var tag = element.TryGetProperty("tag_name", out var tagElement)
            ? tagElement.GetString() ?? "unknown"
            : "unknown";

        var name = element.TryGetProperty("name", out var nameElement)
            ? nameElement.GetString() ?? tag
            : tag;

        var draft = element.TryGetProperty("draft", out var draftElement)
                    && draftElement.GetBoolean();

        var prerelease = element.TryGetProperty("prerelease", out var prereleaseElement)
                         && prereleaseElement.GetBoolean();

        var assets = new List<GitHubAsset>();

        if (element.TryGetProperty("assets", out var assetsElement))
        {
            foreach (var asset in assetsElement.EnumerateArray())
            {
                var assetName = asset.GetProperty("name").GetString() ?? "";
                var url = asset.GetProperty("browser_download_url").GetString() ?? "";
                string? sha = null;

                if (asset.TryGetProperty("digest", out var digestElement))
                {
                    var digest = digestElement.GetString();
                    if (!string.IsNullOrWhiteSpace(digest) &&
                        digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                        sha = digest["sha256:".Length..];
                }

                if (!string.IsNullOrWhiteSpace(assetName) && !string.IsNullOrWhiteSpace(url))
                    assets.Add(new GitHubAsset(assetName, url, sha));
            }
        }

        var body = element.TryGetProperty("body", out var bodyElement)
            ? bodyElement.GetString() ?? ""
            : "";

        return new GitHubRelease(
            tag,
            name,
            body,
            draft,
            prerelease,
            assets);
    }

    private static bool ReleaseTargetsMinecraftVersion(
        GitHubRelease release,
        string minecraftVersion)
    {
        var escaped = System.Text.RegularExpressions.Regex.Escape(
            minecraftVersion);

        return System.Text.RegularExpressions.Regex.IsMatch(
                   release.Tag,
                   $@"(?<!\d){escaped}(?!\d)",
                   System.Text.RegularExpressions.RegexOptions.IgnoreCase |
                   System.Text.RegularExpressions.RegexOptions.CultureInvariant)
               || System.Text.RegularExpressions.Regex.IsMatch(
                   release.Name,
                   $@"(?<!\d){escaped}(?!\d)",
                   System.Text.RegularExpressions.RegexOptions.IgnoreCase |
                   System.Text.RegularExpressions.RegexOptions.CultureInvariant)
               || System.Text.RegularExpressions.Regex.IsMatch(
                   release.Body,
                   $@"(?<!\d){escaped}(?!\d)",
                   System.Text.RegularExpressions.RegexOptions.IgnoreCase |
                   System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    }

    private static bool DetectFabric(string root)
    {
        var fabric = MinecraftPreflightService.DetectFabricLoader(root);
        return fabric.ForMinecraft262 &&
               fabric.Version != null &&
               fabric.Version >= Version.Parse(MinimumFabricLoader);
    }

    private static void AddChildren(
        string parent,
        string source,
        Action<string, string, string> add)
    {
        if (!Directory.Exists(parent))
            return;

        try
        {
            foreach (var child in Directory.EnumerateDirectories(parent))
            {
                var candidate = Directory.Exists(Path.Combine(child, ".minecraft"))
                    ? Path.Combine(child, ".minecraft")
                    : child;

                add($"{source} — {Path.GetFileName(child)}", candidate, source);
            }
        }
        catch { }
    }

    private static async Task<string> EnsureJava25Async(
        string minecraftRoot,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var java = await FindJava25ExecutableAsync(
            minecraftRoot,
            cancellationToken);

        if (java != null)
            return java;

        var winget = FindOnPath("winget.exe");
        if (winget == null)
        {
            throw new InvalidOperationException(
                "Java 25 was not found and WinGet is unavailable. " +
                "Install an x64 Java 25 runtime (for example Eclipse Temurin 25), then retry.");
        }

        progress?.Report(
            "Java 25 is missing. Installing Eclipse Temurin 25 automatically with WinGet…");

        foreach (var packageId in new[]
        {
            "EclipseAdoptium.Temurin.25.JRE",
            "EclipseAdoptium.Temurin.25.JDK"
        })
        {
            if (await TryInstallWingetPackageAsync(
                    winget,
                    packageId,
                    cancellationToken))
            {
                java = await FindJava25ExecutableAsync(
                    minecraftRoot,
                    cancellationToken);

                if (java != null)
                {
                    progress?.Report("Java 25 installed and verified.");
                    return java;
                }
            }
        }

        throw new InvalidOperationException(
            "Java 25 could not be installed or verified automatically. " +
            "Install Eclipse Temurin 25 x64 manually, then retry.");
    }

    private static async Task<string?> FindJava25ExecutableAsync(
        string minecraftRoot,
        CancellationToken cancellationToken)
    {
        var candidates = new List<string>();

        void AddCandidate(string? path)
        {
            if (!string.IsNullOrWhiteSpace(path) &&
                File.Exists(path) &&
                !candidates.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                candidates.Add(path);
            }
        }

        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(javaHome))
        {
            AddCandidate(Path.Combine(javaHome, "bin", "java.exe"));
            AddCandidate(Path.Combine(javaHome, "bin", "javaw.exe"));
        }

        foreach (var pathRoot in (Environment.GetEnvironmentVariable("PATH") ?? "")
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            AddCandidate(Path.Combine(pathRoot, "java.exe"));
            AddCandidate(Path.Combine(pathRoot, "javaw.exe"));
        }

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
                {
                    AddCandidate(candidate);
                }
            }
            catch { }
        }

        foreach (var root in new[]
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
                var vendorRoot = Path.Combine(root, vendor);
                if (!Directory.Exists(vendorRoot))
                    continue;

                try
                {
                    foreach (var candidate in Directory.EnumerateFiles(
                                 vendorRoot,
                                 "java.exe",
                                 SearchOption.AllDirectories))
                    {
                        AddCandidate(candidate);
                    }
                }
                catch { }
            }
        }

        foreach (var candidate in candidates)
        {
            if (await IsJavaMajorVersionAsync(candidate, 25, cancellationToken))
                return candidate;
        }

        return null;
    }

    private static string? FindOnPath(string executable)
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "")
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Combine(directory, executable);
                if (File.Exists(candidate))
                    return candidate;
            }
            catch { }
        }

        var windowsApps = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft",
            "WindowsApps",
            executable);

        return File.Exists(windowsApps) ? windowsApps : null;
    }

    private static async Task<bool> TryInstallWingetPackageAsync(
        string winget,
        string packageId,
        CancellationToken cancellationToken)
    {
        try
        {
            var psi = new ProcessStartInfo(winget)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            foreach (var arg in new[]
            {
                "install",
                "--id", packageId,
                "--exact",
                "--source", "winget",
                "--accept-package-agreements",
                "--accept-source-agreements",
                "--silent",
                "--disable-interactivity"
            })
            {
                psi.ArgumentList.Add(arg);
            }

            using var process = Process.Start(psi);
            if (process == null)
                return false;

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            await process.WaitForExitAsync(cancellationToken);
            await stdoutTask;
            await stderrTask;

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> IsJavaMajorVersionAsync(
        string javaExecutable,
        int expectedMajor,
        CancellationToken cancellationToken)
    {
        try
        {
            var psi = new ProcessStartInfo(javaExecutable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            psi.ArgumentList.Add("-version");

            using var process = Process.Start(psi);
            if (process == null)
                return false;

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            await process.WaitForExitAsync(cancellationToken);

            var versionText = (await stdoutTask) + "\n" + (await stderrTask);
            if (process.ExitCode != 0)
                return false;

            var firstQuote = versionText.IndexOf('"');
            if (firstQuote < 0)
                return false;

            var secondQuote = versionText.IndexOf('"', firstQuote + 1);
            if (secondQuote <= firstQuote)
                return false;

            var version = versionText[(firstQuote + 1)..secondQuote];
            var firstPart = version.Split('.', '-', '+')[0];

            return int.TryParse(firstPart, out var major) &&
                   major == expectedMajor;
        }
        catch
        {
            return false;
        }
    }

    private static void RestoreBackupDirectory(
        string source,
        string destination)
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

            Directory.CreateDirectory(
                Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }
    }

    private static void RestoreBackupJars(
        string backup,
        string mods)
    {
        if (!Directory.Exists(backup))
            return;

        Directory.CreateDirectory(mods);

        foreach (var file in Directory.EnumerateFiles(
                     backup,
                     "*.jar",
                     SearchOption.TopDirectoryOnly))
        {
            var destination = Path.Combine(mods, Path.GetFileName(file));
            File.Copy(file, destination, true);
        }
    }

    private static async Task BackupMatchingAsync(
        string directory,
        string backup,
        string contains,
        CancellationToken cancellationToken)
    {
        foreach (var file in Directory.EnumerateFiles(directory, "*.jar", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(file);
            if (!name.Contains(contains, StringComparison.OrdinalIgnoreCase))
                continue;

            var destination = Path.Combine(backup, name);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

            await using var input = File.OpenRead(file);
            await using var output = File.Create(destination);
            await input.CopyToAsync(output, cancellationToken);

            File.Delete(file);
        }
    }

    private static void WriteManagedManifest(
        string root,
        IReadOnlyList<MinecraftComponentResult> components,
        string backup)
    {
        var manifest = new ManagedManifest(
            DateTimeOffset.UtcNow,
            components
                .Select(x => Path.GetRelativePath(root, x.InstalledPath))
                .ToList(),
            Path.GetRelativePath(root, backup));

        File.WriteAllText(
            Path.Combine(root, ".dlss-nr-manager-minecraft.json"),
            JsonSerializer.Serialize(
                manifest,
                new JsonSerializerOptions { WriteIndented = true }));
    }

    private static async Task<string> Sha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static void ValidateInstance(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            throw new DirectoryNotFoundException("Minecraft instance directory was not found.");

        if (File.Exists(Path.Combine(root, "launcher_profiles.json")) ||
            Directory.Exists(Path.Combine(root, "versions")) ||
            Directory.Exists(Path.Combine(root, "mods")))
            return;

        throw new InvalidOperationException(
            "The selected directory does not look like a Minecraft Java instance.");
    }

    private static bool ContainsAny(string value, params string[] terms)
        => terms.Any(x => value.Contains(x, StringComparison.OrdinalIgnoreCase));

    private static string Tail(string value, int max)
        => string.IsNullOrWhiteSpace(value)
            ? ""
            : value.Length <= max
                ? value.Trim()
                : value[^max..].Trim();

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { }
    }

    private sealed record FabricInstallerPackage(
        string Version,
        string FileName,
        string DownloadUrl,
        string? Sha256);

    private sealed record ModrinthProject(
        string Name,
        string Slug,
        string FileToken);

    private sealed record GitHubRelease(
        string Tag,
        string Name,
        string Body,
        bool Draft,
        bool Prerelease,
        IReadOnlyList<GitHubAsset> Assets);

    private sealed record GitHubAsset(
        string Name,
        string Url,
        string? Sha256);

    private sealed record ManagedManifest(
        DateTimeOffset CreatedAt,
        List<string> ManagedFiles,
        string BackupDirectory);
}
