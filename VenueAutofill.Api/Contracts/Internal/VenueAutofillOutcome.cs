using VenueAutofill.Api.Contracts.Responses;

namespace VenueAutofill.Api.Contracts.Internal;

public enum VenueAutofillOutcomeKind
{
    Success,
    Ambiguous,
    NotFound,
    Error
}

public class VenueAutofillOutcome
{
    public VenueAutofillOutcomeKind Kind { get; set; }
    public VenueAutofillStandardResponse? Success { get; set; }
    public VenueAmbiguousResponse? Ambiguous { get; set; }
    public NotFoundResponse? NotFound { get; set; }
    public ErrorResponse? Error { get; set; }
    public int StatusCode { get; set; } = 200;

    public int? ConfidenceScore { get; set; }
    public string? SourceUsed { get; set; }
    public List<string> Warnings { get; set; } = [];
}
