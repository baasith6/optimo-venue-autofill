using VenueAutofill.Api.Application.Interfaces;
using VenueAutofill.Api.Contracts;
using VenueAutofill.Api.Contracts.Internal;
using VenueAutofill.Api.Contracts.Requests;
using VenueAutofill.Api.Contracts.Responses;

namespace VenueAutofill.Api.Application.Services;

public class ImageResolverService : IImageResolverService
{
    private const int MinMatchScoreForBookingImage = 40;

    public (string ImageUrl, ImageSourceInfo? ImageSource, List<string> ImageCandidates, List<string> Warnings) Resolve(
        VenueAutofillRequest request,
        RetrievalMode mode,
        VenueCandidate candidate,
        CrossSourceEnrichmentContext crossSource,
        string? googlePhotoUrl,
        string? officialWebsiteImageUrl,
        string? userSourceImageUrl)
    {
        var warnings = new List<string>();
        var candidates = new List<(string Url, ImageSourceInfo Source, int Priority)>();

        if (mode == RetrievalMode.CustomSource
            && !string.IsNullOrWhiteSpace(userSourceImageUrl)
            && crossSource.SourcesChecked.Any(s => s.SourceId == "user_source" && s.Status == "matched"))
        {
            candidates.Add((userSourceImageUrl, new ImageSourceInfo
            {
                SourceId = "user_source",
                Label = "User-provided source",
                Url = userSourceImageUrl
            }, 1));
        }

        if (!string.IsNullOrWhiteSpace(officialWebsiteImageUrl))
        {
            candidates.Add((officialWebsiteImageUrl, new ImageSourceInfo
            {
                SourceId = "official_website",
                Label = "Official website",
                Url = officialWebsiteImageUrl
            }, 2));
        }

        if (!string.IsNullOrWhiteSpace(googlePhotoUrl))
        {
            candidates.Add((googlePhotoUrl, new ImageSourceInfo
            {
                SourceId = "google_places",
                Label = "Google Places",
                Url = googlePhotoUrl
            }, 3));
        }

        var bestPlatform = crossSource.Probes
            .Where(p => !string.IsNullOrWhiteSpace(p.ImageUrl) && p.ImageReachable)
            .Select(p => new
            {
                Probe = p,
                Check = crossSource.SourcesChecked.FirstOrDefault(s => s.SourceId == p.SourceId)
            })
            .Where(x => x.Check is not null && x.Check!.Score >= MinMatchScoreForBookingImage)
            .OrderByDescending(x => x.Check!.Score)
            .FirstOrDefault();

        if (bestPlatform?.Probe.ImageUrl is not null)
        {
            candidates.Add((bestPlatform.Probe.ImageUrl, new ImageSourceInfo
            {
                SourceId = bestPlatform.Probe.SourceId,
                Label = bestPlatform.Probe.Label,
                Url = bestPlatform.Probe.ImageUrl
            }, 4));
        }

        foreach (var probe in crossSource.Probes
            .Where(p => !string.IsNullOrWhiteSpace(p.ImageUrl))
            .OrderByDescending(p => crossSource.SourcesChecked.FirstOrDefault(s => s.SourceId == p.SourceId)?.Score ?? 0))
        {
            if (candidates.Any(c => c.Url == probe.ImageUrl))
                continue;
            candidates.Add((probe.ImageUrl!, new ImageSourceInfo
            {
                SourceId = probe.SourceId,
                Label = probe.Label,
                Url = probe.ImageUrl!
            }, 10));
        }

        var imageCandidateUrls = candidates.Select(c => c.Url).Distinct().Take(3).ToList();

        if (candidates.Count == 0)
        {
            warnings.Add("No validated image URL found across sources.");
            return (string.Empty, null, imageCandidateUrls, warnings);
        }

        var winner = candidates.OrderBy(c => c.Priority).First();
        return (winner.Url, winner.Source, imageCandidateUrls, warnings);
    }
}
