namespace VenueAutofill.Api.Configuration;

public class GoogleCustomSearchOptions
{
    public const string SectionName = "GoogleCustomSearch";

    public string ApiKey { get; set; } = string.Empty;
    public string SearchEngineId { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://www.googleapis.com/customsearch/v1";
}
