using AppPathsCommon;
using BV6Tools.Collections;
using BV6Tools.Messages;
using BV6Tools.Services.Database;
using BV6Tools.Services.Database.Models;
using BV6Tools.ViewModels.Shared;
using CommunityToolkit.Mvvm.Messaging;
using SqlNado;
using STCommon;
using SteamCommon;
using System.Collections.Specialized;
using System.IO;
using System.Windows.Threading;

namespace BV6Tools.Services;

public partial class GameService : ObservableRecipient
{
    public const string UnknownName = "Unknown App";
    private readonly Dictionary<uint, List<uint>> _appsParent = [];
    private readonly Dictionary<uint, AppBaseline> _baseline = [];
    private readonly HashSet<uint> _dirtyApps = [];
    private readonly HashSet<uint> _parents = [];
    private readonly Dictionary<uint, bool> _pendingSubscriber = [];
    private readonly HashSet<uint> _removedApps = [];
    private readonly Dictionary<uint, HashSet<Action<AppViewModel, bool>>> _subscribers = [];
    private readonly DatabaseService databaseService;
    private readonly ISettingsService settingsService;
    private bool _updateChanges = true;

    public GameService(DatabaseService databaseService, ISettingsService settingsService)
    {
        this.databaseService = databaseService;
        this.settingsService = settingsService;

        try
        {
            InstalledGames = SteamGameScanner.GetInstalledGames(settingsService.Settings.SteamPath);
        }
        catch
        {
            InstalledGames = new Dictionary<uint, string>();
        }

        Apps.ItemPropertyChanged += OnAppPropertyChanged;
        Apps.CollectionChanged += OnAppsCollectionChanged;

        AppidsDb = [.. databaseService.Database.Load<AppDb>($"WHERE ProfileID = {settingsService.Settings.ActiveProfileId}").Select(x => x.AppID)];
        EnabledAppids = [.. AppidsDb];

        Profiles = [.. settingsService.Profiles.Select(x => new ProfileDbViewModel()
        {
            ProfileID = x.ProfileID,
            ProfileName = x.ProfileName
        })];
        ActiveProfile = Profiles.FirstOrDefault(x => x.ProfileID == settingsService.Settings.ActiveProfileId, Profiles.First());

        IsActive = true;
    }

    [ObservableProperty]
    public partial ProfileDbViewModel ActiveProfile { get; set; }

    public HashSet<uint> AppidsDb { get; private set; } = [];
    public ObservableDictionary<uint, AppViewModel> Apps { get; set; } = [];
    public HashSet<uint> EnabledAppids { get; private set; } = [];
    public IReadOnlyDictionary<uint, string> InstalledGames { get; private set; }
    public ObservableDictionary<int, ProfileDbViewModel> Profiles { get; set; }
    private Dictionary<uint, int> AppsRefCount { get; } = [];

    public void ApplyUpdate()
    {
        if (_pendingSubscriber.Count == 0) return;

        foreach (var (appId, isItemChanged) in _pendingSubscriber)
        {
            if (!Apps.TryGetValue(appId, out var app)) continue;
            if (!_subscribers.TryGetValue(appId, out var callbacks)) continue;
            foreach (var cb in callbacks)
            {
                cb(app, isItemChanged);
            }
        }
        _pendingSubscriber.Clear();
    }

    public void ApplyUpdateOnChanges(bool update)
    {
        _updateChanges = update;
        if (update)
        {
            ApplyUpdate();
        }
    }

    public AppViewModel GetOrAddApp(uint appId, out bool exists, string? name = default, bool isEnabled = default,
        uint? parentAppid = default, Dictionary<uint, ApplistCacheDb>? caches = default, bool addRefCount = true)
    {
        exists = false;
        if (!Apps.TryGetValue(appId, out var app))
        {
            app = new AppViewModel { AppId = appId, Name = name, IsEnabled = isEnabled };
            Apps[appId] = app;
            ApplistCacheDb? cache = null;
            caches?.TryGetValue(appId, out cache);

            if (cache != null)
            {
                _baseline[appId] = new AppBaseline { IsEnabled = AppidsDb.Contains(appId), Name = cache.Name };
            }
            else
            {
                _baseline[appId] = new AppBaseline { IsEnabled = AppidsDb.Contains(appId), Name = name };
                _dirtyApps.Add(appId);
            }
        }
        else
        {
            exists = true;
        }

        if (!parentAppid.HasValue)
        {
            _parents.Add(app.AppId);
        }
        else
        {
            AddParent(appId, parentAppid.Value);
        }

        if (addRefCount)
        {
            AppsRefCount[appId] = AppsRefCount.GetValueOrDefault(appId) + 1;
        }

        return app;
    }

    public AppViewModel GetOrAddApp(uint appId, string? name = default,
        bool isEnabled = default, uint? parentAppid = default, Dictionary<uint, ApplistCacheDb>? caches = default,
        bool addRefCount = true) => GetOrAddApp(appId, out _, name, isEnabled, parentAppid, caches, addRefCount);

    public void RefreshInstalledGames()
    {
        InstalledGames = SteamGameScanner.GetInstalledGames(settingsService.Settings.SteamPath);
    }

    public void ReleaseApp<TCollection>(Dictionary<uint, TCollection> appids)
        where TCollection : IEnumerable<uint>
    {
        foreach (var (appId, itemId) in appids)
        {
            ReleaseApp(itemId, appId);
            ReleaseApp(appId);
        }
    }

    public void ReleaseApp(IEnumerable<uint> appIds, uint? parentId = default)
    {
        var toRemove = new List<AppViewModel>();

        foreach (var appId in appIds)
        {
            if (parentId.HasValue && _appsParent.TryGetValue(appId, out var parents))
            {
                parents.Remove(parentId.Value);
                if (parents.Count == 0)
                {
                    _appsParent.Remove(appId);
                }
            }

            AppsRefCount[appId] = Math.Max(0, AppsRefCount.GetValueOrDefault(appId) - 1);
            var refCount = AppsRefCount[appId];

            if (refCount <= 0)
            {
                AppsRefCount.Remove(appId);
                if (Apps.TryGetValue(appId, out var app))
                {
                    toRemove.Add(app);
                }
            }
        }

        foreach (var app in toRemove)
        {
            EnabledAppids.Remove(app.AppId);
            Apps.Remove(app);
        }
    }

    public void ReleaseApp(uint appId, uint? parentId = default) => ReleaseApp([appId], parentId);

    public void Subscribe(uint appId, Action<AppViewModel, bool> callback)
    {
        if (!_subscribers.TryGetValue(appId, out var callbacks))
        {
            _subscribers[appId] = callbacks = [];
        }
        callbacks.Add(callback);
    }

    public void Unsubscribe(uint appId, Action<AppViewModel, bool> callback)
    {
        if (_subscribers.TryGetValue(appId, out var callbacks))
        {
            callbacks.Remove(callback);
        }
    }

    protected override void OnActivated()
    {
        Messenger.Register<SaveMessage>(this, OnSaveMessage);
        Messenger.Register<ProfileChangedMessage, string>(this, MessengerTokens.GameService, OnProfileChangedMessage);
    }

    private void AddParent(uint appId, uint gameId)
    {
        if (!_appsParent.TryGetValue(appId, out var parents))
        {
            _appsParent[appId] = parents = [];
        }

        parents.Add(gameId);
    }

    partial void OnActiveProfileChanged(ProfileDbViewModel oldValue, ProfileDbViewModel newValue)
    {
        if (oldValue != null)
        {
            string destinationPath = Path.Combine(AppPaths.LuaPath, $"appids_{oldValue.ProfileID}.lua");
            File.Delete(destinationPath);

            if (newValue != null)
            {
                settingsService.Save(s =>
                {
                    s.ActiveProfileId = newValue.ProfileID;
                });
                ResetState();
                destinationPath = Path.Combine(AppPaths.LuaPath, $"appids_{newValue.ProfileID}.lua");
                ST.SaveAppId(EnabledAppids.Order(), destinationPath);
                Messenger.Send(new ProfileChangedMessage(newValue));
            }
        }
    }

    private void OnSaveMessage(object? r, SaveMessage m)
    {
        if (_dirtyApps.Count == 0 && _removedApps.Count == 0) return;

        var db = databaseService.Database;

        var toUpsertEnabled = new List<AppDb>();
        var toDeleteIds = new List<uint>(_removedApps);
        var cacheUpsert = new List<ApplistCacheDb>();

        var caches = databaseService.GetAppsCache(_dirtyApps);

        foreach (var appId in _dirtyApps)
        {
            if (!Apps.TryGetValue(appId, out var app)) continue;

            if (app.IsEnabled)
            {
                toUpsertEnabled.Add(new() { AppID = appId, ProfileID = settingsService.Settings.ActiveProfileId });
            }
            else if (AppidsDb.Contains(appId))
            {
                toDeleteIds.Add(appId);
            }

            if (app.Name != null)
            {
                caches.TryGetValue(appId, out var cache);
                if (cache == null || cache.Name != app.Name)
                {
                    cacheUpsert.Add(new() { AppID = appId, Name = app.Name });
                }
            }
        }

        var dirtySnapshot = new HashSet<uint>(_dirtyApps);
        var removedSnapshot = new HashSet<uint>(_removedApps);
        _dirtyApps.Clear();
        _removedApps.Clear();

        var enabledAppsTable = databaseService.AppidsTableName;

        db.BeginTransaction();
        try
        {
            if (toDeleteIds.Count > 0)
            {
                var ids = string.Join(",", toDeleteIds);
                db.ExecuteNonQuery(
                    $"DELETE FROM {enabledAppsTable} WHERE ProfileID = {settingsService.Settings.ActiveProfileId} AND AppID IN ({ids})");
            }

            SQLiteSaveOptions upsertOptions = new(db)
            {
                ConflictResolution = SQLiteConflictResolution.Replace
            };

            if (toUpsertEnabled.Count > 0)
            {
                db.Save(toUpsertEnabled, upsertOptions);
            }

            if (cacheUpsert.Count > 0)
            {
                db.Save(cacheUpsert, upsertOptions);
            }

            db.Commit();
            AppidsDb = [.. EnabledAppids];
            foreach (var id in dirtySnapshot)
            {
                if (Apps.TryGetValue(id, out var app))
                {
                    _baseline[id] = new AppBaseline { IsEnabled = app.IsEnabled, Name = app.Name };
                }
            }
            string destinationPath = Path.Combine(AppPaths.LuaPath, $"appids_{settingsService.Settings.ActiveProfileId}.lua");
            ST.SaveAppId(EnabledAppids, destinationPath);
        }
        catch
        {
            db.Rollback();

            foreach (var id in dirtySnapshot)
            {
                _dirtyApps.Add(id);
            }
            foreach (var id in removedSnapshot)
            {
                _removedApps.Add(id);
            }
        }
    }

    private void OnAppPropertyChanged(AppViewModel app, string? propName)
    {
        if (propName == nameof(AppViewModel.SelectAllDLC)) return;
        if (propName == nameof(AppViewModel.SelectAllDepot)) return;
        if (propName == nameof(AppViewModel.ManifestMissing)) return;
        if (propName == nameof(AppViewModel.DecryptionKey)) return;

        if (app.IsEnabled)
        {
            EnabledAppids.Add(app.AppId);
        }
        else
        {
            EnabledAppids.Remove(app.AppId);
        }

        if (_baseline.TryGetValue(app.AppId, out var baseline))
        {
            bool isDirty = app.IsEnabled != baseline.IsEnabled
                        || app.Name != baseline.Name;

            if (isDirty)
            {
                _dirtyApps.Add(app.AppId);
            }
            else
            {
                _dirtyApps.Remove(app.AppId);
            }
        }

        if (_parents.Contains(app.AppId))
        {
            QueueApp(app.AppId, isItemsChanged: false);
        }

        if (_appsParent.TryGetValue(app.AppId, out var parents))
        {
            foreach (var p in parents)
            {
                QueueApp(p, isItemsChanged: true);
            }
        }
        ScheduleUpdate();
    }

    private void OnAppsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (AppViewModel game in e.NewItems)
            {
                if (game.IsEnabled)
                {
                    EnabledAppids.Add(game.AppId);
                }
                else
                {
                    EnabledAppids.Remove(game.AppId);
                }
            }
        }

        if (e.OldItems != null)
        {
            foreach (AppViewModel game in e.OldItems)
            {
                _dirtyApps.Remove(game.AppId);
                _baseline.Remove(game.AppId);
                if (AppidsDb.Contains(game.AppId))
                {
                    _removedApps.Add(game.AppId);
                }

                _parents.Remove(game.AppId);
            }
        }
    }
    private void ResetState()
    {
        AppidsDb = [.. databaseService.Database.Load<AppDb>($"WHERE ProfileID = {settingsService.Settings.ActiveProfileId}").Select(x => x.AppID)];
        EnabledAppids = [.. AppidsDb];
        Apps.Clear();
        AppsRefCount.Clear();
        _appsParent.Clear();
        _baseline.Clear();
        _dirtyApps.Clear();
        _parents.Clear();
        _pendingSubscriber.Clear();
        _removedApps.Clear();
        _subscribers.Clear();
    }
    private void OnProfileChangedMessage(object r, ProfileChangedMessage m)
    {
        ResetState();
        Messenger.Send(m);
    }

    private void QueueApp(uint appId, bool isItemsChanged)
    {
        if (!_subscribers.ContainsKey(appId)) return;

        _pendingSubscriber[appId] = _pendingSubscriber.GetValueOrDefault(appId) || isItemsChanged;
    }
    private void ScheduleUpdate()
    {
        if (!_updateChanges) return;
        Dispatcher.CurrentDispatcher.BeginInvoke(ApplyUpdate, DispatcherPriority.DataBind);
    }

    private struct AppBaseline
    {
        public bool IsEnabled;
        public string? Name;
    }
}