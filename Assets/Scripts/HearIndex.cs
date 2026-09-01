// Fused hearsay calls reconstructed from `call … peer origin= hops= n= e=`.
//
// declare_track only names a local track_id, so a drone that knows about an
// aircraft only by radio never appears in beliefs[]. The peer call line is
// the same geometric association the brain used (D14 / D18): n/e are NED
// at the call, converted here, then nearest live entity.

using System.Collections.Generic;
using UnityEngine;

namespace SwarmViewer
{
    public readonly struct HearCall
    {
        public readonly float T;
        public readonly BeliefClass Class;
        public readonly int Origin;
        public readonly int Hops;

        public HearCall(float t, BeliefClass cls, int origin, int hops)
        {
            T = t;
            Class = cls;
            Origin = origin;
            Hops = hops;
        }
    }

    public sealed class HearIndex
    {
        readonly Dictionary<(int observer, int slot), List<HearCall>> _byPair = new();

        public HearIndex(RunData run)
        {
            if (run?.Meta?.logs == null) return;

            for (int i = 0; i < run.Meta.logs.Count; i++)
            {
                var line = run.Meta.logs[i];
                if (line == null || string.IsNullOrEmpty(line.text)) continue;
                if (line.text.IndexOf(" peer", System.StringComparison.Ordinal) < 0)
                    continue;

                string verb = LogPhrase.Verb(line.text);
                BeliefClass cls;
                if (verb == "call")
                    cls = ParseClass(ClassToken(line.text));
                else if (verb == "drop")
                    cls = BeliefClass.Unknown;
                else
                    continue;

                if (!LogPhrase.TryNumber(line.text, "n=", out float n) ||
                    !LogPhrase.TryNumber(line.text, "e=", out float e))
                    continue;

                int slot = NearestTo(run, line.t, SwarmCoord.Ned(n, e), 40f);
                if (slot < 0) continue;

                int origin = LogPhrase.IntField(line.text, "origin=");
                int hops = LogPhrase.IntField(line.text, "hops=");
                if (origin < 0) origin = 0;
                if (hops < 0) hops = 0;

                var key = (line.drone, slot);
                if (!_byPair.TryGetValue(key, out var list))
                {
                    list = new List<HearCall>();
                    _byPair[key] = list;
                }
                list.Add(new HearCall(line.t, cls, origin, hops));
            }

            foreach (var list in _byPair.Values)
                list.Sort((a, b) => a.T.CompareTo(b.T));
        }

        public HearCall? At(int observer, int slot, float t)
        {
            if (!_byPair.TryGetValue((observer, slot), out var list) || list.Count == 0)
                return null;

            int lo = 0, hi = list.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (list[mid].T <= t + 0.001f) lo = mid + 1;
                else hi = mid;
            }
            if (lo == 0) return null;
            return list[lo - 1];
        }

        public void FillSubject(int slot, float t, List<(int observer, HearCall call)> dst)
        {
            dst.Clear();
            foreach (var kv in _byPair)
            {
                if (kv.Key.slot != slot) continue;
                var call = At(kv.Key.observer, slot, t);
                if (call == null || call.Value.Class == BeliefClass.Unknown) continue;
                dst.Add((kv.Key.observer, call.Value));
            }
            dst.Sort((a, b) => a.observer.CompareTo(b.observer));
        }

        static int NearestTo(RunData run, float t, Vector3 origin, float cap)
        {
            float frame = run.FrameOf(t);
            float capSq = cap * cap;
            int best = -1;
            float bestD = capSq;
            for (int s = 0; s < run.SlotCount; s++)
            {
                if (!run.Sample(frame, s, out Vector3 p, out _, out _)) continue;
                float d = (p - origin).sqrMagnitude;
                if (d < bestD) { bestD = d; best = s; }
            }
            return best;
        }

        static string ClassToken(string raw)
        {
            var tok = raw.Split(' ');
            return tok.Length > 2 ? tok[2] : "";
        }

        static BeliefClass ParseClass(string cls) => cls switch
        {
            "hostile" or "enemy" => BeliefClass.Enemy,
            "civilian" or "neutral" => BeliefClass.Neutral,
            "friendly" => BeliefClass.Friendly,
            "compromised" => BeliefClass.Compromised,
            _ => BeliefClass.Unknown,
        };
    }
}
