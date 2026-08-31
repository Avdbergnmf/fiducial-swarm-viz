// Shared execution context and view interface definition connecting views to the simulation state and clock.
// Manages global selection and view modes across view components.

using System;
using System.Collections.Generic;

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
        void Bind(ViewerContext ctx); // called once when the view is created and bound to a run (updates the context reference)
    }

    /// <summary>
    /// What is selected and whose eyes we look through. Separate from RunState
    /// because it changes on click rather than on time, so views that care about
    /// one are not woken by the other.
    ///
    /// The set can hold many slots. <see cref="Primary"/> / <see cref="SelectedSlot"/>
    /// is the first entity selected and stays single-valued for the belief Observer.
    /// </summary>
    public sealed class SelectionModel
    {
        readonly List<int> _slots = new();
        readonly HashSet<int> _set = new();

        int _observer = -1;
        ViewMode _mode = ViewMode.GroundTruth;

        /// <summary>Fires with <see cref="Primary"/> (-1 if the set is empty). Always
        /// fires when the set changes, even if the primary slot is unchanged (shift-add).</summary>
        public event Action<int> OnSelectionChanged;
        public event Action OnSelectionSetChanged;
        public event Action<int> OnObserverChanged;
        public event Action<ViewMode> OnViewModeChanged;

        /// <summary>First-selected slot, or -1. Belief Observer is derived from this.</summary>
        public int Primary => _slots.Count > 0 ? _slots[0] : -1;

        /// <summary>Alias for <see cref="Primary"/>. Assigning replaces the set
        /// (negative clears). Kept so existing subscribers keep compiling.</summary>
        public int SelectedSlot
        {
            get => Primary;
            set
            {
                if (value < 0) Clear();
                else SelectOnly(value);
            }
        }

        public int Count => _slots.Count;
        public IReadOnlyList<int> Slots => _slots;
        public bool IsSelected(int slot) => _set.Contains(slot);

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

        public void SelectOnly(int slot)
        {
            if (_slots.Count == 1 && _slots[0] == slot) return;
            _slots.Clear();
            _set.Clear();
            _slots.Add(slot);
            _set.Add(slot);
            Fire();
        }

        public void Add(int slot)
        {
            if (!_set.Add(slot)) return;
            _slots.Add(slot);
            Fire();
        }

        public void Remove(int slot)
        {
            if (!_set.Remove(slot)) return;
            _slots.Remove(slot);
            Fire();
        }

        public void Toggle(int slot)
        {
            if (IsSelected(slot)) Remove(slot);
            else Add(slot);
        }

        public void Clear()
        {
            if (_slots.Count == 0) return;
            _slots.Clear();
            _set.Clear();
            Fire();
        }

        void Fire()
        {
            OnSelectionChanged?.Invoke(Primary);
            OnSelectionSetChanged?.Invoke();
        }
    }

    /// <summary>The two views worth having; the gap between them is the tier-5 problem.</summary>
    public enum ViewMode { GroundTruth, FleetBelief }
}
