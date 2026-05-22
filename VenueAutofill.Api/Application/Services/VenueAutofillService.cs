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
    private readonly IVenueExtractionService _extraction;
    private readonly IAiVenueFormatterService _aiFormatter;
    private readonly IZoneResolverService _zoneResolver;
    private readonly IAmbiguousSearchCache _cache;
    private readonly ILogger<VenueAutofillService> _logger;

    public VenueAutofillService(
        IOptions<VenueAutofillOptions> options,
        IVenueDiscoveryService discovery,
        IVenueDetailsService details,
        IVenueConfidenceService confidence,
        IVenueExtractionService extraction,
        IAiVenueFormatterService aiFormatter,
        IZoneResolverService zoneResolver,
        IAmbiguousSearchCache cache,
        ILogger<VenueAutofillService> logger)
    {
        _options = options.Value;
        _discovery = discovery;
        _details = details;
        _confidence = confidence;
        _extraction = extraction;
        _aiFormatter = aiFormatter;
        _zoneResolver = zoneResolver;
        _cache = cache;
        _logger = logger;
    }

    public async Task<VenueAutofillOutcome> AutofillAsync(VenueAutofillRequest request, CancellationToken cancellationToken = default)
    {
        if (_options.UseMocks)
            return MockVenueDataService.ResolveMockAutofill(request);

        try
        {
            var candidates = (await _discovery.DiscoverAsync(request, cancellationToken)).ToList();
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
                ConfidenceDecisionKind.Success => await EnrichCandidateAsync(request, decision.TopCandidate!, decision.Warnings, cancellationToken),
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
                    Message = "Venue autofill timed out waiting for an external provider. Check network access to Google Places and OpenRouter."
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

        return await EnrichCandidateAsync(session.OriginalRequest, candidate, [], cancellationToken);
    }

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
        var detailed = await _details.GetDetailsAsync(candidate, cancellationToken) ?? candidate;
        var extracted = await _extraction.ExtractAsync(request, detailed, cancellationToken);
        var (description, amenities) = await _aiFormatter.FormatAsync(request, detailed, extracted, cancellationToken);

        var photoUrl = await _details.GetPhotoUrlAsync(detailed.PhotoName, cancellationToken);
        var image = extracted.ImageUrl ?? photoUrl ?? string.Empty;

        var hasEffectiveType = VenueTypeHelper.TryResolveEffectiveType(request, detailed, out var venueType, out var venueTypeDisplay);
        var isHotel = hasEffectiveType && venueType == VenueType.Hotel;

        var zone = _zoneResolver.Resolve(detailed, request.Area);
        if (string.IsNullOrWhiteSpace(zone))
        {
            zone = request.City;
            warnings.Add("LA28 zone could not be resolved; using city as location fallback.");
        }

        if (isHotel)
        {
            if (string.IsNullOrWhiteSpace(extracted.CheckInTime))
                warnings.Add("Check-in time not found on source website.");
            if (string.IsNullOrWhiteSpace(extracted.CheckOutTime))
                warnings.Add("Check-out time not found on source website.");
            if (amenities.Count == 0)
                warnings.Add("Amenities not found on source website.");
        }

        var enrichment = new EnrichmentResult
        {
            Response = new VenueAutofillStandardResponse
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
            SourceUsed = !string.IsNullOrWhiteSpace(extracted.SourceUrl) ? extracted.SourceUrl : detailed.Website,
            Warnings = warnings
        };

        _logger.LogInformation(
            "Venue enriched: {Name}, confidence={Score}, source={Source}, warnings={Warnings}",
            enrichment.Response.Name,
            enrichment.ConfidenceScore,
            enrichment.SourceUsed,
            string.Join("; ", enrichment.Warnings));

        return new VenueAutofillOutcome
        {
            Kind = VenueAutofillOutcomeKind.Success,
            Success = enrichment.Response,
            ConfidenceScore = enrichment.ConfidenceScore,
            SourceUsed = enrichment.SourceUsed,
            Warnings = enrichment.Warnings
        };
    }
}
