using AppPathsCommon;
using BV6Tools.Services.Database;
using BV6Tools.Services.Database.Models;
using GreenLumaCommon;
using STCommon;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;

namespace BV6Tools.Services.Injector
{
    public class InjectorManagerService
    {
        private const int MaxInjectRetries = 3;

        private readonly DatabaseService databaseService;
        private readonly InjectorService injectorService;
        private readonly ILoggerService logger;
        private readonly ISettingsService settingsService;
        private readonly IToastService toastService;

        private CancellationTokenSource? cancellationTokenSource;

        public InjectorManagerService(DatabaseService databaseService, InjectorService injectorService,
                    ILoggerService logger, ISettingsService settingsService, IToastService toastService)
        {
            this.databaseService = databaseService;
            this.injectorService = injectorService;
            this.logger = logger;
            this.settingsService = settingsService;
            this.toastService = toastService;

            injectorService.OnInjected += InjectorService_OnInjected;
            injectorService.LoadState(AppPaths.SteamProcess);
        }

        public void CancelInject()
        {
            try
            {
                cancellationTokenSource?.Cancel();
            }
            catch { }
            finally
            {
                cancellationTokenSource?.Dispose();
            }
        }

        /// <summary>
        /// Start Injection logic
        /// </summary>
        /// <returns><![CDATA[True]]> if success</returns>
        /// <exception cref="InvalidOperationException"></exception>
        public Task Inject(HashSet<uint> appids, ProcessMode mode, string? args, uint? gameAppId = null)
        {
            CancelInject();

            cancellationTokenSource = new CancellationTokenSource();
            return Inject(appids, mode, args, 1, gameAppId, cancellationTokenSource.Token);
        }

        public async Task WaitForWatcherCleanupAsync(int connectTimeoutMs = 500)
        {
            NamedPipeClientStream? client = null;
            try
            {
                client = new NamedPipeClientStream(".", SteamWatcherRunner.PipeName,
                    PipeDirection.InOut, PipeOptions.Asynchronous);

                await client.ConnectAsync(connectTimeoutMs);
            }
            catch
            {
                logger.Log("Failed to connect to Steam Watcher pipe. No cleanup is needed.");
                client?.Dispose();
                return;
            }

            using (client)
            {
                try
                {
                    using var writer = new StreamWriter(client, leaveOpen: true) { AutoFlush = true };
                    await writer.WriteLineAsync("--wait-cleanup");

                    using var reader = new StreamReader(client, leaveOpen: true);
                    logger.Log("Waiting for Steam Watcher cleanup to complete...");
                    await reader.ReadLineAsync();
                }
                catch (Exception ex)
                {
                    logger.LogError("Error occurred while waiting for watcher cleanup.", ex);
                }
            }
        }

        private async Task CleanGreenLuma()
        {
            await SteamCommon.Steam.KillSteamAsync();
            try
            {
                GreenLuma.CleanGreenLumaFiles(settingsService.Settings.SteamPath, AppPaths.GLPath);
            }
            catch (Exception ex)
            {
                logger.LogError(ex);
            }
        }

        private void CreateGameST(string destinationPath, uint appid)
        {
            string destination = Path.Combine(settingsService.Settings.SteamPath, "config", destinationPath);
            Directory.CreateDirectory(destination);

            string luaPath = Path.Combine(AppPaths.LuaPath, $"{appid}.lua");
            string destinationLuaPath = Path.Combine(destination, $"{appid}.lua");

            if (File.Exists(luaPath))
            {
                if (File.Exists(destinationLuaPath))
                {
                    File.Delete(destinationLuaPath);
                }
                File.CreateSymbolicLink(destinationLuaPath, luaPath);
            }

            string ticketPath = Path.Combine(AppPaths.LuaPath, "ticket.lua");
            string ticketPathDestination = Path.Combine(destination, "ticket.lua");

            if (File.Exists(ticketPath))
            {
                if (File.Exists(ticketPathDestination))
                {
                    File.Delete(ticketPathDestination);
                }
                File.CreateSymbolicLink(ticketPathDestination, ticketPath);
            }
        }

        private void CreateTicketST(HashSet<uint> appids, bool setEticket = false)
        {
            var tickets = databaseService.Database.LoadAll<TicketDb>();
            List<SetTicket> setTickets = [];

            foreach (var ticket in tickets)
            {
                if (!appids.Contains(ticket.AppId)) continue;
                if (ticket.AppTicketBytes != null)
                {
                    setTickets.Add(new SetTicket(ticket.AppId, TicketType.AppOwnership, ticket.AppTicketBytes));
                }
                if (setEticket && ticket.EncryptedTicketBytes != null)
                {
                    setTickets.Add(new SetTicket(ticket.AppId, TicketType.Encrypted, ticket.EncryptedTicketBytes));
                }
            }

            if (setTickets.Count > 0)
            {
                ST.SaveTicket(setTickets, Path.Combine(AppPaths.LuaPath, "ticket.lua"));
            }
        }

        private async Task Inject(HashSet<uint> appids, ProcessMode mode, string? args, int attempt, uint? gameAppId, CancellationToken token)
        {
            try
            {
                token.ThrowIfCancellationRequested();
                int pid = 0;
                switch (mode)
                {
                    case ProcessMode.SteamTools:
                        {
                            CreateTicketST(appids);

                            if (gameAppId.HasValue)
                            {
                                CreateGameST("stplug-in", gameAppId.Value);

                                pid = ST.StartSteamTools(AppPaths.STPath, settingsService.Settings.SteamPath, args: args, appids: appids);
                            }
                            else
                            {
                                pid = ST.StartSteamTools(AppPaths.STPath, settingsService.Settings.SteamPath, luaPath: AppPaths.LuaPath, args);
                            }
                            break;
                        }
                    case ProcessMode.OpenSteamTool:
                        {
                            CreateTicketST(appids, true);

                            if (gameAppId.HasValue)
                            {
                                CreateGameST("lua", gameAppId.Value);

                                pid = ST.StartOpenSteamTool(AppPaths.OpenSteamToolPath, settingsService.Settings.SteamPath, args: args, appids: appids);
                            }
                            else
                            {
                                pid = ST.StartOpenSteamTool(AppPaths.OpenSteamToolPath, settingsService.Settings.SteamPath, luaPath: AppPaths.LuaPath, args);
                            }
                            break;
                        }
                    default:
                        {
                            var glMode = GreenLumaMode.Stealth;
                            if (mode.HasFlag(ProcessMode.GreenLumaNormal))
                            {
                                glMode = GreenLumaMode.Normal;
                                var ownershipDirPath = Path.Combine(settingsService.Settings.SteamPath, "AppOwnershipTickets");
                                var encryptedDirPath = Path.Combine(settingsService.Settings.SteamPath, "EncryptedAppTickets");
                                Directory.CreateDirectory(ownershipDirPath);
                                Directory.CreateDirectory(encryptedDirPath);

                                var tickets = databaseService.Database.LoadAll<TicketDb>();

                                foreach (var ticket in tickets)
                                {
                                    if (!appids.Contains(ticket.AppId)) continue;
                                    if (ticket.AppTicketBytes != null)
                                    {
                                        var destination = Path.Combine(ownershipDirPath, $"Ticket.{ticket.AppId}");
                                        await File.WriteAllBytesAsync(destination, ticket.AppTicketBytes, token);
                                    }
                                    if (ticket.EncryptedTicketBytes != null)
                                    {
                                        var destination = Path.Combine(encryptedDirPath, $"EncryptedTicket.{ticket.AppId}");
                                        await File.WriteAllBytesAsync(destination, ticket.EncryptedTicketBytes, token);
                                    }
                                }
                            }

                            pid = await GreenLuma.StartGreenLuma(AppPaths.GLPath, settingsService.Settings.SteamPath, appids, args, glMode);
                            break;
                        }
                }

                if (pid == 0) throw new InvalidOperationException("Failed to detect steam with PID 0!");

                var isRunning = await SteamCommon.Steam.WaitForSteamAsync();
                token.ThrowIfCancellationRequested();
                if (!isRunning)
                {
                    toastService.Show(t => t
                    .AddText("Error")
                    .AddText("Steam did not respond after launch."), "SteamUpdatingTag");
                }

                // if steam active process from registry shows 0 or not equal with injected steam PID
                // assuming the steam is updating
                // then wait until it finish the update then reinject
                if (SteamCommon.Steam.GetSteamActiveProcessPid() != pid)
                {
                    if (attempt >= MaxInjectRetries)
                    {
                        logger.LogError("Timed out, Steam took too long to finish updating");
                    }
                    else
                    {
                        logger.Log("Steam is updating, waiting steam to finish update");
                        await SteamCommon.Steam.WaitForSteamPidChangeAsync(token);
                        token.ThrowIfCancellationRequested();
                        logger.Log("Steam updated, shutting down steam");
                        await SteamCommon.Steam.ShutdownSteamAsync();
                        token.ThrowIfCancellationRequested();
                        // reinject after steam update
                        logger.Log("Reinjecting Steam");
                        await Inject(appids, mode, args, attempt + 1, gameAppId, token);
                    }

                    return;
                }

                var state = new SteamProcessData(appids, mode, pid, settingsService.Settings.SteamPath, args);
                injectorService.RaiseInjected(state);
            }
            catch (OperationCanceledException) { throw; }
            catch (AggregateException ex)
            {
                logger.LogError(ex);
                if (mode.IsGreenLuma())
                {
                    await CleanGreenLuma();
                }
                foreach (var error in ex.InnerExceptions)
                {
                    logger.LogError(error);
                    injectorService.RaiseInjectFailed(mode, ex);
                }
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex);
                if (mode.HasFlag(ProcessMode.GreenLumaNormal))
                {
                    await CleanGreenLuma();
                }
                injectorService.RaiseInjectFailed(mode, ex);
                throw;
            }
        }

        private void InjectorService_OnInjected(SteamProcessData state)
        {
            logger.Log($"Steam injected with PID: {state.PID}; Mode: {state.Mode}; Args: {state.Args}");
            try
            {
                injectorService.SaveState(state, AppPaths.SteamProcess);
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
                }

                if (settingsService.Settings.OnInject.HasFlag(Models.OnInject.Exit))
                {
                    Application.Current.Dispatcher.Invoke(Application.Current.Shutdown);
                }
                else
                {
                    _ = injectorService.WaitForSteamExitAsync();
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex);
                injectorService.RaiseInjectFailed(state.Mode, ex);
            }
        }
    }
}