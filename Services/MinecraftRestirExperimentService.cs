using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DlssNrManager.Services;

public sealed record MinecraftRestirState(bool Available, bool Enabled, string Detail);

/// <summary>
/// Offline, opt-in configuration of the experimental Caustica temporal ReSTIR switch.
/// Never downloads, replaces or promotes a Caustica JAR.
/// </summary>
public sealed class MinecraftRestirExperimentService
{
    private const string SettingsClass = "dev/comfyfluffy/caustica/CausticaConfig$Rt$Lights.class";
    private static readonly Regex Section = new(@"^\s*\[([A-Za-z0-9_.-]+)\]\s*(?:#.*)?$", RegexOptions.Compiled);
    private static readonly Regex Option = new(@"^(\s*restir-di\s*=\s*)(true|false)(\s*(?:#.*)?)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex OptionKey = new(@"^\s*restir-di\s*=", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public MinecraftRestirState Inspect(string root)
    {
        try
        {
            var modDirectory = Path.Combine(Path.GetFullPath(root), "mods");
            if (!Directory.Exists(modDirectory) || IsReparsePoint(modDirectory))
                return Unavailable("No regular mods directory was found.");

            var jars = Directory.EnumerateFiles(modDirectory, "*", SearchOption.TopDirectoryOnly)
                .Where(path => Path.GetFileName(path).Contains("caustica", StringComparison.OrdinalIgnoreCase)
                    && path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (jars.Length != 1 || IsReparsePoint(jars[0]))
                return Unavailable("Exactly one regular Caustica JAR is required.");

            using (var zip = ZipFile.OpenRead(jars[0]))
            {
                var manifest = zip.GetEntry("fabric.mod.json");
                if (manifest is null || manifest.Length is <= 0 or > 131072)
                    return Unavailable("The Caustica Fabric identity or Minecraft version is not compatible.");

                using (var manifestStream = manifest.Open())
                using (var metadata = JsonDocument.Parse(manifestStream))
                {
                    var mod = metadata.RootElement;
                    if (mod.ValueKind != JsonValueKind.Object
                        || !mod.TryGetProperty("id", out var modId)
                        || modId.ValueKind != JsonValueKind.String
                        || modId.GetString() != "caustica"
                        || !mod.TryGetProperty("depends", out var depends)
                        || depends.ValueKind != JsonValueKind.Object
                        || !depends.TryGetProperty("minecraft", out var version)
                        || version.ValueKind != JsonValueKind.String
                        || version.GetString() != MinecraftIntegrationService.MinecraftVersion)
                        return Unavailable("The Caustica Fabric identity or Minecraft version is not compatible.");
                }

                var entry = zip.GetEntry(SettingsClass);
                if (entry is null || entry.Length is <= 0 or > 262144)
                    return Unavailable("The installed Caustica JAR does not expose the experimental option.");

                using var stream = entry.Open();
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                var literals = Encoding.UTF8.GetString(memory.ToArray());
                if (!literals.Contains("lights.restir-di", StringComparison.Ordinal)
                    || !literals.Contains("caustica.rt.restirDi", StringComparison.Ordinal))
                    return Unavailable("The installed Caustica JAR does not expose the experimental option.");
            }

            var config = ConfigPath(root);
            if (File.Exists(config) && IsReparsePoint(config))
                return Unavailable("The Caustica configuration is a symbolic link.");

            var parent = Path.GetDirectoryName(config)!;
            if (Directory.Exists(parent) && IsReparsePoint(parent))
                return Unavailable("The Caustica configuration directory is a symbolic link.");

            var enabled = ReadSetting(File.Exists(config) ? File.ReadAllText(config) : "");
            return new(true, enabled,
                "Experimental Caustica setting detected. The binary is not certified for Vulkan/RTX rendering.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or InvalidDataException or JsonException or ArgumentException or NotSupportedException)
        {
            return Unavailable("Cannot safely inspect the Caustica installation: " + ex.Message);
        }
    }

    public void SetEnabled(string root, bool enabled)
    {
        var state = Inspect(root);
        if (!state.Available)
            throw new InvalidOperationException(state.Detail);

        if (state.Enabled == enabled)
            return;

        var config = ConfigPath(root);
        var parent = Path.GetDirectoryName(config)!;
        if (Directory.Exists(parent) && IsReparsePoint(parent))
            throw new IOException("Refusing to write through a linked configuration directory.");

        Directory.CreateDirectory(parent);
        if (File.Exists(config) && IsReparsePoint(config))
            throw new IOException("Refusing to overwrite a linked configuration.");

        var original = File.Exists(config) ? File.ReadAllText(config) : "";
        var updated = WriteSetting(original, enabled);
        var backup = config + ".restir-backup";
        if (File.Exists(backup) && IsReparsePoint(backup))
            throw new IOException("Refusing to overwrite a linked backup.");

        var temp = Path.Combine(parent, ".caustica-restir-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            var hasBom = File.Exists(config) && File.ReadAllBytes(config).AsSpan()
                .StartsWith(new byte[] { 0xEF, 0xBB, 0xBF });
            File.WriteAllText(temp, updated, new UTF8Encoding(hasBom));
            if (File.Exists(config))
                File.Replace(temp, config, backup, ignoreMetadataErrors: true);
            else
                File.Move(temp, config);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }

    private static string ConfigPath(string root) =>
        Path.Combine(Path.GetFullPath(root), "config", "caustica.toml");

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static MinecraftRestirState Unavailable(string detail) => new(false, false, detail);

    private static bool ReadSetting(string content)
    {
        var (_, keyIndex, state) = Locate(content);
        return keyIndex >= 0 && state;
    }

    private static string WriteSetting(string content, bool enabled)
    {
        var (sectionEnd, keyIndex, _) = Locate(content);
        var newline = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = content.Split(new[] { newline }, StringSplitOptions.None);

        if (keyIndex >= 0)
        {
            lines[keyIndex] = Option.Replace(lines[keyIndex],
                match => match.Groups[1].Value + (enabled ? "true" : "false") + match.Groups[3].Value);
            return string.Join(newline, lines);
        }

        var setting = "restir-di = " + (enabled ? "true" : "false");
        if (sectionEnd >= 0)
        {
            var list = lines.ToList();
            list.Insert(sectionEnd, setting);
            return string.Join(newline, list);
        }

        if (string.IsNullOrEmpty(content))
            return "[lights]" + newline + setting + newline;

        return content.TrimEnd('\r', '\n') + newline + newline + "[lights]" + newline + setting + newline;
    }

    private static (int SectionEnd, int KeyIndex, bool Enabled) Locate(string content)
    {
        var newline = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = content.Split(new[] { newline }, StringSplitOptions.None);
        var inLights = false;
        var sectionCount = 0;
        var sectionEnd = -1;
        var keyIndex = -1;
        var enabled = false;
        for (var i = 0; i < lines.Length; i++)
        {
            var section = Section.Match(lines[i]);
            if (section.Success)
            {
                if (inLights)
                    sectionEnd = i;
                inLights = section.Groups[1].Value.Equals("lights", StringComparison.Ordinal);
                if (inLights)
                    sectionEnd = i + 1;
                if (inLights && ++sectionCount > 1)
                    throw new InvalidDataException("Duplicate [lights] sections are not supported.");
                continue;
            }

            if (!inLights)
                continue;

            sectionEnd = i + 1;

            if (!OptionKey.IsMatch(lines[i]))
                continue;

            var value = Option.Match(lines[i]);
            if (!value.Success || keyIndex >= 0)
                throw new InvalidDataException("Ambiguous or invalid lights.restir-di setting.");
            keyIndex = i;
            enabled = value.Groups[2].Value.Equals("true", StringComparison.OrdinalIgnoreCase);
        }
        return (sectionEnd, keyIndex, enabled);
    }
}
