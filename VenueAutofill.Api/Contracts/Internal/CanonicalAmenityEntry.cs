namespace VenueAutofill.Api.Contracts.Internal;

public class CanonicalAmenityEntry
{
    public string Canonical { get; set; } = string.Empty;
    public List<string> Aliases { get; set; } = [];
}
