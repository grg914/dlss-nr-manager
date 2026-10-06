namespace DlssNrManager.Models;

public sealed record GpuInfo(string Name, string Generation, bool IsNvidia);
public sealed record ReleaseInfo(string Tag, string Name, bool Prerelease, string ZipUrl, string? ZipSha256 = null);

public sealed record InstallState(
    bool Installed,
    string? ProxyName,
    string? Version,
    bool RuntimePresent,
    string? RuntimeHash,
    bool RuntimeHashValid);

public sealed record RuntimeValidation(
    string Hash,
    bool HashValid,
    bool SignatureValid,
    string? Publisher,
    string? FileVersion,
    bool Is64Bit)
{
    public bool Trusted =>
        Is64Bit &&
        (
            HashValid ||
            (
                SignatureValid &&
                !string.IsNullOrWhiteSpace(Publisher) &&
                Publisher.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)
            )
        );
}

public sealed record DiagnosticResult(
    string Summary,
    IReadOnlyList<string> Lines,
    bool Ready,
    bool OptiScalerLoaded,
    bool DlssNrRunning);

public sealed record DetectedGame(
    string Name,
    string Platform,
    string InstallRoot,
    string TargetDirectory,
    string Confidence,
    string Evidence,
    string RecommendedProxy = "dxgi.dll",
    string? ArtworkUrl = null)
{
    public string DisplayName => $"{Name}  •  {Platform}  •  {Confidence}";
}

public sealed record InstallManifest(
    string Release,
    string Proxy,
    string GameExecutable,
    string GameExecutableHash,
    string RuntimeHash,
    DateTimeOffset InstalledAt,
    IReadOnlyList<string>? ManagedFiles = null,
    string? BaselineBackup = null,
    IReadOnlyDictionary<string, string>? ManagedFileHashes = null);
