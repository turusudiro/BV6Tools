// SteamTicketExtractor.cs
// Direct C# translation of:
// https://github.com/OpenSteam001/OpenSteamTool/tree/main/tools/extract_tickets
// Uses self-process spawning to avoid Steam "playing" status in host process.

using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace SteamCommon
{
    public struct TicketBundle
    {
        public ulong SteamID;
        public byte[]? AppTicket;  // appticket.bin
        public byte[]? ETicket;    // eticket.bin
    }

    public static partial class SteamTicketExtractor
    {
        // ── Host side: spawns self as worker, communicates via named pipe ─
        public static async Task<TicketBundle> ExtractTicketsAsync(
            uint appId,
            IProgress<string>? progress = null,
            CancellationToken token = default)
        {
            string pipeName = $"SteamExtract_{Guid.NewGuid():N}";

            using var pipeServer = new NamedPipeServerStream(
                pipeName, PipeDirection.In,
                maxNumberOfServerInstances: 1,
                transmissionMode: PipeTransmissionMode.Byte,
                options: PipeOptions.Asynchronous);

            // Spawn ourselves as worker process
            var psi = new ProcessStartInfo
            {
                FileName = Environment.ProcessPath!,
                Arguments = $"--extract-ticket {pipeName} {appId}",
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var proc = Process.Start(psi)
                ?? throw new Exception("Failed to start worker process.");

            // If child crashes before connecting, unblock WaitForConnectionAsync
            proc.EnableRaisingEvents = true;
            proc.Exited += (_, _) => { try { pipeServer.Dispose(); } catch { } };

            await pipeServer.WaitForConnectionAsync(token);

            using var reader = new StreamReader(pipeServer);
            TicketBundleDto? result = null;
            string? error = null;

            while (true)
            {
                token.ThrowIfCancellationRequested();
                string? line = await reader.ReadLineAsync(token);
                if (line == null) break;

                var msg = JsonSerializer.Deserialize<PipeMessage>(line);
                if (msg == null) continue;

                switch (msg.Type)
                {
                    case "progress": progress?.Report(msg.Data ?? ""); break;
                    case "result": result = JsonSerializer.Deserialize<TicketBundleDto>(msg.Data!); break;
                    case "error": error = msg.Data; break;
                }
            }

            await proc.WaitForExitAsync(token);

            if (error != null) throw new Exception(error);
            if (result == null) throw new Exception("Worker exited without a result.");

            return new TicketBundle
            {
                SteamID = result.SteamID,
                AppTicket = result.AppTicket != null ? Convert.FromBase64String(result.AppTicket) : null,
                ETicket = result.ETicket != null ? Convert.FromBase64String(result.ETicket) : null,
            };
        }

        // ── Worker side: called from App.xaml.cs OnStartup ───────────────
        // Usage in App.xaml.cs OnStartup (before mutex check):
        //   if (e.Args.Length > 0 && e.Args[0] == "--extract-ticket")
        //   {
        //       var thread = new Thread(() => SteamTicketExtractor.RunWorker(e.Args));
        //       thread.SetApartmentState(ApartmentState.STA);
        //       thread.Start();
        //       thread.Join();
        //       Current.Shutdown();
        //       return;
        //   }
        public static void RunWorker(string[] args)
        {
            // args: --extract-ticket <pipeName> <appId>
            string pipeName = args[1];
            uint appId = uint.Parse(args[2]);

            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
            pipe.Connect(5000);
            using var writer = new StreamWriter(pipe) { AutoFlush = true };

            void Send(string type, string? data = null) =>
                writer.WriteLine(JsonSerializer.Serialize(new PipeMessage(type, data)));

            try
            {
                var bundle = NativeSteam.ExtractTickets(appId, new Progress<string>(msg => Send("progress", msg)));

                Send("result", JsonSerializer.Serialize(new TicketBundleDto
                {
                    SteamID = bundle.SteamID,
                    AppTicket = bundle.AppTicket != null ? Convert.ToBase64String(bundle.AppTicket) : null,
                    ETicket = bundle.ETicket != null ? Convert.ToBase64String(bundle.ETicket) : null,
                }));
            }
            catch (Exception ex)
            {
                Send("error", ex.Message);
            }
        }

        // ── Internal pipe message ─────────────────────────────────────────
        private record PipeMessage(string Type, string? Data = null);

        // ── Internal DTO for pipe serialization ───────────────────────────
        private class TicketBundleDto
        {
            public ulong SteamID { get; set; }
            public string? AppTicket { get; set; }
            public string? ETicket { get; set; }
        }
    }

    // ── Native Steam extraction (direct steamclient64.dll interop) ────────
    // Based on https://github.com/OpenSteam001/OpenSteamTool/tree/main/tools/extract_tickets
    internal static unsafe partial class NativeSteam
    {
        // ── steam.h constants ─────────────────────────────────────────────
        private const string kSteamClientInterfaceVersion = "SteamClient023";
        private const string kSteamUserInterfaceVersion = "SteamUser023";
        private const string kSteamUtilsInterfaceVersion = "SteamUtils010";
        private const string kSteamAppTicketInterfaceVersion = "STEAMAPPTICKET_INTERFACE_VERSION001";
        private const int k_EResultOK = 1;
        private const int kEncryptedAppTicketCallback = 100 + 54;

        // ── Kernel32 ──────────────────────────────────────────────────────
        [LibraryImport("kernel32.dll", EntryPoint = "LoadLibraryExA", StringMarshalling = StringMarshalling.Utf8)]
        private static partial nint LoadLibraryExA(string lpFileName, nint hFile, uint dwFlags);

        [LibraryImport("kernel32.dll", EntryPoint = "GetProcAddress", StringMarshalling = StringMarshalling.Utf8)]
        private static partial nint GetProcAddress(nint hModule, string lpProcName);

        [LibraryImport("kernel32.dll", EntryPoint = "FreeLibrary")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool FreeLibrary(nint hModule);

        [LibraryImport("kernel32.dll", EntryPoint = "SetDllDirectoryA", StringMarshalling = StringMarshalling.Utf8)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool SetDllDirectoryA(string lpPathName);

        [LibraryImport("kernel32.dll", EntryPoint = "SetEnvironmentVariableA", StringMarshalling = StringMarshalling.Utf8)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool SetEnvironmentVariableA(string lpName, string lpValue);

        [LibraryImport("kernel32.dll", EntryPoint = "GetLastError")]
        private static partial uint GetLastError();

        [LibraryImport("kernel32.dll", EntryPoint = "Sleep")]
        private static partial void Sleep(uint dwMilliseconds);

        private const uint LOAD_WITH_ALTERED_SEARCH_PATH = 0x00000008;

        // ── EncryptedAppTicketResponse_t ──────────────────────────────────
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct EncryptedAppTicketResponse_t { public int m_eResult; }

        // ── vtable delegates (Cdecl = x64 calling convention) ────────────
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CreateSteamPipeFn(nint self);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate bool BReleaseSteamPipeFn(nint self, int hSteamPipe);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ConnectToGlobalUserFn(nint self, int hSteamPipe);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint GetISteamUserFn(nint self, int hSteamUser, int hSteamPipe, [MarshalAs(UnmanagedType.LPStr)] string pchVersion);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint GetISteamUtilsFn(nint self, int hSteamPipe, [MarshalAs(UnmanagedType.LPStr)] string pchVersion);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint GetISteamGenericInterfaceFn(nint self, int hSteamUser, int hSteamPipe, [MarshalAs(UnmanagedType.LPStr)] string pchVersion);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ReleaseUserFn(nint self, int hSteamPipe, int hSteamUser);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate ulong RequestEncryptedAppTicketFn(nint self, void* pDataToInclude, int cbDataToInclude);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate bool GetEncryptedAppTicketFn(nint self, void* pTicket, int cbMaxTicket, uint* pcbTicket);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate bool IsAPICallCompletedFn(nint self, ulong hSteamAPICall, bool* pbFailed);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate bool GetAPICallResultFn(nint self, ulong hSteamAPICall, void* pCallback, int cubCallback, int iCallbackExpected, bool* pbFailed);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint GetAppOwnershipTicketDataFn(nint self, uint nAppID, void* pvBuffer, uint cbBufferLength, uint* piAppId, uint* piSteamId, uint* piSignature, uint* pcbSignature);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint CreateInterfaceFn([MarshalAs(UnmanagedType.LPStr)] string pName, int* pReturnCode);

        // ── ISteamClient vtable indices ───────────────────────────────────
        //  0: CreateSteamPipe        5: GetISteamUser       9: GetISteamUtils
        //  1: BReleaseSteamPipe      6: GetISteamGameServer 10: GetISteamMatchmaking
        //  2: ConnectToGlobalUser    7: SetLocalIPBinding   11: GetISteamMatchmakingServers
        //  3: CreateLocalUser        8: GetISteamFriends    12: GetISteamGenericInterface
        //  4: ReleaseUser

        // ── ISteamUser vtable indices ─────────────────────────────────────
        //  0: GetHSteamUser          8: StopVoiceRecording  16: EndAuthSession
        //  1: BLoggedOn              9: GetAvailableVoice   17: CancelAuthTicket
        //  2: GetSteamID*           10: GetVoice            18: UserHasLicenseForApp
        //  3: InitiateGameConn_DEP  11: DecompressVoice     19: BIsBehindNAT
        //  4: TerminateGameConn_DEP 12: GetVoiceOptimalRate 20: AdvertiseGame
        //  5: TrackAppUsageEvent    13: GetAuthSessionTicket 21: RequestEncryptedAppTicket
        //  6: GetUserDataFolder     14: GetAuthTicketForWebApi 22: GetEncryptedAppTicket
        //  7: StartVoiceRecording   15: BeginAuthSession
        // *GetSteamID uses hidden-pointer return on x64, called via delegate* unmanaged

        // ── ISteamUtils vtable indices ────────────────────────────────────
        //  0-10: (misc)   11: IsAPICallCompleted
        //  12: GetAPICallFailureReason   13: GetAPICallResult

        // ── ISteamAppTicket vtable indices ────────────────────────────────
        //  0: GetAppOwnershipTicketData

        private static nint VFunc(nint iface, int index)
        {
            nint* vtable = *(nint**)iface;
            return vtable[index];
        }

        private static string? FindSteamPath() =>
            Microsoft.Win32.Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;

        // ── Public entry point (synchronous, runs in worker process) ──────
        public static TicketBundle ExtractTickets(uint appId, IProgress<string>? progress = null)
        {
            string? steamPath = FindSteamPath()
                ?? throw new Exception("Steam install path not found in registry.");

            steamPath = steamPath.Replace('/', '\\').TrimEnd('\\');
            string steamClientPath = Path.Combine(steamPath, "steamclient64.dll");

            SetEnvironmentVariableA("SteamAppId", appId.ToString());
            SetEnvironmentVariableA("SteamGameId", appId.ToString());
            SetDllDirectoryA(steamPath);

            progress?.Report($"Loading {steamClientPath}...");
            nint module = LoadLibraryExA(steamClientPath, 0, LOAD_WITH_ALTERED_SEARCH_PATH);
            if (module == 0)
                throw new Exception($"Failed to load {steamClientPath} (error={GetLastError()})");

            try { return Run(module, appId, progress); }
            finally { FreeLibrary(module); }
        }

        private static TicketBundle Run(nint module, uint appId, IProgress<string>? progress)
        {
            progress?.Report("Getting CreateInterface...");
            nint procAddr = GetProcAddress(module, "CreateInterface");
            if (procAddr == 0)
                throw new Exception("steamclient64.dll has no CreateInterface export.");

            var createInterface = Marshal.GetDelegateForFunctionPointer<CreateInterfaceFn>(procAddr);
            int returnCode = 0;
            nint client = createInterface(kSteamClientInterfaceVersion, &returnCode);
            if (client == 0)
                throw new Exception($"CreateInterface({kSteamClientInterfaceVersion}) failed (returnCode={returnCode}).");

            progress?.Report("Opening Steam session...");
            var fnCreatePipe = Marshal.GetDelegateForFunctionPointer<CreateSteamPipeFn>(VFunc(client, 0));
            var fnReleasePipe = Marshal.GetDelegateForFunctionPointer<BReleaseSteamPipeFn>(VFunc(client, 1));
            var fnConnectUser = Marshal.GetDelegateForFunctionPointer<ConnectToGlobalUserFn>(VFunc(client, 2));
            var fnReleaseUser = Marshal.GetDelegateForFunctionPointer<ReleaseUserFn>(VFunc(client, 4));
            var fnGetUser = Marshal.GetDelegateForFunctionPointer<GetISteamUserFn>(VFunc(client, 5));
            var fnGetUtils = Marshal.GetDelegateForFunctionPointer<GetISteamUtilsFn>(VFunc(client, 9));
            var fnGetGeneric = Marshal.GetDelegateForFunctionPointer<GetISteamGenericInterfaceFn>(VFunc(client, 12));

            int pipe = fnCreatePipe(client);
            if (pipe == 0) throw new Exception("CreateSteamPipe failed. Is Steam running?");

            int user = fnConnectUser(client, pipe);
            if (user == 0)
            {
                fnReleasePipe(client, pipe);
                throw new Exception("ConnectToGlobalUser failed. Is a user logged in?");
            }

            try
            {
                nint steamUser = fnGetUser(client, user, pipe, kSteamUserInterfaceVersion);
                nint steamUtils = fnGetUtils(client, pipe, kSteamUtilsInterfaceVersion);
                nint appTicket = fnGetGeneric(client, user, pipe, kSteamAppTicketInterfaceVersion);

                if (steamUser == 0 || steamUtils == 0)
                    throw new Exception("GetISteamUser or GetISteamUtils returned null.");

                // GetSteamID — CSteamID is a struct so MSVC uses hidden-pointer return on x64
                progress?.Report("Getting SteamID...");
                ulong steamId = 0;
                ((delegate* unmanaged[Cdecl]<nint, ulong*, void>)VFunc(steamUser, 2))(steamUser, &steamId);

                progress?.Report("Extracting ownership ticket...");
                byte[]? ownershipTicket = appTicket != 0
                    ? ExtractOwnershipTicket(appTicket, appId)
                    : null;

                progress?.Report("Requesting encrypted ticket...");
                byte[]? encryptedTicket = ExtractEncryptedTicket(steamUser, steamUtils, progress);

                progress?.Report("Done.");
                return new TicketBundle { SteamID = steamId, AppTicket = ownershipTicket, ETicket = encryptedTicket };
            }
            finally
            {
                fnReleaseUser(client, pipe, user);
                fnReleasePipe(client, pipe);
            }
        }

        private static byte[]? ExtractOwnershipTicket(nint appTicket, uint appId)
        {
            var fn = Marshal.GetDelegateForFunctionPointer<GetAppOwnershipTicketDataFn>(VFunc(appTicket, 0));
            byte[] buffer = new byte[2048];
            uint appIdOff = 0, steamIdOff = 0, sigOff = 0, sigSize = 0;
            fixed (byte* pBuf = buffer)
            {
                uint written = fn(appTicket, appId, pBuf, (uint)buffer.Length, &appIdOff, &steamIdOff, &sigOff, &sigSize);
                if (written == 0 || written > (uint)buffer.Length) return null;
                return buffer[..(int)written];
            }
        }

        private static byte[]? ExtractEncryptedTicket(nint steamUser, nint steamUtils, IProgress<string>? progress)
        {
            var fnRequest = Marshal.GetDelegateForFunctionPointer<RequestEncryptedAppTicketFn>(VFunc(steamUser, 21));
            var fnIsComplete = Marshal.GetDelegateForFunctionPointer<IsAPICallCompletedFn>(VFunc(steamUtils, 11));
            var fnGetResult = Marshal.GetDelegateForFunctionPointer<GetAPICallResultFn>(VFunc(steamUtils, 13));
            var fnGetTicket = Marshal.GetDelegateForFunctionPointer<GetEncryptedAppTicketFn>(VFunc(steamUser, 22));

            ulong hCall = fnRequest(steamUser, null, 0);
            if (hCall == 0) return null;

            const int kMaxWaitMs = 15000;
            const int kStepMs = 50;
            bool failed = false;
            int waited = 0;

            while (!fnIsComplete(steamUtils, hCall, &failed))
            {
                if (waited >= kMaxWaitMs)
                    throw new Exception("Timed out waiting for EncryptedAppTicketResponse_t.");
                Sleep((uint)kStepMs);
                waited += kStepMs;
                progress?.Report($"Waiting for encrypted ticket... ({waited}ms)");
            }

            EncryptedAppTicketResponse_t response = default;
            bool gotResult = fnGetResult(steamUtils, hCall, &response,
                sizeof(EncryptedAppTicketResponse_t), kEncryptedAppTicketCallback, &failed);

            if (!gotResult || failed || response.m_eResult != k_EResultOK) return null;

            uint cbTicket = 0;
            fnGetTicket(steamUser, null, 0, &cbTicket);
            if (cbTicket == 0) return null;

            byte[] ticketBuffer = new byte[cbTicket];
            fixed (byte* pBuf = ticketBuffer)
            {
                if (!fnGetTicket(steamUser, pBuf, (int)cbTicket, &cbTicket)) return null;
            }
            return ticketBuffer;
        }
    }
}