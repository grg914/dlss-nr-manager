using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DlssNrManager.Services;

public enum PcUpdateKind
{
    Software,
    Windows,
    Driver,
    Firmware,
    BiosInfo
}

public sealed record PcUpdateItem(
    PcUpdateKind Kind,
    string Name,
    string CurrentVersion,
    string AvailableVersion,
    string Source,
    string ActionLabel,
    string? ActionValue,
    string Details);

public sealed record PcUpdateScanResult(
    DateTimeOffset ScannedAt,
    string ComputerManufacturer,
    string ComputerModel,
    string BiosVersion,
    string BiosReleaseDate,
    IReadOnlyList<PcUpdateItem> Items);

public sealed class PcUpdateService
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(30);

    public static string CachePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DlssNrManager",
        "pc-update-scan.json");

    public async Task<PcUpdateScanResult> ScanAsync(
        bool forceRefresh = false,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!forceRefresh)
        {
            var cached = LoadCache();
            if (cached != null && DateTimeOffset.UtcNow - cached.ScannedAt < CacheLifetime)
            {
                progress?.Report($"Loaded PC update scan from cache ({cached.ScannedAt.LocalDateTime:g}).");
                return cached;
            }
        }

        var items = new List<PcUpdateItem>();

        progress?.Report("Reading computer, motherboard and BIOS information…");
        var bios = await ScanBiosAsync(cancellationToken);

        items.Add(new PcUpdateItem(
            PcUpdateKind.BiosInfo,
            $"BIOS / UEFI — {bios.Manufacturer} {bios.Model}".Trim(),
            bios.Version,
            "",
            bios.BiosManufacturer,
            "Open official support",
            GetOfficialSupportUrl(
                $"{bios.Manufacturer} {bios.BiosManufacturer} {bios.BoardManufacturer}",
                bios.Manufacturer),
            $"Installed BIOS: {bios.Version} • Release date: {bios.ReleaseDate}. " +
            $"Motherboard: {bios.BoardManufacturer} {bios.BoardProduct}. " +
            "The app never flashes firmware."));

        progress?.Report("Scanning installed programs for available updates…");
        try
        {
            items.AddRange(await ScanSoftwareUpdatesAsync(cancellationToken));
        }
        catch (Exception ex)
        {
            items.Add(new PcUpdateItem(
                PcUpdateKind.Software,
                "Installed-program update scan unavailable",
                "",
                "",
                "Software inventory",
                "Open WinGet documentation",
                "https://learn.microsoft.com/windows/package-manager/winget/",
                ex.Message));
        }

        progress?.Report("Checking Windows, connected-device drivers and firmware…");
        try
        {
            var windowsItems = await ScanWindowsUpdateAsync(bios, cancellationToken);
            items.AddRange(windowsItems);
            items.Add(await ScanNvidiaDriverStatusAsync(windowsItems, cancellationToken));
        }
        catch (Exception ex)
        {
            items.Add(new PcUpdateItem(
                PcUpdateKind.Windows,
                "Windows Update scan unavailable",
                "",
                "",
                "Windows Update",
                "Open Windows Update",
                "ms-settings:windowsupdate",
                ex.Message));
        }

        var result = new PcUpdateScanResult(
            DateTimeOffset.UtcNow,
            bios.Manufacturer,
            bios.Model,
            bios.Version,
            bios.ReleaseDate,
            items
                .OrderBy(x => x.Kind)
                .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList());

        SaveCache(result);
        progress?.Report($"PC update scan complete • {result.Items.Count} entries.");
        return result;
    }

    public void ClearCache()
    {
        try
        {
            if (File.Exists(CachePath))
                File.Delete(CachePath);
        }
        catch { }
    }

    public void OpenAction(PcUpdateItem item)
    {
        var action = item.ActionValue;
        if (string.IsNullOrWhiteSpace(action))
            return;

        var isSettingsUri =
            action.StartsWith(
                "ms-settings:",
                StringComparison.OrdinalIgnoreCase);

        var isHttpsUri =
            Uri.TryCreate(action, UriKind.Absolute, out var uri) &&
            uri.Scheme.Equals(
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase);

        if (!isSettingsUri && !isHttpsUri)
        {
            throw new InvalidOperationException(
                "The cached update action is not a trusted HTTPS or Windows Settings target.");
        }

        _ = Process.Start(new ProcessStartInfo(action)
        {
            UseShellExecute = true
        }) ?? throw new InvalidOperationException(
            "Windows could not open the selected update action.");
    }

    // The default all-updates action must respect user-pinned packages and
    // never upgrade packages whose installed version is unknown.
    public static string[] BuildWingetUpgradeAllArguments() =>
    [
        "upgrade",
        "--all",
        "--accept-package-agreements",
        "--accept-source-agreements",
        "--disable-interactivity",
        "--nowarn"
    ];

    public async Task<string> UpdateAllWingetAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report("Updating applications with WinGet…");

        var result = await RunAsync(
            "winget.exe",
            BuildWingetUpgradeAllArguments(),
            cancellationToken);

        if (result.ExitCode != 0 &&
            !LooksLikeNoWingetUpdates(result.Output + "\n" + result.Error))
        {
            throw new InvalidOperationException(
                $"WinGet update failed with exit code {result.ExitCode}. {Tail(result.Error, 1500)}");
        }

        ClearCache();

        var output = (result.Output + "\n" + result.Error).Trim();
        return string.IsNullOrWhiteSpace(output)
            ? "WinGet update completed."
            : Tail(output, 5000);
    }

    private async Task<PcUpdateItem> ScanNvidiaDriverStatusAsync(
        IReadOnlyList<PcUpdateItem> windowsItems,
        CancellationToken cancellationToken)
    {
        string gpuName = "NVIDIA GPU";
        string installedVersion = "Unknown";

        try
        {
            var smi = await RunAsync(
                "nvidia-smi.exe",
                new[]
                {
                    "--query-gpu=name,driver_version",
                    "--format=csv,noheader"
                },
                cancellationToken);

            var line = smi.Output
                .Replace("\r", "")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(line))
            {
                var parts = line.Split(',', 2);
                if (parts.Length >= 1 && !string.IsNullOrWhiteSpace(parts[0]))
                    gpuName = parts[0].Trim();
                if (parts.Length >= 2 && !string.IsNullOrWhiteSpace(parts[1]))
                    installedVersion = parts[1].Trim();
            }
        }
        catch
        {
            // nvidia-smi may not be on PATH. Fall back to the registry.
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\NVIDIA Corporation\Installer");
                installedVersion = key?.GetValue("LastInstallerVersion")?.ToString()
                                   ?? "Unknown";
            }
            catch { }
        }

        var offered = windowsItems.FirstOrDefault(item =>
            item.Kind == PcUpdateKind.Driver &&
            (
                item.Name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
                item.Details.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)
            ));

        if (offered != null)
        {
            return new PcUpdateItem(
                PcUpdateKind.Driver,
                $"NVIDIA driver — {gpuName}",
                installedVersion,
                string.IsNullOrWhiteSpace(offered.AvailableVersion)
                    ? "Update available"
                    : offered.AvailableVersion,
                "Windows Update + NVIDIA",
                "Open NVIDIA drivers",
                "https://www.nvidia.com/Download/index.aspx",
                "A newer NVIDIA driver is currently offered through Windows Update. " +
                "For the newest Game Ready / Studio release, use NVIDIA's official driver page.");
        }

        return new PcUpdateItem(
            PcUpdateKind.Driver,
            $"NVIDIA driver — {gpuName}",
            installedVersion,
            "No newer NVIDIA driver offered by Windows Update",
            "NVIDIA",
            "Check official NVIDIA drivers",
            "https://www.nvidia.com/Download/index.aspx",
            "Windows Update does not currently advertise a newer NVIDIA display driver. " +
            "This does not guarantee you have NVIDIA's newest Game Ready / Studio branch; " +
            "open the official NVIDIA page for the definitive vendor check.");
    }

    private async Task<IReadOnlyList<PcUpdateItem>> ScanSoftwareUpdatesAsync(
        CancellationToken cancellationToken)
    {
        var updates = new List<PcUpdateItem>();

        // WinGet is the primary inventory because it can match traditional
        // uninstall-registry entries, MSIX/MS Store apps and winget packages
        // against current package manifests without installing anything.
        try
        {
            updates.AddRange(await ScanWingetAsync(cancellationToken));
        }
        catch (Exception ex)
        {
            updates.Add(new PcUpdateItem(
                PcUpdateKind.Software,
                "WinGet scan unavailable",
                "",
                "",
                "WinGet",
                "Open WinGet documentation",
                "https://learn.microsoft.com/windows/package-manager/winget/",
                ex.Message));
        }

        // Chocolatey is common on developer PCs and its 'outdated' command is
        // read-only. Include it when present, but never run 'choco upgrade'.
        try
        {
            updates.AddRange(await ScanChocolateyAsync(cancellationToken));
        }
        catch
        {
            // Chocolatey is optional; absence/failure should not fail the full scan.
        }

        return updates
            .GroupBy(
                x => $"{x.Source}|{NormalizeSoftwareKey(x.Name)}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private async Task<IReadOnlyList<PcUpdateItem>> ScanWingetAsync(
        CancellationToken cancellationToken)
    {
        // 'winget upgrade' without --all is a read-only query that lists
        // available updates. It is more comprehensive than relying on one
        // localized 'list' variant, and includes Store packages when matched.
        var primary = await RunAsync(
            "winget.exe",
            new[]
            {
                "upgrade",
                "--include-unknown",
                "--include-pinned",
                "--accept-source-agreements",
                "--disable-interactivity",
                "--nowarn"
            },
            cancellationToken);

        var parsed = ParseWingetUpgradeTable(primary.Output).ToList();

        // Some WinGet versions expose additional matched programs through
        // 'list --upgrade-available'. Query both and merge by package ID/name.
        try
        {
            var secondary = await RunAsync(
                "winget.exe",
                new[]
                {
                    "list",
                    "--upgrade-available",
                    "--include-unknown",
                    "--accept-source-agreements",
                    "--disable-interactivity",
                    "--nowarn"
                },
                cancellationToken);

            parsed.AddRange(ParseWingetUpgradeTable(secondary.Output));
        }
        catch
        {
            // The primary query is sufficient on clients that do not support
            // every list option.
        }

        if (primary.ExitCode != 0 && parsed.Count == 0)
        {
            var combined = $"{primary.Output}\n{primary.Error}";
            if (!LooksLikeNoWingetUpdates(combined))
            {
                throw new InvalidOperationException(
                    $"WinGet returned exit code {primary.ExitCode}. {Tail(primary.Error, 1000)}");
            }
        }

        return parsed
            .GroupBy(
                x => NormalizeSoftwareKey($"{x.Name}|{x.CurrentVersion}|{x.AvailableVersion}"),
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private static IReadOnlyList<PcUpdateItem> ParseWingetUpgradeTable(string output)
    {
        // Strip VT/ANSI progress sequences and other terminal control data before
        // reading the fixed-width table. Column separators are language-neutral,
        // so this works with French/English/German Windows.
        output = Regex.Replace(output, @"\x1B\[[0-?]*[ -/]*[@-~]", string.Empty);

        var lines = output
            .Replace("\r", "")
            .Split('\n')
            .Select(line => line.TrimEnd())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();

        var separatorIndex = lines.FindIndex(line =>
            Regex.IsMatch(line, @"^\s*-{3,}(\s+-{3,}){2,}\s*$"));

        if (separatorIndex < 0 || separatorIndex + 1 >= lines.Count)
            return [];

        var columns = Regex.Matches(lines[separatorIndex], @"-+")
            .Select(match => (Start: match.Index, Width: match.Length))
            .ToList();

        if (columns.Count < 4)
            return [];

        static string Slice(string line, int start, int width)
        {
            if (start >= line.Length)
                return "";

            var length = Math.Min(width, line.Length - start);
            return line.Substring(start, length).Trim();
        }

        var updates = new List<PcUpdateItem>();

        foreach (var raw in lines.Skip(separatorIndex + 1))
        {
            var trimmed = raw.Trim();
            if (trimmed.Length == 0 ||
                trimmed.StartsWith("-", StringComparison.Ordinal) ||
                trimmed.Contains("upgrade available", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("mise", StringComparison.OrdinalIgnoreCase) &&
                !raw.Contains("  "))
                continue;

            var values = columns
                .Select(column => Slice(raw, column.Start, column.Width))
                .ToList();

            if (values.Count < 4)
                continue;

            var name = values[0];
            var id = values[1];
            var installed = values[2];
            var available = values[3];
            var source = values.Count >= 5 ? values[4] : "winget";

            if (string.IsNullOrWhiteSpace(name) ||
                string.IsNullOrWhiteSpace(id) ||
                string.IsNullOrWhiteSpace(available) ||
                available.Equals("Available", StringComparison.OrdinalIgnoreCase) ||
                available.Equals("Disponible", StringComparison.OrdinalIgnoreCase))
                continue;

            var officialPage = GetSoftwareOfficialUrl(id, name, source);

            updates.Add(new PcUpdateItem(
                PcUpdateKind.Software,
                name,
                installed,
                available,
                string.IsNullOrWhiteSpace(source) ? "WinGet" : source,
                "Open official/source page",
                officialPage,
                $"Package ID: {id}. Read-only detection; DLSS NR Manager does not run winget upgrade."));
        }

        return updates;
    }

    private async Task<IReadOnlyList<PcUpdateItem>> ScanChocolateyAsync(
        CancellationToken cancellationToken)
    {
        var result = await RunAsync(
            "choco.exe",
            new[]
            {
                "outdated",
                "--limit-output",
                "--no-color"
            },
            cancellationToken);

        if (result.ExitCode is not 0 and not 2)
            return [];

        var updates = new List<PcUpdateItem>();

        foreach (var raw in result.Output.Replace("\r", "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("Chocolatey", StringComparison.OrdinalIgnoreCase))
                continue;

            var parts = line.Split('|');
            if (parts.Length < 3)
                continue;

            var name = parts[0].Trim();
            var current = parts[1].Trim();
            var available = parts[2].Trim();

            if (string.IsNullOrWhiteSpace(name) ||
                string.IsNullOrWhiteSpace(available))
                continue;

            updates.Add(new PcUpdateItem(
                PcUpdateKind.Software,
                name,
                current,
                available,
                "Chocolatey",
                "Open Chocolatey package page",
                $"https://community.chocolatey.org/packages/{Uri.EscapeDataString(name)}",
                "Detected with 'choco outdated'. Read-only detection; DLSS NR Manager does not run choco upgrade."));
        }

        return updates;
    }

    private static bool LooksLikeNoWingetUpdates(string value)
        => value.Contains("No applicable upgrade", StringComparison.OrdinalIgnoreCase)
           || value.Contains("No installed package", StringComparison.OrdinalIgnoreCase)
           || value.Contains("No available upgrade", StringComparison.OrdinalIgnoreCase)
           || value.Contains("Aucune mise", StringComparison.OrdinalIgnoreCase)
           || value.Contains("Aucun package", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeSoftwareKey(string value)
        => Regex.Replace(
            value.ToLowerInvariant(),
            @"[^a-z0-9]+",
            string.Empty,
            RegexOptions.CultureInvariant);

    private async Task<IReadOnlyList<PcUpdateItem>> ScanWindowsUpdateAsync(
        BiosInfo bios,
        CancellationToken cancellationToken)
    {
        const string script = """
$ErrorActionPreference = 'Stop'
$session = New-Object -ComObject Microsoft.Update.Session
$searcher = $session.CreateUpdateSearcher()
$result = $searcher.Search("IsInstalled=0 and IsHidden=0")
$items = @()
foreach ($u in $result.Updates) {
    $categories = @($u.Categories | ForEach-Object { $_.Name })
    $kb = @($u.KBArticleIDs)

    $driverClass = ''
    $driverHardwareId = ''
    $driverManufacturer = ''
    $driverModel = ''
    $driverProvider = ''
    $driverVersion = ''
    $driverVerDate = ''

    if ([int]$u.Type -eq 2) {
        try { $driverClass = [string]$u.DriverClass } catch {}
        try { $driverHardwareId = [string]$u.DriverHardwareID } catch {}
        try { $driverManufacturer = [string]$u.DriverManufacturer } catch {}
        try { $driverModel = [string]$u.DriverModel } catch {}
        try { $driverProvider = [string]$u.DriverProvider } catch {}
        try { $driverVersion = [string]$u.DriverVersion } catch {}
        try {
            if ($u.DriverVerDate) { $driverVerDate = $u.DriverVerDate.ToString('yyyy-MM-dd') }
        } catch {}
    }

    $items += [pscustomobject]@{
        Title = [string]$u.Title
        Type = [int]$u.Type
        Categories = $categories
        KB = $kb
        RebootRequired = [bool]$u.RebootRequired
        DriverClass = $driverClass
        DriverHardwareID = $driverHardwareId
        DriverManufacturer = $driverManufacturer
        DriverModel = $driverModel
        DriverProvider = $driverProvider
        DriverVersion = $driverVersion
        DriverVerDate = $driverVerDate
    }
}
$items | ConvertTo-Json -Depth 6 -Compress
""";

        var result = await RunPowerShellAsync(script, cancellationToken);
        if (result.ExitCode != 0)
            throw new InvalidOperationException(
                $"Windows Update Agent failed. {Tail(result.Error, 1000)}");

        if (string.IsNullOrWhiteSpace(result.Output))
            return [];

        using var document = JsonDocument.Parse(result.Output.Trim());
        var root = document.RootElement;

        var entries = root.ValueKind == JsonValueKind.Array
            ? root.EnumerateArray().ToList()
            : root.ValueKind == JsonValueKind.Object
                ? [root]
                : [];

        var updates = new List<PcUpdateItem>();

        foreach (var entry in entries)
        {
            var title = GetString(entry, "Title") ?? "Windows update";
            var type = GetInt(entry, "Type");
            var categories = ReadStringArray(entry, "Categories");
            var kb = ReadStringArray(entry, "KB");
            var reboot = GetBool(entry, "RebootRequired");

            var driverClass = GetString(entry, "DriverClass") ?? "";
            var driverHardwareId = GetString(entry, "DriverHardwareID") ?? "";
            var driverManufacturer = GetString(entry, "DriverManufacturer") ?? "";
            var driverModel = GetString(entry, "DriverModel") ?? "";
            var driverProvider = GetString(entry, "DriverProvider") ?? "";
            var driverVersion = GetString(entry, "DriverVersion") ?? "";
            var driverVerDate = GetString(entry, "DriverVerDate") ?? "";

            var joinedCategories = string.Join(", ", categories);
            var isFirmware =
                categories.Any(x => x.Contains("Firmware", StringComparison.OrdinalIgnoreCase)) ||
                title.Contains("Firmware", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("BIOS", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("UEFI", StringComparison.OrdinalIgnoreCase);

            var isDriver =
                type == 2 ||
                categories.Any(x => x.Contains("Driver", StringComparison.OrdinalIgnoreCase));

            var kind = isFirmware
                ? PcUpdateKind.Firmware
                : isDriver
                    ? PcUpdateKind.Driver
                    : PcUpdateKind.Windows;

            string actionLabel;
            string actionValue;

            if (kind is PcUpdateKind.Driver or PcUpdateKind.Firmware)
            {
                actionLabel = "Open official support";
                actionValue = GetOfficialSupportUrl(
                    $"{driverManufacturer} {driverProvider} {driverModel} {title}",
                    $"{bios.Manufacturer} {bios.BoardManufacturer}");
            }
            else
            {
                actionLabel = "Open Windows Update";
                actionValue = "ms-settings:windowsupdate";
            }

            var availableVersion = !string.IsNullOrWhiteSpace(driverVersion)
                ? driverVersion
                : string.Join(", ", kb.Select(x => $"KB{x}"));

            var driverDetails = kind == PcUpdateKind.Driver
                ? string.Join(" • ", new[]
                {
                    driverManufacturer,
                    driverProvider,
                    driverModel,
                    driverClass,
                    string.IsNullOrWhiteSpace(driverVerDate) ? "" : $"Driver date {driverVerDate}",
                    string.IsNullOrWhiteSpace(driverHardwareId) ? "" : $"Hardware ID {driverHardwareId}"
                }.Where(x => !string.IsNullOrWhiteSpace(x)))
                : joinedCategories;

            updates.Add(new PcUpdateItem(
                kind,
                string.IsNullOrWhiteSpace(driverModel) ? title : driverModel,
                "",
                availableVersion,
                "Windows Update",
                actionLabel,
                actionValue,
                $"{driverDetails}{(reboot ? " • Restart may be required" : "")}".Trim()));
        }

        return updates;
    }

    private async Task<BiosInfo> ScanBiosAsync(CancellationToken cancellationToken)
    {
        const string script = """
$ErrorActionPreference = 'Stop'
$bios = Get-CimInstance Win32_BIOS
$system = Get-CimInstance Win32_ComputerSystem
$board = Get-CimInstance Win32_BaseBoard
[pscustomobject]@{
    Manufacturer = [string]$system.Manufacturer
    Model = [string]$system.Model
    BiosManufacturer = [string]$bios.Manufacturer
    Version = [string]$bios.SMBIOSBIOSVersion
    ReleaseDate = if ($bios.ReleaseDate) { $bios.ReleaseDate.ToString('yyyy-MM-dd') } else { '' }
    BoardManufacturer = [string]$board.Manufacturer
    BoardProduct = [string]$board.Product
} | ConvertTo-Json -Compress
""";

        var result = await RunPowerShellAsync(script, cancellationToken);
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Output))
            return new BiosInfo("Unknown", "Unknown", "Unknown", "Unknown", "", "", "");

        using var document = JsonDocument.Parse(result.Output.Trim());
        var root = document.RootElement;

        return new BiosInfo(
            GetString(root, "Manufacturer") ?? "Unknown",
            GetString(root, "Model") ?? "Unknown",
            GetString(root, "BiosManufacturer") ?? "Unknown",
            GetString(root, "Version") ?? "Unknown",
            GetString(root, "ReleaseDate") ?? "",
            GetString(root, "BoardManufacturer") ?? "",
            GetString(root, "BoardProduct") ?? "");
    }

    private static string GetOfficialSupportUrl(string vendorText, string fallbackVendor)
    {
        var value = $"{vendorText} {fallbackVendor}".ToLowerInvariant();

        if (value.Contains("nvidia"))
            return "https://www.nvidia.com/Download/index.aspx";
        if (value.Contains("advanced micro devices") || value.Contains(" amd") || value.StartsWith("amd"))
            return "https://www.amd.com/en/support/download/drivers.html";
        if (value.Contains("intel"))
            return "https://www.intel.com/content/www/us/en/download-center/home.html";
        if (value.Contains("realtek"))
            return "https://www.realtek.com/Download/Index";
        if (value.Contains("dell"))
            return "https://www.dell.com/support/home";
        if (value.Contains("hewlett") || value.Contains(" hp"))
            return "https://support.hp.com/drivers";
        if (value.Contains("lenovo"))
            return "https://pcsupport.lenovo.com/";
        if (value.Contains("asus"))
            return "https://www.asus.com/support/download-center/";
        if (value.Contains("micro-star") || value.Contains("msi"))
            return "https://www.msi.com/support/download";
        if (value.Contains("gigabyte"))
            return "https://www.gigabyte.com/Support";
        if (value.Contains("acer"))
            return "https://www.acer.com/support/drivers-and-manuals";
        if (value.Contains("microsoft"))
            return "https://support.microsoft.com/surface";
        if (value.Contains("logitech"))
            return "https://support.logi.com/";
        if (value.Contains("corsair"))
            return "https://www.corsair.com/us/en/s/downloads";
        if (value.Contains("razer"))
            return "https://mysupport.razer.com/";
        if (value.Contains("brother"))
            return "https://support.brother.com/";
        if (value.Contains("epson"))
            return "https://epson.com/Support";
        if (value.Contains("canon"))
            return "https://www.usa.canon.com/support";

        return "ms-settings:windowsupdate-optionalupdates";
    }

    private static string GetSoftwareOfficialUrl(string packageId, string name, string source)
    {
        if (source.Contains("msstore", StringComparison.OrdinalIgnoreCase))
            return $"ms-windows-store://pdp/?ProductId={Uri.EscapeDataString(packageId)}";

        var value = $"{packageId} {name}".ToLowerInvariant();
        if (value.Contains("nvidia"))
            return "https://www.nvidia.com/Download/index.aspx";
        if (value.Contains("amd"))
            return "https://www.amd.com/en/support/download/drivers.html";
        if (value.Contains("intel"))
            return "https://www.intel.com/content/www/us/en/download-center/home.html";
        if (value.Contains("mozilla") || value.Contains("firefox"))
            return "https://www.mozilla.org/firefox/new/";
        if (value.Contains("google.chrome") || value.Contains("chrome"))
            return "https://www.google.com/chrome/";
        if (value.Contains("microsoft.edge") || value.Contains("edge"))
            return "https://www.microsoft.com/edge/download";
        if (value.Contains("discord"))
            return "https://discord.com/download";
        if (value.Contains("valve.steam") || value.Contains("steam"))
            return "https://store.steampowered.com/about/";
        if (value.Contains("epicgames"))
            return "https://store.epicgames.com/download";
        if (value.Contains("7zip"))
            return "https://www.7-zip.org/download.html";
        if (value.Contains("videolan") || value.Contains("vlc"))
            return "https://www.videolan.org/vlc/";

        return $"https://github.com/microsoft/winget-pkgs/search?q={Uri.EscapeDataString(packageId)}&type=code";
    }

    private static async Task<ProcessResult> RunPowerShellAsync(
        string script,
        CancellationToken cancellationToken)
    {
        const string utf8Bootstrap =
            "$utf8 = New-Object System.Text.UTF8Encoding($false); " +
            "[Console]::OutputEncoding = $utf8; " +
            "$OutputEncoding = $utf8; " +
            "[Console]::InputEncoding = $utf8; ";

        script = utf8Bootstrap + script;
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

        return await RunAsync(
            "powershell.exe",
            new[]
            {
                "-NoProfile",
                "-NonInteractive",
                "-ExecutionPolicy", "Bypass",
                "-EncodedCommand", encoded
            },
            cancellationToken);
    }

    private static async Task<ProcessResult> RunAsync(
        string executable,
        IEnumerable<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = ExternalProcessTracker.Start(startInfo);

        try
        {
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

            await process.WaitForExitAsync(cancellationToken);

            return new ProcessResult(
                process.ExitCode,
                await outputTask,
                await errorTask);
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

    private static PcUpdateScanResult? LoadCache()
    {
        try
        {
            if (!File.Exists(CachePath))
                return null;

            var json = File.ReadAllText(CachePath);

            // A previous build decoded Windows PowerShell output with the wrong
            // code page. Never reuse that cache: rescan instead.
            if (json.Contains('\uFFFD') ||
                json.Contains("\\uFFFD", StringComparison.OrdinalIgnoreCase) ||
                json.Contains("Ã", StringComparison.Ordinal) ||
                json.Contains("â€", StringComparison.Ordinal))
                return null;

            return JsonSerializer.Deserialize<PcUpdateScanResult>(json);
        }
        catch
        {
            return null;
        }
    }

    private static void SaveCache(PcUpdateScanResult result)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
            AtomicFile.WriteAllText(
                CachePath,
                JsonSerializer.Serialize(
                    result,
                    new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    private static string? GetString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int GetInt(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.TryGetInt32(out var result)
            ? result
            : 0;

    private static bool GetBool(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) &&
           value.ValueKind is JsonValueKind.True or JsonValueKind.False &&
           value.GetBoolean();

    private static IReadOnlyList<string> ReadStringArray(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
            return [];

        if (value.ValueKind == JsonValueKind.Array)
            return value.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!)
                .ToList();

        if (value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()))
            return [value.GetString()!];

        return [];
    }

    private static string Tail(string value, int max)
        => string.IsNullOrWhiteSpace(value)
            ? ""
            : value.Length <= max
                ? value.Trim()
                : value[^max..].Trim();

    private sealed record BiosInfo(
        string Manufacturer,
        string Model,
        string BiosManufacturer,
        string Version,
        string ReleaseDate,
        string BoardManufacturer,
        string BoardProduct);

    private sealed record ProcessResult(int ExitCode, string Output, string Error);
}
