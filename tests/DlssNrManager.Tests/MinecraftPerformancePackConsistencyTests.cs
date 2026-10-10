using System.Text.Json;
using DlssNrManager.Services;
using Xunit;

namespace DlssNrManager.Tests;

public sealed class MinecraftPerformancePackConsistencyTests
{
    private const string English =
        "Performance pack without renderer replacement (Lithium + FerriteCore + Krypton + BadOptimizations + Dynamic FPS)";
    private const string French =
        "Pack de performances sans remplacement du moteur de rendu (Lithium + FerriteCore + Krypton + BadOptimizations + Dynamic FPS)";

    [Fact]
    public void Advertised_performance_pack_has_bilingual_labels()
    {
        Assert.Equal(French, UiLocalizationService.Translate(English, "fr"));
        Assert.Equal(English, UiLocalizationService.Translate(French, "en"));
    }

    [Fact]
    public void Excluded_c2me_is_not_requested_or_advertised()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "DlssNrManager.csproj")))
            root = root.Parent;

        Assert.NotNull(root);
        var rootPath = root!.FullName;
        using var runtimeLock = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(rootPath, "third_party", "minecraft", "RUNTIME.lock.json")));
        Assert.Contains(
            runtimeLock.RootElement.GetProperty("excluded_components").EnumerateArray(),
            entry => entry.GetProperty("name").GetString() == "C2ME");

        var installer = File.ReadAllText(Path.Combine(rootPath, "Services", "MinecraftIntegrationService.cs"));
        var oneClick = File.ReadAllText(Path.Combine(rootPath, "Services", "MinecraftOneClickService.cs"));
        var xaml = File.ReadAllText(Path.Combine(rootPath, "MainWindow.xaml"));
        Assert.DoesNotContain("new MinecraftProject(\"C2ME\"", installer);
        Assert.DoesNotContain("\"C2ME\",", oneClick);
        Assert.DoesNotContain("+ C2ME +", xaml);
        Assert.Contains(English, xaml);
    }
}
