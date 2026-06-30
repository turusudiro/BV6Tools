using System.Reflection;

namespace BV6Tools.Tracking
{
    public partial class TrackingState : ObservableObject
    {

        public TrackingState(ITrackable trackable, ITrackable? parentTrackable)
        {
            Trackable = trackable;
            ParentTrackable = parentTrackable;
        }

        [ObservableProperty]
        public partial ITrackable? ParentTrackable { get; set; }

        [ObservableProperty]
        public partial ITrackable Trackable { get; set; }
        partial void OnTrackableChanged(ITrackable oldValue, ITrackable newValue)
        {
            if (oldValue != null)
            {
                oldValue.PropertyChanged -= Trackable_PropertyChanged;
            }
            if (newValue != null)
            {
                newValue.PropertyChanged += Trackable_PropertyChanged;
            }
        }

        internal bool SuppressNotifications { get; set; }

        private void Trackable_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (SuppressNotifications) return;

            if (ReferenceEquals(sender, Trackable))
            {
                if (Snapshot.Any(x => x.Key.Name == e.PropertyName))
                {
                    foreach (var subscribe in OnChanged)
                    {
                        subscribe(Trackable, ParentTrackable);
                    }
                }
            }
        }
        [ObservableProperty]
        public partial bool Added { get; set; }
        partial void OnAddedChanged(bool value)
        {
            foreach (var subscribe in OnChanged)
            {
                subscribe(Trackable, ParentTrackable);
            }
        }
        [ObservableProperty]
        public partial bool Deleted { get; set; }
        partial void OnDeletedChanged(bool value)
        {
            if (SuppressNotifications) return;

            foreach (var subscribe in OnChanged)
            {
                subscribe(Trackable, ParentTrackable);
            }
        }
        public Dictionary<PropertyInfo, object?> Snapshot { get; set; } = [];
        public HashSet<Action<ITrackable, ITrackable?>> OnChanged { get; set; } = [];
    }
}
