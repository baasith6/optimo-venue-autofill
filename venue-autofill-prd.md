Below is a **build-ready PRD** for the **Venue Autofill Module** in **ASP.NET Core 10 Web API**.

Microsoft’s current ASP.NET Core 10 documentation supports both **controller-based APIs** and **Minimal APIs**. For this project, use **controller-based APIs** because the module has multiple services, validation paths, ambiguous flows, and integration contracts. ([Microsoft Learn][1])

---

# PRD: Venue Autofill API Module

## 1. Product Name

**Venue Autofill API**

## 2. Project Type

```text
ASP.NET Core 10 Web API
```

## 3. Purpose

Build an API module that receives limited venue information from Optimo and returns enriched venue details using Google Places, controlled website extraction, AI formatting, confidence scoring, and ambiguity handling.

This module is needed because Jon’s meeting requirement was to help users populate hotel/venue information automatically instead of manually filling hundreds of records. The first business target is to improve venue presentation for the demo and later reuse the same logic inside Optimo. 

---

# 4. Main Objective

Given this input:

```json
{
  "venueName": "The Westin Bonaventure Hotel & Suites",
  "country": "United States",
  "city": "Los Angeles",
  "source": null
}
```

The API should return either:

```text
1. Standard venue autofill response
2. Ambiguous options response
3. Not found response
4. Error response
```

---

# 5. Supported Venue Types

```text
Hotel
Stadium
Arena
Activity Centre
```

Internally, use an enum:

```csharp
public enum VenueType
{
    Hotel = 1,
    Stadium = 2,
    Arena = 3,
    ActivityCentre = 4
}
```

---

# 6. Core Data Sources

## Primary source: Google Places API

Use Google Places for venue discovery and structured details.

Google Places API supports Text Search, Place Details, and Place Photos. Text Search can find places from a text query, Place Details can return more complete place information using a place ID, and Place Photos can provide place images. ([Google for Developers][2])

## Secondary source: official/source website

Use controlled website extraction for fields Google Places may not reliably provide:

```text
check-in time
check-out time
amenities/facilities
hotel description
email
better image
```

## AI usage

AI should **not** randomly search the web.

AI should only:

```text
1. Summarize extracted source text
2. Normalize amenities
3. Convert extracted content into the expected JSON shape
4. Remove irrelevant marketing/review content
```

---

# 7. High-Level Flow

```text
Optimo UI dropdown/button
        ↓
POST /api/venue-autofill
        ↓
Validate request
        ↓
Search Google Places
        ↓
Score candidates
        ↓
If ambiguous → return options
        ↓
If clear match → get Place Details
        ↓
Extract official/source website content
        ↓
AI formats/normalizes data
        ↓
Resolve LA28 zone/location
        ↓
Return standard JSON
```

---

# 8. API Endpoints

## 8.1 Primary Endpoint

```http
POST /api/venue-autofill
```

### Request Payload

```json
{
  "venueName": "The Westin Bonaventure Hotel & Suites",
  "country": "United States",
  "city": "Los Angeles",
  "area": "Downtown LA",
  "venueType": "Hotel",
  "source": null
}
```

### Request Field Rules

| Field       | Required | Type       | Notes                            |
| ----------- | -------: | ---------- | -------------------------------- |
| `venueName` |      Yes | string     | Main search term                 |
| `country`   |      Yes | string     | Country filter                   |
| `city`      |      Yes | string     | City filter                      |
| `area`      |       No | string     | Extra narrowing input            |
| `venueType` |      Yes | string     | Must match supported venue type  |
| `source`    |       No | string/url | Optional specific source website |

### Validation Rules

| Rule                                      | Response |
| ----------------------------------------- | -------- |
| Missing `venueName`                       | 400      |
| Missing `country`                         | 400      |
| Missing `city`                            | 400      |
| Invalid `venueType`                       | 400      |
| Invalid `source` URL                      | 400      |
| Source URL points to localhost/private IP | 400      |

---

## 8.2 Ambiguous Confirmation Endpoint

```http
POST /api/venue-autofill/confirm
```

### Request Payload

```json
{
  "reference": "search_20260522_0001",
  "optionId": "1"
}
```

### Purpose

Used when `/api/venue-autofill` returns multiple possible matches.

The `reference` identifies the previous search session.
The `optionId` identifies the selected venue candidate.

---

# 9. Standard Success Response

Jon’s required no-ambiguity response:

```json
{
  "name": "The Westin Bonaventure Hotel & Suites, Los Angeles",
  "venueType": "Hotel",
  "rating": 4,
  "checkInTime": "15:00:00",
  "checkOutTime": "12:00:00",
  "amenities": [
    "Free WiFi",
    "Outdoor Pool",
    "Fitness Center",
    "Restaurant",
    "Bar",
    "Room Service",
    "Business Center",
    "Conference Rooms",
    "Parking",
    "Pet Friendly"
  ],
  "description": "Iconic downtown Los Angeles hotel featuring modern accommodations, panoramic city views, multiple dining options, and extensive meeting and event facilities.",
  "image": "https://example.com/images/westin-bonaventure-los-angeles.jpg",
  "country": "United States",
  "location": "Inglewood Zone",
  "latitude": 34.0526,
  "longitude": -118.2551,
  "mapUrl": "https://maps.google.com/?q=34.0526,-118.2551",
  "email": "info@westinbonaventure.com",
  "phone": "+1-213-624-1000"
}
```

## Contract Note

The external response can be the clean object above, but internally you must still keep:

```json
{
  "status": "success",
  "confidenceScore": 94,
  "sourceUsed": "https://...",
  "warnings": []
}
```

Do not throw away confidence/warnings internally. You will need them for debugging and integration issues.

---

# 10. Ambiguous Response

When multiple candidates are found:

```json
{
  "status": "ambiguous",
  "reference": "search_20260522_0001",
  "message": "Multiple matching venues found. Please select the correct venue.",
  "options": [
    {
      "optionId": "1",
      "name": "The Westin Bonaventure Hotel & Suites",
      "venueType": "Hotel",
      "country": "United States",
      "city": "Los Angeles",
      "area": "Downtown LA",
      "address": "404 S Figueroa St, Los Angeles, CA",
      "latitude": 34.0526,
      "longitude": -118.2551,
      "source": "https://www.marriott.com/...",
      "confidenceScore": 92
    },
    {
      "optionId": "2",
      "name": "Another Similar Venue",
      "venueType": "Hotel",
      "country": "United States",
      "city": "Los Angeles",
      "area": "Hollywood",
      "address": "Example address",
      "latitude": 34.0000,
      "longitude": -118.0000,
      "source": "https://example.com",
      "confidenceScore": 81
    }
  ]
}
```

---

# 11. Not Found Response

```json
{
  "status": "not_found",
  "reference": "search_20260522_0002",
  "message": "No reliable matching venue was found.",
  "warnings": [
    "Venue name may be too generic.",
    "No official source could be verified."
  ]
}
```

---

# 12. Error Response

```json
{
  "status": "error",
  "message": "Venue autofill failed due to an external provider error.",
  "traceId": "00-abc123"
}
```

---

# 13. Confidence Score

Use a score out of 100.

| Factor                   | Weight |
| ------------------------ | -----: |
| Venue name match         |     30 |
| Location match           |     30 |
| Venue type match         |     10 |
| Source reliability       |     10 |
| Data completeness        |     10 |
| Cross-source consistency |     10 |

## Confidence Thresholds

|  Score | Result                               |
| -----: | ------------------------------------ |
| 90–100 | Success                              |
|  75–89 | Success, but keep warning internally |
|  50–74 | Ambiguous                            |
|   0–49 | Not found                            |

## Ambiguity Rule

```text
If top candidate score - second candidate score < 10,
return ambiguous.
```

This prevents wrong venue selection when two results are close.

---

# 14. Source Priority

| Priority | Source                                     | Usage                                              |
| -------: | ------------------------------------------ | -------------------------------------------------- |
|        1 | User-provided `source`                     | When Optimo user selects/provides a source         |
|        2 | Official website from Google Place Details | Best source for verified venue content             |
|        3 | Google Places data                         | Best for identity, map, coordinates, phone, rating |
|        4 | Trusted listing source                     | Backup for hotels                                  |
|        5 | General web page                           | Last resort only                                   |

Google Places requires field masks for Place Details/Text Search responses, so the implementation should request only needed fields to control cost and response size. ([Google for Developers][3])

---

# 15. Optimo Dropdown/Button Requirement

Jon said the Optimo UI button will behave like a dropdown on the side, with an option to retrieve from a specific source.

Recommended dropdown actions:

```text
Retrieve automatically
Retrieve from official website
Retrieve from Google/Maps
Retrieve from specific source
```

## API Impact

The API must support:

```json
{
  "source": null
}
```

and:

```json
{
  "source": "https://specific-source.com/venue-page"
}
```

Hard rule:

```text
Even if source is provided, validate it against venueName + country + city.
```

---

# 16. LA28 Zone / Location Handling

Jon’s sample uses:

```json
"location": "Inglewood Zone"
```

So for this project, treat `location` as the **Olympic zone/location label**, not the full street address.

## First Version

Use a static JSON file:

```json
[
  {
    "zoneName": "Inglewood Zone",
    "keywords": ["Inglewood", "Hollywood Park", "SoFi Stadium", "Intuit Dome"]
  },
  {
    "zoneName": "DTLA Zone",
    "keywords": ["Downtown Los Angeles", "Crypto.com Arena", "Los Angeles Convention Center"]
  },
  {
    "zoneName": "Carson Zone",
    "keywords": ["Carson", "Dignity Health Sports Park"]
  }
]
```

Later, replace with coordinate polygon matching if needed.

---

# 17. Field Mapping

## From Google Places

| Output Field         | Source                          |
| -------------------- | ------------------------------- |
| `name`               | Place Details                   |
| `rating`             | Place Details                   |
| `country`            | Address components              |
| `latitude`           | Geometry/location               |
| `longitude`          | Geometry/location               |
| `mapUrl`             | Google Maps URI / generated URL |
| `phone`              | Place Details                   |
| `image`              | Place Photos                    |
| `website` internally | Place Details                   |

## From Website Extraction

| Output Field   | Source                              |
| -------------- | ----------------------------------- |
| `checkInTime`  | Official/source website             |
| `checkOutTime` | Official/source website             |
| `amenities`    | Official/source website             |
| `description`  | Extracted text + AI summary         |
| `email`        | Website/contact page if available   |
| Better `image` | Website if better than Google photo |

---

# 18. ASP.NET Core Project Architecture

```text
VenueAutofill.Api
│
├── Controllers
│   └── VenueAutofillController.cs
│
├── Application
│   ├── Interfaces
│   │   ├── IVenueAutofillService.cs
│   │   ├── IVenueDiscoveryService.cs
│   │   ├── IVenueDetailsService.cs
│   │   ├── IVenueExtractionService.cs
│   │   ├── IVenueConfidenceService.cs
│   │   ├── IAiVenueFormatterService.cs
│   │   ├── IZoneResolverService.cs
│   │   └── IAmbiguousSearchCache.cs
│   │
│   └── Services
│       ├── VenueAutofillService.cs
│       ├── VenueDiscoveryService.cs
│       ├── VenueDetailsService.cs
│       ├── VenueExtractionService.cs
│       ├── VenueConfidenceService.cs
│       ├── AiVenueFormatterService.cs
│       ├── ZoneResolverService.cs
│       └── AmbiguousSearchCache.cs
│
├── Infrastructure
│   ├── Providers
│   │   ├── GooglePlacesProvider.cs
│   │   ├── WebsiteExtractionProvider.cs
│   │   └── AiProvider.cs
│   │
│   ├── Http
│   │   ├── SafeHttpClientService.cs
│   │   └── UrlSafetyValidator.cs
│   │
│   └── Caching
│       └── MemoryAmbiguousSearchCache.cs
│
├── Contracts
│   ├── Requests
│   │   ├── VenueAutofillRequest.cs
│   │   └── VenueAutofillConfirmRequest.cs
│   │
│   ├── Responses
│   │   ├── VenueAutofillStandardResponse.cs
│   │   ├── VenueAmbiguousResponse.cs
│   │   ├── VenueOptionResponse.cs
│   │   ├── NotFoundResponse.cs
│   │   └── ErrorResponse.cs
│   │
│   └── Internal
│       ├── VenueCandidate.cs
│       ├── VenueExtractedData.cs
│       ├── ConfidenceBreakdown.cs
│       └── ProviderResult.cs
│
├── Data
│   ├── la28-zones.json
│   └── amenities-map.json
│
├── Configuration
│   ├── GooglePlacesOptions.cs
│   ├── AiOptions.cs
│   └── VenueAutofillOptions.cs
│
└── Program.cs
```

---

# 19. Service Responsibilities

## `VenueAutofillService`

Main orchestrator.

Responsibilities:

```text
Validate business flow
Call discovery
Call confidence scoring
Return ambiguous/not_found/success
Call extraction and formatter
```

---

## `GooglePlacesProvider`

Responsibilities:

```text
Call Text Search
Call Place Details
Call Place Photos
Map Google response to VenueCandidate
```

Use Text Search first because it can find places using free-text venue queries, then Place Details with the returned place ID for richer structured data. ([Google for Developers][4])

---

## `VenueConfidenceService`

Responsibilities:

```text
Score candidate against input
Detect ambiguity
Apply hard caps for wrong country/city/type
Return confidence breakdown
```

---

## `VenueExtractionService`

Responsibilities:

```text
Fetch official/source page
Extract clean visible text
Extract image candidates
Extract check-in/check-out
Extract amenities
Extract email/phone if available
```

Rules:

```text
No unlimited crawling
No scraping Google search pages
No private/internal URL fetching
No copying long descriptions directly
```

---

## `AiVenueFormatterService`

Responsibilities:

```text
Summarize extracted description
Normalize amenities
Remove irrelevant review/site-specific text
Return structured data
```

Example AI instruction:

```text
Convert the extracted venue text into the required JSON fields.
Use only the provided source text.
Do not invent missing data.
Return empty string/null where data is unavailable.
Keep description under 35 words.
```

---

## `ZoneResolverService`

Responsibilities:

```text
Resolve location/zone from area, address, or coordinates
Return LA28 zone label
```

First version:

```text
Static keyword-based resolver
```

Later version:

```text
Geo polygon-based resolver
```

---

## `AmbiguousSearchCache`

Responsibilities:

```text
Store candidates by reference
Expire records after 30–60 minutes
Resolve selected optionId
```

For POC:

```text
IMemoryCache
```

For production:

```text
Redis or database-backed cache
```

---

# 20. Required DTOs

## `VenueAutofillRequest`

```csharp
public class VenueAutofillRequest
{
    public string VenueName { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string? Area { get; set; }
    public string VenueType { get; set; } = string.Empty;
    public string? Source { get; set; }
}
```

## `VenueAutofillConfirmRequest`

```csharp
public class VenueAutofillConfirmRequest
{
    public string Reference { get; set; } = string.Empty;
    public string OptionId { get; set; } = string.Empty;
}
```

## `VenueAutofillStandardResponse`

```csharp
public class VenueAutofillStandardResponse
{
    public string Name { get; set; } = string.Empty;
    public string VenueType { get; set; } = string.Empty;
    public int Rating { get; set; }
    public string? CheckInTime { get; set; }
    public string? CheckOutTime { get; set; }
    public List<string> Amenities { get; set; } = new();
    public string Description { get; set; } = string.Empty;
    public string Image { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string MapUrl { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
}
```

---

# 21. Configuration

## `appsettings.json`

```json
{
  "GooglePlaces": {
    "ApiKey": "",
    "BaseUrl": "https://places.googleapis.com/v1",
    "TextSearchEndpoint": "/places:searchText",
    "PlaceDetailsEndpoint": "/places/{placeId}",
    "PhotoEndpoint": "/{photoName}/media"
  },
  "VenueAutofill": {
    "AmbiguousCacheMinutes": 60,
    "MinimumSuccessConfidence": 75,
    "MinimumNotFoundConfidence": 50,
    "AmbiguousScoreGap": 10,
    "MaxExtractionPages": 3,
    "DescriptionMaxWords": 35
  },
  "AI": {
    "Provider": "OpenAI",
    "Model": "gpt-4.1-mini"
  }
}
```

Use secrets/environment variables for API keys, not committed config.

---

# 22. Security Requirements

## URL safety

Because `source` is user-provided, protect against SSRF.

Block:

```text
localhost
127.0.0.1
0.0.0.0
private IP ranges
file://
ftp://
internal domains
metadata endpoints
```

Allow only:

```text
http
https
```

## API Security

Required:

```text
API key or bearer token between Optimo and this service
Rate limiting
Request size limits
Timeouts for all external calls
Structured logging
No secrets in logs
```

---

# 23. Logging Requirements

Log these:

```text
request reference
venue name
city/country
venue type
source mode
candidate count
top confidence score
selected option
source used
warnings
external provider failures
```

Do not log:

```text
API keys
full AI prompts with secrets
sensitive headers
```

---

# 24. Acceptance Criteria

## Primary endpoint

* Accepts valid venue autofill request.
* Rejects invalid requests with 400.
* Calls Google Places Text Search.
* Returns ambiguous response when multiple close candidates exist.
* Returns standard response when one strong candidate exists.
* Returns not_found when no reliable candidate exists.

## Confirm endpoint

* Accepts `reference` and `optionId`.
* Finds cached candidate.
* Extracts and formats final venue data.
* Returns standard response.
* Returns 404/expired when reference is missing or expired.

## Data quality

* Description is short and not copied directly.
* Amenities are normalized.
* Location returns LA28-style zone label where possible.
* Check-in/check-out are only populated for hotels.
* Non-hotel venue types return empty/null hotel-only fields.

## Security

* Invalid source URLs are blocked.
* Private/internal URLs are blocked.
* External calls have timeout.
* API keys are stored securely.

---

# 25. MVP Development Order

Build in this order:

```text
1. Create ASP.NET Core 10 Web API project
2. Add controllers and DTOs
3. Add mock success/ambiguous/not_found responses
4. Add request validation
5. Add Google Places Text Search
6. Add Google Place Details
7. Add confidence scoring
8. Add ambiguous cache
9. Add confirm endpoint
10. Add static LA28 zone resolver
11. Add website extraction
12. Add AI formatter
13. Add logging/security/rate limiting
14. Add Swagger/Postman examples
```

---

# 26. Deliverables

```text
ASP.NET Core 10 Web API source code
Swagger documentation
Postman collection
Sample request/response payloads
README setup guide
Environment variable guide
Google Places configuration guide
AI provider configuration guide
LA28 zones static JSON
Amenities normalization JSON
```

---

# 27. Immediate Build Decision

Use:

```text
ASP.NET Core 10 Web API
Controller-based API
Service-based architecture
Google Places as discovery/details provider
Website extraction as secondary enrichment
AI only for formatting/summarization
IMemoryCache for POC ambiguous selection
Static JSON for LA28 zones
```

Do **not** build a UI now.
Do **not** start with scraping.
Do **not** let AI decide the venue identity by itself.

The module’s first job is reliability, not cleverness.
