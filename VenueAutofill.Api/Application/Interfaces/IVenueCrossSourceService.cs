using VenueAutofill.Api.Contracts;
using VenueAutofill.Api.Contracts.Internal;
using VenueAutofill.Api.Contracts.Requests;

namespace VenueAutofill.Api.Application.Interfaces;

public interface IVenueCrossSourceService
{
    Task<CrossSourceEnrichmentContext> RunCrossCheckAsync(
        VenueAutofillRequest request,
        VenueCandidate candidate,
        RetrievalMode mode,
        CancellationToken cancellationToken = default);
}
