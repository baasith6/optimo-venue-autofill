using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Identity;
using Microsoft.Extensions.Options;
using VenueAutofill.Api.Application.Interfaces;
using VenueAutofill.Api.Configuration;

namespace VenueAutofill.Api.Infrastructure.Storage;

public class AzureBlobImageStorage : IImageBlobStorage
{
    private readonly AzureBlobStorageOptions _options;
    private readonly ILogger<AzureBlobImageStorage> _logger;
    private BlobContainerClient? _container;

    public AzureBlobImageStorage(
        IOptions<AzureBlobStorageOptions> options,
        ILogger<AzureBlobImageStorage> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_options.ConnectionString)
        || (_options.UseManagedIdentity && !string.IsNullOrWhiteSpace(_options.AccountName));

    public async Task<string?> UploadAsync(
        string blobName,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        var container = await GetContainerAsync(cancellationToken);
        if (container is null)
            return null;

        var client = container.GetBlobClient(blobName);
        await client.UploadAsync(
            content,
            new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
            },
            cancellationToken);

        return GetPublicUrl(blobName);
    }

    public string GetPublicUrl(string blobName)
    {
        if (!string.IsNullOrWhiteSpace(_options.PublicBaseUrl))
            return $"{_options.PublicBaseUrl.TrimEnd('/')}/{blobName}";

        var account = _options.AccountName;
        if (string.IsNullOrWhiteSpace(account) && !string.IsNullOrWhiteSpace(_options.ConnectionString))
            account = TryParseAccountName(_options.ConnectionString);

        var container = _options.ContainerName;
        return $"https://{account}.blob.core.windows.net/{container}/{blobName}";
    }

    private async Task<BlobContainerClient?> GetContainerAsync(CancellationToken cancellationToken)
    {
        if (_container is not null)
            return _container;

        try
        {
            BlobServiceClient service;
            if (!string.IsNullOrWhiteSpace(_options.ConnectionString))
            {
                service = new BlobServiceClient(_options.ConnectionString);
            }
            else if (_options.UseManagedIdentity && !string.IsNullOrWhiteSpace(_options.AccountName))
            {
                var uri = new Uri($"https://{_options.AccountName}.blob.core.windows.net");
                service = new BlobServiceClient(uri, new DefaultAzureCredential());
            }
            else
            {
                return null;
            }

            _container = service.GetBlobContainerClient(_options.ContainerName);
            await _container.CreateIfNotExistsAsync(PublicAccessType.Blob, cancellationToken: cancellationToken);
            return _container;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Azure Blob container {Container}", _options.ContainerName);
            return null;
        }
    }

    private static string? TryParseAccountName(string connectionString)
    {
        foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.StartsWith("AccountName=", StringComparison.OrdinalIgnoreCase))
                return part["AccountName=".Length..];
        }

        return null;
    }
}
