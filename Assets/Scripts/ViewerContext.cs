// Shared execution context and view interface definition connecting views to the simulation state and clock.
// Manages global selection and view modes across view components.

using System;

namespace SwarmViewer
{
    /// <summary>Everything a view could need, handed to it once.</summary>
    public sealed class ViewerContext
    {
        public RunData Run;
        public RunState State;
        public PlaybackClock Clock;
        public SelectionModel Selection;
    }

    /// <summary>
    /// THE extension point. Implement this on a MonoBehaviour, drop it on a child
    /// of the visualiser root, and it gets found and bound automatically. Every
    /// future layer -- links, trails, timeline, log panel, belief overlay -- is one
    /// of these and nothing existing has to change to add one.
    /// </summary>
    public interface IRunView
    {
        void Bind(ViewerContext ctx);
    }

    /// <summary>
    /// What is selected and whose eyes we look through. Separate from RunState
    /// because it changes on click rather than on time, so views that care about
    /// one are not woken by the other.
    /// </summary>
    public sealed class SelectionModel
    {
        int _selected = -1;
        int _observer = -1;
        ViewMode _mode = ViewMode.GroundTruth;

        public event Action<int> OnSelectionChanged;
        public event Action<int> OnObserverChanged;
        public event Action<ViewMode> OnViewModeChanged;

        /// <summary>Slot index, or -1 for nothing.</summary>
        public int SelectedSlot
        {
            get => _selected;
            set { if (_selected != value) { _selected = value; OnSelectionChanged?.Invoke(value); } }
        }

        /// <summary>Drone id whose beliefs a belief view renders, or -1.</summary>
        public int Observer
        {
            get => _observer;
            set { if (_observer != value) { _observer = value; OnObserverChanged?.Invoke(value); } }
        }

        public ViewMode Mode
        {
            get => _mode;
            set { if (_mode != value) { _mode = value; OnViewModeChanged?.Invoke(value); } }
        }

        public void Clear() => SelectedSlot = -1;
    }

    /// <summary>The two views worth having; the gap between them is the tier-5 problem.</summary>
    public enum ViewMode { GroundTruth, FleetBelief }
}
