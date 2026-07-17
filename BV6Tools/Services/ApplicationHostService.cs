using AppPathsCommon;
using BV6Tools.Services.Injector;
using BV6Tools.ViewModels.Pages;
using BV6Tools.ViewModels.Pages.Lua;
using BV6Tools.Views.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wpf.Ui;

namespace BV6Tools.Services;

/// <summary>
///     Managed host of the application.
/// </summary>
public class ApplicationHostService(IServiceProvider serviceProvider) : IHostedService
{
    private INavigationWindow? _navigationWindow;

    /// <summary>
    ///     Triggered when the application host is ready to start the service.
    /// </summary>
    /// <param name="cancellationToken">Indicates that the start process has been aborted.</param>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await HandleActivationAsync();
    }

    /// <summary>
    ///     Triggered when the application host is performing a graceful shutdown.
    /// </summary>
    /// <param name="cancellationToken">Indicates that the shutdown process should no longer be graceful.</param>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
    }

    /// <summary>
    ///     Creates main window during activation.
    /// </summary>
    private async Task HandleActivationAsync()
    {
        await Task.CompletedTask;

        serviceProvider.GetRequiredService<TrayWindow>();

        if (App.StartSteam)
        {
            var toastService = serviceProvider.GetRequiredService<IToastService>();

            _ = Task.Run(async () =>
            {
                var logger = serviceProvider.GetRequiredService<ILoggerService>();
                logger.Log("Starting from startup");

                if (SteamCommon.Steam.IsSteamStartupEnabled())
                {
                    logger.Log("Steam startup detected, waiting steam to start");
                    // wait 10 secs to steam start from startup
                    await SteamCommon.Steam.WaitForSteamAsync(TimeSpan.FromSeconds(10));

                    // check if steam is running but steam active pid from registry is 0
                    // this means steam is on update state
                    if (SteamCommon.Steam.GetSteamActiveProcessPid() == 0)
                    {
                        // wait for steam active pid change, if not changed after 5 secs, kill steam
                        var pidChanged = await SteamCommon.Steam.WaitForSteamPidChangeAsync();
                        if (!pidChanged)
                        {
                            await SteamCommon.Steam.KillSteamAsync();
                        }
                    }
                    await SteamCommon.Steam.ShutdownSteamAsync();
                }

                var gameService = serviceProvider.GetRequiredService<GameService>();
                var settingsService = serviceProvider.GetRequiredService<ISettingsService>();
                var injectorManagerService = serviceProvider.GetRequiredService<InjectorManagerService>();
                await Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    try
                    {
                        var args = "-silent " + string.Join(" ", settingsService.Settings.SteamArgs);
                        await injectorManagerService.Inject(gameService.EnabledAppids, settingsService.Settings.Mode, args);
                    }
                    catch { }
                });
            });
        }

        if (!App.StartMinimized && !Application.Current.Windows.OfType<MainWindow>().Any())
        {
            _navigationWindow = serviceProvider.GetRequiredService<INavigationWindow>();
            _navigationWindow.ShowWindow();

            _ = _navigationWindow.Navigate(typeof(Views.Pages.DashboardPage));
        }

        await Task.CompletedTask;

        System.IO.Directory.CreateDirectory(AppPaths.ImagesPath);
        ImageCommon.ImageUtilites.Initialize(AppPaths.ImageFallbackPath);

        serviceProvider.GetRequiredService<ListPageViewModel>();
        serviceProvider.GetRequiredService<DepotPageViewModel>();
        serviceProvider.GetRequiredService<LuaPageViewModel>();
    }
}