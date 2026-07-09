using BV6Tools.Collections;
using BV6Tools.Extensions;
using BV6Tools.Messages;
using BV6Tools.Models;
using BV6Tools.Services;
using BV6Tools.Services.Database;
using BV6Tools.Services.Database.Models;
using BV6Tools.Services.Injector;
using BV6Tools.Services.Steam;
using BV6Tools.ViewModels.Pages.Shared;
using BV6Tools.ViewModels.Shared;
using CommunityToolkit.Mvvm.Messaging;
using STCommon;
using SteamCommon;
using SteamKit2;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Data;
using System.Windows.Threading;
using Wpf.Ui;
using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui.Controls;

namespace BV6Tools.ViewModels.Pages;

public partial class DepotPageViewModel : AppManagerPageViewModel, INavigationAware
{
    private readonly IContentDialogService contentDialogService;

    private readonly HashSet<uint> excludedAppIds =
    [
        480,
        228980,
        241100
    ];

    private readonly HashSet<uint> excludedSharedDepot =
    [
        228990,
        229000
    ];

    private readonly InjectorService injectorService;
    private readonly ILoggerService logger;
    private readonly ISettingsService settings;
    private readonly ISnackbarService snackbarService;

    public DepotPageViewModel(IContentDialogService contentDialogService, ISnackbarService snackbarService, ILoggerService logger,
        ISettingsService settings, GameService gameService, DatabaseService databaseService, InjectorService injectorService) : base(contentDialogService, gameService, snackbarService, databaseService, settings)
    {
        this.contentDialogService = contentDialogService;
        this.snackbarService = snackbarService;
        this.logger = logger;
        this.settings = settings;
        this.injectorService = injectorService;
        IsActive = true;
    }

    [GeneratedRegex(@"(?<appid>\d{3,})")]
    public partial Regex AppIdFileRegex { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(UndoCommand))]
    public partial bool FetchOnlineDepotInfo { get; set; }

    [GeneratedRegex(@"(?<depotid>\d+)_(?<manifestid>\d+)\.manifest$")]
    public partial Regex ManifestIDFileRegex { get; }

    public AppSettings Settings => settings.Settings;

    protected override string ManagerType => "Depot";

    protected override ObservableDictionary<uint, AppViewModel> GetAppItemsCollection(AppViewModel app) => app.Depot;

    protected override ICollectionView? GetItemsView(AppViewModel app) => app.DepotView;

    protected override bool? GetSelectAllValue(AppViewModel app) => app.SelectAllDepot;

    protected override async Task InitializeViewModel()
    {
        FetchOnlineDepotInfo = settings.Settings.FetchOnlineDepotInfo;
        await base.InitializeViewModel();
        await RefreshDepotStatus();
    }

    protected override void OnActivated()
    {
        base.OnActivated();
        Messenger.Register<LuaAddedMessage>(this, (s, m) =>
        {
            ProcessLua(m.LuaData, m.AppId, m.Name);
        });
    }

    protected override void OnProfileChangedMessage(object r, ProfileChangedMessage m)
    {
        base.OnProfileChangedMessage(r, m);
        Messenger.Send(new NavigationPageBadgeMessage(nameof(DepotPageViewModel), dirtyGames.Count));
    }

    protected override void OnSaveMessage(object r, SaveMessage m)
    {
        base.OnSaveMessage(r, m);
        Messenger.Send(new NavigationPageBadgeMessage(nameof(DepotPageViewModel), dirtyGames.Count));
    }

    protected override void RefreshSelectAll(AppViewModel app) => app.SelectAllDepot = GetSelectAllState(app.DepotView);

    protected override Task Save()
    {
        if (settings.Settings.FetchOnlineDepotInfo != FetchOnlineDepotInfo)
        {
            settings.Save((s) =>
            {
                s.FetchOnlineDepotInfo = FetchOnlineDepotInfo;
            });
        }
        return base.Save();
    }

    protected override void SetItemsView(AppViewModel app)
    {
        app.DepotView = CollectionViewSource.GetDefaultView(app.Depot);
    }

    protected override void SetSelectAllValue(AppViewModel app, bool? value) => app.SelectAllDepot = value;

    protected override Task Undo()
    {
        FetchOnlineDepotInfo = settings.Settings.FetchOnlineDepotInfo;
        return base.Undo();
    }

    private static void Cache(SteamAppInfos app, Dictionary<uint, string> result)
    {
        result[app.AppId] = app.Name ?? string.Empty;

        foreach (var depot in app.Depot.Where(d => d.DepotFromApp == 0))
            result[depot.AppId] = depot.Name ?? string.Empty;
    }

    private static void FindMissing(SteamAppInfos app, HashSet<uint> missing)
    {
        // if app is not dlc and has depot, treat it as parent. Example (soundpad demo)
        var isParent = app.Depot.Count != 0 && app.Type != SteamAppInfoType.DLC;

        if (!isParent && app.HasParent)
        {
            missing.Add(app.Parent);
        }

        foreach (var depot in app.Depot.Where(d => d.DepotFromApp != 0))
        {
            missing.Add(depot.DepotFromApp);
        }
    }

    private async Task<Dictionary<uint, string>> GetSteamAppInfoAsCacheAsync(
        HashSet<uint> appids,
        CancellationToken token)
    {
        ProgressText = "Fetching depot info";

        var progress = new Progress<string>(msg => ProgressText = msg);
        var steam = new SteamService(progress, token);
        await steam.EnsureAnonymousLoggedOn();

        var result = new Dictionary<uint, string>();

        var fetched = await steam.GetSteamAppInfoAsync(appids);
        token.ThrowIfCancellationRequested();

        var missing = new HashSet<uint>();
        foreach (var app in fetched)
        {
            Cache(app, result);
            FindMissing(app, missing);
        }

        missing.ExceptWith(result.Keys);
        if (missing.Count > 0)
        {
            var rest = await steam.GetSteamAppInfoAsync(missing);
            token.ThrowIfCancellationRequested();

            foreach (var app in rest)
                Cache(app, result);
        }

        return result;
    }

    [RelayCommand]
    private void OnCancelTask()
    {
        RefreshGameCommand.Cancel();
        ScanCommand.Cancel();
        CancelVisible = false;
    }

    [RelayCommand]
    private async Task OnRefreshGameAsync(object sender, CancellationToken token)
    {
        if (sender is not AppViewModel app) return;

        CancelVisible = true;
        IsProgressBarVisible = true;
        IsProgressBarIndeterminate = true;
        ProgressText = "Fetching App Info...";

        try
        {
            var appidToResolve = app.Depot.Select(x => x.AppId).ToHashSet();
            appidToResolve.Add(app.AppId);

            var cacheByAppId = await GetSteamAppInfoAsCacheAsync(appidToResolve, token);
            if (cacheByAppId.TryGetValue(app.AppId, out var name))
            {
                app.Name = name;
            }

            foreach (var depot in app.Depot)
            {
                if (cacheByAppId.TryGetValue(depot.AppId, out name))
                {
                    depot.Name = name;
                }
                await Dispatcher.Yield();
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex);
            snackbarService.Show("Error", ex.Message, ControlAppearance.Danger,
                new SymbolIcon(SymbolRegular.Warning16), TimeSpan.FromSeconds(3));
        }
        finally
        {
            IsProgressBarVisible = false;
            CancelVisible = false;
            GamesView.Refresh();
        }
    }
    protected override void OnDirtyChanged()
    {
        Messenger.Send(new NavigationPageBadgeMessage(nameof(DepotPageViewModel), dirtyGames.Count));
        base.OnDirtyChanged();
    }
    [RelayCommand]
    private void OpenLibrary(AppViewModel app)
    {
        if (!injectorService.IsSteamRunning)
        {
            snackbarService.Show("Aborting", "Steam is not running!", ControlAppearance.Caution, default, default);
            return;
        }
        try
        {
            ProcessStartInfo processStartInfo = new()
            {
                FileName = "steam://open/games/details/" + app.AppId,
                UseShellExecute = true
            };
            Process.Start(processStartInfo);
        }
        catch (Exception ex)
        {
            logger.LogError(ex);
            snackbarService.Show("Error", ex.Message, ControlAppearance.Danger, null, default);
        }
    }

    private void ProcessLua(LuaData luaData, uint appid, string? name)
    {
        var appids = new HashSet<uint>(luaData.Manifest.Keys.Select(uint.Parse))
        {
            appid
        };

        var caches = databaseService.GetAppsCache(appids);

        if (!Games.TryGetValue(appid, out var game))
        {
            game = gameService.GetOrAddApp(appid, name, isEnabled: true, caches: caches);
            game.Name ??= name;
            Games[appid] = game;
        }

        foreach (var depot in luaData.Manifest)
        {
            uint depotId = uint.Parse(depot.Key);
            if (!game.Depot.TryGetValue(depotId, out var app))
            {
                app = gameService.GetOrAddApp(depotId, isEnabled: true, parentAppid: game.AppId, caches: caches);
                game.Depot[depotId] = app;
            }

            if (luaData.Appids.TryGetValue(depot.Key, out var luaAppId) && luaAppId is LuaAppIdWithKey appIdWithKey)
            {
                app.DecryptionKey = appIdWithKey.DecryptionKey ?? null;
            }
            app.ManifestMissing = depot.Value.ManifestID.IsNullOrEmpty();
        }
        RefreshSelectAll(game);

        OnAppChanged(game, true);
    }

    private async Task RefreshDepotStatus()
    {
        var scannedManifest = new HashSet<uint>();
        Dictionary<uint, string> scannedKey = [];

        try
        {
            var depotCachePath = Path.Combine(Steam.GetSteamDirectory(), "depotcache");
            foreach (var file in Directory.EnumerateFiles(depotCachePath, "*.manifest"))
            {
                var fileName = Path.GetFileNameWithoutExtension(file);
                var appIdPart = fileName.Split('_')[0];
                if (uint.TryParse(appIdPart, out var appid) && appid != 0) scannedManifest.Add(appid);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex.Message);
        }

        try
        {
            var vdfPath = Path.Combine(Settings.SteamPath, "config", "config.vdf");
            var stream = File.OpenRead(vdfPath);
            var kv = KeyValue.LoadAsText(vdfPath);
            var depotConfig = kv?["Software"]?["Valve"]?["Steam"]?["depots"];
            if (depotConfig != null)
            {
                foreach (var entry in depotConfig.Children)
                {
                    if (!uint.TryParse(entry.Name, out var appid)) continue;

                    var decryptionKey = entry["DecryptionKey"].ToString();
                    if (string.IsNullOrEmpty(decryptionKey)) continue;
                    scannedKey[appid] = decryptionKey;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex);
            snackbarService.Show("Error", ex.Message, ControlAppearance.Danger,
                new SymbolIcon(SymbolRegular.Warning16), TimeSpan.FromSeconds(3));
        }

        foreach (var game in Games)
        {
            foreach (var depot in game.Depot)
            {
                depot.ManifestMissing = !scannedManifest.Contains(depot.AppId);
                depot.DecryptionKey = scannedKey.GetValueOrDefault(depot.AppId) ?? string.Empty;
            }
        }
    }

    [RelayCommand]
    private async Task Scan(CancellationToken token)
    {
        IsProgressBarVisible = true;
        IsProgressBarIndeterminate = true;
        ProgressText = "Scanning available manifest";

        var scannedDepot = new HashSet<uint>();
        HashSet<uint> scannedManifest = [];
        Dictionary<uint, string> scannedKey = [];

        gameService.ApplyUpdateOnChanges(false);

        try
        {
            var depotCachePath = Path.Combine(Settings.SteamPath, "depotcache");
            foreach (var file in Directory.EnumerateFiles(depotCachePath, "*.manifest"))
            {
                var fileName = Path.GetFileNameWithoutExtension(file);
                var appIdPart = fileName.Split('_')[0];
                if (uint.TryParse(appIdPart, out var appid) && appid != 0) scannedManifest.Add(appid);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex);
            snackbarService.Show("Error", ex.Message, ControlAppearance.Danger,
                new SymbolIcon(SymbolRegular.Warning16), TimeSpan.FromSeconds(3));
        }

        try
        {
            var vdfPath = Path.Combine(Settings.SteamPath, "config", "config.vdf");
            using var fs = new FileStream(vdfPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var kv = new KeyValue();
            kv.ReadAsText(fs);
            var depotConfig = kv?["Software"]?["Valve"]?["Steam"]?["depots"];
            if (depotConfig != null)
            {
                foreach (var entry in depotConfig.Children)
                {
                    if (!uint.TryParse(entry.Name, out var appid)) continue;

                    var decryptionKey = entry["DecryptionKey"].ToString();
                    if (string.IsNullOrEmpty(decryptionKey)) continue;
                    scannedKey[appid] = decryptionKey;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex);
            snackbarService.Show("Error", ex.Message, ControlAppearance.Danger,
                new SymbolIcon(SymbolRegular.Warning16), TimeSpan.FromSeconds(3));
        }

        foreach (var appid in scannedManifest) scannedDepot.Add(appid);

        foreach (var appid in scannedKey) scannedDepot.Add(appid.Key);
        MaxProgress = scannedDepot.Count;

        try
        {
            CancelVisible = true;

            var predictParentAppid = new HashSet<uint>();

            foreach (var appid in scannedDepot)
            {
                predictParentAppid.Add(appid - appid % 10);
            }

            var caches = databaseService.GetAppsCache(scannedDepot.Union(predictParentAppid));

            if (FetchOnlineDepotInfo)
            {
                var appidToResolve = predictParentAppid
                    .Union(scannedDepot)
                    .ToHashSet();

                var steamAppInfoCaches = await GetSteamAppInfoAsCacheAsync(appidToResolve, token);

                foreach (var (appid, name) in steamAppInfoCaches)
                {
                    caches[appid] = new ApplistCacheDb
                    {
                        AppID = appid,
                        Name = name
                    };
                }
            }

            IsProgressBarIndeterminate = false;

            foreach (var appid in scannedDepot)
            {
                token.ThrowIfCancellationRequested();
                CurrentProgress++;
                ProgressText = $"Resolving depot {appid} {CurrentProgress}/{MaxProgress}";

                var parent = appid - appid % 10;

                if (excludedAppIds.Contains(parent) || excludedSharedDepot.Contains(parent)) continue;

                string? gameName = null;

                Games.TryGetValue(parent, out var game);

                if (game == null)
                {
                    if (!caches.TryGetValue(parent, out var cache))
                    {
                        gameService.RefreshInstalledGames();
                        if (gameService.InstalledGames.TryGetValue(parent, out var name))
                        {
                            gameName = name;
                            caches[parent] = new ApplistCacheDb
                            {
                                AppID = parent,
                                Name = name
                            };
                        }
                    }
                    else
                    {
                        gameName = cache.Name;
                    }
                    game = gameService.GetOrAddApp(parent, gameName, caches: caches);
                    Games[parent] = game;
                }
                else
                {
                    if (FetchOnlineDepotInfo)
                    {
                        game.Name = caches.GetValueOrDefault(parent)?.Name ?? game.Name;
                    }
                    gameName = game.Name;
                }

                game.Depot.TryGetValue(appid, out var depot);

                if (depot == null)
                {
                    depot = gameService.GetOrAddApp(appid, gameName, parentAppid: game.AppId, caches: caches);
                    game.Depot[appid] = depot;
                }

                if (FetchOnlineDepotInfo)
                {
                    depot.Name = caches.GetValueOrDefault(depot.AppId)?.Name ?? game.Name;
                }

                depot.ManifestMissing = !scannedManifest.Contains(appid);
                depot.DecryptionKey = scannedKey.GetValueOrDefault(appid);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex);
            snackbarService.Show(
                "Error",
                ex.Message,
                ControlAppearance.Danger,
                new SymbolIcon(SymbolRegular.Warning16),
                TimeSpan.FromSeconds(3));
        }
        finally
        {
            IsProgressBarVisible = false;
            CurrentProgress = 0;
            ProgressText = string.Empty;
            CancelVisible = false;

            gameService.ApplyUpdateOnChanges(true);
            GamesView.Refresh();
        }
    }
}