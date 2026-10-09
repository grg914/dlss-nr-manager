using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;

namespace DlssNrManager.Services;

/// <summary>
/// One bounded connectivity probe at startup, not a background network monitor.
/// A local network adapter alone is not evidence that the release API is reachable.
/// </summary>
public static class StartupInternetConnectivity
{
    private static readonly HttpClient ProbeClient = new()
    {
        Timeout = TimeSpan.FromSeconds(3)
    };

    public static bool IsReachableStatus(HttpStatusCode status) =>
        (int)status is >= 200 and < 500;

    public static async Task<bool> IsAvailableAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!NetworkInterface.GetIsNetworkAvailable())
                return false;

            using var request = new HttpRequestMessage(
                HttpMethod.Head, "https://api.github.com/");
            request.Headers.UserAgent.ParseAdd(
                "DlssNrManager/" + AppIdentity.UserAgentVersion);
            using var response = await ProbeClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            return IsReachableStatus(response.StatusCode);
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (NetworkInformationException)
        {
            return false;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
