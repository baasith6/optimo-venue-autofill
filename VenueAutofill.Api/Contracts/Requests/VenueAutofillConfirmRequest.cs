namespace VenueAutofill.Api.Contracts.Requests;

public class VenueAutofillConfirmRequest
{
    public string Reference { get; set; } = string.Empty;
    public string OptionId { get; set; } = string.Empty;
    public string? RetrievalMode { get; set; }
    public string? PlatformId { get; set; }
    public string? Source { get; set; }
}
