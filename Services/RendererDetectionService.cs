using System.Diagnostics;
using System.Text;

namespace DlssNrManager.Services;

public sealed record RendererCandidate(
    string Executable,
    string Api,
    string Evidence,
    bool VulkanWrapper);

public sealed record RendererDetectionResult(
    IReadOnlyList<RendererCandidate> Candidates,
    string Summary)
{
    public RendererCandidate? Preferred => Candidates.FirstOrDefault();
}

public static class RendererDetectionService
{
    private const int MarkerReadLimit = 16 * 1024 * 1024;

    private static readonly string[] SkipExecutableTokens =
    [
        "launcher", "setup", "install", "unins", "crash", "report",
        "helper", "service", "updater", "update", "easyanticheat",
        "battleye", "redlauncher", "modorganizer"
    ];

    public static RendererDetectionResult Detect(string directory)
    {
        if (!Directory.Exists(directory))
            return new([], "Renderer: target directory not found.");

        var candidates = new List<RendererCandidate>();
        foreach (var exe in SafeFiles(directory, "*.exe")
                     .Where(path => !SkipExecutableTokens.Any(token =>
                         Path.GetFileName(path).Contains(
                             token,
                             StringComparison.OrdinalIgnoreCase)))
                     .OrderByDescending(SafeLength)
                     .Take(16))
        {
            var api = DetectApi(exe, out var evidence);

            if (api == null)
            {
                var module = DetectEngineModuleApi(exe);
                if (module != null)
                {
                    api = module.Value.Api;
                    evidence = $"engine module {module.Value.Module}";
                }
            }

            var wrapper = DetectVulkanWrapper(Path.GetDirectoryName(exe)!);

            if (wrapper != null)
            {
                api = "Vulkan";
                evidence += $" • translated through {wrapper}";
            }

            if (api != null)
            {
                candidates.Add(new RendererCandidate(
                    exe,
                    api,
                    evidence,
                    wrapper != null));
            }
        }

        if (candidates.Count == 0)
            return new([], "Renderer: no authoritative DX/Vulkan/OpenGL signal found.");

        var preferred = candidates[0];
        var alternatives = candidates
            .Skip(1)
            .Select(x => $"{Path.GetFileName(x.Executable)}={x.Api}")
            .ToList();

        var summary =
            $"Renderer: {preferred.Api} • {preferred.Evidence}" +
            (alternatives.Count == 0
                ? ""
                : $" • alternatives: {string.Join(", ", alternatives)}");

        return new(candidates, summary);
    }

    public static string? ReadPreferredExecutable(string gameDir)
    {
        try
        {
            var marker = Path.Combine(gameDir, ".dlssnr-manager-exe");
            if (!File.Exists(marker))
                return null;

            var relative = File.ReadAllText(marker).Trim();
            if (string.IsNullOrWhiteSpace(relative) ||
                Path.IsPathRooted(relative))
                return null;

            var root = NormalizeRoot(gameDir);
            var full = Path.GetFullPath(Path.Combine(gameDir, relative));

            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) &&
                   File.Exists(full)
                ? full
                : null;
        }
        catch
        {
            return null;
        }
    }

    public static void SetPreferredExecutable(
        string gameDir,
        string executable)
    {
        var root = NormalizeRoot(gameDir);
        var full = Path.GetFullPath(executable);

        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(full))
        {
            throw new InvalidOperationException(
                "The selected executable is outside the selected game folder.");
        }

        AtomicFile.WriteAllText(
            Path.Combine(gameDir, ".dlssnr-manager-exe"),
            Path.GetRelativePath(gameDir, full));
    }

    private static string NormalizeRoot(string path)
        => Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
           + Path.DirectorySeparatorChar;

    private static string? DetectApi(
        string executable,
        out string evidence)
    {
        var lower = Path.GetFileName(executable).ToLowerInvariant();

        if (lower.Contains("dx12") || lower.Contains("d3d12"))
        {
            evidence = "executable name";
            return "DirectX 12";
        }

        if (lower.Contains("dx11") || lower.Contains("d3d11"))
        {
            evidence = "executable name";
            return "DirectX 11";
        }

        if (lower.Contains("vulkan"))
        {
            evidence = "executable name";
            return "Vulkan";
        }

        var markers = ReadMarkers(executable);
        foreach (var (marker, api) in new[]
                 {
                     ("D3D12CreateDevice", "DirectX 12"),
                     ("D3D12SDKVersion", "DirectX 12"),
                     ("D3D11CreateDevice", "DirectX 11"),
                     ("D3D10CreateDevice", "DirectX 10"),
                     ("Direct3DCreate9", "DirectX 9"),
                     ("Direct3DCreate8", "DirectX 8"),
                     ("DirectDrawCreate", "DirectDraw"),
                     ("vkCreateInstance", "Vulkan"),
                     ("wglCreateContext", "OpenGL")
                 })
        {
            if (markers.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                evidence = $"binary marker {marker}";
                return api;
            }
        }

        evidence = "no renderer marker";
        return null;
    }

    private static (string Api, string Module)? DetectEngineModuleApi(
        string executable)
    {
        var exeName = Path.GetFileName(executable).ToLowerInvariant();

        var candidates = exeName switch
        {
            "hl.exe" => new[] { "hw.dll" },
            "hl2.exe" => new[] { "bin/shaderapidx9.dll", "bin/engine.dll", "bin/x64/shaderapidx9.dll", "bin/x64/engine.dll" },
            "left4dead2.exe" => new[] { "bin/shaderapidx9.dll", "bin/engine.dll" },
            "portal2.exe" => new[] { "bin/shaderapidx9.dll", "bin/x64/shaderapidx9.dll" },
            "garrysmod.exe" => new[] { "bin/shaderapidx9.dll", "bin/win64/shaderapidx9.dll" },
            "xrengine.exe" => new[] { "xrRender_R4.dll", "xrRender_R3.dll", "xrRender_R2.dll", "xrRender_R1.dll" },
            "farcry5.exe" => new[] { "bin/FC_m64.dll" },
            "watch_dogs.exe" => new[] { "bin/Disrupt_b64.dll" },
            "kingdomcome.exe" => new[] { "WHGame.dll" },
            _ => Array.Empty<string>()
        };

        var root = Path.GetDirectoryName(executable)!;

        foreach (var relative in candidates)
        {
            var module = Path.Combine(
                root,
                relative.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(module))
                continue;

            var api = DetectApi(module, out _);
            if (api != null)
                return (api, relative);
        }

        return null;
    }

    private static string? DetectVulkanWrapper(string directory)
    {
        foreach (var name in new[]
                 {
                     "dxgi.dll", "d3d12.dll", "d3d11.dll",
                     "d3d10.dll", "d3d9.dll", "d3d8.dll"
                 })
        {
            var path = Path.Combine(directory, name);
            if (!File.Exists(path))
                continue;

            try
            {
                var info = FileVersionInfo.GetVersionInfo(path);
                var metadata =
                    $"{info.FileDescription} {info.ProductName} {info.CompanyName}";
                var markers = ReadMarkers(path);

                if (metadata.Contains("DXVK", StringComparison.OrdinalIgnoreCase) ||
                    markers.Contains("DXVK", StringComparison.OrdinalIgnoreCase))
                    return $"DXVK ({name})";

                if (metadata.Contains("vkd3d", StringComparison.OrdinalIgnoreCase) ||
                    markers.Contains("vkd3d", StringComparison.OrdinalIgnoreCase))
                    return $"vkd3d ({name})";
            }
            catch { }
        }

        return null;
    }

    private static string ReadMarkers(string path)
    {
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            var count = (int)Math.Min(stream.Length, MarkerReadLimit);
            var buffer = new byte[count];
            _ = stream.Read(buffer, 0, count);
            return Encoding.ASCII.GetString(buffer);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static IEnumerable<string> SafeFiles(
        string directory,
        string pattern)
    {
        try
        {
            return Directory.EnumerateFiles(
                    directory,
                    pattern,
                    SearchOption.TopDirectoryOnly)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static long SafeLength(string path)
    {
        try { return new FileInfo(path).Length; }
        catch { return 0; }
    }
}
