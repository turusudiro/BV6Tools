using AppPathsCommon;
using BV6Tools.Messages;
using BV6Tools.Models;
using BV6Tools.Services;
using BV6Tools.Services.Database;
using BV6Tools.Services.Database.Models;
using BV6Tools.Services.Injector;
using BV6Tools.Services.Steam;
using BV6Tools.ViewModels.Dialogs;
using BV6Tools.ViewModels.Shared;
using BV6Tools.Views.Dialogs;
using CommunityToolkit.Mvvm.Messaging;
using GreenLumaCommon;
using SqlNado;
using STCommon;
using SteamCommon;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Data;
using System.Windows.Threading;
using Wpf.Ui;
using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;

namespace BV6Tools.ViewModels.Pages;

public partial class GameLaunchingPageViewModel : ObservableObject, INavigationAware
{
    private readonly record struct DlcSnapshot(uint AppId, string? Name, bool IsEnabled);
    private readonly Dictionary<uint, HashSet<DlcSnapshot>> _dlcSnapshot = [];
    private readonly Dictionary<uint, LibraryGameOptionsDb> _snapshotOptions = [];
    private readonly IContentDialogService contentDialogService;
    private readonly DatabaseService databaseService;
    private readonly GameService gameService;
    private readonly InjectorService injectorService;
    private readonly Dictionary<uint, LibraryGameOptionsDb> libraryGameOptions;
    private readonly SQLiteLoadOptions loadOptions;
    private readonly ILoggerService logger;
    private readonly ISettingsService settings;
    private readonly ISnackbarService snackbarService;

    private EventHandler? _searchDebounceHandler;

    private DispatcherTimer? _searchDebounceTimer;

    private bool canPlay = true;

    public GameLaunchingPageViewModel(IContentDialogService contentDialogService, ISnackbarService snackbarService,
            ILoggerService logger, ISettingsService settingsService, GameService gameService,
            DatabaseService databaseService, InjectorService injectorService)
    {
        this.contentDialogService = contentDialogService;
        this.snackbarService = snackbarService;
        this.logger = logger;
        this.databaseService = databaseService;
        this.injectorService = injectorService;
        this.gameService = gameService;
        settings = settingsService;
        loadOptions = new(databaseService.Database)
        {
            CreateIfNotLoaded = true,
            TestTableExists = true,
        };

        libraryGameOptions = databaseService.Database.LoadAll<LibraryGameOptionsDb>(loadOptions).ToDictionary(x => x.AppId);
    }

    [ObservableProperty]
    public partial HashSet<uint> Appids { get; set; } = [];

    public int DLCCount => Game.DLC.Where(x => x.IsEnabled).Count();

    [ObservableProperty]
    public partial GameViewModel Game { get; set; } = null!;

    [ObservableProperty]
    public partial bool IsAddToListVisible { get; set; }

    [ObservableProperty]
    public partial bool IsFlyoutOpen { get; set; }

    public bool IsLimit => GreenLuma.Limit < Appids.Count;

    public int LimitMax { get; } = GreenLuma.Limit;

    [ObservableProperty]
    public partial ProcessMode Mode { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(UndoCommand))]
    public partial bool OnlineFix { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    public bool? SelectAll { get; set; }

    [ObservableProperty]
    public partial bool Unlock_Base { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Appids))]
    public partial bool Unlock_DLC { get; set; }

    public async Task InitializeGame(GameViewModel game)
    {
        Game = game;

        LibraryGameOptionsDb options = databaseService.Database.LoadByPrimaryKey<LibraryGameOptionsDb>(Game.AppId, loadOptions)!;

        _snapshotOptions[Game.AppId] = options.Clone();

        _dlcSnapshot[Game.AppId] = [];

        libraryGameOptions[Game.AppId] = options;

        Unlock_Base = options.Base;
        Unlock_DLC = options.DLC;
        Mode = options.ProcessMode;
        OnlineFix = options.OnlineFix;

        var dlcs = databaseService.Database.Load<LibraryDb>($"WHERE Parent = {game.AppId}", loadOptions);

        foreach (var dlc in dlcs)
        {
            var appid = dlc.AppId;
            var addedDlc = new DLCViewModel
            {
                AppId = appid,
                Name = dlc.Name,
                IsEnabled = dlc.IsEnabled
            };
            Game.DLC.Add(addedDlc);
        }

        if (Game.DLC.Any() && Unlock_DLC)
        {
            Appids = [.. Game.DLC.Where(x => x.IsEnabled).Select(x => x.AppId)];
        }
        if (Unlock_Base)
        {
            Appids.Add(Game.AppId);
        }

        _dlcSnapshot[Game.AppId] = [.. Game.DLC.Select(x => new DlcSnapshot(x.AppId, x.Name, x.IsEnabled))];

        SetDLCView();
        RefreshSelectAll();

        Game.IsInitialized = true;

        OnPropertyChanged(nameof(Appids));
        OnPropertyChanged(nameof(Game));
    }

    public Task OnNavigatedFromAsync()
    {
        WeakReferenceMessenger.Default.Send(new CounterVisibleChangedMessage(true));
        return Task.CompletedTask;
    }

    public async Task OnNavigatedToAsync()
    {
        canPlay = true;
        PlayCommand.NotifyCanExecuteChanged();

        if (!Game.IsInitialized)
        {
            await InitializeGame(Game);
        }
        else
        {
            Unlock_Base = libraryGameOptions[Game.AppId].Base;
            Unlock_DLC = libraryGameOptions[Game.AppId].DLC;
            Mode = libraryGameOptions[Game.AppId].ProcessMode;
            OnlineFix = libraryGameOptions[Game.AppId].OnlineFix;
            SaveCommand.NotifyCanExecuteChanged();
            UndoCommand.NotifyCanExecuteChanged();
        }

        Appids.Clear();

        if (Game.DLC.Any() && Unlock_DLC)
        {
            Appids = [.. Game.DLC.Where(x => x.IsEnabled).Select(x => x.AppId)];
        }
        if (Unlock_Base)
        {
            Appids.Add(Game.AppId);
        }

        OnPropertyChanged(nameof(Appids));

        WeakReferenceMessenger.Default.Send(new CounterVisibleChangedMessage(false));
        IsAddToListVisible = !gameService.EnabledAppids.Contains(Game.AppId);
    }

    [RelayCommand]
    private void AddToList()
    {
        var addedMessage = new AddedMessage(Game.AppId, Game.Name);
        WeakReferenceMessenger.Default.Send(addedMessage, MessengerTokens.Dashboard);
        IsAddToListVisible = false;
    }

    private bool CanPlay() => canPlay;

    private async Task CleanGreenLuma()
    {
        await Steam.KillSteamAsync();
        try
        {
            GreenLuma.CleanGreenLumaFiles(settings.Settings.SteamPath, AppPaths.GLPath);
        }
        catch (Exception ex)
        {
            logger.LogError(ex);
        }
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

    private bool Filter(object obj)
    {
        if (obj is not DLCViewModel d)
            return false;

        if (string.IsNullOrWhiteSpace(SearchText))
            return true;

        var q = SearchText.Trim().ToLower();

        if (d.Name?.ToLower().Contains(q, StringComparison.CurrentCultureIgnoreCase) == true)
            return true;

        if (d.AppId.ToString().Contains(q))
            return true;

        return false;
    }

    private bool IsDirty()
    {
        _snapshotOptions.TryGetValue(Game.AppId, out var snapshotOptions);
        if (snapshotOptions?.Base != Unlock_Base)
        {
            return true;
        }
        if (snapshotOptions?.DLC != Unlock_DLC)
        {
            return true;
        }

        if (snapshotOptions?.ProcessMode != Mode)
        {
            return true;
        }

        if (snapshotOptions?.OnlineFix != OnlineFix)
        {
            return true;
        }

        return !_dlcSnapshot[Game.AppId].SetEquals(Game.DLC.Select(x => new DlcSnapshot(x.AppId, x.Name, x.IsEnabled)));
    }

    [RelayCommand]
    private void OnButtonCancelClick(object parameter)
    {
        IsFlyoutOpen = false;
    }

    [RelayCommand]
    private void OnButtonClick(object parameter)
    {
        if (!IsFlyoutOpen)
        {
            IsFlyoutOpen = true;
        }
        else
        {
            IsFlyoutOpen = false;
        }
    }

    [RelayCommand]
    private void OnButtonSaveClick(object parameter)
    {
        if (parameter is not object[] param) return;
        if (param[0] is not string text) return;
        if (param[1] is not double value) return;
        IsFlyoutOpen = false;

        string? name = string.IsNullOrWhiteSpace(text) ? null : text;
        uint appid = (uint)value;

        Game.DLC.TryGetValue(appid, out var dlc);

        if (dlc == null)
        {
            dlc = new()
            {
                AppId = appid,
                IsEnabled = true
            };
            Game.DLC[appid] = dlc;
        }

        dlc.Name = string.IsNullOrEmpty(name) ? null : name;

        Game.DLCView?.Refresh();

        if (Unlock_DLC)
        {
            Appids.Add((uint)value);
        }
        RefreshSelectAll();
        IsFlyoutOpen = false;
        SaveCommand.NotifyCanExecuteChanged();
        UndoCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void OnDlcChecked(DLCViewModel dlc)
    {
        if (dlc.IsEnabled)
        {
            Appids.Add(dlc.AppId);
        }
        else
        {
            Appids.Remove(dlc.AppId);
        }
        RefreshSelectAll();

        SaveCommand.NotifyCanExecuteChanged();
        UndoCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task OnEditDLC(DLCViewModel dlc)
    {
        var data = new EditDialogViewModel
        {
            Title = "Edit",
            Name = dlc.Name,
            AppId = dlc.AppId,
            PlaceHolder = "DLC Name",
            ParentEdit = false
        };
        var dialog = new EditDialog(data);
        var result = await contentDialogService.ShowAsync(dialog, CancellationToken.None);
        if (result != ContentDialogResult.Primary) return;

        string? name = string.IsNullOrWhiteSpace(data.Name) ? null : data.Name;

        dlc.Name = name;
        dlc.AppId = (uint)data.AppId;

        OnPropertyChanged(nameof(dlc));

        SaveCommand.NotifyCanExecuteChanged();
        UndoCommand.NotifyCanExecuteChanged();
    }

    partial void OnModeChanged(ProcessMode value)
    {
        libraryGameOptions[Game.AppId].ProcessMode = value;
        SaveCommand.NotifyCanExecuteChanged();
        UndoCommand.NotifyCanExecuteChanged();
    }

    partial void OnOnlineFixChanged(bool value)
    {
        libraryGameOptions[Game.AppId].OnlineFix = value;
    }

    [RelayCommand]
    private async Task OnRefresh()
    {
        var currentGame = Game;
        var availableDlcs = new HashSet<uint>();

        try
        {
            var progressDialog = new ProgressDialog("Getting DLC", async (progress) =>
            {
                progress.IsIndeterminate = true;

                Progress<string> progressReport = new(msg => progress.Text = msg);

                var steam = new SteamService(progressReport, progress.Token);
                await steam.EnsureAnonymousLoggedOn();
                progress.Text = "Fetching info from Steam";
                var steamAppInfo = await steam.GetSteamAppInfoAsync(currentGame.AppId);

                if (steamAppInfo.Type == SteamAppInfoType.Unknown)
                {
                    throw new InvalidOperationException("Unknown App type");
                }

                availableDlcs.UnionWith(steamAppInfo.DLC);

                progress.Text = "Fetching info from Steam Web Details";
                var appDetail = await SteamStore.GetAppDetailsAsync(Game.AppId, progress.Token);

                availableDlcs.UnionWith(appDetail?.DLC ?? []);

                progress.MaxValue = availableDlcs.Count;
                HashSet<SteamApp> dlcs = [];

                foreach (var info in await steam.GetSteamAppInfoAsync(availableDlcs))
                {
                    progress.Text = $"Found info {info.Name}";
                    progress.Value++;

                    availableDlcs.Remove(info.AppId);
                    dlcs.Add(new()
                    {
                        AppId = info.AppId,
                        Name = info.Name
                    });
                }

                await foreach (var detail in SteamStore.GetAppDetailsStreamAsync(availableDlcs, cancellationToken: progress.Token))
                {
                    progress.Text = $"Found info {detail.Name}";
                    progress.Value++;

                    availableDlcs.Remove(detail.AppId);
                    dlcs.Add(new()
                    {
                        AppId = detail.AppId,
                        Name = detail.Name
                    });
                }

                foreach (var info in dlcs)
                {
                    currentGame.DLC.TryGetValue(info.AppId, out var dlc);
                    if (dlc == null)
                    {
                        dlc = new()
                        {
                            AppId = info.AppId
                        };
                        currentGame.DLC[dlc.AppId] = dlc;
                    }
                    dlc.Name = info.Name;
                }
                snackbarService.Show(
                "Info",
                $"Updated {dlcs.Count} DLC for {currentGame.Name}",
                ControlAppearance.Success,
                new SymbolIcon(SymbolRegular.CheckmarkCircle12),
                TimeSpan.FromSeconds(3));
            });
            await contentDialogService.ShowAsync(progressDialog, default);
        }
        catch (Exception ex)
        {
            snackbarService.Show("Error", ex.Message, ControlAppearance.Danger,
                new SymbolIcon(SymbolRegular.Warning16), TimeSpan.FromSeconds(3));
        }
        RefreshSelectAll();
        SaveCommand.NotifyCanExecuteChanged();
        UndoCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task OnRemoveAllDLC()
    {
        Appids.Clear();
        Game.DLC.Clear();

        if (Unlock_Base) Appids.Add(Game.AppId);
        RefreshSelectAll();
        SaveCommand.NotifyCanExecuteChanged();
        UndoCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task OnRemoveDLC(DLCViewModel dlc)
    {
        Game.DLC.Remove(dlc);
        Appids.Remove(dlc.AppId);
        SaveCommand.NotifyCanExecuteChanged();
        UndoCommand.NotifyCanExecuteChanged();
    }

    partial void OnSearchTextChanged(string value)
    {
        DebounceSearch(() =>
        {
            if (Game.DLCView == null) return;

            Game.DLCView.Refresh();
            RefreshSelectAll();
        });
    }

    [RelayCommand]
    private void OnSelectAll()
    {
        var value = SelectAll != true;

        foreach (var item in Game.DLCView?.Cast<DLCViewModel>() ?? [])
        {
            if (value)
            {
                Appids.Add(item.AppId);
            }
            else
            {
                Appids.Remove(item.AppId);
            }
            item.IsEnabled = value;
        }

        RefreshSelectAll();

        SaveCommand.NotifyCanExecuteChanged();
        UndoCommand.NotifyCanExecuteChanged();
    }

    partial void OnUnlock_BaseChanged(bool value)
    {
        libraryGameOptions[Game.AppId].Base = value;

        if (value)
        {
            Appids.Add(Game.AppId);
        }
        else
        {
            Appids.Remove(Game.AppId);
        }

        SaveCommand.NotifyCanExecuteChanged();
        UndoCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(Appids));
    }

    partial void OnUnlock_DLCChanged(bool value)
    {
        libraryGameOptions[Game.AppId].DLC = value;

        if (value)
        {
            Appids.UnionWith(Game.DLC.Where(d => d.IsEnabled).Select(d => d.AppId));
        }
        else
        {
            var disabledIds = Game.DLC
                .Select(d => d.AppId)
                .ToHashSet();

            Appids.RemoveWhere(id => disabledIds.Contains(id));
        }

        SaveCommand.NotifyCanExecuteChanged();
        UndoCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(Appids));
    }

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private async Task Play()
    {
        var state = injectorService.State;

        if (injectorService.IsInjected(Appids, Mode))
        {
            RunAppId();
            return;
        }

        if (injectorService.IsSteamRunning)
        {
            string title = "Steam is running";
            string msg = string.Empty;
            if (state.PID != injectorService.PID)
            {
                msg += "Steam detected running from outside this app";
            }
            else if (state.Mode != Mode)
            {
                msg = "Steam detected running in different inject mode";
            }
            else
            {
                msg = "Steam is injected without configured appids";
            }

            msg += Environment.NewLine + "Select Yes to stop steam immediately";

            var dialog = new SimpleContentDialogCreateOptions()
            {
                Title = title,
                Content = msg,
                PrimaryButtonText = "Yes",
                CloseButtonText = "Cancel"
            };
            var result = await contentDialogService.ShowSimpleDialogAsync(dialog);
            if (result != ContentDialogResult.Primary) return;
            await Steam.ShutdownSteamAsync();
        }

        try
        {
            int pid = 0;

            switch (Mode)
            {
                case ProcessMode.SteamTools:
                    {
                        var tickets = databaseService.Database.LoadAll<TicketDb>();
                        List<SetTicket> setTickets = [];

                        foreach (var ticket in tickets)
                        {
                            if (!gameService.EnabledAppids.Contains(ticket.AppId)) continue;
                            if (ticket.AppTicketBytes != null)
                            {
                                setTickets.Add(new SetTicket(ticket.AppId, TicketType.AppOwnership, ticket.AppTicketBytes));
                            }
                        }

                        if (tickets.Any())
                        {
                            ST.SaveTicket(setTickets, Path.Combine(AppPaths.LuaPath, "ticket.lua"));
                        }

                        pid = ST.StartSteamTools(AppPaths.STPath, settings.Settings.SteamPath, appids: Appids);
                        break;
                    }
                case ProcessMode.OpenSteamTool:
                    {
                        var tickets = databaseService.Database.LoadAll<TicketDb>();
                        List<SetTicket> setTickets = [];

                        foreach (var ticket in tickets)
                        {
                            if (!gameService.EnabledAppids.Contains(ticket.AppId)) continue;
                            if (ticket.AppTicketBytes != null)
                            {
                                setTickets.Add(new SetTicket(ticket.AppId, TicketType.AppOwnership, ticket.AppTicketBytes));
                            }
                            if (ticket.EncryptedTicketBytes != null)
                            {
                                setTickets.Add(new SetTicket(ticket.AppId, TicketType.Encrypted, ticket.EncryptedTicketBytes));
                            }
                        }

                        if (tickets.Any())
                        {
                            ST.SaveTicket(setTickets, Path.Combine(AppPaths.LuaPath, "ticket.lua"));
                        }

                        pid = ST.StartOpenSteamTool(AppPaths.OpenSteamToolPath, settings.Settings.SteamPath, appids: Appids);
                        break;
                    }
                default:
                    {
                        var glMode = GreenLumaMode.Stealth;
                        if (Mode.HasFlag(ProcessMode.GreenLumaNormal))
                        {
                            glMode = GreenLumaMode.Normal;
                            var ownershipDirPath = Path.Combine(settings.Settings.SteamPath, "AppOwnershipTickets");
                            var encryptedDirPath = Path.Combine(settings.Settings.SteamPath, "EncryptedAppTickets");
                            Directory.CreateDirectory(ownershipDirPath);
                            Directory.CreateDirectory(encryptedDirPath);

                            var tickets = databaseService.Database.LoadAll<TicketDb>();

                            foreach (var ticket in tickets)
                            {
                                if (!gameService.EnabledAppids.Contains(ticket.AppId)) continue;
                                if (ticket.AppTicketBytes != null)
                                {
                                    var destination = Path.Combine(ownershipDirPath, $"Ticket.{ticket.AppId}");
                                    await File.WriteAllBytesAsync(destination, ticket.AppTicketBytes);
                                }
                                if (ticket.EncryptedTicketBytes != null)
                                {
                                    var destination = Path.Combine(encryptedDirPath, $"EncryptedTicket.{ticket.AppId}");
                                    await File.WriteAllBytesAsync(destination, ticket.EncryptedTicketBytes);
                                }
                            }
                        }

                        pid = await GreenLuma.StartGreenLuma(AppPaths.GLPath, settings.Settings.SteamPath, Appids, mode: glMode);
                        break;
                    }
            }

            if (pid == 0) throw new InvalidOperationException("Failed to detect steam with PID 0!");

            injectorService.RaiseInjected(new(Appids, Mode, pid, settings.Settings.SteamPath));
            canPlay = false;
            RunAppId();
        }
        catch (IOException ex) when (ex.HResult == unchecked((int)0x80070522))
        {
            var dialog = new SimpleContentDialogCreateOptions()
            {
                Title = "Error",
                Content = $"Cannot inject Steam in {Mode} Mode. Developer Mode is disabled, Please enable developer mode.",
                PrimaryButtonText = "Open developer settings",
                CloseButtonText = "Cancel"
            };
            var result = await contentDialogService.ShowSimpleDialogAsync(dialog);
            if (result != ContentDialogResult.Primary) return;
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "ms-settings:developers",
                    UseShellExecute = true
                };

                Process.Start(startInfo);
            }
            catch (Exception e)
            {
                logger.LogError(e);
                snackbarService.Show("Could not open Settings window automatically", e.Message,
                    ControlAppearance.Danger, default, default);
            }
        }
        catch (AggregateException ex)
        {
            logger.LogError(ex);
            if (Mode.IsGreenLuma())
            {
                await CleanGreenLuma();
            }
            foreach (var error in ex.InnerExceptions)
            {
                snackbarService.Show("Error", error.Message, ControlAppearance.Danger, new SymbolIcon(SymbolRegular.Warning16),
                default);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex);
            snackbarService.Show("Error", ex.Message, ControlAppearance.Danger, default, default);
        }
    }

    private void RunAppId()
    {
        string args = $"steam://run/{Game.AppId}";
        if (Mode.HasFlag(ProcessMode.OpenSteamTool) && OnlineFix)
        {
            args += "//-onlinefix";
        }
        try
        {
            ProcessStartInfo processStartInfo = new()
            {
                FileName = args,
                UseShellExecute = true
            };
            Process.Start(processStartInfo);
            canPlay = false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex);
            snackbarService.Show("Error", ex.Message, ControlAppearance.Danger, default, default);
        }
    }

    private void RefreshSelectAll()
    {
        var items = Game.DLCView?.Cast<DLCViewModel>() ?? [];
        var allEnabled = items.All(x => x.IsEnabled);
        var noneEnabled = items.All(x => !x.IsEnabled);
        SelectAll = allEnabled ? true : noneEnabled ? false : null; ;
        OnPropertyChanged(nameof(DLCCount));
        OnPropertyChanged(nameof(Appids));
        OnPropertyChanged(nameof(SelectAll));
    }

    [RelayCommand(CanExecute = nameof(IsDirty))]
    private void Save()
    {
        databaseService.Database.BeginTransaction();
        try
        {
            LibraryGameOptionsDb gameOptionsDb = new()
            {
                AppId = Game.AppId,
                Base = Unlock_Base,
                DLC = Unlock_DLC,
                ProcessMode = Mode,
                OnlineFix = OnlineFix
            };
            databaseService.Database.Save(gameOptionsDb);
            _snapshotOptions[Game.AppId] = gameOptionsDb;

            HashSet<DlcSnapshot> currentDlcs = [];
            HashSet<LibraryDb> libraryDbs = [];

            foreach (var dlc in Game.DLC)
            {
                currentDlcs.Add(new DlcSnapshot(dlc.AppId, dlc.Name, dlc.IsEnabled));
                libraryDbs.Add(new LibraryDb
                {
                    AppId = dlc.AppId,
                    Name = dlc.Name,
                    IsEnabled = dlc.IsEnabled,
                    Parent = Game.AppId
                });
            }

            var removedAppIds = _dlcSnapshot[Game.AppId]
                .Where(snapshot => !Game.DLC.ContainsKey(snapshot.AppId))
                .Select(snapshot => snapshot.AppId)
                .ToHashSet();

            if (removedAppIds.Count > 0)
            {
                var libraryTableName = databaseService.Database.SynchronizeSchema<LibraryDb>().Name;
                var appids = string.Join(",", removedAppIds);
                databaseService.Database.ExecuteNonQuery(
                    $"DELETE FROM {libraryTableName} WHERE AppId IN ({appids}) AND Parent == {Game.AppId}"
                );
            }
            databaseService.Database.Save(libraryDbs);
            databaseService.Database.Commit();
            _dlcSnapshot[Game.AppId] = currentDlcs;
        }
        catch
        {
            databaseService.Database.Rollback();
        }
        finally
        {
            SaveCommand.NotifyCanExecuteChanged();
            UndoCommand.NotifyCanExecuteChanged();
        }
    }

    private void SetDLCView()
    {
        Game.DLCView = CollectionViewSource.GetDefaultView(Game.DLC);
        Game.DLCView.Filter = Filter;
        Game.DLCView.SortDescriptions.Add(new SortDescription(nameof(DLCViewModel.AppId), ListSortDirection.Ascending));
    }

    [RelayCommand(CanExecute = nameof(IsDirty))]
    private void Undo()
    {
        var snapshot = _snapshotOptions[Game.AppId];
        Unlock_Base = snapshot.Base;
        Unlock_DLC = snapshot.DLC;
        Mode = snapshot.ProcessMode;
        OnlineFix = snapshot.OnlineFix;

        Game.DLC = [.. _dlcSnapshot[Game.AppId].Select(x => new DLCViewModel()
        {
            AppId = x.AppId,
            Name = x.Name,
            IsEnabled = x.IsEnabled
        })];

        SetDLCView();
        RefreshSelectAll();

        SaveCommand.NotifyCanExecuteChanged();
        UndoCommand.NotifyCanExecuteChanged();
    }
}