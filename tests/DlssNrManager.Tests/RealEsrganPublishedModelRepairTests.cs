using System.Security.Cryptography;
using System.Text;
using DlssNrManager.Services;
using Xunit;

namespace DlssNrManager.Tests;

/// <summary>
/// Historical v3.2.0 release emitted five model .param files after Windows
/// LF→CRLF checkout conversion. Only exact public SHA-256-locked contents
/// may be returned to the immutable vendor LF Git source bytes.
/// </summary>
public sealed class RealEsrganPublishedModelRepairTests
{
    private const string PyModels =
        "third_party/Real-ESRGAN-model-sources/realesrgan-ncnn-py/src/realesrgan_ncnn_py/models";
    private const string SpinModels =
        "third_party/Real-ESRGAN-model-sources/spintexture/vendor/realesrgan/models";

    [Theory]
    [InlineData("realesrgan-x4plus.param", 116029, 117030,
        "c8a066c12541a1ef01ba90fddd90688278bdc11536847699239be29225015bc8")]
    [InlineData("realesrgan-x4plus-anime.param", 30290, 30560,
        "d63c7e93c58ec5d0048ca1f0a995f40b2af17c2816a2e0a3b7172046d98795ec")]
    [InlineData("realesr-animevideov3-x2.param", 3173, 3216,
        "1393f7c0e885f9d15a0668329a13f695ebd0ea45791f46d28145d7934824d224")]
    [InlineData("realesr-animevideov3-x3.param", 3173, 3216,
        "584a43e429188c159ef8e42191ef5a5fd1d2e3b17398e5cee6089726dec879c7")]
    [InlineData("realesr-animevideov3-x4.param", 3077, 3119,
        "157c0a10405f885d4c53ce76fe2ba731694bcd16ec90ab6b889335e384239682")]
    public async Task Exactly_published_crlf_model_recovers_vendor_blob(
        string fileName, int canonicalSize, int publishedSize, string publishedSha256)
    {
        var source = Path.Combine(FindRoot(), PyModels.Replace('/', Path.DirectorySeparatorChar),
            fileName);
        var vendorBytes = await File.ReadAllBytesAsync(source);
        Assert.Equal(canonicalSize, vendorBytes.Length);
        Assert.DoesNotContain((byte)'\r', vendorBytes);

        using var crlf = new MemoryStream();
        foreach (var value in vendorBytes)
        {
            if (value == (byte)'\n')
                crlf.WriteByte((byte)'\r');
            crlf.WriteByte(value);
        }
        var publishedBytes = crlf.ToArray();
        Assert.Equal(publishedSize, publishedBytes.Length);
        Assert.Equal(publishedSha256,
            Convert.ToHexString(SHA256.HashData(publishedBytes)).ToLowerInvariant());
        Assert.Equal(publishedSize,
            RealEsrganPublishedModelRepair.PublishedSize(fileName, canonicalSize));

        var folder = Path.Combine(Path.GetTempPath(), "esr-model-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var stage = Path.Combine(folder, fileName + ".download");
            await File.WriteAllBytesAsync(stage, publishedBytes);
            await RealEsrganPublishedModelRepair.VerifyAndRestoreAsync(
                fileName, canonicalSize, stage);
            Assert.Equal(vendorBytes, await File.ReadAllBytesAsync(stage));
            Assert.Equal(publishedSha256,
                Convert.ToHexString(SHA256.HashData(publishedBytes)).ToLowerInvariant());
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task Already_correct_model_is_not_transformed()
    {
        var name = "realesrnet-x4plus.param";
        var source = Path.Combine(FindRoot(), SpinModels.Replace('/', Path.DirectorySeparatorChar),
            name);
        var canonical = await File.ReadAllBytesAsync(source);
        Assert.Equal(116029, canonical.Length);
        Assert.Equal("35330ececcea33b6c397a72548e788d5d53becee4734c50b7fada36e89f10a86",
            Convert.ToHexString(SHA256.HashData(canonical)).ToLowerInvariant());
        var folder = Path.Combine(Path.GetTempPath(), "esr-correct-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var stage = Path.Combine(folder, name);
            await File.WriteAllBytesAsync(stage, canonical);
            await RealEsrganPublishedModelRepair.VerifyAndRestoreAsync(
                name, canonical.Length, stage);
            Assert.Equal(canonical, await File.ReadAllBytesAsync(stage));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task Modified_published_model_must_fail_before_replacement()
    {
        var name = "realesr-animevideov3-x2.param";
        var original = await File.ReadAllBytesAsync(Path.Combine(
            FindRoot(), PyModels.Replace('/', Path.DirectorySeparatorChar), name));
        var withCrLf = new List<byte>(original.Length + 43);
        foreach (var value in original)
        {
            if (value == (byte)'\n')
                withCrLf.Add((byte)'\r');
            withCrLf.Add(value);
        }
        withCrLf[5] ^= 1;
        var path = Path.Combine(Path.GetTempPath(),
            "esr-damaged-" + Guid.NewGuid().ToString("N") + ".download");
        try
        {
            await File.WriteAllBytesAsync(path, withCrLf.ToArray());
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                RealEsrganPublishedModelRepair.VerifyAndRestoreAsync(
                    name, original.Length, path));
            Assert.Equal(withCrLf.ToArray(), await File.ReadAllBytesAsync(path));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void Unknown_or_wrong_pins_are_rejected()
    {
        Assert.Throws<InvalidDataException>(() =>
            RealEsrganPublishedModelRepair.PublishedSize("malicious.param", 100));
        Assert.Throws<InvalidDataException>(() =>
            RealEsrganPublishedModelRepair.PublishedSize("realesrgan-x4plus.param", 117030));
    }

    [Fact]
    public void Installer_uses_immutable_model_release_and_preserves_canonical_git_validation()
    {
        var root = FindRoot();
        var installer = File.ReadAllText(Path.Combine(root, "Services", "AiUpscaleService.cs"));
        var verifier = File.ReadAllText(Path.Combine(root,
            "tools", "verify-realesrgan-model-candidate.ps1"));
        const string pinned = "https://github.com/grg914/dlss-nr-manager/releases/download/v3.2.0/";
        Assert.DoesNotContain("releases/latest/download", installer, StringComparison.Ordinal);
        Assert.Equal(12, installer.Split(pinned).Length - 1);
        Assert.Contains("RealEsrganPublishedModelRepair.VerifyAndRestoreAsync(", installer);
        Assert.Contains("await GitBlobSha1Async(temp, token)", installer);
        Assert.Contains("git hash-object --no-filters", verifier);
        Assert.Contains("releases/download/v3.2.0/", verifier);
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "DlssNrManager.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
