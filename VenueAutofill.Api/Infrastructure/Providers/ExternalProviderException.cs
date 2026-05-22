namespace VenueAutofill.Api.Infrastructure.Providers;

public class ExternalProviderException : Exception
{
    public string Provider { get; }
    public int? ProviderStatusCode { get; }

    public ExternalProviderException(string provider, string message, int? providerStatusCode = null, Exception? inner = null)
        : base(message, inner)
    {
        Provider = provider;
        ProviderStatusCode = providerStatusCode;
    }
}
