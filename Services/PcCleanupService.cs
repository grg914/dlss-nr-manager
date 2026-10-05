namespace DlssNrManager.Services;

public sealed class PcCleanupItem
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required IReadOnlyList<string> Paths { get; init; }
    public bool IsSelected { get; set; } = true;
    public long Bytes { get; set; }
    public int FileCount { get; set; }
    public int SkippedCount { get; set; }

    public string SizeText => PcCleanupService.FormatBytes(Bytes);
    public string Details =>
        FileCount == 0 && SkippedCount == 0
            ? Description
            : $"{Description} • {FileCount:N0} files • {SizeText}" +
              (SkippedCount > 0 ? $" • {SkippedCount:N0} inaccessible" : "");
}

public sealed record PcCleanupResult(
    long DeletedBytes,
    int DeletedFiles,
    int SkippedFiles);

public sealed class PcCleanupService
{
    public IReadOnlyList<PcCleanupItem> CreateDefaultItems()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

        return new List<PcCleanupItem>
        {
            new()
            {
                Id = "user-temp",
                Name = "User temporary files",
                Description = "Temporary files for the current Windows account",
                Paths = [Path.GetTempPath()]
            },
            new()
            {
                Id = "windows-temp",
                Name = "Windows temporary files",
                Description = "System temporary files; locked/in-use files are skipped",
                Paths = [Path.Combine(windows, "Temp")]
            },
            new()
            {
                Id = "directx-shader",
                Name = "DirectX shader cache",
                Description = "Windows Direct3D shader cache; games rebuild it as needed",
                Paths = [Path.Combine(local, "D3DSCache")]
            },
            new()
            {
                Id = "nvidia-dx",
                Name = "NVIDIA DirectX shader cache",
                Description = "NVIDIA DXCache; shaders are rebuilt after cleanup",
                Paths = [Path.Combine(local, "NVIDIA", "DXCache")]
            },
            new()
            {
                Id = "nvidia-gl",
                Name = "NVIDIA OpenGL/Vulkan cache",
                Description = "NVIDIA GLCache; shaders are rebuilt after cleanup",
                Paths = [Path.Combine(local, "NVIDIA", "GLCache")]
            },
            new()
            {
                Id = "nvidia-nv-cache",
                Name = "NVIDIA legacy shader cache",
                Description = "Known NVIDIA NV_Cache locations",
                Paths =
                [
                    Path.Combine(local, "NVIDIA Corporation", "NV_Cache"),
                    Path.Combine(programData, "NVIDIA Corporation", "NV_Cache")
                ]
            }
        };
    }

    public Task<IReadOnlyList<PcCleanupItem>> AnalyzeAsync(
        IReadOnlyList<PcCleanupItem> items,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
        => Task.Run<IReadOnlyList<PcCleanupItem>>(
            () =>
            {
                foreach (var item in items)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    progress?.Report($"Analyzing {item.Name}…");

                    long bytes = 0;
                    var files = 0;
                    var skipped = 0;

                    foreach (var root in item.Paths.Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        if (!IsAllowedRoot(root))
                        {
                            skipped++;
                            continue;
                        }

                        ScanRoot(root, ref bytes, ref files, ref skipped, cancellationToken);
                    }

                    item.Bytes = bytes;
                    item.FileCount = files;
                    item.SkippedCount = skipped;
                }

                return items;
            },
            cancellationToken);

    public Task<PcCleanupResult> CleanAsync(
        IReadOnlyList<PcCleanupItem> items,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
        => Task.Run(
            () =>
            {
                long deletedBytes = 0;
                var deletedFiles = 0;
                var skippedFiles = 0;

                foreach (var item in items.Where(x => x.IsSelected))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    progress?.Report($"Cleaning {item.Name}…");

                    foreach (var root in item.Paths.Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        if (!IsAllowedRoot(root))
                        {
                            skippedFiles++;
                            continue;
                        }

                        CleanRoot(
                            root,
                            ref deletedBytes,
                            ref deletedFiles,
                            ref skippedFiles,
                            cancellationToken);
                    }
                }

                return new PcCleanupResult(
                    deletedBytes,
                    deletedFiles,
                    skippedFiles);
            },
            cancellationToken);

    private static void ScanRoot(
        string root,
        ref long bytes,
        ref int files,
        ref int skipped,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(root))
            return;

        foreach (var file in EnumerateFilesSafe(root, ref skipped, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var info = new FileInfo(file);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                    continue;

                bytes += Math.Max(0, info.Length);
                files++;
            }
            catch
            {
                skipped++;
            }
        }
    }

    private static void CleanRoot(
        string root,
        ref long deletedBytes,
        ref int deletedFiles,
        ref int skippedFiles,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(root))
            return;

        var files = EnumerateFilesSafe(root, ref skippedFiles, cancellationToken).ToList();

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var info = new FileInfo(file);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                    continue;

                var length = Math.Max(0, info.Length);
                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);

                deletedBytes += length;
                deletedFiles++;
            }
            catch
            {
                skippedFiles++;
            }
        }

        DeleteEmptyDirectories(root, ref skippedFiles, cancellationToken);
    }

    private static IReadOnlyList<string> EnumerateFilesSafe(
        string root,
        ref int skipped,
        CancellationToken cancellationToken)
    {
        var result = new List<string>();
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();

            try
            {
                result.AddRange(Directory.EnumerateFiles(directory));
            }
            catch
            {
                skipped++;
                continue;
            }

            IEnumerable<string> directories;
            try
            {
                directories = Directory.EnumerateDirectories(directory).ToArray();
            }
            catch
            {
                skipped++;
                continue;
            }

            foreach (var child in directories)
            {
                try
                {
                    var attributes = File.GetAttributes(child);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                        continue;

                    pending.Push(child);
                }
                catch
                {
                    skipped++;
                }
            }
        }

        return result;
    }

    private static void DeleteEmptyDirectories(
        string root,
        ref int skipped,
        CancellationToken cancellationToken)
    {
        List<string> directories;
        try
        {
            directories = Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                .OrderByDescending(path => path.Length)
                .ToList();
        }
        catch
        {
            return;
        }

        foreach (var directory in directories)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var attributes = File.GetAttributes(directory);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    continue;

                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                    Directory.Delete(directory, false);
            }
            catch
            {
                skipped++;
            }
        }
    }

    private static bool IsAllowedRoot(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
            return false;

        string full;
        try
        {
            full = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return false;
        }

        var local = Path.GetFullPath(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        var windowsTemp = Path.GetFullPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "Temp"));
        var nvidiaProgramData = Path.GetFullPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "NVIDIA Corporation",
            "NV_Cache"));

        return full.StartsWith(
                   local.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                   StringComparison.OrdinalIgnoreCase)
               || full.Equals(windowsTemp, StringComparison.OrdinalIgnoreCase)
               || full.Equals(nvidiaProgramData, StringComparison.OrdinalIgnoreCase);
    }

    public static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = Math.Max(0, bytes);
        var unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }
}
