namespace DlssNrManager.Services;

public sealed class PcCleanupItem
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required IReadOnlyList<string> Paths { get; init; }
    public IReadOnlyList<string> FilePatterns { get; init; } = ["*"];
    public bool Recursive { get; init; } = true;
    public bool DeleteEmptyDirectories { get; init; } = true;
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
                Id = "windows-shell-cache",
                Name = "Windows thumbnail and icon cache",
                Description = "Explorer thumbnail/icon databases; Windows rebuilds them automatically",
                Paths = [Path.Combine(local, "Microsoft", "Windows", "Explorer")],
                FilePatterns = ["thumbcache_*.db", "iconcache_*.db"],
                Recursive = false,
                DeleteEmptyDirectories = false
            },
            new()
            {
                Id = "nvidia-dx",
                Name = "NVIDIA DirectX shader cache",
                Description = "NVIDIA DXCache variants; shaders are rebuilt after cleanup",
                Paths =
                [
                    Path.Combine(local, "NVIDIA", "DXCache"),
                    Path.Combine(local, "NVIDIA", "PerDriverVersion", "DXCache")
                ]
            },
            new()
            {
                Id = "nvidia-gl",
                Name = "NVIDIA OpenGL/Vulkan cache",
                Description = "NVIDIA GLCache variants; shaders are rebuilt after cleanup",
                Paths =
                [
                    Path.Combine(local, "NVIDIA", "GLCache"),
                    Path.Combine(local, "NVIDIA", "PerDriverVersion", "GLCache")
                ]
            },
            new()
            {
                Id = "nvidia-compute",
                Name = "NVIDIA compute cache",
                Description = "CUDA/NVIDIA compute kernels; applications rebuild them as needed",
                Paths = [Path.Combine(local, "NVIDIA", "ComputeCache")]
            },
            new()
            {
                Id = "amd-shaders",
                Name = "AMD shader caches",
                Description = "AMD DirectX/OpenGL/Vulkan shader caches; drivers rebuild them as needed",
                Paths =
                [
                    Path.Combine(local, "AMD", "DxCache"),
                    Path.Combine(local, "AMD", "DxcCache"),
                    Path.Combine(local, "AMD", "GLCache"),
                    Path.Combine(local, "AMD", "VkCache")
                ]
            },
            new()
            {
                Id = "intel-shaders",
                Name = "Intel shader cache",
                Description = "Intel graphics shader cache; drivers rebuild it as needed",
                Paths = [Path.Combine(local, "Intel", "ShaderCache")]
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

                        ScanRoot(
                            root,
                            item.FilePatterns,
                            item.Recursive,
                            ref bytes,
                            ref files,
                            ref skipped,
                            cancellationToken);
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
                            item.FilePatterns,
                            item.Recursive,
                            item.DeleteEmptyDirectories,
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
        IReadOnlyList<string> filePatterns,
        bool recursive,
        ref long bytes,
        ref int files,
        ref int skipped,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(root))
            return;

        if (!IsSafeDirectoryRoot(root))
        {
            skipped++;
            return;
        }

        foreach (var file in EnumerateFilesSafe(
                     root,
                     filePatterns,
                     recursive,
                     ref skipped,
                     cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var info = new FileInfo(file);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0 ||
                    ManagedPathSafety.HasReparsePointOnPath(file))
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
        IReadOnlyList<string> filePatterns,
        bool recursive,
        bool deleteEmptyDirectories,
        ref long deletedBytes,
        ref int deletedFiles,
        ref int skippedFiles,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(root))
            return;

        if (!IsSafeDirectoryRoot(root))
        {
            skippedFiles++;
            return;
        }

        var files = EnumerateFilesSafe(
            root,
            filePatterns,
            recursive,
            ref skippedFiles,
            cancellationToken).ToList();

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var info = new FileInfo(file);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0 ||
                    ManagedPathSafety.HasReparsePointOnPath(file))
                {
                    skippedFiles++;
                    continue;
                }

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

        if (deleteEmptyDirectories && recursive)
            DeleteEmptyDirectories(root, ref skippedFiles, cancellationToken);
    }

    private static IReadOnlyList<string> EnumerateFilesSafe(
        string root,
        IReadOnlyList<string> filePatterns,
        bool recursive,
        ref int skipped,
        CancellationToken cancellationToken)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var patterns = filePatterns
            .Where(pattern =>
                !string.IsNullOrWhiteSpace(pattern) &&
                !pattern.Contains(Path.DirectorySeparatorChar) &&
                !pattern.Contains(Path.AltDirectorySeparatorChar))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (patterns.Length == 0)
            patterns = ["*"];

        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            try
            {
                if (ManagedPathSafety.HasReparsePointOnPath(directory))
                {
                    skipped++;
                    continue;
                }
            }
            catch
            {
                skipped++;
                continue;
            }

            foreach (var pattern in patterns)
            {
                try
                {
                    foreach (var file in Directory.EnumerateFiles(
                                 directory,
                                 pattern,
                                 SearchOption.TopDirectoryOnly))
                    {
                        result.Add(file);
                    }
                }
                catch
                {
                    skipped++;
                }
            }

            if (!recursive)
                continue;

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

        return result.ToArray();
    }

    private static void DeleteEmptyDirectories(
        string root,
        ref int skipped,
        CancellationToken cancellationToken)
    {
        var pending = new Stack<string>();
        var directories = new List<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();

            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateDirectories(current).ToArray();
            }
            catch
            {
                skipped++;
                continue;
            }

            foreach (var child in children)
            {
                try
                {
                    var attributes = File.GetAttributes(child);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                        continue;

                    directories.Add(child);
                    pending.Push(child);
                }
                catch
                {
                    skipped++;
                }
            }
        }

        foreach (var directory in directories.OrderByDescending(path => path.Length))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (ManagedPathSafety.HasReparsePointOnPath(directory))
                {
                    skipped++;
                    continue;
                }
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                    Directory.Delete(directory, false);
            }
            catch
            {
                skipped++;
            }
        }
    }

    private static bool IsSafeDirectoryRoot(string root)
    {
        try
        {
            var attributes = File.GetAttributes(root);
            // A normal leaf can still be nested below a junction that
            // redirects cleanup outside the manager's allowed roots.
            return (attributes & FileAttributes.ReparsePoint) == 0 &&
                   !ManagedPathSafety.HasReparsePointOnPath(root);
        }
        catch
        {
            return false;
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
