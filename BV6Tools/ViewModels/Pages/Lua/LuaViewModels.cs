using BV6Tools.Collections;
using BV6Tools.Services.Database.Models;
using BV6Tools.Tracking;
using STCommon;
using System.ComponentModel;
using System.Windows.Data;

namespace BV6Tools.ViewModels.Pages.Lua
{
    public static class LuaViewModelExtensions
    {
        public static LuaData ToLuaData(this LuaViewModel viewModel)
        {
            var appids = viewModel.AddAppId
                .Where(vm => vm.IsEnabled)
                .ToDictionary(
                    vm => vm.AppId.ToString(),
                    vm => (LuaAppIdWithKey?)new LuaAppIdWithKey(
                        Flag: ((int)vm.AppType).ToString(),
                        DecryptionKey: vm.Key));

            var manifest = viewModel.SetManifestID
                .Where(vm => vm.IsEnabled)
                .ToDictionary(
                    vm => vm.AppId.ToString(),
                    vm => new ManifestData(
                        ManifestID: vm.GID,
                        Size: vm.Size));

            var tokenData = viewModel.AddToken
                .Where(vm => vm.IsEnabled)
                .ToDictionary(
                    vm => vm.AppId.ToString(),
                    vm => vm.Token ?? string.Empty);

            return new LuaData(appids, manifest, tokenData);
        }
    }

    public partial class AddAppIdViewModel : LuaAppViewModel
    {
        [ObservableProperty]
        public partial AddAppIdType AppType { get; set; }

        [ObservableProperty]
        public partial string Key { get; set; } = string.Empty;
    }

    public partial class LuaAppViewModel : ObservableObject, IContainerTrackable, IKeyed<uint>
    {
        [ObservableProperty]
        public partial uint AppId { get; set; }

        [ObservableProperty]
        public partial bool IsEnabled { get; set; }

        [ObservableProperty]
        [IgnoreTrack]
        public partial bool IsVisible { get; set; } = true;

        uint IKeyed<uint>.Key => AppId;

        public override string ToString() => AppId.ToString();
    }

    public partial class LuaTokenViewModel : LuaAppViewModel
    {
        [ObservableProperty]
        public partial string? Token { get; set; }
    }

    public partial class LuaViewModel : ObservableObject, IKeyed<uint>, ITrackable
    {
        public LuaViewModel()
        {
            AddAppIdView = CollectionViewSource.GetDefaultView(AddAppId);
            AddAppIdView.SortDescriptions.Add(new SortDescription(nameof(LuaAppViewModel.AppId), ListSortDirection.Ascending));
            SetManifestIDView = CollectionViewSource.GetDefaultView(SetManifestID);
            SetManifestIDView.SortDescriptions.Add(new SortDescription(nameof(LuaAppViewModel.AppId), ListSortDirection.Ascending));
            AddTokenView = CollectionViewSource.GetDefaultView(AddToken);
            AddTokenView.SortDescriptions.Add(new SortDescription(nameof(LuaAppViewModel.AppId), ListSortDirection.Ascending));
        }

        public ObservableDictionary<uint, AddAppIdViewModel> AddAppId { get; set; } = [];
        public ICollectionView AddAppIdView { get; }
        public ObservableDictionary<uint, LuaTokenViewModel> AddToken { get; set; } = [];
        public ICollectionView AddTokenView { get; }
        public uint AppId { get; set; }
        [ObservableProperty]
        public partial bool IsEnabled { get; set; }

        [ObservableProperty]
        [IgnoreTrack]
        public partial bool IsVisible { get; set; } = true;

        uint IKeyed<uint>.Key => AppId;

        [ObservableProperty]
        [IgnoreTrack]
        public partial string? Name { get; set; }

        public ObservableDictionary<uint, SetManifestIDViewModel> SetManifestID { get; set; } = [];
        public ICollectionView SetManifestIDView { get; }

        public override string ToString() => Name ?? AppId.ToString();
    }

    public partial class SetManifestIDViewModel : LuaAppViewModel
    {
        [ObservableProperty]
        public partial string? GID { get; set; }

        [ObservableProperty]
        public partial string? Size { get; set; }
    }
}