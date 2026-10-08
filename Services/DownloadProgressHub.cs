using System.Diagnostics;

namespace DlssNrManager.Services;

public sealed record DownloadProgressSnapshot(
    Guid OperationId,
    string Label,
    long BytesReceived,
    long? TotalBytes,
    double BytesPerSecond,
    TimeSpan? EstimatedRemaining,
    bool IsActive,
    string? Error)
{
    public double? Percent =>
        TotalBytes is > 0
            ? Math.Clamp(
                BytesReceived * 100d / TotalBytes.Value,
                0d,
                100d)
            : null;
}

public sealed class DownloadProgressHandle : IDisposable
{
    private readonly Guid _operationId;
    private readonly string _label;
    private readonly long? _totalBytes;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private long _bytesReceived;
    private long _lastReportedBytes;
    private TimeSpan _lastReportTime;
    private bool _completed;

    internal DownloadProgressHandle(
        Guid operationId,
        string label,
        long? totalBytes)
    {
        _operationId = operationId;
        _label = label;
        _totalBytes = totalBytes;

        DownloadProgressHub.Publish(
            new DownloadProgressSnapshot(
                _operationId,
                _label,
                0,
                _totalBytes,
                0,
                null,
                true,
                null));
    }

    public void Report(long bytesReceived)
    {
        if (_completed)
            return;

        _bytesReceived = Math.Max(0, bytesReceived);
        var now = _stopwatch.Elapsed;

        // Avoid flooding WPF/Dispatcher for every 128 KiB read.
        if (now - _lastReportTime < TimeSpan.FromMilliseconds(180) &&
            _totalBytes is not > 0)
        {
            return;
        }

        if (now - _lastReportTime < TimeSpan.FromMilliseconds(180) &&
            _bytesReceived < (_lastReportedBytes + 512 * 1024))
        {
            return;
        }

        _lastReportTime = now;
        _lastReportedBytes = _bytesReceived;

        var elapsedSeconds =
            Math.Max(_stopwatch.Elapsed.TotalSeconds, 0.001);

        var speed =
            _bytesReceived / elapsedSeconds;

        TimeSpan? remaining = null;
        if (_totalBytes is > 0 &&
            speed > 1 &&
            _bytesReceived < _totalBytes.Value)
        {
            remaining = TimeSpan.FromSeconds(
                (_totalBytes.Value - _bytesReceived) / speed);
        }

        DownloadProgressHub.Publish(
            new DownloadProgressSnapshot(
                _operationId,
                _label,
                _bytesReceived,
                _totalBytes,
                speed,
                remaining,
                true,
                null));
    }

    public void Complete()
    {
        if (_completed)
            return;

        _completed = true;
        _stopwatch.Stop();

        var elapsedSeconds =
            Math.Max(_stopwatch.Elapsed.TotalSeconds, 0.001);

        var speed =
            _bytesReceived / elapsedSeconds;

        DownloadProgressHub.Publish(
            new DownloadProgressSnapshot(
                _operationId,
                _label,
                _bytesReceived,
                _totalBytes,
                speed,
                TimeSpan.Zero,
                false,
                null));
    }

    public void Fail(Exception exception)
    {
        if (_completed)
            return;

        _completed = true;
        _stopwatch.Stop();

        DownloadProgressHub.Publish(
            new DownloadProgressSnapshot(
                _operationId,
                _label,
                _bytesReceived,
                _totalBytes,
                0,
                null,
                false,
                exception.Message));
    }

    public void Dispose()
    {
        if (!_completed)
            Complete();
    }
}

public static class DownloadProgressHub
{
    public static event Action<DownloadProgressSnapshot>? Changed;

    public static DownloadProgressHandle Begin(
        string label,
        long? totalBytes)
        => new(
            Guid.NewGuid(),
            string.IsNullOrWhiteSpace(label)
                ? "Téléchargement"
                : label,
            totalBytes);

    internal static void Publish(
        DownloadProgressSnapshot snapshot)
    {
        try
        {
            Changed?.Invoke(snapshot);
        }
        catch
        {
            // Progress UI must never break a download.
        }
    }
}
