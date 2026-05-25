using VenueAutofill.Api.Contracts.Internal;

namespace VenueAutofill.Api.Application.Interfaces;

public interface IZoneResolverService
{
    IReadOnlyCollection<string> OfficialZoneNames { get; }
    bool IsOfficialZone(string? value);
    string Resolve(VenueCandidate candidate, string? area);
}
