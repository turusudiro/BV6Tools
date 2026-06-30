using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace BV6Tools.Tracking
{
    public enum TrackingStatus
    {
        /// <summary>No add/delete/modify state — nothing to do.</summary>
        Unchanged,

        /// <summary>Newly added and not yet deleted — should be visible and tracked as dirty.</summary>
        Added,

        /// <summary>Was added this session and then deleted again before saving — discard entirely.</summary>
        Discarded,

        /// <summary>Pre-existing item flagged deleted — keep it (for Undo), just hide it.</summary>
        Deleted,

        /// <summary>Pre-existing item with a property changed — keep visible, mark dirty.</summary>
        Modified
    }

    public static class Tracking
    {
        private static readonly ConditionalWeakTable<object, CollectionTrackingInfo> collectionActions = [];

        private static readonly ConditionalWeakTable<ITrackable, TrackingState> trackedObjects = [];

        public static void Apply(this IEnumerable<ITrackable> source)
        {
            foreach (var item in source.ToList())
            {
                item.Apply();
            }
        }

        /// <returns>True if the item itself was deleted and committed - the caller is
        /// responsible for removing it from wherever it's stored. False if it's still alive.</returns>
        public static bool Apply(this ITrackable source)
        {
            var tracker = GetTracker(source);

            if (tracker.Deleted)
            {
                StopTracking(source);
                return true;
            }

            ApplyRecursive(source, []);
            return false;
        }

        public static void Delete(this ITrackable source)
        {
            TrackingState tracker = GetTracker(source);
            tracker.Deleted = true;
        }

        public static TrackingStatus GetStatus(this ITrackable source)
        {
            var tracker = GetTracker(source);

            if (tracker.Added && tracker.Deleted)
                return TrackingStatus.Discarded;
            if (tracker.Added)
                return TrackingStatus.Added;
            if (tracker.Deleted)
                return TrackingStatus.Deleted;

            return source.HasChanges() ? TrackingStatus.Modified : TrackingStatus.Unchanged;
        }

        public static bool HasChanges(this ITrackable source)
        {
            return HasChangesRecursive(source, []);
        }

        public static bool IsAdded(this ITrackable source)
        {
            if (source == null) return false;
            return GetTracker(source).Added;
        }

        public static bool IsDeleted(this ITrackable source)
        {
            if (source == null)
                return false;

            TrackingState tracker = GetTracker(source);
            return IsDeleted(source, tracker, null);
        }

        public static bool IsDeleted(this ITrackable source, out IEnumerable<ITrackable> deletedItems)
        {
            deletedItems = [];
            if (source == null)
                return false;

            TrackingState tracker = GetTracker(source);

            return IsDeleted(source, tracker, (List<ITrackable>)deletedItems);
        }

        public static bool IsTracking(this IEnumerable<ITrackable> source)
        {
            return source.All(item => item.IsTracking());
        }

        public static bool IsTracking(this ITrackable source)
        {
            if (source == null) return false;
            return trackedObjects.TryGetValue(source, out _);
        }

        public static TrackingState New(this ITrackable source, ITrackable parent)
        {
            TrackingState parentTracker = GetTracker(parent);

            // If parent is a container, flatten through it just like SubscribeOnChanged does.
            ITrackable effectiveParent = parent is IContainerTrackable
                ? parentTracker.ParentTrackable!
                : parent;

            TrackingSnapshot cache = new();
            TrackingState tracker = StartTracking(source, cache, effectiveParent);

            foreach (var action in parentTracker.OnChanged)
                SubscribeOnChanged(source, action, cache, effectiveParent);

            tracker.Added = true; // fires now that it's subscribed
            return tracker;
        }

        // For root-level items with no trackable parent (e.g. a new LuaViewModel going into Games).
        public static TrackingState New(this ITrackable source, Action<ITrackable, ITrackable?> action, ITrackable? parent = null)
        {
            TrackingSnapshot cache = new();
            TrackingState tracker = StartTracking(source, cache, parent);
            SubscribeOnChanged(source, action, cache, parent);
            tracker.Added = true;
            return tracker;
        }

        public static TrackingState New<TValue>(this ICollection<TValue> collection, TValue item)
            where TValue : ITrackable
        {
            TrackingSnapshot cache = new();

            collectionActions.TryGetValue(collection, out var info);
            ITrackable? parent = info?.Parent;

            TrackingState tracker = StartTracking(item, cache, parent);

            if (info != null)
            {
                foreach (var action in info.Actions)
                    SubscribeOnChanged(item, action, cache, parent);
            }

            collection.Add(item);
            tracker.Added = true;

            return tracker;
        }

        public static TrackingState NewOrUpdate<TValue>(this ICollection<TValue> collection, TValue item, Func<TValue, TValue, bool> match)
            where TValue : class, ITrackable
        {
            var existing = collection.FirstOrDefault(existingItem => match(existingItem, item));
            if (existing is null)
                return collection.New(item);

            TrackingState tracker = existing.IsTracking()
                ? GetTracker(existing)
                : StartTracking(existing);

            CopyTrackableValues(existing, item);
            tracker.Deleted = false;

            return tracker;
        }

        public static void StartTracking(this IEnumerable<ITrackable> source)
        {
            TrackingSnapshot propertyCache = new();
            foreach (ITrackable item in source)
            {
                StartTracking(item, propertyCache);
            }
        }

        public static TrackingState StartTracking(this ITrackable source)
        {
            TrackingSnapshot cache = new();
            return StartTracking(source, cache);
        }

        public static void StopTracking(this IEnumerable<ITrackable> source)
        {
            foreach (ITrackable item in source)
            {
                item.StopTracking();
            }
        }

        public static void StopTracking(this ITrackable source)
        {
            trackedObjects?.Remove(source);
        }

        public static IDisposable SubscribeOnChanged(this IEnumerable<ITrackable> source, Action<ITrackable, ITrackable?> action)
        {
            TrackingSnapshot snap = new();
            foreach (ITrackable item in source)
                SubscribeOnChanged(item, action, snap);

            RegisterCollection(source, action, null);
            return new ChangeSubscription(source, action);
        }

        public static void Undo(this IEnumerable<ITrackable> source)
        {
            foreach (var item in source.ToList())
            {
                item.Undo();
            }
        }

        public static bool Undo(this ITrackable source)
        {
            var tracker = GetTracker(source);

            if (tracker.Added)
            {
                StopTracking(source);
                return true; // it was never supposed to exist - caller removes it from wherever it's stored
            }

            UndoRecursive(source, []);
            return false;
        }

        public static void UnsubscribeOnChanged(this IEnumerable<ITrackable> source, Action<ITrackable, ITrackable?> action)
        {
            HashSet<ITrackable> visited = [];
            foreach (ITrackable item in source.ToList())
                UnsubscribeOnChanged(item, action, visited);

            if (collectionActions.TryGetValue(source, out var info))
                info.Actions.Remove(action);
        }

        internal static void SubscribeOnChanged(ITrackable item, Action<ITrackable, ITrackable?> action, TrackingSnapshot cache, ITrackable? parent = null)
        {
            TrackingState tracker = GetTracker(item);

            tracker.ParentTrackable ??= parent;

            tracker.OnChanged.Add(action);

            // containers don't introduce a new level in the parent chain.
            ITrackable? parentForChildren = item is IContainerTrackable ? tracker.ParentTrackable : item;

            if (!cache.PropertiesByType.TryGetValue(item.GetType(), out PropertyInfo[]? properties))
            {
                properties = item.GetType().GetProperties();
                cache.PropertiesByType[item.GetType()] = properties;
            }

            foreach (var property in properties)
            {
                if (property.PropertyType != typeof(TrackingState) && IsPropertyAllowed(property, cache))
                {
                    var value = property.GetValue(item);
                    if (property.PropertyType.IsGenericType && property.PropertyType.IsAssignableTo(typeof(IEnumerable)))
                    {
                        if (value is not IEnumerable list) continue;

                        RegisterCollection(list, action, parentForChildren);

                        foreach (object subItem in list)
                        {
                            if (subItem is ITrackable trackable)
                            {
                                SubscribeOnChanged(trackable, action, cache, parentForChildren);
                            }
                        }
                    }
                    else if (property.PropertyType.IsAssignableTo(typeof(ITrackable)))
                    {
                        if (value is ITrackable trackable)
                        {
                            SubscribeOnChanged(trackable, action, cache, parentForChildren);
                        }
                    }
                }
            }
        }

        private static void ApplyRecursive(ITrackable source, HashSet<ITrackable> visited)
        {
            if (!visited.Add(source)) return; // Prevent infinite loops in circular graphs

            var tracker = GetTracker(source);
            bool changed = tracker.Added;

            tracker.SuppressNotifications = true;

            try
            {
                tracker.Added = false;

                foreach (var property in tracker.Snapshot.Keys.ToList())
                {
                    var currentValue = property.GetValue(source);

                    if (!Equals(currentValue, tracker.Snapshot[property]))
                    {
                        changed = true;
                        tracker.Snapshot[property] = currentValue; // accept it as the new baseline
                    }

                    if (property.PropertyType.IsAssignableTo(typeof(ITrackable)))
                    {
                        if (currentValue is ITrackable child)
                        {
                            if (child.IsTracking())
                                ApplyRecursive(child, visited);
                            else
                                child.StartTracking(); // never tracked - baseline it fresh
                        }
                    }
                    else if (property.PropertyType.IsGenericType && property.PropertyType.IsAssignableTo(typeof(IEnumerable)))
                    {
                        if (currentValue is IEnumerable list)
                        {
                            List<ITrackable> committedDeletes = [];
                            foreach (object item in list)
                            {
                                if (item is not ITrackable child) continue;

                                if (!child.IsTracking())
                                {
                                    changed = true;
                                    child.StartTracking(); // added this session - it's the baseline now
                                }
                                else if (child.IsDeleted())
                                {
                                    changed = true;
                                    committedDeletes.Add(child); // pending delete becomes final
                                    StopTracking(child);
                                }
                                else
                                {
                                    ApplyRecursive(child, visited);
                                }
                            }

                            if (list is IList editableList)
                            {
                                foreach (var removed in committedDeletes)
                                    editableList.Remove(removed);
                            }
                        }
                    }
                }
            }
            finally
            {
                tracker.SuppressNotifications = false;
            }

            if (changed)
            {
                foreach (var subscribe in tracker.OnChanged)
                    subscribe(source, tracker.ParentTrackable);
            }
        }

        private static void CopyTrackableValues<TValue>(TValue target, TValue source)
            where TValue : ITrackable
        {
            foreach (var property in source.GetType().GetProperties())
            {
                if (!property.CanRead || !property.CanWrite)
                    continue;

                if (property.GetIndexParameters().Length > 0)
                    continue;

                if (property.PropertyType == typeof(TrackingState) || !IsPropertyAllowed(property))
                    continue;

                if (IsTrackableCollection(property.PropertyType))
                    continue;

                if (property.PropertyType.IsAssignableTo(typeof(ITrackable)))
                    continue;

                property.SetValue(target, property.GetValue(source));
            }
        }

        private static TrackingState GetTracker(ITrackable source)
        {
            if (!trackedObjects.TryGetValue(source, out var tracker))
                throw new InvalidOperationException("Unable to detect changes because object is not being tracked. Please call StartTracking() first.");
            return tracker;
        }
        private static bool IsTrackableCollection(Type type)
        {
            if (type == typeof(string)) return false;
            if (!type.IsAssignableTo(typeof(IEnumerable))) return false;

            Type? elementType = type.IsArray
                ? type.GetElementType()
                : type.GetInterfaces()
                      .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                      .Select(i => i.GetGenericArguments()[0])
                      .FirstOrDefault();

            // Only skip if elements are themselves trackable - byte[], int[], etc. should copy normally
            return elementType != null && elementType.IsAssignableTo(typeof(ITrackable));
        }
        private static bool HasChangesRecursive(ITrackable source, HashSet<ITrackable> visited)
        {
            if (!visited.Add(source)) return false; // Prevent infinite loops in circular graphs

            var tracker = GetTracker(source);

            if (tracker.Deleted || tracker.Added)
            {
                return true;
            }

            foreach (var entry in tracker.Snapshot)
            {
                var currentValue = entry.Key.GetValue(source);

                // Compare live value vs snapshot for THIS object - previously this only ran for the root.
                if (!Equals(currentValue, entry.Value))
                    return true;

                // Then recurse to catch changes further down the graph.
                if (entry.Key.PropertyType.IsAssignableTo(typeof(ITrackable)))
                {
                    if (currentValue is ITrackable child && HasChangesRecursive(child, visited))
                        return true;
                }
                else if (entry.Key.PropertyType.IsGenericType && entry.Key.PropertyType.IsAssignableTo(typeof(IEnumerable)))
                {
                    if (currentValue is IEnumerable list)
                    {
                        foreach (var item in list)
                        {
                            if (item is ITrackable child && HasChangesRecursive(child, visited))
                                return true;
                        }
                    }
                }
            }

            return false;
        }

        private static bool IsDeleted(ITrackable source, TrackingState tracker, List<ITrackable>? deletedItems)
        {
            //If the parent object is deleted then no need to check children.
            if (tracker.Deleted)
            {
                deletedItems?.Add(tracker.Trackable);
                return true;
            }

            bool hasDeletes = false;
            foreach (var property in tracker.Snapshot)
            {
                if (property.Key.PropertyType.IsGenericType && property.Key.PropertyType.IsAssignableTo(typeof(IEnumerable)))
                {
                    if (property.Value is IEnumerable list)
                    {
                        foreach (object item in list)
                        {
                            if (item is ITrackable trackedItem)
                            {
                                if (IsDeleted(trackedItem, GetTracker(trackedItem), deletedItems))
                                {
                                    if (deletedItems == null)
                                        return true;
                                    hasDeletes = true;
                                }
                            }
                        }
                    }
                }
            }
            return hasDeletes;
        }

        private static bool IsPropertyAllowed(PropertyInfo property)
        {
            var trackingAttribute = property.GetCustomAttribute<IgnoreTrackAttribute>();
            if (trackingAttribute != null)
            {
                return false;
            }

            return property.GetCustomAttribute<IgnoreTrackAttribute>() == null;
        }

        private static bool IsPropertyAllowed(PropertyInfo property, TrackingSnapshot cache)
        {
            if (cache.AllowedTrackByProperty.TryGetValue(property, out bool result))
            {
                return result;
            }

            result = IsPropertyAllowed(property);
            cache.AllowedTrackByProperty[property] = result;
            return result;
        }

        private static void RegisterCollection(object collection, Action<ITrackable, ITrackable?> action, ITrackable? parent)
        {
            var info = collectionActions.GetOrCreateValue(collection);
            info.Parent = parent;
            if (!info.Actions.Contains(action))
                info.Actions.Add(action);
        }

        private static TrackingState StartTracking(ITrackable source, TrackingSnapshot cache, ITrackable? parent = null)
        {
            TrackingState tracker = new(source, parent);

            PropertyInfo[]? properties;
            if (!cache.PropertiesByType.TryGetValue(source.GetType(), out properties))
            {
                properties = source.GetType().GetProperties();
                cache.PropertiesByType[source.GetType()] = properties;
            }

            foreach (var property in properties)
            {
                if (property.PropertyType != typeof(TrackingState) && IsPropertyAllowed(property, cache))
                {
                    var value = property.GetValue(source);

                    if (property.PropertyType.IsGenericType && property.PropertyType.IsAssignableTo(typeof(IEnumerable)))
                    {
                        if (value is IEnumerable list)
                        {
                            List<ITrackable> deletedItems = [];
                            foreach (object item in list)
                            {
                                if (item is ITrackable trackedItem)
                                {
                                    if (trackedItem.IsTracking() && trackedItem.IsDeleted())
                                    {
                                        deletedItems.Add(trackedItem);
                                        StopTracking(trackedItem);
                                    }
                                    else
                                    {
                                        StartTracking(trackedItem, cache);
                                    }
                                }
                            }

                            //Remove any items we detected as having been deleted.
                            if (list is IList editableList)
                            {
                                foreach (var removeItem in deletedItems)
                                    editableList.Remove(removeItem);
                            }
                        }
                    }
                    else if (property.PropertyType.IsAssignableTo(typeof(ITrackable)))
                    {
                        if (value is ITrackable trackedItem)
                        {
                            StartTracking(trackedItem, cache);
                        }
                    }

                    tracker.Snapshot.Add(property, value);
                }
            }

            trackedObjects.AddOrUpdate(source, tracker);
            return tracker;
        }

        private static void UndoRecursive(ITrackable source, HashSet<ITrackable> visited)
        {
            if (!visited.Add(source)) return; // Prevent infinite loops in circular graphs

            var tracker = GetTracker(source);
            bool changed = tracker.Deleted;

            tracker.SuppressNotifications = true;
            try
            {
                tracker.Deleted = false;

                foreach (var entry in tracker.Snapshot)
                {
                    if (entry.Key.PropertyType.IsGenericType && entry.Key.PropertyType.IsAssignableTo(typeof(IEnumerable)))
                    {
                        if (entry.Key.GetValue(source) is IEnumerable list)
                        {
                            List<ITrackable> addedItems = [];
                            foreach (object item in list)
                            {
                                if (item is not ITrackable child) continue;

                                if (!child.IsTracking() || GetTracker(child).Added)
                                    addedItems.Add(child);
                                else
                                    UndoRecursive(child, visited);
                            }

                            if (addedItems.Count > 0)
                            {
                                changed = true;
                                if (list is IList editableList)
                                {
                                    foreach (var added in addedItems)
                                        editableList.Remove(added);
                                }
                            }
                        }
                    }
                    else
                    {
                        var currentValue = entry.Key.GetValue(source);
                        if (!Equals(currentValue, entry.Value))
                        {
                            changed = true;
                            if (entry.Key.CanWrite)
                                entry.Key.SetValue(source, entry.Value);
                        }

                        if (entry.Value is ITrackable child && child.IsTracking())
                            UndoRecursive(child, visited);
                    }
                }
            }
            finally
            {
                tracker.SuppressNotifications = false;
            }

            if (changed)
            {
                foreach (var subscribe in tracker.OnChanged)
                    subscribe(source, tracker.ParentTrackable);
            }
        }

        private static void UnsubscribeOnChanged(ITrackable item, Action<ITrackable, ITrackable?> action, HashSet<ITrackable> visited)
        {
            if (!visited.Add(item)) return;
            if (!trackedObjects.TryGetValue(item, out var tracker)) return; // already untracked, nothing to clean up

            tracker.OnChanged.Remove(action);

            foreach (var property in item.GetType().GetProperties())
            {
                if (property.PropertyType == typeof(TrackingState) || !IsPropertyAllowed(property)) continue;

                var value = property.GetValue(item);
                if (property.PropertyType.IsGenericType && property.PropertyType.IsAssignableTo(typeof(IEnumerable)))
                {
                    if (value is IEnumerable list)
                    {
                        if (collectionActions.TryGetValue(list, out var info))
                            info.Actions.Remove(action);

                        foreach (object subItem in list)
                        {
                            if (subItem is ITrackable trackable)
                                UnsubscribeOnChanged(trackable, action, visited);
                        }
                    }
                }
                else if (property.PropertyType.IsAssignableTo(typeof(ITrackable)))
                {
                    if (value is ITrackable trackable)
                        UnsubscribeOnChanged(trackable, action, visited);
                }
            }
        }

        private sealed class ChangeSubscription(IEnumerable<ITrackable> source, Action<ITrackable, ITrackable?> action) : IDisposable
        {
            private bool disposed;

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                source.UnsubscribeOnChanged(action);
            }
        }

        private class CollectionTrackingInfo
        {
            public List<Action<ITrackable, ITrackable?>> Actions { get; } = [];
            public ITrackable? Parent { get; set; }
        }
    }

    internal class TrackingSnapshot
    {
        public Dictionary<PropertyInfo, bool> AllowedTrackByProperty { get; set; } = [];
        public Dictionary<Type, PropertyInfo[]> PropertiesByType { get; set; } = [];
    }
}