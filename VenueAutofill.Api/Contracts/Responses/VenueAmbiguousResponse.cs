using System.Text.Json.Serialization;

namespace VenueAutofill.Api.Contracts.Responses;

public class VenueAmbiguousResponse
{
    public string Status { get; set; } = "ambiguous";
    public string Reference { get; set; } = string.Empty;
    public string Message { get; set; } = "Multiple matching venues found. Please select the correct venue.";
    public List<VenueOptionResponse> Options { get; set; } = [];
}

public class VenueOptionResponse
{
    public string OptionId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? VenueType { get; set; }

    public string Country { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string? Area { get; set; }
    public string Address { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string Source { get; set; } = string.Empty;
    public int ConfidenceScore { get; set; }
}
