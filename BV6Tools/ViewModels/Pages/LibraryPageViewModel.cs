using AppPathsCommon;
using BV6Tools.Models;
using BV6Tools.Services;
using BV6Tools.ViewModels.Shared;
using BV6Tools.Views.Pages;
using ImageCommon;
using Microsoft.Extensions.DependencyInjection;
using SteamCommon;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using System.Windows.Threading;
using Wpf.Ui;
using Wpf.Ui.Abstractions.Controls;

namespace BV6Tools.ViewModels.Pages;

public partial class LibraryPageViewModel : ObservableObject, INavigationAware
{
    protected Task? _initializeTask;

    private readonly HashSet<uint> ExcludedAppIds =
    [
        480,
        228980
    ];

    private readonly GameService gameService;
    private readonly HttpClientService httpClientService;
    private readonly SemaphoreSlim imageLoadSemaphore = new(3);
    private readonly ConcurrentDictionary<GameViewModel, CancellationTokenSource> imageLoadTokens = new();
    private readonly ILoggerService logger;
    private readonly INavigationService navigation;
    private readonly IServiceProvider serviceProvider;
    private readonly ISettingsService settings;
    private double oldviewBoxMargin;
    private double oldviewBoxSize;

    public LibraryPageViewModel(INavigationService navigationService, IServiceProvider serviceProvider, HttpClientService httpClientService,
        ILoggerService logger, ISettingsService settings,
        GameService gameService)
    {
        navigation = navigationService;
        this.serviceProvider = serviceProvider;
        this.httpClientService = httpClientService;
        this.logger = logger;
        this.settings = settings;
        this.gameService = gameService;
        GamesView = CollectionViewSource.GetDefaultView(Games);
        GamesView.Filter = FilterGames;
        GamesView.SortDescriptions.Add(new SortDescription("Name", ListSortDirection.Ascending));
    }

    public ObservableCollection<GameViewModel> Games { get; private set; } = [];

    public ICollectionView GamesView { get; private set; }

    [ObservableProperty]
    public partial bool IsInitialized { get; set; }

    public ConcurrentDictionary<GameViewModel, CancellationTokenSource> LoadTokens { get; } = new();

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    public AppSettings Settings => settings.Settings;

    public bool ViewBoxChanged => oldviewBoxSize != ViewBoxSize || oldviewBoxMargin != ViewBoxMargin;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ViewBoxChanged))]
    public partial double ViewBoxMargin { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ViewBoxChanged))]
    public partial double ViewBoxSize { get; set; }

    public Task OnNavigatedFromAsync() => Task.CompletedTask;

    public Task OnNavigatedToAsync() => _initializeTask ??= InitializeViewModel();

    private bool FilterGames(object obj)
    {
        if (obj is not GameViewModel g)
            return false;

        if (string.IsNullOrWhiteSpace(SearchText))
            return true;

        var q = SearchText.Trim().ToLower();

        if (g.Name?.ToLower().Contains(q, StringComparison.CurrentCultureIgnoreCase) == true)
            return true;

        if (g.AppId.ToString().Contains(q))
            return true;

        return false;
    }

    private Task InitializeViewModel()
    {
        oldviewBoxMargin = Settings.LibraryViewBoxMargin;
        oldviewBoxSize = Settings.LibraryViewBoxSize;
        ViewBoxMargin = oldviewBoxMargin;
        ViewBoxSize = oldviewBoxSize;
        logger.Log("Loading Library...");

        foreach (var installed in gameService.InstalledGames)
        {
            if (!ExcludedAppIds.Contains(installed.Key))
            {
                Games.Add(new()
                {
                    AppId = installed.Key,
                    Name = installed.Value
                });
            }
        }

        IsInitialized = true;
        return Task.CompletedTask;
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

    public void RescanInstalledGames()
    {
        gameService.RefreshInstalledGames();
        Games.Clear();
        foreach (var installed in gameService.InstalledGames)
        {
            if (!ExcludedAppIds.Contains(installed.Key))
            {
                Games.Add(new()
                {
                    AppId = installed.Key,
                    Name = installed.Value
                });
            }
        }
    }

    [RelayCommand]
    private async Task OnCardClick(GameViewModel game)
    {
        serviceProvider.GetService<GameLaunchingPageViewModel>()?.Game = game;
        navigation.NavigateWithHierarchy(typeof(GameLaunchingPage));
    }

    [RelayCommand]
    private void OnRevertViewBox()
    {
        ViewBoxMargin = oldviewBoxMargin;
        ViewBoxSize = oldviewBoxSize;
    }

    [RelayCommand]
    private void OnSaveViewBox()
    {
        oldviewBoxMargin = ViewBoxMargin;
        oldviewBoxSize = ViewBoxSize;
        settings.Save(s =>
        {
            s.LibraryViewBoxMargin = ViewBoxMargin;
            s.LibraryViewBoxSize = ViewBoxSize;
        });
        OnPropertyChanged(nameof(ViewBoxChanged));
    }

    partial void OnSearchTextChanged(string value)
    {
        if (!IsInitialized) return;
        GamesView.Refresh();
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