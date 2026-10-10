using System.Security.Cryptography;

namespace DlssNrManager.Services;

/// <summary>
/// v3.2.0 historical release contained five text NCNN .param files checked out
/// as CRLF on Windows. Their public SHA-256 values exactly equal the original
/// pinned Git LF blobs after LF→CRLF conversion. Restore ONLY these five
/// known byte-identical inputs, NEVER an arbitrary changed model payload.
/// </summary>
public static class RealEsrganPublishedModelRepair
{
    public const string ApprovedReleaseTag = "v3.2.0";

    private sealed record PublishedModel(
        long OriginalSize,
        long PublishedSize,
        string PublishedSha256,
        bool RestoreLf);

    private static readonly IReadOnlyDictionary<string, PublishedModel> Models =
        new Dictionary<string, PublishedModel>(StringComparer.Ordinal)
        {
            ["realesrgan-x4plus.param"] = new(116029, 117030,
                "c8a066c12541a1ef01ba90fddd90688278bdc11536847699239be29225015bc8", true),
            ["realesrgan-x4plus.bin"] = new(33424520, 33424520,
                "713ee713b0353afaa27976f0563a64a5043bd70b9bd8936c2e26e25ebcdbcddf", false),
            ["realesrnet-x4plus.param"] = new(116029, 116029,
                "35330ececcea33b6c397a72548e788d5d53becee4734c50b7fada36e89f10a86", false),
            ["realesrnet-x4plus.bin"] = new(33424520, 33424520,
                "26bccfcc82d9e8260c0c6b0dffb34ab297982740882d1f33c6d423f70b562c40", false),
            ["realesrgan-x4plus-anime.param"] = new(30290, 30560,
                "d63c7e93c58ec5d0048ca1f0a995f40b2af17c2816a2e0a3b7172046d98795ec", true),
            ["realesrgan-x4plus-anime.bin"] = new(8943500, 8943500,
                "fe01c269cfd10cdef8e018ab66ebe750cf79c7af4d1f9c16c737e1295229bacc", false),
            ["realesr-animevideov3-x2.param"] = new(3173, 3216,
                "1393f7c0e885f9d15a0668329a13f695ebd0ea45791f46d28145d7934824d224", true),
            ["realesr-animevideov3-x2.bin"] = new(1247368, 1247368,
                "548a36f9c3f4ab8da56cd3b13badf23968bee207b396dad14d04b830e5f2ab2d", false),
            ["realesr-animevideov3-x3.param"] = new(3173, 3216,
                "584a43e429188c159ef8e42191ef5a5fd1d2e3b17398e5cee6089726dec879c7", true),
            ["realesr-animevideov3-x3.bin"] = new(1247368, 1247368,
                "548a36f9c3f4ab8da56cd3b13badf23968bee207b396dad14d04b830e5f2ab2d", false),
            ["realesr-animevideov3-x4.param"] = new(3077, 3119,
                "157c0a10405f885d4c53ce76fe2ba731694bcd16ec90ab6b889335e384239682", true),
            ["realesr-animevideov3-x4.bin"] = new(1247368, 1247368,
                "548a36f9c3f4ab8da56cd3b13badf23968bee207b396dad14d04b830e5f2ab2d", false)
        };

    public static long PublishedSize(string fileName, long originalSize)
        => Resolve(fileName, originalSize).PublishedSize;

    public static async Task VerifyAndRestoreAsync(
        string fileName, long originalSize, string downloadedPath,
        CancellationToken cancellationToken = default)
    {
        var approved = Resolve(fileName, originalSize);
        var info = new FileInfo(downloadedPath);
        if (info.Length != approved.PublishedSize)
            throw new InvalidDataException(
                $"Real-ESRGAN published model size mismatch: {fileName}.");

        await using (var stream = new FileStream(
                         downloadedPath, FileMode.Open, FileAccess.Read,
                         FileShare.Read, 128 * 1024, useAsync: true))
        {
            var hash = Convert.ToHexString(
                await SHA256.HashDataAsync(stream, cancellationToken));
            if (!hash.Equals(approved.PublishedSha256,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"Real-ESRGAN published model SHA-256 mismatch: {fileName}.");
        }

        if (!approved.RestoreLf)
            return;

        // Only the exact SHA-256-verified historical CRLF file is accepted.
        // Never modify the destination file until the outer installer verifies
        // the restored canonical size and original pinned Git blob SHA-1.
        var published = await File.ReadAllBytesAsync(
            downloadedPath, cancellationToken);
        var restored = new byte[checked((int)approved.OriginalSize)];
        var written = 0;
        for (var i = 0; i < published.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var value = published[i];
            if (value == (byte)'\r')
            {
                if (i + 1 >= published.Length || published[++i] != (byte)'\n')
                    throw new InvalidDataException(
                        $"Real-ESRGAN model has unexpected CR bytes: {fileName}.");
                value = (byte)'\n';
            }
            else if (value == (byte)'\n' || value > 127)
            {
                throw new InvalidDataException(
                    $"Real-ESRGAN model has unexpected text encoding: {fileName}.");
            }

            if (written >= restored.Length)
                throw new InvalidDataException(
                    $"Real-ESRGAN normalized model is too large: {fileName}.");
            restored[written++] = value;
        }

        if (written != restored.Length)
            throw new InvalidDataException(
                $"Real-ESRGAN normalized model size mismatch: {fileName}.");

        await File.WriteAllBytesAsync(
            downloadedPath, restored, cancellationToken);
    }

    private static PublishedModel Resolve(string fileName, long originalSize)
    {
        if (!Models.TryGetValue(fileName, out var approved) ||
            originalSize != approved.OriginalSize)
            throw new InvalidDataException(
                $"Unknown or changed Real-ESRGAN immutable model pin: {fileName}.");
        return approved;
    }
}
