using System.Text.Json.Serialization;

namespace VenueAutofill.Api.Contracts.Responses;

public class ImageCandidateDetail
{
    public string Url { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int Score { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool CrossSourceAgreed { get; set; }
}
