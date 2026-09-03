// Diagnostic overlay: range rings, motion arrows, radio links, intercepts,
// yield, log pings, picket ring, pick-volume ghosts.
//
// Drawing only. The Cues panel and its chips live in SceneStateView alongside
// Aircraft, Events, Score and Logs, so one component owns the windows instead of two
// racing to wire the same UIDocument. A new cue is a CueSpec row plus a few
// lines in DrawEntity. Radius cues share DrawRadius (equator ring, optional
// sphere). Numbers come from RunParams, and a cue whose number this
// run does not carry reports itself unavailable rather than guessing one.

using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace SwarmViewer
{
    [Flags]
    public enum CueMask
    {
        None = 0,
        Kill = 1 << 0,
        Sense = 1 << 1,
        Comm = 1 << 2,
        Separate = 1 << 3,
        Velocity = 1 << 4,
        Accel = 1 << 5,
        Attitude = 1 << 6,
        Links = 1 << 7,
        Picket = 1 << 8,
        Intercept = 1 << 9,
        Yield = 1 << 10,
        Pings = 1 << 11,
        Hops = 1 << 12,
        Ghosts = 1 << 13,
        Aim = 1 << 14,
    }

    public sealed class CueOverlay : MonoBehaviour, IRunView
    {
        const CueMask DefaultMask = CueMask.Kill | CueMask.Velocity;
        const int RingVerts = 48;
        const float VelScale = 0.45f;   // metres of arrow per m/s
        const float AccelScale = 1.1f;  // metres of arrow per m/s^2
        const float AttitudeLen = 5f;
        const float AccelNoise = 0.4f;  // hide jitter below this m/s^2

        /// <summary>
        /// Chip rows in the Cues panel, in display order. Every cue carries where its
        /// number came from, because a ring you cannot trace back to a recorded field
        /// is decoration, not evidence.
        /// </summary>
        public static readonly CueSpec[] Specs =
        {
            new(CueMask.Kill, "Kill radius",
                "A sphere of kill_radius around the selected craft's origin. A hit is the other craft's origin inside this sphere — not two shells touching (that would be 2× the real radius). Hex volume and ring are the same radius. Never both.",
                "The trace header field kill_radius. Collision is |p_a − p_b| < kill_radius at 100 Hz. The recording is 10 Hz, so a real ram's last frame is often still ~2 m apart."),

            new(CueMask.Ghosts, "Ghosts",
                "The fat pick sphere around a craft, tinted with its class / belief colour — the same mesh hover uses. The checkbox below chooses the selection or every living craft. Hover still shows a sphere under the pointer when this cue is off.",
                "The viewer's pick volume (SceneBuilder pickColliderRadius), not a recorded field. Colour is the airframe material in the current view mode."),

            new(CueMask.Sense, "Sense range",
                "How far a selected friendly can see. Sphere is the hex volume; otherwise a ring at the craft's altitude. Never both.",
                "The brain's boot params log line, sense=. If a run never logged it the cue stays unavailable rather than drawing a radius from memory."),

            new(CueMask.Comm, "Comm range",
                "How far a selected friendly can talk. Sphere is the hex volume; otherwise a ring at the craft's altitude. Never both.",
                "params comm= when the brain logged it. Otherwise measured — the longest distance any recorded link actually spanned — which is a floor on the true range, not the range itself. The footer marks that case."),

            new(CueMask.Separate, "Separation",
                "The spacing a selected friendly tries to keep from unknown traffic. Sphere is the hex volume; otherwise a ring. Known mates use a larger keep-out (fsep=) that this cue does not draw.",
                "params sep=, the unknown/civilian blend — four times the kill radius. Mates use fsep=, sized to arrest cruise with the lateral bound (D8)."),

            new(CueMask.Velocity, "Velocity",
                "An arrow along where the craft is actually going, 0.45 m of arrow per m/s. Selected and hovered craft.",
                "The velocity recorded in each trace frame. This is what the craft did, never what the brain asked for."),

            new(CueMask.Accel, "Acceleration",
                "An arrow along the change in velocity, 1.1 m per m/s². Below 0.4 m/s² it is treated as jitter and hidden.",
                "Differenced between recorded frames here in the viewer. The trace carries no commanded-accel column, so read this as a reconstruction after the fact, not as the command."),

            new(CueMask.Attitude, "Attitude",
                "A fixed 5 m arrow straight out of the nose. Selected and hovered craft.",
                "The attitude quaternion in the trace. Its disagreeing with Velocity during straight flight is how a wrong quaternion conversion gives itself away."),

            new(CueMask.Links, "Radio links",
                "A line between two friendlies who can hear each other right now. The radio is a broadcast: anyone in range may get the frame. These lines are that reachability, not a transcript of what crossed.",
                "links[] in the trace: drone ids a, b and a closed interval [t_start, t_end], from the simulator's add/remove deltas. Recorded edges, not distance inferred from comm_radius. Payloads are not in the recording."),

            new(CueMask.Hops, "Hops",
                "From the selected friendly, the shortest path along the radio graph. Hop 1 is a neighbour; hop 2+ is a drone this craft can only reach if someone forwards. Track reports hop this graph, hop-limited to 4, budget-checked. Select a drone.",
                "Reconstructed from links[] at this time. Not a transcript of which frames actually forwarded — that is the peer call lines (Pings) and the Disagree window. The example flood is not what this brain does; only TrackReport is relayed."),

            new(CueMask.Intercept, "Intercept",
                "A red line from a drone that has committed to the craft it is spending itself on, for as long as that intercept is still on. All active intercepts, not only the selection.",
                "Reconstructed from commit / abort / picket log lines. The other end is the hostile nearest the believed n=/e=/alt= pose on that commit (horizontal, so a 30 m picket is not preferred over a 40 m inbound). Fallback: this drone's Enemy declaration, then the nearest alive hostile."),

            new(CueMask.Yield, "Yield",
                "An amber line from a picket onto the remaining intercept flight it is sitting in. That is the drone stepping off so it does not cancel the interceptor's ProNav. The same moments appear as yield rows in Logs.",
                "Reconstructed from intercept spans plus params fsep=, then written into the log list as yield / yield clear so you can filter them. The brain does not write this verb. The keep-out is interceptor → predicted ram (cruise × time-to-meet, D17), not the whole red line to the hostile's current pose."),

            new(CueMask.Pings, "Pings",
                "A short fading line when a drone's log names another craft: a classification call, a drop, wreckage, a close pass, the last metres of a ram, a duplicate abort, or a neighbour presumed gone / back on the radio.",
                "The log line itself. trk= is observer-local, so the other end is the craft this drone had declared (or the nearest alive of that class) at that time — same association as Intercept. Peer (hearsay) lines use the n=/e= pose the brain associated by geometry. gone / live use the brain id directly. Visible for 1.4 s after the log."),

            new(CueMask.Picket, "Picket ring",
                "The radius the brain holds around the asset. Sphere is the hex volume; otherwise a ring at picket altitude. Never both.",
                "params ring= for the radius and alt= for the height, centred on the asset position from the trace header."),

            new(CueMask.Aim, "Believed aim",
                "A glowing ghost at the pose the selected drone believed its target was at, plus a line from the drone to that ghost. Ground truth is the real craft (and the red Intercept line). This is the offset: own fix plus the track it had associated, coasted on the logged velocity until the next commit/near/ram sample.",
                "commit / near / ram log lines: n=, e=, alt=, vn=, ve=. Available when those fields were logged. Select a committed friendly."),
        };

        /// <summary>Ping-line kinds, in the order the legend and Cues panel list them.</summary>
        public static readonly (RelationKind Kind, string Label)[] PingKinds =
        {
            (RelationKind.Call, "call"),
            (RelationKind.Drop, "drop"),
            (RelationKind.Wreck, "wreck"),
            (RelationKind.Near, "near"),
            (RelationKind.Ram, "ram"),
            (RelationKind.Duplicate, "duplicate"),
            (RelationKind.Gone, "gone"),
            (RelationKind.Live, "live"),
        };

        /// <summary>Hop-count colours, same order as Palette.Hop.</summary>
        public static readonly (int Hops, string Label)[] HopSteps =
        {
            (1, "1 hop"),
            (2, "2 hops"),
            (3, "3 hops"),
            (4, "4+ hops"),
        };

        ViewerContext _ctx;
        EntityPicker _picker;
        EntityView[] _bySlot;
        LinePool _lines;
        SpherePool _spheres;
        Material _lineMat;
        CueMask _mask = DefaultMask;
        CueMask _sphereMask = CueMask.Kill;
        bool _pickVolumesAll;

        public event Action MaskChanged;
        public CueMask Mask => _mask;
        public bool PickVolumesAll => _pickVolumesAll;

        /// <summary>Radius cues: hex volume or equatorial ring, never both.</summary>
        public static bool IsRadius(CueMask bit) =>
            bit is CueMask.Kill or CueMask.Sense or CueMask.Comm or CueMask.Separate or CueMask.Picket;

        /// <summary>Legend / detail hint: who the Ghosts cue is drawing on.</summary>
        public string GhostScope => _pickVolumesAll ? "every living craft" : "the selection";

        public void Bind(ViewerContext ctx)
        {
            Unhook();
            _ctx = ctx;
            _bySlot = null;
            _mask = LoadMask(out _pickVolumesAll, out _sphereMask);
            Hook();
            Refresh();
            MaskChanged?.Invoke();
        }

        void OnDestroy()
        {
            Unhook();
            if (_lineMat != null) Destroy(_lineMat);
            _lines?.Dispose();
            _spheres?.Dispose();
        }

        void Hook()
        {
            if (_ctx == null) return;
            if (_ctx.State != null) _ctx.State.Changed += Refresh;
            if (_ctx.Selection != null)
            {
                _ctx.Selection.OnSelectionChanged += OnSelection;
                _ctx.Selection.OnSelectionSetChanged += Refresh;
            }
        }

        void Unhook()
        {
            if (_ctx == null) return;
            if (_ctx.State != null) _ctx.State.Changed -= Refresh;
            if (_ctx.Selection != null)
            {
                _ctx.Selection.OnSelectionChanged -= OnSelection;
                _ctx.Selection.OnSelectionSetChanged -= Refresh;
            }
        }

        void OnSelection(int _) => Refresh();

        /// <summary>Flip one cue and remember it. A cue this run cannot draw is ignored.</summary>
        public void Toggle(CueMask bit)
        {
            if (!Available(bit)) return;
            _mask ^= bit;
            var settings = ViewerSettings.Load();
            settings.cueMask = (int)_mask;
            settings.Save();
            Refresh();
            MaskChanged?.Invoke();
        }

        public void SetPickVolumesAll(bool all)
        {
            if (_pickVolumesAll == all) return;
            _pickVolumesAll = all;
            var settings = ViewerSettings.Load();
            settings.pickVolumesAll = all;
            settings.Save();
            Refresh();
            MaskChanged?.Invoke();
        }

        public bool SphereOn(CueMask bit) => (_sphereMask & bit) != 0;

        public void SetSphere(CueMask bit, bool on)
        {
            if (!IsRadius(bit)) return;
            CueMask next = on ? _sphereMask | bit : _sphereMask & ~bit;
            if (next == _sphereMask) return;
            _sphereMask = next;
            var settings = ViewerSettings.Load();
            settings.cueSphereMask = (int)_sphereMask;
            settings.Save();
            Refresh();
            MaskChanged?.Invoke();
        }

        static CueMask LoadMask(out bool pickAll, out CueMask spheres)
        {
            var settings = ViewerSettings.Load();
            CueMask mask = settings.cueMask == 0 ? DefaultMask : (CueMask)settings.cueMask;
            pickAll = settings.pickVolumesAll;
            spheres = settings.cueSphereMask.HasValue
                ? (CueMask)settings.cueSphereMask.Value
                : CueMask.Kill;

            // Old scale-bar Ghosts toggle: pickVolumesOn meant "show on every craft".
            if (settings.pickVolumesOn)
            {
                mask |= CueMask.Ghosts;
                pickAll = true;
                settings.pickVolumesOn = false;
                settings.pickVolumesAll = true;
                settings.cueMask = (int)mask;
                settings.Save();
            }
            return mask;
        }

        bool On(CueMask bit) => (_mask & bit) != 0 && Available(bit);

        /// <summary>Switched on, availability aside. For the chip's lit state.</summary>
        public bool IsOn(CueMask bit) => (_mask & bit) != 0;

        /// <summary>False when this run carries no number for the cue.</summary>
        public bool Available(CueMask bit)
        {
            if (_ctx?.Run == null) return false;
            var p = _ctx.Run.Params;
            return bit switch
            {
                CueMask.Kill => p != null && p.Has(p.KillRadius),
                CueMask.Sense => p != null && p.Has(p.SenseRadius),
                CueMask.Comm => p != null && p.Has(p.CommDraw),
                CueMask.Separate => p != null && p.Has(p.SeparationMargin),
                CueMask.Picket => p != null && p.Has(p.RingRadius),
                CueMask.Links => _ctx.Run.Meta?.links != null && _ctx.Run.Meta.links.Count > 0,
                CueMask.Hops => _ctx.Run.Meta?.links != null && _ctx.Run.Meta.links.Count > 0,
                CueMask.Intercept => _ctx.Run.Commits != null && _ctx.Run.Commits.Spans.Count > 0,
                CueMask.Yield => YieldAvailable(),
                CueMask.Pings => _ctx.Run.Relations != null && _ctx.Run.Relations.Pings.Count > 0,
                CueMask.Aim => _ctx.Run.Aims != null && _ctx.Run.Aims.Count > 0,
                _ => true,
            };
        }

        bool YieldAvailable()
        {
            var p = _ctx.Run.Params;
            return p != null && p.Has(p.FriendlyMargin)
                && _ctx.Run.Commits != null && _ctx.Run.Commits.Spans.Count > 0;
        }

        /// <summary>
        /// What this run actually carries for one cue, so the explanation can quote a
        /// number instead of describing one. Empty when the run has nothing to quote.
        /// </summary>
        public string ValueText(CueMask bit)
        {
            if (bit == CueMask.Ghosts)
                return GhostScope;
            var p = _ctx?.Run?.Params;
            if (p == null) return "";
            return bit switch
            {
                CueMask.Kill => p.Has(p.KillRadius) ? $"{p.KillRadius:G4} m" : "",
                CueMask.Sense => p.Has(p.SenseRadius) ? $"{p.SenseRadius:G4} m" : "",
                CueMask.Comm => !p.Has(p.CommDraw) ? ""
                    : p.CommFromLinks ? $"{p.CommDraw:G4} m, measured from links" : $"{p.CommDraw:G4} m",
                CueMask.Separate => p.Has(p.SeparationMargin) ? $"{p.SeparationMargin:G4} m" : "",
                CueMask.Picket => p.Has(p.RingRadius) ? $"{p.RingRadius:G4} m" : "",
                CueMask.Links => _ctx.Run.Meta?.links != null && _ctx.Run.Meta.links.Count > 0
                    ? $"{_ctx.Run.Meta.links.Count} link records" : "",
                CueMask.Hops => HopValue(),
                CueMask.Intercept => InterceptValue(_ctx.Run.Commits),
                CueMask.Yield => YieldValue(_ctx.Run.Yields, YieldAvailable()),
                CueMask.Pings => PingValue(_ctx.Run.Relations),
                CueMask.Aim => AimValue(_ctx.Run.Aims),
                _ => "",
            };
        }

        static string InterceptValue(CommitIndex commits)
        {
            if (commits == null) return "";
            int n = commits.Spans.Count;
            if (n <= 0) return "";
            return n == 1 ? "1 intercept" : n + " intercepts";
        }

        static string YieldValue(YieldIndex yields, bool available)
        {
            if (!available) return "";
            int n = yields != null ? yields.Spans.Count : 0;
            if (n <= 0)
                return "0 yields — no picket sat in a remaining intercept corridor";
            return n == 1 ? "1 yield" : n + " yields";
        }

        static string PingValue(RelationIndex rel)
        {
            if (rel == null) return "";
            int n = rel.Pings.Count;
            if (n <= 0) return "";
            return n == 1 ? "1 ping" : n + " pings";
        }

        static string AimValue(AimIndex aims)
        {
            if (aims == null || aims.Count <= 0) return "";
            return aims.Count == 1 ? "1 believed pose" : aims.Count + " believed poses";
        }

        string HopValue()
        {
            int src = SelectedFriendlyDrone();
            if (src < 0) return "select a friendly";
            int max = BfsHops(_ctx.Clock != null ? _ctx.Clock.Time : 0f, src, null, null);
            if (max <= 0) return $"drone {src}, no other radio";
            return $"drone {src}, up to {max} hop" + (max == 1 ? "" : "s");
        }

        int SelectedFriendlyDrone()
        {
            var sel = _ctx?.Selection;
            var run = _ctx?.Run;
            if (sel == null || run == null || sel.Count == 0) return -1;
            for (int i = 0; i < sel.Count; i++)
            {
                int slot = sel.Slots[i];
                if ((uint)slot >= (uint)run.SlotCount) continue;
                int id = run.Info(slot).drone_id;
                if (id >= 0) return id;
            }
            return -1;
        }

        /// <summary>Shortest-path hops from src along current radio links.
        /// Optional hop[] / parent[] are filled for drawing. Returns max hop.</summary>
        int BfsHops(float t, int src, int[] hop, int[] parent)
        {
            var links = _ctx?.Run?.Meta?.links;
            if (links == null || src < 0) return 0;

            int cap = 64;
            hop ??= new int[cap];
            parent ??= new int[cap];
            if (src >= cap) return 0;
            for (int i = 0; i < cap; i++)
            {
                hop[i] = -1;
                parent[i] = -1;
            }
            hop[src] = 0;

            var q = new System.Collections.Generic.Queue<int>();
            q.Enqueue(src);
            int max = 0;
            while (q.Count > 0)
            {
                int a = q.Dequeue();
                for (int i = 0; i < links.Count; i++)
                {
                    var link = links[i];
                    if (t < link.t_start || t >= link.t_end) continue;
                    int b = link.a == a ? link.b : link.b == a ? link.a : -1;
                    if (b < 0 || b >= cap || hop[b] >= 0) continue;
                    hop[b] = hop[a] + 1;
                    parent[b] = a;
                    if (hop[b] > max) max = hop[b];
                    q.Enqueue(b);
                }
            }
            return max;
        }

        /// <summary>This run's cue distances, for the panel footer.</summary>
        public string FooterText()
        {
            var p = _ctx?.Run?.Params;
            if (p == null) return "";
            string comm = !p.Has(p.CommDraw) ? ""
                : p.CommFromLinks ? $"comm {p.CommDraw:G4} m (links)"
                : $"comm {p.CommDraw:G4} m";
            string sense = p.Has(p.SenseRadius) ? $"sense {p.SenseRadius:G4} m" : "";
            string sep = p.Has(p.SeparationMargin) ? $"sep {p.SeparationMargin:G4} m" : "";
            string ring = p.Has(p.RingRadius) ? $"picket {p.RingRadius:G4} m" : "";
            string fsep = p.Has(p.FriendlyMargin) ? $"fsep {p.FriendlyMargin:G4} m" : "";
            return JoinNonEmpty(" · ", sense, comm, sep, fsep, ring);
        }

        static string JoinNonEmpty(string sep, params string[] parts)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < parts.Length; i++)
            {
                if (string.IsNullOrEmpty(parts[i])) continue;
                if (sb.Length > 0) sb.Append(sep);
                sb.Append(parts[i]);
            }
            return sb.ToString();
        }

        void Refresh()
        {
            if (_ctx?.Run == null || _ctx.State == null)
            {
                _lines?.Begin();
                _lines?.End();
                _spheres?.Begin();
                _spheres?.End();
                return;
            }

            EnsureViews();
            EnsureLines();
            EnsureSpheres();
            var p = _ctx.Run.Params;
            var snaps = _ctx.State.Entities;
            float t = _ctx.Clock.Time;

            bool ghostsOn = On(CueMask.Ghosts);
            if (_bySlot != null)
            {
                for (int i = 0; i < _bySlot.Length; i++)
                {
                    _bySlot[i]?.SetKillCueEnabled(false);
                    _bySlot[i]?.SetPickVolumeCueEnabled(ghostsOn, _pickVolumesAll);
                }
            }

            _lines.Begin();
            _spheres.Begin();

            int hover = HoverSlot();
            for (int i = 0; i < _ctx.Selection.Count; i++)
                DrawEntity(snaps, _ctx.Selection.Slots[i], p, selected: true);
            if (hover >= 0 && !_ctx.Selection.IsSelected(hover))
                DrawEntity(snaps, hover, p, selected: false);
            DrawMarqueeHovers(snaps, p, hover);

            if (On(CueMask.Links))
                DrawLinks(snaps, t);
            if (On(CueMask.Hops))
                DrawHops(snaps, t);
            if (On(CueMask.Intercept))
                DrawIntercepts(snaps, t);
            if (On(CueMask.Yield))
                DrawYields(snaps, t);
            if (On(CueMask.Pings))
                DrawPings(snaps, t);
            if (On(CueMask.Picket) && p.Has(p.RingRadius))
                DrawPicket(p);
            if (On(CueMask.Aim))
                DrawAim(snaps, t);

            _spheres.End();
            _lines.End();
        }

        int HoverSlot()
        {
            if (_picker == null)
                _picker = FindAnyObjectByType<EntityPicker>();
            var view = _picker != null ? _picker.HoveredEntity : null;
            return view != null ? view.Slot : -1;
        }

        void DrawMarqueeHovers(System.Collections.Generic.IReadOnlyList<EntitySnapshot> snaps, RunParams p, int already)
        {
            if (_picker == null) return;
            var boxed = _picker.MarqueeHovered;
            if (boxed == null) return;
            for (int i = 0; i < boxed.Count; i++)
            {
                var view = boxed[i];
                if (view == null) continue;
                int slot = view.Slot;
                if (slot == already) continue;
                if (_ctx.Selection.IsSelected(slot)) continue;
                DrawEntity(snaps, slot, p, selected: false);
            }
        }

        void DrawEntity(System.Collections.Generic.IReadOnlyList<EntitySnapshot> snaps, int slot, RunParams p, bool selected)
        {
            if ((uint)slot >= (uint)snaps.Count) return;
            var snap = snaps[slot];
            if (!snap.Alive) return;

            var info = _ctx.Run.Info(slot);
            bool friendly = info.drone_id >= 0;
            Vector3 pos = snap.Position;

            if (selected && On(CueMask.Kill) && p.Has(p.KillRadius))
                DrawRadius(CueMask.Kill, pos, p.KillRadius, Palette.A(Palette.Opaque(Palette.Kill), 0.85f), 0.22f);

            if (selected && friendly)
            {
                if (On(CueMask.Sense) && p.Has(p.SenseRadius))
                    DrawRadius(CueMask.Sense, pos, p.SenseRadius, Palette.A(Palette.Sense, 0.85f), 0.35f);
                if (On(CueMask.Comm) && p.Has(p.CommDraw))
                    DrawRadius(CueMask.Comm, pos, p.CommDraw, Palette.A(Palette.Comm, 0.85f), 0.35f);
                if (On(CueMask.Separate) && p.Has(p.SeparationMargin))
                    DrawRadius(CueMask.Separate, pos, p.SeparationMargin, Palette.A(Palette.Separate, 0.95f), 0.22f);
            }

            if (On(CueMask.Velocity) && snap.Velocity.sqrMagnitude > 0.01f)
                _lines.Arrow(pos, snap.Velocity * VelScale, Palette.Velocity, 0.18f);
            if (On(CueMask.Accel) && snap.Acceleration.sqrMagnitude > AccelNoise * AccelNoise)
                _lines.Arrow(pos, snap.Acceleration * AccelScale, Palette.Accel, 0.18f);
            if (On(CueMask.Attitude))
                _lines.Arrow(pos, snap.Rotation * Vector3.forward * AttitudeLen, Palette.Attitude, 0.12f);
        }

        void DrawLinks(System.Collections.Generic.IReadOnlyList<EntitySnapshot> snaps, float t)
        {
            var links = _ctx.Run.Meta.links;
            var color = Palette.Links;
            bool filter = _ctx.Selection.Count > 0;
            for (int i = 0; i < links.Count; i++)
            {
                var link = links[i];
                if (t < link.t_start || t >= link.t_end) continue;
                int sa = _ctx.Run.SlotOfDrone(link.a);
                int sb = _ctx.Run.SlotOfDrone(link.b);
                if ((uint)sa >= (uint)snaps.Count || (uint)sb >= (uint)snaps.Count) continue;
                if (!snaps[sa].Alive || !snaps[sb].Alive) continue;
                if (filter && !_ctx.Selection.IsSelected(sa) && !_ctx.Selection.IsSelected(sb))
                    continue;
                _lines.Segment(snaps[sa].Position, snaps[sb].Position, color, 0.1f);
            }
        }

        void DrawHops(System.Collections.Generic.IReadOnlyList<EntitySnapshot> snaps, float t)
        {
            int src = SelectedFriendlyDrone();
            if (src < 0) return;

            int[] hop = new int[64];
            int[] parent = new int[64];
            BfsHops(t, src, hop, parent);

            for (int d = 0; d < hop.Length; d++)
            {
                if (hop[d] <= 0 || parent[d] < 0) continue;
                int sa = _ctx.Run.SlotOfDrone(parent[d]);
                int sb = _ctx.Run.SlotOfDrone(d);
                if ((uint)sa >= (uint)snaps.Count || (uint)sb >= (uint)snaps.Count) continue;
                if (!snaps[sa].Alive || !snaps[sb].Alive) continue;
                float width = hop[d] == 1 ? 0.16f : 0.22f;
                _lines.Segment(snaps[sa].Position, snaps[sb].Position, Palette.Hop(hop[d]), width);
            }
        }

        void DrawIntercepts(System.Collections.Generic.IReadOnlyList<EntitySnapshot> snaps, float t)
        {
            var spans = _ctx.Run.Commits?.Spans;
            if (spans == null || spans.Count == 0) return;
            var color = Palette.Intercept;
            for (int i = 0; i < spans.Count; i++)
            {
                var span = spans[i];
                if (!span.ActiveAt(t)) continue;
                int a = span.DroneSlot;
                int b = span.TargetSlot;
                if ((uint)a >= (uint)snaps.Count || (uint)b >= (uint)snaps.Count) continue;
                if (!snaps[a].Alive || !snaps[b].Alive) continue;
                _lines.Segment(snaps[a].Position, snaps[b].Position, color, 0.22f);
            }
        }

        void DrawYields(System.Collections.Generic.IReadOnlyList<EntitySnapshot> snaps, float t)
        {
            var spans = _ctx.Run.Yields?.Spans;
            if (spans == null || spans.Count == 0) return;
            var color = Palette.Yield;

            for (int i = 0; i < spans.Count; i++)
            {
                var span = spans[i];
                if (!span.ActiveAt(t)) continue;
                int s = span.PicketSlot;
                int a = span.InterceptorSlot;
                int b = span.TargetSlot;
                if ((uint)s >= (uint)snaps.Count || (uint)a >= (uint)snaps.Count || (uint)b >= (uint)snaps.Count)
                    continue;
                if (!snaps[s].Alive || !snaps[a].Alive || !snaps[b].Alive) continue;
                Vector3 end = YieldIndex.CorridorHorizon(
                    snaps[a].Position, snaps[b].Position, snaps[b].Velocity);
                if (!YieldIndex.ClosestOnSegmentXZ(snaps[s].Position, snaps[a].Position, end,
                        out Vector3 hit, out _))
                    continue;
                _lines.Segment(snaps[s].Position, hit, color, 0.16f);
            }
        }

        void DrawPings(System.Collections.Generic.IReadOnlyList<EntitySnapshot> snaps, float t)
        {
            var pings = _ctx.Run.Relations?.Pings;
            if (pings == null || pings.Count == 0) return;
            float hold = RelationIndex.PingHold;

            for (int i = 0; i < pings.Count; i++)
            {
                var ping = pings[i];
                float age = t - ping.T;
                if (age < -0.02f || age > hold) continue;
                int a = ping.FromSlot;
                int b = ping.ToSlot;
                if ((uint)a >= (uint)snaps.Count || (uint)b >= (uint)snaps.Count) continue;
                bool ring = ping.Kind == RelationKind.Gone || ping.Kind == RelationKind.Live;
                if (!TryCuePos(snaps, a, out Vector3 pa)) continue;
                if (!TryCuePos(snaps, b, out Vector3 pb)) continue;
                if (!ring && (!snaps[a].Alive || !snaps[b].Alive)) continue;

                float k = 1f - age / hold;
                if (k < 0.08f) k = 0.08f;
                Color ca = Palette.A(Palette.Ping(ping.Kind), k);
                Color cb = Palette.A(Palette.Ping(ping.Kind), k * 0.35f);
                _lines.Segment(pa, pb, ca, cb, 0.28f * k, 0.08f);
            }
        }

        bool TryCuePos(System.Collections.Generic.IReadOnlyList<EntitySnapshot> snaps,
            int slot, out Vector3 pos)
        {
            pos = default;
            if ((uint)slot >= (uint)snaps.Count) return false;
            if (snaps[slot].Alive)
            {
                pos = snaps[slot].Position;
                return true;
            }
            var info = _ctx.Run.Info(slot);
            if (info.last_frame < 0) return false;
            return _ctx.Run.Sample(info.last_frame, slot, out pos, out _, out _);
        }

        void DrawPicket(RunParams p)
        {
            var asset = _ctx.Run.Meta.asset;
            Vector3 c = Vector3.zero;
            if (asset?.position != null && asset.position.Length >= 3)
                c = new Vector3(asset.position[0], asset.position[1], asset.position[2]);
            c.y = p.Has(p.RingAltitude) ? p.RingAltitude : c.y;
            DrawRadius(CueMask.Picket, c, p.RingRadius, Palette.A(Palette.Picket, 0.7f), 0.4f);
        }

        void DrawAim(System.Collections.Generic.IReadOnlyList<EntitySnapshot> snaps, float t)
        {
            var aims = _ctx.Run.Aims;
            if (aims == null || aims.Count == 0) return;
            var sel = _ctx.Selection;
            if (sel == null || sel.Count == 0) return;

            var glow = Palette.A(Palette.Aim, 0.18f);
            var core = Palette.A(Palette.Opaque(Palette.Aim), 0.55f);
            var line = Palette.Opaque(Palette.Aim);

            for (int i = 0; i < sel.Count; i++)
            {
                int slot = sel.Slots[i];
                if ((uint)slot >= (uint)snaps.Count) continue;
                if (!snaps[slot].Alive) continue;
                int drone = _ctx.Run.Info(slot).drone_id;
                if (drone < 0) continue;
                if (!aims.TryAt(_ctx.Run, drone, t, out Vector3 ghost)) continue;
                _spheres.Show(ghost, 6f, glow);
                _spheres.Show(ghost, 2.2f, core);
                _lines.Segment(snaps[slot].Position, ghost, line, 0.2f);
            }
        }

        /// <summary>
        /// One visualisation: hex volume if Sphere is on, equatorial ring if not.
        /// Kill's old per-airframe mesh is left off; this pool is the one path.
        /// </summary>
        void DrawRadius(CueMask bit, Vector3 center, float radius, Color ring, float width)
        {
            if (SphereOn(bit))
                _spheres.Show(center, radius, VolumeColor(bit));
            else
                _lines.Circle(center, radius, ring, width);
        }

        static Color VolumeColor(CueMask bit)
        {
            if (bit == CueMask.Kill) return Palette.Kill;
            var c = Palette.Opaque(Palette.Cue(bit));
            c.a = 0.10f;
            return c;
        }

        void EnsureViews()
        {
            int n = _ctx.Run.SlotCount;
            bool stale = _bySlot == null || _bySlot.Length != n;
            if (!stale)
            {
                for (int i = 0; i < n; i++)
                {
                    if (_bySlot[i] == null)
                    {
                        stale = true;
                        break;
                    }
                }
            }
            if (!stale) return;

            _bySlot = new EntityView[n];
            var found = FindObjectsByType<EntityView>(FindObjectsInactive.Include);
            for (int i = 0; i < found.Length; i++)
            {
                int s = found[i].Slot;
                if ((uint)s < (uint)n) _bySlot[s] = found[i];
            }
        }

        void EnsureLines()
        {
            if (_lines != null) return;
            _lineMat = MakeLineMaterial();
            var root = new GameObject("CueLines");
            root.transform.SetParent(transform, false);
            _lines = new LinePool(root.transform, _lineMat);
        }

        void EnsureSpheres()
        {
            if (_spheres != null) return;
            var root = new GameObject("CueSpheres");
            root.transform.SetParent(transform, false);
            _spheres = new SpherePool(root.transform);
        }

        static Material MakeLineMaterial()
        {
            var shader = Shader.Find("Sprites/Default")
                         ?? Shader.Find("Universal Render Pipeline/Unlit")
                         ?? Shader.Find("Hidden/Internal-Colored");
            var mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            var white = Color.white;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", white);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", white);
            return mat;
        }

        public readonly struct CueSpec
        {
            public readonly CueMask Bit;
            public readonly string Label;
            /// <summary>What appears in the scene.</summary>
            public readonly string Draws;
            /// <summary>Which field it was read from, and what that field does or does not promise.</summary>
            public readonly string Source;

            public CueSpec(CueMask bit, string label, string draws, string source)
            {
                Bit = bit;
                Label = label;
                Draws = draws;
                Source = source;
            }

            /// <summary>Same hue the overlay and legend use. Always opaque for UI chrome.</summary>
            public Color Color => Palette.Opaque(Palette.Cue(Bit));

            /// <summary>What the overlay actually draws, for the legend hint and the Cues panel.</summary>
            public string Shape => Bit switch
            {
                CueMask.Kill or CueMask.Sense or CueMask.Comm or CueMask.Separate or CueMask.Picket => "ring",
                CueMask.Ghosts or CueMask.Aim => "sphere",
                CueMask.Velocity or CueMask.Accel or CueMask.Attitude => "arrow",
                CueMask.Links or CueMask.Hops or CueMask.Intercept or CueMask.Yield or CueMask.Pings => "line",
                _ => "",
            };
        }

        public string ShapeHint(CueSpec spec)
        {
            if (spec.Bit == CueMask.Ghosts)
                return PickVolumesAll ? "sphere on every craft" : "sphere on the selection";
            if (spec.Bit == CueMask.Aim)
                return "glowing ghost";
            if (IsRadius(spec.Bit))
                return SphereOn(spec.Bit) ? "sphere" : "ring";
            return spec.Shape;
        }

        public string DrawnAs(CueSpec spec)
        {
            if (spec.Bit == CueMask.Ghosts)
                return $"Drawn as a sphere on {GhostScope}.";
            string shape = ShapeHint(spec);
            if (string.IsNullOrEmpty(shape)) return "";
            bool an = "aeiou".IndexOf(char.ToLowerInvariant(shape[0])) >= 0;
            return $"Drawn as {(an ? "an" : "a")} {shape}.";
        }

        sealed class LinePool
        {
            readonly Transform _root;
            readonly Material _mat;
            readonly System.Collections.Generic.List<LineRenderer> _all = new();
            int _used;

            public LinePool(Transform root, Material mat)
            {
                _root = root;
                _mat = mat;
            }

            public void Begin() => _used = 0;

            public void End()
            {
                for (int i = 0; i < _all.Count; i++)
                    _all[i].enabled = i < _used;
            }

            public void Dispose()
            {
                if (_root != null) UnityEngine.Object.Destroy(_root.gameObject);
                _all.Clear();
            }

            public void Circle(Vector3 center, float radius, Color color, float width)
            {
                var lr = Next(color, width, loop: true);
                lr.positionCount = RingVerts;
                for (int i = 0; i < RingVerts; i++)
                {
                    float a = (i / (float)RingVerts) * Mathf.PI * 2f;
                    lr.SetPosition(i, center + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius));
                }
            }

            public void Arrow(Vector3 from, Vector3 delta, Color color, float width)
            {
                Vector3 tip = from + delta;
                Segment(from, tip, color, width);
                float len = delta.magnitude;
                if (len < 0.2f) return;
                Vector3 dir = delta / len;
                Vector3 side = Vector3.Cross(Vector3.up, dir);
                if (side.sqrMagnitude < 0.01f) side = Vector3.Cross(Vector3.right, dir);
                side.Normalize();
                float head = Mathf.Min(1.2f, len * 0.25f);
                Segment(tip, tip - dir * head + side * head * 0.4f, color, width);
                Segment(tip, tip - dir * head - side * head * 0.4f, color, width);
            }

            public void Segment(Vector3 a, Vector3 b, Color color, float width)
            {
                Segment(a, b, color, color, width, width);
            }

            public void Segment(Vector3 a, Vector3 b, Color ca, Color cb, float wa, float wb)
            {
                var lr = Next(ca, wa, loop: false);
                lr.endColor = cb;
                lr.endWidth = wb;
                lr.positionCount = 2;
                lr.SetPosition(0, a);
                lr.SetPosition(1, b);
            }

            LineRenderer Next(Color color, float width, bool loop)
            {
                LineRenderer lr;
                if (_used < _all.Count)
                    lr = _all[_used];
                else
                {
                    var go = new GameObject("cue");
                    go.transform.SetParent(_root, false);
                    lr = go.AddComponent<LineRenderer>();
                    lr.sharedMaterial = _mat;
                    lr.shadowCastingMode = ShadowCastingMode.Off;
                    lr.receiveShadows = false;
                    lr.useWorldSpace = true;
                    lr.alignment = LineAlignment.View;
                    lr.numCapVertices = 2;
                    lr.textureMode = LineTextureMode.Stretch;
                    _all.Add(lr);
                }

                _used++;
                lr.loop = loop;
                lr.startWidth = width;
                lr.endWidth = width;
                lr.startColor = color;
                lr.endColor = color;
                return lr;
            }
        }

        /// <summary>
        /// VolumeFixture spheres for radius cues. Same hex shell as kill / ghosts,
        /// pooled so a new radius never means a new mesh type.
        /// </summary>
        sealed class SpherePool
        {
            readonly Transform _root;
            readonly System.Collections.Generic.List<MeshRenderer> _all = new();
            readonly System.Collections.Generic.Dictionary<Color, Material> _mats = new();
            int _used;
            static Shader _shader;

            public SpherePool(Transform root)
            {
                _root = root;
            }

            public void Begin() => _used = 0;

            public void End()
            {
                for (int i = 0; i < _all.Count; i++)
                {
                    if (_all[i] != null)
                        _all[i].gameObject.SetActive(i < _used);
                }
            }

            public void Dispose()
            {
                if (_root != null) UnityEngine.Object.Destroy(_root.gameObject);
                _all.Clear();
                foreach (var kv in _mats)
                    if (kv.Value != null) UnityEngine.Object.Destroy(kv.Value);
                _mats.Clear();
            }

            public void Show(Vector3 center, float radius, Color color)
            {
                var rend = Next();
                var t = rend.transform;
                t.position = center;
                t.rotation = Quaternion.identity;
                t.localScale = Vector3.one * (radius * 2f);
                rend.sharedMaterial = MaterialOf(color);
            }

            MeshRenderer Next()
            {
                MeshRenderer rend;
                if (_used < _all.Count)
                    rend = _all[_used];
                else
                {
                    var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    go.name = "cue-sphere";
                    go.transform.SetParent(_root, false);
                    var col = go.GetComponent<Collider>();
                    if (col != null) UnityEngine.Object.Destroy(col);
                    rend = go.GetComponent<MeshRenderer>();
                    rend.shadowCastingMode = ShadowCastingMode.Off;
                    rend.receiveShadows = false;
                    VolumeFixtureRegistry.Ensure();
                    _all.Add(rend);
                }

                _used++;
                rend.gameObject.SetActive(true);
                return rend;
            }

            Material MaterialOf(Color color)
            {
                if (_mats.TryGetValue(color, out var mat) && mat != null)
                    return mat;

                if (_shader == null)
                    _shader = Shader.Find("Custom/VolumeFixture");
                mat = new Material(_shader != null ? _shader : Shader.Find("Sprites/Default"))
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    name = "CueRadius",
                };
                Palette.TintVolume(mat, color);
                _mats[color] = mat;
                return mat;
            }
        }
    }
}
