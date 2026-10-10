using System.IO.Compression;
using DlssNrManager.Models;
using DlssNrManager.Services;
using Xunit;

namespace DlssNrManager.Tests;

public sealed class DiagnosticBundlePrivacyTests
{
    [Fact]
    public void Exported_game_logs_and_ini_must_not_leak_credentials()
    {
        var workspace = Path.Combine(
            Path.GetTempPath(), "dlssnr-privacy-" + Guid.NewGuid().ToString("N"));
        var gameDir = Path.Combine(workspace, "sample-game");
        var bundle = Path.Combine(workspace, "support.zip");
        Directory.CreateDirectory(gameDir);

        try
        {
            File.WriteAllText(Path.Combine(gameDir, "OptiScaler.log"),
                "Starting OptiScaler\nAuthorization: Bearer externalBearerSecret123\n" +
                "Authorization: Basic basicAuthSecret123\n" +
                "https://example.test/api?access_token=externalQuerySecret123&status=ready\n");

            File.WriteAllText(Path.Combine(gameDir, "OptiScaler.ini"),
                "[Diagnostics]\npassword=externalIniSecret123\n" +
                "raw_json={\"api_key\":\"externalJsonSecret123\"}\n");

            var service = new DiagnosticService();
            var destination = service.CreateSupportBundle(
                gameDir,
                new GpuInfo("Test GPU", "Unknown", false),
                new InstallState(false, null, null, false, null, false),
                bundle);

            Assert.Equal(bundle, destination);
            using var archive = ZipFile.OpenRead(bundle);
            Assert.Contains(archive.Entries, x => x.FullName == "game/OptiScaler.log");
            Assert.Contains(archive.Entries, x => x.FullName == "game/OptiScaler.ini");

            var contents = new List<string>();
            foreach (var entry in archive.Entries)
            {
                using var reader = new StreamReader(entry.Open());
                contents.Add(reader.ReadToEnd());
            }

            var allEntries = string.Join("\n", contents);
            foreach (var secret in new[]
                     {
                         "externalBearerSecret123",
                         "basicAuthSecret123",
                         "externalQuerySecret123",
                         "externalIniSecret123",
                         "externalJsonSecret123"
                     })
            {
                Assert.DoesNotContain(secret, allEntries, StringComparison.Ordinal);
            }
            Assert.Contains("[REDACTED]", allEntries, StringComparison.Ordinal);
            Assert.Contains("status=ready", allEntries, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(workspace))
                Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    public void Redacted_json_credential_remains_valid_json()
    {
        const string original = "{\"api_key\":\"neverPersistSecret123\",\"mode\":\"offline\"}";
        var redacted = AppLogger.RedactSensitiveData(original);
        using var parsed = System.Text.Json.JsonDocument.Parse(redacted);
        Assert.Equal("[REDACTED]",
            parsed.RootElement.GetProperty("api_key").GetString());
        Assert.Equal("offline",
            parsed.RootElement.GetProperty("mode").GetString());
        Assert.DoesNotContain("neverPersistSecret123", redacted, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{\"access_token\": \"jsonValueSecret123\"}", "jsonValueSecret123")]
    [InlineData("Authorization: Basic encodedBasicSecret123", "encodedBasicSecret123")]
    [InlineData("{\"client_secret\":\"jsonClientSecret123\"}", "jsonClientSecret123")]
    public void Shared_redactor_masks_external_secret_formats(string input, string secret)
    {
        var output = AppLogger.RedactSensitiveData(input);
        Assert.DoesNotContain(secret, output, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", output, StringComparison.Ordinal);
    }
}
