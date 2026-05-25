namespace VenueAutofill.Api.Infrastructure.Http;

public static class ImageUrlNormalizer
{
    public static string NormalizeForComparison(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return url.Trim().ToLowerInvariant();

        var builder = new UriBuilder(uri) { Query = string.Empty, Fragment = string.Empty };
        return builder.Uri.ToString().TrimEnd('/').ToLowerInvariant();
    }

    public static string SanitizeForResponse(string url)
    {
        if (!url.Contains("key=", StringComparison.OrdinalIgnoreCase))
            return url;

        var idx = url.IndexOf('?', StringComparison.Ordinal);
        if (idx < 0)
            return url;

        var basePart = url[..idx];
        var parts = url[(idx + 1)..].Split('&')
            .Where(p => !p.StartsWith("key=", StringComparison.OrdinalIgnoreCase))
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToArray();

        return parts.Length == 0 ? basePart : $"{basePart}?{string.Join('&', parts)}";
    }
}
