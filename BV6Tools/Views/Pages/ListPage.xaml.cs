using BV6Tools.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace BV6Tools.Views.Pages;

/// <summary>
///     Interaction logic for ListPage.xaml
/// </summary>
public partial class ListPage : INavigableView<ListPageViewModel>
{
    public ListPage(ListPageViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }

    public ListPageViewModel ViewModel { get; }
}