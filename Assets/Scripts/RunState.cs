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
        public Vector3 Acceleration; // finite difference of recorded velocity, m/s^2
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
        readonly BudgetSnap[] _budget;

        public event Action Changed;

        public RunData Run => _run;
        public float Time { get; private set; }
        public int FrameIndex { get; private set; }

        /// <summary>Indexed by slot. Check Alive before drawing.</summary>
        public IReadOnlyList<EntitySnapshot> Entities => _entities;

        /// <summary>Last telemetry sample at or before now. <see cref="BudgetSnap.Has"/> is false when this slot is not a fleet drone or the run has no telemetry.</summary>
        public BudgetSnap Budget(int slot) =>
            (uint)slot < (uint)_budget.Length ? _budget[slot] : default;

        public RunState(RunData run)
        {
            _run = run;
            _entities = new EntitySnapshot[run.SlotCount];
            _budget = new BudgetSnap[run.SlotCount];
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
                bool prevOk = _run.Sample(Mathf.Max(0f, frameF - 1f), s, out _, out _, out var vPrev);
                _entities[s].Acceleration = prevOk ? (v - vPrev) * _run.TraceHz : Vector3.zero;
            }

            var telemetry = _run.Telemetry;
            for (int s = 0; s < _budget.Length; s++)
            {
                int drone = _run.Info(s).drone_id;
                _budget[s] = drone >= 0 ? telemetry.At(drone, t) : default;
            }

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
