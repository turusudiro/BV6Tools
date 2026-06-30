using BV6Tools.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui.Controls;

namespace BV6Tools.Views.Pages;

/// <summary>
///     Interaction logic for GameLaunchingPage.xaml
/// </summary>
public partial class GameLaunchingPage : INavigableView<GameLaunchingPageViewModel>
{
    public GameLaunchingPage(GameLaunchingPageViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }

    public GameLaunchingPageViewModel ViewModel { get; }

    private void Flyout_Closed(Flyout sender, RoutedEventArgs args)
    {
        NewDlcAppid.Clear();
        NewDlcName.Clear();
        ViewModel.IsFlyoutOpen = false;
    }
}