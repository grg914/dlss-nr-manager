namespace DlssNrManager.Services;

public sealed record LargeDownloadApprovalRequest(
    string Label,
    long TotalBytes,
    string Purpose)
{
    public double TotalGiB =>
        TotalBytes / (1024d * 1024d * 1024d);
}

public static class LargeDownloadApprovalHub
{
    public const long ApprovalThresholdBytes =
        1024L * 1024 * 1024;

    public static Func<
        LargeDownloadApprovalRequest,
        CancellationToken,
        Task<bool>>? ApprovalRequested { get; set; }

    public static async Task EnsureApprovedAsync(
        string label,
        long? totalBytes,
        string purpose,
        CancellationToken cancellationToken = default)
    {
        if (totalBytes is not > ApprovalThresholdBytes)
            return;

        var handler = ApprovalRequested
            ?? throw new InvalidOperationException(
                "A download larger than 1 GB requires explicit user approval, but no approval UI is available.");

        var approved = await handler(
            new LargeDownloadApprovalRequest(
                string.IsNullOrWhiteSpace(label)
                    ? "Téléchargement"
                    : label,
                totalBytes.Value,
                string.IsNullOrWhiteSpace(purpose)
                    ? "Téléchargement manager-owned"
                    : purpose),
            cancellationToken);

        if (!approved)
        {
            throw new OperationCanceledException(
                $"Large download rejected by user: {label}",
                cancellationToken);
        }
    }
}
