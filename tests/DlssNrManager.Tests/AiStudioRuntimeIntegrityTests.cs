using System.Text.Json;
using DlssNrManager.Services;
using Xunit;

namespace DlssNrManager.Tests;

public sealed class AiStudioRuntimeIntegrityTests
{
    [Fact]
    public async Task Refuses_local_manifest_without_external_trusted_pin()
    {
        using var fixture = new RuntimeFixture();

        var result = await AiStudioRuntimeIntegrityService.VerifyAsync(fixture.Root, null);

        Assert.Equal(AiStudioRuntimeIntegrityStatus.MissingTrustedPin, result.Status);
        Assert.False(Directory.Exists(fixture.Root));
    }

    [Fact]
    public async Task Reports_missing_manifest_without_creating_any_directory()
    {
        using var fixture = new RuntimeFixture();
        var pin = new string('a', 64);

        var result = await AiStudioRuntimeIntegrityService.VerifyAsync(fixture.Root, pin);

        Assert.Equal(AiStudioRuntimeIntegrityStatus.MissingManifest, result.Status);
        Assert.False(Directory.Exists(fixture.Root));
    }

    [Fact]
    public async Task Verifies_only_explicitly_pinned_files_without_execution()
    {
        using var fixture = new RuntimeFixture();
        var pin = fixture.WriteManifest("python/python.exe", "not an executable");

        var result = await AiStudioRuntimeIntegrityService.VerifyAsync(fixture.Root, pin);

        Assert.Equal(AiStudioRuntimeIntegrityStatus.ManifestFilesVerifiedOnly, result.Status);
        Assert.True(File.Exists(Path.Combine(fixture.Root, "python", "python.exe")));
    }

    [Fact]
    public async Task Rejects_tampered_manifest_even_when_embedded_file_hash_is_valid()
    {
        using var fixture = new RuntimeFixture();
        var pin = fixture.WriteManifest("python/python.exe", "fixture");

        File.AppendAllText(Path.Combine(fixture.Root, "runtime-integrity.json"), " ");

        var result = await AiStudioRuntimeIntegrityService.VerifyAsync(fixture.Root, pin);

        Assert.Equal(AiStudioRuntimeIntegrityStatus.InvalidManifest, result.Status);
    }

    [Fact]
    public async Task Rejects_corrupt_runtime_file_against_valid_manifest()
    {
        using var fixture = new RuntimeFixture();
        var pin = fixture.WriteManifest("python/python.exe", "original");

        File.WriteAllText(Path.Combine(fixture.Root, "python", "python.exe"), "modified");

        var result = await AiStudioRuntimeIntegrityService.VerifyAsync(fixture.Root, pin);

        Assert.Equal(AiStudioRuntimeIntegrityStatus.FileHashMismatch, result.Status);
    }

    [Fact]
    public async Task Rejects_path_traversal_even_with_manifest_sha256_pin()
    {
        using var fixture = new RuntimeFixture();
        var pin = fixture.WriteManifest("../outside.exe", "fixture", makeFile: false);

        var result = await AiStudioRuntimeIntegrityService.VerifyAsync(fixture.Root, pin);

        Assert.Equal(AiStudioRuntimeIntegrityStatus.InvalidManifest, result.Status);
    }

    [Fact]
    public async Task Rejects_pinned_malformed_manifest_json()
    {
        using var fixture = new RuntimeFixture();
        Directory.CreateDirectory(fixture.Root);
        var path = Path.Combine(fixture.Root, "runtime-integrity.json");
        File.WriteAllText(path, "{invalid-json");

        var result = await AiStudioRuntimeIntegrityService.VerifyAsync(
            fixture.Root, HashService.Sha256(path));

        Assert.Equal(AiStudioRuntimeIntegrityStatus.InvalidManifest, result.Status);
    }

    [Fact]
    public async Task Rejects_oversized_manifest_without_deserializing_it()
    {
        using var fixture = new RuntimeFixture();
        Directory.CreateDirectory(fixture.Root);
        var path = Path.Combine(fixture.Root, "runtime-integrity.json");
        File.WriteAllText(path, new string(' ', 256 * 1024 + 1));

        var result = await AiStudioRuntimeIntegrityService.VerifyAsync(
            fixture.Root, HashService.Sha256(path));

        Assert.Equal(AiStudioRuntimeIntegrityStatus.InvalidManifest, result.Status);
    }

    [Theory]
    [InlineData("/absolute/python.exe")]
    [InlineData("C:/Windows/System32/a.dll")]
    [InlineData("python\\python.exe")]
    [InlineData("python//python.exe")]
    [InlineData("python/./python.exe")]
    [InlineData("python/../python.exe")]
    public async Task Rejects_unsafe_manifest_member_paths(string path)
    {
        using var fixture = new RuntimeFixture();
        var pin = fixture.WriteManifest(path, "fixture", makeFile: false);

        var result = await AiStudioRuntimeIntegrityService.VerifyAsync(fixture.Root, pin);

        Assert.Equal(AiStudioRuntimeIntegrityStatus.InvalidManifest, result.Status);
    }

    [Theory]
    [InlineData("python/CON")]
    [InlineData("python/con.txt")]
    [InlineData("python/NUL.dll")]
    [InlineData("python/prn")]
    [InlineData("LPT9/output.bin")]
    [InlineData("COM1")]
    [InlineData("COM¹.exe")]
    [InlineData("python/bad?.dll")]
    [InlineData("python/a*b.dll")]
    [InlineData("python/quote\".dll")]
    [InlineData("python/bad|name.dll")]
    [InlineData("python/bad<name.dll")]
    [InlineData("python/bad>name.dll")]
    [InlineData("python/line\nfeed.dll")]
    public async Task Rejects_windows_reserved_or_invalid_member_paths(string memberPath)
    {
        using var fixture = new RuntimeFixture();
        var pin = fixture.WriteManifest(memberPath, "fixture", makeFile: false);

        var result = await AiStudioRuntimeIntegrityService.VerifyAsync(fixture.Root, pin);

        Assert.Equal(AiStudioRuntimeIntegrityStatus.InvalidManifest, result.Status);
    }

    [Fact]
    public async Task Rejects_duplicate_manifest_members_case_insensitively()
    {
        using var fixture = new RuntimeFixture();
        fixture.WriteManifest("python/python.exe", "fixture");
        var firstFile = new AiStudioRuntimeFile(
            "python/python.exe",
            7,
            HashService.Sha256(Path.Combine(fixture.Root, "python", "python.exe")));
        var manifest = new AiStudioRuntimeManifest(1, "ai-studio-runtime-win-x64", "1.0",
            [firstFile, firstFile with { Path = "Python/PYTHON.exe" }]);
        var pin = fixture.WriteRawManifest(manifest);

        var result = await AiStudioRuntimeIntegrityService.VerifyAsync(fixture.Root, pin);

        Assert.Equal(AiStudioRuntimeIntegrityStatus.InvalidManifest, result.Status);
    }

    [Fact]
    public async Task Rejects_an_unexpected_package_id()
    {
        using var fixture = new RuntimeFixture();
        var manifest = new AiStudioRuntimeManifest(1, "unrelated-package", "1.0",
            [new AiStudioRuntimeFile("python/python.exe", 0, new string('a', 64))]);
        var pin = fixture.WriteRawManifest(manifest);

        Assert.Equal(
            AiStudioRuntimeIntegrityStatus.InvalidManifest,
            (await AiStudioRuntimeIntegrityService.VerifyAsync(fixture.Root, pin)).Status);
    }

    [Fact]
    public async Task Propagates_explicit_cancellation()
    {
        using var fixture = new RuntimeFixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => AiStudioRuntimeIntegrityService.VerifyAsync(
                fixture.Root, new string('a', 64), cancellation.Token));
    }

    private sealed class RuntimeFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(
            Path.GetTempPath(), "ai-runtime-integrity-" + Guid.NewGuid().ToString("N"));

        public string WriteManifest(
            string memberPath, string data, bool makeFile = true)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(data);
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));

            if (makeFile)
            {
                var path = Path.Combine(Root, memberPath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, bytes);
            }

            return WriteRawManifest(new AiStudioRuntimeManifest(
                1, "ai-studio-runtime-win-x64", "1.0",
                [new AiStudioRuntimeFile(memberPath, bytes.LongLength, hash)]));
        }

        public string WriteRawManifest(AiStudioRuntimeManifest manifest)
        {
            Directory.CreateDirectory(Root);
            var path = Path.Combine(Root, "runtime-integrity.json");
            File.WriteAllText(path, JsonSerializer.Serialize(manifest));
            return HashService.Sha256(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }
}
