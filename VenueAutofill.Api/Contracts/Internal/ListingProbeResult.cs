namespace VenueAutofill.Api.Contracts.Internal;

public class ListingProbeResult
{
    public string SourceId { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public string? ExtractedName { get; set; }
    public string? ExtractedCity { get; set; }
    public string? ExtractedCountry { get; set; }
    public string? ExtractedPhone { get; set; }
    public string? CheckInTime { get; set; }
    public string? CheckOutTime { get; set; }
    public bool PageFetched { get; set; }
    public bool ImageReachable { get; set; }
    public string? FetchedVia { get; set; }
}
