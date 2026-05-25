namespace VenueAutofill.Api.Application.Interfaces;

public interface IAmenityNormalizationService
{
    IReadOnlyList<string> CanonicalLabels { get; }
    List<string> Normalize(IEnumerable<string> rawHints, string? sourceText = null);
}
