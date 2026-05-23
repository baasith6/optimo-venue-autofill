using Microsoft.Extensions.Options;
using VenueAutofill.Api.Application.Interfaces;
using VenueAutofill.Api.Configuration;
using VenueAutofill.Api.Contracts;
using VenueAutofill.Api.Contracts.Internal;
using VenueAutofill.Api.Contracts.Requests;
using VenueAutofill.Api.Contracts.Responses;
using VenueAutofill.Api.Infrastructure.Providers;

namespace VenueAutofill.Api.Application.Services;

public class VenueAutofillService : IVenueAutofillService
{
    private readonly VenueAutofillOptions _options;
    private readonly IVenueDiscoveryService _discovery;
    private readonly IVenueDetailsService _details;
    private readonly IVenueConfidenceService _confidence;
    private readonly IVenueCrossSourceService _crossSource;
    private readonly IVenueExtractionService _extraction;
    private readonly IAiVenueFormatterService _aiFormatter;
    private readonly IImageResolverService _imageResolver;
    private readonly IZoneResolverService _zoneResolver;
    private readonly IAmbiguousSearchCache _cache;
    private readonly ILogger<VenueAutofillService> _logger;

    public VenueAutofillService(
        IOptions<VenueAutofillOptions> options,
        IVenueDiscoveryService discovery,
        IVenueDetailsService details,
        IVenueConfidenceService confidence,
        IVenueCrossSourceService crossSource,
        IVenueExtractionService extraction,
        IAiVenueFormatterService aiFormatter,
        IImageResolverService imageResolver,
        IZoneResolverService zoneResolver,
        IAmbiguousSearchCache cache,
        ILogger<VenueAutofillService> logger)
    {
        _options = options.Value;
        _discovery = discovery;
        _details = details;
        _confidence = confidence;
        _crossSource = crossSource;
        _extraction = extraction;
        _aiFormatter = aiFormatter;
        _imageResolver = imageResolver;
        _zoneResolver = zoneResolver;
        _cache = cache;
        _logger = logger;
    }

    public async Task<VenueAutofillOutcome> AutofillAsync(VenueAutofillRequest request, CancellationToken cancellationToken = default)
    {
        if (_options.UseMocks)
            return MockVenueDataService.ResolveMockAutofill(request);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.RequestTimeoutSeconds));
        var token = timeoutCts.Token;

        try
        {
            var candidates = (await _discovery.DiscoverAsync(request, token)).ToList();
            _logger.LogInformation(
                "Venue autofill search: {VenueName}, {City}, {Country}, candidates={Count}",
                request.VenueName, request.City, request.Country, candidates.Count);

            if (candidates.Count == 0)
                return BuildNotFound(request, ["No candidates returned from Google Places."]);

            _confidence.ScoreCandidates(request, candidates);
            var decision = _confidence.Evaluate(candidates);

            return decision.Kind switch
            {
                ConfidenceDecisionKind.Ambiguous => BuildAmbiguous(request, decision.AmbiguousCandidates),
                ConfidenceDecisionKind.NotFound => BuildNotFound(request, decision.Warnings),
                ConfidenceDecisionKind.Success => await EnrichCandidateAsync(request, decision.TopCandidate!, decision.Warnings, token),
                _ => BuildNotFound(request, ["Unable to evaluate candidates."])
            };
        }
        catch (ExternalProviderException ex)
        {
            _logger.LogError(ex, "External provider error from {Provider}", ex.Provider);
            var status = ex.ProviderStatusCode switch
            {
                401 or 403 => 503,
                504 => 504,
                _ => 502
            };
            return new VenueAutofillOutcome
            {
                Kind = VenueAutofillOutcomeKind.Error,
                StatusCode = status,
                Error = new ErrorResponse { Message = ex.Message }
            };
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Venue autofill timed out");
            return new VenueAutofillOutcome
            {
                Kind = VenueAutofillOutcomeKind.Error,
                StatusCode = 504,
                Error = new ErrorResponse
                {
                    Message = "Venue autofill timed out waiting for an external provider. Check network access to Google Places, Custom Search, and OpenRouter."
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Venue autofill failed");
            return new VenueAutofillOutcome
            {
                Kind = VenueAutofillOutcomeKind.Error,
                StatusCode = 502,
                Error = new ErrorResponse
                {
                    Message = $"Venue autofill failed: {ex.Message}"
                }
            };
        }
    }

    public async Task<VenueAutofillOutcome> ConfirmAsync(VenueAutofillConfirmRequest request, CancellationToken cancellationToken = default)
    {
        if (_options.UseMocks)
            return MockVenueDataService.ResolveMockConfirm(request);

        var session = _cache.Get(request.Reference);
        if (session is null)
        {
            return new VenueAutofillOutcome
            {
                Kind = VenueAutofillOutcomeKind.Error,
                StatusCode = 404,
                Error = new ErrorResponse { Message = "Search reference not found or expired." }
            };
        }

        var candidate = _cache.GetCandidate(request.Reference, request.OptionId);
        if (candidate is null)
        {
            return new VenueAutofillOutcome
            {
                Kind = VenueAutofillOutcomeKind.Error,
                StatusCode = 404,
                Error = new ErrorResponse { Message = "Selected option not found." }
            };
        }

        var autofillRequest = MergeConfirmRequest(session.OriginalRequest, request);
        return await EnrichCandidateAsync(autofillRequest, candidate, [], cancellationToken);
    }

    private static VenueAutofillRequest MergeConfirmRequest(VenueAutofillRequest original, VenueAutofillConfirmRequest confirm) =>
        new()
        {
            VenueName = original.VenueName,
            Country = original.Country,
            City = original.City,
            Area = original.Area,
            VenueType = original.VenueType,
            RetrievalMode = confirm.RetrievalMode ?? original.RetrievalMode,
            PlatformId = confirm.PlatformId ?? original.PlatformId,
            Source = confirm.Source ?? original.Source
        };

    private VenueAutofillOutcome BuildAmbiguous(VenueAutofillRequest request, IReadOnlyList<VenueCandidate> candidates)
    {
        var reference = _cache.Store(request, candidates);
        return new VenueAutofillOutcome
        {
            Kind = VenueAutofillOutcomeKind.Ambiguous,
            Ambiguous = new VenueAmbiguousResponse
            {
                Reference = reference,
                Options = candidates.Select((c, i) => new VenueOptionResponse
                {
                    OptionId = (i + 1).ToString(),
                    Name = c.Name,
                    VenueType = VenueTypeHelper.ToDisplayNameOrNull(c.VenueType),
                    Country = c.Country,
                    City = c.City,
                    Area = c.Area,
                    Address = c.Address,
                    Latitude = c.Latitude,
                    Longitude = c.Longitude,
                    Source = !string.IsNullOrWhiteSpace(c.Website) ? c.Website : c.MapUrl,
                    ConfidenceScore = c.ConfidenceScore
                }).ToList()
            }
        };
    }

    private VenueAutofillOutcome BuildNotFound(VenueAutofillRequest request, List<string> warnings)
    {
        var reference = $"search_{DateTime.UtcNow:yyyyMMdd}_{Guid.NewGuid():N}"[..32];
        return new VenueAutofillOutcome
        {
            Kind = VenueAutofillOutcomeKind.NotFound,
            NotFound = new NotFoundResponse
            {
                Reference = reference,
                Warnings = warnings
            }
        };
    }

    private async Task<VenueAutofillOutcome> EnrichCandidateAsync(
        VenueAutofillRequest request,
        VenueCandidate candidate,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var mode = RetrievalModeHelper.Parse(request.RetrievalMode);
        var detailed = await _details.GetDetailsAsync(candidate, cancellationToken) ?? candidate;

        var crossContext = await _crossSource.RunCrossCheckAsync(request, detailed, mode, cancellationToken);
        if (!_options.EnablePlatformCrossCheck && mode == RetrievalMode.Automatic)
            warnings.Add("Platform cross-check is disabled; confidence uses Google and official website only.");

        var cseSkipped = crossContext.SourcesChecked.Count(s => s.Status == "skipped" && s.SourceId != "google_places");
        if (cseSkipped > 0 && _options.EnablePlatformCrossCheck)
            warnings.Add($"{cseSkipped} booking platform(s) could not be verified (search or probe skipped).");

        _confidence.ApplyPostEnrichmentScore(detailed, crossContext.CrossSourceConsistencyScore);

        var extractionRequest = BuildExtractionRequest(request, mode, crossContext);
        var extracted = await _extraction.ExtractAsync(extractionRequest, detailed, cancellationToken);

        string description;
        List<string> amenities;
        if (mode == RetrievalMode.GooglePlaces)
        {
            description = $"Venue identified via Google Places: {detailed.Name} in {detailed.City}, {detailed.Country}.";
            amenities = [];
        }
        else
        {
            (description, amenities) = await _aiFormatter.FormatAsync(extractionRequest, detailed, extracted, cancellationToken);
        }

        var photoUrl = await _details.GetPhotoUrlAsync(detailed.PhotoName, cancellationToken);
        var officialImage = crossContext.Probes.FirstOrDefault(p => p.SourceId == "official_website")?.ImageUrl;
        var userImage = crossContext.Probes.FirstOrDefault(p => p.SourceId == "user_source")?.ImageUrl;

        var (image, imageSource, imageCandidates, imageWarnings) = _imageResolver.Resolve(
            request,
            mode,
            detailed,
            crossContext,
            photoUrl,
            officialImage ?? extracted.ImageUrl,
            userImage);

        warnings.AddRange(imageWarnings);

        var hasEffectiveType = VenueTypeHelper.TryResolveEffectiveType(request, detailed, out var venueType, out var venueTypeDisplay);
        var isHotel = hasEffectiveType && venueType == VenueType.Hotel;

        var zone = _zoneResolver.Resolve(detailed, request.Area);
        if (string.IsNullOrWhiteSpace(zone))
        {
            zone = request.City;
            warnings.Add("LA28 zone could not be resolved; using city as location fallback.");
        }

        if (isHotel && mode != RetrievalMode.GooglePlaces)
        {
            if (string.IsNullOrWhiteSpace(extracted.CheckInTime))
                warnings.Add("Check-in time not found on source website.");
            if (string.IsNullOrWhiteSpace(extracted.CheckOutTime))
                warnings.Add("Check-out time not found on source website.");
            if (amenities.Count == 0)
                warnings.Add("Amenities not found on source website.");
        }

        var sourceUsed = !string.IsNullOrWhiteSpace(extracted.SourceUrl)
            ? extracted.SourceUrl
            : crossContext.Probes.FirstOrDefault(p => p.SourceId == request.PlatformId)?.Url
              ?? detailed.Website;

        var breakdown = detailed.ConfidenceBreakdown is not null
            ? _confidence.ToBreakdownResponse(detailed.ConfidenceBreakdown)
            : null;

        _logger.LogInformation(
            "Venue enriched: {Name}, confidence={Score}, source={Source}, platformsChecked={Count}, warnings={Warnings}",
            detailed.Name,
            detailed.ConfidenceScore,
            sourceUsed,
            crossContext.SourcesChecked.Count,
            string.Join("; ", warnings));

        return new VenueAutofillOutcome
        {
            Kind = VenueAutofillOutcomeKind.Success,
            Success = new VenueAutofillStandardResponse
            {
                Name = $"{detailed.Name}, {detailed.City}",
                VenueType = venueTypeDisplay,
                Rating = (int)Math.Round(detailed.Rating ?? 0),
                CheckInTime = isHotel ? extracted.CheckInTime : null,
                CheckOutTime = isHotel ? extracted.CheckOutTime : null,
                Amenities = amenities,
                Description = description,
                Image = image,
                Country = detailed.Country,
                Location = zone,
                Latitude = detailed.Latitude,
                Longitude = detailed.Longitude,
                MapUrl = detailed.MapUrl,
                Email = extracted.Email,
                Phone = detailed.Phone
            },
            ConfidenceScore = detailed.ConfidenceScore,
            ConfidenceBreakdown = breakdown,
            SourceUsed = sourceUsed,
            ImageSource = imageSource,
            ImageCandidates = imageCandidates,
            SourcesChecked = crossContext.SourcesChecked,
            Warnings = warnings
        };
    }

    private static VenueAutofillRequest BuildExtractionRequest(
        VenueAutofillRequest request,
        RetrievalMode mode,
        CrossSourceEnrichmentContext crossContext)
    {
        if (mode != RetrievalMode.BookingPlatform)
            return request;

        var platformUrl = crossContext.Probes
            .FirstOrDefault(p => p.SourceId.Equals(request.PlatformId, StringComparison.OrdinalIgnoreCase))?.Url;

        if (string.IsNullOrWhiteSpace(platformUrl))
            return request;

        return new VenueAutofillRequest
        {
            VenueName = request.VenueName,
            Country = request.Country,
            City = request.City,
            Area = request.Area,
            VenueType = request.VenueType,
            RetrievalMode = request.RetrievalMode,
            PlatformId = request.PlatformId,
            Source = platformUrl
        };
    }
}
