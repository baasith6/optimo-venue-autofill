namespace VenueAutofill.Api.Configuration;

public class ImageNormalizationOptions
{
    public const string SectionName = "ImageNormalization";

    public bool Enabled { get; set; } = true;
    public int TargetWidth { get; set; } = 1200;
    public int TargetHeight { get; set; } = 800;
    public int JpegQuality { get; set; } = 85;
    public int MaxDownloadBytes { get; set; } = 5_000_000;
    public int DownloadTimeoutSeconds { get; set; } = 15;
    public string CacheKeyVersion { get; set; } = "v1";
}
