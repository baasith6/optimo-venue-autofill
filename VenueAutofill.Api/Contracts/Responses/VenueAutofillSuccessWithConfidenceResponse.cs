using System.Text.Json.Serialization;

namespace VenueAutofill.Api.Contracts.Responses;

/// <summary>Flat venue fields plus optional confidence metadata when includeConfidence=true.</summary>
public class VenueAutofillSuccessWithConfidenceResponse : VenueAutofillStandardResponse
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ConfidenceScore { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ConfidenceBreakdownResponse? ConfidenceBreakdown { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SourceUsed { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ImageSourceInfo? ImageSource { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<SourceCheckResult>? SourcesChecked { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? ImageCandidates { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Warnings { get; set; }
}
