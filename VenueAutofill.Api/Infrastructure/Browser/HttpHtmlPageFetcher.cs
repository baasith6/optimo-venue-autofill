using System.Net;
using Microsoft.Extensions.Options;
using VenueAutofill.Api.Configuration;

namespace VenueAutofill.Api.Infrastructure.Browser;

public class HttpHtmlPageFetcher
{
  public const string HttpClientName = "HtmlFetch";

  private readonly IHttpClientFactory _httpClientFactory;
  private readonly BrowserFetchOptions _options;

  public HttpHtmlPageFetcher(IHttpClientFactory httpClientFactory, IOptions<BrowserFetchOptions> options)
  {
    _httpClientFactory = httpClientFactory;
    _options = options.Value;
  }

  public async Task<HtmlFetchResult> FetchAsync(string url, CancellationToken cancellationToken = default)
  {
    var result = new HtmlFetchResult { FinalUrl = url };

    if (!_options.Enabled)
      return result;

    try
    {
      var client = _httpClientFactory.CreateClient(HttpClientName);
      using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
      cts.CancelAfter(TimeSpan.FromSeconds(_options.HttpTimeoutSeconds));

      using var response = await client.GetAsync(url, cts.Token);
      result.StatusCode = (int)response.StatusCode;
      result.FinalUrl = response.RequestMessage?.RequestUri?.ToString() ?? url;

      if (!response.IsSuccessStatusCode)
        return result;

      result.Html = await response.Content.ReadAsStringAsync(cts.Token);
      result.Succeeded = !LooksBlocked(result.Html, ExtractTitle(result.Html));
      result.LooksBlocked = !result.Succeeded && result.Html.Length > 0;
      result.FetchedVia = "http";
      return result;
    }
    catch (Exception)
    {
      return result;
    }
  }

  internal static bool LooksBlocked(string html, string? title)
  {
    if (string.IsNullOrWhiteSpace(html))
      return true;
    if (html.Length < 500)
      return true;

    if (!string.IsNullOrWhiteSpace(title))
    {
      if (title.Contains("access denied", StringComparison.OrdinalIgnoreCase)
          || title.Contains("just a moment", StringComparison.OrdinalIgnoreCase)
          || title.Contains("attention required", StringComparison.OrdinalIgnoreCase))
        return true;
    }

    var sample = html.Length > 4000 ? html[..4000] : html;
    return sample.Contains("cf-browser-verification", StringComparison.OrdinalIgnoreCase)
           || sample.Contains("challenge-platform", StringComparison.OrdinalIgnoreCase)
           || sample.Contains("Please enable cookies", StringComparison.OrdinalIgnoreCase);
  }

  private static string? ExtractTitle(string html)
  {
    var match = System.Text.RegularExpressions.Regex.Match(
      html,
      @"<title[^>]*>(.*?)</title>",
      System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
    return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value.Trim()) : null;
  }
}
