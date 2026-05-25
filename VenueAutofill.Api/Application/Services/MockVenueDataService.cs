using VenueAutofill.Api.Contracts;
using VenueAutofill.Api.Contracts.Internal;
using VenueAutofill.Api.Contracts.Requests;
using VenueAutofill.Api.Contracts.Responses;

namespace VenueAutofill.Api.Application.Services;

public static class MockVenueDataService
{
    public static VenueAutofillOutcome CreateSuccessOutcome()
    {
        return new VenueAutofillOutcome
        {
            Kind = VenueAutofillOutcomeKind.Success,
            Success = new VenueAutofillStandardResponse
            {
                Name = "The Westin Bonaventure Hotel & Suites, Los Angeles",
                VenueType = "Hotel",
                Rating = 4,
                CheckInTime = "15:00:00",
                CheckOutTime = "12:00:00",
                Amenities =
                [
                    "Free high-speed internet", "Swimming pool", "Gym/fitness center",
                    "Cocktail lounge", "Room service", "Meeting event space", "Parking", "Pet friendly"
                ],
                Description = "Iconic downtown Los Angeles hotel featuring modern accommodations, panoramic city views, multiple dining options, and extensive meeting and event facilities.",
                Image = "https://example.com/images/westin-bonaventure-los-angeles.jpg",
                Country = "United States",
                Location = "DTLA Zone",
                Latitude = 34.0526,
                Longitude = -118.2551,
                MapUrl = "https://maps.google.com/?q=34.0526,-118.2551",
                Email = "info@westinbonaventure.com",
                Phone = "+1-213-624-1000"
            },
            ConfidenceScore = 94,
            ConfidenceBreakdown = new ConfidenceBreakdownResponse
            {
                NameMatch = 28,
                LocationMatch = 22,
                VenueTypeMatch = 8,
                SourceReliability = 9,
                DataCompleteness = 8,
                CrossSourceConsistency = 13
            },
            SourceUsed = "https://www.marriott.com/en-us/hotels/laxwb-the-westin-bonaventure-hotel-and-suites/overview/",
            ImageSource = new ImageSourceInfo
            {
                SourceId = "official_website",
                Label = "Official website",
                Url = "https://example.com/images/westin-bonaventure-los-angeles.jpg"
            },
            SourcesChecked =
            [
                new SourceCheckResult
                {
                    SourceId = "google_places",
                    Label = "Google Places",
                    Status = "matched",
                    Score = 94,
                    MatchedFields = ["name", "city", "country"]
                },
                new SourceCheckResult
                {
                    SourceId = "booking.com",
                    Label = "Booking.com",
                    Url = "https://www.booking.com/hotel/example",
                    Status = "matched",
                    Score = 82,
                    MatchedFields = ["name", "city"]
                }
            ],
            Warnings = []
        };
    }

    public static VenueAutofillOutcome CreateAmbiguousOutcome()
    {
        return new VenueAutofillOutcome
        {
            Kind = VenueAutofillOutcomeKind.Ambiguous,
            Ambiguous = new VenueAmbiguousResponse
            {
                Reference = "search_20260522_0001",
                Options =
                [
                    new VenueOptionResponse
                    {
                        OptionId = "1",
                        Name = "The Westin Bonaventure Hotel & Suites",
                        VenueType = "Hotel",
                        Country = "United States",
                        City = "Los Angeles",
                        Area = "Downtown LA",
                        Address = "404 S Figueroa St, Los Angeles, CA",
                        Latitude = 34.0526,
                        Longitude = -118.2551,
                        Source = "https://www.marriott.com/",
                        ConfidenceScore = 92
                    },
                    new VenueOptionResponse
                    {
                        OptionId = "2",
                        Name = "Another Similar Venue",
                        VenueType = "Hotel",
                        Country = "United States",
                        City = "Los Angeles",
                        Area = "Hollywood",
                        Address = "Example address",
                        Latitude = 34.0,
                        Longitude = -118.0,
                        Source = "https://example.com",
                        ConfidenceScore = 81
                    }
                ]
            }
        };
    }

    public static VenueAutofillOutcome CreateNotFoundOutcome()
    {
        return new VenueAutofillOutcome
        {
            Kind = VenueAutofillOutcomeKind.NotFound,
            NotFound = new NotFoundResponse
            {
                Reference = "search_20260522_0002",
                Warnings =
                [
                    "Venue name may be too generic.",
                    "No official source could be verified."
                ]
            }
        };
    }

    public static VenueAutofillOutcome ResolveMockConfirm(VenueAutofillConfirmRequest request)
    {
        if (request.Reference.Contains("0002", StringComparison.Ordinal))
        {
            return new VenueAutofillOutcome
            {
                Kind = VenueAutofillOutcomeKind.Error,
                StatusCode = 404,
                Error = new ErrorResponse { Message = "Search reference not found or expired." }
            };
        }

        return CreateSuccessOutcome();
    }

    public static VenueAutofillOutcome ResolveMockAutofill(VenueAutofillRequest request)
    {
        if (request.VenueName.Contains("ambiguous", StringComparison.OrdinalIgnoreCase))
            return CreateAmbiguousOutcome();
        if (request.VenueName.Contains("notfound", StringComparison.OrdinalIgnoreCase))
            return CreateNotFoundOutcome();
        return CreateSuccessOutcome();
    }
}
