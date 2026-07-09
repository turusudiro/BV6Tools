using AppPathsCommon;
using BV6Tools.Messages;
using BV6Tools.Services;
using BV6Tools.ViewModels.Shared;
using CommunityToolkit.Mvvm.Messaging;
using ImageCommon;
using SteamCommon;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using System.Windows.Threading;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace BV6Tools.ViewModels.Pages;

public partial class SearchPageViewModel : ObservableRecipient
{
    private readonly HttpClientService httpClientService;
    private readonly SemaphoreSlim imageLoadSemaphore = new(3);
    private readonly ConcurrentDictionary<GameViewModel, CancellationTokenSource> imageLoadTokens = new();
    private readonly ILoggerService logger;
    private readonly ISnackbarService snackbarService;

    public SearchPageViewModel(HttpClientService httpClientService, ILoggerService logger, ISnackbarService snackbarService)
    {
        this.httpClientService = httpClientService;
        this.logger = logger;
        this.snackbarService = snackbarService;
        GamesView = CollectionViewSource.GetDefaultView(Games);
        GamesView.SortDescriptions.Add(new SortDescription("Name", ListSortDirection.Ascending));
    }

    public ObservableCollection<GameViewModel> Games { get; } = [];

    public ICollectionView GamesView { get; }

    [ObservableProperty]
    public partial string Search { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusProgress { get; set; } = string.Empty;

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
    private void AddButtonGameCard(GameViewModel app)
    {
        var addedMessage = new AddedMessage(app.AppId, app.Name);
        Messenger.Send(addedMessage, MessengerTokens.Dashboard);
    }
    [RelayCommand]
    private void DownloadButtonGameCard(GameViewModel app)
    {
        Messenger.Send(new DownloadMessage(app.AppId, app.Name));
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task OnSearchTextChanged(CancellationToken token)
    {
        if (string.IsNullOrEmpty(Search)) return;
        try
        {
            await Task.Delay(500, token);
            if (!token.IsCancellationRequested) await SearchGameAsync(Search);
        }
        catch (TaskCanceledException)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex.Message);
            snackbarService.Show("Error", ex.Message, ControlAppearance.Danger,
                new SymbolIcon(SymbolRegular.Warning24), TimeSpan.FromSeconds(3));
        }
        finally
        {
            OnPropertyChanged(nameof(Games));
        }
    }

    private async Task SearchGameAsync(string name)
    {
        var results = await SteamStore.Search(name) ?? throw new InvalidOperationException("Search result empty!");

        if (results.Count == 0) return;

        Games.Clear();

        foreach (var result in results)
        {
            var game = new GameViewModel
            {
                AppId = result.AppId,
                Name = result.Name
            };
            Games.Add(game);
        }
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