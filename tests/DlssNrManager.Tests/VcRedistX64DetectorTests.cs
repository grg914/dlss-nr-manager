using DlssNrManager.Services;
using Xunit;

namespace DlssNrManager.Tests;

public sealed class VcRedistX64DetectorTests
{
    [Theory]
    [InlineData(1, "v14.50.35719.0", "v14.50.35719.0")]
    [InlineData(1, " 14.44.35211 ", "14.44.35211")]
    [InlineData(1, "", null)]
    [InlineData(1, null, null)]
    public void RegisteredX64RedistributableIsDetected(
        int installed,
        string? version,
        string? expectedVersion)
    {
        var result = VcRedistX64Detector.FromRegistryValues(installed, version);

        Assert.Equal(VcRedistX64State.Installed, result.State);
        Assert.Equal(expectedVersion, result.Version);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(-1)]
    public void UninstalledFlagIsNotDetected(int flag)
    {
        var result = VcRedistX64Detector.FromRegistryValues(flag, "14.50");

        Assert.Equal(VcRedistX64State.NotDetected, result.State);
        Assert.Null(result.Version);
    }

    [Fact]
    public void MissingOrUnexpectedInstalledFlagIsNotAnInstallation()
    {
        Assert.Equal(
            VcRedistX64State.NotDetected,
            VcRedistX64Detector.FromRegistryValues(null, null).State);
        Assert.Equal(
            VcRedistX64State.NotDetected,
            VcRedistX64Detector.FromRegistryValues("1", "14.50").State);
    }
}
