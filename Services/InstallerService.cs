using System.Diagnostics;
using System.IO.Compression;
using DlssNrManager.Models;
namespace DlssNrManager.Services;
public sealed class InstallerService
{
    public static readonly string Rtx50Hash = "E16BCF15E16E13F527491CDF7845B2FE6521A738D8F7C9C721866A8496E1FC8E";
    public static readonly string Rtx2040Hash = "E67DEE209320CDAFE0E93E45675D7AA34323A53ACC57A72B2E40A181581C989A";
    private static readonly string[] ProxyNames = ["dbghelp.dll","dxgi.dll","d3d12.dll","winmm.dll","version.dll","wininet.dll","winhttp.dll"];

    public InstallState Inspect(string gameDir, string gpuGeneration)
    {
        var managedProxy = ReadManagedProxy(gameDir);
        string? proxy = null;

        if (!string.IsNullOrWhiteSpace(managedProxy) &&
            ProxyNames.Contains(managedProxy, StringComparer.OrdinalIgnoreCase) &&
            File.Exists(Path.Combine(gameDir, managedProxy)))
        {
            proxy = managedProxy;
        }

        proxy ??= ProxyNames.FirstOrDefault(p => IsOptiScaler(Path.Combine(gameDir, p)));

        var runtime = Path.Combine(gameDir, "nvngx_dlssnr.dll");
        string? hash = null;
        if (File.Exists(runtime))
            hash = HashService.Sha256Async(runtime).GetAwaiter().GetResult();

        var expected = gpuGeneration == "RTX 50"
            ? Rtx50Hash
            : gpuGeneration is "RTX 20" or "RTX 30" or "RTX 40"
                ? Rtx2040Hash
                : null;

        var ini = Path.Combine(gameDir, "OptiScaler.ini");
        var forwarder = Path.Combine(gameDir, "nvngx.dll_dlssnr.dll");

        var installed = proxy != null &&
                        File.Exists(ini) &&
                        File.Exists(runtime) &&
                        File.Exists(forwarder);

        return new(
            installed,
            proxy,
            ReadInstalledVersion(gameDir),
            File.Exists(runtime),
            hash,
            expected != null && hash?.Equals(expected, StringComparison.OrdinalIgnoreCase) == true);
    }

    public async Task<string> InstallAsync(string gameDir, string runtimePath, GpuInfo gpu, ReleaseInfo release, string proxy, string workingScale, GitHubReleaseService releases)
    {
        if (!Directory.Exists(gameDir) || !Directory.EnumerateFiles(gameDir, "*.exe", SearchOption.TopDirectoryOnly).Any())
            throw new InvalidOperationException("No game executable was found in the selected target folder.");
        var expected = gpu.Generation == "RTX 50" ? Rtx50Hash : gpu.Generation is "RTX 20" or "RTX 30" or "RTX 40" ? Rtx2040Hash : null;
        if (expected == null) throw new InvalidOperationException("Unsupported or undetected NVIDIA RTX generation.");
        var runtimeHash = await HashService.Sha256Async(runtimePath);
        if (!runtimeHash.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"DLSSNR runtime SHA-256 mismatch. Expected {expected}, got {runtimeHash}.");

        var proxyPath = Path.Combine(gameDir, proxy);
        if (File.Exists(proxyPath) && !IsOptiScaler(proxyPath))
            throw new InvalidOperationException($"{proxy} already exists and is not identified as OptiScaler. Choose another proxy or resolve loader chaining first.");

        var backup = CreateBackup(gameDir);
        var temp = Path.Combine(Path.GetTempPath(), "DlssNrManager", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var zip = Path.Combine(temp, "optiscaler.zip");
        await releases.DownloadAsync(release.ZipUrl, zip);
        var extract = Path.Combine(temp, "extract");
        ZipFile.ExtractToDirectory(zip, extract);

        var sourceRoot = Directory.GetFiles(extract, "OptiScaler.dll", SearchOption.AllDirectories).Select(Path.GetDirectoryName).FirstOrDefault();
        if (sourceRoot == null) throw new InvalidDataException("Release archive does not contain OptiScaler.dll.");
        CopyTree(sourceRoot, gameDir, overwrite:true);
        var optiDll = Path.Combine(gameDir, "OptiScaler.dll");
        File.Copy(optiDll, proxyPath, true);
        File.Delete(optiDll);
        File.Copy(runtimePath, Path.Combine(gameDir, "nvngx_dlssnr.dll"), true);
        IniService.ApplyPreset(Path.Combine(gameDir, "OptiScaler.ini"), workingScale);
        File.WriteAllText(Path.Combine(gameDir, ".dlssnr-manager-version"), release.Tag);
        File.WriteAllText(Path.Combine(gameDir, ".dlssnr-manager-proxy"), proxy);
        Directory.Delete(temp, true);
        return backup;
    }

    public void ApplyPreset(string gameDir, string workingScale)
    {
        var ini = Path.Combine(gameDir, "OptiScaler.ini");
        if (!File.Exists(ini))
            throw new InvalidOperationException("OptiScaler.ini was not found for the selected game.");

        IniService.ApplyPreset(ini, workingScale);
    }

    public string CreateBackup(string gameDir)
    {
        var root = Path.Combine(gameDir, ".dlssnr-manager-backups");
        Directory.CreateDirectory(root);
        var folder = Path.Combine(root, DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(folder);
        foreach (var name in ProxyNames.Concat(new[]{"OptiScaler.ini","OptiScaler.log","nvngx_dlssnr.dll","nvngx.dll_dlssnr.dll",".dlssnr-manager-version",".dlssnr-manager-proxy"}))
        {
            var src=Path.Combine(gameDir,name); if (File.Exists(src)) File.Copy(src,Path.Combine(folder,name),true);
        }
        var opti = Path.Combine(gameDir,"OptiScaler");
        if (Directory.Exists(opti)) CopyTree(opti,Path.Combine(folder,"OptiScaler"),true);
        return folder;
    }

    public void RestoreLatest(string gameDir)
    {
        var root=Path.Combine(gameDir,".dlssnr-manager-backups");
        var latest=Directory.Exists(root)?Directory.GetDirectories(root).OrderByDescending(x=>x).FirstOrDefault():null;
        if (latest==null) throw new InvalidOperationException("No backup available.");
        Uninstall(gameDir, preserveBackups:true);
        CopyTree(latest, gameDir, true);
    }

    public void Uninstall(string gameDir, bool preserveBackups=true)
    {
        foreach(var p in ProxyNames)
        {
            var f=Path.Combine(gameDir,p); if(File.Exists(f)&&IsOptiScaler(f)) File.Delete(f);
        }
        foreach(var name in new[]{"OptiScaler.ini","OptiScaler.log","nvngx_dlssnr.dll","nvngx.dll_dlssnr.dll",".dlssnr-manager-version",".dlssnr-manager-proxy"})
        { var f=Path.Combine(gameDir,name); if(File.Exists(f)) File.Delete(f); }
        var opti=Path.Combine(gameDir,"OptiScaler"); if(Directory.Exists(opti)) Directory.Delete(opti,true);
        if(!preserveBackups){var b=Path.Combine(gameDir,".dlssnr-manager-backups");if(Directory.Exists(b))Directory.Delete(b,true);}
    }

    public string ReadLog(string gameDir)
    {
        var p=Path.Combine(gameDir,"OptiScaler.log");
        return File.Exists(p)?File.ReadAllText(p):"No OptiScaler.log found yet.";
    }

    private static string? ReadInstalledVersion(string gameDir)
    {
        var p=Path.Combine(gameDir,".dlssnr-manager-version");
        return File.Exists(p)?File.ReadAllText(p).Trim():null;
    }

    private static string? ReadManagedProxy(string gameDir)
    {
        var p = Path.Combine(gameDir, ".dlssnr-manager-proxy");
        return File.Exists(p) ? File.ReadAllText(p).Trim() : null;
    }

    private static bool IsOptiScaler(string path)
    {
        if(!File.Exists(path)) return false;
        try
        {
            var info=FileVersionInfo.GetVersionInfo(path);
            return string.Equals(info.OriginalFilename,"OptiScaler.dll",StringComparison.OrdinalIgnoreCase) ||
                   (info.FileDescription?.Contains("OptiScaler",StringComparison.OrdinalIgnoreCase) ?? false);
        } catch { return false; }
    }

    private static void CopyTree(string source,string dest,bool overwrite)
    {
        Directory.CreateDirectory(dest);
        foreach(var file in Directory.GetFiles(source)) File.Copy(file,Path.Combine(dest,Path.GetFileName(file)),overwrite);
        foreach(var dir in Directory.GetDirectories(source)) CopyTree(dir,Path.Combine(dest,Path.GetFileName(dir)),overwrite);
    }
}