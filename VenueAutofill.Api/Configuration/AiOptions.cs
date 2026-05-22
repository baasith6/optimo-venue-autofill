namespace VenueAutofill.Api.Configuration;

public class AiOptions
{
    public const string SectionName = "AI";

    public string Provider { get; set; } = "OpenRouter";
    public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1";
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "openai/gpt-4.1-mini";
}
