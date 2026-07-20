using BV6Tools.Services;
using BV6Tools.Services.Injector;
using BV6Tools.ViewModels.Pages;
using BV6Tools.Views.Pages;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Wpf.Ui;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;

namespace BV6Tools.Views.Windows
{
    /// <summary>
    /// Interaction logic for TrayWindow.xaml
    /// </summary>
    public partial class TrayWindow
    {
        private readonly IContentDialogService contentDialogService;
        private readonly MenuItem stopSteamMenu;
        private INavigationWindow? navigationWindow;

        public TrayWindow(ISettingsService settingsService, IContentDialogService contentDialogService, InjectorService injectorService)
        {
            this.contentDialogService = contentDialogService;

            injectorService.OnInjectFailed += InjectorService_OnInjectFailed;

            stopSteamMenu = new MenuItem { Header = "Stop Steam", Command = StopSteamCommand };

            DataContext = this;

            SystemThemeWatcher.Watch(this);

            ApplicationThemeManager.Apply(settingsService.Settings.CurrentTheme);

            TrayMenuItems =
            [
                new MenuItem { Header = "Open", Command = OpenWindowCommand },
                new MenuItem { Header = "Inject Steam", Command = App.Services.GetRequiredService<DashboardViewModel>().StartSteamCommand, CommandParameter = true },
                stopSteamMenu,
                new MenuItem { Header = "Exit", Command = ExitAppCommand }
            ];

            InitializeComponent();

            Show();
            Hide();

            /// fix wpf ui not properly register trayicon, idk why it need to be this way
            Application.Current.MainWindow = this;

            TrayIcon.Register();

            /// set the mainwindow back to null for MainWindow.xaml.cs
            Application.Current.MainWindow = null;
        }

        public object[] TrayMenuItems { get; set; }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            source.AddHook(WndProc);
        }

        [RelayCommand]
        private static void ExitApp()
        {
            Application.Current.Shutdown();
        }

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool SetForegroundWindow(IntPtr hWnd);

        [RelayCommand]
        private static void StopSteam()
        {
            SteamCommon.Steam.ShutdownSteam();
            if (SteamCommon.Steam.IsSteamRunning())
            {
                SteamCommon.Steam.KillSteam();
            }
        }

        private async void InjectorService_OnInjectFailed(object sender, Exception ex)
        {
            switch (ex)
            {
                case IOException ioEx when ioEx.HResult == unchecked((int)0x80070522):
                    if (Application.Current.MainWindow?.IsVisible != true)
                    {
                        Wpf.Ui.Controls.MessageBox messageBox = new()
                        {
                            Title = "Error",
                            Content = $"Cannot inject Steam in {sender} Mode. " +
                            Environment.NewLine +
                            "Developer Mode is disabled, Please enable developer mode." +
                            Environment.NewLine +
                            "Or restart BV6Tools as admin instead.",
                            PrimaryButtonText = "Open developer settings",
                            CloseButtonText = "Cancel"
                        };

                        if (await messageBox.ShowDialogAsync(true) != Wpf.Ui.Controls.MessageBoxResult.Primary) return;
                    }
                    else
                    {
                        var dialog = new SimpleContentDialogCreateOptions()
                        {
                            Title = "Error",
                            Content = $"Cannot inject Steam in {sender} Mode. " +
                            Environment.NewLine +
                            "Developer Mode is disabled, Please enable developer mode." +
                            Environment.NewLine +
                            "Or restart BV6Tools as admin instead.",
                            PrimaryButtonText = "Open developer settings",
                            CloseButtonText = "Cancel"
                        };
                        if (await contentDialogService.ShowSimpleDialogAsync(dialog) != ContentDialogResult.Primary) return;
                    }
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "ms-settings:developers",
                            UseShellExecute = true
                        });
                    }
                    catch (Exception e)
                    {
                        Wpf.Ui.Controls.MessageBox messageBox = new()
                        {
                            Title = "Could not open Settings window automatically",
                            Content = e.Message,
                        };
                        await messageBox.ShowDialogAsync(true);
                    }
                    break;
            }
        }

        [RelayCommand]
        private void OpenWindow()
        {
            bool isFirstOpen = navigationWindow == null;
            navigationWindow ??= App.Services.GetRequiredService<INavigationWindow>();

            navigationWindow.ShowWindow();

            if (isFirstOpen)
            {
                navigationWindow.Navigate(typeof(DashboardPage));
            }

            var hwnd = new WindowInteropHelper((Window)navigationWindow).Handle;
            SetForegroundWindow(hwnd);
        }

        private void TrayIcon_LeftClick(Wpf.Ui.Tray.Controls.NotifyIcon sender, RoutedEventArgs e)
        {
            if (!Application.Current.Windows.OfType<MainWindow>().Any())
            {
                OpenWindowCommand.Execute(default);
                return;
            }

            if (Application.Current.MainWindow.IsVisible)
            {
                (Application.Current.MainWindow as MainWindow)?.HideWindow();
            }
            else
            {
                OpenWindowCommand.Execute(default);
            }
        }

        private void TrayIcon_RightClick(Wpf.Ui.Tray.Controls.NotifyIcon sender, RoutedEventArgs e)
        {
            stopSteamMenu.Visibility = SteamCommon.Steam.IsSteamRunning() ? Visibility.Visible : Visibility.Collapsed;
        }

        #region Taskbar Created

        private static readonly uint WM_TASKBARCREATED =
            RegisterWindowMessage("TaskbarCreated");

        [LibraryImport("user32.dll", EntryPoint = "RegisterWindowMessageW", StringMarshalling = StringMarshalling.Utf16)]
        private static partial uint RegisterWindowMessage(string lpString);

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == (int)WM_TASKBARCREATED)
            {
                Dispatcher.BeginInvoke(() =>
                {
                    var previousMainWindow = Application.Current.MainWindow;
                    Application.Current.MainWindow = this;
                    TrayIcon.Unregister();
                    TrayIcon.Register();
                    Application.Current.MainWindow = previousMainWindow;
                });
            }
            return IntPtr.Zero;
        }

        #endregion Taskbar Created
    }
}