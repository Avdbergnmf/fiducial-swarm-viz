// World and brain numbers the overlay is allowed to draw.
// Missing is unknown — never fill in 60/90 from memory (FORMAT.md).

using System.Globalization;
using UnityEngine;

namespace SwarmViewer
{
    public sealed class RunParams
    {
        public float KillRadius;
        public float SenseRadius;
        public float CommRadius;
        public float MaxSpeed;
        public float MaxAccel;
        public float MaxTilt;
        public float LateralLimit;
        public float SeparationMargin;
        public float RingRadius;
        public float RingAltitude;
        public float ObservedComm;
        public bool CommFromLinks;

        public float CommDraw => CommRadius > 0f ? CommRadius : ObservedComm;
        public bool Has(float metres) => metres > 0.01f;

        public static RunParams From(RunData run)
        {
            var p = new RunParams();
            if (run?.Meta == null) return p;

            p.KillRadius = run.Meta.kill_radius;
            ParseLogs(run, p);
            p.ObservedComm = MeasureMaxLinkRange(run);
            if (!p.Has(p.CommRadius) && p.Has(p.ObservedComm))
            {
                p.CommRadius = p.ObservedComm;
                p.CommFromLinks = true;
            }

            return p;
        }

        static void ParseLogs(RunData run, RunParams p)
        {
            var logs = run.Meta.logs;
            if (logs == null) return;
            for (int i = 0; i < logs.Count; i++)
            {
                string text = logs[i]?.text;
                if (string.IsNullOrEmpty(text) || !text.StartsWith("params ")) continue;
                TryKey(text, "sense", ref p.SenseRadius);
                TryKey(text, "comm", ref p.CommRadius);
                TryKey(text, "maxv", ref p.MaxSpeed);
                TryKey(text, "maxa", ref p.MaxAccel);
                TryKey(text, "tilt", ref p.MaxTilt);
                TryKey(text, "lat", ref p.LateralLimit);
                TryKey(text, "sep", ref p.SeparationMargin);
                TryKey(text, "ring", ref p.RingRadius);
                TryKey(text, "alt", ref p.RingAltitude);
                return;
            }
        }

        static void TryKey(string line, string key, ref float dst)
        {
            string token = key + "=";
            int at = line.IndexOf(token, System.StringComparison.Ordinal);
            if (at < 0) return;
            int start = at + token.Length;
            int end = start;
            while (end < line.Length && line[end] != ' ') end++;
            if (float.TryParse(line.Substring(start, end - start), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out float v) && v > 0f)
                dst = v;
        }

        static float MeasureMaxLinkRange(RunData run)
        {
            var links = run.Meta.links;
            if (links == null || links.Count == 0) return 0f;

            float max = 0f;
            for (int i = 0; i < links.Count; i++)
            {
                var link = links[i];
                int sa = run.SlotOfDrone(link.a);
                int sb = run.SlotOfDrone(link.b);
                if (sa < 0 || sb < 0) continue;

                float t0 = link.t_start;
                float t1 = link.t_end > t0 ? link.t_end : t0;
                SampleRange(run, sa, sb, t0, ref max);
                SampleRange(run, sa, sb, 0.5f * (t0 + t1), ref max);
                SampleRange(run, sa, sb, Mathf.Max(t0, t1 - 0.05f), ref max);
            }

            return max;
        }

        static void SampleRange(RunData run, int a, int b, float t, ref float max)
        {
            float f = run.FrameOf(t);
            if (!run.Sample(f, a, out var pa, out _, out _)) return;
            if (!run.Sample(f, b, out var pb, out _, out _)) return;
            float d = Vector3.Distance(pa, pb);
            if (d > max) max = d;
        }
    }
}
