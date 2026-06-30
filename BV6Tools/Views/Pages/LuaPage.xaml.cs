using BV6Tools.ViewModels.Pages.Lua;
using Wpf.Ui.Abstractions.Controls;

namespace BV6Tools.Views.Pages
{
    /// <summary>
    /// Interaction logic for LuaPage.xaml
    /// </summary>
    public partial class LuaPage : INavigableView<LuaPageViewModel>
    {
        public LuaPage(LuaPageViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = this;

            InitializeComponent();
        }

        public LuaPageViewModel ViewModel { get; }

        private void Grid_PreviewDragOver(object sender, DragEventArgs e)
        {
            DropOverlay.Visibility = Visibility.Visible;
            DockPanelContent.Visibility = Visibility.Collapsed;
            e.Handled = true;
        }

        private void Grid_PreviewDrop(object sender, DragEventArgs e)
        {
            DropOverlay.Visibility = Visibility.Collapsed;
            DockPanelContent.Visibility = Visibility.Visible;
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            ViewModel.LuaZipDropCommand.Execute(files);
        }

        private void Grid_PreviewDragLeave(object sender, DragEventArgs e)
        {
            DropOverlay.Visibility = Visibility.Collapsed;
            DockPanelContent.Visibility = Visibility.Visible;
            e.Handled = true;
        }
    }
}
