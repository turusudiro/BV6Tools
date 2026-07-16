using ProcessCommon;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Automation;

namespace GreenLumaCommon
{
    public enum GreenLumaMode
    {
        Normal,
        Stealth,
        Family,
        AnyFolder
    }

    public static partial class GreenLuma
    {
        public const int Limit = 148;

        public const string Url = @"https://cs.rin.ru/forum/viewtopic.php?f=29&t=103709";

        [GeneratedRegex(@"GreenLuma\w+64\.dll", RegexOptions.IgnoreCase)]
        public static partial Regex GreenLumaDLLFile64Regex { get; }

        [GeneratedRegex(@"GreenLuma\w+86\.dll", RegexOptions.IgnoreCase)]
        public static partial Regex GreenLumaDLLFile86Regex { get; }

        [GeneratedRegex(@"ach[a-z]+\.wav", RegexOptions.IgnoreCase)]
        private static partial Regex AchievementRegex { get; }

        [GeneratedRegex(@"GreenLuma.*.files|applist|AppOwnershipTickets|EncryptedAppTickets", RegexOptions.IgnoreCase)]
        private static partial Regex GreenLumaDirectoriesRegex { get; }

        [GeneratedRegex(@"injector\.exe", RegexOptions.IgnoreCase)]
        private static partial Regex InjectorExeRegex { get; }

        [GeneratedRegex(@"x86\w+\.exe", RegexOptions.IgnoreCase)]
        private static partial Regex X86LauncherRegex { get; }

        public static void CleanGreenLumaFiles(string steamPath, string? backupPath = null)
        {
            if (!Directory.Exists(steamPath)) return;

            foreach (var dir in Directory.EnumerateDirectories(steamPath, "*", SearchOption.TopDirectoryOnly))
            {
                bool isDirGreenLuma = GreenLumaDirectoriesRegex.IsMatch(dir);

                if (isDirGreenLuma)
                {
                    Directory.Delete(dir, true);
                }
            }

            string logsPath = Path.Combine(steamPath, "logs");

            if (Directory.Exists(logsPath))
            {
                foreach (var file in Directory.EnumerateFiles(logsPath, "*", SearchOption.TopDirectoryOnly))
                {
                    bool isFileGreenLuma = file.Contains("greenluma", StringComparison.OrdinalIgnoreCase);

                    if (!isFileGreenLuma)
                    {
                        continue;
                    }
                    File.Delete(file);
                }
            }

            foreach (var file in Directory.EnumerateFiles(steamPath, "*", SearchOption.TopDirectoryOnly))
            {
                string fileName = Path.GetFileName(file);

                bool isFileGreenLuma = fileName.Contains("dllinjector", StringComparison.OrdinalIgnoreCase) ||
                    fileName.Contains("greenluma", StringComparison.OrdinalIgnoreCase);

                if (isFileGreenLuma)
                {
                    File.Delete(file);
                }
            }

            string launcherPath = Path.Combine(steamPath, "bin", "x86launcher.exe");
            if (File.Exists(launcherPath))
            {
                FileVersionInfo fileVersionInfo = FileVersionInfo.GetVersionInfo(launcherPath);

                if (fileVersionInfo.LegalCopyright?.Contains("valve", StringComparison.OrdinalIgnoreCase) == false)
                {
                    if (backupPath != null)
                    {
                        string backupLauncherPath = Path.Combine(backupPath, Path.GetFileName(launcherPath) + ".bak");
                        if (File.Exists(backupLauncherPath))
                        {
                            File.Copy(backupLauncherPath, launcherPath, true);
                        }
                    }
                    else
                    {
                        File.Delete(launcherPath);
                    }
                }
            }
        }

        public static bool GreenLumaFilesExists(string path, out List<string> missingFiles)
        {
            missingFiles = [];
            if (!Directory.Exists(path))
            {
                missingFiles.AddRange(["Achievement", "GreenLuma DLL x86", "GreenLuma DLL x64",
            "Injector", "X64launcher", "Family DLL", "Delete Steam App Cache"]);
                return false;
            }

            var files = Directory.GetFiles(path, "*");

            var requiredFiles = new Dictionary<Regex, string>
            {
                { AchievementRegex,         "Achievement"            },
                { GreenLumaDLLFile86Regex,  "GreenLuma DLL x86"      },
                { GreenLumaDLLFile64Regex,  "GreenLuma DLL x64"      },
                { InjectorExeRegex,         "Injector"               },
                { X86LauncherRegex,         "X86launcher"            },
            };

            foreach (var (regex, name) in requiredFiles)
            {
                if (!files.Any(regex.IsMatch))
                {
                    missingFiles.Add(name);
                }
            }

            return missingFiles.Count == 0;
        }

        #region Core

        /// <exception cref="ArgumentNullException">Thrown when <paramref name="steamPath"/> is null or empty.</exception>
        /// <exception cref="AggregateException">Thrown when one or more error dialogs were captured.
        /// Each <see cref="AggregateException.InnerExceptions"/> entry contains the dialog window name and message text.</exception>
        public static async Task<int> StartGreenLuma(string greenLumaPath, string steamPath, IEnumerable<uint> appids,
            string? args = default, GreenLumaMode mode = GreenLumaMode.Stealth)
        {
            var greenlumaFiles = Directory.EnumerateFiles(greenLumaPath, "*", SearchOption.TopDirectoryOnly);

            var greenlumaDll = greenlumaFiles.FirstOrDefault(GreenLumaDLLFile64Regex.IsMatch) ??
                throw new InvalidOperationException("GreenLuma DLL not found!");
            var injectorExe = greenlumaFiles.FirstOrDefault(InjectorExeRegex.IsMatch) ??
                throw new InvalidOperationException("DLLInjector.exe not found!");

            var useFullPathsFromIni = "0";
            var exe = "Steam.exe";
            var commandLine = args;
            var dll = Path.GetFileName(greenlumaDll);
            var waitForProcessTermination = "1";
            var enableFakeParentProcess = "0";
            var createFiles = "1";
            var fileToCreate_1 = "NoQuestion.bin";
            var fileToCreate_2 = string.Empty;
            string dllPath = Path.Combine(steamPath, "DLLInjector.ini");
            string applistPath = Path.Combine(steamPath, "AppList");

            if (mode == GreenLumaMode.Stealth)
            {
                foreach (var file in Directory.EnumerateFiles(steamPath, "steam.exe", SearchOption.TopDirectoryOnly))
                {
                    exe = file;
                }
                dll = greenlumaDll;
                useFullPathsFromIni = "1";
                waitForProcessTermination = "0";
                enableFakeParentProcess = "1";
                createFiles = "2";
                fileToCreate_2 = "StealthMode.bin";
                dllPath = Path.Combine(greenLumaPath, "DLLInjector.ini");
                applistPath = Path.Combine(greenLumaPath, "AppList");
            }
            else
            {
                var greenluma86dll = greenlumaFiles.FirstOrDefault(GreenLumaDLLFile86Regex.IsMatch) ??
                    throw new InvalidOperationException("GreenLuma DLL not found!");
                var x86launcher = greenlumaFiles.FirstOrDefault(X86LauncherRegex.IsMatch) ??
                    throw new InvalidOperationException("GreenLuma x86launcher not found!");

                string injectorDest = Path.Combine(steamPath, Path.GetFileName(injectorExe));

                File.Copy(injectorExe, injectorDest, true);
                File.Copy(greenlumaDll, Path.Combine(steamPath, Path.GetFileName(greenlumaDll)), true);
                File.Copy(greenluma86dll, Path.Combine(steamPath, Path.GetFileName(greenluma86dll)), true);

                string launcherFileName = Path.GetFileName(x86launcher);
                string destX86launcher = Path.Combine(steamPath, "bin", launcherFileName);
                if (File.Exists(destX86launcher))
                {
                    string x86backupPath = Path.Combine(greenLumaPath, launcherFileName + ".bak");
                    FileVersionInfo fileVersionInfo = FileVersionInfo.GetVersionInfo(destX86launcher);

                    if (fileVersionInfo.LegalCopyright?.Contains("valve", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        if (File.Exists(x86backupPath))
                        {
                            var origBytes = File.ReadAllBytes(destX86launcher);
                            var backupBytes = File.ReadAllBytes(x86backupPath);
                            bool fileEqual = origBytes.AsSpan().SequenceEqual(backupBytes.AsSpan());

                            if (!fileEqual)
                            {
                                File.Copy(destX86launcher, x86backupPath, true);
                            }
                        }
                        else
                        {
                            File.Copy(destX86launcher, x86backupPath, true);
                        }
                    }
                }
                File.Copy(x86launcher, destX86launcher, true);

                commandLine = $"-inhibitbootstrap {commandLine}";
                injectorExe = injectorDest;
            }

            var DLLInjectorIniContent = $"""
                                     [DllInjector]
                                     AllowMultipleInstancesOfDLLInjector = 0
                                     UseFullPathsFromIni = {useFullPathsFromIni}

                                     # Exe to start
                                     Exe = {exe}
                                     CommandLine = {commandLine}

                                     # Dll to inject
                                     Dll = {dll}

                                     # Export to call in dll
                                     Export = Init

                                     # Check if call to export returned positive value
                                     CheckReturnValue = 0

                                     # Wait for started exe to close before exiting the DllInjector process.
                                     WaitForProcessTermination = {waitForProcessTermination}

                                     # Set a fake parent process
                                     # EnableMitigationsOnChildProcess must be disabled for this.
                                     EnableFakeParentProcess = {enableFakeParentProcess}
                                     FakeParentProcess = explorer.exe

                                     # Enable security mitigations on child process.
                                     EnableMitigationsOnChildProcess = 0

                                     DEP = 1
                                     SEHOP = 1
                                     HeapTerminate = 1
                                     ForceRelocateImages = 1
                                     BottomUpASLR = 1
                                     HighEntropyASLR = 1
                                     RelocationsRequired = 1
                                     StrictHandleChecks = 0
                                     Win32kSystemCallDisable = 0
                                     ExtensionPointDisable = 1
                                     CFG = 1
                                     CFGExportSuppression = 1
                                     StrictCFG = 1
                                     DynamicCodeDisable = 0
                                     DynamicCodeAllowOptOut = 0
                                     BlockNonMicrosoftBinaries = 0
                                     FontDisable = 1
                                     NoRemoteImages = 1
                                     NoLowLabelImages = 1
                                     PreferSystem32 = 0
                                     RestrictIndirectBranchPrediction = 1
                                     SpeculativeStoreBypassDisable = 0
                                     ShadowStack = 0
                                     ContextIPValidation = 0
                                     BlockNonCETEHCONT = 0
                                     BlockFSCTL = 0

                                     # Number to files to create
                                     CreateFiles = {createFiles}

                                     # Name of the file(s) to create
                                     FileToCreate_1 = {fileToCreate_1}
                                     FileToCreate_2 = {fileToCreate_2}

                                     #Patch an x86 exe to enable IMAGE_FILE_LARGE_ADDRESS_AWARE
                                     Use4GBPatch = 0
                                     FileToPatch_1 =

                                     BootImage =
                                     BootImageWidth = 500
                                     BootImageHeight = 500
                                     BootImageXOffest = 240
                                     BootImageYOffest = 280
                                     """;

            File.WriteAllText(dllPath, DLLInjectorIniContent);

            if (Directory.Exists(applistPath))
            {
                Directory.Delete(applistPath, true);
            }
            Directory.CreateDirectory(applistPath);

            int totalAppid = appids.Count();
            int i = 0;

            foreach (var appid in appids)
            {
                string pathTxt = Path.Combine(applistPath, $"{i}.txt");
                File.WriteAllText(pathTxt, appid.ToString());
                i++;
            }

            if (mode == GreenLumaMode.Normal)
            {
                await StartTrackedAsync(injectorExe, "steam");
            }
            else
            {
                await StartTrackedAsync(injectorExe);
            }
            var processes = Process.GetProcessesByName("steam");

            var steamProcess = Process.GetProcessesByName("steam").FirstOrDefault()
                ?? throw new InvalidOperationException("Failed to inject GreenLuma!, Steam is not detected running!");

            return steamProcess.Id;
        }

        /// <summary>
        /// Starts a process and monitors it for error dialogs, automatically dismissing them by clicking OK/Yes/Close.
        /// <br/>
        /// The tracking stops when the process exits, <paramref name="targetExe"/> is found running, or <paramref name="timeout"/> is reached.
        /// </summary>
        /// <param name="path">Path to the executable to start. Supports relative paths with '..'.</param>
        /// <param name="targetExe">Optional target steam executable name to watch for. When found running, tracking stops and the task resolves as success.</param>
        /// <param name="timeout">Optional timeout duration for tracking. When reached, resolves as success or throws if errors were collected.</param>
        /// <returns>
        /// A tuple of the started <see cref="Process"/> and a <see cref="Task"/> that completes when tracking stops.
        /// <br/>
        /// Await the task to observe errors — it resolves silently on success or throws on failure.
        /// </returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="path"/> is null or empty.</exception>
        /// <exception cref="AggregateException">Thrown when one or more error dialogs were captured.
        /// Each <see cref="AggregateException.InnerExceptions"/> entry contains the dialog window name and message text.</exception>
        public static Task StartTrackedAsync(
            string path,
            string? targetExe = null,
            TimeSpan? timeout = null)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentNullException(nameof(path), "Executable path cannot be empty.");
            }

            var process = ProcessHelper.RunAsRestrictedUser(path)
                ?? throw new InvalidOperationException("Failed to start process.");

            var tcs = new TaskCompletionSource<bool>();
            var errors = new List<string>();
            int pid = process.Id;

            Task.Run(async () =>
            {
                var textCondition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text);
                var buttonCondition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button);
                var pidCondition = new PropertyCondition(AutomationElement.ProcessIdProperty, pid);
                var seenWindows = new HashSet<string>();
                var exeName = targetExe != null ? Path.GetFileNameWithoutExtension(targetExe) : null;
                var timeoutAt = timeout.HasValue ? DateTime.UtcNow + timeout.Value : DateTime.MaxValue;

                try
                {
                    while (!process.HasExited)
                    {
                        // check dialogs first before any exit condition
                        foreach (AutomationElement window in AutomationElement.RootElement.FindAll(TreeScope.Children, pidCondition))
                        {
                            try
                            {
                                var runtimeId = string.Join(",", window.GetRuntimeId());
                                if (!seenWindows.Add(runtimeId)) continue;

                                var text = window.FindFirst(TreeScope.Children, textCondition)?.Current.Name ?? "";
                                if (string.IsNullOrEmpty(text)) continue;

                                var windowName = window.Current.Name;

                                var buttons = window.FindAll(TreeScope.Children, buttonCondition);
                                var okButton = buttons.Cast<AutomationElement>()
                                    .FirstOrDefault(b => new[] { "ok", "yes", "close" }.Contains(b.Current.Name.ToLower()))
                                    ?? (buttons.Count > 0 ? buttons[0] : null);

                                try
                                {
                                    if (okButton?.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern) == true)
                                        ((InvokePattern)pattern).Invoke();
                                }
                                catch (ElementNotAvailableException) { }

                                errors.Add($"{windowName}: {text}");
                            }
                            catch (ElementNotAvailableException) { }
                        }

                        // exit conditions checked AFTER dialog sweep
                        if (exeName != null && Process.GetProcessesByName(exeName).Length != 0)
                        {
                            if (errors.Count > 0)
                            {
                                try { process.Kill(); } catch { }
                                tcs.TrySetException(new AggregateException(errors.Select(e => new Exception(e))));
                            }
                            else
                                tcs.TrySetResult(true);
                            return;
                        }

                        if (DateTime.UtcNow >= timeoutAt)
                        {
                            if (errors.Count > 0)
                            {
                                try { process.Kill(); } catch { }
                                tcs.TrySetException(new AggregateException(errors.Select(e => new Exception(e))));
                            }
                            else
                                tcs.TrySetResult(true);
                            return;
                        }

                        await Task.Delay(100);
                    }
                    if (errors.Count > 0)
                    {
                        try { process.Kill(); } catch { }
                        tcs.TrySetException(new AggregateException(errors.Select(e => new Exception(e))));
                    }
                    else
                        tcs.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
                finally
                {
                    process.Dispose();
                }
            });

            return tcs.Task;
        }

        #endregion Core
    }
}