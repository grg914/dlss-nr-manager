using Xunit;

namespace DlssNrManager.Tests;

public sealed class DownloadCenterDetailsSafetyTests
{
    private static string ReadSource(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "DlssNrManager.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, name));
    }

    [Fact]
    public void Selecting_a_model_must_only_read_local_details()
    {
        var service = ReadSource("Services/DownloadCenterService.cs");
        var start = service.IndexOf("public Task<string> GetDetailsAsync(", StringComparison.Ordinal);
        var end = service.IndexOf("public async Task<MediaUpdateAvailability> CheckAiStudioModelUpdateAsync(", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var details = service[start..end];

        Assert.Contains("ReadInstalledReceipt(model)", details);
        Assert.Contains("Task.FromResult(", details);
        Assert.DoesNotContain("FindLatestPackageAsync(", details);
        Assert.DoesNotContain("_http.", details);

        // The explicit refresh/update check still owns remote release discovery.
        Assert.Contains("FindLatestPackageAsync(", service[end..]);
    }

    [Fact]
    public void Old_async_details_cannot_overwrite_current_selection()
    {
        var ui = ReadSource("MainWindow.xaml.cs");
        var start = ui.IndexOf("private async Task RefreshDownloadCenterSelectionAsync()", StringComparison.Ordinal);
        var end = ui.IndexOf("private void RefreshDownloadCenterButtons()", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var details = ui[start..end];

        Assert.Contains("var revision = ++_downloadDetailsRevision;", details);
        Assert.Contains("revision != _downloadDetailsRevision", details);
        Assert.Contains("SelectedDownloadCenterEntry()?.Id != entry.Id", details);
        Assert.Contains("DownloadCenterDetailsText.Text = details;", details);
        Assert.True(details.IndexOf("revision != _downloadDetailsRevision", StringComparison.Ordinal) <
                    details.IndexOf("DownloadCenterDetailsText.Text = details;", StringComparison.Ordinal));
    }
}
