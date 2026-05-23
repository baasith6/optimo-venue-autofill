namespace VenueAutofill.Api.Contracts.Responses;

public class ConfidenceBreakdownResponse
{
    public int NameMatch { get; set; }
    public int LocationMatch { get; set; }
    public int VenueTypeMatch { get; set; }
    public int SourceReliability { get; set; }
    public int DataCompleteness { get; set; }
    public int CrossSourceConsistency { get; set; }
}
