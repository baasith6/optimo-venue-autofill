namespace VenueAutofill.Api.Contracts.Internal;

public class VenueCandidate
{
    public string PlaceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string VenueType { get; set; } = string.Empty;
    public List<string> GoogleTypes { get; set; } = [];
    public string Country { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string? Area { get; set; }
    public string Address { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string Website { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public double? Rating { get; set; }
    public string PhotoName { get; set; } = string.Empty;
    public string MapUrl { get; set; } = string.Empty;
    public int ConfidenceScore { get; set; }
    public ConfidenceBreakdown? ConfidenceBreakdown { get; set; }
}
