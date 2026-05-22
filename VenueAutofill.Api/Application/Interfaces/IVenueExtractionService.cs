using VenueAutofill.Api.Contracts.Internal;
using VenueAutofill.Api.Contracts.Requests;

namespace VenueAutofill.Api.Application.Interfaces;

public interface IVenueExtractionService
{
    Task<VenueExtractedData> ExtractAsync(VenueAutofillRequest request, VenueCandidate candidate, CancellationToken cancellationToken = default);
}
