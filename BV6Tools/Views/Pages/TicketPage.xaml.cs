using BV6Tools.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace BV6Tools.Views.Pages
{
    /// <summary>
    /// Interaction logic for TicketPage.xaml
    /// </summary>
    public partial class TicketPage : INavigableView<TicketPageViewModel>
    {
        public TicketPage(TicketPageViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = this;

            InitializeComponent();
        }

        public TicketPageViewModel ViewModel { get; }
    }
}
