using AppPathsCommon;
using BV6Tools.Services;
using BV6Tools.Services.Database;
using BV6Tools.Services.Injector;
using BV6Tools.Services.ManifestDownloader;
using BV6Tools.ViewModels.Pages;
using BV6Tools.ViewModels.Pages.Lua;
using BV6Tools.ViewModels.Windows;
using BV6Tools.Views.Pages;
using BV6Tools.Views.Windows;
using GreenLumaCommon;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Win32;
using STCommon;
using SteamCommon;
using System.IO;
using System.IO.Pipes;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Threading;
using Wpf.Ui;
using Wpf.Ui.DependencyInjection;
using Wpf.Ui.Tray;

namespace BV6Tools;

/// <summary>
///     Interaction logic for App.xaml
/// </summary>
public partial class App
{
    // The.NET Generic Host provides dependency injection, configuration, logging, and other services.
    // https://docs.microsoft.com/dotnet/core/extensions/generic-host
    // https://docs.microsoft.com/dotnet/core/extensions/dependency-injection
    // https://docs.microsoft.com/dotnet/core/extensions/configuration
    // https://docs.microsoft.com/dotnet/core/extensions/logging
    private static readonly IHost _host = Host.CreateDefaultBuilder()
        .ConfigureAppConfiguration(c =>
        {
            var basePath =
                Path.GetDirectoryName(AppContext.BaseDirectory)
                ?? throw new DirectoryNotFoundException(
                    "Unable to find the base directory of the application."
                );
            _ = c.SetBasePath(basePath);
        })
        .ConfigureServices(
            (context, services) =>
            {
                _ = services.AddNavigationViewPageProvider();

                // App Host
                _ = services.AddHostedService<ApplicationHostService>();

                // Register Shared Navigation Service
                // Theme manipulation
                _ = services.AddSingleton<IThemeService, ThemeService>();

                // TaskBar manipulation
                _ = services.AddSingleton<ITaskBarService, TaskBarService>();

                // Service containing navigation, same as INavigationWindow... but without window
                _ = services.AddSingleton<INavigationService, NavigationService>();

                // Main window with navigation
                _ = services.AddSingleton<INavigationWindow, MainWindow>();
                _ = services.AddSingleton<MainWindowViewModel>();

                // TrayIcon
                _ = services.AddSingleton<INotifyIconService, NotifyIconService>();
                _ = services.AddSingleton<TrayWindow>();

                // BV6Tools
                _ = services.AddSingleton<IContentDialogService, ContentDialogService>();

                _ = services.AddSingleton<ISnackbarService, SnackbarService>();

                _ = services.AddSingleton<HttpClient>();
                _ = services.AddSingleton<IManifestDownloader, ManifestDownloader>();

                _ = services.AddSingleton<DashboardPage>();
                _ = services.AddSingleton<DashboardViewModel>();
                _ = services.AddSingleton<LibraryPage>();
                _ = services.AddSingleton<LibraryPageViewModel>();
                _ = services.AddSingleton<ListPage>();
                _ = services.AddSingleton<ListPageViewModel>();
                _ = services.AddSingleton<SearchPage>();
                _ = services.AddSingleton<SearchPageViewModel>();
                _ = services.AddSingleton<SettingsPage>();
                _ = services.AddSingleton<SettingsPageViewModel>();
                _ = services.AddSingleton<GameLaunchingPage>();
                _ = services.AddSingleton<GameLaunchingPageViewModel>();
                _ = services.AddSingleton<DepotPage>();
                _ = services.AddSingleton<DepotPageViewModel>();
                _ = services.AddSingleton<LuaPage>();
                _ = services.AddSingleton<LuaPageViewModel>();
                _ = services.AddSingleton<TicketPage>();
                _ = services.AddSingleton<TicketPageViewModel>();
                _ = services.AddSingleton<GuidePage>();
                _ = services.AddHttpClient();
                _ = services.AddSingleton<HttpClientService>();
                _ = services.AddSingleton<ILoggerService, FileLoggerService>();
                _ = services.AddSingleton<ISettingsService, SettingsService>();
                _ = services.AddSingleton<GameService>();
                _ = services.AddSingleton<DatabaseService>();
                _ = services.AddSingleton<InjectorService>();
                _ = services.AddSingleton<IToastService, ToastService>();
            }
            )
        .Build();

    private static CancellationTokenSource? _cts;
    private static Mutex? _mutex;

    /// <summary>
    ///     Gets services.
    /// </summary>
    public static IServiceProvider Services => _host.Services;

    public static bool StartMinimized { get; private set; }
    public static bool StartSteam { get; private set; }

    private static void StartPipeServer()
    {
        _cts = new CancellationTokenSource();

        Task.Run(async () =>
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                using var server = new NamedPipeServerStream("BV6Tools_Pipe");
                await server.WaitForConnectionAsync(_cts.Token);

                using var reader = new StreamReader(server);
                var msg = await reader.ReadToEndAsync();

                if (msg == "show")
                {
                    await Current.Dispatcher.InvokeAsync(() =>
                    {
                        var navigationWindow = Services.GetRequiredService<INavigationWindow>();
                        navigationWindow.ShowWindow();
                    });
                }
            }
        }, _cts.Token);
    }

    /// <summary>
    ///     Occurs when an exception is thrown by an application but not handled.
    /// </summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // For more info see https://docs.microsoft.com/en-us/dotnet/api/system.windows.application.dispatcherunhandledexception?view=windowsdesktop-6.0
        var logger = Services.GetService<ILoggerService>();
        logger?.Log($"Unhandled exception: {e.Exception}");
    }

    /// <summary>
    ///     Occurs when the application is closing.
    /// </summary>
    private async void OnExit(object sender, ExitEventArgs e)
    {
        _cts?.Cancel();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();

        var toastService = Services.GetService<IToastService>();
        if (toastService != null)
        {
            Current.MainWindow?.Hide();
            await toastService.ClearAll();
        }

        var logger = Services.GetService<ILoggerService>();
        logger?.Log("Application exited");

        await _host.StopAsync();
        _host.Dispose();
    }

    /// <summary>
    ///     Occurs when the application is loading.
    /// </summary>
    private async void OnStartup(object sender, StartupEventArgs e)
    {
        if (e.Args.Length > 0 && e.Args[0] == "--steam-watcher")
        {
            var thread = new Thread(() =>
            {
                using var mutex = new Mutex(true, "BV6Tools_SteamWatcher", out bool isNew);
                if (!isNew) return;

                var toastService = new ToastService();
                using var injectorService = new InjectorService();

                bool isSessionEnding = false;

                SystemEvents.SessionEnding += (s, e) =>
                {
                    isSessionEnding = true;
                    Steam.KillSteamAsync().GetAwaiter().GetResult();
                    injectorService.StopWatcher();
                    toastService.ClearAll(true).GetAwaiter().GetResult();
                };


                injectorService.CleanupTask += state =>
                {
                    try
                    {
                        if (state.Mode == ProcessMode.GreenLumaStealth) return Task.CompletedTask;

                        if (state.Mode == ProcessMode.GreenLumaNormal)
                        {
                            GreenLuma.CleanGreenLumaFiles(state.Path, AppPaths.GLPath);
                        }
                        else
                        {
                            string destionationLuaTicketPath = Path.Combine(AppPaths.LuaPath, "ticket.lua");
                            if (File.Exists(destionationLuaTicketPath))
                            {
                                File.Delete(destionationLuaTicketPath);
                            }
                            if (state.Mode == ProcessMode.SteamTools)
                            {
                                ST.DeleteSteamToolsFiles(state.Path);
                            }
                            else
                            {
                                ST.DeleteOpenSteamToolFiles(state.Path);
                            }
                        }

                        if (!isSessionEnding)
                        {
                            toastService.Show(t => t.AddText("Cleanup Complete").AddText("Steam cleanup successful."), "SteamCleanupTag");
                        }
                    }
                    catch (Exception ex)
                    {
                        if (!isSessionEnding)
                        {
                            toastService.Show(t => t.AddText("Cleanup Failed").AddText($"Error: {ex.Message}"), "SteamCleanupTag");
                        }
                    }
                    finally
                    {
                        if (!isSessionEnding)
                        {
                            toastService.ClearAll().GetAwaiter().GetResult();
                        }
                    }
                    return Task.CompletedTask;
                };

                try
                {
                    using var pipe = new NamedPipeServerStream("BV6Tools_SteamWatcher_Pipe", PipeDirection.In,
                        maxNumberOfServerInstances: 1, PipeTransmissionMode.Byte);
                    pipe.WaitForConnection();

                    using var reader = new StreamReader(pipe);
                    var line = reader.ReadLine();
                    if (line == null) return;

                    if (line == "--stop")
                    {
                        injectorService.StopWatcher();
                        return;
                    }

                    var state = JsonSerializer.Deserialize<SteamProcessData>(line);

                    injectorService.SaveState(state, AppPaths.SteamProcess);
                    injectorService.WaitForSteamExitAsync().GetAwaiter().GetResult();
                }
                catch { }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            Current.Shutdown();
            return;
        }

        if (e.Args.Length > 0 && e.Args[0] == "--extract-ticket")
        {
            var thread = new Thread(() => SteamTicketExtractor.RunWorker(e.Args));
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            Current.Shutdown();
            return;
        }

        _mutex = new Mutex(true, "BV6Tools_SingleInstance", out bool isNewInstance);

        if (!isNewInstance)
        {
            using var client = new NamedPipeClientStream("BV6Tools_Pipe");
            try
            {
                client.Connect(1000);
                using var writer = new StreamWriter(client);
                writer.Write("show");
            }
            catch { }

            Current.Shutdown();
            return;
        }

        StartPipeServer();

        if (e.Args.Contains("--minimized"))
        {
            StartMinimized = true;
        }

        if (e.Args.Contains("--startsteam"))
        {
            StartMinimized = true;
            StartSteam = true;
        }

        await _host.StartAsync();
    }
}