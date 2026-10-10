using Xunit;

namespace DlssNrManager.Tests;

public sealed class AiOriginImmutableReleasePinTests
{
    [Fact]
    public void Detector_model_downloads_use_only_approved_immutable_v3_2_0_assets()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null &&
               !File.Exists(Path.Combine(directory.FullName, "DlssNrManager.csproj")))
            directory = directory.Parent;
        Assert.NotNull(directory);

        var source = File.ReadAllText(Path.Combine(
            directory!.FullName, "Services", "AiOriginDetectionService.cs"));

        Assert.Contains(
            "https://github.com/grg914/dlss-nr-manager/releases/download/v3.2.0/ai-origin-primary-int8.onnx",
            source);
        Assert.Contains(
            "https://github.com/grg914/dlss-nr-manager/releases/download/v3.2.0/ai-origin-secondary-int8.onnx",
            source);
        Assert.DoesNotContain("/releases/latest/download/ai-origin-", source);
        Assert.Contains(
            "08B349F1B535F2F0CC2A8610BBF57C27593A0364E78B6C91205C0FF2BF29D714",
            source);
        Assert.Contains(
            "7273CB9CD81E17EAE04771010D2199BA6AE34EA2A75A275518C0BC4A2C26FFD2",
            source);
    }
}
