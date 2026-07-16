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
        internal const string PipeName = "BV6Tools_SteamWatcher_Pipe";
        private const string MutexName = "BV6Tools_SteamWatcher";

        public static void Run()
        {
            var thread = new Thread(ThreadMain);
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
        }

        private static Task HandleCleanup(SteamProcessData state, ToastService toastService, Func<bool> isSessionEnding)
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

        private static async Task ListenForCommandsAsync(TaskCompletionSource<bool> cleanupDoneTcs, CancellationToken token)
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
                    Environment.Exit(0);
                    return;
                }

                if (line == "--wait-cleanup")
                {
                    try
                    {
                        await cleanupDoneTcs.Task.WaitAsync(token);
                        using var writer = new StreamWriter(pipe) { AutoFlush = true };
                        await writer.WriteLineAsync();
                    }
                    catch (OperationCanceledException) { }
                    return;
                }
            }
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
            var cleanupDoneTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task? exitTask = null;

            SystemEvents.SessionEnding += (_, _) =>
            {
                isSessionEnding = true;
                SteamCommon.Steam.KillSteamAsync().GetAwaiter().GetResult();
                exitTask?.GetAwaiter().GetResult();
                toastService.ClearAll(true).GetAwaiter().GetResult();
                cts.Cancel();
                staDispatcher.InvokeShutdown();
            };

            injectorService.CleanupTask += state =>
            {
                var task = HandleCleanup(state, toastService, () => isSessionEnding);
                return task.ContinueWith(_ => cleanupDoneTcs.TrySetResult(true));
            };

            _ = Task.Run(() =>
            {
                exitTask = WatchAsync(injectorService, toastService, cts, staDispatcher, cleanupDoneTcs);
            });

            Dispatcher.Run();
        }

        private static async Task WatchAsync(InjectorService injectorService, ToastService toastService,
            CancellationTokenSource cts, Dispatcher staDispatcher, TaskCompletionSource<bool> cleanupDoneTcs)
        {
            try
            {
                var json = File.ReadAllText(AppPaths.SteamProcess);
                var state = JsonSerializer.Deserialize<SteamProcessData>(json);
                injectorService.SaveState(state);
                var exitTask = injectorService.WaitForSteamExitAsync();
                var stopTask = ListenForCommandsAsync(cleanupDoneTcs, cts.Token);
                toastService.Show(t => t
                .AddText("Cleanup Scheduled")
                .AddText("Monitoring Steam. Files will be removed automatically upon exit."), "SteamCleanupTag", 5);
                var completed = await Task.WhenAny(exitTask, stopTask);

                if (completed.IsFaulted)
                {
                    var ex = completed.Exception?.GetBaseException();
                    toastService.Show(t => t.AddText("Watcher stopped unexpectedly").AddText(ex?.Message ?? "Unknown error"), "SteamCleanupTag");
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                toastService.Show(t => t.AddText("Failed to schedule cleanup").AddText(ex.Message), "SteamCleanupTag");
            }
            finally
            {
                cts.Cancel();
                staDispatcher.InvokeShutdown();
            }
        }
    }
}