using System.Globalization;
using System.Net;
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

public class GooglePlacesProvider : IVenueDiscoveryService, IVenueDetailsService
{
    private const string TextSearchFieldMask =
        "places.id,places.displayName,places.formattedAddress,places.location,places.rating,places.types,places.websiteUri,places.nationalPhoneNumber,places.googleMapsUri,places.photos";

    private const string DetailsFieldMask =
        "id,displayName,formattedAddress,location,rating,types,websiteUri,nationalPhoneNumber,googleMapsUri,photos,addressComponents";

    private readonly HttpClient _httpClient;
    private readonly GooglePlacesOptions _options;
    private readonly ILogger<GooglePlacesProvider> _logger;

    public GooglePlacesProvider(
        HttpClient httpClient,
        IOptions<GooglePlacesOptions> options,
        ILogger<GooglePlacesProvider> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<VenueCandidate>> DiscoverAsync(VenueAutofillRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new ExternalProviderException(
                "GooglePlaces",
                "Google Places API key is not configured. Set GooglePlaces:ApiKey via dotnet user-secrets or environment variable.");
        }

        var query = BuildQuery(request);
        var url = $"{_options.BaseUrl.TrimEnd('/')}{_options.TextSearchEndpoint}";
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower
        };
        httpRequest.Headers.Add("X-Goog-Api-Key", _options.ApiKey);
        httpRequest.Headers.Add("X-Goog-FieldMask", TextSearchFieldMask);
        httpRequest.Content = new StringContent(
            JsonSerializer.Serialize(new
            {
                textQuery = query,
                languageCode = "en"
            }),
            Encoding.UTF8,
            "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Google Text Search timed out");
            throw new ExternalProviderException(
                "GooglePlaces",
                "Google Places request timed out. Check network connectivity and firewall access to places.googleapis.com.",
                504,
                ex);
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Google Text Search failed: {Status} {Body}", response.StatusCode, body);
            throw CreateGoogleException(response.StatusCode, body, "Text Search");
        }

        PlacesSearchResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<PlacesSearchResponse>(body, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse Google Text Search response");
            throw new ExternalProviderException("GooglePlaces", "Failed to parse Google Places Text Search response.", (int)response.StatusCode, ex);
        }

        if (parsed?.Places is null || parsed.Places.Count == 0)
            return [];

        return parsed.Places.Select(p => MapPlace(p, request)).ToList();
    }

    public async Task<VenueCandidate?> GetDetailsAsync(VenueCandidate candidate, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(candidate.PlaceId) || string.IsNullOrWhiteSpace(_options.ApiKey))
            return candidate;

        var placeId = NormalizePlaceId(candidate.PlaceId);
        var url = $"{_options.BaseUrl.TrimEnd('/')}/places/{Uri.EscapeDataString(placeId)}";
        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, url);
        httpRequest.Headers.Add("X-Goog-Api-Key", _options.ApiKey);
        httpRequest.Headers.Add("X-Goog-FieldMask", DetailsFieldMask);

        var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Google Place Details failed for {PlaceId}: {Status} {Body}", placeId, response.StatusCode, body);
            return candidate;
        }

        var place = JsonSerializer.Deserialize<PlaceResult>(body, JsonOptions);
        if (place is null)
            return candidate;

        return MergeDetails(candidate, place);
    }

    public Task<string?> GetPhotoUrlAsync(string photoName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(photoName) || string.IsNullOrWhiteSpace(_options.ApiKey))
            return Task.FromResult<string?>(null);

        var mediaPath = photoName.TrimStart('/');
        if (!mediaPath.StartsWith("places/", StringComparison.Ordinal))
            mediaPath = $"places/{mediaPath}";

        var url = $"{_options.BaseUrl.TrimEnd('/')}/{mediaPath}/media?maxHeightPx=800&key={Uri.EscapeDataString(_options.ApiKey)}";
        return Task.FromResult<string?>(url);
    }

    private static string BuildQuery(VenueAutofillRequest request)
    {
        var parts = new List<string> { request.VenueName, request.City, request.Country };
        if (!string.IsNullOrWhiteSpace(request.Area))
            parts.Insert(2, request.Area);
        return string.Join(", ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    private static string NormalizePlaceId(string placeId)
    {
        const string prefix = "places/";
        return placeId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? placeId[prefix.Length..]
            : placeId;
    }

    private static VenueCandidate MapPlace(PlaceResult place, VenueAutofillRequest request)
    {
        var address = place.FormattedAddress ?? string.Empty;
        var rawId = place.Id ?? place.ResourceName ?? string.Empty;
        var candidate = new VenueCandidate
        {
            PlaceId = NormalizePlaceId(rawId),
            Name = place.DisplayName?.Text ?? request.VenueName,
            GoogleTypes = place.Types ?? [],
            Country = ExtractCountry(place) ?? request.Country,
            City = request.City,
            Area = request.Area,
            Address = address,
            Latitude = place.Location?.Latitude ?? 0,
            Longitude = place.Location?.Longitude ?? 0,
            Website = place.WebsiteUri ?? string.Empty,
            Phone = place.NationalPhoneNumber ?? string.Empty,
            Rating = place.Rating,
            PhotoName = place.Photos?.FirstOrDefault()?.Name ?? string.Empty,
            MapUrl = place.GoogleMapsUri ?? BuildMapUrl(place.Location?.Latitude, place.Location?.Longitude)
        };

        VenueTypeHelper.ApplyInferredType(candidate, request);
        return candidate;
    }

    private static VenueCandidate MergeDetails(VenueCandidate candidate, PlaceResult place)
    {
        candidate.Name = place.DisplayName?.Text ?? candidate.Name;
        candidate.Address = place.FormattedAddress ?? candidate.Address;
        candidate.Website = place.WebsiteUri ?? candidate.Website;
        candidate.Phone = place.NationalPhoneNumber ?? candidate.Phone;
        candidate.Rating = place.Rating ?? candidate.Rating;
        candidate.Latitude = place.Location?.Latitude ?? candidate.Latitude;
        candidate.Longitude = place.Location?.Longitude ?? candidate.Longitude;
        candidate.MapUrl = place.GoogleMapsUri ?? candidate.MapUrl;
        candidate.PhotoName = place.Photos?.FirstOrDefault()?.Name ?? candidate.PhotoName;
        candidate.Country = ExtractCountry(place) ?? candidate.Country;
        if (place.Types is { Count: > 0 })
            candidate.GoogleTypes = place.Types;

        if (string.IsNullOrWhiteSpace(candidate.VenueType)
            && VenueTypeHelper.TryInfer(candidate.Name, candidate.GoogleTypes, out var inferred))
            candidate.VenueType = VenueTypeHelper.ToDisplayName(inferred);

        return candidate;
    }

    private static ExternalProviderException CreateGoogleException(System.Net.HttpStatusCode statusCode, string body, string operation)
    {
        var hint = statusCode switch
        {
            System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden =>
                " Check that Places API (New) is enabled in Google Cloud Console and the API key is valid with billing enabled.",
            System.Net.HttpStatusCode.BadRequest =>
                " Check API request format and field masks.",
            _ => string.Empty
        };

        string message = $"Google Places {operation} failed ({(int)statusCode} {statusCode}).{hint}";

        try
        {
            var errorDoc = JsonDocument.Parse(body);
            if (errorDoc.RootElement.TryGetProperty("error", out var error)
                && error.TryGetProperty("message", out var msg))
            {
                var googleMessage = msg.GetString();
                if (!string.IsNullOrWhiteSpace(googleMessage))
                    message = $"Google Places {operation} failed: {googleMessage}";
            }
        }
        catch
        {
            // ignore parse errors
        }

        return new ExternalProviderException("GooglePlaces", message, (int)statusCode);
    }

    private static string? ExtractCountry(PlaceResult place) =>
        place.AddressComponents?
            .FirstOrDefault(c => c.Types?.Contains("country") == true)
            ?.LongText;

    private static string BuildMapUrl(double? lat, double? lng) =>
        lat.HasValue && lng.HasValue
            ? $"https://maps.google.com/?q={lat.Value.ToString(CultureInfo.InvariantCulture)},{lng.Value.ToString(CultureInfo.InvariantCulture)}"
            : string.Empty;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private sealed class PlacesSearchResponse
    {
        public List<PlaceResult>? Places { get; set; }
    }

    private sealed class PlaceResult
    {
        public string? Id { get; set; }

        [JsonPropertyName("name")]
        public string? ResourceName { get; set; }

        public DisplayName? DisplayName { get; set; }
        public string? FormattedAddress { get; set; }
        public LatLng? Location { get; set; }
        public double? Rating { get; set; }
        public List<string>? Types { get; set; }
        public string? WebsiteUri { get; set; }
        public string? NationalPhoneNumber { get; set; }
        public string? GoogleMapsUri { get; set; }
        public List<PhotoRef>? Photos { get; set; }
        public List<AddressComponent>? AddressComponents { get; set; }
    }

    private sealed class DisplayName
    {
        public string? Text { get; set; }
    }

    private sealed class LatLng
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
    }

    private sealed class PhotoRef
    {
        public string? Name { get; set; }
    }

    private sealed class AddressComponent
    {
        public string? LongText { get; set; }
        public List<string>? Types { get; set; }
    }
}
