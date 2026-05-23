namespace VenueAutofill.Api.Contracts.Internal;

public class BookingPlatformEntry
{
    public string PlatformId { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool ProbeEnabled { get; set; } = true;
}
