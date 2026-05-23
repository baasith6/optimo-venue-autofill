using System.Text.Json;
using VenueAutofill.Api.Contracts.Internal;

namespace VenueAutofill.Api.Infrastructure.Data;

public class BookingPlatformRegistry
{
    private readonly IReadOnlyList<BookingPlatformEntry> _platforms;

    public BookingPlatformRegistry(IHostEnvironment environment)
    {
        var path = Path.Combine(environment.ContentRootPath, "Data", "booking-platforms.json");
        _platforms = File.Exists(path)
            ? JsonSerializer.Deserialize<List<BookingPlatformEntry>>(File.ReadAllText(path)) ?? []
            : [];
    }

    public IReadOnlyList<BookingPlatformEntry> GetAll() => _platforms;

    public BookingPlatformEntry? GetById(string platformId) =>
        _platforms.FirstOrDefault(p => p.PlatformId.Equals(platformId, StringComparison.OrdinalIgnoreCase));

    public IEnumerable<BookingPlatformEntry> GetProbeEnabled(int maxCount) =>
        _platforms.Where(p => p.ProbeEnabled).Take(maxCount);
}
