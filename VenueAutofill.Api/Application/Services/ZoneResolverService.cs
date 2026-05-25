using System.Text.Json;
using VenueAutofill.Api.Application.Interfaces;
using VenueAutofill.Api.Contracts.Internal;

namespace VenueAutofill.Api.Application.Services;

public class ZoneResolverService : IZoneResolverService
{
    private readonly IReadOnlyList<La28ZoneEntry> _zones;
    private readonly HashSet<string> _officialZoneNames;

    public ZoneResolverService(IHostEnvironment environment)
    {
        var path = Path.Combine(environment.ContentRootPath, "Data", "la28-zones.json");
        if (File.Exists(path))
        {
            var json = File.ReadAllText(path);
            _zones = JsonSerializer.Deserialize<List<La28ZoneEntry>>(json, JsonOptions) ?? [];
        }
        else
        {
            _zones = [];
        }

        _officialZoneNames = _zones
            .Select(z => z.ZoneName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<string> OfficialZoneNames => _officialZoneNames;

    public bool IsOfficialZone(string? value) =>
        !string.IsNullOrWhiteSpace(value) && _officialZoneNames.Contains(value.Trim());

    public string Resolve(VenueCandidate candidate, string? area)
    {
        if (!string.IsNullOrWhiteSpace(area) && TryGetOfficialZoneName(area, out var fromArea))
            return fromArea;

        if (candidate.Latitude != 0 && candidate.Longitude != 0)
        {
            var fromBounds = _zones
                .Where(z => z.Bounds is not null && z.Bounds.Contains(candidate.Latitude, candidate.Longitude))
                .Select(z => z.ZoneName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (fromBounds.Count == 1)
                return fromBounds[0];
        }

        var searchText = BuildSearchText(area, candidate);
        var keywordMatches = MatchZonesByKeywords(searchText);
        if (keywordMatches.Count == 1)
            return keywordMatches[0];

        return string.Empty;
    }

    private List<string> MatchZonesByKeywords(string searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText))
            return [];

        var matches = new List<string>();
        foreach (var zone in _zones)
        {
            var allTerms = zone.Keywords
                .Append(zone.ZoneName.Replace(" Zone", "", StringComparison.OrdinalIgnoreCase))
                .Where(t => !string.IsNullOrWhiteSpace(t));

            foreach (var term in allTerms)
            {
                if (ContainsPhrase(searchText, term))
                {
                    matches.Add(zone.ZoneName);
                    break;
                }
            }
        }

        return matches.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string BuildSearchText(string? area, VenueCandidate candidate) =>
        string.Join(' ', new[]
        {
            area,
            candidate.Area,
            candidate.Address,
            candidate.City,
            candidate.Country,
            candidate.Name
        }.Where(s => !string.IsNullOrWhiteSpace(s)));

    private bool TryGetOfficialZoneName(string input, out string officialName)
    {
        officialName = string.Empty;
        var trimmed = input.Trim();
        var match = _zones.FirstOrDefault(z =>
            z.ZoneName.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
        if (match is null)
            return false;

        officialName = match.ZoneName;
        return true;
    }

    private static bool ContainsPhrase(string haystack, string phrase)
    {
        var normalizedHaystack = Normalize(haystack);
        var normalizedPhrase = Normalize(phrase);
        return normalizedPhrase.Length > 0
            && normalizedHaystack.Contains(normalizedPhrase, StringComparison.Ordinal);
    }

    private static string Normalize(string value) =>
        value.ToLowerInvariant().Replace("  ", " ").Trim();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed class La28ZoneEntry
    {
        public string ZoneName { get; set; } = string.Empty;
        public List<string> Keywords { get; set; } = [];
        public ZoneBounds? Bounds { get; set; }
    }

    private sealed class ZoneBounds
    {
        public double MinLat { get; set; }
        public double MaxLat { get; set; }
        public double MinLng { get; set; }
        public double MaxLng { get; set; }

        public bool Contains(double lat, double lng) =>
            lat >= MinLat && lat <= MaxLat && lng >= MinLng && lng <= MaxLng;
    }
}
