namespace DlssNrManager.Services;

/// <summary>
/// Read-only endpoint validation for a possible future ComfyUI adapter.
/// This neither opens a socket nor starts Python or an inference process.
/// </summary>
public static class AiStudioComfyUiEndpointPolicy
{
    public static bool TryValidate(string? candidate, out Uri? endpoint)
    {
        endpoint = null;

        if (string.IsNullOrWhiteSpace(candidate) ||
            !string.Equals(candidate, candidate.Trim(), StringComparison.Ordinal) ||
            candidate.Any(char.IsControl) ||
            !Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
            return false;

        // Numeric loopback only: never resolve localhost or a configurable DNS
        // name, which may redirect a future inference request off-device.
        var host = uri.Host.Trim('[', ']');
        var isIpv4 = uri.HostNameType == UriHostNameType.IPv4 &&
                     string.Equals(host, "127.0.0.1", StringComparison.Ordinal);
        var isIpv6 = uri.HostNameType == UriHostNameType.IPv6 &&
                     string.Equals(host, "::1", StringComparison.Ordinal);

        if (uri.Scheme != Uri.UriSchemeHttp ||
            !(isIpv4 || isIpv6) ||
            uri.Port is < 1024 or > 65535 ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.Equals(uri.AbsolutePath, "/", StringComparison.Ordinal) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
            return false;

        endpoint = uri;
        return true;
    }
}
