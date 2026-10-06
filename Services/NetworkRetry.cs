using System.Net.Http;
using System.Net;

namespace DlssNrManager.Services;

public static class NetworkRetry
{
    public static async Task ExecuteAsync(
        Func<int, CancellationToken, Task> operation,
        CancellationToken cancellationToken,
        int attempts = 3)
    {
        if (attempts < 1)
            throw new ArgumentOutOfRangeException(nameof(attempts));

        Exception? last = null;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await operation(attempt, cancellationToken);
                return;
            }
            catch (Exception ex) when (
                attempt < attempts &&
                IsTransient(ex, cancellationToken))
            {
                last = ex;
                await Task.Delay(
                    TimeSpan.FromMilliseconds(500 * attempt * attempt),
                    cancellationToken);
            }
        }

        throw last ?? new InvalidOperationException(
            "Network operation failed without an exception.");
    }

    public static bool IsTransient(
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return false;

        if (exception is HttpRequestException http)
        {
            return http.StatusCode is null ||
                   http.StatusCode == HttpStatusCode.RequestTimeout ||
                   http.StatusCode == HttpStatusCode.TooManyRequests ||
                   (int?)http.StatusCode >= 500;
        }

        return exception is IOException or TimeoutException or TaskCanceledException;
    }
}
