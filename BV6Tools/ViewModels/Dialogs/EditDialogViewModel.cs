using BV6Tools.ViewModels.Shared;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Input;
using Wpf.Ui.Controls;

namespace BV6Tools.ViewModels.Dialogs;

public partial class EditDialogViewModel : ObservableObject
{
    private string? _name;

    [NotifyPropertyChangedFor(nameof(IsSaveVisible))]
    [NotifyPropertyChangedFor(nameof(NumberBoxAppID))]
    [ObservableProperty]
    public partial uint? AppId { get; set; }

    [ObservableProperty]
    public partial GameViewModel Game { get; set; }

    [ObservableProperty]
    public partial IEnumerable<AppViewModel> Games { get; set; }

    public bool IsSaveVisible => IsSaveValid();

    public string? Name
    {
        get => _name;
        set => SetProperty(ref _name, string.IsNullOrWhiteSpace(value) ? null : value);
    }

    public double? NumberBoxAppID
    {
        get => AppId ?? 0;
        set
        {
            AppId = uint.TryParse(value.ToString(), out var u) ? u : 0;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AppId));
        }
    }

    [ObservableProperty]
    public partial bool ParentEdit { get; set; }

    [ObservableProperty]
    public partial string PlaceHolder { get; set; }

    [NotifyPropertyChangedFor(nameof(IsSaveVisible))]
    [ObservableProperty]
    public partial AppViewModel SelectedGame { get; set; }

    [ObservableProperty]
    public partial string? Title { get; set; }

    private bool IsSaveValid()
    {
        if (!AppId.HasValue || AppId.Value == 0) return false;
        if (ParentEdit && SelectedGame == null) return false;
        return true;
    }
}