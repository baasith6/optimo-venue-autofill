namespace VenueAutofill.Api.Contracts.Requests;

public class VenueAutofillConfirmRequest
{
    public string Reference { get; set; } = string.Empty;
    public string OptionId { get; set; } = string.Empty;
}
