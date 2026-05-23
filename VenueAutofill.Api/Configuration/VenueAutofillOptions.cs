namespace VenueAutofill.Api.Configuration;

public class VenueAutofillOptions
{
    public const string SectionName = "VenueAutofill";

    public bool UseMocks { get; set; } = true;
    public int AmbiguousCacheMinutes { get; set; } = 60;
    public int MinimumSuccessConfidence { get; set; } = 75;
    public int MinimumNotFoundConfidence { get; set; } = 50;
    public int AmbiguousScoreGap { get; set; } = 10;
    public int MaxExtractionPages { get; set; } = 3;
    public int DescriptionMaxWords { get; set; } = 35;
    public string? ApiKey { get; set; }
    public bool RequireApiKey { get; set; }
    public int RateLimitPermitLimit { get; set; } = 60;
    public int RateLimitWindowSeconds { get; set; } = 60;
    public bool EnablePlatformCrossCheck { get; set; } = true;
    public int MaxPlatformDiscoveryCount { get; set; } = 8;
    public int PlatformProbeTimeoutSeconds { get; set; } = 8;
    public int MaxConcurrentProbes { get; set; } = 4;
    public int RequestTimeoutSeconds { get; set; } = 45;
}
