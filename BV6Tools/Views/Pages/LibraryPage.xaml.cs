using BV6Tools.ViewModels.Pages;
using System.Windows.Controls;
using System.Windows.Media;
using Wpf.Ui.Abstractions.Controls;

namespace BV6Tools.Views.Pages;

/// <summary>
///     Interaction logic for LibraryPage.xaml
/// </summary>
public partial class LibraryPage : INavigableView<LibraryPageViewModel>
{
    private ScrollViewer? scrollViewer;

    public LibraryPage(LibraryPageViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }

    public LibraryPageViewModel ViewModel { get; }

    private void Button_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.RescanInstalledGames();
        scrollViewer?.ScrollToTop();
    }

    private void LibraryPage_Loaded(object sender, RoutedEventArgs e)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(GamesVirtualizingItemsControl); i++)
        {
            if (VisualTreeHelper.GetChild(GamesVirtualizingItemsControl, i) is ScrollViewer scrollViewer)
            {
                this.scrollViewer = scrollViewer;
                break;
            }
        }
    }
}