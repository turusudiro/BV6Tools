using BV6Tools.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace BV6Tools.Views.Pages;

public partial class SearchPage : INavigableView<SearchPageViewModel>
{
    public SearchPageViewModel ViewModel { get; }

    public SearchPage(SearchPageViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;

        InitializeComponent();
    }

    private void GoToPreviousPage(object sender, RoutedEventArgs e)
    {
        GamesPage.MovePrevious();
    }

    private void GoToNextPage(object sender, RoutedEventArgs e)
    {
        GamesPage.MoveNext();
    }

}