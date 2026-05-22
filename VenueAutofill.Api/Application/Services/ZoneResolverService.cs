using System.Text.Json;
using VenueAutofill.Api.Application.Interfaces;
using VenueAutofill.Api.Contracts.Internal;

namespace VenueAutofill.Api.Application.Services;

public class ZoneResolverService : IZoneResolverService
{
    private readonly IReadOnlyList<La28ZoneEntry> _zones;

    public ZoneResolverService(IHostEnvironment environment)
    {
        var path = Path.Combine(environment.ContentRootPath, "Data", "la28-zones.json");
        if (File.Exists(path))
        {
            var json = File.ReadAllText(path);
            _zones = JsonSerializer.Deserialize<List<La28ZoneEntry>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        }
        else
        {
            _zones = [];
        }
    }

    public string Resolve(VenueCandidate candidate, string? area)
    {
        if (candidate.Latitude != 0 && candidate.Longitude != 0)
        {
            foreach (var zone in _zones)
            {
                if (zone.Bounds is not null && zone.Bounds.Contains(candidate.Latitude, candidate.Longitude))
                    return zone.ZoneName;
            }
        }

        var searchText = string.Join(' ', new[]
        {
            area,
            candidate.Area,
            candidate.Address,
            candidate.City,
            candidate.Name
        }.Where(s => !string.IsNullOrWhiteSpace(s)));

        foreach (var zone in _zones)
        {
            if (zone.Keywords.Any(k => searchText.Contains(k, StringComparison.OrdinalIgnoreCase)))
                return zone.ZoneName;
        }

        return string.Empty;
    }

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
