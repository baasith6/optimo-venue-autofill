namespace VenueAutofill.Api.Contracts.Requests;

public class VenueAutofillRequest
{
    public string VenueName { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string? Area { get; set; }
    public string? VenueType { get; set; }
    /// <summary>automatic | officialWebsite | googlePlaces | customSource | bookingPlatform</summary>
    public string? RetrievalMode { get; set; }
    /// <summary>Required when retrievalMode is bookingPlatform (e.g. booking.com).</summary>
    public string? PlatformId { get; set; }
    public string? Source { get; set; }
}
