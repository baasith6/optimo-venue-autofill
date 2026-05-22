using Microsoft.Extensions.Options;
using VenueAutofill.Api.Configuration;

namespace VenueAutofill.Api.Middleware;

public class ApiKeyMiddleware
{
    private const string ApiKeyHeader = "X-Api-Key";
    private readonly RequestDelegate _next;
    private readonly VenueAutofillOptions _options;

    public ApiKeyMiddleware(RequestDelegate next, IOptions<VenueAutofillOptions> options)
    {
        _next = next;
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_options.RequireApiKey
            || context.Request.Path.StartsWithSegments("/swagger")
            || context.Request.Path.StartsWithSegments("/health"))
        {
            await _next(context);
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(ApiKeyHeader, out var provided)
            || provided != _options.ApiKey)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { message = "Invalid or missing API key." });
            return;
        }

        await _next(context);
    }
}
