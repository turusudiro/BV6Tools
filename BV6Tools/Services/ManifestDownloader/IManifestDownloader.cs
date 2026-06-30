using BV6Tools.Services.ManifestDownloader.Models;
using BV6Tools.Views.Dialogs;
using SteamKit2;

namespace BV6Tools.Services.ManifestDownloader;

public interface IManifestDownloader
{
    Task<ManifestDownloaderResult> DownloadManifestAsync(uint appid,
    IProgress<ProgressInfo>? progress = default, CancellationToken token = default);
    Task<IReadOnlyCollection<ManifestURL>> GetManifestLinkAsync(IProgress<ProgressInfo>? progress, uint appid, CancellationToken token = default);
    Task<byte[]?> DownloadFileAsync(string repo, string sha, string path, CancellationToken token = default);
    string ParseVdfToLua(KeyValue depotInfo, string appid, string saveDir);
    string ParseVdfToLua(List<(string depotId, string decryptionKey)> depotInfo, string appid, string saveDir);
}