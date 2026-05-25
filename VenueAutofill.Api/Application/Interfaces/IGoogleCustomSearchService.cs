using VenueAutofill.Api.Contracts.Internal;
using VenueAutofill.Api.Contracts.Requests;

namespace VenueAutofill.Api.Application.Interfaces;

public interface IGoogleCustomSearchService
{
    Task<CseSearchResult> FindListingAsync(
        VenueAutofillRequest request,
        BookingPlatformEntry platform,
        CancellationToken cancellationToken = default);
}
