using BV6Tools.Collections;
using BV6Tools.Messages;
using BV6Tools.Models;
using BV6Tools.Services;
using BV6Tools.Services.Database;
using BV6Tools.Services.Database.Extensions;
using BV6Tools.Services.Database.Models;
using BV6Tools.Services.Steam;
using BV6Tools.ViewModels.Dialogs;
using BV6Tools.ViewModels.Pages.Shared;
using BV6Tools.ViewModels.Shared;
using BV6Tools.Views.Dialogs;
using CommunityToolkit.Mvvm.Messaging;
using SteamCommon;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace BV6Tools.ViewModels.Pages;

public partial class ListPageViewModel : AppManagerPageViewModel
{
    private readonly IContentDialogService contentDialogService;

    private readonly ILoggerService logger;
    private readonly ISnackbarService snackbarService;

    public ListPageViewModel(IContentDialogService contentDialogService, ISnackbarService snackbarService,
               ILoggerService loggerService, GameService gameService,
               DatabaseService databaseService, ISettingsService settingsService) :
        base(contentDialogService, gameService, snackbarService, databaseService, settingsService)
    {
        this.contentDialogService = contentDialogService;
        this.snackbarService = snackbarService;
        logger = loggerService;
        IsActive = true;
    }

    protected override string ManagerType => "DLC";
    private bool CanAddNewDlc => Games?.Count > 0;

    protected override ObservableDictionary<uint, AppViewModel> GetAppItemsCollection(AppViewModel app) => app.DLC;

    protected override ICollectionView? GetItemsView(AppViewModel app) => app.DLCView;

    protected override bool? GetSelectAllValue(AppViewModel app) => app.SelectAllDLC;

    protected override void OnDirtyChanged()
    {
        Messenger.Send(new NavigationPageBadgeMessage(nameof(ListPageViewModel), dirtyGames.Count));
        base.OnDirtyChanged();
    }
    protected override void OnActivated()
    {
        base.OnActivated();
        Messenger.Register<AddedMessage, string>(this, MessengerTokens.List, AddedMessageHandler);
    }

    protected override void OnFirstItemLoaded()
    {
        AddNewDlcCommand.NotifyCanExecuteChanged();
    }

    protected override void OnProfileChangedMessage(object r, ProfileChangedMessage m)
    {
        base.OnProfileChangedMessage(r, m);
        Messenger.Send(new NavigationPageBadgeMessage(nameof(ListPageViewModel), dirtyGames.Count));
    }

    protected override async Task OnRemoveAllAppItems(AppViewModel game)
    {
        await base.OnRemoveAllAppItems(game);
        AddNewDlcCommand.NotifyCanExecuteChanged();
    }

    protected override async Task OnRemoveAsync(AppViewModel app)
    {
        await base.OnRemoveAsync(app);
        AddNewDlcCommand.NotifyCanExecuteChanged();
    }

    protected override void OnSaveMessage(object r, SaveMessage m)
    {
        base.OnSaveMessage(r, m);
        Messenger.Send(new NavigationPageBadgeMessage(nameof(ListPageViewModel), dirtyGames.Count));
    }

    protected override void RefreshSelectAll(AppViewModel app) => app.SelectAllDLC = GetSelectAllState(app.DLCView);

    protected override void SetItemsView(AppViewModel app)
    {
        app.DLCView = CollectionViewSource.GetDefaultView(app.DLC);
        app.DLCView.SortDescriptions.Add(new SortDescription(nameof(AppViewModel.Name), ListSortDirection.Ascending));
        app.DLCView.SortDescriptions.Add(new SortDescription(nameof(AppViewModel.AppId), ListSortDirection.Ascending));
    }

    protected override void SetSelectAllValue(AppViewModel app, bool? value) => app.SelectAllDLC = value;

    protected override async Task Undo()
    {
        await base.Undo();
        AddNewDlcCommand.NotifyCanExecuteChanged();
    }

    private void AddedMessageHandler(object r, AddedMessage m)
    {
        try
        {
            var db = databaseService.Database.LoadByKeys<GameDb>(
                ManagerType, m.AppID, settingsService.Settings.ActiveProfileId);
            if (db != null)
            {
                return;
            }
        }
        catch { }

        Games.TryGetValue(m.AppID, out var game);

        if (game != null && m.DLC == null)
        {
            return;
        }

        int addedCount = 0;

        if (game == null)
        {
            game = gameService.GetOrAddApp(m.AppID, m.Name);
            game.IsEnabled = true;

            Games[m.AppID] = game;
            addedCount++;
        }
        game.IsEnabled = m.IsEnabled ?? game.IsEnabled;
        foreach (var dlc in m.DLC ?? [])
        {
            game.DLC.TryGetValue(dlc, out var app);
            if (app == null)
            {
                app = gameService.GetOrAddApp(dlc, parentAppid: m.AppID);
                app.IsEnabled = true;

                game.DLC[dlc] = app;
                addedCount++;
            }
            app.IsEnabled = m.IsEnabled ?? app.IsEnabled;
        }

        OnAppChanged(game, true);

        AddNewDlcCommand.NotifyCanExecuteChanged();

        Messenger.Send(new NavigationPageBadgeMessage(nameof(ListPageViewModel), dirtyGames.Count));

        snackbarService.Show("Success", "Added to the List", ControlAppearance.Success,
                    new SymbolIcon(SymbolRegular.TaskListSquareAdd24), default);
    }

    [RelayCommand]
    private void CancelTask()
    {
        RefreshGameCommand.Cancel();
    }

    private async IAsyncEnumerable<SteamApp> GetSteamAppInfo(ICollection<uint> appids, SteamService steam,
    [EnumeratorCancellation] CancellationToken token = default)
    {
        var remaining = appids.ToList();

        var steamEnumerator = steam.GetSteamAppInfoStreamAsync(remaining).WithCancellation(token).GetAsyncEnumerator();
        while (true)
        {
            SteamAppInfos info;
            try
            {
                if (!await steamEnumerator.MoveNextAsync()) break;
                info = steamEnumerator.Current;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError($"Cannot get app info from steamkit2: {ex.Message}", ex);
                break;
            }

            remaining.Remove(info.AppId);
            yield return new() { AppId = info.AppId, Name = info.Name };
        }

        SteamAppDetailsResponse detail;
        var storeEnumerator = SteamStore.GetAppDetailsStreamAsync(remaining, cancellationToken: token).WithCancellation(token).GetAsyncEnumerator();
        while (true)
        {
            try
            {
                if (!await storeEnumerator.MoveNextAsync()) break;
                detail = storeEnumerator.Current;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError($"Cannot get app info from web store: {ex.Message}", ex);
                break;
            }

            remaining.Remove(detail.AppId);
            yield return new() { AppId = detail.AppId, Name = detail.Name };
        }

        foreach (var missingId in remaining)
        {
            yield return new() { AppId = missingId };
        }
    }

    [RelayCommand]
    private async Task OnAddGameAsync(CancellationToken token)
    {
        var data = new EditDialogViewModel
        {
            Title = "Add",
            PlaceHolder = "App Name",
            ParentEdit = false
        };
        var dialog = new EditDialog(data);
        var result = await contentDialogService.ShowAsync(dialog, token);
        if (result != ContentDialogResult.Primary) return;
        var newAppId = data.AppId ?? 0;

        if (Games.TryGetValue(newAppId, out var g))
        {
            g.Name = data.Name ?? g.Name;
        }
        else
        {
            var game = gameService.GetOrAddApp(newAppId, data.Name);
            game.Name = data.Name ?? game.Name;
            Games[newAppId] = game;
        }

        AddNewDlcCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
        UndoCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanAddNewDlc))]
    private async Task OnAddNewDlcAsync(object sender)
    {
        var data = new EditDialogViewModel
        {
            Title = "New DLC",
            PlaceHolder = "DLC Name"
        };
        var dialog = new EditDialog(data);
        if (sender is AppViewModel g)
        {
            data.SelectedGame = g;
        }
        else
        {
            var games = GamesView.Cast<AppViewModel>();
            data.ParentEdit = true;
            data.Games = games;
            data.SelectedGame = games.First();
        }

        var result = await contentDialogService.ShowAsync(dialog, CancellationToken.None);
        if (result != ContentDialogResult.Primary) return;

        var newAppId = data.AppId ?? 0;

        if (!data.SelectedGame.DLC.TryGetValue(newAppId, out var dlc))
        {
            dlc = gameService.GetOrAddApp(newAppId, data.Name, parentAppid: data.SelectedGame.AppId);
            data.SelectedGame.DLC[newAppId] = dlc;
        }

        dlc.Name = data.Name;
    }

    [RelayCommand]
    private async Task OnRefreshGameAsync(AppViewModel game, CancellationToken token)
    {
        CancelVisible = true;
        IsProgressBarVisible = true;
        IsProgressBarIndeterminate = true;
        ProgressText = "Fetching App Info...";
        var availableDlcs = new HashSet<uint>();
        uint? parentAppId = 0;
        var gameName = string.Empty;
        var infoFound = false;
        gameService.ApplyUpdateOnChanges(false);

        try
        {
            var progress = new Progress<string>(msg => ProgressText = msg);
            var steam = new SteamService(progress, token);

            try
            {
                token.ThrowIfCancellationRequested();

                // Get Info from steamkit
                await steam.EnsureAnonymousLoggedOn();
                var steamAppInfo = await steam.GetSteamAppInfoAsync(game.AppId);
                if (steamAppInfo.HasParent)
                {
                    availableDlcs.Add(steamAppInfo.AppId);
                    steamAppInfo = await steam.GetSteamAppInfoAsync(steamAppInfo.Parent);
                }

                if (steamAppInfo.Type == SteamAppInfoType.Unknown)
                {
                    logger.LogError("Unknown App type, aborting...");
                    throw new InvalidOperationException("Unknown App type");
                }

                token.ThrowIfCancellationRequested();
                ProgressText = "Found appinfo";
                infoFound = true;
                parentAppId = steamAppInfo.AppId;
                gameName = steamAppInfo.Name;
                availableDlcs.UnionWith(steamAppInfo.DLC);
            }
            catch (Exception ex)
            {
                logger.LogError(ex);
            }
            try
            {
                // Get Info from steam app details web store
                token.ThrowIfCancellationRequested();

                SteamAppDetailsResponse? detail;
                if (infoFound && parentAppId.HasValue)
                {
                    detail = await SteamStore.GetAppDetailsAsync(parentAppId.Value, token);
                }
                else if (infoFound)
                {
                    detail = await SteamStore.GetAppDetailsAsync(game.AppId, token);
                }
                else
                {
                    detail = await SteamStore.GetAppDetailsAsync(game.AppId, token);
                    if (detail.HasValue && detail.Value.Type == SteamAppDetailsType.Game)
                    {
                        gameName = detail.Value.Name ?? gameName;
                        parentAppId = detail.Value.AppId;
                        infoFound = true;
                    }
                    else if (detail != null && detail.Value.FullGame.AppId != 0)
                    {
                        availableDlcs.Add(detail.Value.AppId);
                        gameName = detail.Value.FullGame.Name ?? gameName;
                        parentAppId = detail.Value.FullGame.AppId;
                        detail = await SteamStore.GetAppDetailsAsync(parentAppId.Value, token);
                    }

                    if (detail.HasValue && detail.Value.Type == SteamAppDetailsType.Game) infoFound = true;
                }

                if (detail.HasValue)
                {
                    availableDlcs.UnionWith(detail.Value.DLC);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex);
            }

            if (!infoFound)
            {
                logger.LogError($"No info found for {game.AppId}, aborting...");
                throw new InvalidOperationException($"No info found for ({game.AppId}) {game.Name}");
            }

            if (parentAppId.HasValue)
            {
                var dlcs = game.DLC.ToList();
                Games.Remove(game);
                if (!Games.TryGetValue(parentAppId.Value, out game))
                {
                    game = gameService.GetOrAddApp(parentAppId.Value, gameName);
                    Games[parentAppId.Value] = game;
                }

                foreach (var dlc in dlcs)
                {
                    availableDlcs.Add(dlc.AppId);
                    if (!game.DLC.ContainsKey(dlc.AppId))
                    {
                        game.DLC[dlc.AppId] = gameService.GetOrAddApp(dlc.AppId, dlc.Name, dlc.IsEnabled);
                    }
                }
            }

            game.Name = gameName;

            var caches = databaseService.GetAppsCache(availableDlcs);

            if (availableDlcs.Count > 0)
            {
                CurrentProgress = 0;
                MaxProgress = availableDlcs.Count;
                ProgressText = $"Fetching DLCs info {CurrentProgress}/{MaxProgress}";
                IsProgressBarIndeterminate = false;

                await foreach (var info in GetSteamAppInfo(availableDlcs, steam, token))
                {
                    token.ThrowIfCancellationRequested();
                    uint appid = info.AppId;
                    string? name = info.Name;

                    CurrentProgress++;
                    ProgressText = $"Fetching DLCs info {CurrentProgress}/{MaxProgress}";

                    if (!game.DLC.TryGetValue(appid, out var dlc))
                    {
                        dlc = gameService.GetOrAddApp(appid, parentAppid: game.AppId, caches: caches);
                        game.DLC[appid] = dlc;
                    }

                    dlc.Name = name;

                    if (Games.TryGetValue(appid, out var g))
                    {
                        HashSet<uint> toRelease = [];
                        foreach (var existsDlc in g.DLC)
                        {
                            toRelease.Add(existsDlc.AppId);
                            if (!game.DLC.ContainsKey(existsDlc.AppId))
                            {
                                game.DLC[existsDlc.AppId] = gameService.GetOrAddApp(existsDlc.AppId, name: existsDlc.Name,
                                    isEnabled: existsDlc.IsEnabled, game.AppId, caches: caches);
                            }
                        }

                        Games.Remove(g);
                        gameService.ReleaseApp(toRelease, g.AppId);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex);
            snackbarService.Show("Error", ex.Message, ControlAppearance.Danger,
                new SymbolIcon(SymbolRegular.Warning16), default);
        }
        finally
        {
            gameService.ApplyUpdateOnChanges(true);
            IsProgressBarVisible = false;
            CancelVisible = false;
            AddNewDlcCommand.NotifyCanExecuteChanged();
            GamesView.Refresh();
        }
    }
}