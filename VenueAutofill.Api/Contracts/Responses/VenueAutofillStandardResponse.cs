using System.Text.Json.Serialization;

namespace VenueAutofill.Api.Contracts.Responses;

public class VenueAutofillStandardResponse
{
    public string Name { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? VenueType { get; set; }

    public int Rating { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CheckInTime { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CheckOutTime { get; set; }

    public List<string> Amenities { get; set; } = [];
    public string Description { get; set; } = string.Empty;
    public string Image { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string MapUrl { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
}
