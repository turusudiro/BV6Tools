using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace STCommon
{
    public readonly record struct SetTicket(uint AppId, TicketType TicketType, byte[] Bytes);

    public enum TicketType
    {
        AppOwnership,
        Encrypted
    }

    public readonly record struct ManifestData(string? ManifestID, string? Size);
    public readonly record struct LuaAppIdWithKey(string Flag, string DecryptionKey);
    public readonly record struct LuaData(IReadOnlyDictionary<string, LuaAppIdWithKey?> Appids, IReadOnlyDictionary<string, ManifestData> Manifest, IReadOnlyDictionary<string, string> TokenData);

    public static partial class ST
    {
        public static readonly Regex[] SteamToolsFiles = [DwmApiDLLRegex, SteamToolsCoreDLLRegex];
        private const string CoreDLLUrl = "http://update.tnkjmec.com/update";
        private const string CoreDLLUrlFallback = "http://update.steamcdn.com/update";

        private const string DwmapiUrl = "http://update.tnkjmec.com/dwmapi";
        private const string DwmapiUrlFallback = "http://update.steamcdn.com/dwmapi";

        private const string OpenSteamToolApiUrl = "https://api.github.com/repos/OpenSteam001/OpenSteamTool/releases/latest";

        private static readonly HttpClient httpClient = new();

        [GeneratedRegex(@"appid\((?<appid>\d+)\)", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
        public static partial Regex AppIdLuaRegex { get; }

        [GeneratedRegex(@"appid\((?<appid>\d+)(\D+)(?<flag>\d)(\W+)(?<key>\w+)", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
        public static partial Regex AppIdLuaWithKeyRegex { get; }

        [GeneratedRegex(@"token\D+(?<appid>\d+)\D+(?<token>\d+)", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
        public static partial Regex AppIdTokenLuaRegex { get; }

        [GeneratedRegex(@"\\dwmapi\.dll$", RegexOptions.IgnoreCase)]
        public static partial Regex DwmApiDLLRegex { get; }

        [GeneratedRegex(@"manifestid\((?<depotid>\d+)\W+(?<gid>\d+)(?:\W+\s*(?<size>\d+))?\)", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
        public static partial Regex ManifestIDLuaRegex { get; }

        [GeneratedRegex(@"(\\|^)(opensteamtool|xinput1_4|dwmapi)\.dll$", RegexOptions.IgnoreCase)]
        public static partial Regex OpenSteamToolDLLRegex { get; }

        [GeneratedRegex(@"\\(core|xinput1_4)\.dll$", RegexOptions.IgnoreCase)]
        public static partial Regex SteamToolsCoreDLLRegex { get; }

        public static void DeleteOpenSteamToolFiles(string steamPath)
        {
            string luaPath = Path.Combine(steamPath, "config", "lua");
            if (Directory.Exists(luaPath))
            {
                Directory.Delete(luaPath, true);
            }

            string openSteamToolPath = Path.Combine(steamPath, "opensteamtool");
            if (Directory.Exists(openSteamToolPath))
            {
                Directory.Delete(openSteamToolPath, true);
            }

            File.Delete(Path.Combine(steamPath, "xinput1_4.dll"));
            File.Delete(Path.Combine(steamPath, "OpenSteamTool.dll"));
            File.Delete(Path.Combine(steamPath, "opensteamtool.toml"));
            File.Delete(Path.Combine(steamPath, "dwmapi.dll"));
        }

        public static void DeleteSteamToolsFiles(string steamPath)
        {
            string stpluginPath = Path.Combine(steamPath, "config", "stplug-in");
            if (Directory.Exists(stpluginPath))
            {
                Directory.Delete(stpluginPath, true);
            }
            File.Delete(Path.Combine(steamPath, "xinput1_4.dll"));
            File.Delete(Path.Combine(steamPath, "dwmapi.dll"));
        }

        public static async Task DownloadOpenSteamToolAsync(
                            string outputPath,
            IProgress<(string Text, double ProgressValue, bool IsIndeterminate)>? progress = null,
            CancellationToken token = default)
        {
            progress?.Report(("Fetching latest release info...", default, true));
            using var request = new HttpRequestMessage(HttpMethod.Get, OpenSteamToolApiUrl);
            request.Headers.Add("User-Agent", "BV6Tools");
            using var apiResponse = await httpClient.SendAsync(request, token);
            apiResponse.EnsureSuccessStatusCode();

            using var doc = System.Text.Json.JsonDocument.Parse(
                await apiResponse.Content.ReadAsStringAsync(token));

            progress?.Report(("Resolving download URL...", default, true));

            string zipUrl = doc.RootElement
                .GetProperty("assets")
                .EnumerateArray()
                .First(a =>
                    a.GetProperty("name").GetString()?.Contains("Release", StringComparison.OrdinalIgnoreCase) == true
                    && a.GetProperty("name").GetString()?.EndsWith(".zip") == true)
                .GetProperty("browser_download_url").GetString()
                    ?? throw new Exception("Could not find zip asset in latest release.");

            using var response = await httpClient.GetAsync(
                zipUrl, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();

            long? totalBytes = response.Content.Headers.ContentLength;

            progress?.Report(("Downloading...", default, false));

            await using var stream = await response.Content.ReadAsStreamAsync(token);
            await using var fileStream = File.Create(outputPath);
            if (totalBytes is null or <= 0)
                throw new InvalidOperationException("Server did not return Content-Length; accurate progress tracking is unavailable.");

            const int bufferSize = 81920;
            var buffer = new byte[bufferSize];
            long totalRead = 0;
            double lastReported = -1;
            int bytesRead;

            while ((bytesRead = await stream.ReadAsync(buffer, token)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), token);
                totalRead += bytesRead;

                double percent = Math.Round((double)totalRead / totalBytes.Value * 100.0, 2);

                if (percent != lastReported)
                {
                    progress?.Report(("Downloading...", percent, false));
                    lastReported = percent;
                }
            }

            progress?.Report(("Downloading...", 100.0, false));
        }

        public static async Task DownloadSteamToolsAsync(string outputDir, CancellationToken token = default)
        {
            // Define your URL lists
            var coreUrls = new[] { CoreDLLUrl, CoreDLLUrlFallback };
            var dwmUrls = new[] { DwmapiUrl, DwmapiUrlFallback };

            Directory.CreateDirectory(outputDir);

            // Execute downloads in parallel with fallback logic
            await Task.WhenAll(
                DownloadWithFallbackAsync(coreUrls, Path.Combine(outputDir, "xinput1_4.dll"), token),
                DownloadWithFallbackAsync(dwmUrls, Path.Combine(outputDir, "dwmapi.dll"), token)
            );
        }

        public static bool IsSharedDepot(string depotId) => uint.TryParse(depotId, out var id) && id >= 228981 && id <= 229033;

        public static bool IsSharedDepot(uint depotId) => depotId >= 228981 && depotId <= 229033;

        public static LuaData ParseFromLua(string luaContent)
        {
            Dictionary<string, LuaAppIdWithKey?> appids = [];
            Dictionary<string, ManifestData> manifestData = [];
            Dictionary<string, string> tokenData = [];
            foreach (Match match in AppIdLuaRegex.Matches(luaContent))
            {
                if (!match.Success) continue;
                string appid = match.Groups["appid"].Value;
                if (IsSharedDepot(appid)) continue;
                appids.TryAdd(appid, default);
            }
            foreach (Match match in AppIdLuaWithKeyRegex.Matches(luaContent))
            {
                if (!match.Success) continue;
                string appid = match.Groups["appid"].Value;
                if (IsSharedDepot(appid)) continue;
                string flag = match.Groups["flag"].Value;
                string key = match.Groups["key"].Value;
                appids[appid] = new()
                {
                    DecryptionKey = key,
                    Flag = flag
                };
            }
            foreach (Match match in AppIdTokenLuaRegex.Matches(luaContent))
            {
                if (!match.Success) continue;
                string appid = match.Groups["appid"].Value;
                if (IsSharedDepot(appid)) continue;
                string token = match.Groups["token"].Value;
                tokenData[appid] = token;
            }

            foreach (Match match in ManifestIDLuaRegex.Matches(luaContent))
            {
                if (!match.Success) continue;
                string depotid = match.Groups["depotid"].Value;
                if (IsSharedDepot(depotid)) continue;
                string gid = match.Groups["gid"].Value;
                string? size = null;
                if (match.Groups["size"].Success)
                {
                    size = match.Groups["size"].Value;
                }
                manifestData[depotid] = manifestData.GetValueOrDefault(depotid) with { ManifestID = gid, Size = size };
            }
            return new(appids, manifestData, tokenData);
        }

        public static void SaveAppId(IEnumerable<uint> appids, string destinationPath, bool append = false)
        {
            string? destinationDir = Path.GetDirectoryName(destinationPath) ??
                throw new ArgumentException("Path must include a directory.", nameof(destinationPath));
            Directory.CreateDirectory(destinationDir);

            using var stream = new FileStream(destinationPath,
                append ? FileMode.Append : FileMode.Create,
                FileAccess.Write,
                FileShare.None);
            using var writer = new StreamWriter(stream);

            foreach (var appid in appids)
            {
                writer.WriteLine($"addappid({appid})");
            }
        }

        public static void SaveLua(LuaData luaData, string destinationPath)
        {
            string? destinationDir = Path.GetDirectoryName(destinationPath) ??
                throw new ArgumentException("Path must include a directory.", nameof(destinationPath));
            Directory.CreateDirectory(destinationDir);

            using var stream = new FileStream(destinationPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None);
            using var writer = new StreamWriter(stream);

            foreach (var appid in luaData.Appids)
            {
                if (appid.Value is LuaAppIdWithKey(var flag, var key))
                {
                    writer.WriteLine($"addappid({appid.Key},{flag},\"{key}\")");
                }
            }

            foreach (var token in luaData.TokenData)
            {
                writer.WriteLine($"addtoken({token.Key},\"{token.Value}\")");
            }

            foreach (var (appid, manifest) in luaData.Manifest)
            {
                if (manifest.Size != null)
                {
                    writer.WriteLine($"setManifestid({appid},\"{manifest.ManifestID}\",{manifest.Size})");
                }
                else
                {
                    writer.WriteLine($"setManifestid({appid},\"{manifest.ManifestID}\")");
                }
            }
        }

        public static void SaveTicket(IReadOnlyCollection<SetTicket> tickets, string destinationPath)
        {
            string? destinationDir = Path.GetDirectoryName(destinationPath) ??
                throw new ArgumentException("Path must include a directory.", nameof(destinationPath));
            Directory.CreateDirectory(destinationDir);

            using var stream = new FileStream(destinationPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None);
            using var writer = new StreamWriter(stream);

            foreach (var ticket in tickets)
            {
                string setTicket = "setAppticket";
                if (ticket.TicketType == TicketType.Encrypted)
                {
                    setTicket = "setETicket";
                }
                writer.WriteLine($"{setTicket}({ticket.AppId}, \"{Convert.ToHexString(ticket.Bytes)}\")");
            }
        }

        /// <summary>
        /// Inject steam with opensteamtool in <paramref name="dllDirectoryPath"/>
        /// </summary>
        /// <returns>Steam PID from <paramref name="steamPath"/></returns>
        /// <exception cref="InvalidOperationException"></exception>
        public static async Task<int> StartInjectOpenSteamTool(string dllDirectoryPath, string steamPath, string? args = null)
        {
            var files = Directory.EnumerateFiles(dllDirectoryPath, "*", SearchOption.TopDirectoryOnly);
            var dllPath = files.FirstOrDefault(OpenSteamToolDLLRegex.IsMatch) ?? throw new InvalidOperationException("OpenSteamTool DLL not found!");

            var steamFiles = Directory.EnumerateFiles(steamPath, "steam.exe", SearchOption.TopDirectoryOnly);
            var steamExePath = steamFiles.FirstOrDefault() ?? throw new InvalidOperationException("Steam exe not found!");

            return await LaunchAndInjectAsync(steamExePath, dllPath, args);
        }

        public static int StartOpenSteamTool(string openSteamToolPath, string steamPath, string? luaPath = default,
            string? args = default, IEnumerable<uint>? appids = default)
        {
            var files = Directory.EnumerateFiles(openSteamToolPath, "*", SearchOption.TopDirectoryOnly);
            var xinputDll = files.FirstOrDefault(x => x.EndsWith("xinput1_4.dll", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("xinput DLL not found!");
            var dwmapiDll = files.FirstOrDefault(DwmApiDLLRegex.IsMatch) ?? throw new InvalidOperationException("dwmapi DLL not found!");
            var openSteamToolDll = files.FirstOrDefault(x => x.EndsWith("OpenSteamTool.dll", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("dwmapi DLL not found!");

            var steamExePath = Path.Combine(steamPath, "steam.exe");

            if (!File.Exists(steamExePath))
            {
                throw new InvalidOperationException("Steam exe not found!");
            }

            string openSteamToolDir = Path.Combine(openSteamToolPath, "opensteamtool");
            Directory.CreateDirectory(openSteamToolDir);

            string destinationOpenSteamToolDir = Path.Combine(steamPath, "opensteamtool");
            if (Directory.Exists(destinationOpenSteamToolDir))
            {
                Directory.Delete(destinationOpenSteamToolDir);
            }
            Directory.CreateSymbolicLink(destinationOpenSteamToolDir, openSteamToolDir);

            var openSteamToolToml = Path.Combine(openSteamToolPath, "opensteamtool.toml");
            if (File.Exists(openSteamToolToml))
            {
                var destinationToml = Path.Combine(steamPath, "opensteamtool.toml");
                File.Delete(destinationToml);
                File.CreateSymbolicLink(destinationToml, openSteamToolToml);
            }

            string destinationDll = Path.Combine(steamPath, Path.GetFileName(xinputDll));
            File.Delete(destinationDll);
            File.CreateSymbolicLink(destinationDll, xinputDll);

            string destinationDwmDll = Path.Combine(steamPath, Path.GetFileName(dwmapiDll));
            File.Delete(destinationDwmDll);
            File.CreateSymbolicLink(destinationDwmDll, dwmapiDll);

            string destinationOpenSteamToolDll = Path.Combine(steamPath, Path.GetFileName(openSteamToolDll));
            File.Delete(destinationOpenSteamToolDll);
            File.CreateSymbolicLink(destinationOpenSteamToolDll, openSteamToolDll);

            var destinationLuaPath = Path.Combine(steamPath, "config", "lua");

            if (luaPath != null)
            {
            if (Directory.Exists(destinationLuaPath))
            {
                Directory.Delete(destinationLuaPath, true);
            }
                Directory.CreateSymbolicLink(destinationLuaPath, luaPath);
            }
            else if (appids != null)
            {
                Directory.CreateDirectory(destinationLuaPath);
                SaveAppId(appids, Path.Combine(destinationLuaPath, "appids.lua"));
            }

            var processInfo = new ProcessStartInfo { FileName = steamExePath, WorkingDirectory = steamPath, Arguments = args };
            return Process.Start(processInfo)?.Id ?? throw new InvalidOperationException("Failed to start Steam.");
        }

        public static int StartSteamTools(string dllPath, string steamPath, string? luaPath = default, string? args = default, IEnumerable<uint>? appids = default)
        {
            var files = Directory.EnumerateFiles(dllPath, "*", SearchOption.TopDirectoryOnly);
            var coreDll = files.FirstOrDefault(SteamToolsCoreDLLRegex.IsMatch) ?? throw new InvalidOperationException("SteamTools DLL not found!");
            var dwmapiDll = files.FirstOrDefault(DwmApiDLLRegex.IsMatch) ?? throw new InvalidOperationException("dwmapi DLL not found!");

            var steamExePath = Path.Combine(steamPath, "steam.exe");

            if (!File.Exists(steamExePath))
            {
                throw new InvalidOperationException("Steam exe not found!");
            }

            string destinationDll = Path.Combine(steamPath, "xinput1_4.dll");
            File.Delete(destinationDll);
            File.CreateSymbolicLink(destinationDll, coreDll);

            string destinationDwmDll = Path.Combine(steamPath, Path.GetFileName(dwmapiDll));
            File.Delete(destinationDwmDll);
            File.CreateSymbolicLink(destinationDwmDll, dwmapiDll);

            var destinationLuaPath = Path.Combine(steamPath, "config", "stplug-in");

            if (luaPath != null)
            {
            if (Directory.Exists(destinationLuaPath))
            {
                Directory.Delete(destinationLuaPath, true);
            }
                Directory.CreateSymbolicLink(destinationLuaPath, luaPath);
            }
            else if (appids != null)
            {
                Directory.CreateDirectory(destinationLuaPath);
                SaveAppId(appids, Path.Combine(destinationLuaPath, "appids.lua"));
            }

            var processInfo = new ProcessStartInfo { FileName = steamExePath, WorkingDirectory = steamPath, Arguments = args };
            return Process.Start(processInfo)?.Id ?? throw new InvalidOperationException("Failed to start Steam.");
        }

        private static async Task DownloadWithFallbackAsync(string[] urls, string outputPath, CancellationToken token)
        {
            foreach (var url in urls)
            {
                try
                {
                    using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
                    response.EnsureSuccessStatusCode();

                    await using var stream = await response.Content.ReadAsStreamAsync(token);
                    await using var fileStream = File.Create(outputPath);
                    await stream.CopyToAsync(fileStream, token);

                    return; // Success, exit the method
                }
                catch { }
            }
            throw new Exception($"All download attempts failed for {outputPath}");
        }

        #region OpenSteamTool Injector

        // Constant for waiting infinitely until a thread completes
        private const uint INFINITE = 0xFFFFFFFF;

        // Used for memory allocation
        private const uint MEM_COMMIT = 0x00001000;

        private const uint MEM_RESERVE = 0x00002000;

        private const uint PAGE_READWRITE = 4;

        // Privileges
        private const int PROCESS_CREATE_THREAD = 0x0002;

        private const int PROCESS_QUERY_INFORMATION = 0x0400;

        private const int PROCESS_VM_OPERATION = 0x0008;

        private const int PROCESS_VM_READ = 0x0010;

        private const int PROCESS_VM_WRITE = 0x0020;

        [LibraryImport("kernel32.dll", EntryPoint = "CloseHandle", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool CloseHandle(IntPtr hObject);

        [LibraryImport("kernel32.dll", EntryPoint = "FreeLibrary", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool FreeLibrary(IntPtr hLibModule);

        [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
        public static partial IntPtr GetModuleHandle(string lpModuleName);

        [LibraryImport("kernel32.dll", EntryPoint = "GetProcAddress", SetLastError = true)]
        public static partial IntPtr GetProcAddress(IntPtr hModule, [MarshalAs(UnmanagedType.LPStr)] string procName);

        public static void InjectProcess(Process targetProcess, string dll)
        {
            IntPtr procHandle = OpenProcess(PROCESS_CREATE_THREAD | PROCESS_QUERY_INFORMATION | PROCESS_VM_OPERATION | PROCESS_VM_WRITE | PROCESS_VM_READ, false, targetProcess.Id);
            if (procHandle == IntPtr.Zero)
                throw new Exception($"Failed to open process handle for {targetProcess.ProcessName} (Error: {Marshal.GetLastWin32Error()}).");

            IntPtr loadLibraryAddr = GetProcAddress(GetModuleHandle("kernel32.dll"), "LoadLibraryA");
            if (loadLibraryAddr == IntPtr.Zero)
                throw new Exception("Failed to locate LoadLibraryA in kernel32.dll.");

            byte[] dllBytes = Encoding.ASCII.GetBytes(dll + "\0");

            IntPtr allocMemAddress = VirtualAllocEx(procHandle, IntPtr.Zero, (uint)dllBytes.Length, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
            if (allocMemAddress == IntPtr.Zero)
                throw new Exception($"VirtualAllocEx failed. Windows Error: {Marshal.GetLastWin32Error()}");

            if (!WriteProcessMemory(procHandle, allocMemAddress, dllBytes, (uint)dllBytes.Length, out _))
                throw new Exception($"WriteProcessMemory failed. Windows Error: {Marshal.GetLastWin32Error()}");

            IntPtr hThread = CreateRemoteThread(procHandle, IntPtr.Zero, 0, loadLibraryAddr, allocMemAddress, 0, IntPtr.Zero);
            if (hThread == IntPtr.Zero)
                throw new Exception($"CreateRemoteThread failed. Windows Error: {Marshal.GetLastWin32Error()}");
        }

        public static async Task<int> LaunchAndInjectAsync(string exePath, string dllPath, string? args = null)
        {
            string fullDllPath = Path.GetFullPath(dllPath);
            if (!File.Exists(fullDllPath))
                throw new FileNotFoundException($"The DLL could not be found at: {fullDllPath}");

            ProcessStartInfo startInfo = new(exePath)
            {
                WorkingDirectory = Path.GetDirectoryName(exePath),
                Arguments = args
            };

            using var process = Process.Start(startInfo) ?? throw new Exception($"Failed to start process at {exePath}");

            bool modulesLoaded = false;
            Stopwatch timeoutWatch = Stopwatch.StartNew();

            // 10 second safety limit max
            while (timeoutWatch.ElapsedMilliseconds < 10000)
            {
                process.Refresh();
                try
                {
                    foreach (ProcessModule module in process.Modules)
                    {
                        if (module.ModuleName != null && module.ModuleName.Equals("steamui.dll", StringComparison.OrdinalIgnoreCase))
                        {
                            modulesLoaded = true;
                            break;
                        }
                    }
                }
                catch { }
                if (modulesLoaded) break;

                await Task.Delay(50);
            }

            if (!modulesLoaded)
            {
                throw new TimeoutException("Timed out waiting for steamui.dll to load.");
            }

            InjectProcess(process, fullDllPath);

            return process.Id;
        }

        [LibraryImport("kernel32.dll", EntryPoint = "LoadLibraryW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        public static partial IntPtr LoadLibrary(string lpLibFileName);

        [LibraryImport("kernel32.dll", EntryPoint = "OpenProcess")]
        public static partial IntPtr OpenProcess(
                int dwDesiredAccess,
                [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle,
                int dwProcessId);

        [LibraryImport("kernel32.dll", EntryPoint = "VirtualAllocEx", SetLastError = true)]
        public static partial IntPtr VirtualAllocEx(
            IntPtr hProcess,
            IntPtr lpAddress,
            uint dwSize,
            uint flAllocationType,
            uint flProtect);

        [LibraryImport("kernel32.dll", EntryPoint = "WaitForSingleObject", SetLastError = true)]
        public static partial uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        [LibraryImport("kernel32.dll", EntryPoint = "WriteProcessMemory", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool WriteProcessMemory(
            IntPtr hProcess,
            IntPtr lpBaseAddress,
            byte[] lpBuffer,
            uint nSize,
            out UIntPtr lpNumberOfBytesWritten);

        [LibraryImport("kernel32.dll", EntryPoint = "CreateRemoteThread")]
        private static partial IntPtr CreateRemoteThread(
            IntPtr hProcess,
            IntPtr lpThreadAttributes,
            uint dwStackSize,
            IntPtr lpStartAddress,
            IntPtr lpParameter,
            uint dwCreationFlags,
            IntPtr lpThreadId);

        #endregion OpenSteamTool Injector
    }
}