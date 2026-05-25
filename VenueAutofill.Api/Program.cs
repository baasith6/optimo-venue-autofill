using System.Net;
using System.Threading.RateLimiting;
using FluentValidation;
using Microsoft.AspNetCore.RateLimiting;
using VenueAutofill.Api.Application.Interfaces;
using VenueAutofill.Api.Application.Services;
using VenueAutofill.Api.Configuration;
using VenueAutofill.Api.Contracts.Requests;
using VenueAutofill.Api.Infrastructure.Browser;
using VenueAutofill.Api.Infrastructure.Caching;
using VenueAutofill.Api.Infrastructure.Data;
using VenueAutofill.Api.Infrastructure.Http;
using VenueAutofill.Api.Infrastructure.Providers;
using VenueAutofill.Api.Infrastructure.Storage;
using VenueAutofill.Api.Middleware;
using VenueAutofill.Api.Validation;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Venue Autofill API", Version = "v1" });
});

builder.Services.Configure<GooglePlacesOptions>(builder.Configuration.GetSection(GooglePlacesOptions.SectionName));
builder.Services.Configure<GoogleCustomSearchOptions>(builder.Configuration.GetSection(GoogleCustomSearchOptions.SectionName));
builder.Services.Configure<AiOptions>(builder.Configuration.GetSection(AiOptions.SectionName));
builder.Services.Configure<VenueAutofillOptions>(builder.Configuration.GetSection(VenueAutofillOptions.SectionName));
builder.Services.Configure<ImageNormalizationOptions>(builder.Configuration.GetSection(ImageNormalizationOptions.SectionName));
builder.Services.Configure<AzureBlobStorageOptions>(builder.Configuration.GetSection(AzureBlobStorageOptions.SectionName));
builder.Services.Configure<BrowserFetchOptions>(builder.Configuration.GetSection(BrowserFetchOptions.SectionName));

builder.Services.AddMemoryCache();
builder.Services.AddSingleton<UrlSafetyValidator>();
builder.Services.AddSingleton<SourceRelevanceValidator>();
builder.Services.AddSingleton<BookingPlatformRegistry>();
builder.Services.AddSingleton<IAmenityNormalizationService, AmenityNormalizationService>();
builder.Services.AddSingleton<IImageBlobStorage, AzureBlobImageStorage>();
builder.Services.AddScoped<IAmbiguousSearchCache, MemoryAmbiguousSearchCache>();
builder.Services.AddScoped<IVenueConfidenceService, VenueConfidenceService>();
builder.Services.AddScoped<IVenueCrossSourceService, VenueCrossSourceService>();
builder.Services.AddScoped<IImageResolverService, ImageResolverService>();
builder.Services.AddScoped<IImageNormalizationService, ImageNormalizationService>();
builder.Services.AddScoped<IZoneResolverService, ZoneResolverService>();
builder.Services.AddScoped<IVenueAutofillService, VenueAutofillService>();

builder.Services.AddValidatorsFromAssemblyContaining<VenueAutofillRequestValidator>();

builder.Services.AddHttpClient<GooglePlacesProvider>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(90);
    client.DefaultRequestVersion = HttpVersion.Version11;
    client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
});
builder.Services.AddHttpClient(HttpHtmlPageFetcher.HttpClientName, client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
    client.DefaultRequestHeaders.UserAgent.ParseAdd(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
    client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
});
builder.Services.AddSingleton<HttpHtmlPageFetcher>();
builder.Services.AddSingleton<PlaywrightHtmlPageFetcher>();
builder.Services.AddSingleton<IHtmlPageFetcher, CompositeHtmlPageFetcher>();

builder.Services.AddHttpClient<WebsiteExtractionProvider>();
builder.Services.AddHttpClient<OpenRouterAiProvider>(client => client.Timeout = TimeSpan.FromSeconds(60));
builder.Services.AddHttpClient<HotelTimesInferenceService>(client => client.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddHttpClient<GoogleCustomSearchProvider>(client => client.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddHttpClient<ListingProbeService>(client => client.Timeout = TimeSpan.FromSeconds(20));
builder.Services.AddHttpClient<ImageNormalizationService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("VenueAutofillBot/1.0");
});

builder.Services.AddScoped<IGoogleCustomSearchService>(sp => sp.GetRequiredService<GoogleCustomSearchProvider>());
builder.Services.AddScoped<IListingProbeService>(sp => sp.GetRequiredService<ListingProbeService>());
builder.Services.AddScoped<IVenueDiscoveryService>(sp => sp.GetRequiredService<GooglePlacesProvider>());
builder.Services.AddScoped<IVenueDetailsService>(sp => sp.GetRequiredService<GooglePlacesProvider>());
builder.Services.AddScoped<IVenueExtractionService, WebsiteExtractionProvider>();
builder.Services.AddScoped<IAiVenueFormatterService, OpenRouterAiProvider>();
builder.Services.AddScoped<IHotelTimesInferenceService, HotelTimesInferenceService>();

var venueOptions = builder.Configuration.GetSection(VenueAutofillOptions.SectionName).Get<VenueAutofillOptions>() ?? new();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("venue-autofill", limiter =>
    {
        limiter.PermitLimit = venueOptions.RateLimitPermitLimit;
        limiter.Window = TimeSpan.FromSeconds(venueOptions.RateLimitWindowSeconds);
        limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        limiter.QueueLimit = 0;
    });
});

builder.Services.AddHealthChecks();
builder.Services.AddProblemDetails();

var app = builder.Build();

var venueOpts = app.Configuration.GetSection(VenueAutofillOptions.SectionName).Get<VenueAutofillOptions>() ?? new();
var googleKey = app.Configuration["GooglePlaces:ApiKey"];
var cseKey = app.Configuration["GoogleCustomSearch:ApiKey"];
var aiKey = app.Configuration["AI:ApiKey"];
var blobConfigured = !string.IsNullOrWhiteSpace(app.Configuration["AzureBlobStorage:ConnectionString"])
    || (app.Configuration.GetValue<bool>("AzureBlobStorage:UseManagedIdentity")
        && !string.IsNullOrWhiteSpace(app.Configuration["AzureBlobStorage:AccountName"]));
app.Logger.LogInformation(
    "Venue Autofill startup: UseMocks={UseMocks}, GooglePlacesKeyConfigured={HasGoogle}, CustomSearchConfigured={HasCse}, OpenRouterKeyConfigured={HasAi}, PlatformCrossCheck={CrossCheck}, BlobStorageConfigured={HasBlob}",
    venueOpts.UseMocks,
    !string.IsNullOrWhiteSpace(googleKey),
    !string.IsNullOrWhiteSpace(cseKey),
    !string.IsNullOrWhiteSpace(aiKey),
    venueOpts.EnablePlatformCrossCheck,
    blobConfigured);
if (venueOpts.UseMocks)
    app.Logger.LogWarning("UseMocks is enabled — responses are hardcoded sample data, not from Google Places.");

app.UseExceptionHandler();
app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();
app.UseMiddleware<ApiKeyMiddleware>();
app.UseRateLimiter();

app.MapControllers().RequireRateLimiting("venue-autofill");
app.MapHealthChecks("/health");

app.Run();
