using AppPathsCommon;
using BV6Tools.Services.Injector;
using BV6Tools.ViewModels.Pages;
using BV6Tools.ViewModels.Pages.Lua;
using BV6Tools.Views.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
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

        var injectorService = serviceProvider.GetRequiredService<InjectorService>();
        var toastService = serviceProvider.GetRequiredService<IToastService>();
        var settingsService = serviceProvider.GetRequiredService<ISettingsService>();
        var logger = serviceProvider.GetRequiredService<ILoggerService>();

        injectorService.OnInjected += async state =>
        {
            try
            {
                injectorService.SaveState(state);
                if (!settingsService.Settings.DisableCleanup && !state.Mode.HasFlag(ProcessMode.GreenLumaStealth))
                {
                    if (!Mutex.TryOpenExisting("BV6Tools_SteamWatcher", out _))
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = Environment.ProcessPath,
                            Arguments = "--steam-watcher",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                        });
                    }

                    using var pipe = new NamedPipeClientStream(".", "BV6Tools_SteamWatcher_Pipe", PipeDirection.Out);
                    pipe.Connect(3000);
                    using var writer = new StreamWriter(pipe) { AutoFlush = true };
                    writer.WriteLine(JsonSerializer.Serialize(state));

                    toastService.Show(t => t.AddText("Cleanup Scheduled").AddText("Monitoring Steam. Files will be removed automatically upon exit."), "SteamCleanupTag");
                }

                if (settingsService.Settings.OnInject.HasFlag(Models.OnInject.Exit))
                {
                    Application.Current.Shutdown();
                }
                else
                {
                    _ = injectorService.WaitForSteamExitAsync();
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex);
                toastService.Show(t => t.AddText("Failed to schedule cleanup").AddText(ex.Message), "SteamCleanupTag");
                injectorService.RaiseInjectFailed(ex);
            }
        };

        serviceProvider.GetRequiredService<TrayWindow>();

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