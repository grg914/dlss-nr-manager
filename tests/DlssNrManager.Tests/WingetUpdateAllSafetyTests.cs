using DlssNrManager.Services;
using Xunit;

namespace DlssNrManager.Tests;

public sealed class WingetUpdateAllSafetyTests
{
    [Fact]
    public void UpdateAll_respects_user_pins_and_skips_unknown_installed_versions()
    {
        var arguments = PcUpdateService.BuildWingetUpgradeAllArguments();
        Assert.Equal("upgrade", arguments[0]);
        Assert.Contains("--all", arguments);
        Assert.DoesNotContain("--include-pinned", arguments);
        Assert.DoesNotContain("--include-unknown", arguments);
        Assert.Contains("--disable-interactivity", arguments);
    }

    [Fact]
    public void Production_update_uses_the_safe_argument_builder()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "DlssNrManager.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var source = File.ReadAllText(Path.Combine(dir!.FullName, "Services", "PcUpdateService.cs"));
        var start = source.IndexOf("public async Task<string> UpdateAllWingetAsync(", StringComparison.Ordinal);
        var end = source.IndexOf("private async Task<PcUpdateItem> ScanNvidiaDriverStatusAsync(", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        Assert.Contains("BuildWingetUpgradeAllArguments()", source[start..end]);
    }
}
