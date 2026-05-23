namespace VenueAutofill.Api.Contracts;

public enum RetrievalMode
{
    Automatic,
    OfficialWebsite,
    GooglePlaces,
    CustomSource,
    BookingPlatform
}

public static class RetrievalModeHelper
{
    public static RetrievalMode Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return RetrievalMode.Automatic;

        var key = value.Trim().Replace("_", "", StringComparison.Ordinal).ToLowerInvariant();
        return key switch
        {
            "officialwebsite" => RetrievalMode.OfficialWebsite,
            "googleplaces" => RetrievalMode.GooglePlaces,
            "customsource" => RetrievalMode.CustomSource,
            "bookingplatform" => RetrievalMode.BookingPlatform,
            "automatic" => RetrievalMode.Automatic,
            _ => Enum.TryParse<RetrievalMode>(value, ignoreCase: true, out var mode) ? mode : RetrievalMode.Automatic
        };
    }

    public static string ToJsonValue(RetrievalMode mode) =>
        mode switch
        {
            RetrievalMode.Automatic => "automatic",
            RetrievalMode.OfficialWebsite => "officialWebsite",
            RetrievalMode.GooglePlaces => "googlePlaces",
            RetrievalMode.CustomSource => "customSource",
            RetrievalMode.BookingPlatform => "bookingPlatform",
            _ => "automatic"
        };
}
