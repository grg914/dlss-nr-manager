using DlssNrManager.Services;
using Xunit;

namespace DlssNrManager.Tests;

public sealed class AppLoggerSafetyTests
{
    [Theory]
    [InlineData("Authorization: Bearer verySecretToken123456", "verySecretToken123456")]
    [InlineData("https://example.test/?token=topSecret777&mode=offline", "topSecret777")]
    [InlineData("api_key=secretApiKeyValue", "secretApiKeyValue")]
    [InlineData("password: \"secretPassValue\"", "secretPassValue")]
    [InlineData("refresh_token=secretRefreshToken", "secretRefreshToken")]
    [InlineData("client_secret=secretClientKey", "secretClientKey")]
    [InlineData("github_pat_abcdefghijklmnopqrstuvwxyz0123456789", "github_pat_abcdefghijklmnopqrstuvwxyz0123456789")]
    [InlineData("ghp_abcdefghijklmnopqrstuvwxyz0123456789", "ghp_abcdefghijklmnopqrstuvwxyz0123456789")]
    public void Redacts_common_credentials_before_writing_logs(string message, string secret)
    {
        var sanitized = AppLogger.RedactSensitiveData(message);
        Assert.DoesNotContain(secret, sanitized, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", sanitized, StringComparison.Ordinal);
    }

    [Fact]
    public void Preserves_safe_context_when_redacting_query_parameters()
    {
        var redacted = AppLogger.RedactSensitiveData(
            "GET https://example.test/api?token=hiddenValue&mode=offline failed");
        Assert.Contains("mode=offline", redacted);
        Assert.DoesNotContain("hiddenValue", redacted);
    }
}
