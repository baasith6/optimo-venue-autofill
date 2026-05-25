using VenueAutofill.Api.Contracts.Internal;

namespace VenueAutofill.Api.Application.Services;

public static class HotelFieldMerger
{
    public static (string? CheckInTime, string? CheckOutTime) MergeCheckTimes(
        VenueExtractedData extracted,
        CrossSourceEnrichmentContext crossSource)
    {
        var checkIn = extracted.CheckInTime;
        var checkOut = extracted.CheckOutTime;

        var official = crossSource.Probes.FirstOrDefault(p => p.SourceId == "official_website");
        checkIn ??= official?.CheckInTime;
        checkOut ??= official?.CheckOutTime;

        var bestBooking = crossSource.Probes
            .Where(p => p.SourceId is not "official_website" and not "user_source" and not "google_places")
            .Select(p => new
            {
                Probe = p,
                Check = crossSource.SourcesChecked.FirstOrDefault(s => s.SourceId == p.SourceId)
            })
            .Where(x => x.Check is not null && x.Check.Score >= 40)
            .OrderByDescending(x => x.Check!.Score)
            .Select(x => x.Probe)
            .FirstOrDefault();

        checkIn ??= bestBooking?.CheckInTime;
        checkOut ??= bestBooking?.CheckOutTime;

        foreach (var probe in crossSource.Probes)
        {
            checkIn ??= probe.CheckInTime;
            checkOut ??= probe.CheckOutTime;
            if (!string.IsNullOrWhiteSpace(checkIn) && !string.IsNullOrWhiteSpace(checkOut))
                break;
        }

        return (checkIn, checkOut);
    }
}
