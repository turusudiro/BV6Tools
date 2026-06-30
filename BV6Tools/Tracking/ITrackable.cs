using System.ComponentModel;

namespace BV6Tools.Tracking
{
    public interface ITrackable : INotifyPropertyChanged { }

    /// <summary>
    /// Marks a trackable type as a pure container: an object that only exists to
    /// group other trackables together, with no real identity of its own.
    ///
    /// Normally, when the tracker walks down the object graph, each trackable becomes
    /// the "parent" passed to its own children's change callbacks. A container is
    /// skipped for that purpose — its children are told the container's own parent,
    /// not the container.
    /// </summary>
    public interface IContainerTrackable : ITrackable { }
}
