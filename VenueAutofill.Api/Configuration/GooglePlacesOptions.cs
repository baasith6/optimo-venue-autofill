namespace VenueAutofill.Api.Configuration;

public class GooglePlacesOptions
{
    public const string SectionName = "GooglePlaces";

    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://places.googleapis.com/v1";
    public string TextSearchEndpoint { get; set; } = "/places:searchText";
    public string PlaceDetailsEndpoint { get; set; } = "/places/{placeId}";
    public string PhotoEndpoint { get; set; } = "/{photoName}/media";
}
