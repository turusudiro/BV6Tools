namespace BV6Tools.Collections
{
    public class HashSetNotify<T> : HashSet<T>
    {
        public HashSetNotify() : base() { }

        public HashSetNotify(IEqualityComparer<T>? comparer) : base(comparer) { }

        public HashSetNotify(IEnumerable<T> collection) : base(collection) { }

        public event Action? OnDirtyChanged;
        public new bool Add(T item)
        {
            if (base.Add(item))
            {
                OnDirtyChanged?.Invoke();
                return true;
            }
            return false;
        }

        public new void Clear()
        {
            if (Count == 0) return;
            base.Clear();
            OnDirtyChanged?.Invoke();
        }

        public new bool Remove(T item)
        {
            if (base.Remove(item))
            {
                OnDirtyChanged?.Invoke();
                return true;
            }
            return false;
        }
    }
}
