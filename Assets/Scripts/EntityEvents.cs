// Inspector face for one entity: identity, kill radius, events it appears in, and
// (for friendlies) the log lines that drone wrote. Data comes from EntityEventIndex
// / DroneLogIndex; this component does not group anything itself.

using System.Collections.Generic;
using UnityEngine;

namespace SwarmViewer
{
    public sealed class EntityEvents : MonoBehaviour
    {
        [Header("Identity (from meta.entities[])")]
        [SerializeField] int slot;
        [SerializeField] int traceId;
        [SerializeField] string kind;
        [SerializeField] int droneId = -1;
        [SerializeField] int firstFrame;
        [SerializeField] int lastFrame;
        [SerializeField] int compromisedFrom = -1;

        [Header("Limits")]
        [SerializeField] float killRadius;

        [Header("Events involving this slot, sorted by time")]
        [SerializeField] List<EntityEventRecord> events = new();

        [Header("Logs this drone wrote (empty if not a friendly)")]
        [SerializeField] List<LogLine> logs = new();

        public int Slot => slot;
        public IReadOnlyList<EntityEventRecord> Events => events;
        public IReadOnlyList<LogLine> Logs => logs;

        public void Populate(
            EntityInfo info,
            float killRadiusMetres,
            IReadOnlyList<EntityEventRecord> slotEvents,
            IReadOnlyList<LogLine> droneLogs)
        {
            slot = info.slot;
            traceId = info.trace_id;
            kind = info.kind;
            droneId = info.drone_id;
            firstFrame = info.first_frame;
            lastFrame = info.last_frame;
            compromisedFrom = info.compromised_from;
            killRadius = killRadiusMetres;

            events.Clear();
            if (slotEvents != null)
            {
                for (int i = 0; i < slotEvents.Count; i++)
                    events.Add(slotEvents[i]);
            }

            logs.Clear();
            if (droneLogs != null)
            {
                for (int i = 0; i < droneLogs.Count; i++)
                    logs.Add(droneLogs[i]);
            }
        }
    }
}
