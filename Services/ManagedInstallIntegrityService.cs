using DlssNrManager.Models;

namespace DlssNrManager.Services;

public sealed record ManagedInstallIntegrityResult(
    bool HasManifest,
    bool HasPendingTransaction,
    IReadOnlyList<string> MissingFiles,
    IReadOnlyList<string> ChangedFiles,
    IReadOnlyList<string> UnhashedFiles)
{
    public bool Healthy =>
        HasManifest &&
        !HasPendingTransaction &&
        MissingFiles.Count == 0 &&
        ChangedFiles.Count == 0;

    public string Summary
    {
        get
        {
            if (!HasManifest)
                return "No managed install manifest was found.";

            if (Healthy)
            {
                return UnhashedFiles.Count == 0
                    ? "Managed installation verified: all tracked file hashes match."
                    : $"Managed installation is structurally healthy; {UnhashedFiles.Count} legacy/unhashed tracked file(s) could not be cryptographically compared.";
            }

            return
                $"Managed installation needs attention • missing {MissingFiles.Count} • changed {ChangedFiles.Count}" +
                (HasPendingTransaction ? " • interrupted transaction detected" : "");
        }
    }
}

public sealed class ManagedInstallIntegrityService
{
    public ManagedInstallIntegrityResult Verify(string gameDir)
    {
        var manifest = InstallerService.ReadManifest(gameDir);
        if (manifest == null)
        {
            return new ManagedInstallIntegrityResult(
                false,
                FileTransactionJournal.ReadPending(gameDir) != null,
                [],
                [],
                []);
        }

        var expectedHashes =
            manifest.ManagedFileHashes ??
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

        var missing = new List<string>();
        var changed = new List<string>();
        var unhashed = new List<string>();

        foreach (var relative in manifest.ManagedFiles ?? [])
        {
            if (relative.Equals(
                    ".dlssnr-manager-state.json",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!TryResolveUnderRoot(
                    gameDir,
                    relative,
                    out var path))
            {
                changed.Add(relative + " (unsafe path)");
                continue;
            }

            if (!File.Exists(path))
            {
                missing.Add(relative);
                continue;
            }

            if (!expectedHashes.TryGetValue(
                    relative,
                    out var expected) ||
                string.IsNullOrWhiteSpace(expected))
            {
                unhashed.Add(relative);
                continue;
            }

            var actual = HashService.Sha256(path);
            if (!actual.Equals(
                    expected,
                    StringComparison.OrdinalIgnoreCase))
            {
                changed.Add(relative);
            }
        }

        return new ManagedInstallIntegrityResult(
            true,
            FileTransactionJournal.ReadPending(gameDir) != null,
            missing,
            changed,
            unhashed);
    }

    private static bool TryResolveUnderRoot(
        string rootPath,
        string relative,
        out string resolved)
    {
        resolved = string.Empty;

        if (string.IsNullOrWhiteSpace(relative) ||
            Path.IsPathRooted(relative))
            return false;

        try
        {
            var root = Path.GetFullPath(rootPath)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            var candidate = Path.GetFullPath(
                Path.Combine(root, relative));

            if (!candidate.StartsWith(
                    root,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            resolved = candidate;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
