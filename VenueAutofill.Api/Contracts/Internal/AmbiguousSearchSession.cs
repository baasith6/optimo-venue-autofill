using VenueAutofill.Api.Contracts.Requests;

namespace VenueAutofill.Api.Contracts.Internal;

public class AmbiguousSearchSession
{
    public string Reference { get; set; } = string.Empty;
    public VenueAutofillRequest OriginalRequest { get; set; } = new();
    public List<VenueCandidate> Candidates { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
