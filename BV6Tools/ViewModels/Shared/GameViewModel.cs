using BV6Tools.Collections;
using System.ComponentModel;
using System.Windows.Media;

namespace BV6Tools.ViewModels.Shared;

public partial class GameViewModel : ObservableObject, IKeyed<uint>
{
    [ObservableProperty]
    public partial uint AppId { get; set; }

    [ObservableProperty]
    public partial ObservableDictionary<uint, DLCViewModel> DLC { get; set; } = [];
    [ObservableProperty]
    public partial ICollectionView? DLCView { get; set; }

    [ObservableProperty]
    public partial ImageSource? ImageSource { get; set; }

    [ObservableProperty]
    public partial bool IsEnabled { get; set; }

    public bool IsInitialized { get; set; }

    public uint Key => AppId;

    [ObservableProperty]
    public partial string? Name { get; set; }

    [ObservableProperty]
    public partial bool? SelectAll { get; set; }
    public override string ToString() => Name ?? AppId.ToString();
}