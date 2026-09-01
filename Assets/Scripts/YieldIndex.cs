// Picket-off-corridor spans, and the log rows that make them filterable.
//
// The brain does not write yield (D3 / D16). These lines are reconstructed
// from intercept spans and params fsep= at load, then merged into meta.logs
// so the Logs window and inspector chips see a real verb. Keep-out is the
// remaining flight to the predicted ram (D17), not interceptor→hostile.

using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace SwarmViewer
{
    public readonly struct YieldSpan
    {
        public readonly int PicketSlot;
        public readonly int PicketDrone;
        public readonly int InterceptorSlot;
        public readonly int TargetSlot;
        public readonly float T0;
        public readonly float T1;
        public readonly float Dist0;

        public YieldSpan(int picketSlot, int picketDrone, int interceptorSlot,
            int targetSlot, float t0, float t1, float dist0)
        {
            PicketSlot = picketSlot;
            PicketDrone = picketDrone;
            InterceptorSlot = interceptorSlot;
            TargetSlot = targetSlot;
            T0 = t0;
            T1 = t1;
            Dist0 = dist0;
        }

        public bool ActiveAt(float t) => t >= T0 - 0.001f && t < T1 - 0.001f;
    }

    public sealed class YieldIndex
    {
        const float ExitSlack = 2f;
        const float MinHold = 0.15f;

        readonly List<YieldSpan> _spans = new();

        public IReadOnlyList<YieldSpan> Spans => _spans;

        public YieldIndex(RunData run)
        {
            if (run?.Commits == null || run.Params == null) return;
            if (!run.Params.Has(run.Params.FriendlyMargin)) return;
            if (run.Commits.Spans.Count == 0) return;

            Build(run, run.Params.FriendlyMargin);
            AppendLogs(run);
        }

        void Build(RunData run, float clear)
        {
            var commits = run.Commits.Spans;
            var open = new Dictionary<(int picket, int inter, int tgt), (float t0, float dist)>();
            var current = new List<(int picket, int inter, int tgt, float dist)>();

            int frames = run.FrameCount;
            for (int f = 0; f < frames; f++)
            {
                float t = run.TimeOfFrame(f);
                current.Clear();
                Collect(run, commits, t, f, clear, current);

                // Close anything no longer inside (with slack so the edge does not chatter).
                var gone = new List<(int picket, int inter, int tgt)>();
                foreach (var kv in open)
                {
                    bool still = false;
                    float dist = kv.Value.dist;
                    for (int i = 0; i < current.Count; i++)
                    {
                        if (current[i].picket != kv.Key.picket ||
                            current[i].inter != kv.Key.inter ||
                            current[i].tgt != kv.Key.tgt)
                            continue;
                        still = current[i].dist <= clear + ExitSlack;
                        dist = current[i].dist;
                        break;
                    }
                    if (!still)
                        gone.Add(kv.Key);
                }
                for (int i = 0; i < gone.Count; i++)
                {
                    var key = gone[i];
                    var prev = open[key];
                    open.Remove(key);
                    Close(run, key.picket, key.inter, key.tgt, prev.t0, t, prev.dist);
                }

                for (int i = 0; i < current.Count; i++)
                {
                    var c = current[i];
                    var key = (c.picket, c.inter, c.tgt);
                    if (open.ContainsKey(key)) continue;
                    if (c.dist > clear) continue;
                    open[key] = (t, c.dist);
                }
            }

            float end = run.Duration;
            foreach (var kv in open)
                Close(run, kv.Key.picket, kv.Key.inter, kv.Key.tgt,
                    kv.Value.t0, end, kv.Value.dist);
        }

        static void Collect(RunData run, IReadOnlyList<CommitSpan> commits, float t, int frame,
            float clear, List<(int picket, int inter, int tgt, float dist)> dst)
        {
            for (int i = 0; i < commits.Count; i++)
            {
                var span = commits[i];
                if (!span.ActiveAt(t)) continue;
                int a = span.DroneSlot;
                int b = span.TargetSlot;
                if (!run.IsAlive(frame, a) || !run.IsAlive(frame, b)) continue;
                if (!run.Sample(frame, a, out Vector3 from, out _, out _)) continue;
                if (!run.Sample(frame, b, out Vector3 to, out _, out Vector3 toVel)) continue;
                Vector3 end = CorridorHorizon(from, to, toVel);
                float hx = end.x - from.x, hz = end.z - from.z;
                if (hx * hx + hz * hz < 1f) continue;

                for (int s = 0; s < run.SlotCount; s++)
                {
                    if (s == a || s == b) continue;
                    if (run.Info(s).drone_id < 0) continue;
                    if (!run.IsAlive(frame, s)) continue;
                    if (IsIntercepting(commits, s, t)) continue;
                    if (!run.Sample(frame, s, out Vector3 p, out _, out _)) continue;
                    if (!ClosestOnSegmentXZ(p, from, end, out _, out float dist)) continue;
                    if (dist > clear + ExitSlack) continue;
                    dst.Add((s, a, b, dist));
                }
            }
        }

        void Close(RunData run, int picket, int inter, int tgt, float t0, float t1, float dist)
        {
            if (t1 < t0 + MinHold) return;
            int drone = run.Info(picket).drone_id;
            if (drone < 0) return;
            _spans.Add(new YieldSpan(picket, drone, inter, tgt, t0, t1, dist));
        }

        void AppendLogs(RunData run)
        {
            if (_spans.Count == 0) return;
            var logs = run.Meta.logs;
            for (int i = 0; i < _spans.Count; i++)
            {
                var s = _spans[i];
                int interceptor = run.Info(s.InterceptorSlot).drone_id;
                string target = run.Info(s.TargetSlot).Label;
                string dist = s.Dist0.ToString("F1", CultureInfo.InvariantCulture);
                logs.Add(new LogLine
                {
                    t = s.T0,
                    drone = s.PicketDrone,
                    text = $"yield interceptor={interceptor} target={target} dist={dist}",
                });
                logs.Add(new LogLine
                {
                    t = s.T1,
                    drone = s.PicketDrone,
                    text = $"yield clear interceptor={interceptor} target={target}",
                });
            }
            logs.Sort((a, b) => a.t.CompareTo(b.t));
        }

        static bool IsIntercepting(IReadOnlyList<CommitSpan> spans, int slot, float t)
        {
            for (int i = 0; i < spans.Count; i++)
            {
                if (spans[i].DroneSlot == slot && spans[i].ActiveAt(t))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Remaining intercept flight, not the chord to the hostile's current
        /// pose. Keep in step with policy.cpp CorridorHorizon (D17): cruise 14,
        /// closing floor 1, 0.5 s catch slack, abort cap 12 s.
        /// </summary>
        public static Vector3 CorridorHorizon(Vector3 from, Vector3 hostile, Vector3 hostileVel)
        {
            const float cruise = 14f;
            const float minClosing = 1f;
            const float slack = 0.5f;
            const float maxTime = 12f;

            float dx = hostile.x - from.x;
            float dz = hostile.z - from.z;
            float range = Mathf.Sqrt(dx * dx + dz * dz);
            if (range < 1f) return hostile;
            float inv = 1f / range;
            float dirx = dx * inv, dirz = dz * inv;
            float ivx = dirx * cruise, ivz = dirz * cruise;
            float closing = -((hostileVel.x - ivx) * dirx + (hostileVel.z - ivz) * dirz);
            if (closing < minClosing) return from;
            float tMeet = Mathf.Min(range / closing + slack, maxTime);
            float along = Mathf.Min(cruise * tMeet, range);
            return new Vector3(from.x + dirx * along, from.y, from.z + dirz * along);
        }

        public static bool ClosestOnSegmentXZ(Vector3 p, Vector3 a, Vector3 b,
            out Vector3 closest, out float dist)
        {
            float abx = b.x - a.x;
            float abz = b.z - a.z;
            float ab2 = abx * abx + abz * abz;
            if (ab2 < 1e-4f)
            {
                closest = a;
                float dx = p.x - a.x;
                float dz = p.z - a.z;
                dist = Mathf.Sqrt(dx * dx + dz * dz);
                return dist > 0.05f;
            }

            float u = ((p.x - a.x) * abx + (p.z - a.z) * abz) / ab2;
            if (u < 0f) u = 0f;
            if (u > 1f) u = 1f;
            closest = new Vector3(a.x + u * abx, p.y, a.z + u * abz);
            float hx = p.x - closest.x;
            float hz = p.z - closest.z;
            dist = Mathf.Sqrt(hx * hx + hz * hz);
            return dist > 0.05f;
        }
    }
}
