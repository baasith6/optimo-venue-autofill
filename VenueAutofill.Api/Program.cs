using System.Net;
using System.Threading.RateLimiting;
using FluentValidation;
using Microsoft.AspNetCore.RateLimiting;
using VenueAutofill.Api.Application.Interfaces;
using VenueAutofill.Api.Application.Services;
using VenueAutofill.Api.Configuration;
using VenueAutofill.Api.Contracts.Requests;
using VenueAutofill.Api.Infrastructure.Caching;
using VenueAutofill.Api.Infrastructure.Http;
using VenueAutofill.Api.Infrastructure.Providers;
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
builder.Services.Configure<AiOptions>(builder.Configuration.GetSection(AiOptions.SectionName));
builder.Services.Configure<VenueAutofillOptions>(builder.Configuration.GetSection(VenueAutofillOptions.SectionName));

builder.Services.AddMemoryCache();
builder.Services.AddSingleton<UrlSafetyValidator>();
builder.Services.AddSingleton<SourceRelevanceValidator>();
builder.Services.AddScoped<IAmbiguousSearchCache, MemoryAmbiguousSearchCache>();
builder.Services.AddScoped<IVenueConfidenceService, VenueConfidenceService>();
builder.Services.AddScoped<IZoneResolverService, ZoneResolverService>();
builder.Services.AddScoped<IVenueAutofillService, VenueAutofillService>();

builder.Services.AddValidatorsFromAssemblyContaining<VenueAutofillRequestValidator>();

builder.Services.AddHttpClient<GooglePlacesProvider>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(90);
    client.DefaultRequestVersion = HttpVersion.Version11;
    client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
});
builder.Services.AddHttpClient<WebsiteExtractionProvider>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("VenueAutofillBot/1.0");
});
builder.Services.AddHttpClient<OpenRouterAiProvider>(client => client.Timeout = TimeSpan.FromSeconds(60));

builder.Services.AddScoped<IVenueDiscoveryService>(sp => sp.GetRequiredService<GooglePlacesProvider>());
builder.Services.AddScoped<IVenueDetailsService>(sp => sp.GetRequiredService<GooglePlacesProvider>());
builder.Services.AddScoped<IVenueExtractionService, WebsiteExtractionProvider>();
builder.Services.AddScoped<IAiVenueFormatterService, OpenRouterAiProvider>();

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
var aiKey = app.Configuration["AI:ApiKey"];
app.Logger.LogInformation(
    "Venue Autofill startup: UseMocks={UseMocks}, GooglePlacesKeyConfigured={HasGoogle}, OpenRouterKeyConfigured={HasAi}",
    venueOpts.UseMocks,
    !string.IsNullOrWhiteSpace(googleKey),
    !string.IsNullOrWhiteSpace(aiKey));
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
