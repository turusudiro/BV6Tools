using AngleSharp;
using BV6Tools.Services.ManifestDownloader.Models;
using BV6Tools.Views.Dialogs;
using STCommon;
using SteamKit2;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

namespace BV6Tools.Services.ManifestDownloader
{
    public readonly record struct ManifestDownloaderResult(LuaData LuaData, IReadOnlyCollection<ManifestResult> ManifestResult);
    public readonly record struct ManifestResult(string FileName, uint DepotID, byte[] Bytes);

    public partial class ManifestDownloader(HttpClientService httpClientService) : IManifestDownloader
    {
        private const string githubUserContentUrl = "https://raw.githubusercontent.com/";
        private readonly HttpClientService httpClientService = httpClientService;
        private record MirrorSource(string Name, string UrlTemplate);

        private readonly Dictionary<string, double> mirrorPriority = [];

        // Some of them are provided from https://githubproxy.net/services/
        private readonly MirrorSource[] mirrors =
        [
            new("statically", "https://cdn.statically.io/gh/{0}@{1}/{2}"),
            new("gh-proxy",   $"https://gh-proxy.org/{githubUserContentUrl}{{0}}/{{1}}/{{2}}"),
            new("jsdelivr",   "https://cdn.jsdelivr.net/gh/{0}@{1}/{2}"),
            new("ghfast",     $"https://ghfast.top/{githubUserContentUrl}{{0}}/{{1}}/{{2}}"),
            new("jsdmirror",  "https://cdn.jsdmirror.com/gh/{0}@{1}/{2}"),
            new("github",     "https://raw.githubusercontent.com/{0}/{1}/{2}"),
        ];

        private readonly string[] repos =
        [
            "hammerwebsite12/sojogames2",
            "dvahana2424-web/sojogamesdatabase1",
            "SPIN0ZAi/SB_manifest_DB",
            "SSMGAlt/ManifestHub2",
            "Auiowu/ManifestAutoUpdate"
        ];

        [GeneratedRegex(@"/blob/(?<SHA>[^/]+)/(?<Path>.+)")]
        private static partial Regex GroupBlobPathAndSHAFromURL { get; }

        public async Task<byte[]?> DownloadFileAsync(string repo, string sha, string path, CancellationToken token = default)
        {
            for (var retry = 3; retry > 0; retry--)
            {
                var sortedMirrors = mirrors.OrderBy(m => mirrorPriority.GetValueOrDefault(m.Name, 0));

                foreach (var mirror in sortedMirrors)
                {
                    var url = string.Format(mirror.UrlTemplate, repo, sha, path);
                    var stopwatch = Stopwatch.StartNew();
                    try
                    {
                        var request = new HttpRequestMessage(HttpMethod.Get, url);
                        request.Headers.UserAgent.ParseAdd(
                            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/122.0.0.0 Safari/537.36");
                        request.Headers.Accept.ParseAdd("*/*");
                        var result = await httpClientService.DownloadDataAsync(request, TimeSpan.FromSeconds(5), token);

                        if (result != null && result.Length > 0)
                        {
                            var contentStart = Encoding.UTF8.GetString([.. result.Take(Math.Min(100, result.Length))]);
                            var isNotHtml = !contentStart.TrimStart().StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase) &&
                                           !contentStart.TrimStart().StartsWith("<html", StringComparison.OrdinalIgnoreCase);
                            if (isNotHtml)
                            {
                                mirrorPriority[mirror.Name] = stopwatch.ElapsedMilliseconds;
                                return result;
                            }
                        }

                        // response is not expected, make it less priority
                        mirrorPriority[mirror.Name] = mirrorPriority.GetValueOrDefault(mirror.Name, 0) + 3000;
                    }
                    catch (TimeoutException)
                    {
                        // Never even responded, worse than a normal failure
                        mirrorPriority[mirror.Name] = mirrorPriority.GetValueOrDefault(mirror.Name, 0) + 5000;
                    }
                    catch (HttpRequestException)
                    {
                        // Failed or timed out, make it less priority
                        mirrorPriority[mirror.Name] = mirrorPriority.GetValueOrDefault(mirror.Name, 0) + 3000;
                    }
                }

                if (retry > 1)
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, 4 - retry)), token);
                }
            }
            return null;
        }

        public async Task<ManifestDownloaderResult> DownloadManifestAsync(uint appid,
                                IProgress<ProgressInfo>? progress = default, CancellationToken token = default)
        {
            progress?.Report(new ProgressInfo("Searching manifest", IsIndeterminate: true));

            var manifestLink = await GetManifestLinkAsync(progress, appid, token);

            // Validate that .lua files exist
            var hasLuaFiles = manifestLink.Any(link => link.Path.EndsWith(".lua", StringComparison.OrdinalIgnoreCase));
            if (!hasLuaFiles)
            {
                progress?.Report(new ProgressInfo("Warning: No .lua files found in repository"));
            }
            if (manifestLink.Count == 0) throw new InvalidOperationException("No Manifest link found in any repo");

            token.ThrowIfCancellationRequested();

            progress?.Report(new(Value: 0, MaxValue: manifestLink.Count, IsIndeterminate: false));

            Dictionary<string, ManifestData> manifestData = [];
            List<ManifestResult> manifestResults = [];
            var luaAppIds = new Dictionary<string, LuaAppIdWithKey?>();
            var luaTokens = new Dictionary<string, string>();

            double progressValue = 0;

            foreach (var link in manifestLink)
            {
                token.ThrowIfCancellationRequested();
                if (link.Path.EndsWith(".manifest", StringComparison.OrdinalIgnoreCase))
                {
                    string fileName = link.Path;
                    var parts = link.Path.Split('_');
                    var depotAppIdStr = parts[0];
                    var manifestId = Path.GetFileNameWithoutExtension(parts[1]);

                    if (ST.IsSharedDepot(depotAppIdStr)) continue;

                    progress?.Report(new ProgressInfo($"Downloading {link.Path}...", progressValue++));
                    var bytes = await DownloadFileAsync(link.Repo, link.SHA, link.Path, token);
                    if (bytes == null || bytes.Length == 0) continue;

                    if (uint.TryParse(depotAppIdStr, out var depotAppId))
                    {
                        if (manifestData.TryGetValue(depotAppIdStr, out var existing))
                        {
                            manifestData[depotAppIdStr] = existing with { ManifestID = manifestId };
                        }
                        else
                        {
                            manifestData[depotAppIdStr] = new ManifestData(ManifestID: manifestId, null);
                        }
                        manifestResults.Add(new()
                        {
                            Bytes = bytes,
                            DepotID = depotAppId,
                            FileName = fileName,
                        });
                    }
                }
                else if (link.Path.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
                {
                    progress?.Report(new ProgressInfo($"Downloading {link.Path} and parsing the available key",
                        progressValue++));
                    var bytes = await DownloadFileAsync(link.Repo, link.SHA, link.Path, token);
                    if (bytes == null || bytes.Length == 0) continue;

                    var luaContent = Encoding.UTF8.GetString(bytes);
                    var parsed = ST.ParseFromLua(luaContent);

                    foreach (var kvp in parsed.Appids)
                    {
                        if (kvp.Value is LuaAppIdWithKey { } luaApp)
                        {
                            if (luaAppIds.TryGetValue(kvp.Key, out var existing) && existing is LuaAppIdWithKey { } existingLuaApp)
                            {
                                luaAppIds[kvp.Key] = new LuaAppIdWithKey(
                                    Flag: !string.IsNullOrEmpty(luaApp.Flag) ? luaApp.Flag : existingLuaApp.Flag,
                                    DecryptionKey: !string.IsNullOrEmpty(luaApp.DecryptionKey) ? luaApp.DecryptionKey : existingLuaApp.DecryptionKey
                                );
                            }
                            else
                            {
                                luaAppIds[kvp.Key] = luaApp;
                            }
                        }
                    }
                    foreach (var kvp in parsed.TokenData) luaTokens[kvp.Key] = kvp.Value;
                    foreach (var kvp in parsed.Manifest)
                    {
                        if (manifestData.TryGetValue(kvp.Key, out var existing))
                        {
                            manifestData[kvp.Key] = existing with
                            {
                                Size = kvp.Value.Size ?? existing.Size,
                                ManifestID = kvp.Value.ManifestID ?? existing.ManifestID
                            };
                        }
                        else
                        {
                            manifestData[kvp.Key] = kvp.Value;
                        }
                    }
                }
                else if (link.Path.EndsWith("key.vdf", StringComparison.OrdinalIgnoreCase))
                {
                    progress?.Report(new ProgressInfo($"Downloading {link.Path} and parsing the available key",
                        progressValue++));
                    var bytes = await DownloadFileAsync(link.Repo, link.SHA, link.Path, token);
                    if (bytes == null || bytes.Length == 0) continue;

                    using var stream = new MemoryStream(bytes, 0, bytes.Length, writable: false);
                    KeyValue keyValue = new();
                    if (!keyValue.ReadAsText(stream)) continue;
                    foreach (var depot in keyValue.Children)
                    {
                        string? depotId = depot.Name;
                        if (depotId == null) continue;
                        string? key = depot["DecryptionKey"].Value;
                        if (key == null) continue;
                        if (luaAppIds.TryGetValue(depotId, out var luaAppId) && luaAppId is LuaAppIdWithKey { } existingLuaAppid)
                        {
                            luaAppIds[depotId] = existingLuaAppid with { DecryptionKey = key };
                        }
                        else
                        {
                            luaAppIds[depotId] = new LuaAppIdWithKey("0", key);
                        }
                    }
                }
            }
            var luaData = new LuaData(luaAppIds, manifestData, luaTokens);
            return new ManifestDownloaderResult(luaData, manifestResults);
        }

        public async Task<IReadOnlyCollection<ManifestURL>> GetManifestLinkAsync(IProgress<ProgressInfo>? progress, uint appid, CancellationToken token)
        {
            var config = Configuration.Default.WithDefaultLoader();

            var context = BrowsingContext.New(config);
            var manifestUrls = new HashSet<ManifestURL>();

            foreach (var repo in repos)
            {
                progress?.Report(new ProgressInfo($"Searching in {Environment.NewLine + repo + Environment.NewLine}repository"));
                if (manifestUrls.Count != 0) break;
                try
                {
                    var url = $"https://github.com/{repo}/tree/{appid}";

                    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);

                    var document = await context.OpenAsync(url, linkedCts.Token);
                    var anchors = document.QuerySelectorAll("a.Link--primary");

                    if (anchors != null && anchors.Length > 0)
                    {
                        foreach (var anchor in anchors)
                        {
                            var href = anchor.GetAttribute("href");

                            // Look for GitHub blob URLs (files)
                            if (!string.IsNullOrEmpty(href) && href.Contains("/blob/"))
                            {
                                // Use regex to extract SHA and Path from the URL
                                var match = GroupBlobPathAndSHAFromURL.Match(href);

                                if (match.Success)
                                {
                                    var sha = match.Groups["SHA"].Value;
                                    var path = match.Groups["Path"].Value;

                                    manifestUrls.Add(new ManifestURL(repo, sha, path));
                                }
                            }
                        }
                    }
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    continue;
                }
            }

            if (manifestUrls.Count == 0) throw new Exception("No Manifest URL Found");
            return manifestUrls;
        }

        public string ParseVdfToLua(KeyValue depotInfo, string appid, string saveDir)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"addappid({appid})");
            foreach (var depot in depotInfo.Children)
            {
                sb.AppendLine($"addappid({depot.Name},0,\"{depot["DecryptionKey"].Value}\")");
                var manifestFiles = Directory.GetFiles(saveDir, $"{depot.Name}_*.manifest");
                foreach (var file in manifestFiles)
                {
                    var manifestId = Path.GetFileNameWithoutExtension(file)[(depot.Name!.Length + 1)..];
                    sb.AppendLine($"setManifestid({depot.Name},\"{manifestId}\")");
                }
            }

            return sb.ToString();
        }

        public string ParseVdfToLua(List<(string depotId, string decryptionKey)> depotInfo, string appid, string saveDir)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"addappid({appid})");
            foreach (var (depotId, key) in depotInfo)
            {
                sb.AppendLine($"addappid({depotId},1,\"{key}\")");
                var manifestFiles = Directory.GetFiles(saveDir, $"{depotId}_*.manifest");
                foreach (var file in manifestFiles)
                {
                    var manifestId = Path.GetFileNameWithoutExtension(file)[(depotId.Length + 1)..];
                    sb.AppendLine($"setManifestid({depotId},\"{manifestId}\",0)");
                }
            }

            return sb.ToString();
        }
    }
}