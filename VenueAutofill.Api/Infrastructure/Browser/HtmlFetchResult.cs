namespace VenueAutofill.Api.Infrastructure.Browser;

public class HtmlFetchResult
{
    public bool Succeeded { get; set; }
    public string Html { get; set; } = string.Empty;
    public string FinalUrl { get; set; } = string.Empty;
    public string FetchedVia { get; set; } = "none";
    public int StatusCode { get; set; }
    public bool LooksBlocked { get; set; }
}
