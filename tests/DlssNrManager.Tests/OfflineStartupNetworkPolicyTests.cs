using System.Net;
using DlssNrManager.Services;
using Xunit;

namespace DlssNrManager.Tests;

public sealed class OfflineStartupNetworkPolicyTests
{
    [Theory]
    [InlineData(HttpStatusCode.OK, true)]
    [InlineData(HttpStatusCode.NoContent, true)]
    [InlineData(HttpStatusCode.Forbidden, true)]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.BadGateway, false)]
    [InlineData(HttpStatusCode.ServiceUnavailable, false)]
    public void Connectivity_probe_distinguishes_reachable_api_from_server_failure(
        HttpStatusCode status, bool reachable)
    {
        Assert.Equal(reachable, StartupInternetConnectivity.IsReachableStatus(status));
    }

    [Fact]
    public void Offline_startup_must_skip_all_background_release_checks_and_remote_artwork()
    {
        var root = FindProjectRoot();
        var window = File.ReadAllText(Path.Combine(root, "MainWindow.xaml.cs"));
        var start = window.IndexOf("private async Task InitializeAsync()", StringComparison.Ordinal);
        var end = window.IndexOf("private async Task RefreshMinecraftCausticaBuildAsync(", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var startup = window[start..end];

        var probe = startup.IndexOf("await StartupInternetConnectivity.IsAvailableAsync()", StringComparison.Ordinal);
        var onlineBranch = startup.IndexOf("if (internetAvailable)", StringComparison.Ordinal);
        var releases = startup.IndexOf("RefreshReleaseAsync(),", StringComparison.Ordinal);
        var managerUpdates = startup.IndexOf("CheckManagerUpdateAsync(),", StringComparison.Ordinal);
        var causticaUpdates = startup.IndexOf("RefreshMinecraftCausticaBuildAsync());", StringComparison.Ordinal);
        var offlineScan = startup.IndexOf(
            "ScanGamesAsync(forceRefresh: false, allowNetwork: false)", StringComparison.Ordinal);
        var optIn = startup.IndexOf(
            "if (internetAvailable && AutoUpdateComponentsCheck.IsChecked == true)", StringComparison.Ordinal);

        Assert.True(probe >= 0 && onlineBranch > probe);
        Assert.True(releases > onlineBranch && managerUpdates > onlineBranch && causticaUpdates > onlineBranch);
        Assert.True(offlineScan > causticaUpdates && optIn > offlineScan);
    }

    [Fact]
    public void Local_artwork_lookup_must_return_before_remote_store_queries()
    {
        var source = File.ReadAllText(Path.Combine(
            FindProjectRoot(), "Services", "GameArtworkService.cs"));
        Assert.Contains("if (allowNetwork &&", source);
        Assert.Contains("if (!allowNetwork)", source);
        var offlineGuard = source.IndexOf("if (!allowNetwork)", StringComparison.Ordinal);
        var remoteLookup = source.IndexOf(
            "remote = await GetSteamArtworkByAppIdAsync(", StringComparison.Ordinal);

        Assert.True(offlineGuard >= 0 && remoteLookup > offlineGuard);
    }

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null &&
               !File.Exists(Path.Combine(directory.FullName, "DlssNrManager.csproj")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
