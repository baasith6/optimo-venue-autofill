namespace VenueAutofill.Api.Contracts.Internal;

public class CseSearchResult
{
    public string? Url { get; set; }
    public string? SkipReason { get; set; }
    public bool Succeeded => !string.IsNullOrWhiteSpace(Url);
}
