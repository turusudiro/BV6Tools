using AppPathsCommon;
using BV6Tools.Extensions;
using BV6Tools.Messages;
using BV6Tools.Models;
using BV6Tools.Services;
using BV6Tools.Services.Database;
using BV6Tools.ViewModels.Shared;
using BV6Tools.Views.Dialogs;
using CommunityToolkit.Mvvm.Messaging;
using FileSystemCommon;
using GreenLumaCommon;
using Microsoft.Win32;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using STCommon;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Text.RegularExpressions;
using Wpf.Ui;
using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using WinTask = Microsoft.Win32.TaskScheduler;

namespace BV6Tools.ViewModels.Pages;

public partial class SettingsPageViewModel : ObservableRecipient, INavigationAware
{
    private const string AppName = "BV6Tools";
    private const string RegistryKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private readonly IContentDialogService contentDialogService;
    private readonly DatabaseService databaseService;
    private readonly GameService gameService;
    private readonly ILoggerService logger;
    private readonly ISettingsService settingsService;
    private readonly ISnackbarService snackbarService;
    private Task? _initalizeTask;

    private bool _runAsAdmin;
    private StartupMode _startupMode;
    private AppSettings settingsCopy;

    public SettingsPageViewModel(ILoggerService logger, IContentDialogService contentDialogService, ISnackbarService snackbarService,
        ISettingsService settingsService, DatabaseService databaseService, GameService gameService)
    {
        this.logger = logger;
        this.contentDialogService = contentDialogService;
        this.snackbarService = snackbarService;
        this.settingsService = settingsService;
        this.databaseService = databaseService;
        this.gameService = gameService;
        Settings = settingsService.Settings;
        settingsCopy = Settings.DeepClone();
        Profiles = gameService.Profiles;
        IsActive = true;

        string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString()
               ?? "Unknown Ver";

        AppVersion = $"BV6Tools - {version}";
    }

    public string AppVersion { get; }

    public bool CanRunAsAdmin => StartupMode != StartupMode.None && ProcessCommon.Elevation.IsRunningAsAdmin;

    [ObservableProperty]
    public partial bool GlExists { get; set; }

    [ObservableProperty]
    public partial IEnumerable<string> MissingFiles { get; set; } = [];

    [ObservableProperty]
    public partial bool OpenSteamToolExists { get; set; }

    public ObservableCollection<ProfileDbViewModel> Profiles { get; set; }

    public bool RestartAsAdminRequired { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(UndoCommand))]
    public partial bool RunAsAdmin { get; set; }

    public ProfileDbViewModel SelectedProfile
    {
        get => gameService.ActiveProfile;
        set
        {
            if (value != null)
            {
                gameService.ActiveProfile = value;
            }
        }
    }

    [ObservableProperty]
    public partial AppSettings Settings { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(UndoCommand))]
    [NotifyPropertyChangedFor(nameof(CanRunAsAdmin))]
    public partial StartupMode StartupMode { get; set; }

    [ObservableProperty]
    public partial bool SteamToolsExists { get; set; }

    [GeneratedRegex(@"^(?!.*server).*", RegexOptions.IgnoreCase)]
    private partial Regex GreenLumaFileIsNotServerRegex { get; }

    private bool IsCurrentProfileNotDefault => SelectedProfile.ProfileID != 1;

    public Task OnNavigatedFromAsync() => Task.CompletedTask;

    public Task OnNavigatedToAsync() => _initalizeTask ??= InitializeViewModel();

    protected override void OnActivated()
    {
        Messenger.Register<NotificationCenterMessage, string>(this,
            MessengerTokens.Settings, (r, m) =>
            {
                Save();
                m.Reply(true);
            });
        Messenger.Register<ProfileChangedMessage>(this, OnProfileChangedMessage);
    }

    private static async Task StopWorkerAsync()
    {
        using var pipe = new NamedPipeClientStream(".", "BV6Tools_SteamWatcher_Pipe", PipeDirection.Out);
        await pipe.ConnectAsync(1000);
        using var writer = new StreamWriter(pipe) { AutoFlush = true };
        await writer.WriteLineAsync("--stop");
    }

    [RelayCommand]
    private async Task AddProfile(CancellationToken token)
    {
        var inputDialog = new InputDialog("New Profile")
        {
            InputTitle = "Enter new name",
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel"
        };
        var result = await contentDialogService.ShowAsync(inputDialog, token);
        if (result != ContentDialogResult.Primary) return;
        var profile = databaseService.CreateProfile(inputDialog.InputText);
        ProfileDbViewModel newProfile = new()
        {
            ProfileID = profile.ProfileID,
            ProfileName = profile.ProfileName
        };
        Profiles.Add(newProfile);
        SelectedProfile = newProfile;
    }

    [RelayCommand]
    private void CleanSteamInjectionFiles(string parameter)
    {
        try
        {
            string descFiles = "GreenLuma, SteamTools, OpenSteamTool files";
            switch (parameter)
            {
                case "GL":
                    GreenLuma.CleanGreenLumaFiles(Settings.SteamPath, AppPaths.GLPath);
                    descFiles = "GreenLuma files";
                    break;

                case "ST":
                    ST.DeleteSteamToolsFiles(Settings.SteamPath);
                    descFiles = "SteamTools files";
                    break;

                case "OpenSteamTool":
                    ST.DeleteOpenSteamToolFiles(Settings.SteamPath);
                    descFiles = "OpenSteamTool files";
                    break;

                default:
                    GreenLuma.CleanGreenLumaFiles(Settings.SteamPath, AppPaths.GLPath);
                    ST.DeleteSteamToolsFiles(Settings.SteamPath);
                    ST.DeleteOpenSteamToolFiles(Settings.SteamPath);
                    break;
            }
            snackbarService.Show("Success", $"Deleted all related {descFiles} on {Settings.SteamPath}",
                ControlAppearance.Success, new SymbolIcon(SymbolRegular.Checkmark24), default);
        }
        catch (Exception ex)
        {
            logger.LogError(ex);
            snackbarService.Show(ex.InnerException?.Message ?? "Error", ex.Message, ControlAppearance.Danger, new SymbolIcon(SymbolRegular.Warning16), default);
        }
    }

    [RelayCommand]
    private void DownloadGreenLuma()
    {
        try
        {
            Process.Start(new ProcessStartInfo(GreenLuma.Url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            logger.LogError(ex);
            snackbarService.Show("Error", ex.Message, ControlAppearance.Danger, default, default);
        }
    }

    [RelayCommand]
    private async Task DownloadOpenSteamTool()
    {
        try
        {
            var progressDialog = new ProgressDialog("OpenSteamTool", async (progress) =>
            {
                var status = new Progress<(string Text, double ProgressValue, bool IsIndeterminate)>(s =>
                {
                    progress.Text = s.Text;
                    progress.IsIndeterminate = s.IsIndeterminate;
                    progress.Value = progress.Value;
                });

                progress.Text = "Downloading OpenSteamTool";
                progress.IsIndeterminate = true;
                progress.MaxValue = 100.0;

                Directory.CreateDirectory(AppPaths.OpenSteamToolPath);

                var destinationPath = Path.Combine(AppPaths.OpenSteamToolPath, "opensteamtool.zip");

                await ST.DownloadOpenSteamToolAsync(destinationPath, status, progress.Token);

                progress.Text = "Extracting...";
                progress.IsIndeterminate = false;
                progress.MaxValue = 3;
                progress.Value = 0;

                using Stream stream = File.OpenRead(destinationPath);
                var options = new ReaderOptions
                {
                    LeaveStreamOpen = true
                };
                using var archive = ArchiveFactory.OpenArchive(stream, options);
                foreach (var entry in archive.Entries)
                {
                    progress.Token.ThrowIfCancellationRequested();

                    if (!entry.IsDirectory && entry.Key != null && ST.OpenSteamToolDLLRegex.IsMatch(entry.Key))
                    {
                        var fileName = Path.GetFileName(entry.Key);
                        progress.Text = $"Extracting {fileName}...";
                        progress.Value++;

                        using var outputStream = File.Create(Path.Combine(AppPaths.OpenSteamToolPath, fileName));

                        using var entryStream = entry.OpenEntryStream();
                        await entryStream.CopyToAsync(outputStream, progress.Token);
                    }
                }
                await stream.DisposeAsync();
                File.Delete(destinationPath);
            });
            var result = await contentDialogService.ShowAsync(progressDialog, default);
            if (progressDialog.Progress.IsCancelled) return;
            snackbarService.Show("Success", "Finished download OpenSteamTool", ControlAppearance.Success, default, default);
        }
        catch (Exception ex)
        {
            logger.LogError(ex);
            snackbarService.Show("Error", ex.Message, ControlAppearance.Danger, default, default);
        }
        finally
        {
            RefreshOpenSteamToolStatus();
        }
    }

    [RelayCommand]
    private async Task DownloadSteamTools()
    {
        try
        {
            var progressDialog = new ProgressDialog("SteamTools", async (progress) =>
            {
                progress.Text = "Downloading";
                progress.IsIndeterminate = true;
                await ST.DownloadSteamToolsAsync(AppPaths.STPath, progress.Token);
            });
            await contentDialogService.ShowAsync(progressDialog, default);
            if (progressDialog.Progress.IsCancelled) return;
            snackbarService.Show("Success", "Finished download SteamTools", ControlAppearance.Success, default, default);
        }
        catch (Exception ex)
        {
            logger.LogError(ex);
            snackbarService.Show("Error", ex.Message, ControlAppearance.Danger, default, default);
        }
        finally
        {
            RefreshSteamToolsStatus();
        }
    }

    private async Task ExtractGL(ProgressDialogArgs progress, string path, string password, CancellationToken token)
    {
        progress.IsIndeterminate = true;
        progress.Text = "Extracting GL Files";

        Directory.CreateDirectory(AppPaths.GLPath);
        using Stream stream = File.OpenRead(path);
        var options = ReaderOptions.ForEncryptedArchive(password).WithLeaveStreamOpen(true);
        using var archive = ArchiveFactory.OpenArchive(stream, options);
        if (!archive.Entries.Any(entry => !entry.IsDirectory && entry.Key != null
        && GreenLuma.GreenLumaDLLFile64Regex.IsMatch(entry.Key)))
        {
            throw new FileNotFoundException("No GL Files Found!");
        }
        foreach (var entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested();

            if (!entry.IsDirectory && entry.Key != null && GreenLumaFileIsNotServerRegex.IsMatch(entry.Key))
            {
                var fileName = Path.GetFileName(entry.Key);

                using var outputStream = File.Create(Path.Combine(AppPaths.GLPath, fileName));

                using var entryStream = entry.OpenEntryStream();
                await entryStream.CopyToAsync(outputStream, token);
            }
        }
    }

    private async Task ExtractGLWindow(string filename, string password)
    {
        while (true)
        {
            try
            {
                ProgressDialog progressDialog = new("Extracting", async (progress) =>
                {
                    await ExtractGL(progress, filename, password, progress.Token);
                });
                var result = await contentDialogService.ShowAsync(progressDialog, default);
                if (result != ContentDialogResult.Primary)
                {
                    snackbarService.Show("Success",
                    "Extracted GL Files!",
                    ControlAppearance.Success,
                    new SymbolIcon(SymbolRegular.CheckmarkCircle16),
                    default);
                }
                RefreshGLStatus();
                break;
            }
            catch (InvalidFormatException ex) when (ex.Message.Contains("bad password", StringComparison.OrdinalIgnoreCase))
            {
                var inputDialog = new InputDialog("Wrong Password")
                {
                    InputTitle = "Please enter the password",
                    PrimaryButtonText = "OK",
                    CloseButtonText = "Cancel"
                };
                var result = await contentDialogService.ShowAsync(inputDialog, default);
                if (result != ContentDialogResult.Primary) break;
                password = inputDialog.InputText;
            }
            catch (Exception ex)
            {
                logger.LogError(ex);
                snackbarService.Show("Error",
                    ex.Message,
                    ControlAppearance.Danger,
                    new SymbolIcon(SymbolRegular.Warning12),
                    default);
                break;
            }
        }
    }

    [RelayCommand]
    private async Task ImportFile(string file)
    {
        var fileDialog = new OpenFileDialog
        {
            Filter = "GreenLuma_XXXX_X.X.X-Steam006.zip Files|*.zip",
            Multiselect = false
        };

        switch (file)
        {
            case "GL":
                fileDialog.Title = "Import GreenLuma";
                fileDialog.Filter = "GreenLuma_XXXX_X.X.X-Steam006.zip Files|*.zip";
                break;

            case "ST":
                fileDialog.Title = "Import SteamTools";
                fileDialog.Filter = "SteamTools Files|Core.dll;xinput1_4.dll;dwmapi.dll";
                fileDialog.Multiselect = true;
                break;

            case "OST":
                fileDialog.Title = "Import OpenSteamTool";
                fileDialog.Filter = "OpenSteamTool-(Version)-Release.zip Files|*.zip";
                break;
        }

        if (fileDialog.ShowDialog() != true) return;

        switch (file)
        {
            case "GL":
                await ExtractGLWindow(fileDialog.FileName, "cs.rin.ru");
                break;

            case "ST":
                await ImportSteamTools(fileDialog.FileNames);
                break;

            case "OST":
                await ImportOpenSteamTool(fileDialog.FileName);
                break;
        }
    }

    private async Task ImportOpenSteamTool(string file)
    {
        try
        {
            var progressDialog = new ProgressDialog("OpenSteamTool", async (progress) =>
            {
                Directory.CreateDirectory(AppPaths.OpenSteamToolPath);
                using Stream stream = File.OpenRead(file);
                var options = new ReaderOptions
                {
                    LeaveStreamOpen = true
                };
                using var archive = ArchiveFactory.OpenArchive(stream, options);
                foreach (var entry in archive.Entries)
                {
                    progress.Token.ThrowIfCancellationRequested();

                    if (!entry.IsDirectory && entry.Key != null && ST.OpenSteamToolDLLRegex.IsMatch(entry.Key))
                    {
                        var fileName = Path.GetFileName(entry.Key);
                        progress.Text = $"Extracting {fileName}...";
                        progress.Value++;

                        using var outputStream = File.Create(Path.Combine(AppPaths.OpenSteamToolPath, fileName));

                        using var entryStream = entry.OpenEntryStream();
                        await entryStream.CopyToAsync(outputStream, progress.Token);
                    }
                }
                await stream.DisposeAsync();
                snackbarService.Show("Success", "Extracted OpenSteamTool Files!", ControlAppearance.Success, default, default);
            });
            await contentDialogService.ShowAsync(progressDialog, default);
        }
        finally
        {
            RefreshOpenSteamToolStatus();
        }
    }

    private async Task ImportSteamTools(string[] files)
    {
        int importedCount = 0;
        try
        {
            foreach (var file in files)
            {
                if (!ST.SteamToolsFiles.Any(x => x.IsMatch(file))) continue;
                importedCount++;
                string destFilePath = Path.Combine(AppPaths.STPath, Path.GetFileName(file));
                FileSystem.Copy(file, destFilePath);
            }
        }
        finally
        {
            if (importedCount > 0)
            {
                string message = importedCount == 1 ?
                    "Imported required file" : $"Imported {importedCount} required files";
                snackbarService.Show("Success", message, ControlAppearance.Success,
                    new SymbolIcon(SymbolRegular.CheckmarkCircle24), default);
            }
            RefreshSteamToolsStatus();
        }
    }

    private Task InitializeViewModel()
    {
        string? exePath = null;
        string? argsValue = null;
        bool runAsAdmin = false;

        using (var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath))
        {
            if (key?.GetValue(AppName) is string regValue)
            {
                exePath = regValue.Split('"', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                argsValue = regValue;
                runAsAdmin = false;
            }
        }

        if (exePath == null)
        {
            using var ts = new WinTask.TaskService();
            var task = ts.GetTask(AppName);
            if (task?.Definition.Actions.FirstOrDefault() is WinTask.ExecAction action)
            {
                exePath = action.Path;
                argsValue = action.Arguments;
                runAsAdmin = true;
            }
        }

        if (exePath == null || !File.Exists(exePath))
        {
            _startupMode = StartupMode.None;
            _runAsAdmin = false;
        }
        else if (!exePath.Equals(Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
        {
            _startupMode = StartupMode.None;
            _runAsAdmin = false;
        }
        else if (argsValue != null && argsValue.Contains("--startsteam"))
        {
            _startupMode = StartupMode.AutoInject;
            _runAsAdmin = runAsAdmin;
        }
        else
        {
            _startupMode = StartupMode.Minimized;
            _runAsAdmin = runAsAdmin;
        }

        StartupMode = _startupMode;
        RunAsAdmin = _runAsAdmin;

        RestartAsAdminRequired = RunAsAdmin && !ProcessCommon.Elevation.IsRunningAsAdmin;
        OnPropertyChanged(nameof(RestartAsAdminRequired));

        RefreshGLStatus();
        RefreshSteamToolsStatus();
        RefreshOpenSteamToolStatus();
        return Task.CompletedTask;
    }

    private bool IsDirty()
    {
        var isDirty = StartupMode != _startupMode || RunAsAdmin != _runAsAdmin || !Settings.Equals(settingsCopy);
        if (isDirty)
        {
            Messenger.Send(new NavigationPageBadgeMessage(nameof(SettingsPageViewModel), 1));
        }
        else
        {
            Messenger.Send(new NavigationPageBadgeMessage(nameof(SettingsPageViewModel), 0));
        }
        return isDirty;
    }

    [RelayCommand]
    private void OnBrowseSteamPath()
    {
        var folderDialog = new OpenFolderDialog
        {
            InitialDirectory = Settings.SteamPath
        };

        if (folderDialog.ShowDialog() == true) Settings.SteamPath = folderDialog.FolderName;
    }

    [RelayCommand]
    private void OnChangeTheme(string parameter)
    {
        switch (parameter)
        {
            case "theme_light":
                if (Settings.CurrentTheme == ApplicationTheme.Light)
                {
                    break;
                }
                ApplicationThemeManager.Apply(ApplicationTheme.Light);
                Settings.CurrentTheme = ApplicationTheme.Light;

                break;

            default:
                if (Settings.CurrentTheme == ApplicationTheme.Dark)
                {
                    break;
                }
                ApplicationThemeManager.Apply(ApplicationTheme.Dark);
                Settings.CurrentTheme = ApplicationTheme.Dark;
                break;
        }

        SaveCommand.NotifyCanExecuteChanged();
        UndoCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task OnDropGL(DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
        if (files != null && files.Length == 1) await ExtractGLWindow(files[0], "cs.rin.ru");
    }

    [RelayCommand]
    private async Task OnDropOpenSteamTool(DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
        if (files.Length != 1) return;
        await ImportOpenSteamTool(files[0]);
    }

    [RelayCommand]
    private async Task OnDropSteamTools(DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
        await ImportSteamTools(files);
    }

    private void OnProfileChangedMessage(object r, ProfileChangedMessage m)
    {
        OnPropertyChanged(nameof(SelectedProfile));
    }

    partial void OnSettingsChanging(AppSettings oldValue, AppSettings newValue)
    {
        if (oldValue is not null)
        {
            oldValue.PropertyChanged -= OnSettingsPropertyChanged;
        }

        if (newValue is not null)
        {
            newValue.PropertyChanged += OnSettingsPropertyChanged;
        }
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        SaveCommand.NotifyCanExecuteChanged();
        UndoCommand.NotifyCanExecuteChanged();

        if (e.PropertyName == nameof(AppSettings.Mode))
        {
            Messenger.Send<InjectModeChangedMessage>(new(Settings.Mode));
        }
    }

    private void RefreshGLStatus()
    {
        var glExists = GreenLuma.GreenLumaFilesExists(AppPaths.GLPath, out var missingFilesGL);

        MissingFiles = missingFilesGL;

        GlExists = glExists;
        OnPropertyChanged(nameof(GlExists));
    }

    private void RefreshOpenSteamToolStatus()
    {
        if (!Directory.Exists(AppPaths.OpenSteamToolPath))
        {
            return;
        }

        var files = Directory.EnumerateFiles(FileSystem.FixPathLength(AppPaths.OpenSteamToolPath));
        var foundRequiredFiles = new HashSet<string>();

        foreach (var file in files)
        {
            if (ST.OpenSteamToolDLLRegex.IsMatch(file))
            {
                foundRequiredFiles.Add(file);
            }
        }

        OpenSteamToolExists = foundRequiredFiles.Count >= 3;
    }

    private void RefreshSteamToolsStatus()
    {
        if (!Directory.Exists(AppPaths.STPath))
        {
            return;
        }

        var files = Directory.EnumerateFiles(FileSystem.FixPathLength(AppPaths.STPath));
        var foundRequiredFiles = new HashSet<string>();

        foreach (var file in files)
        {
            if (ST.SteamToolsFiles.Any(x => x.IsMatch(file)))
            {
                foundRequiredFiles.Add(file);
            }
        }

        SteamToolsExists = foundRequiredFiles.Count >= 2;
    }

    [RelayCommand(CanExecute = nameof(IsCurrentProfileNotDefault))]
    private void RemoveProfile(object parameter)
    {
        if (parameter is not ProfileDbViewModel profile) return;
        settingsService.DeleteProfile(SelectedProfile.ProfileID);
        SelectedProfile = Profiles[Profiles.IndexOf(profile) - 1];
        Profiles.Remove(profile);
    }

    [RelayCommand(CanExecute = nameof(IsCurrentProfileNotDefault))]
    private async Task RenameProfile(ProfileDbViewModel profile, CancellationToken token)
    {
        var inputDialog = new InputDialog("Rename Profile")
        {
            InputTitle = "Enter new name",
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel"
        };
        var result = await contentDialogService.ShowAsync(inputDialog, token);
        if (result != ContentDialogResult.Primary) return;
        profile.ProfileName = inputDialog.InputText;
        databaseService.RenameProfile(profile.ProfileID, profile.ProfileName);
    }

    [RelayCommand(CanExecute = nameof(IsDirty))]
    private void Save()
    {
        string exePath = Environment.ProcessPath!;
        string args = "--minimized";
        if (StartupMode == StartupMode.AutoInject)
            args += " --startsteam";

        if (StartupMode != _startupMode)
        {
            // always clear the registry key and task, then re-add if startup is enabled, to avoid duplicates or conflicts
            using (var runKey = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, writable: true))
                runKey?.DeleteValue(AppName, throwOnMissingValue: false);

            using var ts = new WinTask.TaskService();
            if (ts.GetTask(AppName) != null)
            {
                ts.RootFolder.DeleteTask(AppName, exceptionOnNotExists: false);
            }

            if (StartupMode != StartupMode.None)
            {
                if (RunAsAdmin)
                {
                    WinTask.TaskDefinition td = ts.NewTask();
                    td.RegistrationInfo.Description = AppName;
                    td.Principal.RunLevel = WinTask.TaskRunLevel.Highest;
                    td.Principal.LogonType = WinTask.TaskLogonType.InteractiveToken;
                    td.Triggers.Add(new WinTask.LogonTrigger());
                    td.Actions.Add(new WinTask.ExecAction(exePath, args));
                    td.Settings.DisallowStartIfOnBatteries = false;
                    td.Settings.StopIfGoingOnBatteries = false;

                    ts.RootFolder.RegisterTaskDefinition(AppName, td);
                }
                else
                {
                    using var runKey = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, writable: true);
                    runKey?.SetValue(AppName, $"\"{exePath}\" {args}");
                }
            }
        }

        _startupMode = StartupMode;
        _runAsAdmin = RunAsAdmin;

        if (!Settings.Equals(settingsCopy))
        {
            settingsService.Save(setting =>
            {
                setting.OnInject = Settings.OnInject;
                setting.CurrentTheme = Settings.CurrentTheme;
                setting.CloseToTray = Settings.CloseToTray;
                setting.DisableCleanup = Settings.DisableCleanup;
                setting.Mode = Settings.Mode;
                setting.MinimizeToTray = Settings.MinimizeToTray;
                setting.SteamPath = Settings.SteamPath;
                setting.SteamArgs = Settings.SteamArgs;
            });

            if (Settings.DisableCleanup)
            {
                try
                {
                    _ = StopWorkerAsync();
                }
                catch { }
            }

            settingsCopy = Settings.DeepClone();
        }

        SaveCommand.NotifyCanExecuteChanged();
        UndoCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task StopWorker()
    {
        try
        {
            await StopWorkerAsync();
            snackbarService.Show("Success", "Stopped background worker", ControlAppearance.Success, default, default);
        }
        catch
        {
            snackbarService.Show("Failed", "No background worker found!", ControlAppearance.Danger, default, default);
        }
    }

    [RelayCommand(CanExecute = nameof(IsDirty))]
    private async Task Undo()
    {
        if (settingsCopy.CurrentTheme != Settings.CurrentTheme)
        {
            ApplicationThemeManager.Apply(settingsCopy.CurrentTheme);
        }

        StartupMode = _startupMode;
        RunAsAdmin = _runAsAdmin;
        Settings.OnInject = settingsCopy.OnInject;
        Settings.CurrentTheme = settingsCopy.CurrentTheme;
        Settings.CloseToTray = settingsCopy.CloseToTray;
        Settings.DisableCleanup = settingsCopy.DisableCleanup;
        Settings.Mode = settingsCopy.Mode;
        Settings.MinimizeToTray = settingsCopy.MinimizeToTray;
        Settings.SteamPath = settingsCopy.SteamPath;
        Settings.SteamArgs = settingsCopy.SteamArgs;

        SaveCommand.NotifyCanExecuteChanged();
        UndoCommand.NotifyCanExecuteChanged();
    }
}