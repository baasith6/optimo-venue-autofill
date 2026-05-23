using Microsoft.Extensions.Options;
using VenueAutofill.Api.Application.Interfaces;
using VenueAutofill.Api.Configuration;
using VenueAutofill.Api.Contracts;
using VenueAutofill.Api.Contracts.Internal;
using VenueAutofill.Api.Contracts.Requests;
using VenueAutofill.Api.Contracts.Responses;
using VenueAutofill.Api.Infrastructure.Data;

namespace VenueAutofill.Api.Application.Services;

public class VenueCrossSourceService : IVenueCrossSourceService
{
    private readonly VenueAutofillOptions _options;
    private readonly BookingPlatformRegistry _platformRegistry;
    private readonly IGoogleCustomSearchService _customSearch;
    private readonly IListingProbeService _listingProbe;
    private readonly ILogger<VenueCrossSourceService> _logger;

    public VenueCrossSourceService(
        IOptions<VenueAutofillOptions> options,
        BookingPlatformRegistry platformRegistry,
        IGoogleCustomSearchService customSearch,
        IListingProbeService listingProbe,
        ILogger<VenueCrossSourceService> logger)
    {
        _options = options.Value;
        _platformRegistry = platformRegistry;
        _customSearch = customSearch;
        _listingProbe = listingProbe;
        _logger = logger;
    }

    public async Task<CrossSourceEnrichmentContext> RunCrossCheckAsync(
        VenueAutofillRequest request,
        VenueCandidate candidate,
        RetrievalMode mode,
        CancellationToken cancellationToken = default)
    {
        var context = new CrossSourceEnrichmentContext();
        var checks = new List<SourceCheckResult>();

        checks.Add(ScoreGooglePlaces(request, candidate));

        if (mode == RetrievalMode.GooglePlaces)
        {
            context.SourcesChecked = checks;
            context.CrossSourceConsistencyScore = ComputeCrossSourceConsistency(checks);
            return context;
        }

        if (!string.IsNullOrWhiteSpace(candidate.Website))
        {
            var officialProbe = await _listingProbe.ProbeAsync(
                "official_website",
                "Official website",
                candidate.Website,
                request,
                candidate,
                cancellationToken);
            context.Probes.Add(officialProbe);
            checks.Add(ScoreProbe(request, candidate, officialProbe));
        }
        else if (mode == RetrievalMode.OfficialWebsite)
        {
            checks.Add(new SourceCheckResult
            {
                SourceId = "official_website",
                Label = "Official website",
                Status = "failed",
                Score = 0
            });
        }

        if (!string.IsNullOrWhiteSpace(request.Source))
        {
            var userProbe = await _listingProbe.ProbeAsync(
                "user_source",
                "User-provided source",
                request.Source!,
                request,
                candidate,
                cancellationToken);
            context.Probes.Add(userProbe);
            checks.Add(ScoreProbe(request, candidate, userProbe));
        }

        if (_options.EnablePlatformCrossCheck && mode is RetrievalMode.Automatic or RetrievalMode.BookingPlatform)
        {
            var platforms = GetPlatformsForMode(mode, request);
            using var semaphore = new SemaphoreSlim(_options.MaxConcurrentProbes);
            var platformTasks = platforms.Select(async platform =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    return await ProbePlatformAsync(request, candidate, platform, cancellationToken);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            var platformResults = await Task.WhenAll(platformTasks);
            foreach (var (check, probe) in platformResults)
            {
                checks.Add(check);
                if (probe is not null)
                    context.Probes.Add(probe);
            }
        }
        else if (mode == RetrievalMode.BookingPlatform && !_options.EnablePlatformCrossCheck)
        {
            checks.Add(new SourceCheckResult
            {
                SourceId = request.PlatformId ?? "booking_platform",
                Label = "Booking platform",
                Status = "skipped",
                Score = 0
            });
        }

        context.SourcesChecked = checks;
        context.CrossSourceConsistencyScore = ComputeCrossSourceConsistency(checks);
        return context;
    }

    private IEnumerable<BookingPlatformEntry> GetPlatformsForMode(RetrievalMode mode, VenueAutofillRequest request)
    {
        if (mode == RetrievalMode.BookingPlatform)
        {
            var platform = _platformRegistry.GetById(request.PlatformId ?? "");
            if (platform is not null && platform.ProbeEnabled)
                return [platform];
            return [];
        }

        return _platformRegistry.GetProbeEnabled(_options.MaxPlatformDiscoveryCount);
    }

    private async Task<(SourceCheckResult Check, ListingProbeResult? Probe)> ProbePlatformAsync(
        VenueAutofillRequest request,
        VenueCandidate candidate,
        BookingPlatformEntry platform,
        CancellationToken cancellationToken)
    {
        try
        {
            var listingUrl = await _customSearch.FindListingUrlAsync(request, platform, cancellationToken);
            if (string.IsNullOrWhiteSpace(listingUrl))
            {
                return (new SourceCheckResult
                {
                    SourceId = platform.PlatformId,
                    Label = platform.Label,
                    Status = "skipped",
                    Score = 0
                }, null);
            }

            var probe = await _listingProbe.ProbeAsync(
                platform.PlatformId,
                platform.Label,
                listingUrl,
                request,
                candidate,
                cancellationToken);

            return (ScoreProbe(request, candidate, probe), probe);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Platform cross-check failed for {Platform}", platform.PlatformId);
            return (new SourceCheckResult
            {
                SourceId = platform.PlatformId,
                Label = platform.Label,
                Status = "skipped",
                Score = 0
            }, null);
        }
    }

    internal static SourceCheckResult ScoreGooglePlaces(VenueAutofillRequest request, VenueCandidate candidate)
    {
        var matched = new List<string>();
        var score = 0;

        if (NameScore(request.VenueName, candidate.Name) >= 15)
        {
            score += 40;
            matched.Add("name");
        }
        else
            score += Math.Min(NameScore(request.VenueName, candidate.Name), 20);

        if (ContainsIgnoreCase(candidate.City, request.City) || ContainsIgnoreCase(candidate.Address, request.City))
        {
            score += 15;
            matched.Add("city");
        }

        if (ContainsIgnoreCase(candidate.Country, request.Country) || ContainsIgnoreCase(candidate.Address, request.Country))
        {
            score += 15;
            matched.Add("country");
        }

        if (!string.IsNullOrWhiteSpace(candidate.Phone))
        {
            score += 10;
            matched.Add("phone");
        }

        score = Math.Clamp(score, 0, 100);
        return new SourceCheckResult
        {
            SourceId = "google_places",
            Label = "Google Places",
            Url = candidate.MapUrl,
            Score = score,
            MatchedFields = matched,
            Status = StatusFromScore(score, true)
        };
    }

    internal static SourceCheckResult ScoreProbe(VenueAutofillRequest request, VenueCandidate candidate, ListingProbeResult probe)
    {
        if (!probe.PageFetched)
        {
            return new SourceCheckResult
            {
                SourceId = probe.SourceId,
                Label = probe.Label,
                Url = probe.Url,
                Status = string.IsNullOrWhiteSpace(probe.Url) ? "skipped" : "blocked",
                Score = 0
            };
        }

        var matched = new List<string>();
        var score = 0;
        var referenceName = candidate.Name;

        var nameToScore = probe.ExtractedName ?? "";
        var namePoints = NameScore(request.VenueName, string.IsNullOrWhiteSpace(nameToScore) ? referenceName : nameToScore);
        if (namePoints >= 12 || NameScore(request.VenueName, referenceName) >= 12)
        {
            score += 40;
            matched.Add("name");
        }
        else
            score += Math.Min(namePoints, 25);

        if (!string.IsNullOrWhiteSpace(probe.ExtractedCity)
            && (ContainsIgnoreCase(probe.ExtractedCity, request.City) || ContainsIgnoreCase(probe.ExtractedCity, candidate.City)))
        {
            score += 15;
            matched.Add("city");
        }
        else if (ContainsIgnoreCase(probe.Url, request.City))
        {
            score += 8;
            matched.Add("city");
        }

        if (!string.IsNullOrWhiteSpace(probe.ExtractedCountry)
            && ContainsIgnoreCase(probe.ExtractedCountry, request.Country))
        {
            score += 15;
            matched.Add("country");
        }

        if (!string.IsNullOrWhiteSpace(probe.ExtractedPhone))
        {
            score += 10;
            matched.Add("phone");
        }

        if (!string.IsNullOrWhiteSpace(probe.ImageUrl) && probe.ImageReachable)
        {
            score += 10;
            matched.Add("image");
        }
        else if (!string.IsNullOrWhiteSpace(probe.ImageUrl))
        {
            score += 5;
            matched.Add("image");
        }

        score = Math.Clamp(score, 0, 100);
        return new SourceCheckResult
        {
            SourceId = probe.SourceId,
            Label = probe.Label,
            Url = probe.Url,
            Score = score,
            MatchedFields = matched,
            Status = StatusFromScore(score, probe.PageFetched)
        };
    }

    internal static int ComputeCrossSourceConsistency(IReadOnlyList<SourceCheckResult> checks)
    {
        var scored = checks.Where(c => c.Status is "matched" or "partial").ToList();
        if (scored.Count == 0)
            return 0;

        var avg = scored.Average(c => c.Score);
        var consistency = (int)Math.Round(avg / 100.0 * 12);
        var matchedCount = checks.Count(c => c.Status == "matched");
        if (matchedCount >= 3)
            consistency = Math.Min(15, consistency + 3);
        if (matchedCount == 0 && scored.Count > 0)
            consistency = Math.Max(0, consistency - 2);

        return Math.Clamp(consistency, 0, 15);
    }

    private static string StatusFromScore(int score, bool fetched) =>
        !fetched ? "skipped" : score switch
        {
            >= 70 => "matched",
            >= 40 => "partial",
            _ => "failed"
        };

    private static int NameScore(string a, string b)
    {
        var na = Normalize(a);
        var nb = Normalize(b);
        if (string.IsNullOrEmpty(na) || string.IsNullOrEmpty(nb))
            return 0;
        if (na == nb || nb.Contains(na, StringComparison.Ordinal) || na.Contains(nb, StringComparison.Ordinal))
            return 40;
        var longer = na.Length >= nb.Length ? na : nb;
        var shorter = na.Length < nb.Length ? na : nb;
        var matches = shorter.Count(c => longer.Contains(c));
        var ratio = matches / (double)longer.Length;
        return ratio switch
        {
            >= 0.85 => 35,
            >= 0.7 => 25,
            >= 0.5 => 15,
            _ => 5
        };
    }

    private static string Normalize(string value) =>
        string.Join(' ', value.ToLowerInvariant().Split([' ', ',', '.', '-'], StringSplitOptions.RemoveEmptyEntries));

    private static bool ContainsIgnoreCase(string? haystack, string needle) =>
        !string.IsNullOrWhiteSpace(haystack) && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
