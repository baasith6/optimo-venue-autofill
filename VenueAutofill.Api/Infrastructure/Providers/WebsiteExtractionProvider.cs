using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Extensions.Options;
using VenueAutofill.Api.Application.Interfaces;
using VenueAutofill.Api.Configuration;
using VenueAutofill.Api.Contracts;
using VenueAutofill.Api.Contracts.Internal;
using VenueAutofill.Api.Contracts.Requests;
using VenueAutofill.Api.Infrastructure.Http;

namespace VenueAutofill.Api.Infrastructure.Providers;

public class WebsiteExtractionProvider : IVenueExtractionService
{
    private static readonly Regex CheckInRegex = new(
        @"check[-\s]?in[:\s]*(\d{1,2}(?::\d{2})?\s*(?:am|pm)?)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CheckOutRegex = new(
        @"check[-\s]?out[:\s]*(\d{1,2}(?::\d{2})?\s*(?:am|pm)?)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex EmailRegex = new(
        @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}",
        RegexOptions.Compiled);

    private static readonly string[] FollowLinkKeywords =
    [
        "amenit", "facilit", "policy", "policies", "room", "hotel", "about", "overview", "services"
    ];

    private readonly HttpClient _httpClient;
    private readonly UrlSafetyValidator _urlSafetyValidator;
    private readonly SourceRelevanceValidator _relevanceValidator;
    private readonly VenueAutofillOptions _options;
    private readonly IReadOnlyList<string> _trustedDomains;
    private readonly ILogger<WebsiteExtractionProvider> _logger;

    public WebsiteExtractionProvider(
        HttpClient httpClient,
        UrlSafetyValidator urlSafetyValidator,
        SourceRelevanceValidator relevanceValidator,
        IOptions<VenueAutofillOptions> options,
        IHostEnvironment environment,
        ILogger<WebsiteExtractionProvider> logger)
    {
        _httpClient = httpClient;
        _urlSafetyValidator = urlSafetyValidator;
        _relevanceValidator = relevanceValidator;
        _options = options.Value;
        _logger = logger;

        var trustedPath = Path.Combine(environment.ContentRootPath, "Data", "trusted-domains.json");
        _trustedDomains = File.Exists(trustedPath)
            ? JsonSerializer.Deserialize<List<string>>(File.ReadAllText(trustedPath)) ?? []
            : [];
    }

    public async Task<VenueExtractedData> ExtractAsync(
        VenueAutofillRequest request,
        VenueCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        var urls = BuildSourceUrls(request, candidate);
        var result = new VenueExtractedData();
        var aggregatedText = new StringBuilder();

        foreach (var sourceUrl in urls)
        {
            if (string.IsNullOrWhiteSpace(sourceUrl) || !_urlSafetyValidator.IsSafeUrl(sourceUrl, out _))
                continue;

            if (!string.IsNullOrWhiteSpace(request.Source)
                && !_relevanceValidator.IsUrlRelevant(sourceUrl, request.VenueName, request.City, request.Country))
            {
                _logger.LogWarning("User source URL failed relevance check: {Url}", sourceUrl);
                continue;
            }

            var pageData = await CrawlSiteAsync(sourceUrl, cancellationToken);
            if (pageData.Text.Length == 0)
                continue;

            if (!string.IsNullOrWhiteSpace(request.Source)
                && !_relevanceValidator.IsContentRelevant(pageData.Text, request.VenueName, request.City, request.Country))
            {
                _logger.LogWarning("User source content failed relevance check: {Url}", sourceUrl);
                continue;
            }

            aggregatedText.AppendLine(pageData.Text);
            result.SourceUrl = sourceUrl;
            result.CheckInTime ??= pageData.CheckInTime;
            result.CheckOutTime ??= pageData.CheckOutTime;
            result.Email = string.IsNullOrWhiteSpace(result.Email) ? pageData.Email : result.Email;
            result.ImageUrl ??= pageData.ImageUrl;
            result.Amenities = result.Amenities.Union(pageData.Amenities).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            if (HasHotelFields(result))
                break;
        }

        result.RawText = Regex.Replace(aggregatedText.ToString(), @"\s+", " ").Trim();
        if (result.RawText.Length > 12000)
            result.RawText = result.RawText[..12000];

        if (result.Amenities.Count == 0 && result.RawText.Length > 0)
            result.Amenities = ExtractAmenityKeywords(result.RawText);

        return result;
    }

    private List<string> BuildSourceUrls(VenueAutofillRequest request, VenueCandidate candidate)
    {
        var urls = new List<string>();

        if (!string.IsNullOrWhiteSpace(request.Source))
            urls.Add(request.Source!);

        if (!string.IsNullOrWhiteSpace(candidate.Website))
            urls.Add(candidate.Website);

        // Trusted-chain sites (e.g. marriott.com) are already covered via candidate.Website from Google.
        // Re-order so trusted official hotel sites are tried before any secondary URL.
        if (!string.IsNullOrWhiteSpace(candidate.Website)
            && _trustedDomains.Any(d => candidate.Website.Contains(d, StringComparison.OrdinalIgnoreCase)))
        {
            urls.Remove(candidate.Website);
            urls.Insert(string.IsNullOrWhiteSpace(request.Source) ? 0 : 1, candidate.Website);
        }

        return urls.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private async Task<PageExtract> CrawlSiteAsync(string startUrl, CancellationToken cancellationToken)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();
        queue.Enqueue(startUrl);

        var combined = new StringBuilder();
        var extract = new PageExtract();

        while (queue.Count > 0 && visited.Count < _options.MaxExtractionPages)
        {
            var url = queue.Dequeue();
            if (!visited.Add(url))
                continue;

            try
            {
                var html = await _httpClient.GetStringAsync(url, cancellationToken);
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                var pageText = CleanText(doc.DocumentNode.InnerText);
                combined.Append(' ').Append(pageText);

                extract.CheckInTime ??= ParseTime(CheckInRegex, pageText) ?? ParseSchemaCheckTime(html, "checkinTime");
                extract.CheckOutTime ??= ParseTime(CheckOutRegex, pageText) ?? ParseSchemaCheckTime(html, "checkoutTime");
                if (string.IsNullOrWhiteSpace(extract.Email))
                    extract.Email = EmailRegex.Match(pageText).Value;
                extract.Amenities = extract.Amenities.Union(ExtractAmenityKeywords(pageText)).ToList();
                extract.ImageUrl ??= FindBestImage(doc, url);

                if (visited.Count < _options.MaxExtractionPages)
                {
                    foreach (var link in CollectRelevantLinks(doc, url))
                    {
                        if (!visited.Contains(link) && !queue.Contains(link))
                            queue.Enqueue(link);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch page {Url}", url);
            }
        }

        extract.Text = CleanText(combined.ToString());
        return extract;
    }

    private static IEnumerable<string> CollectRelevantLinks(HtmlDocument doc, string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
            yield break;

        var anchors = doc.DocumentNode.SelectNodes("//a[@href]") ?? Enumerable.Empty<HtmlNode>();
        foreach (var anchor in anchors)
        {
            var href = anchor.GetAttributeValue("href", "");
            if (string.IsNullOrWhiteSpace(href))
                continue;

            if (!Uri.TryCreate(baseUri, href, out var absolute))
                continue;

            if (absolute.Host != baseUri.Host)
                continue;

            var path = absolute.PathAndQuery.ToLowerInvariant();
            if (FollowLinkKeywords.Any(k => path.Contains(k, StringComparison.OrdinalIgnoreCase)))
                yield return absolute.ToString();
        }
    }

    private static string? FindBestImage(HtmlDocument doc, string baseUrl)
    {
        var nodes = doc.DocumentNode.SelectNodes("//img[@src]") ?? Enumerable.Empty<HtmlNode>();
        foreach (var img in nodes)
        {
            var src = img.GetAttributeValue("src", "");
            if (string.IsNullOrWhiteSpace(src))
                continue;

            if (Uri.TryCreate(src, UriKind.Absolute, out var abs))
                return abs.ToString();
            if (Uri.TryCreate(new Uri(baseUrl), src, out var relative))
                return relative.ToString();
        }

        return null;
    }

    private static string? ParseSchemaCheckTime(string html, string property)
    {
        var match = Regex.Match(html, $"\"{property}\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
        if (!match.Success)
            return null;
        return NormalizeTimeString(match.Groups[1].Value);
    }

    private static string? ParseTime(Regex regex, string text)
    {
        var match = regex.Match(text);
        if (!match.Success)
            return null;
        return NormalizeTimeString(match.Groups[1].Value.Trim());
    }

    private static string? NormalizeTimeString(string raw)
    {
        if (TimeSpan.TryParse(raw, out var ts))
            return ts.ToString(@"hh\:mm\:ss");

        if (DateTime.TryParse(raw, out var dt))
            return dt.ToString("HH:mm:ss");

        return raw.Contains(':') ? raw : null;
    }

    private static string CleanText(string text) =>
        Regex.Replace(text, @"\s+", " ").Trim();

    private static List<string> ExtractAmenityKeywords(string text)
    {
        var keywords = new[]
        {
            "wifi", "free wifi", "wireless", "pool", "swimming pool", "outdoor pool", "gym", "fitness",
            "fitness center", "restaurant", "dining", "bar", "lounge", "room service", "business center",
            "conference", "meeting room", "parking", "valet", "pet friendly", "spa", "breakfast"
        };

        return keywords
            .Where(k => text.Contains(k, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool HasHotelFields(VenueExtractedData data) =>
        !string.IsNullOrWhiteSpace(data.CheckInTime)
        && !string.IsNullOrWhiteSpace(data.CheckOutTime)
        && data.Amenities.Count >= 3;

    private sealed class PageExtract
    {
        public string Text { get; set; } = string.Empty;
        public string? CheckInTime { get; set; }
        public string? CheckOutTime { get; set; }
        public string Email { get; set; } = string.Empty;
        public string? ImageUrl { get; set; }
        public List<string> Amenities { get; set; } = [];
    }
}
