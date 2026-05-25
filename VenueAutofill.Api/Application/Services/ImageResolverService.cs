using Microsoft.Extensions.Options;
using VenueAutofill.Api.Application.Interfaces;
using VenueAutofill.Api.Configuration;
using VenueAutofill.Api.Contracts;
using VenueAutofill.Api.Contracts.Internal;
using VenueAutofill.Api.Contracts.Requests;
using VenueAutofill.Api.Contracts.Responses;
using VenueAutofill.Api.Infrastructure.Http;

namespace VenueAutofill.Api.Application.Services;

public class ImageResolverService : IImageResolverService
{
    private const int MinMatchScoreForBookingImage = 40;
    private const int AgreementBonus = 40;

    private readonly VenueAutofillOptions _options;

    public ImageResolverService(IOptions<VenueAutofillOptions> options)
    {
        _options = options.Value;
    }

    public ImageResolveResult Resolve(
        VenueAutofillRequest request,
        RetrievalMode mode,
        VenueCandidate candidate,
        CrossSourceEnrichmentContext crossSource,
        string? googlePhotoUrl,
        string? officialWebsiteImageUrl,
        string? userSourceImageUrl)
    {
        var warnings = new List<string>();
        var rawCandidates = new List<(string Url, ImageSourceInfo Source, int BaseWeight, bool Reachable)>();

        if (mode == RetrievalMode.CustomSource
            && !string.IsNullOrWhiteSpace(userSourceImageUrl)
            && crossSource.SourcesChecked.Any(s => s.SourceId == "user_source" && s.Status is "matched" or "partial"))
        {
            rawCandidates.Add((userSourceImageUrl, new ImageSourceInfo
            {
                SourceId = "user_source",
                Label = "User-provided source",
                Url = Sanitize(userSourceImageUrl)
            }, 90, true));
        }

        if (!string.IsNullOrWhiteSpace(officialWebsiteImageUrl))
        {
            rawCandidates.Add((officialWebsiteImageUrl, new ImageSourceInfo
            {
                SourceId = "official_website",
                Label = "Official website",
                Url = Sanitize(officialWebsiteImageUrl)
            }, 100, true));
        }

        if (!string.IsNullOrWhiteSpace(googlePhotoUrl))
        {
            rawCandidates.Add((googlePhotoUrl, new ImageSourceInfo
            {
                SourceId = "google_places",
                Label = "Google Places",
                Url = Sanitize(googlePhotoUrl)
            }, 50, true));
        }

        foreach (var probe in crossSource.Probes.Where(p => !string.IsNullOrWhiteSpace(p.ImageUrl)))
        {
            if (rawCandidates.Any(c => c.Url == probe.ImageUrl))
                continue;

            var check = crossSource.SourcesChecked.FirstOrDefault(s => s.SourceId == probe.SourceId);
            var probeScore = check?.Score ?? 0;
            if (probe.SourceId is not "official_website" and not "user_source"
                && probeScore < MinMatchScoreForBookingImage)
                continue;

            var weight = probe.SourceId == "official_website"
                ? 100
                : 80 + probeScore;

            rawCandidates.Add((probe.ImageUrl!, new ImageSourceInfo
            {
                SourceId = probe.SourceId,
                Label = probe.Label,
                Url = Sanitize(probe.ImageUrl!)
            }, weight, probe.ImageReachable || probe.SourceId == "google_places"));
        }

        if (rawCandidates.Count == 0)
        {
            warnings.Add("No validated image URL found across sources.");
            return new ImageResolveResult { Warnings = warnings };
        }

        var normalizedGroups = rawCandidates
            .GroupBy(c => ImageUrlNormalizer.NormalizeForComparison(c.Url))
            .ToList();

        var scored = rawCandidates.Select(c =>
        {
            var norm = ImageUrlNormalizer.NormalizeForComparison(c.Url);
            var agreementCount = normalizedGroups.First(g => g.Key == norm).Count();
            var agreed = agreementCount >= 2;
            var score = c.BaseWeight + (agreed ? AgreementBonus : 0) + (c.Reachable ? 5 : 0);
            return new
            {
                c.Url,
                c.Source,
                Score = score,
                CrossSourceAgreed = agreed,
                AgreementCount = agreementCount
            };
        }).OrderByDescending(x => x.Score).ThenByDescending(x => x.AgreementCount).ToList();

        var details = scored
            .Take(5)
            .Select(x => new ImageCandidateDetail
            {
                Url = Sanitize(x.Url),
                SourceId = x.Source.SourceId,
                Label = x.Source.Label,
                Score = x.Score,
                CrossSourceAgreed = x.CrossSourceAgreed
            })
            .ToList();

        var agreedWinner = scored.FirstOrDefault(x => x.CrossSourceAgreed);
        var top = agreedWinner ?? scored[0];
        var imageVerified = top.CrossSourceAgreed
            || (top.Source.SourceId is "official_website" && top.Score >= _options.MinImageConfidenceScore)
            || (top.Source.SourceId is not "google_places" && top.Score >= _options.MinImageConfidenceScore);

        if (!imageVerified && top.Source.SourceId == "google_places")
        {
            warnings.Add("Image not verified across sources (only Google Places photo available).");
            if (_options.RequireCrossSourceImageAgreement)
            {
                warnings.Add("Image rejected: cross-source verification required.");
                return new ImageResolveResult
                {
                    ImageCandidates = details,
                    Warnings = warnings,
                    ImageVerified = false
                };
            }
        }

        if (scored.Count >= 2)
        {
            var second = scored[1];
            if (!top.CrossSourceAgreed
                && ImageUrlNormalizer.NormalizeForComparison(top.Url) != ImageUrlNormalizer.NormalizeForComparison(second.Url)
                && top.Score >= 60 && second.Score >= 60)
            {
                warnings.Add($"Image sources disagree; selected {top.Source.Label}.");
            }
        }

        return new ImageResolveResult
        {
            ImageUrl = top.Url,
            ImageSource = top.Source,
            ImageCandidates = details,
            Warnings = warnings,
            ImageVerified = imageVerified
        };
    }

    private static string Sanitize(string url) => ImageUrlNormalizer.SanitizeForResponse(url);
}
