namespace VenueAutofill.Api.Contracts.Internal;

public class VenueExtractedData
{
    public string RawText { get; set; } = string.Empty;
    public string? CheckInTime { get; set; }
    public string? CheckOutTime { get; set; }
    public List<string> Amenities { get; set; } = [];
    public string Email { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public string SourceUrl { get; set; } = string.Empty;
}
