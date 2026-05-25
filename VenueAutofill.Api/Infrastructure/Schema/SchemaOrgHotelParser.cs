using System.Text.Json;
using System.Text.RegularExpressions;

namespace VenueAutofill.Api.Infrastructure.Schema;

public static class SchemaOrgHotelParser
{
    private static readonly string[] LodgingTypes =
    [
        "Hotel", "LodgingBusiness", "Motel", "Resort", "Hostel"
    ];

    public static SchemaOrgHotelData ParseFromHtml(string html)
    {
        var merged = new SchemaOrgHotelData();
        foreach (var json in ExtractJsonLdBlocks(html))
        {
            if (!IsLodgingType(json))
                continue;

            var block = ParseLodgingElement(json);
            Merge(merged, block);
        }

        return merged;
    }

    public static void Merge(SchemaOrgHotelData target, SchemaOrgHotelData source)
    {
        target.Name ??= source.Name;
        target.ImageUrl ??= source.ImageUrl;
        target.CheckInTime ??= source.CheckInTime;
        target.CheckOutTime ??= source.CheckOutTime;
        target.City ??= source.City;
        target.Country ??= source.Country;
        target.Phone ??= source.Phone;
    }

    public static SchemaOrgHotelData ParseLodgingElement(JsonElement json)
    {
        var data = new SchemaOrgHotelData();

        if (TryGetString(json, "name", out var name))
            data.Name = name;

        if (TryGetString(json, "image", out var image))
            data.ImageUrl = image;

        if (TryGetTimeProperty(json, "checkinTime", out var checkIn)
            || TryGetTimeProperty(json, "checkInTime", out checkIn))
            data.CheckInTime = checkIn;

        if (TryGetTimeProperty(json, "checkoutTime", out var checkOut)
            || TryGetTimeProperty(json, "checkOutTime", out checkOut))
            data.CheckOutTime = checkOut;

        if (json.TryGetProperty("address", out var address))
        {
            if (address.TryGetProperty("addressLocality", out var city))
                data.City = city.GetString();
            if (address.TryGetProperty("addressCountry", out var country))
                data.Country = country.GetString();
        }

        if (TryGetString(json, "telephone", out var phone))
            data.Phone = phone;

        return data;
    }

    public static string? NormalizeTimeString(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        raw = raw.Trim();

        if (TimeSpan.TryParse(raw, out var ts))
            return ts.ToString(@"hh\:mm\:ss");

        if (DateTime.TryParse(raw, out var dt))
            return dt.ToString("HH:mm:ss");

        var timeMatch = Regex.Match(raw, @"(\d{1,2}:\d{2}(?::\d{2})?)\s*(am|pm)?", RegexOptions.IgnoreCase);
        if (timeMatch.Success)
        {
            var fragment = timeMatch.Groups[1].Value;
            if (timeMatch.Groups[2].Success
                && DateTime.TryParse($"{fragment} {timeMatch.Groups[2].Value}", out var parsed12))
                return parsed12.ToString("HH:mm:ss");
            if (TimeSpan.TryParse(fragment, out var parsedTs))
                return parsedTs.ToString(@"hh\:mm\:ss");
        }

        return raw.Contains(':') ? raw : null;
    }

    private static bool IsLodgingType(JsonElement json)
    {
        if (!json.TryGetProperty("@type", out var typeEl))
            return false;

        if (typeEl.ValueKind == JsonValueKind.String)
        {
            var t = typeEl.GetString() ?? "";
            return LodgingTypes.Any(l => t.Contains(l, StringComparison.OrdinalIgnoreCase));
        }

        if (typeEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in typeEl.EnumerateArray())
            {
                var t = item.GetString() ?? "";
                if (LodgingTypes.Any(l => t.Contains(l, StringComparison.OrdinalIgnoreCase)))
                    return true;
            }
        }

        return false;
    }

    private static bool TryGetTimeProperty(JsonElement json, string property, out string? value)
    {
        value = null;
        if (!TryGetString(json, property, out var raw))
            return false;

        value = NormalizeTimeString(raw);
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryGetString(JsonElement json, string property, out string? value)
    {
        value = null;
        if (!json.TryGetProperty(property, out var prop))
            return false;

        value = prop.ValueKind switch
        {
            JsonValueKind.String => prop.GetString(),
            JsonValueKind.Array when prop.GetArrayLength() > 0 && prop[0].ValueKind == JsonValueKind.String => prop[0].GetString(),
            JsonValueKind.Object when prop.TryGetProperty("url", out var urlProp) => urlProp.GetString(),
            _ => null
        };

        return !string.IsNullOrWhiteSpace(value);
    }

    private static List<JsonElement> ExtractJsonLdBlocks(string html)
    {
        var results = new List<JsonElement>();
        var matches = Regex.Matches(html, @"<script[^>]*type=[""']application/ld\+json[""'][^>]*>(.*?)</script>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        foreach (Match match in matches)
        {
            var jsonText = match.Groups[1].Value.Trim();
            if (string.IsNullOrWhiteSpace(jsonText))
                continue;

            try
            {
                using var doc = JsonDocument.Parse(jsonText);
                CollectElements(doc.RootElement, results);
            }
            catch
            {
                // skip invalid JSON-LD
            }
        }

        return results;
    }

    private static void CollectElements(JsonElement root, List<JsonElement> results)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
                CollectElements(item, results);
            return;
        }

        if (root.ValueKind != JsonValueKind.Object)
            return;

        if (root.TryGetProperty("@graph", out var graph) && graph.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in graph.EnumerateArray())
                results.Add(item.Clone());
            return;
        }

        results.Add(root.Clone());
    }
}
