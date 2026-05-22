using VenueAutofill.Api.Contracts.Internal;
using VenueAutofill.Api.Contracts.Requests;

namespace VenueAutofill.Api.Application.Interfaces;

public interface IVenueConfidenceService
{
    void ScoreCandidates(VenueAutofillRequest request, IList<VenueCandidate> candidates);
    ConfidenceDecision Evaluate(IReadOnlyList<VenueCandidate> rankedCandidates);
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
