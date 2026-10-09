using DlssNrManager.Services;
using Xunit;

namespace DlssNrManager.Tests;

public sealed class MinecraftCausticaChannelTests
{
    [Theory]
    [InlineData("Caustica-RTX-Minecraft-26.2-build-46.jar", true)]
    [InlineData("Caustica-RTX-Minecraft-26.2-ReSTIR-experimental.jar", false)]
    [InlineData("Caustica-RTX-Minecraft-26.2-preview.jar", false)]
    [InlineData("Caustica-RTX-Minecraft-26.2-snapshot.jar", false)]
    [InlineData("Caustica-RTX-Minecraft-26.2-sources.jar", false)]
    [InlineData("Caustica-RTX-Minecraft-26.2-dev.jar", false)]
    [InlineData("SomethingUnrelated.jar", false)]
    public void OnlyProductionCausticaAssetsAreEligibleForStableInstall(
        string assetName, bool expected)
    {
        Assert.Equal(expected,
            MinecraftIntegrationService.IsProductionCausticaJar(assetName));
    }
}
