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

    [ObservableProperty]
    public partial bool GlExists { get; set; }

    [ObservableProperty]
    public partial IEnumerable<string> MissingFiles { get; set; } = [];

    [ObservableProperty]
    public partial bool OpenSteamToolExists { get; set; }

    public ObservableCollection<ProfileDbViewModel> Profiles { get; set; }

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

    private async Task ExtractGLWindow(string filename, string password, CancellationToken token)
    {
        while (true)
        {
            try
            {
                ProgressDialog progressDialog = new("Extracting", async (progress) =>
                {
                    await ExtractGL(progress, filename, password, token);
                });
                await contentDialogService.ShowAsync(progressDialog, token);
                snackbarService.Show("Success",
                    "Extracted GL Files!",
                    ControlAppearance.Success,
                    new SymbolIcon(SymbolRegular.CheckmarkCircle16),
                    default);
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
                var result = await contentDialogService.ShowAsync(inputDialog, token);
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

    private Task InitializeViewModel()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);

        if (key?.GetValue(AppName) is not string value)
        {
            _startupMode = StartupMode.None;
        }
        else
        {
            var exePath = value.Split('"', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (exePath == null || !File.Exists(exePath))
            {
                _startupMode = StartupMode.None;
            }
            else if (!exePath.Equals(Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
            {
                _startupMode = StartupMode.None;
            }
            else if (value.Contains("--startsteam"))
            {
                _startupMode = StartupMode.AutoInject;
            }
            else
            {
                _startupMode = StartupMode.Minimized;
            }
        }

        StartupMode = _startupMode;

        RefreshGLStatus();
        RefreshSteamToolsStatus();
        RefreshOpenSteamToolStatus();
        return Task.CompletedTask;
    }

    private bool IsDirty()
    {
        return StartupMode != _startupMode || !Settings.Equals(settingsCopy);
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
    private async Task OnDropGL(DragEventArgs e, CancellationToken token)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
        if (files != null && files.Length == 1) await ExtractGLWindow(files[0], "cs.rin.ru", token);
    }

    [RelayCommand]
    private async Task OnDropOpenSteamTool(DragEventArgs e, CancellationToken token)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
        if (files.Length != 1) return;
        try
        {
            var progressDialog = new ProgressDialog("OpenSteamTool", async (progress) =>
            {
                Directory.CreateDirectory(AppPaths.OpenSteamToolPath);
                using Stream stream = File.OpenRead(files[0]);
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

    [RelayCommand]
    private async Task OnDropSteamTools(DragEventArgs e, CancellationToken token)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
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
        using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, writable: true);

        if (StartupMode != StartupMode.None)
        {
            string value = $"\"{Environment.ProcessPath}\" --minimized";
            if (StartupMode == StartupMode.AutoInject)
            {
                value += " --startsteam";
            }

            key?.SetValue(AppName, value);
        }
        else
        {
            key?.DeleteValue(AppName, throwOnMissingValue: false);
        }

        _startupMode = StartupMode;

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