namespace VenueAutofill.Api.Infrastructure.Browser;

public class CompositeHtmlPageFetcher : IHtmlPageFetcher
{
    private readonly HttpHtmlPageFetcher _httpFetcher;
    private readonly PlaywrightHtmlPageFetcher _playwrightFetcher;

    public CompositeHtmlPageFetcher(HttpHtmlPageFetcher httpFetcher, PlaywrightHtmlPageFetcher playwrightFetcher)
    {
        _httpFetcher = httpFetcher;
        _playwrightFetcher = playwrightFetcher;
    }

    public async Task<HtmlFetchResult> FetchAsync(string url, CancellationToken cancellationToken = default)
    {
        var httpResult = await _httpFetcher.FetchAsync(url, cancellationToken);
        if (httpResult.Succeeded)
            return httpResult;

        if (httpResult.LooksBlocked || !httpResult.Succeeded)
        {
            var pwResult = await _playwrightFetcher.FetchAsync(url, cancellationToken);
            if (pwResult.Succeeded)
                return pwResult;

            if (!string.IsNullOrWhiteSpace(pwResult.Html))
                return pwResult;
        }

        return httpResult;
    }
}
