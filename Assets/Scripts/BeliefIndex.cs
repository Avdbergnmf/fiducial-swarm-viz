// Per-observer belief overlay from meta.beliefs[] (declaration deltas).
// Hold the last class per (observer drone, entity slot); undeclared is Unknown.

using System;
using System.Collections.Generic;

namespace SwarmViewer
{
    public sealed class BeliefIndex
    {
        readonly Dictionary<(int observer, int slot), List<BeliefChange>> _byPair = new();

        public BeliefIndex(IReadOnlyList<BeliefChange> beliefs)
        {
            if (beliefs == null) return;
            for (int i = 0; i < beliefs.Count; i++)
            {
                var b = beliefs[i];
                if (b == null) continue;
                var key = (b.observer, b.slot);
                if (!_byPair.TryGetValue(key, out var list))
                {
                    list = new List<BeliefChange>();
                    _byPair[key] = list;
                }
                list.Add(b);
            }
            foreach (var list in _byPair.Values)
                list.Sort((a, c) => a.t.CompareTo(c.t));
        }

        public BeliefClass At(int observer, int slot, float t)
        {
            if (!_byPair.TryGetValue((observer, slot), out var list) || list.Count == 0)
                return BeliefClass.Unknown;

            int lo = 0, hi = list.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (list[mid].t <= t + 0.001f) lo = mid + 1;
                else hi = mid;
            }
            if (lo == 0) return BeliefClass.Unknown;
            return Parse(list[lo - 1].@class);
        }

        public void FillSubject(int slot, float t, List<(int observer, BeliefClass cls)> dst)
        {
            dst.Clear();
            foreach (var kv in _byPair)
            {
                if (kv.Key.slot != slot) continue;
                var cls = At(kv.Key.observer, slot, t);
                if (cls == BeliefClass.Unknown) continue;
                dst.Add((kv.Key.observer, cls));
            }
            dst.Sort((a, b) => a.observer.CompareTo(b.observer));
        }

        public void FillObserver(int observer, float t, List<(int slot, BeliefClass cls)> dst)
        {
            dst.Clear();
            foreach (var kv in _byPair)
            {
                if (kv.Key.observer != observer) continue;
                var cls = At(observer, kv.Key.slot, t);
                if (cls == BeliefClass.Unknown) continue;
                dst.Add((kv.Key.slot, cls));
            }
            dst.Sort((a, b) => a.slot.CompareTo(b.slot));
        }

        public static BeliefClass Parse(string name) => name switch
        {
            "friendly" => BeliefClass.Friendly,
            "enemy" => BeliefClass.Enemy,
            "neutral" => BeliefClass.Neutral,
            "compromised" => BeliefClass.Compromised,
            _ => BeliefClass.Unknown,
        };

        public static string Label(BeliefClass cls) => cls switch
        {
            BeliefClass.Friendly => "friendly",
            BeliefClass.Enemy => "enemy",
            BeliefClass.Neutral => "neutral",
            BeliefClass.Compromised => "compromised",
            _ => "unknown",
        };

        /// <summary>Ground-truth class in the same vocabulary as a declaration,
        /// so a row can put "enemy" next to "civilian" without a click.</summary>
        public static string TruthLabel(EntityInfo info, bool compromisedNow)
        {
            if (info == null) return "unknown";
            if (compromisedNow) return "compromised";
            return info.Kind switch
            {
                EntityKind.Friendly => "friendly",
                EntityKind.Hostile => "hostile",
                EntityKind.Civilian => "civilian",
                EntityKind.Wreckage => "wreckage",
                _ => "unknown",
            };
        }

        public static bool Agrees(BeliefClass cls, EntityInfo info, bool compromisedNow)
        {
            if (info == null) return false;
            return cls switch
            {
                BeliefClass.Friendly => info.Kind == EntityKind.Friendly && !compromisedNow,
                BeliefClass.Enemy => info.Kind == EntityKind.Hostile,
                BeliefClass.Neutral => info.Kind == EntityKind.Civilian,
                BeliefClass.Compromised => compromisedNow,
                _ => false,
            };
        }
    }
}
