using BV6Tools.ViewModels.Dialogs;

namespace BV6Tools.Views.Dialogs;

/// <summary>
/// Interaction logic for EditDialog.xaml
/// </summary>
public partial class EditDialog
{
    public EditDialog(EditDialogViewModel viewModel)
    {
        DataContext = this;
        ViewModel = viewModel;

        InitializeComponent();
    }

    public EditDialogViewModel ViewModel { get; }
}