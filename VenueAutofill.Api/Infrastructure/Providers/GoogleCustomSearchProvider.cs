using System.Text.Json;
using Microsoft.Extensions.Options;
using VenueAutofill.Api.Application.Interfaces;
using VenueAutofill.Api.Configuration;
using VenueAutofill.Api.Contracts.Internal;
using VenueAutofill.Api.Contracts.Requests;

namespace VenueAutofill.Api.Infrastructure.Providers;

public class GoogleCustomSearchProvider : IGoogleCustomSearchService
{
    private static readonly string[] BlockedPathSegments =
    [
        "/search", "/s/", "/hotels?", "/flights?", "/packages?", "/help", "/about"
    ];

    private readonly HttpClient _httpClient;
    private readonly GoogleCustomSearchOptions _options;
    private readonly ILogger<GoogleCustomSearchProvider> _logger;

    public GoogleCustomSearchProvider(
        HttpClient httpClient,
        IOptions<GoogleCustomSearchOptions> options,
        ILogger<GoogleCustomSearchProvider> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string?> FindListingUrlAsync(
        VenueAutofillRequest request,
        BookingPlatformEntry platform,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey) || string.IsNullOrWhiteSpace(_options.SearchEngineId))
        {
            _logger.LogDebug("Custom Search not configured; skipping {Platform}", platform.PlatformId);
            return null;
        }

        var query = $"\"{request.VenueName}\" {request.City} {request.Country} site:{platform.Domain}";
        var url = $"{_options.BaseUrl}?key={Uri.EscapeDataString(_options.ApiKey)}&cx={Uri.EscapeDataString(_options.SearchEngineId)}&q={Uri.EscapeDataString(query)}&num=3";

        try
        {
            using var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("CSE failed for {Platform}: {Status}", platform.PlatformId, response.StatusCode);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (!doc.RootElement.TryGetProperty("items", out var items))
                return null;

            foreach (var item in items.EnumerateArray())
            {
                if (!item.TryGetProperty("link", out var linkProp))
                    continue;
                var link = linkProp.GetString();
                if (IsAcceptableListingUrl(link, platform.Domain))
                {
                    _logger.LogInformation("CSE found listing for {Platform}: {Url}", platform.PlatformId, link);
                    return link;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "CSE error for {Platform}", platform.PlatformId);
        }

        return null;
    }

    private static bool IsAcceptableListingUrl(string? url, string domain)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;
        if (!uri.Scheme.StartsWith("https", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!uri.Host.Contains(domain, StringComparison.OrdinalIgnoreCase))
            return false;

        var path = uri.AbsolutePath.ToLowerInvariant();
        if (path is "/" or "")
            return false;
        if (BlockedPathSegments.Any(seg => path.Contains(seg, StringComparison.OrdinalIgnoreCase)))
            return false;

        return true;
    }
}
