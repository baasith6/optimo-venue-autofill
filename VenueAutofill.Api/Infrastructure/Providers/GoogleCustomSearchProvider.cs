using System.Net;
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

    public Task<CseSearchResult> FindListingAsync(
        VenueAutofillRequest request,
        BookingPlatformEntry platform,
        CancellationToken cancellationToken = default) =>
        FindListingInternalAsync(request, platform, useSiteRestriction: true, cancellationToken);

    private async Task<CseSearchResult> FindListingInternalAsync(
        VenueAutofillRequest request,
        BookingPlatformEntry platform,
        bool useSiteRestriction,
        CancellationToken cancellationToken)
    {
        var apiKey = _options.ApiKey.Trim();
        var searchEngineId = _options.SearchEngineId.Trim();

        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(searchEngineId))
        {
            _logger.LogDebug("Custom Search not configured; skipping {Platform}", platform.PlatformId);
            return new CseSearchResult { SkipReason = "cse_not_configured" };
        }

        var query = useSiteRestriction
            ? $"\"{request.VenueName}\" \"{request.City}\" site:{platform.Domain}"
            : $"\"{request.VenueName}\" \"{request.City}\" {request.Country} {platform.Label} hotel";

        var url = $"{_options.BaseUrl}?key={Uri.EscapeDataString(apiKey)}&cx={Uri.EscapeDataString(searchEngineId)}&q={Uri.EscapeDataString(query)}&num=5";

        try
        {
            using var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                var googleMessage = TryParseGoogleErrorMessage(errorBody);
                var skipReason = MapHttpStatusToSkipReason(response.StatusCode);

                _logger.LogWarning(
                    "CSE failed for {Platform}: {Status}, skipReason={SkipReason}, query={Query}, googleError={GoogleError}",
                    platform.PlatformId,
                    (int)response.StatusCode,
                    skipReason,
                    query,
                    googleMessage ?? errorBody[..Math.Min(errorBody.Length, 300)]);

                return new CseSearchResult { SkipReason = skipReason };
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (!doc.RootElement.TryGetProperty("items", out var items))
            {
                _logger.LogInformation(
                    "CSE returned no items for {Platform}, query={Query}",
                    platform.PlatformId, query);

                if (useSiteRestriction)
                    return await FindListingInternalAsync(request, platform, useSiteRestriction: false, cancellationToken);

                return new CseSearchResult { SkipReason = "no_listing_found" };
            }

            var count = 0;
            foreach (var item in items.EnumerateArray())
            {
                count++;
                if (!item.TryGetProperty("link", out var linkProp))
                    continue;
                var link = linkProp.GetString();
                if (IsAcceptableListingUrl(link, platform.Domain))
                {
                    _logger.LogInformation(
                        "CSE found listing for {Platform}: {Url} (results={Count}, query={Query})",
                        platform.PlatformId, link, count, query);
                    return new CseSearchResult { Url = link };
                }
            }

            _logger.LogInformation(
                "CSE items not acceptable for {Platform}, query={Query}, count={Count}",
                platform.PlatformId, query, count);

            if (useSiteRestriction)
                return await FindListingInternalAsync(request, platform, useSiteRestriction: false, cancellationToken);

            return new CseSearchResult { SkipReason = "no_listing_found" };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "CSE error for {Platform}, query={Query}", platform.PlatformId, query);
            return new CseSearchResult { SkipReason = "cse_error" };
        }
    }

    private static string MapHttpStatusToSkipReason(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Forbidden => "cse_forbidden",
        HttpStatusCode.Unauthorized => "cse_forbidden",
        HttpStatusCode.TooManyRequests => "cse_quota_exceeded",
        HttpStatusCode.BadRequest => "cse_bad_request",
        _ => "cse_error"
    };

    private static string? TryParseGoogleErrorMessage(string errorBody)
    {
        if (string.IsNullOrWhiteSpace(errorBody))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(errorBody);
            if (doc.RootElement.TryGetProperty("error", out var error)
                && error.TryGetProperty("message", out var message))
                return message.GetString();
        }
        catch
        {
            // not JSON
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
