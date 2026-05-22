namespace VenueAutofill.Api.Infrastructure.Http;

public class SourceRelevanceValidator
{
    private static readonly Dictionary<string, string[]> CountryAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["United States"] = ["usa", "us", "united states", "america"],
        ["USA"] = ["usa", "us", "united states", "america"],
        ["US"] = ["usa", "us", "united states"],
        ["United Kingdom"] = ["uk", "united kingdom", "great britain"],
        ["UK"] = ["uk", "united kingdom"]
    };

    public bool IsUrlRelevant(string url, string venueName, string city, string country)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        var pathAndQuery = (uri.Host + uri.PathAndQuery).ToLowerInvariant();
        var cityMatch = ContainsToken(pathAndQuery, city);
        var venueTokens = GetSignificantTokens(venueName);
        var venueMatch = venueTokens.Count(t => pathAndQuery.Contains(t, StringComparison.OrdinalIgnoreCase)) >= Math.Min(2, venueTokens.Count);

        return cityMatch || venueMatch;
    }

    public bool IsContentRelevant(string text, string venueName, string city, string country)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var normalized = text.ToLowerInvariant();
        if (!ContainsToken(normalized, city))
            return false;

        if (!CountryMentioned(normalized, country))
            return false;

        var venueTokens = GetSignificantTokens(venueName);
        var matchedVenueTokens = venueTokens.Count(t => normalized.Contains(t, StringComparison.OrdinalIgnoreCase));
        return matchedVenueTokens >= Math.Min(1, venueTokens.Count);
    }

    private static bool CountryMentioned(string text, string country)
    {
        if (ContainsToken(text, country))
            return true;

        if (CountryAliases.TryGetValue(country.Trim(), out var aliases))
            return aliases.Any(a => text.Contains(a, StringComparison.OrdinalIgnoreCase));

        return false;
    }

    private static List<string> GetSignificantTokens(string value) =>
        value.Split([' ', ',', '.', '-', '&'], StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length > 2)
            .Select(t => t.ToLowerInvariant())
            .Distinct()
            .ToList();

    private static bool ContainsToken(string haystack, string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;
        return haystack.Contains(token.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
