using FluentValidation;
using VenueAutofill.Api.Contracts;
using VenueAutofill.Api.Contracts.Requests;
using VenueAutofill.Api.Infrastructure.Http;

namespace VenueAutofill.Api.Validation;

public class VenueAutofillRequestValidator : AbstractValidator<VenueAutofillRequest>
{
    public VenueAutofillRequestValidator(
        UrlSafetyValidator urlSafetyValidator,
        SourceRelevanceValidator sourceRelevanceValidator)
    {
        RuleFor(x => x.VenueName).NotEmpty().WithMessage("venueName is required.");
        RuleFor(x => x.Country).NotEmpty().WithMessage("country is required.");
        RuleFor(x => x.City).NotEmpty().WithMessage("city is required.");
        RuleFor(x => x.VenueType)
            .Must(v => VenueTypeHelper.TryParse(v, out _))
            .When(x => !string.IsNullOrWhiteSpace(x.VenueType))
            .WithMessage("venueType must be one of: Hotel, Stadium, Arena, Activity Centre.");
        RuleFor(x => x.Source)
            .Must(url => urlSafetyValidator.IsSafeUrl(url, out _))
            .When(x => !string.IsNullOrWhiteSpace(x.Source))
            .WithMessage("source URL is invalid or not allowed.");
        RuleFor(x => x.Source)
            .Must((req, url) => sourceRelevanceValidator.IsUrlRelevant(url!, req.VenueName, req.City, req.Country))
            .When(x => !string.IsNullOrWhiteSpace(x.Source))
            .WithMessage("source URL does not appear to match the provided venue name, city, or country.");
    }
}

public class VenueAutofillConfirmRequestValidator : AbstractValidator<VenueAutofillConfirmRequest>
{
    public VenueAutofillConfirmRequestValidator()
    {
        RuleFor(x => x.Reference).NotEmpty().WithMessage("reference is required.");
        RuleFor(x => x.OptionId).NotEmpty().WithMessage("optionId is required.");
    }
}
