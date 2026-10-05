namespace DlssNrManager.Models;
public sealed record GpuInfo(string Name, string Generation, bool IsNvidia);
public sealed record ReleaseInfo(string Tag, string Name, bool Prerelease, string ZipUrl);
public sealed record InstallState(bool Installed, string? ProxyName, string? Version, bool RuntimePresent, string? RuntimeHash, bool RuntimeHashValid);