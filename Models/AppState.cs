namespace DlssNrManager.Models;
public sealed record GpuInfo(string Name, string Generation, bool IsNvidia);
public sealed record ReleaseInfo(string Tag, string Name, bool Prerelease, string ZipUrl);
public sealed record InstallState(bool Installed, string? ProxyName, string? Version, bool RuntimePresent, string? RuntimeHash, bool RuntimeHashValid);
public sealed record DetectedGame(
    string Name,
    string Platform,
    string InstallRoot,
    string TargetDirectory,
    string Confidence,
    string Evidence,
    string RecommendedProxy = "dxgi.dll")
{
    public string DisplayName => $"{Name}  •  {Platform}  •  {Confidence}";
}
