using VenueAutofill.Api.Contracts.Internal;
using VenueAutofill.Api.Contracts.Requests;

namespace VenueAutofill.Api.Application.Interfaces;

public interface IGoogleCustomSearchService
{
    Task<string?> FindListingUrlAsync(
        VenueAutofillRequest request,
        BookingPlatformEntry platform,
        CancellationToken cancellationToken = default);
}
