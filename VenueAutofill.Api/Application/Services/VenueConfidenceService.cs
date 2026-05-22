using System.Globalization;
using Microsoft.Extensions.Options;
using VenueAutofill.Api.Application.Interfaces;
using VenueAutofill.Api.Configuration;
using VenueAutofill.Api.Contracts;
using VenueAutofill.Api.Contracts.Internal;
using VenueAutofill.Api.Contracts.Requests;

namespace VenueAutofill.Api.Application.Services;

public class VenueConfidenceService : IVenueConfidenceService
{
    private readonly VenueAutofillOptions _options;

    public VenueConfidenceService(IOptions<VenueAutofillOptions> options) =>
        _options = options.Value;

    public void ScoreCandidates(VenueAutofillRequest request, IList<VenueCandidate> candidates)
    {
        foreach (var candidate in candidates)
        {
            var breakdown = new ConfidenceBreakdown
            {
                NameMatch = ScoreName(request.VenueName, candidate.Name),
                LocationMatch = ScoreLocation(request, candidate),
                VenueTypeMatch = ScoreVenueType(request, candidate),
                SourceReliability = ScoreSourceReliability(request, candidate),
                DataCompleteness = ScoreCompleteness(candidate),
                CrossSourceConsistency = 8
            };

            candidate.ConfidenceBreakdown = breakdown;
            candidate.ConfidenceScore = Math.Clamp(breakdown.Total, 0, 100);
        }
    }

    public ConfidenceDecision Evaluate(IReadOnlyList<VenueCandidate> rankedCandidates)
    {
        var warnings = new List<string>();
        if (rankedCandidates.Count == 0)
        {
            return new ConfidenceDecision
            {
                Kind = ConfidenceDecisionKind.NotFound,
                Warnings = ["No candidates returned from discovery."]
            };
        }

        var ranked = rankedCandidates.OrderByDescending(c => c.ConfidenceScore).ToList();
        var top = ranked[0];

        if (top.ConfidenceScore < _options.MinimumNotFoundConfidence)
        {
            warnings.Add("Top candidate confidence below minimum threshold.");
            if (string.IsNullOrWhiteSpace(top.Website))
                warnings.Add("No official source could be verified.");
            return new ConfidenceDecision { Kind = ConfidenceDecisionKind.NotFound, TopCandidate = top, Warnings = warnings };
        }

        if (ranked.Count > 1)
        {
            var second = ranked[1];
            var gap = top.ConfidenceScore - second.ConfidenceScore;
            if (gap < _options.AmbiguousScoreGap
                || (top.ConfidenceScore >= _options.MinimumNotFoundConfidence && top.ConfidenceScore < _options.MinimumSuccessConfidence))
            {
                return new ConfidenceDecision
                {
                    Kind = ConfidenceDecisionKind.Ambiguous,
                    AmbiguousCandidates = ranked.Take(5).ToList(),
                    Warnings = warnings
                };
            }
        }

        if (top.ConfidenceScore < _options.MinimumSuccessConfidence)
            warnings.Add("Confidence below ideal success threshold.");

        return new ConfidenceDecision
        {
            Kind = ConfidenceDecisionKind.Success,
            TopCandidate = top,
            Warnings = warnings
        };
    }

    private static int ScoreName(string inputName, string candidateName)
    {
        var a = Normalize(inputName);
        var b = Normalize(candidateName);
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            return 0;
        if (a == b || b.Contains(a, StringComparison.Ordinal) || a.Contains(b, StringComparison.Ordinal))
            return 30;
        var ratio = FuzzyRatio(a, b);
        return ratio switch
        {
            >= 0.85 => 28,
            >= 0.7 => 22,
            >= 0.5 => 15,
            _ => 5
        };
    }

    private static int ScoreLocation(VenueAutofillRequest request, VenueCandidate candidate)
    {
        var score = 0;
        if (ContainsIgnoreCase(candidate.Country, request.Country) || ContainsIgnoreCase(candidate.Address, request.Country))
            score += 15;
        else
            return 0;

        if (ContainsIgnoreCase(candidate.City, request.City) || ContainsIgnoreCase(candidate.Address, request.City))
            score += 12;
        else
            score += 4;

        if (!string.IsNullOrWhiteSpace(request.Area)
            && (ContainsIgnoreCase(candidate.Area, request.Area) || ContainsIgnoreCase(candidate.Address, request.Area)))
            score += 3;

        return Math.Min(score, 30);
    }

    private static int ScoreVenueType(VenueAutofillRequest request, VenueCandidate candidate)
    {
        var hasRequestType = VenueTypeHelper.TryParse(request.VenueType, out var requestedType);
        var hasCandidateType = VenueTypeHelper.TryParse(candidate.VenueType, out var candidateType)
            || VenueTypeHelper.TryInfer(candidate.Name, candidate.GoogleTypes, out candidateType);

        if (!hasRequestType)
            return hasCandidateType ? 8 : 5;

        if (!hasCandidateType)
            return 5;

        return requestedType == candidateType ? 10 : 3;
    }

    private static int ScoreSourceReliability(VenueAutofillRequest request, VenueCandidate candidate)
    {
        if (!string.IsNullOrWhiteSpace(request.Source))
            return 10;
        if (!string.IsNullOrWhiteSpace(candidate.Website))
            return 9;
        return 4;
    }

    private static int ScoreCompleteness(VenueCandidate candidate)
    {
        var fields = 0;
        if (!string.IsNullOrWhiteSpace(candidate.Name)) fields++;
        if (!string.IsNullOrWhiteSpace(candidate.Address)) fields++;
        if (candidate.Latitude != 0 && candidate.Longitude != 0) fields++;
        if (!string.IsNullOrWhiteSpace(candidate.Phone)) fields++;
        if (candidate.Rating.HasValue) fields++;
        if (!string.IsNullOrWhiteSpace(candidate.Website)) fields++;
        return (int)Math.Round(fields / 6.0 * 10);
    }

    private static string Normalize(string value) =>
        string.Join(' ', value.ToLowerInvariant().Split([' ', ',', '.', '-'], StringSplitOptions.RemoveEmptyEntries));

    private static bool ContainsIgnoreCase(string? haystack, string needle) =>
        !string.IsNullOrWhiteSpace(haystack) && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static double FuzzyRatio(string a, string b)
    {
        var longer = a.Length >= b.Length ? a : b;
        var shorter = a.Length < b.Length ? a : b;
        if (longer.Length == 0)
            return 1.0;
        var matches = shorter.Count(c => longer.Contains(c));
        return matches / (double)longer.Length;
    }
}
