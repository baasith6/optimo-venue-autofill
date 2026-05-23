using VenueAutofill.Api.Contracts;
using VenueAutofill.Api.Contracts.Internal;
using VenueAutofill.Api.Contracts.Requests;
using VenueAutofill.Api.Contracts.Responses;

namespace VenueAutofill.Api.Application.Interfaces;

public interface IImageResolverService
{
    (string ImageUrl, ImageSourceInfo? ImageSource, List<string> ImageCandidates, List<string> Warnings) Resolve(
        VenueAutofillRequest request,
        RetrievalMode mode,
        VenueCandidate candidate,
        CrossSourceEnrichmentContext crossSource,
        string? googlePhotoUrl,
        string? officialWebsiteImageUrl,
        string? userSourceImageUrl);
}
