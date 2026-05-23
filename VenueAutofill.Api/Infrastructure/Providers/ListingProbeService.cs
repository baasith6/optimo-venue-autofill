using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Extensions.Options;
using VenueAutofill.Api.Application.Interfaces;
using VenueAutofill.Api.Configuration;
using VenueAutofill.Api.Contracts.Internal;
using VenueAutofill.Api.Contracts.Requests;
using VenueAutofill.Api.Infrastructure.Http;

namespace VenueAutofill.Api.Infrastructure.Providers;

public class ListingProbeService : IListingProbeService
{
    private static readonly Regex PhoneRegex = new(
        @"\+?[\d\s().-]{10,}",
        RegexOptions.Compiled);

    private readonly HttpClient _httpClient;
    private readonly UrlSafetyValidator _urlSafetyValidator;
    private readonly SourceRelevanceValidator _relevanceValidator;
    private readonly VenueAutofillOptions _options;
    private readonly ILogger<ListingProbeService> _logger;

    public ListingProbeService(
        HttpClient httpClient,
        UrlSafetyValidator urlSafetyValidator,
        SourceRelevanceValidator relevanceValidator,
        IOptions<VenueAutofillOptions> options,
        ILogger<ListingProbeService> logger)
    {
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

            var html = await _httpClient.GetStringAsync(url, cts.Token);
            result.PageFetched = true;
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            result.ImageUrl = ExtractMetaImage(doc, url) ?? ExtractSchemaImage(html);
            result.ExtractedName = doc.DocumentNode.SelectSingleNode("//title")?.InnerText?.Trim();
            var bodyText = Regex.Replace(doc.DocumentNode.InnerText ?? "", @"\s+", " ").Trim();
            if (bodyText.Length > 8000)
                bodyText = bodyText[..8000];

            ParseSchemaHotel(html, result);
            result.ExtractedCity ??= TryExtractCity(bodyText, request.City);
            result.ExtractedCountry ??= TryExtractCountry(bodyText, request.Country);
            result.ExtractedPhone ??= PhoneRegex.Match(bodyText).Value;

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

    private static string? ExtractSchemaImage(string html)
    {
        foreach (var json in ExtractJsonLdBlocks(html))
        {
            if (TryGetSchemaString(json, "image", out var image))
                return image;
        }

        return null;
    }

    private static void ParseSchemaHotel(string html, ListingProbeResult result)
    {
        foreach (var json in ExtractJsonLdBlocks(html))
        {
            if (!IsLodgingType(json))
                continue;

            if (TryGetSchemaString(json, "name", out var name))
                result.ExtractedName = name;
            if (json.TryGetProperty("address", out var address))
            {
                if (address.TryGetProperty("addressLocality", out var city))
                    result.ExtractedCity = city.GetString();
                if (address.TryGetProperty("addressCountry", out var country))
                    result.ExtractedCountry = country.GetString();
            }

            if (TryGetSchemaString(json, "telephone", out var phone))
                result.ExtractedPhone = phone;
            if (TryGetSchemaString(json, "image", out var image))
                result.ImageUrl ??= image;
        }
    }

    private static bool IsLodgingType(JsonElement json)
    {
        if (json.TryGetProperty("@type", out var typeEl))
        {
            if (typeEl.ValueKind == JsonValueKind.String)
            {
                var t = typeEl.GetString() ?? "";
                return t.Contains("Hotel", StringComparison.OrdinalIgnoreCase)
                    || t.Contains("Lodging", StringComparison.OrdinalIgnoreCase);
            }
        }

        return false;
    }

    private static bool TryGetSchemaString(JsonElement json, string property, out string? value)
    {
        value = null;
        if (!json.TryGetProperty(property, out var prop))
            return false;

        value = prop.ValueKind switch
        {
            JsonValueKind.String => prop.GetString(),
            JsonValueKind.Array when prop.GetArrayLength() > 0 && prop[0].ValueKind == JsonValueKind.String => prop[0].GetString(),
            _ => null
        };

        return !string.IsNullOrWhiteSpace(value);
    }

    private static List<JsonElement> ExtractJsonLdBlocks(string html)
    {
        var results = new List<JsonElement>();
        var matches = Regex.Matches(html, @"<script[^>]*type=[""']application/ld\+json[""'][^>]*>(.*?)</script>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        foreach (Match match in matches)
        {
            var jsonText = match.Groups[1].Value.Trim();
            if (string.IsNullOrWhiteSpace(jsonText))
                continue;

            try
            {
                using var doc = JsonDocument.Parse(jsonText);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in doc.RootElement.EnumerateArray())
                        results.Add(item.Clone());
                }
                else
                    results.Add(doc.RootElement.Clone());
            }
            catch
            {
                // skip invalid JSON-LD
            }
        }

        return results;
    }

    private static string? TryExtractCity(string text, string city) =>
        text.Contains(city, StringComparison.OrdinalIgnoreCase) ? city : null;

    private static string? TryExtractCountry(string text, string country) =>
        text.Contains(country, StringComparison.OrdinalIgnoreCase) ? country : null;
}
