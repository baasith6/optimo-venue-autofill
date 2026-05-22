using VenueAutofill.Api.Contracts.Internal;
using VenueAutofill.Api.Contracts.Requests;

namespace VenueAutofill.Api.Application.Interfaces;

public interface IVenueAutofillService
{
    Task<VenueAutofillOutcome> AutofillAsync(VenueAutofillRequest request, CancellationToken cancellationToken = default);
    Task<VenueAutofillOutcome> ConfirmAsync(VenueAutofillConfirmRequest request, CancellationToken cancellationToken = default);
}
