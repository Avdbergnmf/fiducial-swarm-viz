// Evaluates and holds the snapshot of all entity states at a single point in time.
// Exposes entity positions, rotations, and velocities for views to render.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace SwarmViewer
{
    public struct EntitySnapshot
    {
        public int Slot;
        public bool Alive;
        public Vector3 Position;
        public Vector3 Velocity;
        public Quaternion Rotation;
    }

    /// <summary>
    /// The world at one instant. The single thing views are allowed to read: no
    /// view samples RunData directly and no view does its own time arithmetic, so
    /// a new panel is a new subscriber rather than a change to anything working.
    ///
    /// Buffers are reused, so a normal frame allocates nothing.
    /// </summary>
    public sealed class RunState
    {
        readonly RunData _run;
        readonly EntitySnapshot[] _entities;

        public event Action Changed;

        public RunData Run => _run;
        public float Time { get; private set; }
        public int FrameIndex { get; private set; }

        /// <summary>Indexed by slot. Check Alive before drawing.</summary>
        public IReadOnlyList<EntitySnapshot> Entities => _entities;

        public RunState(RunData run)
        {
            _run = run;
            _entities = new EntitySnapshot[run.SlotCount];
            for (int i = 0; i < _entities.Length; i++) _entities[i].Slot = i;
        }

        public EntityInfo Info(int slot) => _run.Info(slot);

        public void Evaluate(float t)
        {
            Time = t;
            float frameF = _run.FrameOf(t);
            FrameIndex = Mathf.RoundToInt(frameF);

            for (int s = 0; s < _entities.Length; s++)
            {
                bool alive = _run.Sample(frameF, s, out var p, out var r, out var v);
                _entities[s].Alive = alive;
                if (!alive) continue;
                _entities[s].Position = p;
                _entities[s].Rotation = r;
                _entities[s].Velocity = v;
            }

            // EXTENSION POINT. Everything else derived from time goes here and is
            // exposed as a property, so views stay dumb:
            //   active radio links  -> filter Run.Meta.links by t_start <= t < t_end
            //   belief per observer -> binary search Run.Meta.beliefs
            //   events in a window  -> binary search Run.Meta.events
            //   telemetry per drone -> last sample at or before t

            Changed?.Invoke();
        }

        /// <summary>Ground truth. Never feed this into a view of what the fleet believed.</summary>
        public bool IsCompromisedNow(int slot)
        {
            var e = Info(slot);
            return e.compromised_from >= 0 && FrameIndex >= e.compromised_from;
        }
    }
}
