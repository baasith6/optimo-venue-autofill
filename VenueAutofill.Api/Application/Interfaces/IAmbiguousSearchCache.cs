using VenueAutofill.Api.Contracts.Internal;
using VenueAutofill.Api.Contracts.Requests;

namespace VenueAutofill.Api.Application.Interfaces;

public interface IAmbiguousSearchCache
{
    string Store(VenueAutofillRequest request, IReadOnlyList<VenueCandidate> candidates);
    AmbiguousSearchSession? Get(string reference);
    VenueCandidate? GetCandidate(string reference, string optionId);
}
