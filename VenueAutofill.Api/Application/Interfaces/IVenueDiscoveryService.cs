using VenueAutofill.Api.Contracts.Internal;
using VenueAutofill.Api.Contracts.Requests;

namespace VenueAutofill.Api.Application.Interfaces;

public interface IVenueDiscoveryService
{
    Task<IReadOnlyList<VenueCandidate>> DiscoverAsync(VenueAutofillRequest request, CancellationToken cancellationToken = default);
}
