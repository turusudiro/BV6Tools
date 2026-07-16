using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace BV6Tools.Services.Injector
{
    [Flags]
    public enum ProcessMode
    {
        [Description("GreenLuma (Stealth Mode)")] GreenLumaStealth = 1,
        [Description("GreenLuma (Normal Mode)")] GreenLumaNormal = 2,
        SteamTools = 4,
        OpenSteamTool = 8,
    }

    public static class ProcessModeExtensions
    {
        public static readonly ProcessMode HasGreenLuma =
            ProcessMode.GreenLumaStealth | ProcessMode.GreenLumaNormal;

        public static bool IsGreenLuma(this ProcessMode mode) =>
            (mode & HasGreenLuma) != 0;
    }

    public readonly record struct SteamProcessData(
        IReadOnlyCollection<uint> Appids,
        ProcessMode Mode,
        int PID,
        string Path
    );

    /// <summary>
    ///     Tracks the Steam process and notifies subscribers when it starts or stops,
    ///     so the UI can react and any pending injection cleanup can run.
    /// </summary>
    public class InjectorService : IDisposable
    {
        // Short delay used while waiting for WaitForSteamExitAsync to finish its
        // post-exit cleanup (e.g. waiting for DLLInjector to release file handles).
        private const int ExitCleanupPollIntervalMs = 500;

        // How often we poll for Steam when it's not running.
        private const int PollIntervalMs = 1000;

        private readonly Lock _lock = new();

        private CancellationTokenSource? _cts;
        private bool _isSteamRunning;
        private int _pid;
        private SteamProcessData _state;
        private Task? _steamExitTask;
        private Process? _trackedSteamProcess;

        /// <summary>
        ///     Raised once Steam (and any related injector process) has fully exited,
        ///     so injected files can be cleaned up.
        /// </summary>
        public event Func<SteamProcessData, Task>? CleanupTask;

        /// <summary>
        ///     Raised when Steam's running state changes.
        /// </summary>
        public event Action<bool>? IsSteamRunningChanged;

        /// <summary>
        ///     Raised when Steam injceted.
        /// </summary>
        public event Action<SteamProcessData>? OnInjected;

        public event Action<object, Exception>? OnInjectFailed;

        public void RaiseInjectFailed(Exception ex) => OnInjectFailed?.Invoke(ex);

        public bool IsSteamRunning
        {
            get { lock (_lock) return _isSteamRunning; }
            private set
            {
                bool changed;
                lock (_lock)
                {
                    changed = _isSteamRunning != value;
                    if (changed)
                    {
                        _isSteamRunning = value;
                    }
                }

                if (changed)
                {
                    IsSteamRunningChanged?.Invoke(value);
                }
            }
        }

        public int PID
        {
            get { lock (_lock) return _pid; }
            private set
            {
                lock (_lock)
                {
                    _pid = value;
                }
            }
        }

        public SteamProcessData State
        {
            get { lock (_lock) return _state; }
        }

        public void Dispose()
        {
            StopWatcher();
            GC.SuppressFinalize(this);
        }

        /// <summary>
        ///     Checks whether the given appids/mode match the last injected session.
        /// </summary>
        /// <returns><![CDATA[True]]> if injected</returns>
        public bool IsInjected(IReadOnlyCollection<uint> appids, ProcessMode mode)
        {
            SteamProcessData state;
            int pid;
            bool isSteamRunning = false;

            lock (_lock)
            {
                state = _state;
                pid = _pid;
                isSteamRunning = _isSteamRunning;
            }

            bool isGreenLumaRequest = mode.IsGreenLuma();
            bool isGreenLumaState = state.Mode.IsGreenLuma();
            bool injectedAsGreenLuma = isGreenLumaRequest && isGreenLumaState;

            return isSteamRunning && pid == state.PID
                && (state.Mode == mode || injectedAsGreenLuma)
                && appids.All(state.Appids.Contains);
        }

        public void LoadState(string path)
        {
            try
            {
                var json = File.ReadAllText(path);
                var result = JsonSerializer.Deserialize<SteamProcessData>(json);
                lock (_lock)
                {
                    _state = result;
                }
            }
            catch
            {
                // No saved state yet, or the file is corrupt/missing - ignore.
            }
        }

        public void RaiseInjected(SteamProcessData state) => OnInjected?.Invoke(state);

        public void RaiseInjectFailed(object sender, Exception ex) => OnInjectFailed?.Invoke(sender, ex);

        /// <summary>
        ///     Updates the internal injection state and grabs a handle to the Steam process.
        ///     <br/>
        ///     Pass <paramref name="path"/> to also save the state as a JSON file.
        /// </summary>
        public void SaveState(SteamProcessData state, string? path = null)
        {
            if (path != null)
            {
                var json = JsonSerializer.Serialize(state);
                File.WriteAllText(path, json);
            }

            lock (_lock)
            {
                _state = state;
                PID = state.PID;

                try
                {
                    _trackedSteamProcess = Process.GetProcessById(state.PID);
                }
                catch { }
            }
            IsSteamRunning = true;
        }

        /// <summary>
        ///     Starts polling for the Steam process. Safe to call multiple times.
        /// </summary>
        public void StartWatcher()
        {
            lock (_lock)
            {
                if (_cts != null && !_cts.IsCancellationRequested) return;

                _cts = new CancellationTokenSource();
                Task.Run(() => RunWatcherAsync(_cts.Token));
            }
        }

        /// <summary>
        ///     Stops the polling loop. Any in-progress exit/cleanup task keeps running
        ///     so that cleanup still happens even while the watcher is stopped.
        /// </summary>
        public void StopWatcher()
        {
            lock (_lock)
            {
                if (_cts == null) return;

                _cts.Cancel();
                _cts.Dispose();
                _cts = null;
            }
        }

        /// <summary>
        ///     Waits for the tracked Steam process to exit and performs cleanup.
        /// </summary>
        public async Task WaitForSteamExitAsync()
        {
            Process? steam;
            SteamProcessData state;

            lock (_lock)
            {
                steam = _trackedSteamProcess;
                state = _state;
                if (steam == null) return;

                if (_steamExitTask != null && !_steamExitTask.IsCompleted) return;
                _steamExitTask = Task.CompletedTask;
            }

            using (steam)
            {
                var exitTask = steam.WaitForExitAsync();
                lock (_lock) _steamExitTask = exitTask;
                await exitTask;
            }

            // In GreenLuma Normal mode, DLLInjector outlives Steam (it's the one that
            // issues the -shutdown). Wait for it to release file handles before
            // signalling that cleanup can run.
            if (state.Mode == ProcessMode.GreenLumaNormal)
            {
                using var injector = Process.GetProcessesByName("DLLInjector").FirstOrDefault();
                if (injector != null)
                {
                    await injector.WaitForExitAsync();
                }
            }

            IsSteamRunning = false;
            lock (_lock)
            {
                _steamExitTask = null;
                _trackedSteamProcess = null;
            }

            // Only run cleanup if this exit corresponds to a session we injected.
            if (state.PID != PID) return;

            try
            {
                CleanupTask?.Invoke(state);
            }
            catch { }
        }

        private async Task RunWatcherAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    Task? exitTask;
                    lock (_lock) exitTask = _steamExitTask;

                    if (exitTask != null)
                    {
                        if (exitTask.IsCompleted)
                        {
                            // Steam itself has exited, but WaitForSteamExitAsync is still
                            // finishing up (post-exit cleanup) - check back shortly.
                            await Task.Delay(ExitCleanupPollIntervalMs, token);
                        }
                        else
                        {
                            // Steam is running - wait for it to exit or for the watcher to stop.
                            await Task.WhenAny(exitTask, Task.Delay(Timeout.Infinite, token));
                        }

                        continue;
                    }

                    var steam = Process.GetProcessesByName("steam").FirstOrDefault();

                    if (steam == null)
                    {
                        IsSteamRunning = false;
                        await Task.Delay(PollIntervalMs, token);
                        continue;
                    }

                    IsSteamRunning = true;
                    PID = steam.Id;

                    lock (_lock)
                    {
                        _trackedSteamProcess = steam;
                        _steamExitTask ??= WaitForSteamExitAsync();
                    }
                }
            }
            catch
            {
                // Watcher was cancelled - nothing to do.
            }
        }
    }
}