using VenueAutofill.Api.Contracts.Internal;

namespace VenueAutofill.Api.Application.Interfaces;

public interface IZoneResolverService
{
    string Resolve(VenueCandidate candidate, string? area);
}
