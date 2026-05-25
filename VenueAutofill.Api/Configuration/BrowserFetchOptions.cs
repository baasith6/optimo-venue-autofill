namespace VenueAutofill.Api.Configuration;

public class BrowserFetchOptions
{
    public const string SectionName = "BrowserFetch";

    public bool Enabled { get; set; } = true;
    public bool UsePlaywrightFallback { get; set; } = true;
    public int PlaywrightTimeoutSeconds { get; set; } = 25;
    public int MaxConcurrentBrowsers { get; set; } = 2;
    public int HttpTimeoutSeconds { get; set; } = 20;
}
