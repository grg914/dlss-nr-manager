using System.IO.Compression;
using DlssNrManager.Services;
using Xunit;

namespace DlssNrManager.Tests;

public sealed class GenericNvidiaRuntimeServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "DlssNrManager.GenericNvidia.Tests",
        Guid.NewGuid().ToString("N"));

    public GenericNvidiaRuntimeServiceTests()
        => Directory.CreateDirectory(_root);

    [Fact]
    public void Neural_rendering_requires_streamline_plugin_and_ngx_runtime()
    {
        var files = GenericNvidiaRuntimeService.RequiredFiles(
                new GenericNvidiaFeatureSelection(
                    SuperResolution: false,
                    FrameGeneration: false,
                    Reflex: false,
                    NeuralRendering: true))
            .ToArray();

        Assert.Contains("sl.dlss_nr.dll", files, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("nvngx_dlssnr.dll", files, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Local_package_inspection_reports_missing_neural_pair()
    {
        var zipPath = Path.Combine(_root, "runtime.zip");

        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            foreach (var name in new[] { "sl.interposer.dll", "sl.common.dll" })
            {
                var entry = archive.CreateEntry(name);
                using var writer = new StreamWriter(entry.Open());
                writer.Write("test");
            }
        }

        var service = new GenericNvidiaRuntimeService();
        var result = service.InspectLocalPackage(
            zipPath,
            new GenericNvidiaFeatureSelection(
                SuperResolution: false,
                FrameGeneration: false,
                Reflex: false,
                NeuralRendering: true));

        Assert.Contains(
            "sl.dlss_nr.dll",
            result.MissingRequiredFiles,
            StringComparer.OrdinalIgnoreCase);
        Assert.Contains(
            "nvngx_dlssnr.dll",
            result.MissingRequiredFiles,
            StringComparer.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, true);
        }
        catch
        {
        }
    }
}
