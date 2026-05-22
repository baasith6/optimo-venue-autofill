using VenueAutofill.Api.Contracts.Responses;

namespace VenueAutofill.Api.Contracts.Internal;

public class EnrichmentResult
{
    public VenueAutofillStandardResponse Response { get; set; } = new();
    public int ConfidenceScore { get; set; }
    public string SourceUsed { get; set; } = string.Empty;
    public List<string> Warnings { get; set; } = [];
}
