using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using VenueAutofill.Api.Application.Interfaces;
using VenueAutofill.Api.Contracts.Internal;
using VenueAutofill.Api.Contracts.Requests;

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
    public async Task<IActionResult> Autofill([FromBody] VenueAutofillRequest request, CancellationToken cancellationToken)
    {
        var validation = await _autofillValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
            return BadRequest(new { errors = validation.Errors.Select(e => e.ErrorMessage) });

        var outcome = await _service.AutofillAsync(request, cancellationToken);
        return MapOutcome(outcome);
    }

    [HttpPost("confirm")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Confirm([FromBody] VenueAutofillConfirmRequest request, CancellationToken cancellationToken)
    {
        var validation = await _confirmValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
            return BadRequest(new { errors = validation.Errors.Select(e => e.ErrorMessage) });

        var outcome = await _service.ConfirmAsync(request, cancellationToken);
        return MapOutcome(outcome);
    }

    private IActionResult MapOutcome(VenueAutofillOutcome outcome)
    {
        switch (outcome.Kind)
        {
            case VenueAutofillOutcomeKind.Success:
                return Ok(outcome.Success);
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
}
