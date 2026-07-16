using AppPathsCommon;
using BV6Tools.Collections;
using BV6Tools.Messages;
using BV6Tools.Services;
using BV6Tools.Services.Database;
using BV6Tools.Services.Database.Models;
using BV6Tools.Services.Injector;
using BV6Tools.ViewModels.Shared;
using CommunityToolkit.Mvvm.Messaging;
using ImageCommon;
using SteamCommon;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using System.Windows.Threading;
using Wpf.Ui;
using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui.Controls;

namespace BV6Tools.ViewModels.Pages;

public partial class DashboardViewModel : ObservableRecipient, INavigationAware
{
    private readonly DatabaseService databaseService;
    private readonly GameService gameService;
    private readonly HttpClientService httpClientService;
    private readonly SemaphoreSlim imageLoadSemaphore = new(3);
    private readonly ConcurrentDictionary<GameViewModel, CancellationTokenSource> imageLoadTokens = new();
    private readonly InjectorManagerService injectorManagerService;
    private readonly InjectorService injectorService;
    private readonly ILoggerService logger;
    private readonly ISettingsService settingsService;
    private readonly ISnackbarService snackbarService;
    private Task? initializeTask;

    public DashboardViewModel(HttpClientService httpClientService, ISnackbarService snackbarService,
        ISettingsService settingsService, GameService gameService,
        DatabaseService databaseService, ILoggerService logger, InjectorService injectorService,
        InjectorManagerService injectorManagerService)
    {
        this.httpClientService = httpClientService;
        this.snackbarService = snackbarService;
        this.settingsService = settingsService;
        this.logger = logger;
        this.injectorService = injectorService;
        this.injectorManagerService = injectorManagerService;

        injectorService.IsSteamRunningChanged += SteamWatcher_IsSteamRunningChanged;
        IsSteamRunning = injectorService.IsSteamRunning;

        GamesView = CollectionViewSource.GetDefaultView(Games);
        AppsView = CollectionViewSource.GetDefaultView(Apps);
        AppsView.SortDescriptions.Add(new SortDescription("Name", ListSortDirection.Ascending));
        this.gameService = gameService;
        this.databaseService = databaseService;

        gameService.Apps.CollectionChanged += OnAppsCollectionChanged;
        gameService.Apps.ItemPropertyChanged += OnAppPropertyChanged;

        Profiles = gameService.Profiles;

        IsActive = true;
    }

    public ObservableDictionary<uint, AppViewModel> Apps { get; } = [];

    public ICollectionView AppsView { get; }

    public ObservableDictionary<uint, GameViewModel> Games { get; } = [];

    public ICollectionView GamesView { get; private set; }

    [ObservableProperty]
    public partial bool IsSteamRunning { get; set; }

    public ObservableCollection<ProfileDbViewModel> Profiles { get; set; }

    public ProfileDbViewModel SelectedProfile
    {
        get => gameService.ActiveProfile;
        set
        {
            if (value != null)
            {
                gameService.ActiveProfile = value;
            }
        }
    }

    public Task OnNavigatedFromAsync()
    {
        injectorService.StopWatcher();
        return Task.CompletedTask;
    }

    public Task OnNavigatedToAsync()
    {
        injectorService.StartWatcher();
        IsSteamRunning = injectorService.IsSteamRunning;
        return initializeTask ??= InitializeViewModel();
    }

    protected override void OnActivated()
    {
        Messenger.Register<AddedMessage, string>(this, MessengerTokens.Dashboard, OnAddedMessageHandler);
        Messenger.Register<ProfileChangedMessage>(this, OnProfileChangedMessage);
    }

    [RelayCommand]
    private void DownloadButtonGameCard(GameViewModel app)
    {
        Messenger.Send(new DownloadMessage(app.AppId, app.Name));
    }

    private async Task InitializeViewModel()
    {
        try
        {
            var lastFeaturedGames = databaseService.GetFeaturedCache();
            var lastUpdate = lastFeaturedGames.CachedAt;

            if (lastUpdate?.Date != DateTime.UtcNow.Date || lastFeaturedGames.Games.Count == 0)
            {
                RefreshFeaturedCommand.Execute(default);
            }
            else
            {
                foreach (var cache in lastFeaturedGames.Games)
                {
                    var game = new GameViewModel
                    {
                        AppId = cache.AppID,
                        Name = cache.Name
                    };
                    Games[game.AppId] = game;
                }
            }

            var caches = databaseService.GetAppsCache(gameService.EnabledAppids);

            int i = 0;
            foreach (var appid in gameService.EnabledAppids)
            {
                string? name = caches.TryGetValue(appid, out ApplistCacheDb? value) ? value.Name : null;
                Apps[appid] = gameService.GetOrAddApp(appid, name, true, addRefCount: false);

                if (++i % 100 == 0)
                {
                    await Dispatcher.Yield();
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex);
            snackbarService.Show("Error", ex.Message, ControlAppearance.Danger,
                new SymbolIcon(SymbolRegular.Warning16), default);
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task LoadImageAsync(GameViewModel game)
    {
        if (game.ImageSource != null) return;

        if (imageLoadTokens.TryRemove(game, out var oldCts))
        {
            oldCts.Cancel();
            oldCts.Dispose();
        }

        var cts = new CancellationTokenSource();
        imageLoadTokens[game] = cts;
        var token = cts.Token;

        bool isAcquired = false;

        try
        {
            await imageLoadSemaphore.WaitAsync(token).ConfigureAwait(false);
            isAcquired = true;

            token.ThrowIfCancellationRequested();

            Directory.CreateDirectory(AppPaths.ImagesPath);
            var imagePath = Path.Combine(AppPaths.ImagesPath, $"{game.AppId}.jpg");

            if (!File.Exists(imagePath))
            {
                token.ThrowIfCancellationRequested();

                var app = await SteamStore.GetAppDetailsAsync(game.AppId, token).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("Failed to fetch app details");

                token.ThrowIfCancellationRequested();

                var bytes = await httpClientService.DownloadDataAsync(app.HeaderImage, token).ConfigureAwait(false);

                token.ThrowIfCancellationRequested();

                await File.WriteAllBytesAsync(imagePath, bytes, token).ConfigureAwait(false);
            }

            token.ThrowIfCancellationRequested();

            var image = await Task.Run(
                () => ImageUtilites.LoadImageFromFile(imagePath),
                token
            ).ConfigureAwait(false);

            await Application.Current.Dispatcher.InvokeAsync(
                () => game.ImageSource = image,
                DispatcherPriority.Background
            );
        }
        catch (OperationCanceledException)
        {
            logger.LogError($"Image load cancelled for {game.AppId}");
        }
        catch (Exception ex)
        {
            logger.LogError($"Error loading image for {game.AppId}: ", ex);
            await Application.Current.Dispatcher.InvokeAsync(
                () => game.ImageSource = ImageUtilites.CreateQuestionMarkImage()
            );
        }
        finally
        {
            if (isAcquired)
            {
                imageLoadSemaphore.Release();
            }

            if (imageLoadTokens.TryGetValue(game, out var currentCts) && currentCts == cts)
            {
                imageLoadTokens.TryRemove(game, out _);
                cts.Dispose();
            }
        }
    }

    [RelayCommand]
    private void OnAddButtonGameCard(GameViewModel app)
    {
        var addedMessage = new AddedMessage(app.AppId, app.Name);
        Messenger.Send(addedMessage, MessengerTokens.Dashboard);
    }

    private void OnAddedMessageHandler(object r, AddedMessage m)
    {
        var app = gameService.GetOrAddApp(m.AppID, m.Name, true, addRefCount: false);
        app.IsEnabled = true;
        foreach (var dlc in m.DLC ?? [])
        {
            app = gameService.GetOrAddApp(dlc, parentAppid: m.AppID, isEnabled: true, addRefCount: false);
            app.IsEnabled = true;
        }
        Messenger.Send(m, MessengerTokens.List);
    }

    private void OnAppPropertyChanged(AppViewModel app, string? propName)
    {
        if (propName is not nameof(AppViewModel.IsEnabled)) return;

        if (app.IsEnabled)
        {
            Apps[app.AppId] = gameService.GetOrAddApp(app.AppId, addRefCount: false);
        }
        else
        {
            Apps.Remove(app);
        }
    }

    private void OnAppsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (AppViewModel app in e.NewItems)
            {
                if (app.IsEnabled)
                {
                    Apps[app.AppId] = app;
                }
            }
        }

        if (e.OldItems != null)
        {
            foreach (AppViewModel app in e.OldItems)
            {
                Apps.Remove(app);
            }
        }
    }

    private async void OnProfileChangedMessage(object r, ProfileChangedMessage m)
    {
        await Application.Current.Dispatcher.BeginInvoke(async () =>
        {
            Apps.Clear();

            var caches = databaseService.GetAppsCache(gameService.EnabledAppids);

            int i = 0;
            foreach (var appid in gameService.EnabledAppids)
            {
                string? name = caches.TryGetValue(appid, out ApplistCacheDb? value) ? value.Name : null;
                Apps[appid] = gameService.GetOrAddApp(appid, name, true, addRefCount: false);

                if (++i % 100 == 0)
                {
                    await Dispatcher.Yield();
                }
            }
            OnPropertyChanged(nameof(SelectedProfile));
        });
    }

    [RelayCommand]
    private async Task RefreshFeatured()
    {
        try
        {
            var featuredApps = await SteamStore.GetFeaturedAsync();

            if (featuredApps is not { } featured) return;
            if (featured.FeaturedWin.Count == 0) return;

            Games.Clear();

            var items = featured.FeaturedWin.ToList();

            var keyValuePairs = new Dictionary<uint, string>();

            foreach (var feature in items)
            {
                // rare case when list of featured items has same appid
                if (Games.ContainsKey(feature.AppId)) continue;
                var game = new GameViewModel
                {
                    AppId = feature.AppId,
                    Name = feature.Name
                };

                keyValuePairs[game.AppId] = game.Name;

                Games[game.AppId] = game;
                await Dispatcher.Yield();
            }

            databaseService.SaveFeaturedCache(keyValuePairs);
        }
        catch (Exception ex)
        {
            logger.LogError(ex);
            snackbarService.Show("Error", ex.Message, ControlAppearance.Danger,
                new SymbolIcon(SymbolRegular.Warning16), default);
        }
    }

    [RelayCommand]
    private async Task StartSteam(bool? fromArgs)
    {
        if (IsSteamRunning)
        {
            if (fromArgs == null)
            {
                await Steam.ShutdownSteamAsync();
                if (IsSteamRunning)
                {
                    await Steam.KillSteamAsync();
                }
                injectorManagerService.CancelInject();
                return;
            }
            await Steam.KillSteamAsync();
            await injectorManagerService.WaitForWatcherCleanupAsync();
        }

        try
        {
            await injectorManagerService.Inject(gameService.EnabledAppids, settingsService.Settings.Mode, settingsService.Settings.SteamArgs);
        }
        catch { }
    }

    private void SteamWatcher_IsSteamRunningChanged(bool value)
    {
        if (IsSteamRunning == value) return;
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            IsSteamRunning = value;
            StartSteamCommand.NotifyCanExecuteChanged();
        });
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task UnLoadImageAsync(GameViewModel game)
    {
        if (imageLoadTokens.TryRemove(game, out var cts))
        {
            try { cts.Cancel(); }
            catch { }
            finally { cts.Dispose(); }
        }
        return Task.CompletedTask;
    }
}