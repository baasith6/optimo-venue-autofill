using VenueAutofill.Api.Contracts.Responses;

namespace VenueAutofill.Api.Contracts.Internal;

public class CrossSourceEnrichmentContext
{
    public List<SourceCheckResult> SourcesChecked { get; set; } = [];
    public int CrossSourceConsistencyScore { get; set; }
    public List<ListingProbeResult> Probes { get; set; } = [];
    public ImageSourceInfo? ImageSource { get; set; }
    public List<string> ImageCandidates { get; set; } = [];
}
