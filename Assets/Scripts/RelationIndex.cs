// Short-lived drone → subject lines reconstructed from log verbs that name a track.
//
// Same association problem as CommitIndex: trk= is observer-local. The other
// end is the craft this drone had declared (or the nearest alive of that
// class) at the log time. Drawing lives in CueOverlay.

using System.Collections.Generic;
using UnityEngine;

namespace SwarmViewer
{
    public enum RelationKind : byte
    {
        Call,
        Drop,
        Wreck,
        Near,
        Ram,
        Duplicate,
        Gone,
        Live,
    }

    public readonly struct RelationPing
    {
        public readonly int FromSlot;
        public readonly int ToSlot;
        public readonly float T;
        public readonly RelationKind Kind;
        public readonly LogLine Line;

        public RelationPing(int fromSlot, int toSlot, float t, RelationKind kind, LogLine line)
        {
            FromSlot = fromSlot;
            ToSlot = toSlot;
            T = t;
            Kind = kind;
            Line = line;
        }
    }

    public sealed class RelationIndex
    {
        public const float PingHold = 1.4f;

        readonly List<RelationPing> _pings = new();
        readonly List<(int slot, BeliefClass cls)> _declared = new();
        readonly List<(int slot, BeliefClass cls)> _declaredBefore = new();

        public IReadOnlyList<RelationPing> Pings => _pings;

        public RelationIndex(RunData run)
        {
            if (run?.Meta?.logs == null) return;

            for (int i = 0; i < run.Meta.logs.Count; i++)
            {
                var line = run.Meta.logs[i];
                if (line == null || string.IsNullOrEmpty(line.text)) continue;
                string verb = LogPhrase.Verb(line.text);
                int from = run.SlotOfDrone(line.drone);
                if (from < 0) continue;

                if (verb == "abort")
                {
                    if (line.text.IndexOf(" duplicate", System.StringComparison.Ordinal) < 0)
                        continue;
                    int other = OtherInterceptor(run, from, line.t);
                    if (other >= 0)
                        _pings.Add(new RelationPing(from, other, line.t, RelationKind.Duplicate, line));
                    continue;
                }

                if (verb == "gone" || verb == "live")
                {
                    int id = LogPhrase.IntField(line.text, "id=");
                    int mate = run.SlotOfDrone(id);
                    if (mate >= 0 && mate != from)
                        _pings.Add(new RelationPing(from, mate, line.t,
                            verb == "gone" ? RelationKind.Gone : RelationKind.Live, line));
                    continue;
                }

                RelationKind kind;
                if (verb == "call") kind = RelationKind.Call;
                else if (verb == "drop") kind = RelationKind.Drop;
                else if (verb == "wreck") kind = RelationKind.Wreck;
                else if (verb == "near") kind = RelationKind.Near;
                else if (verb == "ram") kind = RelationKind.Ram;
                else continue;

                int to;
                if (line.text.IndexOf(" peer", System.StringComparison.Ordinal) >= 0)
                {
                    to = ResolvePeer(run, from, line.t, line.text);
                }
                else
                {
                    to = kind == RelationKind.Drop
                        ? ResolveDropped(run, line.drone, from, line.t)
                        : ResolveNamed(run, line.drone, from, line.t, ClassToken(verb, line.text));
                }
                if (to < 0 || to == from) continue;
                _pings.Add(new RelationPing(from, to, line.t, kind, line));
            }
        }

        int ResolvePeer(RunData run, int fromSlot, float t, string raw)
        {
            if (!LogPhrase.TryNumber(raw, "n=", out float n) ||
                !LogPhrase.TryNumber(raw, "e=", out float e))
                return -1;
            float frame = run.FrameOf(t);
            float down = 0f;
            if (LogPhrase.TryNumber(raw, "alt=", out float alt)) down = -alt;
            Vector3 at = SwarmCoord.Ned(n, e, down);
            float capSq = 40f * 40f;
            int best = -1;
            float bestD = capSq;
            for (int s = 0; s < run.SlotCount; s++)
            {
                if (s == fromSlot) continue;
                if (!run.Sample(frame, s, out Vector3 p, out _, out _)) continue;
                float dx = p.x - at.x;
                float dz = p.z - at.z;
                float d = dx * dx + dz * dz;
                if (d < bestD) { bestD = d; best = s; }
            }
            return best;
        }

        int ResolveNamed(RunData run, int droneId, int fromSlot, float t, string cls)
        {
            float frame = run.FrameOf(t);
            if (!run.Sample(frame, fromSlot, out Vector3 origin, out _, out _))
                return -1;

            BeliefClass want = ParseLogClass(cls);
            if (want != BeliefClass.Unknown)
            {
                run.Beliefs.FillObserver(droneId, t, _declared);
                int hit = NearestDeclared(run, frame, origin, want);
                if (hit >= 0) return hit;
            }

            EntityKind kind = KindFromLogClass(cls);
            if (kind == EntityKind.Unknown) return -1;
            return NearestKind(run, frame, origin, kind);
        }

        int ResolveDropped(RunData run, int droneId, int fromSlot, float t)
        {
            float frame = run.FrameOf(t);
            if (!run.Sample(frame, fromSlot, out Vector3 origin, out _, out _))
                return -1;

            run.Beliefs.FillObserver(droneId, t - 0.15f, _declaredBefore);
            run.Beliefs.FillObserver(droneId, t, _declared);

            int best = -1;
            float bestD = float.PositiveInfinity;
            for (int i = 0; i < _declaredBefore.Count; i++)
            {
                int slot = _declaredBefore[i].slot;
                bool still = false;
                for (int j = 0; j < _declared.Count; j++)
                {
                    if (_declared[j].slot == slot) { still = true; break; }
                }
                if (still) continue;
                if (!run.Sample(frame, slot, out Vector3 p, out _, out _)) continue;
                float d = (p - origin).sqrMagnitude;
                if (d < bestD) { bestD = d; best = slot; }
            }
            return best;
        }

        int NearestDeclared(RunData run, float frame, Vector3 origin, BeliefClass want)
        {
            int best = -1;
            float bestD = float.PositiveInfinity;
            for (int i = 0; i < _declared.Count; i++)
            {
                if (_declared[i].cls != want) continue;
                int s = _declared[i].slot;
                if (!run.Sample(frame, s, out Vector3 p, out _, out _)) continue;
                float d = (p - origin).sqrMagnitude;
                if (d < bestD) { bestD = d; best = s; }
            }
            return best;
        }

        static int NearestKind(RunData run, float frame, Vector3 origin, EntityKind kind)
        {
            int best = -1;
            float bestD = float.PositiveInfinity;
            for (int s = 0; s < run.SlotCount; s++)
            {
                if (run.Info(s).Kind != kind) continue;
                if (!run.Sample(frame, s, out Vector3 p, out _, out _)) continue;
                float d = (p - origin).sqrMagnitude;
                if (d < bestD) { bestD = d; best = s; }
            }
            return best;
        }

        static int OtherInterceptor(RunData run, int fromSlot, float t)
        {
            var spans = run.Commits?.Spans;
            if (spans == null) return -1;

            int target = -1;
            for (int i = 0; i < spans.Count; i++)
            {
                var s = spans[i];
                if (s.DroneSlot != fromSlot) continue;
                if (t + 0.05f < s.T0 || t - 0.05f > s.T1) continue;
                target = s.TargetSlot;
                break;
            }
            if (target < 0) return -1;

            for (int i = 0; i < spans.Count; i++)
            {
                var s = spans[i];
                if (s.TargetSlot != target || s.DroneSlot == fromSlot) continue;
                if (s.ActiveAt(t) || (t >= s.T0 && t <= s.T1 + 0.2f))
                    return s.DroneSlot;
            }
            return -1;
        }

        static string ClassToken(string verb, string raw)
        {
            if (verb == "near" || verb == "ram")
            {
                int i = raw.IndexOf("class=", System.StringComparison.Ordinal);
                if (i < 0) return "";
                i += 6;
                int end = raw.IndexOf(' ', i);
                return end < 0 ? raw.Substring(i) : raw.Substring(i, end - i);
            }

            // "call trk=N hostile miss=..."
            var tok = raw.Split(' ');
            return tok.Length > 2 ? tok[2] : "";
        }

        static BeliefClass ParseLogClass(string cls) => cls switch
        {
            "hostile" => BeliefClass.Enemy,
            "enemy" => BeliefClass.Enemy,
            "civilian" => BeliefClass.Neutral,
            "friendly" => BeliefClass.Friendly,
            "compromised" => BeliefClass.Compromised,
            _ => BeliefClass.Unknown,
        };

        static EntityKind KindFromLogClass(string cls) => cls switch
        {
            "hostile" => EntityKind.Hostile,
            "enemy" => EntityKind.Hostile,
            "civilian" => EntityKind.Civilian,
            "friendly" => EntityKind.Friendly,
            "wreck" => EntityKind.Wreckage,
            "wreckage" => EntityKind.Wreckage,
            _ => EntityKind.Unknown,
        };
    }
}
