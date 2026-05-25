namespace VenueAutofill.Api.Contracts.Internal;

public class NormalizedImageResult
{
    public bool Succeeded { get; set; }
    public string NormalizedUrl { get; set; } = string.Empty;
    public string OriginalUrl { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public string? Warning { get; set; }
}
