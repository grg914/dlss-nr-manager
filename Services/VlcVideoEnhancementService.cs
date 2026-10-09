using System.Diagnostics;
using System.Globalization;
using Microsoft.Win32;

namespace DlssNrManager.Services;

public enum VlcHdrMode
{
    Auto,
    Generate,
    Always,
    Never
}

public enum VlcScaleMode
{
    X1 = 1,
    X2 = 2,
    X4 = 4
}

public sealed record VlcLaunchOptions(
    bool SuperResolution,
    bool ArtifactReduction,
    bool HdrEnabled,
    VlcHdrMode HdrMode,
    bool CustomScaleEnabled,
    VlcScaleMode Scale,
    bool Fullscreen,
    bool ShowStatusOverlay);

public sealed record VlcVideoEnhancementStatus(
    bool Found,
    string? ExecutablePath,
    Version? Version,
    bool SupportsD3d11EnhancementOptions,
    string Summary);

public sealed class VlcVideoEnhancementService
{
    private static readonly Version MinimumKnownEnhancementVersion =
        new(3, 0, 24);

    public VlcVideoEnhancementStatus Detect()
    {
        var found = new List<VlcVideoEnhancementStatus>();
        foreach (var candidate in EnumerateCandidates())
        {
            if (string.IsNullOrWhiteSpace(candidate) || !File.Exists(candidate))
                continue;

            Version? version = null;
            try
            {
                var raw = FileVersionInfo.GetVersionInfo(candidate).FileVersion;
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    var numeric = new string(raw.TakeWhile(ch =>
                        char.IsDigit(ch) || ch == '.').ToArray()).TrimEnd('.');
                    _ = Version.TryParse(numeric, out version);
                }
            }
            catch
            {
                // Version metadata is untrusted; keep scanning for a verified VLC.
            }

            // Unknown version is NEVER treated as evidence of D3D11 support.
            var supported = version != null && version >= MinimumKnownEnhancementVersion;
            var label = version == null
                ? "version unverified"
                : $"v{version}";
            found.Add(new VlcVideoEnhancementStatus(
                true, candidate, version, supported,
                supported
                    ? $"VLC {label} detected • Direct3D11 Super Resolution / RTX Video HDR controls available."
                    : $"VLC {label} detected • verified VLC 3.0.24+ required for managed D3D11 enhancement options."));
        }

        return ChoosePreferredCandidate(found);
    }

    /// <summary>
    /// Evaluate every local candidate instead of letting an outdated VLC_PATH
    /// override a verified manager-owned or system VLC installation.
    /// </summary>
    public static VlcVideoEnhancementStatus ChoosePreferredCandidate(
        IEnumerable<VlcVideoEnhancementStatus> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var available = candidates.Where(x => x.Found && !string.IsNullOrWhiteSpace(x.ExecutablePath)).ToArray();
        return available
            .Where(x => x.SupportsD3d11EnhancementOptions && x.Version != null &&
                        x.Version >= MinimumKnownEnhancementVersion)
            .OrderByDescending(x => x.Version)
            .FirstOrDefault()
            ?? available.Where(x => x.Version != null)
                .OrderByDescending(x => x.Version).FirstOrDefault()
            ?? available.FirstOrDefault()
            ?? new VlcVideoEnhancementStatus(
                false, null, null, false,
                "VLC was not detected. Install VLC 3.0.24 or newer.");
    }

    public Process Launch(
        string mediaPath,
        VlcLaunchOptions options)
    {
        if (string.IsNullOrWhiteSpace(mediaPath) ||
            !File.Exists(mediaPath))
        {
            throw new FileNotFoundException(
                "The selected local video file was not found.",
                mediaPath);
        }

        var status = Detect();
        if (!status.Found ||
            string.IsNullOrWhiteSpace(status.ExecutablePath))
        {
            throw new InvalidOperationException(
                "VLC was not detected. Install VLC 3.0.24 or newer first.");
        }

        if (!status.SupportsD3d11EnhancementOptions)
        {
            throw new InvalidOperationException(
                "The detected VLC build is too old for the managed Direct3D11 Super Resolution/HDR options.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = status.ExecutablePath,
            UseShellExecute = false
        };

        foreach (var argument in BuildArguments(mediaPath, options))
            startInfo.ArgumentList.Add(argument);

        // Playback was previously launched outside the manager's lifecycle,
        // allowing a manager-owned VLC subprocess to linger after shutdown.
        // The Windows Job Object now closes it with other owned helpers.
        return ExternalProcessTracker.Start(startInfo);
    }

    public static IReadOnlyList<string> BuildArguments(
        string mediaPath,
        VlcLaunchOptions options)
    {
        var args = new List<string>
        {
            "--vout=direct3d11",

            // Make ON/OFF deterministic instead of inheriting an old VLC
            // preference from the user's profile.
            // VLC exposes NVIDIA artifact cleanup through the same RTX VSR
            // D3D11 extension. ArtifactReduction therefore requests the VSR
            // path even if the explicit upscale toggle is off.
            (options.SuperResolution || options.ArtifactReduction)
                ? "--d3d11-upscale-mode=super"
                : "--d3d11-upscale-mode=linear",

            options.HdrEnabled
                ? $"--d3d11-hdr-mode={HdrModeValue(options.HdrMode)}"
                : "--d3d11-hdr-mode=auto",

            "--no-video-title-show"
        };

        var vsrActive =
            options.SuperResolution ||
            options.ArtifactReduction;

        var hdrOverlayActive =
            options.HdrEnabled &&
            options.HdrMode is VlcHdrMode.Generate or VlcHdrMode.Always;

        if (options.ShowStatusOverlay &&
            (vsrActive || hdrOverlayActive))
        {
            var overlayText =
                vsrActive && hdrOverlayActive
                    ? "● VSR • HDR"
                    : vsrActive
                        ? "● VSR"
                        : "● HDR";

            // VLC marquee sub-source renders a lightweight OSD over the
            // decoded video surface. Position 6 is top-right in VLC's
            // SUBPICTURE_ALIGN_RIGHT | SUBPICTURE_ALIGN_TOP mapping.
            args.Add("--sub-source=marq");
            args.Add($"--marq-marquee={overlayText}");
            args.Add("--marq-position=6");
            args.Add("--marq-opacity=220");
            args.Add("--marq-size=18");
        }

        if (options.Fullscreen)
        {
            // Fullscreen targets the real display surface. A custom zoom is
            // intentionally not applied because it would crop rather than
            // increase the monitor's physical output resolution.
            args.Add("--fullscreen");
            args.Add("--autoscale");
        }
        else
        {
            args.Add("--no-fullscreen");

            if (options.CustomScaleEnabled)
            {
                // Windowed x1/x2/x4 controls the presentation target size.
                // When VSR is also enabled, a larger presentation target
                // gives the NVIDIA D3D11 scaler an upscale surface.
                args.Add("--no-autoscale");
                args.Add(
                    "--zoom=" +
                    ((int)options.Scale).ToString(
                        CultureInfo.InvariantCulture));
            }
            else
            {
                args.Add("--autoscale");
            }
        }

        args.Add(mediaPath);
        return args;
    }

    private static string HdrModeValue(VlcHdrMode mode)
        => mode switch
        {
            VlcHdrMode.Generate => "generate",
            VlcHdrMode.Always => "always",
            VlcHdrMode.Never => "never",
            _ => "auto"
        };

    private static IEnumerable<string> EnumerateCandidates()
    {
        var seen = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        void Add(List<string> values, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            var normalized = Environment.ExpandEnvironmentVariables(value);
            if (seen.Add(normalized))
                values.Add(normalized);
        }

        var candidates = new List<string>();

        Add(
            candidates,
            Environment.GetEnvironmentVariable("VLC_PATH"));

        Add(
            candidates,
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "DlssNrManager",
                "vlc",
                "vlc.exe"));

        Add(
            candidates,
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ProgramFiles),
                "VideoLAN",
                "VLC",
                "vlc.exe"));

        Add(
            candidates,
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ProgramFilesX86),
                "VideoLAN",
                "VLC",
                "vlc.exe"));

        foreach (var hive in new[]
                 {
                     RegistryHive.LocalMachine,
                     RegistryHive.CurrentUser
                 })
        {
            foreach (var view in new[]
                     {
                         RegistryView.Registry64,
                         RegistryView.Registry32
                     })
            {
                try
                {
                    using var baseKey =
                        RegistryKey.OpenBaseKey(hive, view);
                    using var key =
                        baseKey.OpenSubKey(@"SOFTWARE\VideoLAN\VLC");

                    if (key == null)
                        continue;

                    var installDir =
                        key.GetValue("InstallDir") as string ??
                        key.GetValue(null) as string;

                    if (!string.IsNullOrWhiteSpace(installDir))
                    {
                        Add(
                            candidates,
                            Path.Combine(
                                installDir,
                                "vlc.exe"));
                    }
                }
                catch
                {
                    // Registry discovery is best-effort.
                }
            }
        }

        return candidates;
    }
}
