using DlssNrManager.Services;
using Xunit;

namespace DlssNrManager.Tests;

public sealed class NvidiaDriverHealthServiceTests
{
    [Theory]
    [InlineData("617.42", true)]
    [InlineData(" 617.42 ", true)]
    [InlineData("617.4", false)]
    [InlineData("617.43", false)]
    [InlineData("617.420", false)]
    [InlineData("Unknown", false)]
    [InlineData(null, false)]
    public void Vendor_issue_notice_is_exact_release_only(string? version, bool expected)
        => Assert.Equal(expected, NvidiaDriverHealthService.HasDocumented61742Issue(version));

    [Fact]
    public void Quoted_nvidia_smi_csv_preserves_gpu_names_with_commas()
    {
        var rows = NvidiaDriverHealthService.ReadCsv(
            "GPU-1, \"GeForce RTX 5060 Ti, Edition\", 617.42\r\n" +
            "GPU-2, \"GeForce \"\"Special\"\"\", 617.42\n", 3);
        Assert.Equal(2, rows.Count);
        Assert.Equal("GeForce RTX 5060 Ti, Edition", rows[0][1]);
        Assert.Equal("GeForce \"Special\"", rows[1][1]);
        Assert.Equal("GPU-2", rows[1][0]);
    }

    [Theory]
    [InlineData("GPU-1, RTX 5060 Ti", 3)]
    [InlineData("GPU-1, \"unclosed, 617.42", 3)]
    [InlineData("", 3)]
    [InlineData("GPU-1, RTX, 617.42", 2)]
    public void Malformed_csv_is_rejected(string csv, int expectedFields)
        => Assert.Empty(NvidiaDriverHealthService.ReadCsv(csv, expectedFields));

    [Theory]
    [InlineData("N/A", null)]
    [InlineData("", null)]
    [InlineData("-1", null)]
    [InlineData("garbage", null)]
    [InlineData("25.5", 25.5)]
    [InlineData("0", 0)]
    public void Optional_metrics_use_nullable_values(string text, double? expected)
        => Assert.Equal(expected, NvidiaDriverHealthService.ReadMetric(text));

    [Fact]
    public void Cpu_usage_is_normalized_across_logical_processors()
    {
        var normalized = NvidiaDriverHealthService.CpuPercent(
            TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(2), 8);
        Assert.Equal(25d, normalized);
        Assert.Null(NvidiaDriverHealthService.CpuPercent(
            TimeSpan.FromSeconds(1), TimeSpan.Zero, 8));
        Assert.Null(NvidiaDriverHealthService.CpuPercent(
            TimeSpan.FromSeconds(-1), TimeSpan.FromSeconds(2), 8));
        Assert.Null(NvidiaDriverHealthService.CpuPercent(
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), 0));
    }

    [Fact]
    public void Read_only_report_does_not_claim_a_known_issue_is_a_confirmed_pc_fault()
    {
        var report = new NvidiaDriverHealthReport(
            DateTimeOffset.UtcNow, "RTX 5060 Ti", "617.42",
            null, null, null, null, null, null, false, false, []);
        var en = NvidiaDriverHealthService.Format(report, false);
        var fr = NvidiaDriverHealthService.Format(report, true);
        Assert.Contains("#6007998", en);
        Assert.Contains("unconfirmed", en, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("non confirmé", fr, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Unavailable", en);
        Assert.Contains("Indisponible", fr);
        Assert.Contains("No driver settings", en);
    }

    [Fact]
    public void Other_driver_is_unknown_not_declared_healthy()
    {
        var report = new NvidiaDriverHealthReport(
            DateTimeOffset.UtcNow, "RTX 5060 Ti", "617.43",
            5d, 2048d, 450d, 42d, 21d, 0.1d, false, true, []);
        Assert.False(report.HasVendorNotice);
        Assert.Contains("does NOT prove the driver is healthy",
            NvidiaDriverHealthService.Format(report, false));
    }
}
