using FluentValidation;
using VenueAutofill.Api.Contracts;
using VenueAutofill.Api.Contracts.Requests;
using VenueAutofill.Api.Infrastructure.Data;
using VenueAutofill.Api.Infrastructure.Http;

namespace VenueAutofill.Api.Validation;

public class VenueAutofillRequestValidator : AbstractValidator<VenueAutofillRequest>
{
    public VenueAutofillRequestValidator(
        UrlSafetyValidator urlSafetyValidator,
        SourceRelevanceValidator sourceRelevanceValidator,
        BookingPlatformRegistry platformRegistry)
    {
        RuleFor(x => x.VenueName).NotEmpty().WithMessage("venueName is required.");
        RuleFor(x => x.Country).NotEmpty().WithMessage("country is required.");
        RuleFor(x => x.City).NotEmpty().WithMessage("city is required.");
        RuleFor(x => x.VenueType)
            .Must(v => VenueTypeHelper.TryParse(v, out _))
            .When(x => !string.IsNullOrWhiteSpace(x.VenueType))
            .WithMessage("venueType must be one of: Hotel, Stadium, Arena, Activity Centre.");
        RuleFor(x => x.RetrievalMode)
            .Must(BeValidRetrievalMode)
            .When(x => !string.IsNullOrWhiteSpace(x.RetrievalMode))
            .WithMessage("retrievalMode must be one of: automatic, officialWebsite, googlePlaces, customSource, bookingPlatform.");
        RuleFor(x => x.Source)
            .NotEmpty()
            .When(x => RetrievalModeHelper.Parse(x.RetrievalMode) == RetrievalMode.CustomSource)
            .WithMessage("source URL is required when retrievalMode is customSource.");
        RuleFor(x => x.PlatformId)
            .NotEmpty()
            .When(x => RetrievalModeHelper.Parse(x.RetrievalMode) == RetrievalMode.BookingPlatform)
            .WithMessage("platformId is required when retrievalMode is bookingPlatform.");
        RuleFor(x => x.PlatformId)
            .Must(id => platformRegistry.GetById(id!) is not null)
            .When(x => RetrievalModeHelper.Parse(x.RetrievalMode) == RetrievalMode.BookingPlatform && !string.IsNullOrWhiteSpace(x.PlatformId))
            .WithMessage("platformId is not a supported booking platform.");
        RuleFor(x => x.Source)
            .Must(url => urlSafetyValidator.IsSafeUrl(url, out _))
            .When(x => !string.IsNullOrWhiteSpace(x.Source))
            .WithMessage("source URL is invalid or not allowed.");
        RuleFor(x => x.Source)
            .Must((req, url) => sourceRelevanceValidator.IsUrlRelevant(url!, req.VenueName, req.City, req.Country))
            .When(x => !string.IsNullOrWhiteSpace(x.Source))
            .WithMessage("source URL does not appear to match the provided venue name, city, or country.");
    }

    private static bool BeValidRetrievalMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;
        var normalized = value.Trim();
        return Enum.TryParse<RetrievalMode>(normalized, ignoreCase: true, out _)
            || normalized.Equals("officialWebsite", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("googlePlaces", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("customSource", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("bookingPlatform", StringComparison.OrdinalIgnoreCase);
    }
}

public class VenueAutofillConfirmRequestValidator : AbstractValidator<VenueAutofillConfirmRequest>
{
    public VenueAutofillConfirmRequestValidator(
        UrlSafetyValidator urlSafetyValidator,
        SourceRelevanceValidator sourceRelevanceValidator,
        BookingPlatformRegistry platformRegistry)
    {
        RuleFor(x => x.Reference).NotEmpty().WithMessage("reference is required.");
        RuleFor(x => x.OptionId).NotEmpty().WithMessage("optionId is required.");
        RuleFor(x => x.RetrievalMode)
            .Must(BeValidRetrievalMode)
            .When(x => !string.IsNullOrWhiteSpace(x.RetrievalMode))
            .WithMessage("retrievalMode must be one of: automatic, officialWebsite, googlePlaces, customSource, bookingPlatform.");
        RuleFor(x => x.Source)
            .NotEmpty()
            .When(x => RetrievalModeHelper.Parse(x.RetrievalMode) == RetrievalMode.CustomSource)
            .WithMessage("source URL is required when retrievalMode is customSource.");
        RuleFor(x => x.PlatformId)
            .NotEmpty()
            .When(x => RetrievalModeHelper.Parse(x.RetrievalMode) == RetrievalMode.BookingPlatform)
            .WithMessage("platformId is required when retrievalMode is bookingPlatform.");
        RuleFor(x => x.PlatformId)
            .Must(id => platformRegistry.GetById(id!) is not null)
            .When(x => RetrievalModeHelper.Parse(x.RetrievalMode) == RetrievalMode.BookingPlatform && !string.IsNullOrWhiteSpace(x.PlatformId))
            .WithMessage("platformId is not a supported booking platform.");
        RuleFor(x => x.Source)
            .Must(url => urlSafetyValidator.IsSafeUrl(url, out _))
            .When(x => !string.IsNullOrWhiteSpace(x.Source))
            .WithMessage("source URL is invalid or not allowed.");
    }

    private static bool BeValidRetrievalMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;
        var normalized = value.Trim();
        return Enum.TryParse<RetrievalMode>(normalized, ignoreCase: true, out _)
            || normalized.Equals("officialWebsite", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("googlePlaces", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("customSource", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("bookingPlatform", StringComparison.OrdinalIgnoreCase);
    }
}
