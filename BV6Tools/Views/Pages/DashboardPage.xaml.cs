using BV6Tools.Services.Injector;
using BV6Tools.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace BV6Tools.Views.Pages;

public partial class DashboardPage : INavigableView<DashboardViewModel>
{
    public DashboardViewModel ViewModel { get; }
    private readonly InjectorService injectorService;

    public DashboardPage(DashboardViewModel viewModel, InjectorService injectorService)
    {
        this.injectorService = injectorService;

        ViewModel = viewModel;
        DataContext = this;

        InitializeComponent();

        Loaded += (_, _) =>
        {
            if (Window.GetWindow(this) is { } window)
            {
                window.IsVisibleChanged += OnWindowVisibilityChanged;
                window.StateChanged += OnWindowStateChanged;
            }
        };
    }
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is { } window)
        {
            window.IsVisibleChanged += OnWindowVisibilityChanged;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is { } window)
        {
            window.IsVisibleChanged -= OnWindowVisibilityChanged;
        }
    }
    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        if (sender is not Window window) return;

        if (window.WindowState == WindowState.Minimized)
        {
            GamesPage.FreezeAutoSlide();
            injectorService.StopWatcher();
        }
        else
        {
            GamesPage.BeginAutoSlide();
            injectorService.StartWatcher();
        }
    }
    private void OnWindowVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if ((bool)e.NewValue)
        {
            GamesPage.BeginAutoSlide();
            injectorService.StartWatcher();
        }
        else
        {
            GamesPage.FreezeAutoSlide();
            injectorService.StopWatcher();
        }
    }
    private void GoToPreviousPage(object sender, RoutedEventArgs e)
    {
        GamesPage.MovePrevious();
    }

    private void GoToNextPage(object sender, RoutedEventArgs e)
    {
        GamesPage.MoveNext();
    }

    private void GameCard_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        GamesPage.FreezeAutoSlide();
    }

    private void GameCard_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        GamesPage.BeginAutoSlide();
    }
}