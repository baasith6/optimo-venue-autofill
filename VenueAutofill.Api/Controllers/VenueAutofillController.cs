using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using VenueAutofill.Api.Application.Interfaces;
using VenueAutofill.Api.Contracts.Internal;
using VenueAutofill.Api.Contracts.Requests;
using VenueAutofill.Api.Contracts.Responses;

namespace VenueAutofill.Api.Controllers;

[ApiController]
[Route("api/venue-autofill")]
public class VenueAutofillController : ControllerBase
{
    private readonly IVenueAutofillService _service;
    private readonly IValidator<VenueAutofillRequest> _autofillValidator;
    private readonly IValidator<VenueAutofillConfirmRequest> _confirmValidator;
    private readonly ILogger<VenueAutofillController> _logger;

    public VenueAutofillController(
        IVenueAutofillService service,
        IValidator<VenueAutofillRequest> autofillValidator,
        IValidator<VenueAutofillConfirmRequest> confirmValidator,
        ILogger<VenueAutofillController> logger)
    {
        _service = service;
        _autofillValidator = autofillValidator;
        _confirmValidator = confirmValidator;
        _logger = logger;
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> Autofill(
        [FromBody] VenueAutofillRequest request,
        [FromQuery] bool includeConfidence = false,
        CancellationToken cancellationToken = default)
    {
        var validation = await _autofillValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
            return BadRequest(new { errors = validation.Errors.Select(e => e.ErrorMessage) });

        var outcome = await _service.AutofillAsync(request, cancellationToken);
        return MapOutcome(outcome, includeConfidence);
    }

    [HttpPost("confirm")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Confirm(
        [FromBody] VenueAutofillConfirmRequest request,
        [FromQuery] bool includeConfidence = false,
        CancellationToken cancellationToken = default)
    {
        var validation = await _confirmValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
            return BadRequest(new { errors = validation.Errors.Select(e => e.ErrorMessage) });

        var outcome = await _service.ConfirmAsync(request, cancellationToken);
        return MapOutcome(outcome, includeConfidence);
    }

    private IActionResult MapOutcome(VenueAutofillOutcome outcome, bool includeConfidence)
    {
        switch (outcome.Kind)
        {
            case VenueAutofillOutcomeKind.Success:
                if (!includeConfidence)
                    return Ok(outcome.Success);
                return Ok(BuildSuccessWithConfidence(outcome));
            case VenueAutofillOutcomeKind.Ambiguous:
                return Ok(outcome.Ambiguous);
            case VenueAutofillOutcomeKind.NotFound:
                return Ok(outcome.NotFound);
            case VenueAutofillOutcomeKind.Error:
                if (outcome.Error is not null)
                    outcome.Error.TraceId = HttpContext.TraceIdentifier;
                return StatusCode(outcome.StatusCode, outcome.Error);
            default:
                _logger.LogWarning("Unknown outcome kind");
                return StatusCode(500, new { message = "Unknown error." });
        }
    }

    private static VenueAutofillSuccessWithConfidenceResponse BuildSuccessWithConfidence(VenueAutofillOutcome outcome)
    {
        var baseResponse = outcome.Success ?? new VenueAutofillStandardResponse();
        return new VenueAutofillSuccessWithConfidenceResponse
        {
            Name = baseResponse.Name,
            VenueType = baseResponse.VenueType,
            Rating = baseResponse.Rating,
            CheckInTime = baseResponse.CheckInTime,
            CheckOutTime = baseResponse.CheckOutTime,
            Amenities = baseResponse.Amenities,
            Description = baseResponse.Description,
            Image = baseResponse.Image,
            Country = baseResponse.Country,
            Location = baseResponse.Location,
            Latitude = baseResponse.Latitude,
            Longitude = baseResponse.Longitude,
            MapUrl = baseResponse.MapUrl,
            Email = baseResponse.Email,
            Phone = baseResponse.Phone,
            ConfidenceScore = outcome.ConfidenceScore,
            ConfidenceBreakdown = outcome.ConfidenceBreakdown,
            SourceUsed = outcome.SourceUsed,
            ImageSource = outcome.ImageSource,
            SourcesChecked = outcome.SourcesChecked.Count > 0 ? outcome.SourcesChecked : null,
            ImageCandidates = outcome.ImageCandidates.Count > 0 ? outcome.ImageCandidates : null,
            Warnings = outcome.Warnings.Count > 0 ? outcome.Warnings : null
        };
    }
}
