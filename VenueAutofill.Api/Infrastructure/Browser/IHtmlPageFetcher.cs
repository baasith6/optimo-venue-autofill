namespace VenueAutofill.Api.Infrastructure.Browser;

public interface IHtmlPageFetcher
{
    Task<HtmlFetchResult> FetchAsync(string url, CancellationToken cancellationToken = default);
}
