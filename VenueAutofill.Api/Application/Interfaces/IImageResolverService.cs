using VenueAutofill.Api.Application.Services;
using VenueAutofill.Api.Contracts;
using VenueAutofill.Api.Contracts.Internal;
using VenueAutofill.Api.Contracts.Requests;

namespace VenueAutofill.Api.Application.Interfaces;

public interface IImageResolverService
{
    ImageResolveResult Resolve(
        VenueAutofillRequest request,
        RetrievalMode mode,
        VenueCandidate candidate,
        CrossSourceEnrichmentContext crossSource,
        string? googlePhotoUrl,
        string? officialWebsiteImageUrl,
        string? userSourceImageUrl);
}
