namespace VenueAutofill.Api.Contracts.Responses;

public class NotFoundResponse
{
    public string Status { get; set; } = "not_found";
    public string Reference { get; set; } = string.Empty;
    public string Message { get; set; } = "No reliable matching venue was found.";
    public List<string> Warnings { get; set; } = [];
}
