using VenueAutofill.Api.Contracts.Responses;

namespace VenueAutofill.Api.Application.Services;

public class ImageResolveResult
{
    public string ImageUrl { get; set; } = string.Empty;
    public ImageSourceInfo? ImageSource { get; set; }
    public List<ImageCandidateDetail> ImageCandidates { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public bool ImageVerified { get; set; }
}
