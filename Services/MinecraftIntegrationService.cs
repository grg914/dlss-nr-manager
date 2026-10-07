using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

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

    private const string ManagerRepository = "grg914/dlss-nr-manager";
    private const string MinecraftRuntimeAsset =
        "minecraft-runtime-26.2.zip";
    private const long MaxComponentDownloadBytes = 1024L * 1024 * 1024;

    private readonly HttpClient _http = new();

    public MinecraftIntegrationService()
    {
        _http.Timeout = TimeSpan.FromMinutes(10);
        _http.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("DlssNrManager", AppIdentity.UserAgentVersion));
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
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

    public async Task<string?> GetLatestCausticaBuildLabelAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var release = await FindReleaseAsync(
                ManagerRepository,
                r => ReleaseBundlesCausticaForMinecraftVersion(
                    r,
                    MinecraftVersion),
                includePrerelease: true,
                cancellationToken);

            var asset = release.Assets.FirstOrDefault(candidate =>
                IsProductionCausticaJar(candidate.Name));

            return asset == null
                ? $"{release.Tag} • no bundled JAR asset"
                : $"{release.Tag} • {asset.Name}";
        }
        catch
        {
            return null;
        }
    }

    public async Task LaunchFabricInstallerAsync(
        string minecraftRoot,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateInstance(minecraftRoot);

        if (Process.GetProcessesByName("MinecraftLauncher").Length > 0)
        {
            throw new InvalidOperationException(
                "Minecraft Launcher is currently running. Close it completely before installing Fabric so launcher_profiles.json cannot be overwritten while DLSS NR Manager is updating the instance.");
        }

        var java = await EnsureJava25Async(
            minecraftRoot,
            progress,
            cancellationToken);

        var runtimeBundle = await EnsureMinecraftRuntimeBundleAsync(
            progress,
            cancellationToken);

        var installerPackage = runtimeBundle.Manifest.FabricInstaller;
        var loaderVersion = runtimeBundle.Manifest.FabricLoaderVersion;
        var installer = ResolveBundlePath(
            runtimeBundle.RootDirectory,
            installerPackage.RelativePath);

        if (!File.Exists(installer))
        {
            throw new FileNotFoundException(
                "Manager-owned Minecraft runtime bundle is missing its Fabric Installer.",
                installer);
        }

        var installerSha = await Sha256Async(
            installer,
            cancellationToken);

        if (!installerSha.Equals(
                installerPackage.Sha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Bundled Fabric Installer SHA-256 mismatch. Expected {installerPackage.Sha256}, got {installerSha}.");
        }

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

        var launcherType = DetectPreferredFabricLauncherType(
            minecraftRoot);

        if (!string.IsNullOrWhiteSpace(launcherType))
        {
            psi.ArgumentList.Add("-launcher");
            psi.ArgumentList.Add(launcherType);

            progress?.Report(
                $"Fabric profile target: {launcherType}.");
        }

        using var process = ExternalProcessTracker.Start(psi);

        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            _ = await stdout;
            var error = await stderr;

            if (process.ExitCode != 0)
                throw new InvalidOperationException(
                    "Fabric Installer failed.\n" + Tail(error, 3000));
        }
        catch (OperationCanceledException)
        {
            ExternalProcessTracker.Kill(process);
            throw;
        }
        finally
        {
            if (!process.HasExited)
                ExternalProcessTracker.Kill(process);
            else
                ExternalProcessTracker.Untrack(process);
        }

        progress?.Report("Fabric Loader installation finished. Restart Minecraft Launcher before continuing.");
    }

    private static string? DetectPreferredFabricLauncherType(
        string minecraftRoot)
    {
        var win32 = Path.Combine(
            minecraftRoot,
            "launcher_profiles.json");
        var microsoftStore = Path.Combine(
            minecraftRoot,
            "launcher_profiles_microsoft_store.json");

        var hasWin32 = File.Exists(win32);
        var hasStore = File.Exists(microsoftStore);

        if (hasWin32 && !hasStore)
            return "win32";

        if (hasStore && !hasWin32)
            return "microsoft_store";

        if (hasWin32 && hasStore)
        {
            try
            {
                return File.GetLastWriteTimeUtc(microsoftStore) >=
                       File.GetLastWriteTimeUtc(win32)
                    ? "microsoft_store"
                    : "win32";
            }
            catch
            {
                // Prefer the modern launcher when both profile stores exist.
                return "microsoft_store";
            }
        }

        return null;
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
                    $"Finding bundled Fabric API build for Minecraft {MinecraftVersion}…");

                var fabricApi = await InstallBundledMinecraftProjectAsync(
                    instance.RootDirectory,
                    mods,
                    backup,
                    new MinecraftProject(
                        "Fabric API",
                        "fabric-api",
                        "fabric-api",
                        "fabric-api"),
                    loader: "fabric",
                    progress,
                    cancellationToken,
                    requireReleaseBuild: true);

                managedDestinations.Add(fabricApi.InstalledPath);
                installed.Add(fabricApi);
            }

            progress?.Report("Finding latest compatible Caustica RTX bundle…");

            var caustica = await FindReleaseAsync(
                ManagerRepository,
                r => ReleaseBundlesCausticaForMinecraftVersion(
                    r,
                    MinecraftVersion),
                includePrerelease: true,
                cancellationToken);

            const string causticaSourceRepository = ManagerRepository;

            progress?.Report(
                $"Using bundled Caustica RTX from DLSS NR Manager release {caustica.Tag}.");

            var causticaAsset = SelectAsset(
                caustica,
                IsProductionCausticaJar);

            await BackupMatchingFileAsync(
                instance.RootDirectory,
                mods,
                backup,
                "caustica",
                "caustica",
                excludedPath: null,
                cancellationToken);

            var causticaDestination = Path.Combine(mods, causticaAsset.Name);
            managedDestinations.Add(causticaDestination);
            await DownloadAssetAsync(
                causticaAsset,
                causticaDestination,
                progress,
                cancellationToken);

            if (!FabricJarSupportsMinecraftVersion(
                    causticaDestination,
                    "caustica",
                    MinecraftVersion))
            {
                throw new InvalidDataException(
                    $"Downloaded Caustica RTX JAR does not declare compatibility with Minecraft {MinecraftVersion}.");
            }

            installed.Add(new MinecraftComponentResult(
                "Caustica RTX", caustica.Tag, causticaDestination, causticaSourceRepository));

            if (installRtxPerformancePack)
            {
                progress?.Report("Installing bundled RTX-safe Minecraft performance mods…");

                foreach (var project in new[]
                {
                    new MinecraftProject("Lithium", "lithium", "lithium", "lithium"),
                    new MinecraftProject("FerriteCore", "ferrite-core", "ferritecore", "ferritecore"),
                    new MinecraftProject("Krypton", "krypton", "krypton", "krypton"),
                    new MinecraftProject("C2ME", "c2me-fabric", "c2me", "c2me"),
                    new MinecraftProject("BadOptimizations", "badoptimizations", "badoptimizations", "badoptimizations"),
                    new MinecraftProject("Dynamic FPS", "dynamic-fps", "dynamic-fps", "dynamic_fps")
                })
                {
                    try
                    {
                        var component = await InstallBundledMinecraftProjectAsync(
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
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        RestoreMatchingBackupFiles(
                            backup,
                            instance.RootDirectory,
                            mods,
                            project.FileToken,
                            project.FabricModId);

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
                    new MinecraftProject(
                        "SPBR LabPBR",
                        "spbr",
                        "spbr");

                try
                {
                    var spbr = await InstallBundledMinecraftProjectAsync(
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
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    RestoreMatchingBackupFiles(
                        backup,
                        instance.RootDirectory,
                        resourcePacks,
                        spbrProject.FileToken,
                        spbrProject.FabricModId);

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
                _ = Process.Start(
                    new ProcessStartInfo(candidate)
                    {
                        UseShellExecute = true
                    }) ?? throw new InvalidOperationException(
                        "Windows could not launch Minecraft Launcher.");
                return;
            }
        }

        _ = Process.Start(new ProcessStartInfo(
            "https://www.minecraft.net/download")
        {
            UseShellExecute = true
        }) ?? throw new InvalidOperationException(
            "Windows could not open the Minecraft download page.");
    }

    private async Task<GitHubRelease> FindReleaseAsync(
        string repository,
        Func<GitHubRelease, bool> predicate,
        bool includePrerelease,
        CancellationToken cancellationToken)
    {
        const int pageSize = 100;
        const int maxPages = 5;

        for (var page = 1; page <= maxPages; page++)
        {
            using var json = await GetJsonAsync(
                $"https://api.github.com/repos/{repository}/releases?per_page={pageSize}&page={page}",
                cancellationToken);

            if (json.RootElement.ValueKind != JsonValueKind.Array)
                break;

            var count = 0;

            foreach (var item in json.RootElement.EnumerateArray())
            {
                count++;

                var release = ParseRelease(item);
                if (release.Draft ||
                    (!includePrerelease && release.Prerelease))
                    continue;

                if (predicate(release))
                    return release;
            }

            if (count < pageSize)
                break;
        }

        throw new InvalidOperationException(
            $"No compatible release was found in {repository} after checking up to {pageSize * maxPages} releases.");
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

        if (!Uri.TryCreate(asset.Url, UriKind.Absolute, out var assetUri) ||
            !assetUri.Scheme.Equals(
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase) ||
            !assetUri.Host.Equals(
                "github.com",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Unexpected GitHub asset URL: {asset.Url}");
        }

        try
        {
            using var response = await _http.GetAsync(
                assetUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            if (response.Content.Headers.ContentLength is > MaxComponentDownloadBytes)
            {
                throw new InvalidDataException(
                    $"{asset.Name} exceeds the 1 GB download safety limit.");
            }

            await using (var input =
                await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = new FileStream(
                             temp,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             128 * 1024,
                             useAsync: true))
            {
                await CopyWithLimitAsync(
                    input,
                    output,
                    MaxComponentDownloadBytes,
                    cancellationToken);
            }

            if (!string.IsNullOrWhiteSpace(asset.Sha256))
            {
                var actual = await Sha256Async(temp, cancellationToken);
                if (!actual.Equals(
                        asset.Sha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"SHA-256 mismatch for {asset.Name}. Expected {asset.Sha256}, got {actual}.");
                }
            }

            File.Move(temp, destination, true);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    private async Task<MinecraftComponentResult> InstallBundledMinecraftProjectAsync(
        string minecraftRoot,
        string destinationDirectory,
        string backup,
        MinecraftProject project,
        string? loader,
        IProgress<string>? progress,
        CancellationToken cancellationToken,
        bool requireReleaseBuild = false)
    {
        _ = loader;
        _ = requireReleaseBuild;

        var runtimeBundle = await EnsureMinecraftRuntimeBundleAsync(
            progress,
            cancellationToken);

        var component = runtimeBundle.Manifest.Components
            .FirstOrDefault(candidate =>
                candidate.Slug.Equals(
                    project.Slug,
                    StringComparison.OrdinalIgnoreCase));

        if (component == null)
        {
            throw new InvalidOperationException(
                $"Manager-owned Minecraft runtime bundle has no {project.Name} component.");
        }

        var source = ResolveBundlePath(
            runtimeBundle.RootDirectory,
            component.RelativePath);

        if (!File.Exists(source))
        {
            throw new FileNotFoundException(
                $"Bundled {project.Name} file is missing.",
                source);
        }

        var filename = Path.GetFileName(component.FileName);
        if (string.IsNullOrWhiteSpace(filename) ||
            !filename.Equals(
                component.FileName,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Bundled filename is invalid for {project.Name}: {component.FileName}");
        }

        var expectedExtension =
            component.Kind.Equals(
                "resourcepack",
                StringComparison.OrdinalIgnoreCase)
                ? ".zip"
                : ".jar";

        if (!filename.EndsWith(
                expectedExtension,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Bundled {project.Name} has unexpected file type: {filename}");
        }

        if (string.IsNullOrWhiteSpace(component.Sha512) ||
            component.Sha512.Length != 128 ||
            !component.Sha512.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException(
                $"Bundled SHA-512 metadata is invalid for {project.Name}.");
        }

        var destination = Path.Combine(
            destinationDirectory,
            filename);
        var temp = destination + ".download";

        progress?.Report(
            $"Installing manager-owned {project.Name} {component.Version}…");

        try
        {
            File.Copy(source, temp, true);

            await using (var stream = File.OpenRead(temp))
            {
                var actual = Convert.ToHexString(
                    await SHA512.HashDataAsync(
                        stream,
                        cancellationToken));

                if (!actual.Equals(
                        component.Sha512,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"SHA-512 mismatch for bundled {project.Name}. " +
                        $"Expected {component.Sha512}, got {actual}.");
                }
            }

            await BackupMatchingFileAsync(
                minecraftRoot,
                destinationDirectory,
                backup,
                project.FileToken,
                project.FabricModId,
                temp,
                cancellationToken);

            File.Move(temp, destination, true);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }

        return new MinecraftComponentResult(
            project.Name,
            component.Version,
            destination,
            ManagerRepository);
    }

    private static async Task BackupMatchingFileAsync(
        string minecraftRoot,
        string directory,
        string backup,
        string token,
        string? fabricModId,
        string? excludedPath,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(directory))
            return;

        foreach (var file in Directory.EnumerateFiles(
                     directory,
                     "*",
                     SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!string.IsNullOrWhiteSpace(excludedPath) &&
                Path.GetFullPath(file).Equals(
                    Path.GetFullPath(excludedPath),
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var name = Path.GetFileName(file);
            if (name.EndsWith(
                    ".download",
                    StringComparison.OrdinalIgnoreCase))
                continue;

            var matches = false;

            if (!string.IsNullOrWhiteSpace(fabricModId) &&
                file.EndsWith(
                    ".jar",
                    StringComparison.OrdinalIgnoreCase))
            {
                matches = JarContainsFabricModId(
                    file,
                    fabricModId);
            }
            else
            {
                matches = name.Contains(
                    token,
                    StringComparison.OrdinalIgnoreCase);
            }

            if (!matches)
                continue;

            var relativeDirectory = Path.GetRelativePath(
                minecraftRoot,
                directory);

            var destination = Path.Combine(
                backup,
                "extra",
                relativeDirectory,
                name);

            Directory.CreateDirectory(
                Path.GetDirectoryName(destination)!);

            await using var input = File.OpenRead(file);
            await using var output = File.Create(destination);
            await input.CopyToAsync(output, cancellationToken);

            File.Delete(file);
        }
    }

    private static bool FabricJarSupportsMinecraftVersion(
        string jarPath,
        string expectedModId,
        string minecraftVersion)
    {
        try
        {
            using var archive = ZipFile.OpenRead(jarPath);
            var metadata = archive.GetEntry("fabric.mod.json");
            if (metadata == null)
                return false;

            using var stream = metadata.Open();
            using var document = JsonDocument.Parse(stream);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return false;

            if (!document.RootElement.TryGetProperty(
                    "id",
                    out var idElement) ||
                !string.Equals(
                    idElement.GetString(),
                    expectedModId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!document.RootElement.TryGetProperty(
                    "depends",
                    out var depends) ||
                depends.ValueKind != JsonValueKind.Object ||
                !depends.TryGetProperty(
                    "minecraft",
                    out var minecraftConstraint))
            {
                return false;
            }

            return MinecraftConstraintIncludesVersion(
                minecraftConstraint,
                minecraftVersion);
        }
        catch
        {
            return false;
        }
    }

    private static bool MinecraftConstraintIncludesVersion(
        JsonElement constraint,
        string minecraftVersion)
    {
        if (constraint.ValueKind == JsonValueKind.String)
        {
            var value = constraint.GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(value))
                return false;

            if (value == "*")
                return true;

            if (value.Equals(
                    minecraftVersion,
                    StringComparison.OrdinalIgnoreCase))
                return true;

            // Support common Fabric metadata alternatives such as
            // ">=26.2 <26.3" without pretending to implement the entire
            // Fabric semantic-version grammar.
            return System.Text.RegularExpressions.Regex.IsMatch(
                value,
                $@"(?<![0-9.]){System.Text.RegularExpressions.Regex.Escape(minecraftVersion)}(?![0-9.])",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        }

        if (constraint.ValueKind == JsonValueKind.Array)
        {
            return constraint
                .EnumerateArray()
                .Any(item =>
                    MinecraftConstraintIncludesVersion(
                        item,
                        minecraftVersion));
        }

        return false;
    }

    private static bool JarContainsFabricModId(
        string jarPath,
        string expectedModId)
    {
        try
        {
            using var archive = ZipFile.OpenRead(jarPath);
            var metadata = archive.GetEntry("fabric.mod.json");
            if (metadata == null)
                return false;

            using var stream = metadata.Open();
            using var document = JsonDocument.Parse(stream);

            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty(
                    "id",
                    out var idElement))
            {
                return string.Equals(
                    idElement.GetString(),
                    expectedModId,
                    StringComparison.OrdinalIgnoreCase);
            }

            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in document.RootElement.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object &&
                        item.TryGetProperty("id", out var arrayId) &&
                        string.Equals(
                            arrayId.GetString(),
                            expectedModId,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
        }
        catch
        {
            // Unknown/corrupt JARs are never removed by filename fallback
            // when an exact Fabric mod ID is available.
        }

        return false;
    }

    private static void RestoreMatchingBackupFiles(
        string backup,
        string minecraftRoot,
        string destinationDirectory,
        string token,
        string? fabricModId)
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

            var matches =
                !string.IsNullOrWhiteSpace(fabricModId) &&
                file.EndsWith(
                    ".jar",
                    StringComparison.OrdinalIgnoreCase)
                    ? JarContainsFabricModId(
                        file,
                        fabricModId)
                    : name.Contains(
                        token,
                        StringComparison.OrdinalIgnoreCase);

            if (!matches)
                continue;

            File.Copy(
                file,
                Path.Combine(destinationDirectory, name),
                true);
        }
    }

    private async Task<MinecraftRuntimeBundle> EnsureMinecraftRuntimeBundleAsync(
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var release = await FindReleaseAsync(
            ManagerRepository,
            candidate => candidate.Assets.Any(asset =>
                asset.Name.Equals(
                    MinecraftRuntimeAsset,
                    StringComparison.OrdinalIgnoreCase)),
            includePrerelease: true,
            cancellationToken);

        var asset = SelectAsset(
            release,
            name => name.Equals(
                MinecraftRuntimeAsset,
                StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(asset.Sha256))
        {
            throw new InvalidDataException(
                $"Manager-owned {MinecraftRuntimeAsset} has no SHA-256 digest.");
        }

        var safeTag = string.Concat(
            release.Tag.Select(ch =>
                Path.GetInvalidFileNameChars().Contains(ch)
                    ? '_'
                    : ch));

        var cacheRoot = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "DlssNrManager",
            "minecraft",
            "runtime-" + MinecraftVersion,
            safeTag);

        var contentRoot = Path.Combine(cacheRoot, "content");
        var manifestPath = Path.Combine(
            contentRoot,
            "minecraft-runtime.json");

        if (File.Exists(manifestPath))
        {
            try
            {
                var cached = await LoadMinecraftRuntimeManifestAsync(
                    manifestPath,
                    cancellationToken);

                await ValidateMinecraftRuntimeBundleAsync(
                    contentRoot,
                    cached,
                    cancellationToken);

                return new MinecraftRuntimeBundle(
                    contentRoot,
                    cached);
            }
            catch (Exception ex) when (
                ex is not OperationCanceledException)
            {
                AppLogger.Warn(
                    $"Cached Minecraft runtime bundle is invalid; refreshing it: {ex.Message}");
                TryDeleteDirectory(cacheRoot);
            }
        }

        Directory.CreateDirectory(cacheRoot);
        var zip = Path.Combine(
            cacheRoot,
            MinecraftRuntimeAsset);
        var extract = Path.Combine(
            cacheRoot,
            "_extract");

        progress?.Report(
            $"Downloading manager-owned Minecraft {MinecraftVersion} runtime bundle…");

        await DownloadAssetAsync(
            asset,
            zip,
            progress,
            cancellationToken);

        TryDeleteDirectory(extract);
        Directory.CreateDirectory(extract);

        SafeZip.Extract(
            zip,
            extract,
            maxExpandedBytes:
                MaxComponentDownloadBytes * 4);

        var extractedManifest = Path.Combine(
            extract,
            "minecraft-runtime.json");

        if (!File.Exists(extractedManifest))
        {
            throw new InvalidDataException(
                $"{MinecraftRuntimeAsset} does not contain minecraft-runtime.json.");
        }

        var manifest = await LoadMinecraftRuntimeManifestAsync(
            extractedManifest,
            cancellationToken);

        await ValidateMinecraftRuntimeBundleAsync(
            extract,
            manifest,
            cancellationToken);

        TryDeleteDirectory(contentRoot);
        Directory.Move(extract, contentRoot);
        TryDelete(zip);

        return new MinecraftRuntimeBundle(
            contentRoot,
            manifest);
    }

    private static async Task<MinecraftRuntimeManifest> LoadMinecraftRuntimeManifestAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);

        var manifest =
            await JsonSerializer.DeserializeAsync<MinecraftRuntimeManifest>(
                stream,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                },
                cancellationToken);

        return manifest ??
               throw new InvalidDataException(
                   "Minecraft runtime manifest is empty.");
    }

    private static async Task ValidateMinecraftRuntimeBundleAsync(
        string root,
        MinecraftRuntimeManifest manifest,
        CancellationToken cancellationToken)
    {
        if (manifest.SchemaVersion != 1)
        {
            throw new InvalidDataException(
                $"Unsupported Minecraft runtime manifest schema {manifest.SchemaVersion}.");
        }

        if (!manifest.MinecraftVersion.Equals(
                MinecraftVersion,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Minecraft runtime bundle targets {manifest.MinecraftVersion}, expected {MinecraftVersion}.");
        }

        if (!Version.TryParse(
                manifest.FabricLoaderVersion,
                out var loaderVersion) ||
            loaderVersion <
            Version.Parse(MinimumFabricLoader))
        {
            throw new InvalidDataException(
                $"Minecraft runtime bundle has incompatible Fabric Loader {manifest.FabricLoaderVersion}.");
        }

        var installer = ResolveBundlePath(
            root,
            manifest.FabricInstaller.RelativePath);

        if (!File.Exists(installer))
        {
            throw new FileNotFoundException(
                "Minecraft runtime bundle is missing Fabric Installer.",
                installer);
        }

        if (string.IsNullOrWhiteSpace(
                manifest.FabricInstaller.Sha256) ||
            manifest.FabricInstaller.Sha256.Length != 64 ||
            !manifest.FabricInstaller.Sha256.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException(
                "Minecraft runtime bundle has invalid Fabric Installer SHA-256 metadata.");
        }

        var installerHash = await Sha256Async(
            installer,
            cancellationToken);

        if (!installerHash.Equals(
                manifest.FabricInstaller.Sha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Minecraft runtime bundle Fabric Installer hash mismatch.");
        }

        foreach (var component in manifest.Components)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var componentPath = ResolveBundlePath(
                root,
                component.RelativePath);

            if (!File.Exists(componentPath))
            {
                throw new FileNotFoundException(
                    $"Minecraft runtime component is missing: {component.Name}",
                    componentPath);
            }

            if (string.IsNullOrWhiteSpace(component.Sha512) ||
                component.Sha512.Length != 128 ||
                !component.Sha512.All(Uri.IsHexDigit))
            {
                throw new InvalidDataException(
                    $"Minecraft runtime component has invalid SHA-512 metadata: {component.Name}");
            }

            await using var componentStream =
                File.OpenRead(componentPath);

            var actual = Convert.ToHexString(
                await SHA512.HashDataAsync(
                    componentStream,
                    cancellationToken));

            if (!actual.Equals(
                    component.Sha512,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Minecraft runtime component hash mismatch: {component.Name}");
            }
        }
    }

    private static string ResolveBundlePath(
        string root,
        string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new InvalidDataException(
                "Minecraft runtime manifest contains an empty path.");
        }

        var fullRoot =
            Path.GetFullPath(root)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;

        var normalizedRelative = relativePath
            .Replace(
                '/',
                Path.DirectorySeparatorChar);

        var fullPath = Path.GetFullPath(
            Path.Combine(
                fullRoot,
                normalizedRelative));

        if (!fullPath.StartsWith(
                fullRoot,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Minecraft runtime path escapes the bundle root: {relativePath}");
        }

        return fullPath;
    }

    private static async Task CopyWithLimitAsync(
        Stream input,
        Stream output,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[128 * 1024];
        long total = 0;

        while (true)
        {
            var read = await input.ReadAsync(
                buffer.AsMemory(0, buffer.Length),
                cancellationToken);

            if (read == 0)
                break;

            total += read;
            if (total > maxBytes)
            {
                throw new InvalidDataException(
                    $"Download exceeded the {maxBytes / (1024 * 1024)} MB safety limit.");
            }

            await output.WriteAsync(
                buffer.AsMemory(0, read),
                cancellationToken);
        }
    }

    private async Task<JsonDocument> GetJsonAsync(
        string url,
        CancellationToken cancellationToken)
    {
        JsonDocument? document = null;

        await NetworkRetry.ExecuteAsync(
            async (_, token) =>
            {
                using var response = await _http.GetAsync(
                    url,
                    HttpCompletionOption.ResponseHeadersRead,
                    token);
                response.EnsureSuccessStatusCode();

                await using var stream =
                    await response.Content.ReadAsStreamAsync(token);

                document = await JsonDocument.ParseAsync(
                    stream,
                    cancellationToken: token);
            },
            cancellationToken,
            attempts: 3);

        return document ??
               throw new InvalidOperationException(
                   "Minecraft component metadata could not be loaded.");
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

    private static bool IsProductionCausticaJar(string name)
        => name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
           && name.Contains("caustica", StringComparison.OrdinalIgnoreCase)
           && !ContainsAny(name, "sources", "dev", "javadoc");

    private static bool ReleaseBundlesCausticaForMinecraftVersion(
        GitHubRelease release,
        string minecraftVersion)
        => release.Assets.Any(asset =>
            IsProductionCausticaJar(asset.Name)
            && asset.Name.Contains(
                minecraftVersion,
                StringComparison.OrdinalIgnoreCase));

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
                foreach (var candidate in EnumerateFilesBounded(
                             runtimeRoot,
                             "java.exe",
                             maxDepth: 6,
                             maxResults: 64))
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
                    foreach (var candidate in EnumerateFilesBounded(
                                 vendorRoot,
                                 "java.exe",
                                 maxDepth: 5,
                                 maxResults: 48))
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

    private static IEnumerable<string> EnumerateFilesBounded(
        string root,
        string fileName,
        int maxDepth,
        int maxResults)
    {
        if (!Directory.Exists(root) ||
            maxDepth < 0 ||
            maxResults <= 0)
            yield break;

        var pending = new Queue<(string Path, int Depth)>();
        pending.Enqueue((root, 0));
        var yielded = 0;

        while (pending.Count > 0 && yielded < maxResults)
        {
            var (directory, depth) = pending.Dequeue();

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(
                    directory,
                    fileName,
                    SearchOption.TopDirectoryOnly).ToArray();
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                yield return file;
                yielded++;
                if (yielded >= maxResults)
                    yield break;
            }

            if (depth >= maxDepth)
                continue;

            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateDirectories(
                    directory,
                    "*",
                    SearchOption.TopDirectoryOnly).ToArray();
            }
            catch
            {
                continue;
            }

            foreach (var child in children)
            {
                try
                {
                    var attributes = File.GetAttributes(child);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                        continue;

                    pending.Enqueue((child, depth + 1));
                }
                catch { }
            }
        }
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

            using var process = ExternalProcessTracker.Start(psi);

            try
            {
                var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
                var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

                await process.WaitForExitAsync(cancellationToken);
                await stdoutTask;
                await stderrTask;

                return process.ExitCode == 0;
            }
            catch (OperationCanceledException)
            {
                ExternalProcessTracker.Kill(process);
                throw;
            }
            finally
            {
                if (!process.HasExited)
                    ExternalProcessTracker.Kill(process);
                else
                    ExternalProcessTracker.Untrack(process);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
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

            using var process = ExternalProcessTracker.Start(psi);

            try
            {
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
            catch (OperationCanceledException)
            {
                ExternalProcessTracker.Kill(process);
                throw;
            }
            finally
            {
                if (!process.HasExited)
                    ExternalProcessTracker.Kill(process);
                else
                    ExternalProcessTracker.Untrack(process);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
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

        AtomicFile.WriteAllText(
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

    private sealed record MinecraftRuntimeBundle(
        string RootDirectory,
        MinecraftRuntimeManifest Manifest);

    private sealed record MinecraftRuntimeManifest(
        int SchemaVersion,
        string MinecraftVersion,
        DateTimeOffset CreatedAtUtc,
        string FabricLoaderVersion,
        MinecraftRuntimeInstaller FabricInstaller,
        IReadOnlyList<MinecraftRuntimeComponent> Components);

    private sealed record MinecraftRuntimeInstaller(
        string Version,
        string FileName,
        string RelativePath,
        string Sha256);

    private sealed record MinecraftRuntimeComponent(
        string Name,
        string Slug,
        string Kind,
        string Loader,
        string Version,
        string FileName,
        string RelativePath,
        string Sha512);

    private sealed record MinecraftProject(
        string Name,
        string Slug,
        string FileToken,
        string? FabricModId = null);

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
