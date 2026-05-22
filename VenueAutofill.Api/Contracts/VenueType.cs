using VenueAutofill.Api.Contracts.Internal;
using VenueAutofill.Api.Contracts.Requests;

namespace VenueAutofill.Api.Contracts;

public enum VenueType
{
    Hotel = 1,
    Stadium = 2,
    Arena = 3,
    ActivityCentre = 4
}

public static class VenueTypeHelper
{
    private static readonly Dictionary<string, VenueType> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Hotel"] = VenueType.Hotel,
        ["Stadium"] = VenueType.Stadium,
        ["Arena"] = VenueType.Arena,
        ["Activity Centre"] = VenueType.ActivityCentre,
        ["ActivityCentre"] = VenueType.ActivityCentre
    };

    private static readonly Dictionary<string, VenueType> GoogleTypeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["lodging"] = VenueType.Hotel,
        ["hotel"] = VenueType.Hotel,
        ["motel"] = VenueType.Hotel,
        ["resort"] = VenueType.Hotel,
        ["stadium"] = VenueType.Stadium,
        ["arena"] = VenueType.Arena,
        ["gym"] = VenueType.ActivityCentre,
        ["sports_complex"] = VenueType.ActivityCentre,
        ["sports_club"] = VenueType.ActivityCentre
    };

    public static bool TryParse(string? value, out VenueType venueType)
    {
        venueType = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;
        return Map.TryGetValue(value.Trim(), out venueType);
    }

    public static bool TryInfer(string? venueName, IReadOnlyList<string>? googleTypes, out VenueType venueType)
    {
        venueType = default;

        if (googleTypes is not null)
        {
            foreach (var googleType in googleTypes)
            {
                if (GoogleTypeMap.TryGetValue(googleType, out venueType))
                    return true;
            }
        }

        if (string.IsNullOrWhiteSpace(venueName))
            return false;

        var name = venueName.ToLowerInvariant();
        if (name.Contains("hotel", StringComparison.OrdinalIgnoreCase) || name.Contains("inn", StringComparison.OrdinalIgnoreCase))
        {
            venueType = VenueType.Hotel;
            return true;
        }

        if (name.Contains("stadium", StringComparison.OrdinalIgnoreCase))
        {
            venueType = VenueType.Stadium;
            return true;
        }

        if (name.Contains("arena", StringComparison.OrdinalIgnoreCase))
        {
            venueType = VenueType.Arena;
            return true;
        }

        if (name.Contains("activity centre", StringComparison.OrdinalIgnoreCase)
            || name.Contains("activity center", StringComparison.OrdinalIgnoreCase)
            || name.Contains("sports centre", StringComparison.OrdinalIgnoreCase)
            || name.Contains("sports center", StringComparison.OrdinalIgnoreCase))
        {
            venueType = VenueType.ActivityCentre;
            return true;
        }

        return false;
    }

    public static void ApplyInferredType(VenueCandidate candidate, VenueAutofillRequest request)
    {
        if (TryParse(request.VenueType, out var explicitType))
        {
            candidate.VenueType = ToDisplayName(explicitType);
            return;
        }

        if (TryInfer(candidate.Name, candidate.GoogleTypes, out var inferred))
            candidate.VenueType = ToDisplayName(inferred);
        else
            candidate.VenueType = string.Empty;
    }

    public static bool TryResolveEffectiveType(
        VenueAutofillRequest request,
        VenueCandidate candidate,
        out VenueType venueType,
        out string? displayName)
    {
        if (TryParse(request.VenueType, out venueType))
        {
            displayName = ToDisplayName(venueType);
            return true;
        }

        if (TryParse(candidate.VenueType, out venueType))
        {
            displayName = candidate.VenueType;
            return true;
        }

        if (TryInfer(candidate.Name, candidate.GoogleTypes, out venueType))
        {
            displayName = ToDisplayName(venueType);
            candidate.VenueType = displayName;
            return true;
        }

        displayName = null;
        return false;
    }

    public static string? ToDisplayNameOrNull(string? venueTypeDisplay) =>
        string.IsNullOrWhiteSpace(venueTypeDisplay) ? null : venueTypeDisplay;

    public static string ToDisplayName(VenueType venueType) => venueType switch
    {
        VenueType.Hotel => "Hotel",
        VenueType.Stadium => "Stadium",
        VenueType.Arena => "Arena",
        VenueType.ActivityCentre => "Activity Centre",
        _ => venueType.ToString()
    };
}
