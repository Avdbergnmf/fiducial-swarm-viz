// Believed intercept meeting, reconstructed from commit / near / ram logs.
//
// in=/ie=/ialt= is CollisionCourse I(t) — where that drone believed it would
// ram. n=/e=/alt= is the believed body (CommitIndex still uses it). TryAt
// draws I when present. It does not add fix_sigma; the sample is already in
// the believed frame. Old traces without in= fall back to the body.

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
        public readonly float MeetNorth;
        public readonly float MeetEast;
        public readonly float MeetAlt;
        public readonly bool HasMeet;

        public AimSample(int droneId, float t, float north, float east, float alt,
            float meetN, float meetE, float meetAlt, bool hasMeet)
        {
            DroneId = droneId;
            T = t;
            North = north;
            East = east;
            Alt = alt;
            MeetNorth = meetN;
            MeetEast = meetE;
            MeetAlt = meetAlt;
            HasMeet = hasMeet;
        }
    }

    public sealed class AimIndex
    {
        readonly List<AimSample> _samples = new();
        readonly Dictionary<int, int> _first = new();

        public IReadOnlyList<AimSample> Samples => _samples;
        public int Count => _samples.Count;

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
                float inn = 0f, iee = 0f, ialt = 0f;
                bool hasMeet = LogPhrase.TryNumber(line.text, "in=", out inn)
                    && LogPhrase.TryNumber(line.text, "ie=", out iee);
                if (hasMeet)
                    LogPhrase.TryNumber(line.text, "ialt=", out ialt);

                if (!_first.ContainsKey(line.drone))
                    _first[line.drone] = _samples.Count;
                _samples.Add(new AimSample(line.drone, line.t, n, e,
                    hasAlt ? alt : float.NaN,
                    inn, iee, ialt, hasMeet));
            }
        }

        /// <summary>
        /// Believed ram point for this drone at t, while a commit span is open.
        /// Prefers in=/ie=/ialt= (CollisionCourse meeting). No extra noise,
        /// no vn/ve coast on I. age is seconds since the sample.
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

            if (s.HasMeet)
            {
                pos = SwarmCoord.Ned(s.MeetNorth, s.MeetEast, -s.MeetAlt);
                return true;
            }

            float down = 0f;
            if (!float.IsNaN(s.Alt)) down = -s.Alt;
            pos = SwarmCoord.Ned(s.North, s.East, down);
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
