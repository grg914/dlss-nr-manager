using System.IO.Compression;

namespace DlssNrManager.Services;

public static class SafeZip
{
    public const int DefaultMaxEntries = 10_000;
    public const long DefaultMaxExpandedBytes = 4L * 1024 * 1024 * 1024;

    public static void Extract(
        string zipPath,
        string destination,
        int maxEntries = DefaultMaxEntries,
        long maxExpandedBytes = DefaultMaxExpandedBytes)
    {
        if (maxEntries <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxEntries));

        if (maxExpandedBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxExpandedBytes));

        Directory.CreateDirectory(destination);

        var root = Path.GetFullPath(destination)
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        using var archive = ZipFile.OpenRead(zipPath);

        if (archive.Entries.Count > maxEntries)
        {
            throw new InvalidDataException(
                $"Archive contains too many entries ({archive.Entries.Count} > {maxEntries}).");
        }

        long expandedBytes = 0;

        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
                continue;

            if (entry.Length < 0 ||
                expandedBytes > maxExpandedBytes - entry.Length)
            {
                throw new InvalidDataException(
                    $"Archive expands beyond the {FormatBytes(maxExpandedBytes)} safety limit.");
            }

            expandedBytes += entry.Length;

            var target = Path.GetFullPath(
                Path.Combine(
                    destination,
                    entry.FullName.Replace(
                        '/',
                        Path.DirectorySeparatorChar)));

            if (!target.StartsWith(
                    root,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Archive entry escapes destination: {entry.FullName}");
            }

            Directory.CreateDirectory(
                Path.GetDirectoryName(target)!);
            entry.ExtractToFile(
                target,
                overwrite: true);
        }
    }

    private static string FormatBytes(long bytes)
    {
        var gib = bytes / (1024d * 1024d * 1024d);
        return $"{gib:0.##} GB";
    }
}
