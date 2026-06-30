using BV6Tools.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace BV6Tools.Views.Pages;

public partial class SettingsPage : INavigableView<SettingsPageViewModel>
{
    public SettingsPage(SettingsPageViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;

        InitializeComponent();
    }

    public SettingsPageViewModel ViewModel { get; }
}