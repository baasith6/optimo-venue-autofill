using VenueAutofill.Api.Contracts.Internal;
using VenueAutofill.Api.Contracts.Requests;
using VenueAutofill.Api.Contracts.Responses;

namespace VenueAutofill.Api.Application.Interfaces;

public interface IVenueConfidenceService
{
    void ScoreCandidates(VenueAutofillRequest request, IList<VenueCandidate> candidates);
    ConfidenceDecision Evaluate(IReadOnlyList<VenueCandidate> rankedCandidates);
    void ApplyPostEnrichmentScore(VenueCandidate candidate, int crossSourceConsistency);
    ConfidenceBreakdownResponse ToBreakdownResponse(ConfidenceBreakdown breakdown);
}

public enum ConfidenceDecisionKind
{
    Success,
    Ambiguous,
    NotFound
}

public class ConfidenceDecision
{
    public ConfidenceDecisionKind Kind { get; set; }
    public VenueCandidate? TopCandidate { get; set; }
    public IReadOnlyList<VenueCandidate> AmbiguousCandidates { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}
