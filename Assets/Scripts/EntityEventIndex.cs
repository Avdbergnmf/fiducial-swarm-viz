// Groups meta.events[] and meta.logs[] by entity/drone so a slot lookup is O(1)
// and does not walk the scene. Built once at load; EntityEvents is just the inspector face.

using System;
using System.Collections.Generic;

namespace SwarmViewer
{
    /// <summary>
    /// One event as it applies to a single slot. <see cref="otherSlots"/> is
    /// everyone else on the same event (collisions, etc.), not including this slot.
    /// </summary>
    [Serializable]
    public sealed class EntityEventRecord
    {
        public float time;
        public int frame;
        public string kind;
        public int severity;
        public string text;
        public int[] otherSlots;
    }

    /// <summary>
    /// Slot -> events that mention that slot, sorted by time. Not a MonoBehaviour
    /// so timeline navigation can query it without finding GameObjects.
    /// </summary>
    public sealed class EntityEventIndex
    {
        readonly List<EntityEventRecord>[] _bySlot;

        public EntityEventIndex(IReadOnlyList<EventInfo> events, int slotCount)
        {
            _bySlot = new List<EntityEventRecord>[Math.Max(0, slotCount)];
            for (int i = 0; i < _bySlot.Length; i++)
                _bySlot[i] = new List<EntityEventRecord>();

            if (events == null) return;
            foreach (var e in events)
            {
                if (e?.slots == null || e.slots.Length == 0) continue;
                for (int i = 0; i < e.slots.Length; i++)
                {
                    int slot = e.slots[i];
                    if ((uint)slot >= (uint)_bySlot.Length) continue;
                    _bySlot[slot].Add(new EntityEventRecord
                    {
                        time = e.t,
                        frame = e.frame,
                        kind = e.kind,
                        severity = e.severity,
                        text = e.text,
                        otherSlots = Others(e.slots, slot),
                    });
                }
            }

            for (int i = 0; i < _bySlot.Length; i++)
                _bySlot[i].Sort((a, b) => a.time.CompareTo(b.time));
        }

        public IReadOnlyList<EntityEventRecord> ForSlot(int slot)
        {
            if ((uint)slot >= (uint)_bySlot.Length) return Array.Empty<EntityEventRecord>();
            return _bySlot[slot];
        }

        static int[] Others(int[] slots, int self)
        {
            int n = 0;
            for (int i = 0; i < slots.Length; i++)
                if (slots[i] != self) n++;
            if (n == 0) return Array.Empty<int>();
            var rest = new int[n];
            int w = 0;
            for (int i = 0; i < slots.Length; i++)
                if (slots[i] != self) rest[w++] = slots[i];
            return rest;
        }
    }

    /// <summary>drone_id -> that brain's log lines, sorted by time.</summary>
    public sealed class DroneLogIndex
    {
        readonly Dictionary<int, List<LogLine>> _byDrone = new();

        public DroneLogIndex(IReadOnlyList<LogLine> logs)
        {
            if (logs == null) return;
            foreach (var line in logs)
            {
                if (!_byDrone.TryGetValue(line.drone, out var list))
                {
                    list = new List<LogLine>();
                    _byDrone[line.drone] = list;
                }
                list.Add(line);
            }
            foreach (var list in _byDrone.Values)
                list.Sort((a, b) => a.t.CompareTo(b.t));
        }

        public IReadOnlyList<LogLine> ForDrone(int droneId)
        {
            return _byDrone.TryGetValue(droneId, out var list)
                ? list
                : (IReadOnlyList<LogLine>)Array.Empty<LogLine>();
        }
    }
}
