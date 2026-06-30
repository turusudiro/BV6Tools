using System.Collections.ObjectModel;
using System.ComponentModel;

namespace BV6Tools.Collections
{
    public interface IKeyed<TKey>
    {
        TKey Key { get; }
    }

    public class ObservableDictionary<TKey, TValue> : ObservableCollection<TValue>, IDisposable
        where TKey : notnull
        where TValue : class, INotifyPropertyChanged, IKeyed<TKey>
    {
        private readonly Dictionary<TKey, TValue> _lookup = [];
        private bool _disposed;

        public event Action<TValue, string?>? ItemPropertyChanged;

        public TValue this[TKey key]
        {
            get => _lookup[key];
            set
            {
                ArgumentNullException.ThrowIfNull(value);

                if (!EqualityComparer<TKey>.Default.Equals(value.Key, key))
                    throw new ArgumentException("The item's key must match the indexer key.", nameof(value));

                if (_lookup.TryGetValue(key, out var existing))
                {
                    SetItem(IndexOf(existing), value);
                }
                else
                {
                    Add(value);
                }
            }
        }

        public bool ContainsKey(TKey key)
        {
            return _lookup.ContainsKey(key);
        }

        public void Dispose()
        {
            if (_disposed) return;
            ClearItems();
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            return _lookup.TryGetValue(key, out value!);
        }

        protected override void ClearItems()
        {
            foreach (var item in this)
                item.PropertyChanged -= OnItemPropertyChanged;
            _lookup.Clear();
            base.ClearItems();
        }

        protected override void InsertItem(int index, TValue item)
        {
            ArgumentNullException.ThrowIfNull(item);

            if (_lookup.ContainsKey(item.Key))
                throw new ArgumentException($"An item with key '{item.Key}' already exists.", nameof(item));

            _lookup.Add(item.Key, item);
            item.PropertyChanged += OnItemPropertyChanged;
            base.InsertItem(index, item);
        }

        protected override void RemoveItem(int index)
        {
            var item = this[index];
            item.PropertyChanged -= OnItemPropertyChanged;
            _lookup.Remove(item.Key);
            base.RemoveItem(index);
        }

        protected override void SetItem(int index, TValue item)
        {
            ArgumentNullException.ThrowIfNull(item);

            var oldItem = this[index];
            if (_lookup.TryGetValue(item.Key, out var existing) && !ReferenceEquals(existing, oldItem))
                throw new ArgumentException($"An item with key '{item.Key}' already exists.", nameof(item));

            oldItem.PropertyChanged -= OnItemPropertyChanged;
            _lookup.Remove(oldItem.Key);

            _lookup.Add(item.Key, item);
            item.PropertyChanged += OnItemPropertyChanged;
            base.SetItem(index, item);
        }

        private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not TValue item)
                return;

            ReindexItemIfKeyChanged(item);
            ItemPropertyChanged?.Invoke(item, e.PropertyName);
        }

        private void ReindexItemIfKeyChanged(TValue item)
        {
            if (_lookup.TryGetValue(item.Key, out var current) && ReferenceEquals(current, item))
                return;

            var oldKey = default(TKey);
            var foundOldKey = false;

            foreach (var pair in _lookup)
            {
                if (!ReferenceEquals(pair.Value, item))
                    continue;

                oldKey = pair.Key;
                foundOldKey = true;
                break;
            }

            if (!foundOldKey)
                return;

            if (_lookup.ContainsKey(item.Key))
                throw new InvalidOperationException($"Changing the item key to '{item.Key}' would create a duplicate key.");

            _lookup.Remove(oldKey!);
            _lookup.Add(item.Key, item);
        }
    }
}


