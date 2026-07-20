using BV6Tools.Collections;
using BV6Tools.Messages;
using BV6Tools.Services;
using BV6Tools.Services.Database;
using BV6Tools.Services.Database.Models;
using BV6Tools.ViewModels.Dialogs;
using BV6Tools.ViewModels.Shared;
using BV6Tools.Views.Dialogs;
using CommunityToolkit.Mvvm.Messaging;
using SqlNado;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Windows.Data;
using System.Windows.Threading;
using Wpf.Ui;
using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui.Controls;

namespace BV6Tools.ViewModels.Pages.Shared
{
    public abstract partial class AppManagerPageViewModel : ObservableRecipient, INavigationAware
    {
        protected readonly DatabaseService databaseService;

        protected readonly HashSetNotify<uint> dirtyGames = [];
        protected readonly GameService gameService;

        protected readonly ISettingsService settingsService;
        protected Task? _initializeTask;

        private readonly Dictionary<AppViewModel, NotifyCollectionChangedEventHandler> _handlers = [];
        private readonly HashSet<AppViewModel> _pendingGames = [];
        private readonly IContentDialogService contentDialogService;
        private readonly ISnackbarService snackbarService;
        private readonly Dictionary<uint, AppViewModel.Snapshot> snapshot = [];
        private EventHandler? _searchDebounceHandler;

        private DispatcherTimer? _searchDebounceTimer;

        private List<GameDb>? gameDbs;

        private List<ItemDb>? itemDbs;

        protected AppManagerPageViewModel(IContentDialogService contentDialogService, GameService gameService,
            ISnackbarService snackbarService, DatabaseService databaseService, ISettingsService settingsService)
        {
            this.contentDialogService = contentDialogService;
            this.gameService = gameService;
            this.snackbarService = snackbarService;
            this.databaseService = databaseService;
            this.settingsService = settingsService;

            Games.CollectionChanged += OnGamesCollectionChanged;
            GamesView = CollectionViewSource.GetDefaultView(Games);
            GamesView.Filter = o => o is AppViewModel app && FilterApp(app, SearchText);
            GamesView.SortDescriptions.Add(new SortDescription(nameof(AppViewModel.Name), ListSortDirection.Ascending));
            GamesView.SortDescriptions.Add(new SortDescription(nameof(AppViewModel.AppId), ListSortDirection.Ascending));

            dirtyGames.OnDirtyChanged += OnDirtyChanged;

            LoadDB();
        }

        [ObservableProperty]
        public partial bool CancelVisible { get; set; }

        [ObservableProperty]
        public partial double CurrentProgress { get; set; }

        public ObservableDictionary<uint, AppViewModel> Games { get; set; } = [];

        public ICollectionView GamesView { get; }

        [ObservableProperty]
        public partial bool IsInitialized { get; set; }

        [ObservableProperty]
        public partial bool IsProgressBarIndeterminate { get; set; }

        [ObservableProperty]
        public partial bool IsProgressBarVisible { get; set; }

        [ObservableProperty]
        public partial double MaxProgress { get; set; }

        [ObservableProperty]
        public partial string ProgressText { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string SearchText { get; set; } = string.Empty;

        protected abstract string ManagerType { get; }

        public Task OnNavigatedFromAsync() => Task.CompletedTask;

        public virtual Task OnNavigatedToAsync() => _initializeTask ??= InitializeViewModel();

        protected static bool? GetSelectAllState(ICollectionView? view)
        {
            var items = view?.Cast<AppViewModel>() ?? [];
            var allEnabled = items.All(x => x.IsEnabled);
            var noneEnabled = items.All(x => !x.IsEnabled);
            return allEnabled ? true : noneEnabled ? false : null;
        }

        protected abstract ObservableDictionary<uint, AppViewModel> GetAppItemsCollection(AppViewModel app);

        protected abstract ICollectionView? GetItemsView(AppViewModel app);

        protected abstract bool? GetSelectAllValue(AppViewModel app);

        protected async Task InitializeApp(GameDb appDb, Dictionary<uint, ApplistCacheDb> caches, Dictionary<uint, List<ItemDb>> ItemsByAppId)
        {
            if (snapshot.ContainsKey(appDb.AppID)) return;
            string? name = caches.TryGetValue(appDb.AppID, out var cache) ? cache.Name : null;
            uint appid = appDb.AppID;
            bool isEnabled = gameService.AppidsDb.Contains(appDb.AppID);

            List<AppViewModel.AppSnapshot> itemsSnapshot = [];

            ProgressText = $"Processing {appid}";
            CurrentProgress++;

            if (ItemsByAppId.TryGetValue(appDb.AppID, out var itemsDb))
            {
                foreach (var itemDb in itemsDb)
                {
                    string? itemName = caches.TryGetValue(itemDb.AppID, out cache) ? cache.Name : null;
                    itemsSnapshot.Add(new AppViewModel.AppSnapshot(itemDb.AppID, itemName, gameService.AppidsDb.Contains(itemDb.AppID)));
                }
            }

            var snap = new AppViewModel.Snapshot(appDb.AppID, name, gameService.AppidsDb.Contains(appDb.AppID), [.. itemsSnapshot]);
            snapshot[appid] = snap;

            var app = gameService.GetOrAddApp(appid, out bool exists, name, isEnabled, caches: caches);

            SetItemsView(app);
            gameService.Subscribe(app.AppId, OnAppChanged);

            var childCollection = GetAppItemsCollection(app);

            int i = 0;

            foreach (var item in itemsSnapshot)
            {
                childCollection[item.AppId] = gameService.GetOrAddApp(item.AppId, out bool itemExists, item.Name, item.IsEnabled, appid, caches);
                if (itemExists) exists = true;
                if (++i % 200 == 0)
                {
                    await Dispatcher.Yield();
                }
            }
            RefreshSelectAll(app);

            SubscribeItemsCollection(app, childCollection);

            if (exists && !app.EqualsSnapshot(snap, childCollection))
            {
                dirtyGames.Add(appid);
            }

            Games[app.AppId] = app;
        }

        protected virtual async Task InitializeViewModel()
        {
            IsProgressBarVisible = true;
            IsProgressBarIndeterminate = true;
            ProgressText = "Getting list from db";

            IsProgressBarIndeterminate = false;

            try
            {
                LoadDB();
                MaxProgress = gameDbs.Count;

                var ItemsByAppId = itemDbs.GroupBy(d => d.ParentAppID).ToDictionary(g => g.Key, g => g.ToList());

                var caches = databaseService.GetAppsCache(gameDbs.Select(x => x.AppID).Union(itemDbs.Select(x => x.AppID)));

                await InitializeApp(gameDbs.First(), caches, ItemsByAppId);
                OnFirstItemLoaded();

                foreach (var appDb in gameDbs.Skip(1))
                {
                    await InitializeApp(appDb, caches, ItemsByAppId);
                    await Dispatcher.Yield();
                }
            }
            catch (Exception) { }
            finally
            {
                IsInitialized = true;
                IsProgressBarVisible = false;
            }
        }

        protected override void OnActivated()
        {
            Messenger.Register<SaveMessage>(this, OnSaveMessage);
            Messenger.Register<ProfileChangedMessage>(this, OnProfileChangedMessage);
        }

        protected void OnAppChanged(AppViewModel app, bool isItemChanged = false)
        {
            if (!snapshot.ContainsKey(app.AppId) && !Games.ContainsKey(app.AppId))
            {
                return;
            }
            if (snapshot.TryGetValue(app.AppId, out var snap)
                && Games.ContainsKey(app.AppId)
                && app.EqualsSnapshot(snap, GetAppItemsCollection(app)))
            {
                dirtyGames.Remove(app.AppId);
            }
            else
            {
                dirtyGames.Add(app.AppId);
            }

            if (isItemChanged)
            {
                RefreshSelectAll(app);
            }
        }

        protected virtual void OnDirtyChanged()
        {
            SaveCommand.NotifyCanExecuteChanged();
            UndoCommand.NotifyCanExecuteChanged();
        }

        protected virtual void OnFirstItemLoaded()
        {
        }

        protected void OnGamesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (AppViewModel game in e.OldItems)
                {
                    var itemsCollection = GetAppItemsCollection(game);
                    UnsubscribeItemsCollection(game, itemsCollection);

                    if (snapshot.ContainsKey(game.AppId))
                    {
                        dirtyGames.Add(game.AppId);
                    }
                    else
                    {
                        dirtyGames.Remove(game.AppId);
                    }

                    gameService.Unsubscribe(game.AppId, OnAppChanged);
                }
            }

            if (e.NewItems != null)
            {
                foreach (AppViewModel game in e.NewItems)
                {
                    var itemsCollection = GetAppItemsCollection(game);

                    SubscribeItemsCollection(game, itemsCollection);

                    gameService.Subscribe(game.AppId, OnAppChanged);
                    SetItemsView(game);
                    OnAppChanged(game, false);
                }
            }
        }

        protected void OnItemsCollectionChanged(AppViewModel game, NotifyCollectionChangedEventArgs e)
        {
            var isEmpty = _pendingGames.Count == 0;
            _pendingGames.Add(game);

            if (isEmpty)
            {
                Dispatcher.CurrentDispatcher.InvokeAsync(() =>
                {
                    foreach (AppViewModel game in _pendingGames)
                    {
                        OnAppChanged(game, true);
                    }
                    _pendingGames.Clear();
                }, DispatcherPriority.Background);
            }
        }

        protected virtual void OnProfileChangedMessage(object r, ProfileChangedMessage m)
        {
            Games.CollectionChanged -= OnGamesCollectionChanged;
            Dictionary<uint, List<uint>> appidsToRelease = [];
            for (int i = Games.Count - 1; i >= 0; i--)
            {
                var app = Games[i];
                var itemsCollection = GetAppItemsCollection(app);

                gameService.Unsubscribe(app.AppId, OnAppChanged);
                Games.RemoveAt(i);
                UnsubscribeItemsCollection(app, itemsCollection);
                appidsToRelease.TryAdd(app.AppId, [.. itemsCollection.Select(x => x.AppId)]);
            }
            Games.CollectionChanged += OnGamesCollectionChanged;

            gameService.ReleaseApp(appidsToRelease);

            gameDbs = null;
            itemDbs = null;
            dirtyGames.Clear();
            snapshot.Clear();
            IsInitialized = false;

            _initializeTask = null;
        }

        [RelayCommand]
        protected virtual async Task OnRemoveAllAppItems(AppViewModel game)
        {
            var itemsCollection = GetAppItemsCollection(game);
            var itemsView = GetItemsView(game);

            var toRemove = (itemsView?.Cast<AppViewModel>() ?? []).ToList();
            var appidsToRemove = new HashSet<uint>(toRemove.Select(x => x.AppId));

            foreach (var item in toRemove)
            {
                itemsCollection.Remove(item);
            }

            gameService.ReleaseApp(appidsToRemove, game.AppId);

            itemsView?.Refresh();
        }

        [RelayCommand]
        protected virtual async Task OnRemoveAsync(AppViewModel app)
        {
            await RemoveAllAppItemsCommand.ExecuteAsync(app);

            Games.Remove(app);
            gameService.ReleaseApp(app.AppId);
        }

        [RelayCommand]
        protected virtual async Task OnRemoveItem(object parameter)
        {
            if (parameter is not object[] param) return;
            if (param[0] is not AppViewModel item) return;
            if (param[1] is not AppViewModel parent) return;

            var items = GetAppItemsCollection(parent);

            items.Remove(item);

            gameService.ReleaseApp(item.AppId, parent.AppId);
        }

        protected virtual void OnSaveMessage(object r, SaveMessage m)
        {
            if (dirtyGames.Count == 0) return;

            var dirtySnapshot = new HashSet<uint>(dirtyGames);
            dirtyGames.Clear();

            var db = databaseService.Database;

            var gamesToInsert = new List<GameDb>();
            var gamesToDelete = new List<uint>();
            var itemsToInsert = new List<ItemDb>();
            var itemsToDelete = new List<uint>();

            foreach (var appId in dirtySnapshot)
            {
                var hasSnap = snapshot.TryGetValue(appId, out var snap);
                Games.TryGetValue(appId, out var game);

                if (game == null)
                {
                    gamesToDelete.Add(appId);
                    if (hasSnap)
                    {
                        itemsToDelete.AddRange(snap.Items.Select(x => x.AppId));
                    }
                    continue;
                }

                if (!hasSnap)
                {
                    gamesToInsert.Add(new() { AppID = game.AppId, ManagerType = ManagerType, ProfileID = settingsService.Settings.ActiveProfileId });
                }

                var currentItems = GetAppItemsCollection(game);
                var currentItemIds = currentItems.Select(x => x.AppId).ToHashSet();
                var snapItemIds = hasSnap ? snap.Items.Select(x => x.AppId).ToHashSet() : [];

                itemsToDelete.AddRange(snapItemIds.Where(id => !currentItemIds.Contains(id)));
                itemsToInsert.AddRange(currentItems
                    .Where(x => !snapItemIds.Contains(x.AppId))
                    .Select(x => new ItemDb
                    {
                        AppID = x.AppId,
                        ParentAppID = game.AppId,
                        ManagerType = ManagerType,
                        ProfileID = settingsService.Settings.ActiveProfileId
                    }));
            }

            if (gamesToInsert.Count == 0 && gamesToDelete.Count == 0 &&
                itemsToInsert.Count == 0 && itemsToDelete.Count == 0)
            {
                foreach (var appId in dirtySnapshot)
                {
                    if (Games.TryGetValue(appId, out var game))
                    {
                        snapshot[appId] = game.ToSnapshot(GetAppItemsCollection(game));
                    }
                }
                return;
            }

            var gameTable = db.SynchronizeSchema<GameDb>().Name;
            var itemTable = db.SynchronizeSchema<ItemDb>().Name;

            db.BeginTransaction();
            try
            {
                if (gamesToDelete.Count > 0)
                {
                    var ids = string.Join(",", gamesToDelete);
                    db.ExecuteNonQuery(
                        $"DELETE FROM {gameTable} WHERE ProfileID = ? AND ManagerType = ? AND AppID IN ({ids})",
                        settingsService.Settings.ActiveProfileId, ManagerType);
                }

                if (itemsToDelete.Count > 0)
                {
                    var ids = string.Join(",", itemsToDelete);
                    db.ExecuteNonQuery(
                        $"DELETE FROM {itemTable} WHERE ProfileID = ? AND ManagerType = ? AND AppID IN ({ids})",
                        settingsService.Settings.ActiveProfileId, ManagerType);
                }

                SQLiteSaveOptions upsertOptions = new(db)
                {
                    ConflictResolution = SQLiteConflictResolution.Replace
                };

                if (gamesToInsert.Count > 0)
                {
                    db.Save(gamesToInsert, upsertOptions);
                }

                if (itemsToInsert.Count > 0)
                {
                    db.Save(itemsToInsert, upsertOptions);
                }

                db.Commit();

                foreach (var appId in dirtySnapshot)
                {
                    if (Games.TryGetValue(appId, out var game))
                    {
                        snapshot[appId] = game.ToSnapshot(GetAppItemsCollection(game));
                    }
                    else
                    {
                        snapshot.Remove(appId);
                    }
                }
            }
            catch (Exception ex)
            {
                db.Rollback();

                foreach (var id in dirtySnapshot)
                {
                    dirtyGames.Add(id);
                }

                snackbarService.Show("Error", ex.Message, ControlAppearance.Danger,
                    new SymbolIcon(SymbolRegular.ErrorCircle24), default);
            }
        }

        [RelayCommand]
        protected void OnSelectAllChecked(object parameter)
        {
            if (parameter is not AppViewModel game) return;

            var value = GetSelectAllValue(game) != true;

            foreach (var item in GetItemsView(game)?.Cast<AppViewModel>() ?? [])
            {
                item.IsEnabled = value;
            }
        }

        [RelayCommand]
        protected void OnTextSearchItemChanged(object parameter)
        {
            if (parameter is not object[] param) return;
            if (param[0] is not AppViewModel app) return;
            if (param[1] is not string query) return;

            DebounceSearch(() =>
            {
                var childView = GetItemsView(app);
                if (childView == null) return;

                childView.Filter = o => o is AppViewModel item && FilterApp(item, query);
                childView.Refresh();
                RefreshSelectAll(app);
            });
        }

        protected abstract void RefreshSelectAll(AppViewModel app);

        [RelayCommand(CanExecute = nameof(IsDirty))]
        protected virtual async Task Save()
        {
            Messenger.Send(new SaveMessage());
        }

        protected abstract void SetItemsView(AppViewModel app);

        protected abstract void SetSelectAllValue(AppViewModel app, bool? value);

        [RelayCommand(CanExecute = nameof(IsDirty))]
        protected virtual async Task Undo()
        {
            Games.CollectionChanged -= OnGamesCollectionChanged;
            dirtyGames.Clear();

            Dictionary<uint, List<uint>> appidsToRelease = [];

            for (int i = Games.Count - 1; i >= 0; i--)
            {
                var app = Games[i];
                var itemsCollection = GetAppItemsCollection(app);

                gameService.Unsubscribe(app.AppId, OnAppChanged);

                if (snapshot.TryGetValue(app.AppId, out var snap))
                {
                    app.Name = snap.Name;
                    app.IsEnabled = snap.IsEnabled;
                    RestoreChildren(itemsCollection, snap.Items, app.AppId);
                    RefreshSelectAll(app);
                    gameService.Subscribe(app.AppId, OnAppChanged);
                }
                else
                {
                    Games.RemoveAt(i);
                    UnsubscribeItemsCollection(app, itemsCollection);
                    appidsToRelease.TryAdd(app.AppId, [.. itemsCollection.Select(x => x.AppId)]);
                }
            }

            foreach (var backup in snapshot.Values.Where(b => !Games.ContainsKey(b.AppId)))
            {
                var app = gameService.GetOrAddApp(backup.AppId, backup.Name, backup.IsEnabled);
                app.Name = backup.Name;
                app.IsEnabled = backup.IsEnabled;

                var itemsCollection = GetAppItemsCollection(app);
                SubscribeItemsCollection(app, itemsCollection);

                SetItemsView(app);
                RestoreChildren(itemsCollection, backup.Items, backup.AppId);
                RefreshSelectAll(app);

                Games[backup.AppId] = app;
                gameService.Subscribe(app.AppId, OnAppChanged);
            }
            gameService.ReleaseApp(appidsToRelease);

            await Dispatcher.Yield();

            Games.CollectionChanged += OnGamesCollectionChanged;

            GamesView.Refresh();
        }

        private static bool FilterApp(AppViewModel app, string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return true;

            var q = query.Trim().ToLower();
            return (app.Name?.Contains(q, StringComparison.CurrentCultureIgnoreCase) ?? false) || app.AppId.ToString().Contains(q);
        }

        private void DebounceSearch(Action action)
        {
            _searchDebounceTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _searchDebounceTimer.Stop();

            if (_searchDebounceHandler != null)
                _searchDebounceTimer.Tick -= _searchDebounceHandler;

            _searchDebounceHandler = (_, _) =>
            {
                _searchDebounceTimer.Stop();
                action();
            };

            _searchDebounceTimer.Tick += _searchDebounceHandler;
            _searchDebounceTimer.Start();
        }

        private bool IsDirty()
        {
            return dirtyGames.Count > 0;
        }

        [MemberNotNull(nameof(gameDbs), nameof(itemDbs))]
        private void LoadDB()
        {
            gameDbs ??= [.. databaseService.Database.Load<GameDb>(
                "WHERE ProfileID = ? AND ManagerType = ?", null, settingsService.Settings.ActiveProfileId, ManagerType)];

            itemDbs ??= [.. databaseService.Database.Load<ItemDb>(
                "WHERE ProfileID = ? AND ManagerType = ?", null, settingsService.Settings.ActiveProfileId, ManagerType)];
        }

        [RelayCommand]
        private async Task OnEditAsync(object parameter)
        {
            if (parameter is not AppViewModel app) return;
            var dataDialog = new EditDialogViewModel
            {
                Title = "Edit",
                PlaceHolder = "Game name",
                Name = app.Name,
                AppId = app.AppId
            };
            var dialog = new EditDialog(dataDialog);
            var result = await contentDialogService.ShowAsync(dialog, CancellationToken.None);
            if (!result.HasFlag(ContentDialogResult.Primary))
                return;

            var newAppId = dataDialog.AppId ?? 0;
            var enable = app.IsEnabled;
            string? name = string.IsNullOrWhiteSpace(dataDialog.Name) ? null : dataDialog.Name;

            if (app.AppId != newAppId)
            {
                var childCollection = GetAppItemsCollection(app);
                Games.Remove(app);

                if (!Games.TryGetValue(newAppId, out app))
                {
                    app = gameService.GetOrAddApp(newAppId, name, enable);
                    SetItemsView(app);
                    Games[app.AppId] = app;
                }

                foreach (var item in childCollection.ToList())
                {
                    if (!childCollection.ContainsKey(item.AppId))
                    {
                        var child = gameService.GetOrAddApp(item.AppId, item.Name, item.IsEnabled);
                        childCollection[child.AppId] = child;
                    }
                }
            }

            app.Name = name;

            GamesView.Refresh();
        }

        [RelayCommand]
        private async Task OnEditItemAsync(object parameter)
        {
            if (parameter is not object[] param) return;
            if (param[0] is not AppViewModel item) return;
            if (param[1] is not AppViewModel app) return;

            var dataDialog = new EditDialogViewModel
            {
                ParentEdit = true,
                Title = "Edit App",
                PlaceHolder = "Name",
                Name = item.Name,
                AppId = item.AppId,
                SelectedGame = app,
                Games = Games.AsEnumerable()
            };
            var dialog = new EditDialog(dataDialog);
            var result = await contentDialogService.ShowAsync(dialog, CancellationToken.None);
            if (result != ContentDialogResult.Primary)
                return;

            var newAppId = dataDialog.AppId ?? item.AppId;
            string? name = string.IsNullOrWhiteSpace(dataDialog.Name) ? null : dataDialog.Name;

            if (item.AppId != newAppId)
            {
                var items = GetAppItemsCollection(app);
                items.Remove(item);
                gameService.ReleaseApp(item.AppId, app.AppId);

                item = gameService.GetOrAddApp(newAppId, name, item.IsEnabled);

                item.Name = name;
                items[newAppId] = item;
            }
            else
            {
                item.Name = name;
            }

            if (dataDialog.SelectedGame.AppId != app.AppId)
            {
                var items = GetAppItemsCollection(app);
                items.Remove(item);

                items = GetAppItemsCollection(dataDialog.SelectedGame);

                if (!items.ContainsKey(item.AppId))
                {
                    items[item.AppId] = gameService.GetOrAddApp(item.AppId, item.Name, item.IsEnabled, dataDialog.SelectedGame.AppId);
                }
            }
        }

        partial void OnSearchTextChanged(string value)
        {
            if (!IsInitialized) return;
            DebounceSearch(() => GamesView?.Refresh());
        }

        private void RestoreChildren(ObservableDictionary<uint, AppViewModel> collection, IEnumerable<AppViewModel.AppSnapshot> snapItems, uint parent)
        {
            var origIds = snapItems.Select(x => x.AppId).ToHashSet();
            List<uint> toRelease = [];
            foreach (var item in collection.ToList())
            {
                if (!origIds.Contains(item.AppId))
                {
                    collection.Remove(item);
                    toRelease.Add(item.AppId);
                }
            }
            foreach (var item in snapItems)
            {
                if (!collection.TryGetValue(item.AppId, out var existing))
                {
                    collection[item.AppId] = gameService.GetOrAddApp(item.AppId, item.Name, item.IsEnabled, parent);
                }
                else
                {
                    existing.Name = item.Name;
                    existing.IsEnabled = item.IsEnabled;
                }
            }
            gameService.ReleaseApp(toRelease, parent);
        }

        private void SubscribeItemsCollection(AppViewModel parentApp, ObservableDictionary<uint, AppViewModel> itemsCollection)
        {
            UnsubscribeItemsCollection(parentApp, itemsCollection);

            void handler(object? s, NotifyCollectionChangedEventArgs e) => OnItemsCollectionChanged(parentApp, e);
            _handlers[parentApp] = handler;
            itemsCollection.CollectionChanged += handler;
        }

        private void UnsubscribeItemsCollection(AppViewModel parentApp, ObservableDictionary<uint, AppViewModel> itemsCollection)
        {
            if (_handlers.TryGetValue(parentApp, out var handler))
            {
                itemsCollection.CollectionChanged -= handler;
                _handlers.Remove(parentApp);
            }
        }
    }
}