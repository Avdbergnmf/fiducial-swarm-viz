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
    /// <see cref="Back"/> / <see cref="Forward"/> walk prior sets (empty included)
    /// so a misclick or Esc is recoverable; a new click drops the forward stack.
    /// </summary>
    public sealed class SelectionModel
    {
        readonly List<int> _slots = new();
        readonly HashSet<int> _set = new();
        readonly List<int[]> _past = new();
        readonly List<int[]> _future = new();

        const int MaxHistory = 48;
        bool _applyingHistory;

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

        public bool CanBack => _past.Count > 0;
        public bool CanForward => _future.Count > 0;

        /// <summary>Restore the previous selection set (including empty). No-op at the start.</summary>
        public void Back()
        {
            if (_past.Count == 0) return;
            _future.Add(Snapshot());
            var prev = _past[_past.Count - 1];
            _past.RemoveAt(_past.Count - 1);
            ApplySnapshot(prev);
        }

        /// <summary>Undo <see cref="Back"/>. Cleared by any new click.</summary>
        public void Forward()
        {
            if (_future.Count == 0) return;
            _past.Add(Snapshot());
            var next = _future[_future.Count - 1];
            _future.RemoveAt(_future.Count - 1);
            ApplySnapshot(next);
        }

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

        public bool BeliefViewOn => _mode == ViewMode.FleetBelief;

        public bool IsBeliefViewOf(int droneId) =>
            _mode == ViewMode.FleetBelief && _observer == droneId && droneId >= 0;

        /// <summary>Paint the scene as <paramref name="observerDrone"/> sees it.
        /// Pass -1 to keep the current observer.</summary>
        public void SetBeliefView(bool on, int observerDrone = -1)
        {
            if (!on)
            {
                Mode = ViewMode.GroundTruth;
                return;
            }
            if (observerDrone >= 0)
                Observer = observerDrone;
            Mode = ViewMode.FleetBelief;
        }

        public void SelectOnly(int slot)
        {
            if (_slots.Count == 1 && _slots[0] == slot) return;
            RecordBeforeChange();
            _slots.Clear();
            _set.Clear();
            _slots.Add(slot);
            _set.Add(slot);
            Fire();
        }

        public void Add(int slot)
        {
            if (_set.Contains(slot)) return;
            RecordBeforeChange();
            _set.Add(slot);
            _slots.Add(slot);
            Fire();
        }

        public void Remove(int slot)
        {
            if (!_set.Contains(slot)) return;
            RecordBeforeChange();
            _set.Remove(slot);
            _slots.Remove(slot);
            Fire();
        }

        public void Toggle(int slot)
        {
            if (IsSelected(slot)) Remove(slot);
            else Add(slot);
        }

        /// <summary>Replace the set. Primary becomes the first slot. Empty clears.</summary>
        public void Replace(IReadOnlyList<int> slots)
        {
            if (slots == null || slots.Count == 0)
            {
                Clear();
                return;
            }

            if (slots.Count == _slots.Count)
            {
                bool same = true;
                for (int i = 0; i < slots.Count; i++)
                {
                    if (_slots[i] != slots[i])
                    {
                        same = false;
                        break;
                    }
                }
                if (same) return;
            }

            RecordBeforeChange();
            _slots.Clear();
            _set.Clear();
            for (int i = 0; i < slots.Count; i++)
            {
                int slot = slots[i];
                if (_set.Add(slot))
                    _slots.Add(slot);
            }
            Fire();
        }

        public void Clear()
        {
            if (_slots.Count == 0) return;
            RecordBeforeChange();
            _slots.Clear();
            _set.Clear();
            Fire();
        }

        void RecordBeforeChange()
        {
            if (_applyingHistory) return;
            _past.Add(Snapshot());
            while (_past.Count > MaxHistory)
                _past.RemoveAt(0);
            _future.Clear();
        }

        int[] Snapshot()
        {
            if (_slots.Count == 0) return Array.Empty<int>();
            return _slots.ToArray();
        }

        void ApplySnapshot(int[] snap)
        {
            _applyingHistory = true;
            try
            {
                if (snap == null || snap.Length == 0) Clear();
                else Replace(snap);
            }
            finally
            {
                _applyingHistory = false;
            }
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
