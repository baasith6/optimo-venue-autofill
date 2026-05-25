# Venue Autofill API

ASP.NET Core Web API that enriches limited venue input from Optimo using Google Places, multi-platform cross-checking (booking/travel sites), controlled website extraction, and OpenRouter AI formatting.

## Prerequisites

- .NET 9 SDK
- Google Places API (New) key
- Google Programmable Search (Custom Search JSON API) key + Search Engine ID (`cx`) for booking-platform discovery
- OpenRouter API key (optional for AI formatting; falls back to extracted text)
- Azure Storage account (or [Azurite](https://learn.microsoft.com/azure/storage/common/storage-use-azurite) for local dev) for normalized venue images

## Quick start

If build fails with **"file is locked by VenueAutofill.Api"**, stop the old process first:

```powershell
.\scripts\stop-api.ps1
# or: taskkill /F /IM VenueAutofill.Api.exe
```

```bash
cd VenueAutofill.Api
dotnet restore
dotnet user-secrets init
dotnet user-secrets set "GooglePlaces:ApiKey" "YOUR_GOOGLE_KEY"
dotnet user-secrets set "GoogleCustomSearch:ApiKey" "YOUR_GOOGLE_KEY"
dotnet user-secrets set "GoogleCustomSearch:SearchEngineId" "YOUR_CX_ID"
dotnet user-secrets set "AI:ApiKey" "YOUR_OPENROUTER_KEY"
dotnet user-secrets set "AzureBlobStorage:ConnectionString" "YOUR_CONNECTION_STRING"
dotnet user-secrets set "AzureBlobStorage:ContainerName" "venue-images"
dotnet user-secrets set "VenueAutofill:UseMocks" "false"
dotnet run
```

**Local blob (Azurite):**

```powershell
dotnet user-secrets set "AzureBlobStorage:ConnectionString" "UseDevelopmentStorage=true"
```

Ensure Azurite is running (`azurite` or Visual Studio Storage Emulator). The container is created with public blob read access for stable `image` URLs.

**Why mock/fallback response?** If you see `example.com` images and the exact Westin sample JSON, `UseMocks` is still `true`. Development mode reads [`appsettings.Development.json`](VenueAutofill.Api/appsettings.Development.json) — it must have `"UseMocks": false`. You also need a Google Places API key or discovery returns nothing.

**502 / external provider error?** The API now returns the Google error message in `message`. Common fixes:

1. Set `GooglePlaces:ApiKey` in user-secrets (not empty in `appsettings.json`).
2. In [Google Cloud Console](https://console.cloud.google.com/), enable **Places API (New)** (not only the legacy Places API).
3. Ensure billing is enabled on the project.
4. Restrict the API key to Places API (New) if using key restrictions.

**504 timeout / cannot connect to places.googleapis.com?** The API machine must reach Google over HTTPS. From PowerShell, test:

```powershell
curl.exe -m 15 -X POST "https://places.googleapis.com/v1/places:searchText" `
  -H "Content-Type: application/json" `
  -H "X-Goog-Api-Key: YOUR_KEY" `
  -H "X-Goog-FieldMask: places.id" `
  -d "{\"textQuery\":\"hotel los angeles\"}"
```

If `curl.exe` works but the API times out, set system proxy env vars (`HTTP_PROXY` / `HTTPS_PROXY`) or run the API where outbound HTTPS is allowed (VPN, different network).

Swagger UI: `https://localhost:7xxx/swagger` (see launchSettings.json for port).

## Configuration

| Setting | Description |
|---------|-------------|
| `VenueAutofill:UseMocks` | `true` = PRD sample responses without external APIs |
| `GooglePlaces:ApiKey` | Google Places API key |
| `AI:ApiKey` | OpenRouter API key |
| `AI:Model` | OpenRouter model id (default `openai/gpt-4.1-mini`) |
| `VenueAutofill:RequireApiKey` | Enable `X-Api-Key` header check |
| `VenueAutofill:ApiKey` | Expected API key when auth enabled |
| `GoogleCustomSearch:ApiKey` | Google Custom Search API key |
| `GoogleCustomSearch:SearchEngineId` | Programmable Search Engine ID (`cx`) |
| `VenueAutofill:EnablePlatformCrossCheck` | `false` skips CSE + booking probes (Google + official site only) |
| `VenueAutofill:MaxPlatformDiscoveryCount` | Max booking platforms queried per automatic request (default 8) |
| `ImageNormalization:TargetWidth` / `TargetHeight` | Output image size (default **1200×800**, ratio **3:2**) |
| `AzureBlobStorage:ConnectionString` | Storage account connection string (or `UseDevelopmentStorage=true`) |
| `AzureBlobStorage:ContainerName` | Blob container (default `venue-images`) |
| `AzureBlobStorage:PublicBaseUrl` | Optional CDN base URL for `image` field |
| `AzureBlobStorage:UseManagedIdentity` | Use managed identity + `AccountName` in Azure |
| `VenueAutofill:MinImageConfidenceScore` | Minimum score to accept a single-source image (default 70) |
| `VenueAutofill:RequireCrossSourceImageAgreement` | If `true`, reject images not verified across 2+ sources |
| `BrowserFetch:Enabled` | Enable HTML fetching for official/booking pages |
| `BrowserFetch:UsePlaywrightFallback` | Use headless Chromium when HTTP is blocked |
| `BrowserFetch:PlaywrightTimeoutSeconds` | Playwright page load timeout (default 25) |

Environment variable override: `GooglePlaces__ApiKey`, `GoogleCustomSearch__ApiKey`, `AI__ApiKey`, `AzureBlobStorage__ConnectionString`, `VenueAutofill__UseMocks`.

### Venue images (3:2, Azure Blob)

After a source image is selected, the API **center-crops to 3:2**, resizes to **1200×800**, uploads JPEG to blob storage, and sets JSON `image` to the **stable blob HTTPS URL** (not the transient Google/OG URL).

If blob storage is not configured, autofill still succeeds with a warning and the original source URL.

With `?includeConfidence=true`: `imageOriginalUrl`, `imageWidth`, `imageHeight`, `imageAspectRatio` (`3:2`), `imageVerified`, and `imageCandidateDetails[]` (url, sourceId, score, crossSourceAgreed).

Images are chosen by **cross-source scoring**, not Google-only priority: official site and matched booking listings are preferred; when only Google has a photo, `imageVerified` is `false` and a warning is returned.

### Canonical amenities

Response `amenities` uses only labels from [`Data/amenities-canonical.json`](VenueAutofill.Api/Data/amenities-canonical.json). Near matches are supported (e.g. `"Free parking"` → `"Parking"`). Amenities are not invented if the source text does not support them.

### Official `location` (LA28 zones)

The `location` field is **only** one of Optimo’s 24 official zone names from [`Data/la28-zones.json`](VenueAutofill.Api/Data/la28-zones.json)—never a city, neighborhood, or AI-inferred place name.

| Official zone names |
|---------------------|
| DTLA Zone, Exposition Park Zone, Port of Los Angeles Zone, Riviera Zone, Universal City Zone, Valley Zone, Venice Zone, Carson Zone, Inglewood Zone, Long Beach Zone, Pasadena Zone, Anaheim Zone, Arcadia Zone, City of Industry Zone, Pomona Zone, Trestles Beach Zone, Whittier Narrows Zone, OKC Zone, New York Zone, Columbus Zone, Nashville Zone, St. Louis Zone, San José Zone, San Diego Zone |

Resolution uses map coordinates (where bounds are defined) and internal keyword aliases (e.g. `Downtown LA` → `DTLA Zone`, `NYC` → `New York Zone`). If `request.area` is already an exact official zone name, it is used.

If no confident match: `"location": ""` and a warning (`Location could not be mapped to an official LA28 zone.`). The rest of the venue payload is still returned. Venues outside these zones (e.g. international cities) will have an empty `location` until aliases/bounds are extended.

### Custom Search setup

1. Create a [Programmable Search Engine](https://programmablesearchengine.google.com/) (search the entire web).
2. Enable **Custom Search API** in Google Cloud Console for the same project as Places (separate from Places API (New)).
3. Set `GoogleCustomSearch:SearchEngineId` to your `cx` value.
4. On your API key under **API restrictions**, include both **Places API (New)** and **Custom Search API**.

#### Verify Custom Search (fix `cse_forbidden` / `cse_error`)

`cse_forbidden` means HTTP **403** from Google — the search index is fine; the **JSON API** is blocked or not enabled.

```powershell
cd VenueAutofill.Api
# Use the same key/cx as in user-secrets (one line each, no line breaks)
$key = "YOUR_GOOGLE_API_KEY"
$cx = "YOUR_SEARCH_ENGINE_ID"
$q = [uri]::EscapeDataString('"AC Hotel" "New York" site:booking.com')
curl.exe "https://www.googleapis.com/customsearch/v1?key=$key&cx=$cx&q=$q&num=3"
```

| Response | Fix |
|----------|-----|
| JSON with `"items"` | CSE works |
| `403` + `PERMISSION_DENIED` / method blocked | Enable **Custom Search API**; add it to key API restrictions |
| `403` + `accessNotConfigured` | Enable Custom Search API on the GCP project |
| `200` without `items` | API works; no results for that query (`no_listing_found`) |

`skipReason` values: `cse_forbidden`, `cse_quota_exceeded`, `cse_bad_request`, `cse_not_configured`, `no_listing_found`, `cse_error`.

Listing probes fetch public HTML metadata (`og:image`, JSON-LD, check-in/out times). When simple HTTP is blocked, **Playwright** (headless Chromium) is used as a fallback. `page_blocked_bot_protection` means Playwright was also blocked (common on marriott.com, hilton.com).

### Playwright setup (first time)

After `dotnet restore`, install the Chromium browser:

```powershell
cd VenueAutofill.Api
dotnet build
.\bin\Debug\net9.0\playwright.ps1 install chromium
```

On Windows PowerShell use `.\bin\Debug\...` (not `pwsh` — that requires PowerShell 7). If the script is missing, run `dotnet build` first.

Set `BrowserFetch:UsePlaywrightFallback` to `false` to disable browser fallback (HTTP only).

**Never commit API keys** in `appsettings.json`. Use user-secrets or environment variables only. When setting secrets in PowerShell, keep each value on **one line** (no line breaks inside quotes).

## Data pipeline (live mode)

1. Google Places Text Search → candidates
2. Confidence scoring → success / ambiguous / not_found
3. Google Place Details + Photos
4. **Cross-check** — Custom Search finds listing URLs per platform (`Data/booking-platforms.json`); lightweight probes extract name/location/image signals
5. Website extraction (mode-dependent: official site, platform listing, or user `source`)
6. **Image resolver** — scores candidates from all sources; prefers URLs agreed by 2+ sources or high-trust official/booking images
7. **Image normalization** — crop 3:2, resize 1200×800, upload to Azure Blob → stable `image` URL
8. OpenRouter AI — description + canonical amenity mapping (skipped for `googlePlaces` mode)
9. LA28 zone resolver — coordinate bounds + keywords (`Data/la28-zones.json`)

Hotel check-in/out are merged from website crawl, listing probes (JSON-LD), and optional AI inference when text is available. Amenities come from extraction + AI canonical mapping (no default times invented).

User-provided `source` URLs are validated for SSRF safety and relevance to venue name, city, and country.

## Optimo Apply dropdown (`retrievalMode`)

| UI label | `retrievalMode` | Other fields |
|----------|-----------------|--------------|
| Retrieve automatically | `automatic` | — |
| Official website | `officialWebsite` | — |
| Google / Maps | `googlePlaces` | — |
| Booking.com (etc.) | `bookingPlatform` | `platformId`: e.g. `booking.com` |
| Specific URL | `customSource` | `source`: required URL |

Default when omitted: `automatic`. Optimo minimal input does not send `source`; cross-check runs automatically when enabled.

## Confidence in JSON

Append `?includeConfidence=true` to autofill or confirm. The response keeps flat venue fields and adds:

- `confidenceScore`, `confidenceBreakdown`
- `sourceUsed`, `sourcesChecked[]` (includes `skipReason` when skipped: `no_listing_found`, `cse_forbidden`, `page_blocked_bot_protection`, etc.)
- `imageSource`, `imageVerified`, `imageCandidates` (URLs), `imageCandidateDetails[]` (scored candidates), `warnings`

Without the query flag, the response matches the original flat contract (no confidence fields).

**Regression test (Hilton):** `POST /api/venue-autofill?includeConfidence=true` with a DoubleTree Times Square hotel name — expect `official_website` not `blocked` after Playwright, `imageCandidateDetails` with multiple sources when CSE finds listings, and `checkInTime` / `checkOutTime` when schema or policy pages expose them.

## Endpoints

- `POST /api/venue-autofill?includeConfidence=true` — search and enrich (or ambiguous / not_found)
- `POST /api/venue-autofill/confirm?includeConfidence=true` — confirm ambiguous selection
- `GET /health` — health check

## Mock mode testing

With `UseMocks: true`:

- Normal request → Westin Bonaventure success sample
- `venueName` contains `ambiguous` → ambiguous response
- `venueName` contains `notfound` → not_found response

## Production checklist

1. Set `VenueAutofill:UseMocks` to `false`
2. Configure Google Places, Custom Search, and OpenRouter keys via secrets
3. Configure `AzureBlobStorage` (connection string or managed identity); enable public blob read on `venue-images` or set `PublicBaseUrl` to CDN
4. Run Playwright Chromium install on the host (see **Playwright setup** above)
5. Set `VenueAutofill:RequireApiKey` to `true` and set `VenueAutofill:ApiKey`
6. Do not expose Swagger publicly without protection

## Postman

Import [`postman/VenueAutofill.postman_collection.json`](postman/VenueAutofill.postman_collection.json).
