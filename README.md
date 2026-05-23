# Venue Autofill API

ASP.NET Core Web API that enriches limited venue input from Optimo using Google Places, multi-platform cross-checking (booking/travel sites), controlled website extraction, and OpenRouter AI formatting.

## Prerequisites

- .NET 9 SDK
- Google Places API (New) key
- Google Programmable Search (Custom Search JSON API) key + Search Engine ID (`cx`) for booking-platform discovery
- OpenRouter API key (optional for AI formatting; falls back to extracted text)

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
dotnet user-secrets set "VenueAutofill:UseMocks" "false"
dotnet run
```

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

Environment variable override: `GooglePlaces__ApiKey`, `GoogleCustomSearch__ApiKey`, `AI__ApiKey`, `VenueAutofill__UseMocks`.

### Custom Search setup

1. Create a [Programmable Search Engine](https://programmablesearchengine.google.com/) (search the entire web).
2. Enable **Custom Search API** in Google Cloud Console for the same project as Places.
3. Set `GoogleCustomSearch:SearchEngineId` to your `cx` value.

Listing probes fetch public HTML metadata only (`og:image`, JSON-LD). Sites that block bots are reported as `skipped` in `sourcesChecked`.

**Never commit API keys** in `appsettings.json`. Use user-secrets or environment variables only.

## Data pipeline (live mode)

1. Google Places Text Search → candidates
2. Confidence scoring → success / ambiguous / not_found
3. Google Place Details + Photos
4. **Cross-check** — Custom Search finds listing URLs per platform (`Data/booking-platforms.json`); lightweight probes extract name/location/image signals
5. Website extraction (mode-dependent: official site, platform listing, or user `source`)
6. **Image resolver** — picks best HTTPS image (official site → Google photo → matched booking listing)
7. OpenRouter AI — description + amenity normalization (skipped for `googlePlaces` mode)
8. LA28 zone resolver — coordinate bounds + keywords (`Data/la28-zones.json`)

Hotel check-in/out and amenities come from website extraction only (no default times invented).

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
- `sourceUsed`, `sourcesChecked[]`, `imageSource`, `imageCandidates`, `warnings`

Without the query flag, the response matches the original flat contract (no confidence fields).

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
2. Configure Google Places and OpenRouter keys via secrets
3. Set `VenueAutofill:RequireApiKey` to `true` and set `VenueAutofill:ApiKey`
4. Do not expose Swagger publicly without protection

## Postman

Import [`postman/VenueAutofill.postman_collection.json`](postman/VenueAutofill.postman_collection.json).
