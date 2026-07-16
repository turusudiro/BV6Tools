using AppPathsCommon;
using BV6Tools.Services.Injector;
using GreenLumaCommon;
using Microsoft.Win32;
using STCommon;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Windows.Threading;

namespace BV6Tools.Services
{
    internal static class SteamWatcherRunner
    {
        private const string MutexName = "BV6Tools_SteamWatcher";
        private const string PipeName = "BV6Tools_SteamWatcher_Pipe";

        public static void Run()
        {
            var thread = new Thread(ThreadMain);
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
        }

        private static Task HandleCleanup(SteamProcessData state, IToastService toastService, Func<bool> isSessionEnding)
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
                    string destinationLuaTicketPath = Path.Combine(AppPaths.LuaPath, "ticket.lua");
                    if (File.Exists(destinationLuaTicketPath))
                    {
                        File.Delete(destinationLuaTicketPath);
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

                if (!isSessionEnding())
                {
                    toastService.Show(t => t.AddText("Cleanup Complete").AddText("Steam cleanup successful."), "SteamCleanupTag");
                }
            }
            catch (Exception ex)
            {
                if (!isSessionEnding())
                {
                    toastService.Show(t => t.AddText("Cleanup Failed").AddText($"Error: {ex.Message}"), "SteamCleanupTag");
                }
            }
            finally
            {
                if (!isSessionEnding())
                {
                    toastService.ClearAll().GetAwaiter().GetResult();
                }
            }

            return Task.CompletedTask;
        }

        private static async Task ListenForStopAsync(InjectorService injectorService, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                var pipeSecurity = new PipeSecurity();

                var usersGroup = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
                pipeSecurity.AddAccessRule(new PipeAccessRule(
                    usersGroup,
                    PipeAccessRights.ReadWrite,
                    AccessControlType.Allow
                ));

                using var pipe = NamedPipeServerStreamAcl.Create(
                    pipeName: PipeName,
                    direction: PipeDirection.InOut,
                    maxNumberOfServerInstances: 1,
                    transmissionMode: PipeTransmissionMode.Byte,
                    options: PipeOptions.Asynchronous,
                    inBufferSize: 0,
                    outBufferSize: 0,
                    pipeSecurity: pipeSecurity
                );

                try
                {
                    await pipe.WaitForConnectionAsync(token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                using var reader = new StreamReader(pipe);
                var line = await reader.ReadLineAsync(token);
                if (line == "--stop")
                {
                    injectorService.StopWatcher();
                    return;
                }
            }
        }

        private static async Task<SteamProcessData?> ReceiveInitialStateAsync(CancellationToken token)
        {
            using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.In,
                maxNumberOfServerInstances: 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            await pipe.WaitForConnectionAsync(token);

            using var reader = new StreamReader(pipe);
            var line = await reader.ReadLineAsync(token);
            if (line == null || line == "--stop") return null;

            return JsonSerializer.Deserialize<SteamProcessData>(line);
        }

        private static void ThreadMain()
        {
            using var mutex = new Mutex(true, MutexName, out bool isNew);
            if (!isNew) return;

            var toastService = new ToastService();
            using var injectorService = new InjectorService();

            bool isSessionEnding = false;
            var cts = new CancellationTokenSource();
            var staDispatcher = Dispatcher.CurrentDispatcher;

            SystemEvents.SessionEnding += (_, _) =>
            {
                isSessionEnding = true;
                SteamCommon.Steam.KillSteamAsync().GetAwaiter().GetResult();
                injectorService.StopWatcher();
                toastService.ClearAll(true).GetAwaiter().GetResult();
                cts.Cancel();
                staDispatcher.InvokeShutdown();
            };

            injectorService.CleanupTask += state => HandleCleanup(state, toastService, () => isSessionEnding);

            _ = Task.Run(() => WatchAsync(injectorService, cts, staDispatcher));

            Dispatcher.Run();
        }

        private static async Task WatchAsync(InjectorService injectorService, CancellationTokenSource cts, Dispatcher staDispatcher)
        {
            try
            {
                var state = await ReceiveInitialStateAsync(cts.Token);
                if (state is not { } stateData)
                {
                    cts.Cancel();
                    return;
                }

                injectorService.SaveState(stateData, AppPaths.SteamProcess);

                var exitTask = injectorService.WaitForSteamExitAsync();
                var stopTask = ListenForStopAsync(injectorService, cts.Token);
                await Task.WhenAny(exitTask, stopTask);
            }
            catch (OperationCanceledException) { }
            finally
            {
                cts.Cancel();
                staDispatcher.InvokeShutdown();
            }
        }
    }
}