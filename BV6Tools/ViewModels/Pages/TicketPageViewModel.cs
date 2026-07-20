using BV6Tools.Collections;
using BV6Tools.Messages;
using BV6Tools.Services;
using BV6Tools.Services.Database;
using BV6Tools.Services.Database.Models;
using BV6Tools.Tracking;
using BV6Tools.Views.Dialogs;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Win32;
using SteamCommon;
using System.IO;
using Wpf.Ui;
using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui.Controls;

namespace BV6Tools.ViewModels.Pages
{
    public partial class TicketPageViewModel : ObservableRecipient, INavigationAware
    {
        private readonly IContentDialogService contentDialogService;
        private readonly DatabaseService databaseService;
        private readonly HashSetNotify<uint> dirtyTicket = [];
        private readonly ILoggerService loggerService;
        private readonly ISnackbarService snackbarService;
        private Task? _initializeTask;

        public TicketPageViewModel(ILoggerService loggerService, ISnackbarService snackbarService,
            IContentDialogService contentDialogService, DatabaseService databaseService)
        {
            this.loggerService = loggerService;
            this.snackbarService = snackbarService;
            this.contentDialogService = contentDialogService;
            this.databaseService = databaseService;

            databaseService.Database.SynchronizeSchema<TicketDb>();
            IsActive = true;
        }

        protected override void OnActivated()
        {
            base.OnActivated();
            Messenger.Register<NotificationCenterMessage, string>(this,
            MessengerTokens.Ticket, (r, m) =>
            {
                Save();
                m.Reply(true);
            });
        }

        public ObservableDictionary<uint, TicketViewModel> Tickets { get; set; } = [];

        public async Task InitalizeViewModel()
        {
            foreach (var db in databaseService.Database.LoadAll<TicketDb>())
            {
                Tickets[db.AppId] = new TicketViewModel
                {
                    AppId = db.AppId,
                    AppTicketBytes = db.AppTicketBytes,
                    EncryptedTicketBytes = db.EncryptedTicketBytes,
                    OwnerID = db.OwnerID
                };
            }

            Tickets.StartTracking();

            Tickets.SubscribeOnChanged(OnTicketChanged);

            dirtyTicket.OnDirtyChanged += () =>
            {
                SaveCommand.NotifyCanExecuteChanged();
                WeakReferenceMessenger.Default.Send(new NavigationPageBadgeMessage(nameof(TicketPageViewModel), dirtyTicket.Count));
            };
        }

        public Task OnNavigatedFromAsync() => Task.CompletedTask;

        public Task OnNavigatedToAsync() => _initializeTask ??= InitalizeViewModel();

        [RelayCommand]
        private static void DeleteTicket(TicketViewModel ticket)
        {
            ticket.Delete();
        }

        [RelayCommand]
        private async Task AddTicket()
        {
            var inputDialog = new InputDialog("Generate Ticket", true)
            {
                InputTitle = "Make sure Steam is running and you're logged in with the account that owns the game." + Environment.NewLine +
                "The ETicket is only valid for 30 minutes, so make sure to use it before it expires." + Environment.NewLine +
                "If it expires, just generate a new one.",
                PlaceHolderInputText = "Enter AppId",
                CloseButtonText = "Cancel",
                PrimaryButtonText = "Generate"
            };
            var contentDialogResult = await contentDialogService.ShowAsync(inputDialog, default);
            if (contentDialogResult != ContentDialogResult.Primary) return;
            if (!uint.TryParse(inputDialog.InputText, out var appid)) return;
            try
            {
                var progressDialog = new ProgressDialog("Extract Ticket", async (progress) =>
                {
                    progress.Text = "Creating Worker...";
                    var progressReport = new Progress<string>(msg => progress.Text = msg);
                    var bundle = await SteamTicketExtractor.ExtractTicketsAsync(appid, progressReport, progress.Token);
                    if (bundle.AppTicket == null || bundle.ETicket == null) throw new InvalidOperationException("No ticket found");
                    Tickets.NewOrUpdate(new()
                    {
                        AppId = appid,
                        OwnerID = bundle.SteamID,
                        AppTicketBytes = bundle.AppTicket,
                        EncryptedTicketBytes = bundle.ETicket,
                    }, (existing, incoming) => existing.AppId == incoming.AppId);
                });
                await contentDialogService.ShowAsync(progressDialog, default);
            }
            catch (Exception ex)
            {
                loggerService.LogError(ex);
                snackbarService.Show("Error", ex.Message, ControlAppearance.Danger, default, default);
            }
        }

        private bool IsDirty() => dirtyTicket.Count > 0;

        private void OnTicketChanged(ITrackable trackable, ITrackable? trackableParent)
        {
            if (trackable is not TicketViewModel ticket) return;
            var status = ticket.GetStatus();

            ticket.IsVisible = status != TrackingStatus.Deleted;

            if (status == TrackingStatus.Discarded)
            {
                Tickets.Remove(ticket);
                dirtyTicket.Remove(ticket.AppId);
            }
            else if (status == TrackingStatus.Unchanged)
            {
                dirtyTicket.Remove(ticket.AppId);
            }
            else
            {
                dirtyTicket.Add(ticket.AppId);
            }
        }

        [RelayCommand(CanExecute = nameof(IsDirty))]
        private void Save(bool undo = false)
        {
            if (undo)
            {
                foreach (var id in dirtyTicket.ToList())
                {
                    if (Tickets.TryGetValue(id, out var ticket))
                    {
                        if (ticket.Undo())
                        {
                            Tickets.Remove(ticket);
                        }
                    }
                    dirtyTicket.Remove(id);
                }
            }
            else
            {
                databaseService.Database.BeginTransaction();
                try
                {
                    List<TicketViewModel> processed = [];

                    foreach (var id in dirtyTicket.ToList())
                    {
                        if (!Tickets.TryGetValue(id, out var ticket)) continue;

                        if (ticket.IsDeleted())
                        {
                            databaseService.Database.Delete(ticket.ToDb());
                        }
                        else
                        {
                            databaseService.Database.Save(ticket.ToDb());
                        }

                        processed.Add(ticket);
                    }

                    databaseService.Database.Commit();

                    foreach (var ticket in processed)
                    {
                        if (ticket.Apply())
                        {
                            Tickets.Remove(ticket);
                        }
                        dirtyTicket.Remove(ticket.AppId);
                    }
                }
                catch (Exception ex)
                {
                    loggerService.LogError(ex);
                    databaseService.Database.Rollback();
                }
                Tickets.Apply();
            }
        }

        [RelayCommand]
        private async Task SaveTicket(TicketViewModel ticket)
        {
            var folderDialog = new OpenFolderDialog();

            if (folderDialog.ShowDialog() == false) return;

            try
            {
                var destination = Path.Combine(folderDialog.FolderName, $"{ticket.AppId}_");

                var progresDialog = new ProgressDialog("Saving ticket", async (progress) =>
                {
                    progress.IsIndeterminate = true;
                    progress.Text = $"Saving {ticket.AppId} Ticket";

                    if (ticket.AppTicketBytes != null)
                    {
                        await File.WriteAllBytesAsync(destination + "appticket.bin", ticket.AppTicketBytes, progress.Token);
                        string base64 = Convert.ToBase64String(ticket.AppTicketBytes);
                        string hex = Convert.ToHexString(ticket.AppTicketBytes);
                        await File.WriteAllTextAsync(destination + "appticket_base64.txt", base64, progress.Token);
                        await File.WriteAllTextAsync(destination + "appticket_hex.txt", hex, progress.Token);
                    }
                    if (ticket.EncryptedTicketBytes != null)
                    {
                        await File.WriteAllBytesAsync(destination + "eticket.bin", ticket.EncryptedTicketBytes, progress.Token);
                        string base64 = Convert.ToBase64String(ticket.EncryptedTicketBytes);
                        string hex = Convert.ToHexString(ticket.EncryptedTicketBytes);
                        await File.WriteAllTextAsync(destination + "eticket_base64.txt", base64, progress.Token);
                        await File.WriteAllTextAsync(destination + "eticket_hex.txt", hex, progress.Token);
                    }
                });
                await contentDialogService.ShowAsync(progresDialog, default);
                snackbarService.Show("Success", $"ticket extracted to {destination}(format)", ControlAppearance.Success, default, default);
            }
            catch (Exception ex)
            {
                loggerService.LogError(ex);
                snackbarService.Show("Error", ex.Message, ControlAppearance.Danger, default, default);
            }
        }
    }

    public partial class TicketViewModel : ObservableObject, IKeyed<uint>, ITrackable
    {
        public uint AppId { get; set; }

        [ObservableProperty]
        public partial byte[]? AppTicketBytes { get; set; }

        [ObservableProperty]
        public partial byte[]? EncryptedTicketBytes { get; set; }

        [ObservableProperty]
        public partial bool IsVisible { get; set; } = true;

        uint IKeyed<uint>.Key => AppId;

        [ObservableProperty]
        public partial ulong OwnerID { get; set; }

        public TicketDb ToDb()
        {
            return new TicketDb
            {
                AppId = AppId,
                AppTicketBytes = AppTicketBytes,
                EncryptedTicketBytes = EncryptedTicketBytes,
                OwnerID = OwnerID
            };
        }
    }
}