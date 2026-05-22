using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using VenueAutofill.Api.Application.Interfaces;
using VenueAutofill.Api.Configuration;
using VenueAutofill.Api.Contracts;
using VenueAutofill.Api.Contracts.Internal;
using VenueAutofill.Api.Contracts.Requests;

namespace VenueAutofill.Api.Infrastructure.Providers;

public class OpenRouterAiProvider : IAiVenueFormatterService
{
    private readonly HttpClient _httpClient;
    private readonly AiOptions _options;
    private readonly VenueAutofillOptions _venueOptions;
    private readonly ILogger<OpenRouterAiProvider> _logger;

    public OpenRouterAiProvider(
        HttpClient httpClient,
        IOptions<AiOptions> options,
        IOptions<VenueAutofillOptions> venueOptions,
        ILogger<OpenRouterAiProvider> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _venueOptions = venueOptions.Value;
        _logger = logger;
    }

    public async Task<(string Description, List<string> Amenities)> FormatAsync(
        VenueAutofillRequest request,
        VenueCandidate candidate,
        VenueExtractedData extracted,
        CancellationToken cancellationToken = default)
    {
        var amenities = NormalizeAmenities(extracted.Amenities);
        var typeLabel = VenueTypeHelper.TryResolveEffectiveType(request, candidate, out _, out var display)
            ? display ?? "unknown"
            : "unknown";

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            return (BuildFallbackDescription(candidate, extracted), amenities);

        try
        {
            var prompt = $"""
                Convert the extracted venue text into JSON with fields: description (max {_venueOptions.DescriptionMaxWords} words), amenities (array of strings).
                Use ONLY the provided source text. Do not invent missing data.
                Venue: {candidate.Name}
                Type: {typeLabel}
                Source text:
                {extracted.RawText}
                Existing amenities hints: {string.Join(", ", extracted.Amenities)}
                """;

            var payload = new
            {
                model = _options.Model,
                messages = new[]
                {
                    new { role = "system", content = "You format venue data. Return valid JSON only: {\"description\":\"...\",\"amenities\":[\"...\"]}" },
                    new { role = "user", content = prompt }
                },
                temperature = 0.2
            };

            var url = $"{_options.BaseUrl.TrimEnd('/')}/chat/completions";
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
            httpRequest.Headers.Add("Authorization", $"Bearer {_options.ApiKey}");
            httpRequest.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("OpenRouter request failed: {Status}", response.StatusCode);
                return (BuildFallbackDescription(candidate, extracted), amenities);
            }

            var chat = JsonSerializer.Deserialize<ChatCompletionResponse>(body, JsonOptions);
            var content = chat?.Choices?.FirstOrDefault()?.Message?.Content;
            if (string.IsNullOrWhiteSpace(content))
                return (BuildFallbackDescription(candidate, extracted), amenities);

            var jsonStart = content.IndexOf('{');
            var jsonEnd = content.LastIndexOf('}');
            if (jsonStart >= 0 && jsonEnd > jsonStart)
                content = content[jsonStart..(jsonEnd + 1)];

            var formatted = JsonSerializer.Deserialize<AiFormatResult>(content, JsonOptions);
            if (formatted is null)
                return (BuildFallbackDescription(candidate, extracted), amenities);

            var description = formatted.Description ?? BuildFallbackDescription(candidate, extracted);
            var mergedAmenities = formatted.Amenities?.Count > 0
                ? NormalizeAmenities(formatted.Amenities)
                : amenities;

            return (TruncateWords(description, _venueOptions.DescriptionMaxWords), mergedAmenities);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI formatting failed");
            return (BuildFallbackDescription(candidate, extracted), amenities);
        }
    }

    private List<string> NormalizeAmenities(IEnumerable<string> raw)
    {
        var mapPath = Path.Combine(AppContext.BaseDirectory, "Data", "amenities-map.json");
        Dictionary<string, string>? map = null;
        if (File.Exists(mapPath))
        {
            var json = File.ReadAllText(mapPath);
            map = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        }

        var result = new List<string>();
        foreach (var item in raw)
        {
            var key = item.Trim().ToLowerInvariant();
            if (map is not null && map.TryGetValue(key, out var normalized))
                result.Add(normalized);
            else if (!string.IsNullOrWhiteSpace(item))
                result.Add(char.ToUpper(item[0]) + item[1..]);
        }

        return result.Distinct(StringComparer.OrdinalIgnoreCase).Take(15).ToList();
    }

    private static string BuildFallbackDescription(VenueCandidate candidate, VenueExtractedData extracted)
    {
        if (!string.IsNullOrWhiteSpace(extracted.RawText))
            return TruncateWords(extracted.RawText, 35);
        return $"{candidate.Name} in {candidate.City}.";
    }

    private static string TruncateWords(string text, int maxWords)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', words.Take(maxWords));
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private sealed class ChatCompletionResponse
    {
        public List<ChatChoice>? Choices { get; set; }
    }

    private sealed class ChatChoice
    {
        public ChatMessage? Message { get; set; }
    }

    private sealed class ChatMessage
    {
        public string? Content { get; set; }
    }

    private sealed class AiFormatResult
    {
        public string? Description { get; set; }
        public List<string>? Amenities { get; set; }
    }
}
