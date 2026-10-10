using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace DlssNrManager.Tests;

/// <summary>
/// Regression for historical v3.2.0 release model text conversion (#411).
/// These NCNN .param assets must retain their exact Git blob bytes on Windows
/// and must never be accepted merely through a normalized Git hash-object.
/// </summary>
public sealed class RealEsrganModelCheckoutIntegrityTests
{
    private const string PyModels =
        "third_party/Real-ESRGAN-model-sources/realesrgan-ncnn-py/src/realesrgan_ncnn_py/models";
    private const string SpinModels =
        "third_party/Real-ESRGAN-model-sources/spintexture/vendor/realesrgan/models";

    [Theory]
    [InlineData(PyModels, "realesrgan-x4plus.param", 116029L,
        "d14d62ebb815bdd522ed112e67695b3377f86ca0")]
    [InlineData(PyModels, "realesrgan-x4plus-anime.param", 30290L,
        "6c98f9a1932603688683a6f0108cbdfcd6b3e680")]
    [InlineData(PyModels, "realesr-animevideov3-x2.param", 3173L,
        "42e774841c35c8bf0ffeb215bb40c61d4868be16")]
    [InlineData(PyModels, "realesr-animevideov3-x3.param", 3173L,
        "bf4718580cc40eac9ff34f730ca64053feaf7bf4")]
    [InlineData(PyModels, "realesr-animevideov3-x4.param", 3077L,
        "5b922cc388374b1152e01fa633bcab80b2448dae")]
    [InlineData(SpinModels, "realesrnet-x4plus.param", 116029L,
        "d14d62ebb815bdd522ed112e67695b3377f86ca0")]
    public void Checked_out_model_matches_pinned_raw_Git_blob_on_every_host(
        string sourceDirectory, string name, long expectedSize, string expectedBlob)
    {
        var path = Path.Combine(FindRepositoryRoot(),
            sourceDirectory.Replace('/', Path.DirectorySeparatorChar), name);
        var bytes = File.ReadAllBytes(path);
        Assert.Equal(expectedSize, bytes.LongLength);

        // NCNN parameter files in these locked source snapshots use LF, never CRLF.
        for (var i = 1; i < bytes.Length; i++)
            Assert.False(bytes[i - 1] == (byte)'\r' && bytes[i] == (byte)'\n',
                $"Unexpected Windows CRLF conversion in {name}.");

        var prefix = Encoding.ASCII.GetBytes($"blob {bytes.Length}\0");
        var objectBytes = new byte[prefix.Length + bytes.Length];
        Buffer.BlockCopy(prefix, 0, objectBytes, 0, prefix.Length);
        Buffer.BlockCopy(bytes, 0, objectBytes, prefix.Length, bytes.Length);
        Assert.Equal(expectedBlob,
            Convert.ToHexString(SHA1.HashData(objectBytes)).ToLowerInvariant());
    }

    [Fact]
    public void Repository_marks_NCNN_param_files_binary_for_all_subdirectories()
    {
        var attributes = File.ReadAllText(
            Path.Combine(FindRepositoryRoot(), ".gitattributes"));
        Assert.Contains("*.param binary", attributes);
    }

    [Fact]
    public void Release_verifies_raw_bytes_on_both_sides_of_model_copy()
    {
        var workflow = File.ReadAllText(
            Path.Combine(FindRepositoryRoot(), ".github", "workflows", "release.yml"));
        Assert.Contains("git hash-object --no-filters -- $asset.Source", workflow);
        Assert.Contains("git hash-object --no-filters -- $destination", workflow);
        Assert.Contains("if ($LASTEXITCODE -ne 0 -or $copiedBlob -ne $expectedBlob)", workflow);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null &&
               !File.Exists(Path.Combine(directory.FullName, "DlssNrManager.csproj")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
