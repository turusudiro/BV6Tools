using BV6Tools.Collections;
using System.Collections.Immutable;
using System.ComponentModel;

namespace BV6Tools.ViewModels.Shared
{
    public partial class AppViewModel : ObservableObject, IKeyed<uint>
    {
        private ObservableDictionary<uint, AppViewModel>? _depots;
        private ObservableDictionary<uint, AppViewModel>? dlc;

        [ObservableProperty]
        public partial uint AppId { get; set; }

        [ObservableProperty]
        public partial string? DecryptionKey { get; set; }

        public ObservableDictionary<uint, AppViewModel> Depot => _depots ??= [];

        public ICollectionView? DepotView { get; set; }

        public ObservableDictionary<uint, AppViewModel> DLC => dlc ??= [];

        public ICollectionView? DLCView { get; set; }

        [ObservableProperty]
        public partial bool IsEnabled { get; set; }

        public uint Key => AppId;

        [ObservableProperty]
        public partial ulong? Manifest { get; set; }

        [ObservableProperty]
        public partial bool? ManifestMissing { get; set; }

        [ObservableProperty]
        public partial string? Name { get; set; }

        [ObservableProperty]
        public partial bool? SelectAllDepot { get; set; }

        [ObservableProperty]
        public partial bool? SelectAllDLC { get; set; }

        public bool EqualsSnapshot(Snapshot snapshot, ObservableDictionary<uint, AppViewModel> items)
        {
            if (AppId != snapshot.AppId
                || Name != snapshot.Name
                || IsEnabled != snapshot.IsEnabled)
                return false;

            return KeyedUnorderedEqual(items, snapshot.Items);
        }

        public Snapshot ToSnapshot(ObservableDictionary<uint, AppViewModel> items)
        {
            var itemsSnapshots = items
                .Select(d => new AppSnapshot(d.AppId, d.Name, d.IsEnabled))
                .ToImmutableArray();

            return new Snapshot(AppId, Name, IsEnabled, itemsSnapshots);
        }

        public override string ToString() => Name ?? AppId.ToString();

        private static bool KeyedUnorderedEqual(
            ObservableDictionary<uint, AppViewModel> vmDict,
            ImmutableArray<AppSnapshot> snapshots)
        {
            if (vmDict.Count != snapshots.Length) return false;

            foreach (var snap in snapshots)
            {
                if (!vmDict.TryGetValue(snap.AppId, out var vm)) return false;
                if (!vm.EqualsSnapshot(snap)) return false;
            }

            return true;
        }

        private bool EqualsSnapshot(AppSnapshot snapshot)
        {
            return AppId == snapshot.AppId
                   && Name == snapshot.Name
                   && IsEnabled == snapshot.IsEnabled;
        }

        public readonly record struct AppSnapshot(uint AppId, string? Name, bool IsEnabled);

        public readonly record struct Snapshot(uint AppId, string? Name, bool IsEnabled, ImmutableArray<AppSnapshot> Items);
    }
}