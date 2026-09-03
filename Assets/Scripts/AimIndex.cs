// Believed target pose, reconstructed from commit / near / ram log lines.
//
// The recording is ground truth. These samples are what that drone thought it
// was flying at — own fix plus the track it had associated. Drawing lives in
// CueOverlay; this is the lookup.

using System.Collections.Generic;
using UnityEngine;

namespace SwarmViewer
{
    public readonly struct AimSample
    {
        public readonly int DroneId;
        public readonly float T;
        public readonly float North;
        public readonly float East;
        public readonly float Alt;
        public readonly float Vn;
        public readonly float Ve;
        public readonly bool HasVel;

        public AimSample(int droneId, float t, float north, float east, float alt,
            float vn, float ve, bool hasVel)
        {
            DroneId = droneId;
            T = t;
            North = north;
            East = east;
            Alt = alt;
            Vn = vn;
            Ve = ve;
            HasVel = hasVel;
        }
    }

    public sealed class AimIndex
    {
        readonly List<AimSample> _samples = new();
        readonly Dictionary<int, int> _first = new();

        public IReadOnlyList<AimSample> Samples => _samples;
        public int Count => _samples.Count;

        /// <summary>
        /// Same hold as ping lines. Believed pose only refreshes on
        /// commit / near / ram, then this is how long the ghost stays up.
        /// </summary>
        public const float Hold = RelationIndex.PingHold;

        public AimIndex(RunData run)
        {
            if (run?.Meta?.logs == null) return;

            for (int i = 0; i < run.Meta.logs.Count; i++)
            {
                var line = run.Meta.logs[i];
                if (line == null || line.drone < 0) continue;
                string verb = LogPhrase.Verb(line.text);
                if (verb != "commit" && verb != "near" && verb != "ram") continue;
                if (!LogPhrase.TryNumber(line.text, "n=", out float n)) continue;
                if (!LogPhrase.TryNumber(line.text, "e=", out float e)) continue;

                float alt = 0f;
                bool hasAlt = LogPhrase.TryNumber(line.text, "alt=", out alt);
                float vn = 0f, ve = 0f;
                bool hasVel = LogPhrase.TryNumber(line.text, "vn=", out vn)
                    && LogPhrase.TryNumber(line.text, "ve=", out ve);
                if (!hasVel) { vn = 0f; ve = 0f; }

                if (!_first.ContainsKey(line.drone))
                    _first[line.drone] = _samples.Count;
                _samples.Add(new AimSample(line.drone, line.t, n, e,
                    hasAlt ? alt : float.NaN, vn, ve, hasVel));
            }
        }

        /// <summary>
        /// Believed target pose for this drone at t, while a commit span is
        /// still open. Latest sample at or before t; the marker stays at the
        /// pose the brain actually logged until another sample replaces it.
        /// age is seconds since that sample — the overlay fades it out.
        /// </summary>
        public bool TryAt(RunData run, int droneId, float t, out Vector3 pos, out float age)
        {
            pos = default;
            age = 0f;
            if (droneId < 0 || _samples.Count == 0) return false;
            if (!InCommit(run, droneId, t)) return false;
            if (!_first.TryGetValue(droneId, out int start)) return false;

            int pick = -1;
            for (int i = start; i < _samples.Count; i++)
            {
                if (_samples[i].T > t + 0.001f) break;
                if (_samples[i].DroneId != droneId) continue;
                pick = i;
            }
            if (pick < 0) return false;

            var s = _samples[pick];
            float dt = t - s.T;
            if (dt < 0f) dt = 0f;
            age = dt;
            float n = s.North;
            float e = s.East;

            float down = 0f;
            if (!float.IsNaN(s.Alt)) down = -s.Alt;
            pos = SwarmCoord.Ned(n, e, down);
            return true;
        }

        static bool InCommit(RunData run, int droneId, float t)
        {
            var spans = run?.Commits?.Spans;
            if (spans == null) return false;
            for (int i = 0; i < spans.Count; i++)
            {
                if (spans[i].DroneId != droneId) continue;
                if (spans[i].ActiveAt(t)) return true;
            }
            return false;
        }
    }
}
