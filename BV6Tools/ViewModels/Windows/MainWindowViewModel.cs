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

    private void OnInjectorServiceFailedInject(Exception ex)
    {
        snackbarService.Show("Error", ex.Message, ControlAppearance.Danger, default, default);
    }

    public HashSet<uint> Appids => gameService.EnabledAppids;

    [ObservableProperty]
    public partial string ApplicationTitle { get; set; } = "BV6Tools";

    [ObservableProperty]
    public partial bool IsCounterVisible { get; set; } = true;

    public bool IsLimit => LimitMax < Appids.Count;

    public int LimitMax { get; } = GreenLuma.Limit;

    [ObservableProperty]
    public partial ObservableCollection<object> NavigationFooter { get; set; } = [];

    [ObservableProperty]
    public partial ObservableCollection<object> NavigationItems { get; set; } = [];

    [ObservableProperty]
    public partial string NewProfileName { get; set; } = string.Empty;

    public ObservableCollection<ProfileDb> Profiles { get; set; }

    [ObservableProperty]
    public partial ProfileDb? SelectedProfile { get; set; }

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
            case OnInject.Exit:
                /// ApplicationHostService already subscribed first OnInjectorServiceInjected
                /// so cleanup already scheduled (if configured) and its fine to shutdown now.
                Application.Current.Shutdown();
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
        if (item == null) return;
        item.InfoBadge = m.Count > 0 ? new InfoBadge
        {
            Value = m.Count.ToString(),
            Severity = InfoBadgeSeverity.Attention
        } : null;
    }

    private void OnProfileChangedMessage(object r, ProfileChangedMessage m)
    {
        OnPropertyChanged(nameof(Appids));
        OnPropertyChanged(nameof(IsLimit));
    }

    private void UpdateCounterVisibility() =>
                IsCounterVisible = mode.IsGreenLuma();
}