namespace VenueAutofill.Api.Application.Interfaces;

public interface IHotelTimesInferenceService
{
    Task<(string? CheckInTime, string? CheckOutTime, bool UsedAi)> InferAsync(
        string sourceText,
        CancellationToken cancellationToken = default);
}
