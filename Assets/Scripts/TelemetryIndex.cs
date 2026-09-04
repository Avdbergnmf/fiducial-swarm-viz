// Per-drone radio budget from meta.telemetry, sampled about once a second.
// Last-at-or-before-t so scrubbing backwards costs the same as playing forwards.

using System.Collections.Generic;
using UnityEngine;

namespace SwarmViewer
{
    public struct BudgetSnap
    {
        public bool Has;
        public int BytesSent;
        public int Remaining;
        public int Cap;

        public bool Empty => Has && Remaining <= 0;
        public bool Low => Has && Remaining <= LowOf(Cap);

        public static int LowOf(int cap) =>
            cap > 0 ? Mathf.Max(8, cap / 20) : 32;

        public string Label()
        {
            if (!Has) return "—";
            if (Cap > 0)
                return $"{FormatBytes(Remaining)} / {FormatBytes(Cap)}";
            return FormatBytes(Remaining) + " left";
        }

        public string Detail()
        {
            if (!Has) return "This run has no telemetry for this drone yet.";
            return $"Radio byte budget remaining right now. {FormatBytes(BytesSent)} sent so far. "
                 + "Near zero means this brain talked itself quiet — it is a cause, not a footnote.";
        }

        public static string FormatBytes(int n)
        {
            int v = Mathf.Abs(n);
            if (v >= 10000) return $"{n / 1000f:0.#} kB";
            return n + " B";
        }
    }

    public sealed class TelemetryIndex
    {
        readonly Dictionary<int, List<TelemetrySample>> _byDrone = new();
        readonly Dictionary<int, int> _cap = new();

        public bool HasAny => _byDrone.Count > 0;

        public TelemetryIndex(IReadOnlyList<TelemetrySample> samples)
        {
            if (samples == null) return;
            for (int i = 0; i < samples.Count; i++)
            {
                var s = samples[i];
                if (s == null || s.drone < 0) continue;
                if (!_byDrone.TryGetValue(s.drone, out var list))
                {
                    list = new List<TelemetrySample>();
                    _byDrone[s.drone] = list;
                }
                list.Add(s);
            }

            foreach (var kv in _byDrone)
            {
                kv.Value.Sort((a, b) => a.t.CompareTo(b.t));
                int cap = 0;
                var list = kv.Value;
                if (list.Count > 0)
                    cap = Mathf.Max(0, list[0].budget_remaining + list[0].bytes_sent);
                for (int i = 0; i < list.Count; i++)
                    if (list[i].budget_remaining > cap)
                        cap = list[i].budget_remaining;
                _cap[kv.Key] = cap;
            }
        }

        public BudgetSnap At(int droneId, float t)
        {
            if (!_byDrone.TryGetValue(droneId, out var list) || list.Count == 0)
                return default;
            int i = LastAtOrBefore(list, t);
            if (i < 0) return default;
            var s = list[i];
            _cap.TryGetValue(droneId, out int cap);
            return new BudgetSnap
            {
                Has = true,
                BytesSent = s.bytes_sent,
                Remaining = s.budget_remaining,
                Cap = cap,
            };
        }

        /// <summary>Remaining-budget samples at or before t, oldest first. Returns count written.</summary>
        public int CopyRemaining(int droneId, float t, float[] dst)
        {
            if (dst == null || dst.Length == 0) return 0;
            if (!_byDrone.TryGetValue(droneId, out var list) || list.Count == 0)
                return 0;
            int last = LastAtOrBefore(list, t);
            if (last < 0) return 0;
            int n = Mathf.Min(dst.Length, last + 1);
            int start = last + 1 - n;
            for (int i = 0; i < n; i++)
                dst[i] = list[start + i].budget_remaining;
            return n;
        }

        static int LastAtOrBefore(List<TelemetrySample> list, float t)
        {
            int lo = 0, hi = list.Count - 1, ans = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                if (list[mid].t <= t + 0.0001f)
                {
                    ans = mid;
                    lo = mid + 1;
                }
                else
                    hi = mid - 1;
            }
            return ans;
        }
    }
}
