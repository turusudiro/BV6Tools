using BV6Tools.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace BV6Tools.Views.Pages;

/// <summary>
///     Interaction logic for DepotPage.xaml
/// </summary>
public partial class DepotPage : INavigableView<DepotPageViewModel>
{
    public DepotPage(DepotPageViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;

        InitializeComponent();
    }

    public DepotPageViewModel ViewModel { get; }
}