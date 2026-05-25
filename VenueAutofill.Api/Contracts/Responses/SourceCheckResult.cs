using System.Text.Json.Serialization;

namespace VenueAutofill.Api.Contracts.Responses;

public class SourceCheckResult
{
    public string SourceId { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Url { get; set; }

    public string Status { get; set; } = "skipped";
    public int Score { get; set; }
    public List<string> MatchedFields { get; set; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SkipReason { get; set; }
}
