using BV6Tools.Common;
using BV6Tools.Services.Injector;
using Generator.Equals;
using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Wpf.Ui.Appearance;

namespace BV6Tools.Models;

public enum OnInject
{
    [Description("None")] None,
    [Description("Minimize to tray")] Minimize,
    [Description("Exit app")] Exit
}

public enum StartupMode
{
    [Description("Disabled")] None,
    [Description("Start minimized")] Minimized,
    [Description("Start and inject")] AutoInject
}

[Equatable(Explicit = true)]
public partial class AppSettings : ObservableObject
{
    private static readonly HashSet<string> _excludedFromRestore =
    [
        nameof(ActiveProfileId)
    ];

    [ObservableProperty]
    public partial int ActiveProfileId { get; set; } = 1;

    [DefaultEquality]
    [ObservableProperty]
    public partial bool CloseToTray { get; set; }

    [DefaultEquality]
    [ObservableProperty]
    public partial ApplicationTheme CurrentTheme { get; set; } = ApplicationTheme.Dark;

    [DefaultEquality]
    [ObservableProperty]
    public partial bool DisableCleanup { get; set; }

    [ObservableProperty]
    public partial bool FetchOnlineDepotInfo { get; set; }

    [ObservableProperty]
    public partial double LibraryViewBoxMargin { get; set; } = 10;

    [ObservableProperty]
    public partial double LibraryViewBoxSize { get; set; } = 200;

    [DefaultEquality]
    [ObservableProperty]
    public partial bool MinimizeToTray { get; set; }

    [DefaultEquality]
    [ObservableProperty]
    public partial ProcessMode Mode { get; set; } = ProcessMode.GreenLumaStealth;

    [DefaultEquality]
    [ObservableProperty]
    public partial OnInject OnInject { get; set; } = OnInject.None;

    [DefaultEquality]
    [ObservableProperty]
    public partial string? SteamArgs { get; set; }

    [DefaultEquality]
    [ObservableProperty]
    public partial string SteamPath { get; set; } = string.Empty;

    [JsonPropertyName("WindowPlacement")]
    public WINDOWPLACEMENT? WINDOWPLACEMENT { get; set; }

    public AppSettings Clone()
    {
        var json = JsonSerializer.Serialize(this);
        return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
    }

    public void RestoreFromSnapshot(AppSettings snapshot)
    {
        if (snapshot == null) return;

        var properties = typeof(AppSettings).GetProperties(
            BindingFlags.Public | BindingFlags.Instance);

        foreach (var prop in properties)
        {
            if (prop.CanRead && prop.CanWrite && !_excludedFromRestore.Contains(prop.Name))
            {
                prop.SetValue(this, prop.GetValue(snapshot));
                OnPropertyChanged(prop.Name);
            }
        }
    }
}