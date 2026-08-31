// Immutable container for loaded simulation metadata and frame-major trajectory geometry.
// Provides spatial and temporal sampling without maintaining runtime playback state.

using System.Collections.Generic;
using UnityEngine;

namespace SwarmViewer
{
    /// <summary>
    /// One loaded run. Immutable after construction and not time-aware: you ask it
    /// about a moment, it never has a current moment of its own. That separation is
    /// what lets scrubbing backwards cost the same as playing forwards.
    /// </summary>
    public sealed class RunData
    {
        public readonly RunMeta Meta;
        public readonly EntityEventIndex EventsBySlot;
        public readonly DroneLogIndex LogsByDrone;

        readonly float[] _geo;      // frame-major: [(frame * slots + slot) * stride]
        readonly int _slots, _stride;

        public int FrameCount => Meta.frame_count;
        public int SlotCount => _slots;
        public float Duration => Meta.duration;
        public float TraceHz => Meta.trace_hz;

        /// <summary>
        /// Header kill radius in metres. 0 means the meta file predates the field;
        /// call <see cref="KillRadiusOrDefault"/> with the inspector fallback.
        /// </summary>
        public float KillRadius => Meta.kill_radius;

        public RunData(RunMeta meta, float[] geometry)
        {
            Meta = meta;
            _geo = geometry;
            _slots = meta.slot_count;
            _stride = meta.stride;

            foreach (var e in meta.entities) e.Kind = ParseKind(e.kind);
            meta.events.Sort((a, b) => a.t.CompareTo(b.t));
            meta.beliefs.Sort((a, b) => a.t.CompareTo(b.t));
            meta.logs.Sort((a, b) => a.t.CompareTo(b.t));

            EventsBySlot = new EntityEventIndex(meta.events, meta.slot_count);
            LogsByDrone = new DroneLogIndex(meta.logs);
        }

        /// <summary>Uses the meta value when present, otherwise <paramref name="fallback"/>.</summary>
        public float KillRadiusOrDefault(float fallback) =>
            Meta.kill_radius > 0f ? Meta.kill_radius : fallback;

        public IReadOnlyList<EntityEventRecord> EventsFor(int slot) => EventsBySlot.ForSlot(slot);

        public IReadOnlyList<LogLine> LogsForDrone(int droneId) => LogsByDrone.ForDrone(droneId);

        static EntityKind ParseKind(string s) => s switch
        {
            "friendly" => EntityKind.Friendly,
            "hostile" => EntityKind.Hostile,
            "civilian" => EntityKind.Civilian,
            "wreckage" => EntityKind.Wreckage,
            _ => EntityKind.Unknown,
        };

        public EntityInfo Info(int slot) => Meta.entities[slot];

        /// <summary>Fractional frame index for a time, clamped to the run.</summary>
        public float FrameOf(float t) => Mathf.Clamp(t * TraceHz, 0f, FrameCount - 1f);

        public float TimeOfFrame(int frame) => frame / TraceHz;

        int Offset(int frame, int slot) => ((frame * _slots) + slot) * _stride;

        public bool IsAlive(int frame, int slot) => _geo[Offset(frame, slot) + 10] > 0.5f;

        /// <summary>Interpolated sample. False when the entity does not exist then.</summary>
        public bool Sample(float frameF, int slot, out Vector3 pos, out Quaternion rot, out Vector3 vel)
        {
            pos = default; rot = Quaternion.identity; vel = default;

            int f0 = Mathf.FloorToInt(frameF);
            int f1 = Mathf.Min(f0 + 1, FrameCount - 1);
            float u = frameF - f0;

            bool a0 = IsAlive(f0, slot), a1 = IsAlive(f1, slot);
            if (!a0 && !a1) return false;

            // Never interpolate across a spawn or a death: it drags the airframe
            // in from the origin for one frame and looks like a teleport.
            if (!a1) { f1 = f0; u = 0f; }
            else if (!a0) { f0 = f1; u = 1f; }

            int o0 = Offset(f0, slot), o1 = Offset(f1, slot);
            pos = Vector3.Lerp(V3(o0), V3(o1), u);
            vel = Vector3.Lerp(V3(o0 + 3), V3(o1 + 3), u);
            rot = Quaternion.Slerp(Q(o0 + 6), Q(o1 + 6), u);
            return true;
        }

        Vector3 V3(int o) => new(_geo[o], _geo[o + 1], _geo[o + 2]);
        Quaternion Q(int o) => new(_geo[o], _geo[o + 1], _geo[o + 2], _geo[o + 3]);
    }
}
