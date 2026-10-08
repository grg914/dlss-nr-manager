using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;

namespace DlssNrManager.Services;

public sealed class AppUpdateService
{
    private const long MaxUpdateDownloadBytes = 768L * 1024 * 1024;
    private const long MaxUpdateExpandedBytes = 1024L * 1024 * 1024;
    private const int MaxUpdateArchiveEntries = 256;

    private readonly HttpClient _http = new();

    public AppUpdateService()
    {
        _http.Timeout = TimeSpan.FromMinutes(10);
        _http.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("DlssNrManager", AppIdentity.UserAgentVersion));
    }

    public async Task<string> DownloadAndStageAsync(
        ManagerReleaseInfo release,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath) || !File.Exists(processPath))
            throw new InvalidOperationException(
                "The running executable path could not be determined.");

        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DlssNrManager",
            "updates",
            release.Tag);

        Directory.CreateDirectory(root);

        var downloadPath = Path.Combine(root, release.AssetName);
        var temp = downloadPath + ".download";

        if (!Uri.TryCreate(
                release.AssetUrl,
                UriKind.Absolute,
                out var assetUri) ||
            !assetUri.Scheme.Equals(
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase) ||
            !assetUri.Host.Equals(
                "github.com",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Unexpected update asset URL: {release.AssetUrl}");
        }

        progress?.Report($"Downloading DLSS NR Manager {release.Tag}…");

        try
        {
            await NetworkRetry.ExecuteAsync(
                async (attempt, token) =>
                {
                    if (attempt > 1)
                    {
                        TryDelete(temp);
                        progress?.Report(
                            $"Retrying application update download ({attempt}/3)…");
                    }

                    using (var response = await _http.GetAsync(
                               assetUri,
                               HttpCompletionOption.ResponseHeadersRead,
                               token))
                    {
                        response.EnsureSuccessStatusCode();

                        if (response.Content.Headers.ContentLength is > MaxUpdateDownloadBytes)
                        {
                            throw new InvalidDataException(
                                "The update package exceeds the 768 MB safety limit.");
                        }

                        using var transfer =
                            DownloadProgressHub.Begin(
                                release.AssetName,
                                response.Content.Headers.ContentLength);

                        try
                        {
                            await using var input =
                                await response.Content.ReadAsStreamAsync(token);
                            await using var output = new FileStream(
                                temp,
                                FileMode.Create,
                                FileAccess.Write,
                                FileShare.None,
                                128 * 1024,
                                useAsync: true);

                            await CopyWithLimitAsync(
                                input,
                                output,
                                MaxUpdateDownloadBytes,
                                transfer,
                                token);

                            transfer.Complete();
                        }
                        catch (Exception ex)
                        {
                            transfer.Fail(ex);
                            throw;
                        }
                    }

                    if (new FileInfo(temp).Length < 128 * 1024)
                    {
                        throw new InvalidDataException(
                            "The downloaded update package is unexpectedly small.");
                    }

                    if (!string.IsNullOrWhiteSpace(release.Sha256))
                    {
                        progress?.Report("Verifying update SHA-256…");

                        var actual =
                            await HashService.Sha256Async(
                                temp,
                                token);

                        if (!actual.Equals(
                                release.Sha256,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidDataException(
                                $"Update SHA-256 mismatch. Expected {release.Sha256}, got {actual}.");
                        }
                    }
                },
                cancellationToken,
                attempts: 3);

            File.Move(temp, downloadPath, true);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }

        var stagedExe = release.AssetName.EndsWith(
                ".exe",
                StringComparison.OrdinalIgnoreCase)
            ? downloadPath
            : ExtractExecutableFromZip(downloadPath, root);

        ValidateStagedExecutable(stagedExe);

        progress?.Report(
            $"Update {release.Tag} downloaded and validated.");

        return stagedExe;
    }

    public void CleanupSuccessfulUpdateBackup()
    {
        var currentExecutable = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(currentExecutable))
            TryDelete(currentExecutable + ".update-backup");

        PruneOldUpdateCache();
    }

    private static void PruneOldUpdateCache()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DlssNrManager",
            "updates");

        if (!Directory.Exists(root))
            return;

        var directoryCutoff = DateTime.UtcNow - TimeSpan.FromMinutes(10);
        var scriptCutoff = DateTime.UtcNow - TimeSpan.FromDays(1);

        try
        {
            foreach (var directory in Directory.EnumerateDirectories(root))
            {
                try
                {
                    if (Directory.GetLastWriteTimeUtc(directory) < directoryCutoff)
                        Directory.Delete(directory, true);
                }
                catch { }
            }

            foreach (var script in Directory.EnumerateFiles(
                         root,
                         "apply-update-*.ps1",
                         SearchOption.TopDirectoryOnly))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(script) < scriptCutoff)
                        File.Delete(script);
                }
                catch { }
            }
        }
        catch { }
    }

    public void ApplyAndRestart(
        string stagedExecutable,
        Version expectedVersion)
    {
        var currentExecutable = Environment.ProcessPath
            ?? throw new InvalidOperationException(
                "The running executable path could not be determined.");

        var stagedVersion = FileVersionInfo.GetVersionInfo(
            stagedExecutable).FileVersion;

        if (!Version.TryParse(
                NormalizeVersion(stagedVersion),
                out var parsedVersion) ||
            parsedVersion.Major != expectedVersion.Major ||
            parsedVersion.Minor != expectedVersion.Minor ||
            parsedVersion.Build != expectedVersion.Build)
        {
            throw new InvalidDataException(
                $"Downloaded executable version '{stagedVersion ?? "unknown"}' does not match expected {expectedVersion}.");
        }

        var scriptDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DlssNrManager",
            "updates");

        Directory.CreateDirectory(scriptDirectory);

        var scriptPath = Path.Combine(
            scriptDirectory,
            "apply-update-" + Guid.NewGuid().ToString("N") + ".ps1");

        var currentEscaped = EscapePowerShell(currentExecutable);
        var stagedEscaped = EscapePowerShell(stagedExecutable);
        var scriptEscaped = EscapePowerShell(scriptPath);
        var backupEscaped = EscapePowerShell(
            currentExecutable + ".update-backup");

        var script = $$"""
$ErrorActionPreference = 'Stop'
$pidToWait = {{Environment.ProcessId}}
$source = '{{stagedEscaped}}'
$target = '{{currentEscaped}}'
$backup = '{{backupEscaped}}'
$self = '{{scriptEscaped}}'

try {
    Wait-Process -Id $pidToWait -ErrorAction SilentlyContinue

    if (Test-Path -LiteralPath $target) {
        Copy-Item -LiteralPath $target -Destination $backup -Force
    }

    $success = $false
    for ($i = 0; $i -lt 30; $i++) {
        try {
            Copy-Item -LiteralPath $source -Destination $target -Force
            $success = $true
            break
        } catch {
            Start-Sleep -Milliseconds 500
        }
    }

    if (-not $success) {
        throw 'Unable to replace the running executable after waiting for shutdown.'
    }

    try {
        Start-Process -FilePath 'ie4uinit.exe' -ArgumentList '-show' -WindowStyle Hidden
    } catch {}

    try {
        Start-Process -FilePath $target
    } catch {
        if (Test-Path -LiteralPath $backup) {
            Copy-Item -LiteralPath $backup -Destination $target -Force
            Start-Process -FilePath $target
        }

        throw
    }
} catch {
    if ((Test-Path -LiteralPath $backup) -and -not (Get-Process -Name 'DlssNrManager' -ErrorAction SilentlyContinue)) {
        try {
            Copy-Item -LiteralPath $backup -Destination $target -Force
        } catch {}
    }

    throw
} finally {
    Start-Sleep -Milliseconds 500
    Remove-Item -LiteralPath $self -Force -ErrorAction SilentlyContinue
}
""";

        AtomicFile.WriteAllText(scriptPath, script);

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);

        if (!CanWriteDirectory(
                Path.GetDirectoryName(currentExecutable)!))
        {
            startInfo.Verb = "runas";
        }

        _ = Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                "Could not launch the updater process.");
    }

    private static string ExtractExecutableFromZip(
        string zipPath,
        string root)
    {
        var extract = Path.Combine(
            root,
            "extracted-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(extract);

        // Reuse the same hardened extractor as component installs so update
        // archives get identical traversal, entry-count and expansion guards.
        SafeZip.Extract(
            zipPath,
            extract,
            MaxUpdateArchiveEntries,
            MaxUpdateExpandedBytes);

        return Directory.EnumerateFiles(
                extract,
                "DlssNrManager.exe",
                SearchOption.AllDirectories)
            .FirstOrDefault()
            ?? throw new InvalidDataException(
                "The update ZIP does not contain DlssNrManager.exe.");
    }

    private static async Task CopyWithLimitAsync(
        Stream input,
        Stream output,
        long maxBytes,
        DownloadProgressHandle progress,
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

    private static void ValidateStagedExecutable(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException(
                "The staged update executable was not found.",
                path);

        if (new FileInfo(path).Length < 512 * 1024)
            throw new InvalidDataException(
                "The staged update executable is unexpectedly small.");

        var info = FileVersionInfo.GetVersionInfo(path);
        if (string.IsNullOrWhiteSpace(info.FileVersion))
            throw new InvalidDataException(
                "The staged executable does not expose version metadata.");
    }

    private static bool CanWriteDirectory(string directory)
    {
        try
        {
            var probe = Path.Combine(
                directory,
                ".dlssnr-update-write-" + Guid.NewGuid().ToString("N") + ".tmp");

            File.WriteAllText(probe, "probe");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string EscapePowerShell(string value)
        => value.Replace("'", "''", StringComparison.Ordinal);

    private static string NormalizeVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "0.0.0";

        var numeric = new string(
            value.TakeWhile(ch =>
                char.IsDigit(ch) || ch == '.').ToArray());

        return string.IsNullOrWhiteSpace(numeric)
            ? "0.0.0"
            : numeric.TrimEnd('.');
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch { }
    }
}
