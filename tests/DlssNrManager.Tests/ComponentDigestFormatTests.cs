using DlssNrManager.Services;
using Xunit;

namespace DlssNrManager.Tests;

public sealed class ComponentDigestFormatTests
{
    [Fact]
    public void Exactly_64_hex_digits_are_required()
    {
        Assert.True(ComponentUpdateService.IsValidComponentDigest(
            new string('a', 64)));
        Assert.True(ComponentUpdateService.IsValidComponentDigest(
            new string('F', 64)));
        Assert.False(ComponentUpdateService.IsValidComponentDigest(null));
        Assert.False(ComponentUpdateService.IsValidComponentDigest(""));
        Assert.False(ComponentUpdateService.IsValidComponentDigest(new string('a', 63)));
        Assert.False(ComponentUpdateService.IsValidComponentDigest(new string('a', 65)));
        Assert.False(ComponentUpdateService.IsValidComponentDigest(
            new string('g', 64)));
        Assert.False(ComponentUpdateService.IsValidComponentDigest(
            new string(' ', 64)));
    }

    [Fact]
    public void Manifest_ingestion_uses_strict_digest_validation()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null &&
               !File.Exists(Path.Combine(directory.FullName, "DlssNrManager.csproj")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var source = File.ReadAllText(Path.Combine(
            directory!.FullName, "Services", "ComponentUpdateService.cs"));
        Assert.Contains("!IsValidComponentDigest(sha256)", source);
    }
}
