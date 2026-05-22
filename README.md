# Venue Autofill API

ASP.NET Core Web API that enriches limited venue input from Optimo using Google Places, controlled website extraction, and OpenRouter AI formatting.

## Prerequisites

- .NET 9 SDK
- Google Places API (New) key
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

Environment variable override: `GooglePlaces__ApiKey`, `AI__ApiKey`, `VenueAutofill__UseMocks`.

**Never commit API keys** in `appsettings.json`. Use user-secrets or environment variables only.

## Data pipeline (live mode)

1. Google Places Text Search → candidates
2. Confidence scoring → success / ambiguous / not_found
3. Google Place Details + Photos
4. Website extraction (up to `MaxExtractionPages`, official site + user `source`)
5. OpenRouter AI — description + amenity normalization (no invented venue identity)
6. LA28 zone resolver — coordinate bounds + keywords (`Data/la28-zones.json`)

Hotel check-in/out and amenities come from website extraction only (no default times invented).

User-provided `source` URLs are validated for SSRF safety and relevance to venue name, city, and country.

## Endpoints

- `POST /api/venue-autofill` — search and enrich (or ambiguous / not_found)
- `POST /api/venue-autofill/confirm` — confirm ambiguous selection
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
