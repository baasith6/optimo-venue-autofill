using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using VenueAutofill.Api.Application.Interfaces;
using VenueAutofill.Api.Configuration;
using VenueAutofill.Api.Contracts.Internal;
using VenueAutofill.Api.Infrastructure.Http;

namespace VenueAutofill.Api.Application.Services;

public class ImageNormalizationService : IImageNormalizationService
{
    private readonly ImageNormalizationOptions _options;
    private readonly IImageBlobStorage _blobStorage;
    private readonly HttpClient _httpClient;
    private readonly UrlSafetyValidator _urlSafetyValidator;
    private readonly ILogger<ImageNormalizationService> _logger;

    public ImageNormalizationService(
        IOptions<ImageNormalizationOptions> options,
        IImageBlobStorage blobStorage,
        HttpClient httpClient,
        UrlSafetyValidator urlSafetyValidator,
        ILogger<ImageNormalizationService> logger)
    {
        _options = options.Value;
        _blobStorage = blobStorage;
        _httpClient = httpClient;
        _urlSafetyValidator = urlSafetyValidator;
        _logger = logger;
    }

    public async Task<NormalizedImageResult> NormalizeAndUploadAsync(
        string sourceUrl,
        CancellationToken cancellationToken = default)
    {
        var result = new NormalizedImageResult { OriginalUrl = sourceUrl };

        if (string.IsNullOrWhiteSpace(sourceUrl))
        {
            result.Warning = "No image URL provided for normalization.";
            return result;
        }

        if (!_options.Enabled)
        {
            result.Succeeded = true;
            result.NormalizedUrl = sourceUrl;
            return result;
        }

        if (!_blobStorage.IsConfigured)
        {
            result.Warning = "Azure Blob storage is not configured; using source image URL.";
            result.NormalizedUrl = sourceUrl;
            return result;
        }

        if (!_urlSafetyValidator.IsSafeUrl(sourceUrl, out _))
        {
            result.Warning = "Image URL failed safety validation.";
            result.NormalizedUrl = sourceUrl;
            return result;
        }

        try
        {
            using var downloadCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            downloadCts.CancelAfter(TimeSpan.FromSeconds(_options.DownloadTimeoutSeconds));

            var bytes = await DownloadImageAsync(sourceUrl, downloadCts.Token);
            if (bytes is null || bytes.Length == 0)
            {
                result.Warning = "Image could not be downloaded for normalization; using source URL.";
                result.NormalizedUrl = sourceUrl;
                return result;
            }

            using var image = Image.Load(bytes);
            image.Mutate(ctx => ctx.Resize(new ResizeOptions
            {
                Size = new Size(_options.TargetWidth, _options.TargetHeight),
                Mode = ResizeMode.Crop,
                Position = AnchorPositionMode.Center
            }));

            await using var output = new MemoryStream();
            await image.SaveAsJpegAsync(output, new JpegEncoder { Quality = _options.JpegQuality }, cancellationToken);
            output.Position = 0;

            var cacheKey = BuildCacheKey(sourceUrl);
            var blobName = $"{cacheKey}.jpg";
            var blobUrl = await _blobStorage.UploadAsync(blobName, output, "image/jpeg", cancellationToken);

            if (string.IsNullOrWhiteSpace(blobUrl))
            {
                result.Warning = "Image normalized but Azure Blob upload failed; using source URL.";
                result.NormalizedUrl = sourceUrl;
                return result;
            }

            result.Succeeded = true;
            result.NormalizedUrl = blobUrl;
            result.Width = _options.TargetWidth;
            result.Height = _options.TargetHeight;
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Image normalization failed for {Url}", sourceUrl);
            result.Warning = "Image could not be normalized to 1200x800; using source URL.";
            result.NormalizedUrl = sourceUrl;
            return result;
        }
    }

    private async Task<byte[]?> DownloadImageAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        if (response.Content.Headers.ContentLength > _options.MaxDownloadBytes)
            return null;

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var ms = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        long total = 0;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += read;
            if (total > _options.MaxDownloadBytes)
                return null;
            ms.Write(buffer, 0, read);
        }

        return ms.ToArray();
    }

    private string BuildCacheKey(string sourceUrl)
    {
        var input = $"{sourceUrl}|{_options.TargetWidth}x{_options.TargetHeight}|{_options.CacheKeyVersion}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant()[..32];
    }
}
