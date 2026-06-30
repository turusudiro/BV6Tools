using BV6Tools.Collections;
using Generator.Equals;

namespace BV6Tools.ViewModels.Shared;

[Equatable]
public partial class DLCViewModel : ObservableObject, IKeyed<uint>
{
    [ObservableProperty]
    public partial uint AppId { get; set; }

    [ObservableProperty]
    public partial bool IsEnabled { get; set; }

    public uint Key => AppId;

    [ObservableProperty]
    public partial string? Name { get; set; }

    public DLCViewModel Clone()
    {
        return new DLCViewModel
        {
            AppId = AppId,
            Name = Name,
            IsEnabled = IsEnabled
        };
    }

    public override string ToString() => Name ?? AppId.ToString();
}