using SteamKit2;
using SteamKit2.Internal;
using System.Text.RegularExpressions;

namespace BV6Tools.Services.Steam
{
    public static partial class PICSProductInfoExtenstion
    {
        [GeneratedRegex("os", RegexOptions.IgnoreCase, "en-US")]
        private static partial Regex OSDepotInfoRegex { get; }

        public static SteamAppInfos ToSteamAppInfo(this SteamApps.PICSProductInfoCallback.PICSProductInfo productInfo)
        {
            var dlcs = new HashSet<uint>();
            var depots = new List<Depot>();

            var kvInfo = productInfo.KeyValues;

            uint appid = productInfo.ID;
            string? name = kvInfo["common"]["name"].Value;
            SteamAppInfoType type = kvInfo["common"]["type"].AsEnum<SteamAppInfoType>();
            uint parent = kvInfo["common"]["parent"].AsUnsignedInteger();

            foreach (var depot in kvInfo["depots"].Children)
            {
                if (uint.TryParse(depot.Name, out var id))
                {
                    var os = depot["config"]["oslist"].Value;
                    var osarch = depot["config"]["osarch"].Value;
                    var language = depot["config"]["language"].Value;

                    var depotname = name;
                    bool isDlc = false;
                    uint depotfromapp = depot["depotfromapp"].AsUnsignedInteger();

                    var formattedName = string.Join(' ', new[]
                        {
                        string.IsNullOrWhiteSpace(os)
                            ? string.Empty
                            : OSDepotInfoRegex.Replace(char.ToUpper(os[0]) + os[1..], "OS"),
                        string.IsNullOrWhiteSpace(osarch) ? string.Empty : osarch + "-bit",
                        string.IsNullOrWhiteSpace(language) ? string.Empty : char.ToUpper(language[0]) + language[1..]
                    }
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                    );

                    depotname = name + ' ' + formattedName;

                    if (uint.TryParse(depot["dlcappid"].Value, out var dlcId))
                    {
                        dlcs.Add(dlcId);
                        isDlc = true;

                        // Some DLC apps have a depot where dlcappid points to themselves (e.g. appid 776520),
                        // meaning the depot represents the DLC itself, so use the app name directly.
                        if (dlcId == appid) depotname = name;
                    }

                    depots.Add(new Depot(id, depotname, depotfromapp, isDlc));
                }
            }

            var listofdlc = kvInfo["extended"]["listofdlc"].Value;
            if (!string.IsNullOrEmpty(listofdlc))
            {
                var ids = listofdlc.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                foreach (var s in ids)
                    if (uint.TryParse(s, out var id))
                        dlcs.Add(id);
            }

            return new()
            {
                AppId = appid,
                Name = name,
                DLC = dlcs,
                Depot = depots,
                Type = type,
                Parent = parent
            };
        }
    }

    public class SteamService
    {
        private readonly CallbackManager manager;
        private readonly IProgress<string>? progress;
        private readonly SteamApps steamApps;
        private readonly SteamClient steamClient;
        private readonly SteamUser steamUser;
        private readonly CancellationToken token;
        private int attemptReconnect;
        private bool disposed;
        private bool loggedOn;

        public SteamService(IProgress<string>? progress = default, CancellationToken token = default)
        {
            this.progress = progress;
            this.token = token;

            steamClient = new SteamClient();

            manager = new CallbackManager(steamClient);
            steamApps = steamClient.GetHandler<SteamApps>() ??
                        throw new InvalidOperationException("SteamApps handler unavailable");

            steamUser = steamClient.GetHandler<SteamUser>() ??
                        throw new InvalidOperationException("SteamUser handler unavailable");

            manager.Subscribe<SteamClient.ConnectedCallback>(OnConnected);
            manager.Subscribe<SteamClient.DisconnectedCallback>(OnDisconnected);
            manager.Subscribe<SteamUser.LoggedOnCallback>(OnLoggedOn);
        }

        public async Task AnonymousLoginAsync()
        {
            progress?.Report("Connecting to Steam...");
            steamClient.Connect();
            while (!steamClient.IsConnected)
            {
                await manager.RunWaitCallbackAsync(token).ConfigureAwait(false);
            }
            progress?.Report("Logging in anonymously");
            steamUser.LogOnAnonymous();
            while (!loggedOn)
            {
                await manager.RunWaitCallbackAsync(token).ConfigureAwait(false);
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public async Task EnsureAnonymousLoggedOn()
        {
            if (!loggedOn)
            {
                progress?.Report("Connecting to Steam...");
                steamClient.Connect();
                while (!loggedOn)
                {
                    await manager.RunWaitCallbackAsync(token);
                }
            }
        }

        public async Task<PublishedFileDetails> GetPublishedFileDetails(uint appId, PublishedFileID pubFile)
        {
            var pubFileRequest = new CPublishedFile_GetDetails_Request { appid = appId };
            pubFileRequest.publishedfileids.Add(pubFile);

            var details = await new PublishedFile().GetDetails(pubFileRequest).ToTask().WaitAsync(token);

            return details.Body.publishedfiledetails.FirstOrDefault() ??
                    throw new Exception(
                $"EResult {(int)details.Result} ({details.Result}) while retrieving file details for pubfile {pubFile}.");
        }

        public async Task<SteamAppInfos> GetSteamAppInfoAsync(uint appid)
        {
            var request = new SteamApps.PICSRequest(appid);
            var result = await steamApps.PICSGetProductInfo(request, default).ToTask().WaitAsync(token);

            if (result.Results?.First()?.Apps.TryGetValue(appid, out var info) != null && info?.KeyValues != null)
            {
                return info.ToSteamAppInfo();
            }
            return new SteamAppInfos();
        }

        public async Task<IReadOnlyCollection<SteamAppInfos>> GetSteamAppInfoAsync(IReadOnlyCollection<uint> appids)
        {
            List<SteamAppInfos> steamAppInfos = [];
            var requests = appids.Select(x => new SteamApps.PICSRequest(x));
            var results = await steamApps.PICSGetProductInfo(requests, []).ToTask().WaitAsync(token);

            foreach (var result in results?.Results ?? [])
            {
                foreach (var info in result.Apps)
                {
                    steamAppInfos.Add(info.Value.ToSteamAppInfo());
                }
            }

            return steamAppInfos;
        }

        public async IAsyncEnumerable<SteamAppInfos> GetSteamAppInfoStreamAsync(IEnumerable<uint> appids)
        {
            var requests = appids.Distinct().Select(x => new SteamApps.PICSRequest(x));

            var job = steamApps.PICSGetProductInfo(requests, []).ToTask().WaitAsync(token);

            var results = await job;

            foreach (var result in results?.Results ?? [])
            {
                foreach (var info in result.Apps)
                {
                    yield return info.Value.ToSteamAppInfo();
                }
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposed)
                return;

            if (disposing)
            {
                steamClient?.Disconnect();
            }

            disposed = true;
        }

        private void OnConnected(SteamClient.ConnectedCallback callback)
        {
            attemptReconnect = 0;
            steamUser.LogOnAnonymous();
            progress?.Report("Logging in anonymously...");
        }

        private void OnDisconnected(SteamClient.DisconnectedCallback callback)
        {
            if (!callback.UserInitiated)
            {
                if (attemptReconnect == 3) throw new Exception("Cannot connect to steam network!");

                attemptReconnect++;
                steamClient.Connect();
                loggedOn = false;
            }

            loggedOn = false;
        }

        private void OnLoggedOn(SteamUser.LoggedOnCallback callback)
        {
            if (callback.Result != EResult.OK) return;
            progress?.Report("Logged on");
            loggedOn = true;
        }
    }

    #region Model

    public enum SteamAppInfoType
    {
        Unknown,
        Game,
        DLC,
        Application,
        Tool,
        Demo,
        Music,
        Video
    }

    public readonly record struct Depot(uint AppId, string? Name, uint DepotFromApp, bool IsDLC);

    public readonly record struct SteamAppInfos(
        uint AppId,
        string? Name,
        IReadOnlyCollection<Depot> Depot,
        IReadOnlyCollection<uint> DLC,
        uint Parent,
        SteamAppInfoType Type)
    {
        public bool HasParent => Parent != 0;
    }

    #endregion Model
}