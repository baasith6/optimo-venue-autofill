using VenueAutofill.Api.Contracts.Internal;

namespace VenueAutofill.Api.Application.Interfaces;

public interface IVenueDetailsService
{
    Task<VenueCandidate?> GetDetailsAsync(VenueCandidate candidate, CancellationToken cancellationToken = default);
    Task<string?> GetPhotoUrlAsync(string photoName, CancellationToken cancellationToken = default);
}
