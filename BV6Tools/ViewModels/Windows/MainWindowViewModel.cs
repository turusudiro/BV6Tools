using BV6Tools.Messages;
using BV6Tools.Models;
using BV6Tools.Services;
using BV6Tools.Services.Database.Models;
using BV6Tools.Services.Injector;
using BV6Tools.ViewModels.Pages;
using BV6Tools.ViewModels.Pages.Lua;
using BV6Tools.ViewModels.Shared;
using BV6Tools.Views.Pages;
using CommunityToolkit.Mvvm.Messaging;
using GreenLumaCommon;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace BV6Tools.ViewModels.Windows;

public partial class MainWindowViewModel : ObservableRecipient
{
    private readonly string[] allmessengertokens =
    [
        MessengerTokens.List,
        MessengerTokens.Depot,
        MessengerTokens.Lua,
        MessengerTokens.Ticket,
        MessengerTokens.Settings
    ];

    private readonly NavigationViewItem depotNavigationViewItem = new()
    {
        Content = "Depot",
        Icon = new SymbolIcon { Symbol = SymbolRegular.Box24 },
        TargetPageType = typeof(DepotPage),
    };

    private readonly GameService gameService;

    private readonly NavigationViewItem listNavigationViewItem = new()
    {
        Content = "List",
        Icon = new SymbolIcon { Symbol = SymbolRegular.TextBulletListSquare24 },
        TargetPageType = typeof(ListPage),
    };

    private readonly NavigationViewItem luaNavigationViewItem = new()
    {
        Content = "Lua",
        Icon = new SymbolIcon { Symbol = SymbolRegular.Script16 },
        TargetPageType = typeof(LuaPage)
    };

    private readonly Dictionary<string, int> pendingCounts = new()
    {
        [nameof(ListPageViewModel)] = 0,
        [nameof(DepotPageViewModel)] = 0,
        [nameof(LuaPageViewModel)] = 0,
        [nameof(TicketPageViewModel)] = 0,
        [nameof(SettingsPageViewModel)] = 0,
    };

    private readonly ISettingsService settingsService;
    private readonly ISnackbarService snackbarService;

    private readonly NavigationViewItem ticketNavigationViewItem = new()
    {
        Content = "Ticket",
        Icon = new SymbolIcon { Symbol = SymbolRegular.TicketHorizontal24 },
        TargetPageType = typeof(TicketPage)
    };

    private bool _isInitialized = false;
    private ProcessMode mode;

    public MainWindowViewModel(ISnackbarService snackbarService, InjectorService injectorService,
        GameService gameService, ISettingsService settingsService)
    {
        this.snackbarService = snackbarService;
        this.settingsService = settingsService;
        this.gameService = gameService;

        injectorService.OnInjected += OnInjectorServiceInjected;

        injectorService.OnInjectFailed += OnInjectorServiceFailedInject;

        gameService.Apps.CollectionChanged += OnAppsCollectionChanged;
        gameService.Apps.ItemPropertyChanged += OnAppItemPropertyChanged;

        Profiles = new ObservableCollection<ProfileDb>(this.settingsService.Profiles);
        SelectedProfile = Profiles.FirstOrDefault(x => x.ProfileID == this.settingsService.Settings.ActiveProfileId);
        IsActive = true;

        mode = settingsService.Settings.Mode;
        IsCounterVisible = mode.IsGreenLuma();

        if (!_isInitialized)
        {
            InitializeViewModel();
        }
    }

    public HashSet<uint> Appids => gameService.EnabledAppids;

    [ObservableProperty]
    public partial string ApplicationTitle { get; set; } = "BV6Tools";

    public bool DepotHasPendingChanges => pendingCounts[nameof(DepotPageViewModel)] > 0;

    [NotifyCanExecuteChangedFor(nameof(ShowNotificationFlyoutCommand))]
    [ObservableProperty]
    public partial bool HasPendingChanges { get; set; }

    [ObservableProperty]
    public partial bool IsCounterVisible { get; set; } = true;

    public bool IsLimit => LimitMax < Appids.Count;

    [NotifyCanExecuteChangedFor(nameof(ShowNotificationFlyoutCommand))]
    [ObservableProperty]
    public partial bool IsNotificationFlyoutOpen { get; set; }

    public int LimitMax { get; } = GreenLuma.Limit;

    public bool ListHasPendingChanges => pendingCounts[nameof(ListPageViewModel)] > 0;

    public bool LuaHasPendingChanges => pendingCounts[nameof(LuaPageViewModel)] > 0;

    [ObservableProperty]
    public partial ObservableCollection<object> NavigationFooter { get; set; } = [];

    [ObservableProperty]
    public partial ObservableCollection<object> NavigationItems { get; set; } = [];

    [ObservableProperty]
    public partial string NewProfileName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int PendingChangesCount { get; set; }

    public ObservableCollection<ProfileDb> Profiles { get; set; }

    [ObservableProperty]
    public partial ProfileDb? SelectedProfile { get; set; }

    public bool SettingsHasPendingChanges => pendingCounts[nameof(SettingsPageViewModel)] > 0;

    public bool TicketHasPendingChanges => pendingCounts[nameof(TicketPageViewModel)] > 0;

    protected override void OnActivated()
    {
        Messenger.Register<CounterVisibleChangedMessage>(this, OnCounterVisibleMessageReceived);
        Messenger.Register<ProfileChangedMessage>(this, OnProfileChangedMessage);
        Messenger.Register<InjectModeChangedMessage>(this, OnModeChangedMessage);
        Messenger.Register<NavigationPageBadgeMessage>(this, OnNavigationPageMessage);
    }

    private void InitializeViewModel()
    {
        ApplicationTitle = "BV6Tools";

        NavigationItems =
        [
            new NavigationViewItem("Home", SymbolRegular.Home24, typeof(DashboardPage)),
            new NavigationViewItem
            {
                Content = "Search",
                Icon = new SymbolIcon { Symbol = SymbolRegular.Search24 },
                TargetPageType = typeof(SearchPage)
            },
            new NavigationViewItem
            {
                Content = "Library",
                Icon = new SymbolIcon { Symbol = SymbolRegular.Library24 },
                TargetPageType = typeof(LibraryPage)
            },
            listNavigationViewItem,
            depotNavigationViewItem,
            luaNavigationViewItem,
            ticketNavigationViewItem
        ];

        NavigationFooter =
        [
            new NavigationViewItem
            {
                Content = "Guide",
                Icon = new SymbolIcon { Symbol = SymbolRegular.BookOpen24 },
                TargetPageType = typeof(GuidePage)
            },
            new NavigationViewItem()
            {
                Content = "Settings",
                Icon = new SymbolIcon { Symbol = SymbolRegular.Settings24 },
                TargetPageType = typeof(SettingsPage),
            },
        ];

        _isInitialized = true;
    }

    [RelayCommand]
    private async Task NotificationSendSave(string page)
    {
        if (page == "all")
        {
            foreach (var token in allmessengertokens)
            {
                try
                {
                    await Messenger.Send(new NotificationCenterMessage(), token).Response;
                }
                catch { }
            }
        }
        else
        {
            var token = page switch
            {
                "list" => MessengerTokens.List,
                "depot" => MessengerTokens.Depot,
                "lua" => MessengerTokens.Lua,
                "ticket" => MessengerTokens.Ticket,
                "settings" => MessengerTokens.Settings,
                _ => null
            };
            if (token == null) return;
            try
            {
                await Messenger.Send(new NotificationCenterMessage(), token).Response;
            }
            catch { }
        }

        IsNotificationFlyoutOpen = HasPendingChanges;
    }

    private void OnAppItemPropertyChanged(AppViewModel app, string? propName)
    {
        if (propName != nameof(AppViewModel.IsEnabled)) return;
        OnPropertyChanged(nameof(Appids));
        OnPropertyChanged(nameof(IsLimit));
    }

    private void OnAppsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(Appids));
        OnPropertyChanged(nameof(IsLimit));
    }

    private void OnCounterVisibleMessageReceived(object r, CounterVisibleChangedMessage m)
    {
        if (!mode.IsGreenLuma()) return;
        IsCounterVisible = m.Value;
    }

    private void OnInjectorServiceFailedInject(object sender, Exception ex)
    {
        snackbarService.Show("Error", ex.Message, ControlAppearance.Danger, default, default);
    }

    private async void OnInjectorServiceInjected(SteamProcessData state)
    {
        switch (settingsService.Settings.OnInject)
        {
            case OnInject.None:
                Application.Current.MainWindow?.WindowState = WindowState.Minimized;
                break;

            case OnInject.Minimize:
                Application.Current.MainWindow?.Hide();
                break;
        }
    }

    private void OnModeChangedMessage(object r, InjectModeChangedMessage m)
    {
        mode = m.Value;
        UpdateCounterVisibility();
    }

    private void OnNavigationPageMessage(object r, NavigationPageBadgeMessage m)
    {
        var item = m.NavigationPageName switch
        {
            nameof(ListPageViewModel) => listNavigationViewItem,
            nameof(DepotPageViewModel) => depotNavigationViewItem,
            nameof(LuaPageViewModel) => luaNavigationViewItem,
            nameof(TicketPageViewModel) => ticketNavigationViewItem,
            _ => null
        };
        item?.InfoBadge = m.Count > 0 ? new InfoBadge
        {
            Value = m.Count.ToString(),
            Severity = InfoBadgeSeverity.Attention
        } : null;

        pendingCounts[m.NavigationPageName] = m.Count;

        PendingChangesCount = pendingCounts.Values.Count(c => c > 0);
        HasPendingChanges = pendingCounts.Values.Any(c => c > 0);
        OnPropertyChanged(m.NavigationPageName switch
        {
            nameof(ListPageViewModel) => nameof(ListHasPendingChanges),
            nameof(DepotPageViewModel) => nameof(DepotHasPendingChanges),
            nameof(LuaPageViewModel) => nameof(LuaHasPendingChanges),
            nameof(TicketPageViewModel) => nameof(TicketHasPendingChanges),
            nameof(SettingsPageViewModel) => nameof(SettingsHasPendingChanges),
            _ => null
        });
    }

    private void OnProfileChangedMessage(object r, ProfileChangedMessage m)
    {
        OnPropertyChanged(nameof(Appids));
        OnPropertyChanged(nameof(IsLimit));
    }

    [RelayCommand]
    private void ShowNotificationFlyout()
    {
        IsNotificationFlyoutOpen = true;
    }

    private void UpdateCounterVisibility() =>
                IsCounterVisible = mode.IsGreenLuma();
}