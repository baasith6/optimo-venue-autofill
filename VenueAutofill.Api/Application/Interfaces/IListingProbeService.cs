using VenueAutofill.Api.Contracts.Internal;
using VenueAutofill.Api.Contracts.Requests;

namespace VenueAutofill.Api.Application.Interfaces;

public interface IListingProbeService
{
    Task<ListingProbeResult> ProbeAsync(
        string sourceId,
        string label,
        string url,
        VenueAutofillRequest request,
        VenueCandidate candidate,
        CancellationToken cancellationToken = default);
}
