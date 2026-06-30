using Microsoft.Win32;
using System.Diagnostics;

namespace SteamCommon
{

    public static class Steam
    {
        private const string defaultdir = @"C:\Program Files (x86)\Steam";

        private const string defaultexe = @"C:\Program Files (x86)\Steam\steam.exe";

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
            using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Valve\Steam"))
            {
                if (key?.GetValueNames().Contains("SteamExe") == true)
                    return key.GetValue("SteamExe")?.ToString()?.Replace('/', '\\') ?? defaultexe;

                return defaultexe;
            }
        }

        /// <summary>
        /// Get Steam PID.
        /// </summary>
        public static int? GetSteamProcessId()
        {
            var processes = Process.GetProcessesByName("steam");

            foreach (var process in processes)
            {
                try
                {
                    return process.Id;
                }
                catch
                {
                    return null;
                }
            }

            return null;
        }
        /// <summary>
        /// Get Steam process.
        /// </summary>
        /// <returns>Steam main process.</returns>
        public static Process? GetSteamProcess()
        {
            var processes = Process.GetProcessesByName("steam");
            Process? result = null;

            foreach (var p in processes)
            {
                if (result == null)
                {
                    try
                    {
                        if (p.MainModule?.FileName?.EndsWith("steam.exe", StringComparison.OrdinalIgnoreCase) == true)
                        {
                            result = p;
                            continue;
                        }
                    }
                    catch { }
                }
                p.Dispose();
            }

            return result;
        }
        /// <summary>
        /// Get Steam PID.
        /// </summary>
        public static bool TryGetSteamProcessId(out int pid)
        {
            var processes = Process.GetProcessesByName("steam");
            pid = 0;

            foreach (var process in processes)
            {
                try
                {
                    pid = process.Id;
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            return false;
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
                string steamPath = processes[0].MainModule?.FileName ?? throw new InvalidOperationException("Steam path not found!");

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
    }
}