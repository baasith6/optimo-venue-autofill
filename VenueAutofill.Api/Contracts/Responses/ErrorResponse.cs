namespace VenueAutofill.Api.Contracts.Responses;

public class ErrorResponse
{
    public string Status { get; set; } = "error";
    public string Message { get; set; } = string.Empty;
    public string TraceId { get; set; } = string.Empty;
}
