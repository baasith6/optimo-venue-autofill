using VenueAutofill.Api.Contracts.Internal;
using VenueAutofill.Api.Contracts.Requests;

namespace VenueAutofill.Api.Application.Interfaces;

public interface IAiVenueFormatterService
{
    Task<(string Description, List<string> Amenities)> FormatAsync(
        VenueAutofillRequest request,
        VenueCandidate candidate,
        VenueExtractedData extracted,
        CancellationToken cancellationToken = default);
}
