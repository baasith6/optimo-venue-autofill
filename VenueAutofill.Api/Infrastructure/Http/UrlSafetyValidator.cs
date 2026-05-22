using System.Net;
using System.Net.Sockets;

namespace VenueAutofill.Api.Infrastructure.Http;

public class UrlSafetyValidator
{
    private static readonly HashSet<string> BlockedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "localhost", "127.0.0.1", "0.0.0.0", "::1", "metadata.google.internal"
    };

    public bool IsSafeUrl(string? url, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(url))
            return true;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            error = "Invalid source URL.";
            return false;
        }

        if (uri.Scheme is not "http" and not "https")
        {
            error = "Source URL must use http or https.";
            return false;
        }

        if (BlockedHosts.Contains(uri.Host))
        {
            error = "Source URL host is not allowed.";
            return false;
        }

        if (IPAddress.TryParse(uri.Host, out var ip))
        {
            if (IsPrivateOrReserved(ip))
            {
                error = "Source URL points to a private or reserved IP.";
                return false;
            }
        }
        else if (uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
                 || uri.Host.Contains("internal", StringComparison.OrdinalIgnoreCase))
        {
            error = "Source URL host is not allowed.";
            return false;
        }

        return true;
    }

    private static bool IsPrivateOrReserved(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip))
            return true;

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = ip.GetAddressBytes();
            return bytes[0] == 10
                   || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                   || (bytes[0] == 192 && bytes[1] == 168)
                   || bytes[0] == 169 && bytes[1] == 254
                   || bytes[0] == 0;
        }

        return false;
    }
}
