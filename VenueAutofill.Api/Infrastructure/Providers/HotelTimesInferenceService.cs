using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using VenueAutofill.Api.Application.Interfaces;
using VenueAutofill.Api.Configuration;

namespace VenueAutofill.Api.Infrastructure.Providers;

public class HotelTimesInferenceService : IHotelTimesInferenceService
{
    private readonly HttpClient _httpClient;
    private readonly AiOptions _options;
    private readonly ILogger<HotelTimesInferenceService> _logger;

    public HotelTimesInferenceService(
        HttpClient httpClient,
        IOptions<AiOptions> options,
        ILogger<HotelTimesInferenceService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<(string? CheckInTime, string? CheckOutTime, bool UsedAi)> InferAsync(
        string sourceText,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey) || string.IsNullOrWhiteSpace(sourceText))
            return (null, null, false);

        var snippet = sourceText.Length > 4000 ? sourceText[..4000] : sourceText;

        try
        {
            var payload = new
            {
                model = _options.Model,
                messages = new[]
                {
                    new
                    {
                        role = "system",
                        content = "Extract hotel check-in and check-out times from text. Return JSON only: {\"checkInTime\":\"HH:mm:ss\",\"checkOutTime\":\"HH:mm:ss\"}. Use 24-hour format. Use null if not found."
                    },
                    new { role = "user", content = snippet }
                },
                temperature = 0.1
            };

            var url = $"{_options.BaseUrl.TrimEnd('/')}/chat/completions";
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
            httpRequest.Headers.Add("Authorization", $"Bearer {_options.ApiKey.Trim()}");
            httpRequest.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return (null, null, false);

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var content = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            if (string.IsNullOrWhiteSpace(content))
                return (null, null, false);

            var jsonStart = content.IndexOf('{');
            var jsonEnd = content.LastIndexOf('}');
            if (jsonStart < 0 || jsonEnd <= jsonStart)
                return (null, null, false);

            using var parsed = JsonDocument.Parse(content[jsonStart..(jsonEnd + 1)]);
            var checkIn = parsed.RootElement.TryGetProperty("checkInTime", out var ci) ? ci.GetString() : null;
            var checkOut = parsed.RootElement.TryGetProperty("checkOutTime", out var co) ? co.GetString() : null;
            return (checkIn, checkOut, true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "AI hotel times inference failed");
            return (null, null, false);
        }
    }
}
