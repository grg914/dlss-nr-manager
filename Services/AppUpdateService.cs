using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace DlssNrManager.Services;

public sealed class AppUpdateService
{
    private readonly HttpClient _http = new();

    public AppUpdateService()
    {
        _http.Timeout = TimeSpan.FromMinutes(10);
        _http.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("DlssNrManager", "1.0"));
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

        progress?.Report($"Downloading DLSS NR Manager {release.Tag}…");

        using (var response = await _http.GetAsync(
                   release.AssetUrl,
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

        if (new FileInfo(temp).Length < 128 * 1024)
        {
            TryDelete(temp);
            throw new InvalidDataException(
                "The downloaded update package is unexpectedly small.");
        }

        if (!string.IsNullOrWhiteSpace(release.Sha256))
        {
            progress?.Report("Verifying update SHA-256…");

            await using var stream = File.OpenRead(temp);
            var actual = Convert.ToHexString(
                await SHA256.HashDataAsync(stream, cancellationToken));

            if (!actual.Equals(
                    release.Sha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(temp);
                throw new InvalidDataException(
                    $"Update SHA-256 mismatch. Expected {release.Sha256}, got {actual}.");
            }
        }

        File.Move(temp, downloadPath, true);

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

        var script = $"""
$ErrorActionPreference = 'Stop'
$pidToWait = {{Environment.ProcessId}}
$source = '{{stagedEscaped}}'
$target = '{{currentEscaped}}'
$self = '{{scriptEscaped}}'

try {
    Wait-Process -Id $pidToWait -ErrorAction SilentlyContinue

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

    Start-Process -FilePath $target
} finally {
    Start-Sleep -Milliseconds 500
    Remove-Item -LiteralPath $self -Force -ErrorAction SilentlyContinue
}
""";

        File.WriteAllText(scriptPath, script);

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

        Process.Start(startInfo)
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

        var normalizedRoot = Path.GetFullPath(extract)
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        using (var archive = ZipFile.OpenRead(zipPath))
        {
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Name))
                    continue;

                var target = Path.GetFullPath(Path.Combine(
                    extract,
                    entry.FullName.Replace(
                        '/',
                        Path.DirectorySeparatorChar)));

                if (!target.StartsWith(
                        normalizedRoot,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"Unsafe update archive entry: {entry.FullName}");
                }

                Directory.CreateDirectory(
                    Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: true);
            }
        }

        return Directory.EnumerateFiles(
                extract,
                "DlssNrManager.exe",
                SearchOption.AllDirectories)
            .FirstOrDefault()
            ?? Directory.EnumerateFiles(
                    extract,
                    "*.exe",
                    SearchOption.AllDirectories)
                .FirstOrDefault()
            ?? throw new InvalidDataException(
                "The update ZIP does not contain an executable.");
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
