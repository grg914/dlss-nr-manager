using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml;

namespace DlssNrManager.Services;

public static class LauncherGameDiscovery
{
    public static IEnumerable<(string Name, string Platform, string Root, string? ArtworkUrl)> DetectItchApps()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddRoot(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            try
            {
                path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
                path = Path.GetFullPath(path);
                if (Directory.Exists(path))
                    roots.Add(path);
            }
            catch { }
        }

        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var itchData = Path.Combine(roaming, "itch");

        foreach (var jsonPath in new[]
        {
            Path.Combine(itchData, "preferences.json"),
            Path.Combine(itchData, "config.json")
        })
        {
            foreach (var root in ExtractAbsoluteDirectories(jsonPath))
                AddRoot(root);
        }

        foreach (var path in new[]
        {
            Path.Combine(user, "Games"),
            Path.Combine(user, "itch"),
            Path.Combine(user, "itch.io"),
            Path.Combine(user, "Itch Games")
        })
            AddRoot(path);

        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady || drive.DriveType is DriveType.Network or DriveType.CDRom)
                    continue;

                foreach (var name in new[] { "Games", "itch", "itch.io", "Itch Games" })
                    AddRoot(Path.Combine(drive.RootDirectory.FullName, name));
            }
            catch { }
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots)
        {
            var queue = new Queue<(string Path, int Depth)>();
            queue.Enqueue((root, 0));

            while (queue.Count > 0 && seen.Count < 6000)
            {
                var (directory, depth) = queue.Dequeue();
                if (!seen.Add(directory))
                    continue;

                var receipt = Path.Combine(directory, ".itch", "receipt.json.gz");
                if (File.Exists(receipt))
                {
                    var name = ReadItchTitle(receipt)
                               ?? Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar));
                    if (!string.IsNullOrWhiteSpace(name))
                        yield return (name, "itch.io", directory, null);
                    continue;
                }

                if (depth >= 4)
                    continue;

                try
                {
                    foreach (var child in Directory.EnumerateDirectories(directory))
                    {
                        var childName = Path.GetFileName(child);
                        if (childName.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
                            childName.Equals("node_modules", StringComparison.OrdinalIgnoreCase))
                            continue;

                        queue.Enqueue((child, depth + 1));
                    }
                }
                catch { }
            }
        }
    }

    public static IEnumerable<(string Name, string Platform, string Root, string? ArtworkUrl)> DetectXboxApps()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady || drive.DriveType is DriveType.Network or DriveType.CDRom)
                continue;

            var marker = Path.Combine(drive.RootDirectory.FullName, ".GamingRoot");
            if (!File.Exists(marker))
                continue;

            var libraryRoot = DecodeGamingRoot(drive.RootDirectory.FullName, marker);
            if (string.IsNullOrWhiteSpace(libraryRoot) || !Directory.Exists(libraryRoot))
                continue;

            foreach (var config in FindXboxConfigs(libraryRoot))
            {
                var contentRoot = Path.GetDirectoryName(config);
                if (string.IsNullOrWhiteSpace(contentRoot) || !seen.Add(contentRoot))
                    continue;

                var (name, artwork) = ReadXboxMetadata(config, contentRoot);
                name ??= Directory.GetParent(contentRoot)?.Name ?? Path.GetFileName(contentRoot);

                if (!string.IsNullOrWhiteSpace(name))
                    yield return (name, "Xbox App", contentRoot, artwork);
            }
        }
    }

    private static IEnumerable<string> ExtractAbsoluteDirectories(string jsonPath)
    {
        if (!File.Exists(jsonPath))
            yield break;

        JsonDocument? json = null;
        try { json = JsonDocument.Parse(File.ReadAllText(jsonPath)); }
        catch { yield break; }

        using (json)
        {
            foreach (var value in EnumerateStrings(json.RootElement))
            {
                if (string.IsNullOrWhiteSpace(value))
                    continue;

                var expanded = Environment.ExpandEnvironmentVariables(value);
                if (Path.IsPathRooted(expanded) && Directory.Exists(expanded))
                    yield return expanded;
            }
        }
    }

    private static IEnumerable<string> EnumerateStrings(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            var value = element.GetString();
            if (!string.IsNullOrWhiteSpace(value))
                yield return value;
            yield break;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            foreach (var value in EnumerateStrings(property.Value))
                yield return value;
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            foreach (var value in EnumerateStrings(item))
                yield return value;
        }
    }

    private static string? ReadItchTitle(string receiptPath)
    {
        try
        {
            using var file = File.OpenRead(receiptPath);
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            using var json = JsonDocument.Parse(gzip);

            foreach (var key in new[] { "title", "name", "displayName", "gameTitle" })
            {
                var result = FindStringProperty(json.RootElement, key);
                if (!string.IsNullOrWhiteSpace(result))
                    return result;
            }
        }
        catch { }

        return null;
    }

    private static string? FindStringProperty(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase) &&
                    property.Value.ValueKind == JsonValueKind.String)
                    return property.Value.GetString();

                var nested = FindStringProperty(property.Value, propertyName);
                if (!string.IsNullOrWhiteSpace(nested))
                    return nested;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = FindStringProperty(item, propertyName);
                if (!string.IsNullOrWhiteSpace(nested))
                    return nested;
            }
        }

        return null;
    }

    private static string? DecodeGamingRoot(string driveRoot, string markerPath)
    {
        try
        {
            var bytes = File.ReadAllBytes(markerPath);
            if (bytes.Length <= 5 ||
                bytes[0] != (byte)'R' || bytes[1] != (byte)'G' ||
                bytes[2] != (byte)'B' || bytes[3] != (byte)'X')
                return null;

            var builder = new StringBuilder(driveRoot);
            for (var i = 5; i < bytes.Length; i++)
                if (bytes[i] != 0)
                    builder.Append((char)bytes[i]);

            return builder.ToString();
        }
        catch { return null; }
    }

    private static IEnumerable<string> FindXboxConfigs(string libraryRoot)
    {
        var queue = new Queue<(string Path, int Depth)>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        queue.Enqueue((libraryRoot, 0));

        while (queue.Count > 0 && visited.Count < 3000)
        {
            var (directory, depth) = queue.Dequeue();
            if (!visited.Add(directory))
                continue;

            foreach (var candidate in new[]
            {
                Path.Combine(directory, "MicrosoftGame.config"),
                Path.Combine(directory, "Content", "MicrosoftGame.config")
            })
            {
                if (File.Exists(candidate))
                    yield return candidate;
            }

            if (depth >= 3)
                continue;

            try
            {
                foreach (var child in Directory.EnumerateDirectories(directory))
                    queue.Enqueue((child, depth + 1));
            }
            catch { }
        }
    }

    private static (string? Name, string? Artwork) ReadXboxMetadata(string configPath, string contentRoot)
    {
        try
        {
            var xml = new XmlDocument();
            xml.Load(configPath);

            var shell = xml.SelectSingleNode("//ShellVisuals");
            string? name = shell?.Attributes?["DefaultDisplayName"]?.Value
                           ?? shell?.Attributes?["DisplayName"]?.Value;

            if (!string.IsNullOrWhiteSpace(name) &&
                name.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))
                name = null;

            name ??= xml.SelectSingleNode("//Identity")?.Attributes?["Name"]?.Value;

            foreach (var attr in new[] { "StoreLogo", "Square150x150Logo", "Square44x44Logo", "SplashScreenImage" })
            {
                var relative = shell?.Attributes?[attr]?.Value;
                if (string.IsNullOrWhiteSpace(relative))
                    continue;

                var candidate = Path.Combine(
                    contentRoot,
                    relative.Replace('/', Path.DirectorySeparatorChar));

                if (File.Exists(candidate))
                    return (name, candidate);
            }

            return (name, null);
        }
        catch { return (null, null); }
    }
}
