namespace VenueAutofill.Api.Application.Interfaces;

public interface IImageBlobStorage
{
    bool IsConfigured { get; }
    Task<string?> UploadAsync(string blobName, Stream content, string contentType, CancellationToken cancellationToken = default);
    string GetPublicUrl(string blobName);
}
