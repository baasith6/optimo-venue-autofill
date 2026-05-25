using System.Text.Json;
using System.Text.RegularExpressions;
using VenueAutofill.Api.Application.Interfaces;
using VenueAutofill.Api.Contracts.Internal;

namespace VenueAutofill.Api.Application.Services;

public class AmenityNormalizationService : IAmenityNormalizationService
{
    private readonly IReadOnlyList<CanonicalAmenityEntry> _entries;

    public AmenityNormalizationService(IHostEnvironment environment)
    {
        var path = Path.Combine(environment.ContentRootPath, "Data", "amenities-canonical.json");
        _entries = File.Exists(path)
            ? JsonSerializer.Deserialize<List<CanonicalAmenityEntry>>(File.ReadAllText(path), JsonOptions) ?? []
            : [];
    }

    public IReadOnlyList<string> CanonicalLabels =>
        _entries.Select(e => e.Canonical).ToList();

    public List<string> Normalize(IEnumerable<string> rawHints, string? sourceText = null)
    {
        var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var corpus = BuildCorpus(rawHints, sourceText);

        foreach (var entry in _entries)
        {
            if (MatchesEntry(entry, corpus))
                matched.Add(entry.Canonical);
        }

        return _entries
            .Where(e => matched.Contains(e.Canonical))
            .Select(e => e.Canonical)
            .ToList();
    }

    private static string BuildCorpus(IEnumerable<string> rawHints, string? sourceText)
    {
        var parts = rawHints.Where(h => !string.IsNullOrWhiteSpace(h)).Select(h => h.Trim());
        var combined = string.Join(" | ", parts);
        if (!string.IsNullOrWhiteSpace(sourceText))
            combined += " | " + sourceText;
        return NormalizeText(combined);
    }

    private static bool MatchesEntry(CanonicalAmenityEntry entry, string corpus)
    {
        foreach (var alias in entry.Aliases.Append(entry.Canonical))
        {
            var normalizedAlias = NormalizeText(alias);
            if (normalizedAlias.Length < 3)
                continue;

            if (corpus.Contains(normalizedAlias, StringComparison.Ordinal))
                return true;

            if (FuzzyContains(corpus, normalizedAlias))
                return true;
        }

        return false;
    }

    private static bool FuzzyContains(string corpus, string alias)
    {
        var aliasTokens = alias.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (aliasTokens.Length > 1 && aliasTokens.All(t => corpus.Contains(t, StringComparison.Ordinal)))
            return true;

        foreach (var segment in corpus.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = segment.Trim();
            if (trimmed.Length == 0)
                continue;
            if (FuzzyRatio(trimmed, alias) >= 0.78)
                return true;
        }

        return false;
    }

    private static double FuzzyRatio(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0)
            return 0;
        var longer = a.Length >= b.Length ? a : b;
        var shorter = a.Length < b.Length ? a : b;
        var matches = shorter.Count(c => longer.Contains(c));
        return matches / (double)longer.Length;
    }

    private static string NormalizeText(string value) =>
        Regex.Replace(value.ToLowerInvariant(), @"[^a-z0-9\s/]", " ").Trim();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
