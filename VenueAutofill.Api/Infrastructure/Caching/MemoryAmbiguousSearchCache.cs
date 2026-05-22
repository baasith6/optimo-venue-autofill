using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using VenueAutofill.Api.Application.Interfaces;
using VenueAutofill.Api.Configuration;
using VenueAutofill.Api.Contracts.Internal;
using VenueAutofill.Api.Contracts.Requests;

namespace VenueAutofill.Api.Infrastructure.Caching;

public class MemoryAmbiguousSearchCache : IAmbiguousSearchCache
{
    private readonly IMemoryCache _cache;
    private readonly VenueAutofillOptions _options;

    public MemoryAmbiguousSearchCache(IMemoryCache cache, IOptions<VenueAutofillOptions> options)
    {
        _cache = cache;
        _options = options.Value;
    }

    public string Store(VenueAutofillRequest request, IReadOnlyList<VenueCandidate> candidates)
    {
        var reference = $"search_{DateTime.UtcNow:yyyyMMdd}_{Guid.NewGuid():N}"[..32];
        var session = new AmbiguousSearchSession
        {
            Reference = reference,
            OriginalRequest = request,
            Candidates = candidates.ToList()
        };

        _cache.Set(reference, session, TimeSpan.FromMinutes(_options.AmbiguousCacheMinutes));
        return reference;
    }

    public AmbiguousSearchSession? Get(string reference) =>
        _cache.TryGetValue(reference, out AmbiguousSearchSession? session) ? session : null;

    public VenueCandidate? GetCandidate(string reference, string optionId)
    {
        var session = Get(reference);
        if (session is null)
            return null;

        var index = int.TryParse(optionId, out var i) ? i - 1 : -1;
        if (index < 0 || index >= session.Candidates.Count)
            return null;

        return session.Candidates[index];
    }
}
