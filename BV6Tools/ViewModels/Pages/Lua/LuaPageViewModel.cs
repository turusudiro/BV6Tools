using AppPathsCommon;
using BV6Tools.Collections;
using BV6Tools.Extensions;
using BV6Tools.Messages;
using BV6Tools.Services;
using BV6Tools.Services.Database;
using BV6Tools.Services.Database.Models;
using BV6Tools.Services.ManifestDownloader;
using BV6Tools.Tracking;
using BV6Tools.ViewModels.Shared;
using BV6Tools.Views.Dialogs;
using CommunityToolkit.Mvvm.Messaging;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using STCommon;
using SteamCommon;
using System.Collections.Immutable;
using System.ComponentModel;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Data;
using System.Windows.Threading;
using Wpf.Ui;
using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;

namespace BV6Tools.ViewModels.Pages.Lua
{
    public partial class LuaPageViewModel : ObservableRecipient, INavigationAware
    {
        private readonly IContentDialogService contentDialogService;
        private readonly DatabaseService databaseService;
        private readonly HashSetNotify<uint> dirtyLua = [];
        private readonly IDisposable? gamesSubscription;
        private readonly ILoggerService logger;
        private readonly IManifestDownloader manifestDownloader;
        private readonly ISettingsService settingsService;
        private readonly ISnackbarService snackbarService;
        private Task? initializeTask;

        public LuaPageViewModel(ILoggerService logger, IContentDialogService contentDialogService,
            ISnackbarService snackbarService, DatabaseService databaseService, GameService gameService,
            IManifestDownloader manifestDownloader, ISettingsService settingsService)
        {
            this.logger = logger;
            this.contentDialogService = contentDialogService;
            this.snackbarService = snackbarService;
            this.databaseService = databaseService;
            this.manifestDownloader = manifestDownloader;
            this.settingsService = settingsService;

            gameService.Apps.ItemPropertyChanged += OnGameServiceAppsItemPropertyChanged;
            gameService.Apps.CollectionChanged += OnGameServiceAppsCollectionChanged;

            Games = [];

            Games.StartTracking();

            gamesSubscription = Games.SubscribeOnChanged(OnGameChanged);
            dirtyLua.OnDirtyChanged += () =>
            {
                SaveCommand.NotifyCanExecuteChanged();
                Messenger.Send(new NavigationPageBadgeMessage(nameof(LuaPageViewModel), dirtyLua.Count));
            };
            GamesView = CollectionViewSource.GetDefaultView(Games);
            GamesView.SortDescriptions.Add(new SortDescription(nameof(LuaViewModel.AppId), ListSortDirection.Ascending));
            GamesView.Filter = new Predicate<object>(obj =>
            {
                if (obj is LuaViewModel lua)
                {
                    return (lua.Name?.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase) ?? false)
                    || lua.AppId.ToString().Contains(SearchText);
                }
                return true;
            });

            databaseService.Database.SynchronizeSchema<LuaDb>();
            IsActive = true;
        }

        public ObservableDictionary<uint, LuaViewModel> Games { get; }

        public ICollectionView GamesView { get; }

        [ObservableProperty]
        public partial string SearchText { get; set; } = string.Empty;

        [GeneratedRegex(@"(?<appid>\d{3,})")]
        private partial Regex AppIdFileRegex { get; }

        [GeneratedRegex(@"(?<depotid>\d+)_(?<manifestid>\d+)\.manifest$")]
        private partial Regex ManifestIDFileRegex { get; }

        public Task OnNavigatedFromAsync() => Task.CompletedTask;

        public Task OnNavigatedToAsync() => initializeTask ??= InitializeViewModel();

        protected override void OnActivated()
        {
            Messenger.Register<DownloadMessage>(this, DownloadMessageHandler);
        }

        [RelayCommand]
        private static void AddItem(object[] parameter)
        {
            if (parameter[0] is not LuaViewModel game) return;

            switch (parameter[1] as string)
            {
                case "AddAppId":
                    if (parameter.ElementAtOrDefault(2) is not NumberBox addAppIdNumBox) return;
                    if (parameter.ElementAtOrDefault(3) is not System.Windows.Controls.ComboBox addAppIdTypeComboBox) return;
                    if (addAppIdTypeComboBox.SelectedValue is not AddAppIdType addAppIdType) return;
                    if (parameter.ElementAtOrDefault(4) is not TextBox addKeyTextBox) return;

                    if (!uint.TryParse(addAppIdNumBox.Text, out var appid)) return;

                    game.AddAppId.NewOrUpdate(new AddAppIdViewModel
                    {
                        AppId = appid,
                        AppType = addAppIdType,
                        Key = addKeyTextBox.Text,
                        IsEnabled = true
                    }, (existing, incoming) => existing.AppId == incoming.AppId);

                    addAppIdNumBox.Clear();
                    addAppIdTypeComboBox.SelectedItem = AddAppIdType.Depot;
                    addKeyTextBox.Clear();

                    break;

                case "SetManifestID":
                    if (parameter.ElementAtOrDefault(2) is not NumberBox manifestAppIdNumBox) return;
                    if (parameter.ElementAtOrDefault(3) is not TextBox manifestGIDTextBox) return;
                    if (parameter.ElementAtOrDefault(4) is not TextBox manifestSizeTextBox) return;

                    if (!uint.TryParse(manifestAppIdNumBox.Text, out appid))
                        return;

                    game.SetManifestID.NewOrUpdate(new SetManifestIDViewModel
                    {
                        AppId = appid,
                        GID = manifestGIDTextBox.Text,
                        Size = manifestSizeTextBox.Text,
                        IsEnabled = true
                    }, (existing, incoming) => existing.AppId == incoming.AppId);

                    manifestAppIdNumBox.Clear();
                    manifestGIDTextBox.Clear();
                    manifestSizeTextBox.Clear();

                    break;

                case "AddToken":
                    if (parameter.ElementAtOrDefault(2) is not NumberBox addTokenAppIdNumBox) return;
                    if (parameter.ElementAtOrDefault(3) is not TextBox tokenTextBox) return;

                    if (!uint.TryParse(addTokenAppIdNumBox.Text, out appid)) return;

                    game.AddToken.NewOrUpdate(new LuaTokenViewModel
                    {
                        AppId = appid,
                        Token = tokenTextBox.Text,
                        IsEnabled = true
                    }, (existsing, incoming) => existsing.AppId == incoming.AppId);

                    addTokenAppIdNumBox.Clear();
                    tokenTextBox.Clear();

                    break;
            }
        }

        [RelayCommand]
        private static void Remove(ITrackable trackable)
        {
            trackable.Delete();
        }

        [RelayCommand]
        private void Add(NumberBox parameter)
        {
            if (!uint.TryParse(parameter.Text, out var appid)) return;
            if (Games.ContainsKey(appid)) return;

            Games.New(new LuaViewModel
            {
                AppId = appid,
            });

            parameter.Clear();
        }

        [RelayCommand]
        private async Task DownloadManifestAsync()
        {
            var numbox = new NumberBox()
            {
                PlaceholderText = "AppId",
                ClearButtonEnabled = false,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden,
            };

            var dialog = new SimpleContentDialogCreateOptions()
            {
                Title = "Enter AppId",
                Content = numbox,
                PrimaryButtonText = "Download",
                CloseButtonText = "Cancel",
            };
            var result = await contentDialogService.ShowSimpleDialogAsync(dialog);

            if (result != ContentDialogResult.Primary) return;

            if (!uint.TryParse(numbox.Text, out var appid))
            {
                snackbarService.Show("Aborting", "Invalid appid!", ControlAppearance.Danger, default, default);
                return;
            }
            await ManifestDownload(appid);
        }

        private void DownloadMessageHandler(object r, DownloadMessage m)
        {
            _ = Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                await ManifestDownload(m.AppId, m.Name);
            }, DispatcherPriority.Send);
        }

        private async Task InitializeViewModel()
        {
            var luaDb = databaseService.Database.LoadAll<LuaDb>();
            var caches = databaseService.GetAppsCache(luaDb.Select(x => x.AppId));

            foreach (var game in luaDb)
            {
                if (Games.TryGetValue(game.AppId, out var existing))
                {
                    if (existing.HasChanges())
                    {
                        var pendingAppIds = existing.AddAppId.ToList();
                        var pendingManifests = existing.SetManifestID.ToList();
                        var pendingTokens = existing.AddToken.ToList();

                        existing.AddAppId.Clear();
                        existing.SetManifestID.Clear();
                        existing.AddToken.Clear();

                        foreach (var x in game.AddAppId)
                            existing.AddAppId.New(new() { AppId = x.AppId, IsEnabled = x.IsEnabled, AppType = x.AppType, Key = x.Key });

                        foreach (var x in game.ManifestID)
                            existing.SetManifestID.New(new() { AppId = x.AppId, IsEnabled = x.IsEnabled, GID = x.GID, Size = x.Size });

                        foreach (var x in game.AddToken)
                            existing.AddToken.New(new() { AppId = x.AppId, IsEnabled = x.IsEnabled, Token = x.Token });

                        existing.Apply();

                        foreach (var x in pendingAppIds)
                            existing.AddAppId.NewOrUpdate(x, (e, i) => e.AppId == i.AppId);

                        foreach (var x in pendingManifests)
                            existing.SetManifestID.NewOrUpdate(x, (e, i) => e.AppId == i.AppId);

                        foreach (var x in pendingTokens)
                            existing.AddToken.NewOrUpdate(x, (e, i) => e.AppId == i.AppId);
                    }
                    continue;
                }

                var lua = new LuaViewModel
                {
                    AppId = game.AppId,
                    Name = caches.GetValueOrDefault(game.AppId)?.Name,
                    IsEnabled = game.IsEnabled
                };

                foreach (var x in game.AddAppId)
                    lua.AddAppId[x.AppId] = new() { AppId = x.AppId, IsEnabled = x.IsEnabled, AppType = x.AppType, Key = x.Key };

                foreach (var x in game.ManifestID)
                    lua.SetManifestID[x.AppId] = new() { AppId = x.AppId, IsEnabled = x.IsEnabled, GID = x.GID, Size = x.Size };

                foreach (var x in game.AddToken)
                    lua.AddToken[x.AppId] = new() { AppId = x.AppId, IsEnabled = x.IsEnabled, Token = x.Token };

                Games.New(lua);
                lua.Apply();
            }
        }

        private bool IsDirty() => dirtyLua.Count > 0;

        private async Task ManifestDownload(uint appid, string? name = default)
        {
            try
            {
                var progressDialog = new ProgressDialog("Manifest Downloader", async progress =>
                {
                    var progressInfo = new Progress<ProgressInfo>((p) =>
                    {
                        progress.MaxValue = p.MaxValue;
                        progress.Value = p.Value;
                        progress.Text = p.Text;
                        progress.IsIndeterminate = p.IsIndeterminate;
                    });
                    var result = await manifestDownloader.DownloadManifestAsync(appid, progressInfo, progress.Token);

                    progress.Value = 0;
                    progress.MaxValue = result.ManifestResult.Count;
                    progress.IsIndeterminate = false;

                    string depotCachePath = Path.Combine(settingsService.Settings.SteamPath, "depotcache");
                    string manifestPath = Path.Combine(AppPaths.ManifestPath, appid.ToString());
                    Directory.CreateDirectory(depotCachePath);
                    Directory.CreateDirectory(manifestPath);

                    LuaData? luaData = null;

                    var appids = new HashSet<uint>();
                    if (result.LuaData is { } luaDataValue)
                    {
                        luaData = luaDataValue;
                        appids = [.. luaDataValue.Appids.Keys.Select(uint.Parse)];
                    }
                    appids.Add(appid);

                    var caches = databaseService.GetAppsCache(appids);

                    foreach (var manifest in result.ManifestResult)
                    {
                        progress.Value++;
                        progress.Text = $"Saving {manifest.FileName} ({manifest.Bytes.LongLength.ToSizeString()})";

                        string steamDepotCachePath = Path.Combine(depotCachePath, manifest.FileName);
                        string dataManifestPath = Path.Combine(manifestPath, manifest.FileName);

                        if (!File.Exists(steamDepotCachePath) || new FileInfo(steamDepotCachePath).Length != manifest.Bytes.Length)
                        {
                            await File.WriteAllBytesAsync(steamDepotCachePath, manifest.Bytes);
                        }

                        if (!File.Exists(dataManifestPath) || new FileInfo(dataManifestPath).Length != manifest.Bytes.Length)
                        {
                            await File.WriteAllBytesAsync(dataManifestPath, manifest.Bytes);
                        }
                    }
                    if (luaData.HasValue)
                    {
                        ProcessLua(luaData.Value, appid);
                        Messenger.Send(new LuaAddedMessage(luaData.Value, appid, name));
                    }
                });
                await contentDialogService.ShowAsync(progressDialog, default);
            }
            catch (Exception ex)
            {
                logger.LogError(ex);
                snackbarService.Show("Error", ex.Message, ControlAppearance.Danger, default, default);
            }
        }

        private void OnGameChanged(ITrackable trackable, ITrackable? trackableParent)
        {
            LuaViewModel game = trackableParent != null ? (LuaViewModel)trackableParent : (LuaViewModel)trackable;
            switch (trackable)
            {
                case AddAppIdViewModel addAppId:
                    var addAppIdStatus = addAppId.GetStatus();
                    if (addAppIdStatus == TrackingStatus.Discarded)
                    {
                        game.AddAppId.Remove(addAppId);
                    }
                    else
                    {
                        addAppId.IsVisible = addAppIdStatus != TrackingStatus.Deleted;
                    }
                    break;

                case SetManifestIDViewModel setManifest:
                    var setManfiestStatus = setManifest.GetStatus();
                    if (setManfiestStatus == TrackingStatus.Discarded)
                    {
                        game.SetManifestID.Remove(setManifest);
                    }
                    else
                    {
                        setManifest.IsVisible = setManfiestStatus != TrackingStatus.Deleted;
                    }
                    break;

                case LuaTokenViewModel token:
                    var tokenStatus = token.GetStatus();
                    if (tokenStatus == TrackingStatus.Discarded)
                    {
                        game.AddToken.Remove(token);
                    }
                    else
                    {
                        token.IsVisible = tokenStatus != TrackingStatus.Deleted;
                    }
                    break;
            }
            if (trackable is LuaViewModel lua)
            {
                lua.IsVisible = !lua.IsDeleted();
            }
            var status = game.GetStatus();
            if (status == TrackingStatus.Discarded)
            {
                Games.Remove(game);
                dirtyLua.Remove(game.AppId);
            }
            else if (status == TrackingStatus.Unchanged)
            {
                dirtyLua.Remove(game.AppId);
            }
            else
            {
                dirtyLua.Add(game.AppId);
            }
        }

        private void OnGameServiceAppsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems == null) return;
            foreach (AppViewModel app in e.NewItems)
            {
                if (Games.TryGetValue(app.AppId, out var lua))
                {
                    lua.Name = app.Name;
                }
            }
        }

        private void OnGameServiceAppsItemPropertyChanged(AppViewModel app, string? propName)
        {
            if (propName != nameof(AppViewModel.Name)) return;
            if (Games.TryGetValue(app.AppId, out var lua))
            {
                lua.Name = app.Name;
            }
        }

        [RelayCommand]
        private async Task OnLuaZipDrop(string[] files)
        {
            if (files is null || files.Length == 0) return;
            Dictionary<string, LuaData> luaDataByAppid = [];
            foreach (var file in files)
            {
                string password = string.Empty;

                while (true)
                {
                    try
                    {
                        using Stream stream = File.OpenRead(file);
                        var options = ReaderOptions.ForEncryptedArchive(password).WithLeaveStreamOpen(true);
                        using var archive = ArchiveFactory.OpenArchive(stream, options);

                        var lua = archive.Entries.FirstOrDefault(x => x.Key != null && x.Key.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)) ??
                        throw new FileNotFoundException("No .lua file found in zip!");

                        var match = AppIdFileRegex.Match(file);
                        var appid = match.Groups["appid"]?.Value ?? "";
                        if (string.IsNullOrEmpty(appid) && lua.Key != null)
                        {
                            match = AppIdFileRegex.Match(lua.Key);
                            appid = match.Groups["appid"]?.Value ?? "";
                        }

                        if (string.IsNullOrEmpty(appid)) throw new InvalidOperationException("No appid found!");

                        string luaContent;

                        using (var entryStream = lua.OpenEntryStream())
                        using (var reader = new StreamReader(entryStream))
                        {
                            luaContent = await reader.ReadToEndAsync();
                        }

                        var luaData = ST.ParseFromLua(luaContent);

                        luaDataByAppid[appid] = luaData;

                        string depotCachePath = Path.Combine(settingsService.Settings.SteamPath, "depotcache");
                        string manifestPath = Path.Combine(AppPaths.ManifestPath, appid.ToString());
                        Directory.CreateDirectory(depotCachePath);
                        Directory.CreateDirectory(manifestPath);

                        Dictionary<string, ManifestData> manifestDataByAppid = [];

                        foreach (var entry in archive.Entries)
                        {
                            if (entry.Key == null || entry.IsDirectory) continue;
                            var fileName = Path.GetFileName(entry.Key);
                            if (fileName.EndsWith(".manifest", StringComparison.OrdinalIgnoreCase))
                            {
                                match = ManifestIDFileRegex.Match(fileName);
                                if (!match.Success) continue;
                                string gid = match.Groups["manifestid"].Value;
                                string depotid = match.Groups["depotid"].Value;

                                if (ST.IsSharedDepot(depotid)) continue;

                                manifestDataByAppid[depotid] = manifestDataByAppid.GetValueOrDefault(depotid) with { ManifestID = gid };

                                try
                                {
                                    using var entryStream = entry.OpenEntryStream();
                                    using var ms = new MemoryStream();
                                    await entryStream.CopyToAsync(ms);
                                    System.Diagnostics.Debug.WriteLine($"Size is {ms.Length}");
                                    var bytes = ms.ToArray();
                                    await File.WriteAllBytesAsync(Path.Combine(manifestPath, fileName), bytes);
                                    await File.WriteAllBytesAsync(Path.Combine(depotCachePath, fileName), bytes);
                                }
                                catch (Exception ex)
                                {
                                    logger.LogError("Cannot extract .manifest file!", ex);
                                }
                            }
                        }

                        var existing = luaDataByAppid[appid].Manifest.ToDictionary();
                        foreach (var (k, v) in manifestDataByAppid)
                        {
                            existing[k] = existing.GetValueOrDefault(k) with { ManifestID = v.ManifestID };
                        }

                        luaDataByAppid[appid] = luaDataByAppid[appid] with
                        {
                            Manifest = existing
                        };
                        break;
                    }
                    catch (InvalidFormatException ex) when (ex.Message.Contains("bad password", StringComparison.OrdinalIgnoreCase))
                    {
                        var inputDialog = new InputDialog("Bad Password")
                        {
                            InputTitle = $"Please enter the password for {Path.GetFileName(file)}",
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
                        snackbarService.Show("Error", ex.Message, ControlAppearance.Danger, default, default);
                        break;
                    }
                }
            }

            if (luaDataByAppid.Count <= 0)
            {
                return;
            }

            foreach (var lua in luaDataByAppid)
            {
                uint appid = uint.Parse(lua.Key);
                ProcessLua(lua.Value, appid);
            }
            snackbarService.Show("Success", $"Saved {luaDataByAppid.Count} lua info", ControlAppearance.Success,
                new SymbolIcon(SymbolRegular.Checkmark24), default);
        }

        partial void OnSearchTextChanged(string value)
        {
            GamesView.Refresh();
        }

        private void ProcessLua(LuaData luaData, uint appid)
        {
            var cache = databaseService.GetAppsCache([appid]);
            if (!Games.TryGetValue(appid, out var game))
            {
                var dbGame = databaseService.Database.LoadByPrimaryKey<LuaDb>(appid);

                game = new LuaViewModel
                {
                    AppId = appid,
                    Name = cache.GetValueOrDefault(appid)?.Name,
                    IsEnabled = dbGame?.IsEnabled ?? true
                };

                if (dbGame != null)
                {
                    foreach (var x in dbGame.AddAppId)
                        game.AddAppId[x.AppId] = new() { AppId = x.AppId, IsEnabled = x.IsEnabled, AppType = x.AppType, Key = x.Key };

                    foreach (var x in dbGame.ManifestID)
                        game.SetManifestID[x.AppId] = new() { AppId = x.AppId, IsEnabled = x.IsEnabled, GID = x.GID, Size = x.Size };

                    foreach (var x in dbGame.AddToken)
                        game.AddToken[x.AppId] = new() { AppId = x.AppId, IsEnabled = x.IsEnabled, Token = x.Token };
                }

                Games.New(game);

                if (dbGame != null)
                {
                    game.Apply();
                }
            }

            foreach (var (id, token) in luaData.TokenData)
            {
                game.AddToken.NewOrUpdate(new()
                {
                    AppId = uint.Parse(id),
                    IsEnabled = true,
                    Token = token
                }, (existing, incoming) => existing.AppId == incoming.AppId);
            }

            var depotKeyByID = new Dictionary<string, string>();
            List<uint> dlcs = [];

            foreach (var id in luaData.Appids)
            {
                if (id.Value is LuaAppIdWithKey luaAppIdWithKey)
                {
                    depotKeyByID.Add(id.Key, luaAppIdWithKey.DecryptionKey);
                    game.AddAppId.NewOrUpdate(new AddAppIdViewModel
                    {
                        AppId = uint.Parse(id.Key),
                        IsEnabled = true,
                        Key = luaAppIdWithKey.DecryptionKey,
                        AppType = luaAppIdWithKey.Flag.Equals("1") ? AddAppIdType.App : AddAppIdType.Depot,
                    }, (existing, incoming) => existing.AppId == incoming.AppId);
                    continue;
                }
                dlcs.Add(uint.Parse(id.Key));
            }

            foreach (var (id, manifest) in luaData.Manifest)
            {
                uint depotid = uint.Parse(id);
                var setManifest = new SetManifestIDViewModel
                {
                    AppId = depotid,
                    IsEnabled = true,
                    GID = manifest.ManifestID,
                    Size = manifest.Size
                };

                game.SetManifestID.NewOrUpdate(setManifest, (existing, incoming) => existing.AppId == incoming.AppId);
            }
            var vdfPath = Path.Combine(settingsService.Settings.SteamPath, "config", "config.vdf");

            SteamDepot.SaveDecryptionKey(depotKeyByID, vdfPath);

            dlcs.Remove(appid);

            var addedMessage = new AddedMessage(appid, DLC: dlcs);
            Messenger.Send(addedMessage, MessengerTokens.Dashboard);
        }

        [RelayCommand(CanExecute = nameof(IsDirty))]
        private void Save(string? parameter)
        {
            if (parameter != null)
            {
                foreach (var id in dirtyLua.ToList())
                {
                    if (Games.TryGetValue(id, out var game))
                    {
                        if (game.Undo())
                        {
                            Games.Remove(game);
                        }
                        dirtyLua.Remove(id);
                    }
                }
            }
            else
            {
                databaseService.Database.BeginTransaction();
                try
                {
                    List<LuaViewModel> processed = [];

                    foreach (var appid in dirtyLua.ToList())
                    {
                        Games.TryGetValue(appid, out var game);
                        if (game == null) continue;

                        if (game.IsDeleted())
                        {
                            databaseService.Database.Delete(new LuaDb { AppId = appid });
                        }
                        else
                        {
                            var addappid = game.AddAppId
                                .Where(x => !x.IsDeleted())
                                .Select(x => new AddAppIdData
                                {
                                    AppId = x.AppId,
                                    IsEnabled = x.IsEnabled,
                                    Key = x.Key
                                }).ToList();

                            var setmanifest = game.SetManifestID
                                .Where(x => !x.IsDeleted())
                                .Select(x => new SetManifestID
                                {
                                    AppId = x.AppId,
                                    IsEnabled = x.IsEnabled,
                                    GID = x.GID,
                                    Size = x.Size
                                }).ToList();

                            var token = game.AddToken
                                .Where(x => !x.IsDeleted())
                                .Select(x => new LuaToken
                                {
                                    AppId = x.AppId,
                                    IsEnabled = x.IsEnabled,
                                    Token = x.Token
                                }).ToList();

                            var lua = new LuaDb
                            {
                                AppId = appid,
                                IsEnabled = game.IsEnabled,
                                AddAppId = addappid,
                                ManifestID = setmanifest,
                                AddToken = token
                            };
                            databaseService.Database.Save(lua);
                        }

                        processed.Add(game);
                    }

                    databaseService.Database.Commit();

                    foreach (var game in processed)
                    {
                        var luaPath = Path.Combine(AppPaths.LuaPath, $"{game.AppId}.lua");
                        bool deleted = !game.IsEnabled;
                        if (game.Apply())
                        {
                            Games.Remove(game);
                            deleted = true;
                        }
                        if (deleted)
                        {
                            File.Delete(luaPath);
                        }
                        else
                        {
                            ST.SaveLua(game.ToLuaData(), luaPath);
                        }

                        dirtyLua.Remove(game.AppId);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex);
                    databaseService.Database.Rollback();
                }
            }
        }
    }
}