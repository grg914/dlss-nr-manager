namespace DlssNrManager.Services;

public enum DownloadCenterKind
{
    MediaEngine,
    VlcRuntime,
    AiUpscale,
    AiStudioModel,
    AiOriginDetector
}

public sealed record DownloadCenterEntry(
    string Id,
    string DisplayName,
    string Category,
    DownloadCenterKind Kind,
    string Status,
    string License,
    string SizeLabel,
    bool IsInstalled,
    bool CanInstallAutomatically,
    string Details,
    string? ModelId = null,
    bool RequiresLicenseAcceptance = false)
{
    public override string ToString()
        => $"{DisplayName} • {Status}";
}

public sealed class DownloadCenterService
{
    private readonly MediaService _media;
    private readonly AiUpscaleService _aiUpscale;
    private readonly LocalAiStudioService _aiStudio;
    private readonly AiOriginDetectionService _aiOrigin;
    private readonly VlcRuntimeService _vlcRuntime;
    private readonly AiStudioPackageService _aiPackages;
    private readonly AiStudioLicenseAcceptanceService _licenseAcceptances;

    public DownloadCenterService(
        MediaService media,
        AiUpscaleService aiUpscale,
        LocalAiStudioService aiStudio,
        AiOriginDetectionService aiOrigin,
        VlcRuntimeService vlcRuntime)
    {
        _media = media;
        _aiUpscale = aiUpscale;
        _aiStudio = aiStudio;
        _aiOrigin = aiOrigin;
        _vlcRuntime = vlcRuntime;
        _aiPackages = new AiStudioPackageService(aiStudio);
        _licenseAcceptances =
            new AiStudioLicenseAcceptanceService(aiStudio);
    }

    public IReadOnlyList<DownloadCenterEntry> GetEntries(
        string language = "en")
    {
        string T(string value) =>
            UiLocalizationService.Translate(
                value,
                language);
        var entries =
            new List<DownloadCenterEntry>
            {
                new(
                    "media-engine",
                    "Media Neural Runtime",
                    "Media",
                    DownloadCenterKind.MediaEngine,
                    _media.IsReady
                        ? T("Installed")
                        : T("Not installed"),
                    "Manager-owned components",
                    T("Size determined by release assets"),
                    _media.IsReady,
                    true,
                    T("video2dlssnr + FFmpeg. Used by Media Neural, video processing and several local pipelines.")),

                new(
                    "vlc-runtime",
                    "VLC 3.0.24",
                    "Media",
                    DownloadCenterKind.VlcRuntime,
                    _vlcRuntime.IsReady
                        ? T("Installed")
                        : T("Not installed"),
                    "GPL-2.0-or-later • external portable runtime",
                    T("Size determined by release assets"),
                    _vlcRuntime.IsReady,
                    true,
                    T("Portable VLC runtime used by VSR-HDR Video. Matching source and provenance are published with manager-owned assets.")),

                new(
                    "realesrgan",
                    "Real-ESRGAN AI Upscale",
                    "Media",
                    DownloadCenterKind.AiUpscale,
                    _aiUpscale.IsInstalled
                        ? (_aiUpscale.IsReady
                            ? T("Installed")
                            : T("Installed • verification required"))
                        : T("Not installed"),
                    "BSD-3-Clause / model licenses preserved",
                    T("Size determined by release assets"),
                    _aiUpscale.IsInstalled,
                    true,
                    T("Local x2/x3/x4 upscale engine and associated models.")),

                new(
                    "ai-origin-detector",
                    T("AI origin detector"),
                    T("AI Detection"),
                    DownloadCenterKind.AiOriginDetector,
                    _aiOrigin.IsInstalled
                        ? (_aiOrigin.IsReady
                            ? T("Installed")
                            : T("Installed • verification required"))
                        : T("Not installed"),
                    "Manager-owned ONNX model assets",
                    "~102 Mo",
                    _aiOrigin.IsInstalled,
                    true,
                    T("Local two-model ONNX ensemble used by the AI Detection page."))
            };

        foreach (var model in
                 LocalAiStudioService.Models)
        {
            var installed =
                _aiStudio.IsModelInstalled(model);

            entries.Add(
                new DownloadCenterEntry(
                    $"ai-model:{model.Id}",
                    model.DisplayName,
                    T("AI Studio • Models"),
                    DownloadCenterKind.AiStudioModel,
                    installed
                        ? T("Installed")
                        : model.ManagerOwnedRedistributionAllowed
                            ? T("Available if package is published")
                            : T("Manual installation required"),
                    model.License,
                    T(model.HardwareLabel),
                    installed,
                    model.ManagerOwnedRedistributionAllowed,
                    T(model.Notes),
                    model.Id,
                    !model.ManagerOwnedRedistributionAllowed));
        }

        return entries
            .OrderBy(x => x.Category)
            .ThenBy(x => x.DisplayName)
            .ToArray();
    }

    public async Task<string> GetDetailsAsync(
        DownloadCenterEntry entry,
        string language = "en",
        CancellationToken cancellationToken = default)
    {
        string T(string value) =>
            UiLocalizationService.Translate(
                value,
                language);
        if (entry.Kind != DownloadCenterKind.AiStudioModel ||
            string.IsNullOrWhiteSpace(entry.ModelId))
        {
            return
                $"{entry.DisplayName}\n" +
                $"{T("Category")}: {entry.Category}\n" +
                $"{T("Status")}: {entry.Status}\n" +
                $"{T("License")}: {entry.License}\n" +
                $"{T("Size")}: {entry.SizeLabel}\n\n" +
                entry.Details;
        }

        var model =
            LocalAiStudioService.Models.First(
                x => x.Id == entry.ModelId);

        if (!model.ManagerOwnedRedistributionAllowed)
        {
            var info =
                AiStudioLicenseAcceptanceService.GetInfo(model);

            var accepted =
                _licenseAcceptances.IsAccepted(model);

            return
                $"{model.DisplayName}\n" +
                $"{T("Status")}: {entry.Status}\n" +
                $"{T("License")}: {model.License}\n" +
                $"{T("License accepted locally")}: {(accepted ? T("YES") : T("NO"))}\n" +
                $"{T("Distribution from your Releases")}: {T("NO")}\n" +
                $"{T("Installation")}: {T("Import files obtained from the official source after accepting the license.")}\n" +
                $"{T("Official source")}: {info?.OfficialModelUrl ?? model.Repository}\n\n" +
                T(model.Notes);
        }

        var package =
            await _aiPackages.FindLatestPackageAsync(
                model.Id,
                cancellationToken);

        var size =
            package == null
                ? T("Manager-owned package not published")
                : FormatBytes(
                    package.Manifest.Archive.Size,
                    language);

        var version =
            package?.Manifest.Version
            ?? "—";

        return
            $"{model.DisplayName}\n" +
            $"{T("Status")}: {entry.Status}\n" +
            $"{T("License")}: {model.License}\n" +
            $"{T("Package version")}: {version}\n" +
            $"{T("Download/reconstruction size")}: {size}\n" +
            $"Release: {package?.Tag ?? "—"}\n\n" +
            T(model.Notes);
    }

    public AiStudioLicenseInfo? GetManualLicenseInfo(
        DownloadCenterEntry entry)
    {
        if (entry.Kind != DownloadCenterKind.AiStudioModel ||
            string.IsNullOrWhiteSpace(entry.ModelId))
        {
            return null;
        }

        return AiStudioLicenseAcceptanceService.GetInfo(
            ResolveModel(entry));
    }

    public bool IsManualLicenseAccepted(
        DownloadCenterEntry entry)
    {
        if (entry.Kind != DownloadCenterKind.AiStudioModel ||
            string.IsNullOrWhiteSpace(entry.ModelId))
        {
            return true;
        }

        return _licenseAcceptances.IsAccepted(
            ResolveModel(entry));
    }

    public void AcceptManualLicense(
        DownloadCenterEntry entry)
    {
        _licenseAcceptances.Accept(
            ResolveModel(entry));
    }

    public async Task ImportManualModelAsync(
        DownloadCenterEntry entry,
        string sourceDirectory,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var model =
            ResolveModel(entry);

        if (model.ManagerOwnedRedistributionAllowed)
        {
            throw new InvalidOperationException(
                "This model uses the manager-owned release installer instead of manual import.");
        }

        if (!_licenseAcceptances.IsAccepted(model))
        {
            throw new InvalidOperationException(
                $"La licence de {model.DisplayName} doit être acceptée avant l'import.");
        }

        await _aiStudio.ImportModelDirectoryAsync(
            model,
            sourceDirectory,
            progress,
            cancellationToken);
    }

    public async Task InstallAsync(
        DownloadCenterEntry entry,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        switch (entry.Kind)
        {
            case DownloadCenterKind.MediaEngine:
                await _media.SetupAsync(
                    progress,
                    cancellationToken);
                break;

            case DownloadCenterKind.VlcRuntime:
                await _vlcRuntime.SetupAsync(
                    progress,
                    cancellationToken);
                break;

            case DownloadCenterKind.AiUpscale:
                await _aiUpscale.SetupAsync(
                    progress,
                    cancellationToken);
                break;

            case DownloadCenterKind.AiStudioModel:
            {
                var model =
                    ResolveModel(entry);

                if (!model.ManagerOwnedRedistributionAllowed)
                {
                    throw new InvalidOperationException(
                        $"{model.DisplayName} nécessite l'acceptation de sa licence puis l'import des fichiers obtenus depuis la source officielle.");
                }

                await _aiPackages.InstallAsync(
                    model,
                    progress,
                    cancellationToken);
                break;
            }

            case DownloadCenterKind.AiOriginDetector:
                await _aiOrigin.SetupAsync(
                    progress,
                    cancellationToken,
                    forceVerify: true);
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(entry.Kind));
        }
    }

    public void Remove(
        DownloadCenterEntry entry)
    {
        switch (entry.Kind)
        {
            case DownloadCenterKind.MediaEngine:
                _media.ResetTools();
                break;

            case DownloadCenterKind.VlcRuntime:
                _vlcRuntime.Reset();
                break;

            case DownloadCenterKind.AiUpscale:
                _aiUpscale.Reset();
                break;

            case DownloadCenterKind.AiStudioModel:
                _aiStudio.RemoveModel(
                    ResolveModel(entry));
                break;

            case DownloadCenterKind.AiOriginDetector:
                _aiOrigin.Reset();
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(entry.Kind));
        }
    }

    public async Task RedownloadAsync(
        DownloadCenterEntry entry,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // AI Studio packages already stage, validate and atomically replace
        // the selected model. In particular, do not remove a licensed model.
        if (entry.Kind == DownloadCenterKind.AiStudioModel)
        {
            await InstallAsync(entry, progress, cancellationToken);
            return;
        }

        string[] ownedDirectories = entry.Kind switch
        {
            DownloadCenterKind.MediaEngine => new[]
            {
                Path.Combine(_media.RootDirectory, "video2dlssnr"),
                Path.Combine(_media.RootDirectory, "tools")
            },
            DownloadCenterKind.VlcRuntime => [_vlcRuntime.RootDirectory],
            DownloadCenterKind.AiUpscale => [_aiUpscale.RootDirectory],
            DownloadCenterKind.AiOriginDetector => [_aiOrigin.RootDirectory],
            _ => throw new ArgumentOutOfRangeException(nameof(entry.Kind))
        };

        await ManagedComponentRedownload.ReplaceAsync(
            ownedDirectories,
            async token =>
            {
                if (entry.Kind == DownloadCenterKind.AiOriginDetector)
                    await _aiOrigin.SetupAsync(progress, token, forceVerify: true);
                else
                    await InstallAsync(entry, progress, token);
            },
            () => entry.Kind switch
            {
                DownloadCenterKind.MediaEngine => _media.IsReady,
                DownloadCenterKind.VlcRuntime => _vlcRuntime.IsReady,
                DownloadCenterKind.AiUpscale => _aiUpscale.IsReady,
                DownloadCenterKind.AiOriginDetector => _aiOrigin.IsReady,
                _ => false
            },
            cancellationToken);
    }

    private static AiStudioModelDescriptor ResolveModel(
        DownloadCenterEntry entry)
    {
        if (string.IsNullOrWhiteSpace(
                entry.ModelId))
        {
            throw new InvalidOperationException(
                "Download Center model entry has no model id.");
        }

        return LocalAiStudioService.Models.First(
            x => x.Id == entry.ModelId);
    }

    public static string FormatBytes(
        long bytes,
        string language = "en")
    {
        var french =
            UiLocalizationService.NormalizeLanguage(
                language) == "fr";

        if (bytes >= 1024L * 1024 * 1024)
            return french
                ? $"{bytes / (1024d * 1024d * 1024d):0.00} Go"
                : $"{bytes / (1024d * 1024d * 1024d):0.00} GB";

        if (bytes >= 1024L * 1024)
            return french
                ? $"{bytes / (1024d * 1024d):0.0} Mo"
                : $"{bytes / (1024d * 1024d):0.0} MB";

        return french
            ? $"{bytes / 1024d:0.0} Ko"
            : $"{bytes / 1024d:0.0} KB";
    }
}
