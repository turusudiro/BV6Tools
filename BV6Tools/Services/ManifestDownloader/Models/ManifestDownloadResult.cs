namespace BV6Tools.Services.ManifestDownloader.Models;

public class ManifestDownloadResult
{
    public string FileName { get; set; } = string.Empty;
    public byte[] ManifestData { get; set; } = [];
    public ulong ManifestID { get; set; }
    public string DecryptionKey { get; set; } = string.Empty;
}