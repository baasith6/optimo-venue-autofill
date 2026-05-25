using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Extensions.Options;
using VenueAutofill.Api.Application.Interfaces;
using VenueAutofill.Api.Configuration;
using VenueAutofill.Api.Contracts.Internal;
using VenueAutofill.Api.Contracts.Requests;
using VenueAutofill.Api.Infrastructure.Browser;
using VenueAutofill.Api.Infrastructure.Http;
using VenueAutofill.Api.Infrastructure.Schema;

namespace VenueAutofill.Api.Infrastructure.Providers;

public class ListingProbeService : IListingProbeService
{
    private static readonly Regex PhoneRegex = new(
        @"\+?[\d\s().-]{10,}",
        RegexOptions.Compiled);

    private readonly IHtmlPageFetcher _pageFetcher;
    private readonly HttpClient _httpClient;
    private readonly UrlSafetyValidator _urlSafetyValidator;
    private readonly SourceRelevanceValidator _relevanceValidator;
    private readonly VenueAutofillOptions _options;
    private readonly ILogger<ListingProbeService> _logger;

    public ListingProbeService(
        IHtmlPageFetcher pageFetcher,
        HttpClient httpClient,
        UrlSafetyValidator urlSafetyValidator,
        SourceRelevanceValidator relevanceValidator,
        IOptions<VenueAutofillOptions> options,
        ILogger<ListingProbeService> logger)
    {
        _pageFetcher = pageFetcher;
        _httpClient = httpClient;
        _urlSafetyValidator = urlSafetyValidator;
        _relevanceValidator = relevanceValidator;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ListingProbeResult> ProbeAsync(
        string sourceId,
        string label,
        string url,
        VenueAutofillRequest request,
        VenueCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        var result = new ListingProbeResult
        {
            SourceId = sourceId,
            Label = label,
            Url = url
        };

        if (!_urlSafetyValidator.IsSafeUrl(url, out _))
            return result;

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(_options.PlatformProbeTimeoutSeconds));

            var fetch = await _pageFetcher.FetchAsync(url, cts.Token);
            result.FetchedVia = fetch.FetchedVia;
            if (!fetch.Succeeded || string.IsNullOrWhiteSpace(fetch.Html))
            {
                _logger.LogWarning(
                    "Listing probe could not fetch page: {Url}, via={Via}, blocked={Blocked}",
                    url, fetch.FetchedVia, fetch.LooksBlocked);
                return result;
            }

            result.PageFetched = true;
            var doc = new HtmlDocument();
            doc.LoadHtml(fetch.Html);

            result.ImageUrl = ExtractMetaImage(doc, fetch.FinalUrl);
            result.ExtractedName = doc.DocumentNode.SelectSingleNode("//title")?.InnerText?.Trim();
            var bodyText = Regex.Replace(doc.DocumentNode.InnerText ?? "", @"\s+", " ").Trim();
            if (bodyText.Length > 8000)
                bodyText = bodyText[..8000];

            var schema = SchemaOrgHotelParser.ParseFromHtml(fetch.Html);
            result.ExtractedName ??= schema.Name;
            result.ExtractedCity ??= schema.City ?? TryExtractCity(bodyText, request.City);
            result.ExtractedCountry ??= schema.Country ?? TryExtractCountry(bodyText, request.Country);
            result.ExtractedPhone ??= schema.Phone ?? PhoneRegex.Match(bodyText).Value;
            result.ImageUrl ??= schema.ImageUrl;
            result.CheckInTime ??= schema.CheckInTime;
            result.CheckOutTime ??= schema.CheckOutTime;

            if (!string.IsNullOrWhiteSpace(result.ImageUrl)
                && _urlSafetyValidator.IsSafeUrl(result.ImageUrl, out _))
            {
                result.ImageReachable = await IsImageReachableAsync(result.ImageUrl, cts.Token);
            }

            if (!_relevanceValidator.IsContentRelevant(bodyText, request.VenueName, request.City, request.Country)
                && string.IsNullOrWhiteSpace(result.ExtractedName))
            {
                _logger.LogWarning("Probe content low relevance: {Url}", url);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Listing probe failed for {Url}", url);
        }

        return result;
    }

    private async Task<bool> IsImageReachableAsync(string imageUrl, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, imageUrl);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static string? ExtractMetaImage(HtmlDocument doc, string baseUrl)
    {
        var meta = doc.DocumentNode.SelectNodes("//meta[@property='og:image' or @name='twitter:image']");
        if (meta is null)
            return null;

        foreach (var node in meta)
        {
            var content = node.GetAttributeValue("content", "");
            if (string.IsNullOrWhiteSpace(content))
                continue;
            if (Uri.TryCreate(content, UriKind.Absolute, out var abs))
                return abs.ToString();
            if (Uri.TryCreate(new Uri(baseUrl), content, out var rel))
                return rel.ToString();
        }

        return null;
    }

    private static string? TryExtractCity(string text, string city) =>
        text.Contains(city, StringComparison.OrdinalIgnoreCase) ? city : null;

    private static string? TryExtractCountry(string text, string country) =>
        text.Contains(country, StringComparison.OrdinalIgnoreCase) ? country : null;
}
