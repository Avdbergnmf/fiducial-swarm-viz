// Data structures representing the parsed run metadata from *.meta.json.
// Plain data models with no runtime simulation or visual behavior.

using System;
using System.Collections.Generic;

namespace SwarmViewer
{
    // Mirrors run.meta.json (see FORMAT.md). Plain data, no behaviour.
    // Fields the minimal viewer does not use yet are still parsed, so extending
    // means writing a view, not changing the model.

    public enum EntityKind { Unknown, Friendly, Hostile, Civilian, Wreckage }

    public enum BeliefClass { Unknown = 0, Friendly = 1, Enemy = 2, Neutral = 3, Compromised = 4 }

    [Serializable]
    public class EntityInfo
    {
        public int slot;
        public int trace_id;
        public string kind;
        public int drone_id = -1;
        public int first_frame = -1;
        public int last_frame = -1;
        public int compromised_from = -1;      // ground truth; not shown in a belief view

        [NonSerialized] public EntityKind Kind;

        public bool IsFriendly => drone_id >= 0;
        public string Label => IsFriendly ? $"Drone {drone_id}" : $"{Kind} {trace_id}";
    }

    [Serializable]
    public class EventInfo
    {
        public float t;
        public int frame;
        public string kind;                    // spawn | death | collision | breach | detect
        public int severity;                   // 0 info .. 3 mission-losing
        public int[] slots;
        public string text;
    }

    [Serializable] public class LinkInfo { public int a, b; public float t_start, t_end; }
    [Serializable] public class BeliefChange { public float t; public int observer, slot; public string @class; }
    [Serializable] public class LogLine { public float t; public int drone; public string text; }
    [Serializable] public class TelemetrySample { public float t; public int drone; public int bytes_sent, budget_remaining; }
    [Serializable] public class Bounds3 { public float[] min, max; }
    [Serializable] public class AssetInfo { public float[] position; public float radius; }

    [Serializable]
    public class ProvenanceInfo // (where this file came from)
    {
        public string trace;
        public string scenario;
        public string brain;
        public string sim_version;
        public int header_schema;
        public string generated_at;
    }

    [Serializable]
    public class RunMeta
    {
        public int format_version;
        public string source, scenario, sim_version, brain;
        public float dt, trace_hz, duration;
        public int frame_count, slot_count, stride, fleet_size;
        public Bounds3 arena;
        public AssetInfo asset;
        public float kill_radius;              // 0 when the field is absent (older meta files)
        public ProvenanceInfo provenance;

        public List<EntityInfo> entities = new();
        public List<EventInfo> events = new();
        public List<LinkInfo> links = new();
        public List<BeliefChange> beliefs = new();
        public List<LogLine> logs = new();
        public List<TelemetrySample> telemetry = new();
    }
}

