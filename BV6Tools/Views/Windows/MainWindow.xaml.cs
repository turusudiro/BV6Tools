using BV6Tools.Common;
using BV6Tools.Services;
using BV6Tools.ViewModels.Windows;
using BV6Tools.Views.Pages;
using System.ComponentModel;
using System.Windows.Interop;
using Wpf.Ui;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace BV6Tools.Views.Windows;

public partial class MainWindow : INavigationWindow
{
    private readonly ISettingsService settingsService;

    public MainWindow(
        MainWindowViewModel viewModel,
        INavigationViewPageProvider navigationViewPageProvider,
        INavigationService navigationService,
        IContentDialogService ContentDialogService,
        ISnackbarService snackbarService,
        ISettingsService settingsService
    )
    {
        this.settingsService = settingsService;
        ViewModel = viewModel;
        DataContext = this;

        SystemThemeWatcher.Watch(this);

        ApplicationThemeManager.Apply(settingsService.Settings.CurrentTheme);

        InitializeComponent();
        SetPageService(navigationViewPageProvider);

        snackbarService.SetSnackbarPresenter(SnackbarPresenter);
        navigationService.SetNavigationControl(RootNavigation);
        ContentDialogService.SetDialogHost(RootContentDialog);
    }

    public MainWindowViewModel ViewModel { get; }

    INavigationView INavigationWindow.GetNavigation()
    {
        throw new NotImplementedException();
    }

    public void SetServiceProvider(IServiceProvider serviceProvider)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    ///     Raises the closed event.
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);

        // Make sure that closing this window will begin the process of closing the application.
        Application.Current.Shutdown();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (settingsService.Settings.CloseToTray)
        {
            e.Cancel = true;
            HideWindow();
            return;
        }

        base.OnClosing(e);
    }

    public void HideWindow()
    {
        SaveWindowPosition();
        Hide();
    }

    private void SaveWindowPosition()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        // Note: GetWindowPlacement never returns showCmd=3 (maximized) on WPF windows.
        // When maximized and hidden, Win32 stores the maximized intent in flags=2 (WPF_RESTORETOMAXIMIZED)
        // and resets showCmd=1. Handle this on restore side by checking flags instead.
        var state = WindowStateCommon.ClampMinimized(WindowStateCommon.GetWindowState(hwnd),
            settingsService.Settings.WINDOWPLACEMENT);

        if (!settingsService.Settings.WINDOWPLACEMENT.HasValue ||
            !WindowStateCommon.PositionEquals(settingsService.Settings.WINDOWPLACEMENT.Value, state))
        {
            settingsService.Save(x => x.WINDOWPLACEMENT = state);
        }

    }
    private void RootNavigation_SelectionChanged(NavigationView sender, RoutedEventArgs args)
    {
        RootNavigation.SetCurrentValue(
            NavigationView.HeaderVisibilityProperty,
            RootNavigation.SelectedItem?.TargetPageType != typeof(DashboardPage)
            ? Visibility.Visible
            : Visibility.Collapsed
            );
    }

    private void TitleBar_HelpClicked(TitleBar sender, RoutedEventArgs args)
    {
        RootNavigation.Navigate(typeof(GuidePage));
    }

    #region INavigationWindow methods

    public void CloseWindow() => Close();

    public INavigationView GetNavigation() => RootNavigation;
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.Property == VisibilityProperty && Visibility == Visibility.Visible)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (settingsService.Settings.WINDOWPLACEMENT.HasValue)
                {
                    var hwnd = new WindowInteropHelper(this).Handle;
                    var placement = settingsService.Settings.WINDOWPLACEMENT.Value;
                    WindowStateCommon.SetWindowState(hwnd, placement);

                    if (placement.flags == 2)
                    {
                        WindowState = WindowState.Maximized;
                    }
                }
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }
    public bool Navigate(Type pageType) => RootNavigation.Navigate(pageType);

    public void SetPageService(INavigationViewPageProvider navigationViewPageProvider) =>
        RootNavigation.SetPageProviderService(navigationViewPageProvider);

    public void ShowWindow()
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Show();
    }

    #endregion INavigationWindow methods

    private void TitleBar_MinimizeClicked(TitleBar _, RoutedEventArgs __)
    {
        if (settingsService.Settings.MinimizeToTray)
        {
            HideWindow();
        }
    }
}