using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SteamCommon
{
    public static partial class Steam
    {
        private const string defaultdir = @"C:\Program Files (x86)\Steam";

        private const string defaultexe = @"C:\Program Files (x86)\Steam\steam.exe";

        private const int KEY_NOTIFY = 0x0010;

        private const int REG_NOTIFY_CHANGE_LAST_SET = 0x4;

        private const string SteamActiveProcessKey = @"SOFTWARE\Valve\Steam\ActiveProcess";

        private static readonly IntPtr HKEY_CURRENT_USER = new(unchecked((int)0x80000001));

        public static int GetSteamActiveProcessPid()
        {
            using var key = Registry.CurrentUser.OpenSubKey(SteamActiveProcessKey);
            return key?.GetValue("pid") is int pid ? pid : 0;
        }

        /// <summary>
        ///     Get Steam directory path.
        /// </summary>
        /// <returns> Steam directory path</returns>
        public static string GetSteamDirectory()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\WOW6432Node\Valve\Steam"))
            {
                if (key?.GetValueNames().Contains("SteamPath") == true)
                {
                    return key.GetValue("SteamPath")?.ToString()?.Replace('/', '\\') ?? string.Empty;
                }
            }

            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
            {
                if (key?.GetValueNames().Contains("SteamPath") == true)
                {
                    return key.GetValue("SteamPath")?.ToString()?.Replace('/', '\\') ?? string.Empty;
                }
            }

            return defaultdir;
        }

        /// <summary>
        ///     Get Steam executable path.
        /// </summary>
        /// <returns> Steam executable path</returns>
        public static string GetSteamExecutable()
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Valve\Steam");
            if (key?.GetValueNames().Contains("SteamExe") == true)
                return key.GetValue("SteamExe")?.ToString()?.Replace('/', '\\') ?? defaultexe;

            return defaultexe;
        }

        /// <summary>
        ///     Get SteamID3 with SteamID64.
        /// </summary>
        /// <param name="userSteamID">The SteamID64. example: 76561197960287930</param>
        public static ulong GetUserSteamID3(string userSteamID)
        {
            if (ulong.TryParse(userSteamID, out var userSteamID3)) return userSteamID3 - 76561197960265728;

            return 0;
        }

        /// <summary>
        ///     Checks if Steam running.
        /// </summary>
        /// <returns>
        ///     <c>true</c> if Steam running; otherwise <c>false</c>
        /// </returns>
        public static bool IsSteamRunning()
        {
            var steamProcesses = Process.GetProcessesByName("steam");
            return steamProcesses.Length > 0;
        }

        public static bool IsSteamStartupEnabled()
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run");
            return key?.GetValue("Steam") != null;
        }

        /// <summary>
        /// Force kill Steam process.
        /// </summary>
        public static void KillSteam()
        {
            var processes = Process.GetProcessesByName("steam");

            if (processes.Length == 0) return;

            foreach (var process in processes)
            {
                try
                {
                    process.Kill(true);
                }
                catch { }
            }
        }

        /// <inheritdoc cref="KillSteam"/>
        public static async Task KillSteamAsync()
        {
            var processes = Process.GetProcessesByName("steam");

            if (processes.Length == 0) return;

            var killTasks = new List<Task>();

            foreach (var process in processes)
            {
                using (process)
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Kill(true);
                            killTasks.Add(process.WaitForExitAsync());
                        }
                    }
                    catch { }
                    {
                    }
                }
            }
            await Task.WhenAll(killTasks);
        }

        /// <summary>
        /// Shutdown Steam respectfully.
        /// </summary>
        public static void ShutdownSteam()
        {
            var processes = Process.GetProcessesByName("steam");

            if (processes.Length == 0)
            {
                return;
            }

            try
            {
                string steamPath = processes[0].MainModule?.FileName ?? throw new InvalidOperationException("Steam path not found!");

                var startInfo = new ProcessStartInfo(steamPath, "-shutdown")
                {
                    UseShellExecute = false
                };

                using var shutdownProcess = Process.Start(startInfo);

                shutdownProcess?.WaitForExit();
            }
            catch { }
        }

        /// <inheritdoc cref="ShutdownSteam"/>
        public static async Task ShutdownSteamAsync(TimeSpan? timeout = default)
        {
            var taskTimeout = timeout ?? TimeSpan.FromSeconds(5);
            var processes = Process.GetProcessesByName("steam");

            if (processes.Length == 0)
            {
                return;
            }

            try
            {
                string steamPath = processes[0].MainModule?.FileName ?? GetSteamExecutable();

                var startInfo = new ProcessStartInfo(steamPath, "-shutdown")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var shutdownTrigger = Process.Start(startInfo);
                if (shutdownTrigger != null)
                {
                    await shutdownTrigger.WaitForExitAsync();
                }

                using var cancellationTokenSource = new CancellationTokenSource(taskTimeout);
                var token = cancellationTokenSource.Token;

                while (!token.IsCancellationRequested)
                {
                    var runningSteamProcesses = Process.GetProcessesByName("steam");

                    if (runningSteamProcesses.Length == 0)
                    {
                        break;
                    }

                    foreach (var p in runningSteamProcesses)
                    {
                        p.Dispose();
                    }

                    await Task.Delay(500, token);
                }
            }
            catch { }
        }

        /// <summary>
        /// Wait for steam to start
        /// </summary>
        /// <param name="timeout"></param>
        /// <returns><![CDATA[True]]> if steam running with given timeout</returns>
        public static async Task<bool> WaitForSteamAsync(TimeSpan? timeout = null)
        {
            var waitForTimeout = timeout ?? TimeSpan.FromSeconds(5);
            var sw = Stopwatch.StartNew();

            while (sw.Elapsed < waitForTimeout)
            {
                try
                {
                    bool hasIpcEvent = EventWaitHandle.TryOpenExisting(@"Global\Valve_SteamIPC_Class", out var ipcHandle);
                    ipcHandle?.Dispose();
                    if (hasIpcEvent)
                    {
                        return true;
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    return true;
                }
                catch { }

                int pid = GetSteamActiveProcessPid();

                if (pid != 0)
                {
                    return true;
                }

                await Task.Delay(500);
            }

            return false;
        }

        /// <summary>
        ///     Blocks until the Steam active-process registry key changes, or until <paramref name="token"/> is cancelled.
        ///     Returns true if a change was observed, false if cancelled first.
        /// </summary>
        public static async Task<bool> WaitForSteamPidChangeAsync(CancellationToken token)
        {
            if (RegOpenKeyEx(HKEY_CURRENT_USER, SteamActiveProcessKey, 0, KEY_NOTIFY, out IntPtr hKey) != 0)
                return false;

            using var evt = new ManualResetEvent(false);
            try
            {
                int result = RegNotifyChangeKeyValue(
                    hKey, false, REG_NOTIFY_CHANGE_LAST_SET, evt.SafeWaitHandle.DangerousGetHandle(), true);
                if (result != 0)
                    return false;

                var tcs = new TaskCompletionSource<bool>();
                var rwh = ThreadPool.RegisterWaitForSingleObject(
                    evt, (state, timedOut) => ((TaskCompletionSource<bool>)state!).TrySetResult(!timedOut),
                    tcs, Timeout.Infinite, executeOnlyOnce: true);

                await using (token.Register(() => tcs.TrySetResult(false)))
                {
                    var changed = await tcs.Task;
                    rwh.Unregister(null);
                    return changed;
                }
            }
            finally
            {
                _ = RegCloseKey(hKey);
            }
        }

        /// <summary>
        ///     Blocks until the Steam active-process registry key changes, or until given <paramref name="timeout"/> (default 5s).
        ///     Returns true if a change was observed, false if timed out.
        /// </summary>
        public static Task<bool> WaitForSteamPidChangeAsync(TimeSpan? timeout = null)
        {
            using var token = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(5));
            return WaitForSteamPidChangeAsync(token.Token);
        }

        [LibraryImport("advapi32.dll", EntryPoint = "RegCloseKey")]
        private static partial int RegCloseKey(IntPtr hKey);

        [LibraryImport("advapi32.dll", EntryPoint = "RegNotifyChangeKeyValue", SetLastError = true)]
        private static partial int RegNotifyChangeKeyValue(IntPtr hKey, [MarshalAs(UnmanagedType.Bool)] bool watchSubtree,
            int notifyFilter, IntPtr hEvent, [MarshalAs(UnmanagedType.Bool)] bool asynchronous);

        [LibraryImport("advapi32.dll", EntryPoint = "RegOpenKeyExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        private static partial int RegOpenKeyEx(IntPtr hKey, string subKey, int options, int samDesired, out IntPtr result);
    }
}