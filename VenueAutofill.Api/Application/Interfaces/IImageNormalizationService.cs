using VenueAutofill.Api.Contracts.Internal;

namespace VenueAutofill.Api.Application.Interfaces;

public interface IImageNormalizationService
{
    Task<NormalizedImageResult> NormalizeAndUploadAsync(string sourceUrl, CancellationToken cancellationToken = default);
}
