using DlssNrManager.Services;
using Xunit;

namespace DlssNrManager.Tests;

/// <summary>
/// Guard against introducing network version checks that survive window close.
/// The physical end-to-end Windows RTX shutdown matrix remains issue #95.
/// </summary>
public sealed class DownloadCenterUpdateProbeShutdownTests
{
    private static string WindowSource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null &&
               !File.Exists(Path.Combine(directory.FullName, "DlssNrManager.csproj")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory!.FullName, "MainWindow.xaml.cs"));
    }

    [Fact]
    public void Window_close_cancels_both_version_lookup_tokens()
    {
        var source = WindowSource();
        var begin = source.IndexOf("Closed += (_, _) =>", StringComparison.Ordinal);
        var end = source.IndexOf("    private async Task InitializeAsync()", begin, StringComparison.Ordinal);
        Assert.True(begin >= 0 && end > begin);
        var cleanup = source[begin..end];
        Assert.Contains("_isClosed = true;", cleanup);
        Assert.Contains("_downloadUpdateCheckCts?.Cancel();", cleanup);
        Assert.Contains("_officialUpdateCheckCts?.Cancel();", cleanup);
        Assert.Contains("ExternalProcessTracker.Shutdown();", cleanup);
    }

    [Fact]
    public void Window_close_cancels_but_does_not_dispose_tokens_owned_by_active_async_operations()
    {
        var source = WindowSource();
        var begin = source.IndexOf("Closed += (_, _) =>", StringComparison.Ordinal);
        var end = source.IndexOf("    private async Task InitializeAsync()", begin, StringComparison.Ordinal);
        Assert.True(begin >= 0 && end > begin);
        var cleanup = source[begin..end];
        foreach (var name in new[]
        {
            "_mediaOperationCts",
            "_permanentVideoCts",
            "_aiOriginCts",
            "_downloadCenterCts"
        })
        {
            Assert.Contains(name + "?.Cancel();", cleanup);
            Assert.DoesNotContain(name + "?.Dispose();", cleanup);
        }
    }

    [Fact]
    public void Download_operations_release_own_tokens_before_optional_closed_ui_refresh()
    {
        var source = WindowSource();
        var standardBegin = source.IndexOf("private async Task RunDownloadCenterOperationAsync(", StringComparison.Ordinal);
        var manualBegin = source.IndexOf("private async Task RunManualLicensedModelInstallAsync(", standardBegin, StringComparison.Ordinal);
        var removeBegin = source.IndexOf("private void RemoveDownloadCenter_Click(", manualBegin, StringComparison.Ordinal);
        Assert.True(standardBegin >= 0 && manualBegin > standardBegin && removeBegin > manualBegin);

        foreach (var operation in new[]
        {
            source[standardBegin..manualBegin],
            source[manualBegin..removeBegin]
        })
        {
            Assert.Contains("_downloadCenterCts.Dispose();", operation);
            Assert.Contains("_downloadCenterCts = null;", operation);
            Assert.Contains("if (!_isClosed)", operation);
            Assert.Contains("await RefreshDownloadCenterSelectionAsync();", operation);
        }
    }

    [Fact]
    public void Both_version_lookups_forward_their_cancellation_token()
    {
        var source = WindowSource();
        var center = source.IndexOf("private async void RefreshDownloadCenter_Click(", StringComparison.Ordinal);
        var details = source.IndexOf("private sealed record OfficialUpdateDisplay(", center, StringComparison.Ordinal);
        var official = source.IndexOf("private async void CheckOfficialUpstreamUpdates_Click(", StringComparison.Ordinal);
        var selection = source.IndexOf("private void OfficialUpdateList_SelectionChanged(", official, StringComparison.Ordinal);
        Assert.True(center >= 0 && details > center && official > details && selection > official);
        var centerCode = source[center..details];
        var officialCode = source[official..selection];

        Assert.Contains("CheckMediaUpdateAsync(_media, lookupCts.Token)", centerCode);
        Assert.Contains("CheckAiStudioModelUpdateAsync(selected, lookupCts.Token)", centerCode);
        Assert.Contains("_downloadUpdateCheckCts = null;", centerCode);
        Assert.Contains("lookupCts.Token.ThrowIfCancellationRequested();", centerCode);
        Assert.Contains("_officialUpdates.CheckAsync(lookupCts.Token)", officialCode);
        Assert.Contains("_officialUpdateCheckCts = null;", officialCode);
        Assert.Contains("if (_isClosed) return;", officialCode);
    }
}
