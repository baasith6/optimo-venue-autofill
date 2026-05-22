namespace VenueAutofill.Api.Contracts.Internal;

public class ConfidenceBreakdown
{
    public int NameMatch { get; set; }
    public int LocationMatch { get; set; }
    public int VenueTypeMatch { get; set; }
    public int SourceReliability { get; set; }
    public int DataCompleteness { get; set; }
    public int CrossSourceConsistency { get; set; }
    public int Total => NameMatch + LocationMatch + VenueTypeMatch + SourceReliability + DataCompleteness + CrossSourceConsistency;
}
