using Wpf.Ui.Controls;

namespace BV6Tools.Views.Dialogs;

/// <summary>
///     Interaction logic for InputDialog.xaml
/// </summary>
[INotifyPropertyChanged]
public partial class InputDialog : ContentDialog
{
    public InputDialog(string title, bool isInputNumber = false)
    {
        InitializeComponent();
        DataContext = this;
        Title = title;
        IsInputNumber = isInputNumber;
    }

    [ObservableProperty]
    public partial string InputText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string InputTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsInputNumber { get; set; }

    [ObservableProperty]
    public partial string PlaceHolderInputText { get; set; } = string.Empty;
}