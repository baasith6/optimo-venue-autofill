namespace VenueAutofill.Api.Configuration;

public class AzureBlobStorageOptions
{
    public const string SectionName = "AzureBlobStorage";

    public string ConnectionString { get; set; } = string.Empty;
    public string ContainerName { get; set; } = "venue-images";
    public string? PublicBaseUrl { get; set; }
    public bool UseManagedIdentity { get; set; }
    public string? AccountName { get; set; }
}
