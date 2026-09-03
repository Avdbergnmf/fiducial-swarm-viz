// Intercept spans reconstructed from sparse commit/abort/picket logs.
//
// The brain's trk= is observer-local and does not name a world entity, so the
// line is associated by that drone's Enemy declaration at commit time (fallback:
// nearest alive hostile). Drawing lives in CueOverlay; this is the lookup.

using System.Collections.Generic;
using UnityEngine;

namespace SwarmViewer
{
    public readonly struct CommitSpan
    {
        public readonly int DroneId;
        public readonly int DroneSlot;
        public readonly int TargetSlot;
        public readonly float T0;
        public readonly float T1;
        public readonly LogLine Line;

        public CommitSpan(int droneId, int droneSlot, int targetSlot, float t0, float t1, LogLine line)
        {
            DroneId = droneId;
            DroneSlot = droneSlot;
            TargetSlot = targetSlot;
            T0 = t0;
            T1 = t1;
            Line = line;
        }

        public bool ActiveAt(float t) => t >= T0 - 0.001f && t < T1 - 0.001f;
    }

    public sealed class CommitIndex
    {
        readonly List<CommitSpan> _spans = new();

        public IReadOnlyList<CommitSpan> Spans => _spans;

        public CommitIndex(RunData run)
        {
            if (run?.Meta?.logs == null) return;

            // drone_id -> open commit (target slot, t0, commit line)
            var open = new Dictionary<int, (int target, float t0, LogLine line)>();
            var declared = new List<(int slot, BeliefClass cls)>();

            for (int i = 0; i < run.Meta.logs.Count; i++)
            {
                var line = run.Meta.logs[i];
                if (line == null) continue;
                string verb = LogPhrase.Verb(line.text);
                if (verb != "commit" && verb != "abort" && verb != "picket") continue;

                int drone = line.drone;
                if (verb == "commit")
                {
                    if (open.TryGetValue(drone, out var prev))
                        Close(run, drone, prev.target, prev.t0, line.t, prev.line);
                    int target = ResolveTarget(run, drone, line.t, line.text, declared);
                    open[drone] = (target, line.t, line);
                }
                else if (open.TryGetValue(drone, out var cur))
                {
                    Close(run, drone, cur.target, cur.t0, line.t, cur.line);
                    open.Remove(drone);
                }
            }

            foreach (var kv in open)
                Close(run, kv.Key, kv.Value.target, kv.Value.t0, run.Duration, kv.Value.line);
        }

        void Close(RunData run, int droneId, int targetSlot, float t0, float t1, LogLine line)
        {
            int droneSlot = run.SlotOfDrone(droneId);
            if (droneSlot < 0 || targetSlot < 0) return;
            if (t1 <= t0) t1 = t0 + 0.05f;
            t1 = Mathf.Min(t1, EndAlive(run, droneSlot), EndAlive(run, targetSlot));
            if (t1 <= t0) return;
            _spans.Add(new CommitSpan(droneId, droneSlot, targetSlot, t0, t1, line));
        }

        static float EndAlive(RunData run, int slot)
        {
            var info = run.Info(slot);
            // last_frame is the last recorded pose still alive. They are gone
            // on the next frame, which is also when death events are stamped.
            int last = info.last_frame;
            if (last < 0) return run.Duration;
            return run.TimeOfFrame(Mathf.Min(last + 1, run.FrameCount - 1));
        }

        static int ResolveTarget(RunData run, int droneId, float t, string raw,
            List<(int slot, BeliefClass cls)> declared)
        {
            int self = run.SlotOfDrone(droneId);
            if (self < 0) return -1;
            float frame = run.FrameOf(t);
            if (!run.Sample(frame, self, out Vector3 origin, out _, out _)) return -1;

            run.Beliefs.FillObserver(droneId, t, declared);

            // Believed NED on the commit line. Associate in the horizontal plane:
            // the old 3D nearest-to-(east, 0, north) preferred a 30 m picket over
            // a 40 m hostile once every commit started carrying n=/e=.
            if (LogPhrase.TryNumber(raw, "n=", out float n) &&
                LogPhrase.TryNumber(raw, "e=", out float e))
            {
                float down = 0f;
                if (LogPhrase.TryNumber(raw, "alt=", out float alt)) down = -alt;
                Vector3 want = SwarmCoord.Ned(n, e, down);
                int hit = NearestHorizontal(run, frame, want, self, declared,
                    BeliefClass.Enemy, 40f);
                if (hit >= 0) return hit;
                hit = NearestHostileHorizontal(run, frame, want, self, 40f);
                if (hit >= 0) return hit;
            }

            int best = Nearest(run, frame, origin, declared, BeliefClass.Enemy);
            if (best >= 0) return best;

            float cap = 80f;
            if (run.Params != null && run.Params.Has(run.Params.SenseRadius))
                cap = run.Params.SenseRadius * 1.25f;
            return NearestHostileHorizontal(run, frame, origin, self, cap);
        }

        static float HorizSq(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return dx * dx + dz * dz;
        }

        static int NearestHorizontal(RunData run, float frame, Vector3 origin, int self,
            List<(int slot, BeliefClass cls)> declared, BeliefClass want, float cap)
        {
            float capSq = cap * cap;
            int best = -1;
            float bestD = capSq;
            for (int i = 0; i < declared.Count; i++)
            {
                if (declared[i].cls != want) continue;
                int s = declared[i].slot;
                if (s == self) continue;
                if (!run.Sample(frame, s, out Vector3 p, out _, out _)) continue;
                float d = HorizSq(p, origin);
                if (d < bestD) { bestD = d; best = s; }
            }
            return best;
        }

        static int NearestHostileHorizontal(RunData run, float frame, Vector3 origin,
            int self, float cap)
        {
            float capSq = cap * cap;
            int best = -1;
            float bestD = capSq;
            for (int s = 0; s < run.SlotCount; s++)
            {
                if (s == self) continue;
                if (run.Info(s).Kind != EntityKind.Hostile) continue;
                if (!run.Sample(frame, s, out Vector3 p, out _, out _)) continue;
                float d = HorizSq(p, origin);
                if (d < bestD) { bestD = d; best = s; }
            }
            return best;
        }

        static int Nearest(RunData run, float frame, Vector3 origin,
            List<(int slot, BeliefClass cls)> declared, BeliefClass want)
        {
            int best = -1;
            float bestD = float.PositiveInfinity;
            for (int i = 0; i < declared.Count; i++)
            {
                if (declared[i].cls != want) continue;
                int s = declared[i].slot;
                if (!run.Sample(frame, s, out Vector3 p, out _, out _)) continue;
                float d = (p - origin).sqrMagnitude;
                if (d < bestD) { bestD = d; best = s; }
            }
            return best;
        }
    }
}
