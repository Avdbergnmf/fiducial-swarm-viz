// Diagnostic overlay: range rings, motion arrows, radio links, log pings
// (including commit / yield spans), picket ring, pick-volume selection spheres.
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
        // Retired chips (commit / yield are ping kinds). Bits stay so saved cueMask
        // values do not shift; LoadMask folds them into Pings.
        Intercept = 1 << 9,
        Yield = 1 << 10,
        Pings = 1 << 11,
        Hops = 1 << 12,
        Selection = 1 << 13,
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

            new(CueMask.Selection, "Selection",
                "The fat pick sphere around a craft, tinted with its class / belief colour. The checkboxes choose hover, selected, and unselected. Turning this cue off hides all three, including hover — that sphere used to stay up on its own and get in the way.",
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

            new(CueMask.Pings, "Pings",
                "A line when a log names another craft. Short verbs fade in 1.4 s; commit and yield stay up for the span. Kinds below can be switched off one by one. Click a line to select both ends; double-click opens the log.",
                "The log line itself. trk= is observer-local, so the other end is the craft this drone had declared (or the nearest alive of that class) at that time. Peer lines use n=/e=. commit / yield use CommitIndex / YieldIndex spans. gone / live use the brain id."),

            new(CueMask.Picket, "Picket ring",
                "The radius the brain holds around the asset. Sphere is the hex volume; otherwise a ring at picket altitude. Never both.",
                "params ring= for the radius and alt= for the height, centred on the asset position from the trace header."),

            new(CueMask.Aim, "Believed aim",
                "A kill-radius sphere at the pose the selected drone believed its target was at. It only refreshes on commit / near / ram log lines, then coasts on vn/ve and fades out over 1.4 s (same hold as short Pings) until the next sample. Ground truth is the real craft (and the commit ping).",
                "commit / near / ram: n=, e=, alt=, vn=, ve=. Those verbs are sparse by design (D3), not a per-tick track dump. Sphere radius is kill_radius from the trace. Select a committed friendly."),
        };

        /// <summary>Ping-line kinds, in the order the legend and Cues panel list them.</summary>
        public static readonly PingSpec[] PingKinds =
        {
            new(RelationKind.Call, "call",
                "A classification call naming another craft.",
                "call log. trk= is observer-local; the other end is the declared (or nearest) craft of that class."),
            new(RelationKind.Drop, "drop",
                "The track this drone just stopped naming.",
                "drop log. Compared to the previous belief set: the slot that vanished."),
            new(RelationKind.Wreck, "wreck",
                "Wreckage this drone has named.",
                "wreck log. Same association as a call."),
            new(RelationKind.Near, "near",
                "A close pass. Short fade.",
                "near log. class= plus n=/e= when present."),
            new(RelationKind.Ram, "ram",
                "The last metres of a ram. Short fade.",
                "ram log. Same association as near."),
            new(RelationKind.Duplicate, "duplicate",
                "Abort to the other interceptor on the same target.",
                "abort … duplicate. The other end is the overlapping commit."),
            new(RelationKind.Gone, "gone",
                "A neighbour this drone now treats as off the radio.",
                "gone id=. Direct brain id, not trk=."),
            new(RelationKind.Live, "live",
                "That neighbour is back.",
                "live id=. Direct brain id."),
            new(RelationKind.Commit, "commit",
                "Interceptor to its target for as long as the intercept is on. Clickable like the short pings.",
                "commit / abort / picket via CommitIndex. The other end is the hostile nearest the believed n=/e= pose (then Enemy declaration, then nearest alive hostile)."),
            new(RelationKind.Yield, "yield",
                "Picket to the interceptor while this craft sits in that intercept corridor.",
                "Reconstructed yield rows (params fsep=). The brain does not write this verb."),
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
        public const int PickHover = 1;
        public const int PickSelected = 2;
        public const int PickUnselected = 4;
        public const int PickDefault = PickHover | PickSelected;

        CueMask _mask = DefaultMask;
        CueMask _sphereMask = CueMask.Kill;
        int _pickShow = PickDefault;
        int _pingKinds = -1;
        bool _muted;

        public event Action MaskChanged;
        public CueMask Mask => _mask;
        public bool Muted => _muted;
        int _hoverPing = -1;

        public void SetHoverPing(int index)
        {
            if (_hoverPing == index) return;
            _hoverPing = index;
            Refresh();
        }

        /// <summary>
        /// Nearest visible ping line in screen space. Same hold / aliveness
        /// rules as drawing. <paramref name="pixels"/> is the distance to the
        /// segment, not to either drone.
        /// </summary>
        public bool TryPickPing(Camera cam, Vector2 mousePos, float slackPx,
            out RelationPing ping, out int index, out float pixels)
        {
            ping = default;
            index = -1;
            pixels = slackPx;
            if (cam == null || _ctx?.Clock == null || !On(CueMask.Pings)) return false;
            var snaps = _ctx?.State?.Entities;
            var pings = _ctx.Run?.Relations?.Pings;
            if (snaps == null || pings == null || pings.Count == 0) return false;
            float t = _ctx.Clock.Time;

            int best = -1;
            float bestD = slackPx;
            for (int i = 0; i < pings.Count; i++)
            {
                if (!PingKindOn(pings[i].Kind)) continue;
                if (!TryPingEnds(pings[i], snaps, t, out Vector3 pa, out Vector3 pb))
                    continue;
                Vector3 sa = cam.WorldToScreenPoint(pa);
                Vector3 sb = cam.WorldToScreenPoint(pb);
                if (sa.z < 0.1f || sb.z < 0.1f) continue;
                float d = DistPointToSegment(mousePos, new Vector2(sa.x, sa.y), new Vector2(sb.x, sb.y));
                if (d < bestD)
                {
                    bestD = d;
                    best = i;
                }
            }

            if (best < 0) return false;
            ping = pings[best];
            index = best;
            pixels = bestD;
            return true;
        }

        static float DistPointToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lenSq = ab.sqrMagnitude;
            if (lenSq < 1e-4f) return Vector2.Distance(p, a);
            float u = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lenSq);
            return Vector2.Distance(p, a + ab * u);
        }

        public bool PickHoverOn => (_pickShow & PickHover) != 0;
        public bool PickSelectedOn => (_pickShow & PickSelected) != 0;
        public bool PickUnselectedOn => (_pickShow & PickUnselected) != 0;

        /// <summary>Radius cues: hex volume or equatorial ring, never both.</summary>
        public static bool IsRadius(CueMask bit) =>
            bit is CueMask.Kill or CueMask.Sense or CueMask.Comm or CueMask.Separate or CueMask.Picket;

        /// <summary>Legend / detail hint: who the Selection cue is drawing on.</summary>
        public string SelectionScope
        {
            get
            {
                bool h = PickHoverOn, s = PickSelectedOn, u = PickUnselectedOn;
                if (s && u) return h ? "every living craft, and hover" : "every living craft";
                if (s && h) return "the selection, and hover";
                if (s) return "the selection";
                if (u && h) return "unselected craft, and hover";
                if (u) return "unselected craft";
                if (h) return "hover only";
                return "nothing — all three boxes are off";
            }
        }

        public void Bind(ViewerContext ctx)
        {
            Unhook();
            _ctx = ctx;
            _bySlot = null;
            _hoverPing = -1;
            _mask = LoadMask(out _pickShow, out _sphereMask, out _pingKinds);
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

        /// <summary>
        /// Hide every overlay without touching which chips are on. Show restores
        /// that same set. Session only — not written to settings.
        /// </summary>
        public void ToggleMute()
        {
            _muted = !_muted;
            Refresh();
            MaskChanged?.Invoke();
        }

        public void SetPickShow(int flag, bool on)
        {
            int next = on ? _pickShow | flag : _pickShow & ~flag;
            if (next == _pickShow) return;
            _pickShow = next;
            var settings = ViewerSettings.Load();
            settings.pickVolumeShow = _pickShow;
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

        public bool PingKindOn(RelationKind kind)
        {
            if (_pingKinds < 0) return true;
            return (_pingKinds & (1 << (int)kind)) != 0;
        }

        public void TogglePingKind(RelationKind kind)
        {
            int all = PingKindsMask();
            int cur = _pingKinds < 0 ? all : _pingKinds;
            cur ^= 1 << (int)kind;
            _pingKinds = cur == all ? -1 : cur;
            var settings = ViewerSettings.Load();
            settings.pingKindMask = _pingKinds < 0 ? null : _pingKinds;
            settings.Save();
            Refresh();
            MaskChanged?.Invoke();
        }

        static int PingKindsMask()
        {
            int m = 0;
            for (int i = 0; i < PingKinds.Length; i++)
                m |= 1 << (int)PingKinds[i].Kind;
            return m;
        }

        static CueMask LoadMask(out int pickShow, out CueMask spheres, out int pingKinds)
        {
            var settings = ViewerSettings.Load();
            CueMask mask = settings.cueMask == 0 ? DefaultMask : (CueMask)settings.cueMask;
            spheres = settings.cueSphereMask.HasValue
                ? (CueMask)settings.cueSphereMask.Value
                : CueMask.Kill;
            pingKinds = settings.pingKindMask ?? -1;

            if ((mask & (CueMask.Intercept | CueMask.Yield)) != 0)
            {
                mask |= CueMask.Pings;
                mask &= ~(CueMask.Intercept | CueMask.Yield);
                settings.cueMask = (int)mask;
                settings.Save();
            }

            // Old scale-bar toggle: pickVolumesOn meant "show on every craft".
            if (settings.pickVolumesOn)
            {
                mask |= CueMask.Selection;
                settings.pickVolumesOn = false;
                settings.pickVolumesAll = true;
                settings.cueMask = (int)mask;
                settings.Save();
            }

            if (settings.pickVolumeShow.HasValue)
            {
                pickShow = settings.pickVolumeShow.Value;
            }
            else
            {
                // Hover used to draw even with the cue off. Lift that into the
                // Selection chip so it can be turned off, and keep hover on.
                pickShow = PickHover;
                if ((mask & CueMask.Selection) != 0)
                    pickShow |= PickSelected;
                if (settings.pickVolumesAll)
                    pickShow |= PickSelected | PickUnselected;
                mask |= CueMask.Selection;
                settings.pickVolumeShow = pickShow;
                settings.cueMask = (int)mask;
                settings.Save();
            }
            return mask;
        }

        bool On(CueMask bit) => !_muted && (_mask & bit) != 0 && Available(bit);

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
                CueMask.Pings => _ctx.Run.Relations != null && _ctx.Run.Relations.Pings.Count > 0,
                CueMask.Aim => _ctx.Run.Aims != null && _ctx.Run.Aims.Count > 0,
                _ => true,
            };
        }

        /// <summary>
        /// What this run actually carries for one cue, so the explanation can quote a
        /// number instead of describing one. Empty when the run has nothing to quote.
        /// </summary>
        public string ValueText(CueMask bit)
        {
            if (bit == CueMask.Selection)
                return SelectionScope;
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
                CueMask.Pings => PingValue(_ctx.Run.Relations),
                CueMask.Aim => AimValue(_ctx.Run.Aims),
                _ => "",
            };
        }

        static string PingValue(RelationIndex rel)
        {
            if (rel == null) return "";
            int n = rel.Pings.Count;
            if (n <= 0) return "";
            return n == 1 ? "1 ping" : n + " pings";
        }

        public string PingKindValue(RelationKind kind)
        {
            var pings = _ctx?.Run?.Relations?.Pings;
            if (pings == null) return "";
            int n = 0;
            for (int i = 0; i < pings.Count; i++)
                if (pings[i].Kind == kind) n++;
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
            string distances = "";
            if (p != null)
            {
                string comm = !p.Has(p.CommDraw) ? ""
                    : p.CommFromLinks ? $"comm {p.CommDraw:G4} m (links)"
                    : $"comm {p.CommDraw:G4} m";
                string sense = p.Has(p.SenseRadius) ? $"sense {p.SenseRadius:G4} m" : "";
                string sep = p.Has(p.SeparationMargin) ? $"sep {p.SeparationMargin:G4} m" : "";
                string ring = p.Has(p.RingRadius) ? $"picket {p.RingRadius:G4} m" : "";
                string fsep = p.Has(p.FriendlyMargin) ? $"fsep {p.FriendlyMargin:G4} m" : "";
                distances = JoinNonEmpty(" · ", sense, comm, sep, fsep, ring);
            }
            if (_muted)
                return string.IsNullOrEmpty(distances)
                    ? "Cues hidden — Show restores the chips that were on."
                    : "Cues hidden · " + distances;
            return distances;
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

            bool selOn = On(CueMask.Selection);
            if (_bySlot != null)
            {
                for (int i = 0; i < _bySlot.Length; i++)
                {
                    _bySlot[i]?.SetKillCueEnabled(false);
                    _bySlot[i]?.SetPickVolumeCueEnabled(selOn, PickHoverOn, PickSelectedOn, PickUnselectedOn);
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
            if (On(CueMask.Pings))
                DrawPings(snaps, t);
            if (On(CueMask.Picket) && p.Has(p.RingRadius))
                DrawPicket(p, t);
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

        void DrawPings(System.Collections.Generic.IReadOnlyList<EntitySnapshot> snaps, float t)
        {
            var pings = _ctx.Run.Relations?.Pings;
            if (pings == null || pings.Count == 0) return;

            for (int i = 0; i < pings.Count; i++)
            {
                var ping = pings[i];
                if (!PingKindOn(ping.Kind)) continue;
                if (!TryPingEnds(ping, snaps, t, out Vector3 pa, out Vector3 pb)) continue;

                float age = t - ping.T;
                float fade = Mathf.Min(ping.Hold, RelationIndex.PingHold);
                float k = age <= ping.Hold - fade
                    ? 1f
                    : 1f - (age - (ping.Hold - fade)) / fade;
                if (k < 0.08f) k = 0.08f;
                bool hover = i == _hoverPing;
                if (hover) k = Mathf.Max(k, 0.85f);
                Color hue = Palette.Ping(ping.Kind);
                Color ca = Palette.A(hue, k);
                Color cb = Palette.A(hue, k * 0.35f);
                _lines.Segment(pa, pb, ca, cb, (hover ? 0.5f : 0.28f) * k, hover ? 0.16f : 0.08f);
            }
        }

        bool TryPingEnds(RelationPing ping,
            System.Collections.Generic.IReadOnlyList<EntitySnapshot> snaps, float t,
            out Vector3 pa, out Vector3 pb)
        {
            pa = default;
            pb = default;
            float age = t - ping.T;
            if (age < -0.02f || age > ping.Hold) return false;
            int a = ping.FromSlot;
            int b = ping.ToSlot;
            if ((uint)a >= (uint)snaps.Count || (uint)b >= (uint)snaps.Count) return false;
            bool ring = ping.Kind == RelationKind.Gone || ping.Kind == RelationKind.Live;
            if (!TryCuePos(snaps, a, out pa)) return false;
            if (!TryCuePos(snaps, b, out pb)) return false;
            if (!ring && (!snaps[a].Alive || !snaps[b].Alive)) return false;
            return true;
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

        void DrawPicket(RunParams p, float time)
        {
            var asset = _ctx.Run.Meta.asset;
            Vector3 c = Vector3.zero;
            if (asset?.position != null && asset.position.Length >= 3)
                c = new Vector3(asset.position[0], asset.position[1], asset.position[2]);
            float altitude = p.RingAltitudeAt(time);
            float radius = p.RingRadiusAt(time);
            c.y = p.Has(altitude) ? altitude : c.y;
            DrawRadius(CueMask.Picket, c, radius, Palette.A(Palette.Picket, 0.7f), 0.4f);
        }

        void DrawAim(System.Collections.Generic.IReadOnlyList<EntitySnapshot> snaps, float t)
        {
            var aims = _ctx.Run.Aims;
            if (aims == null || aims.Count == 0) return;
            var sel = _ctx.Selection;
            if (sel == null || sel.Count == 0) return;

            float r = _ctx.Run.KillRadius;
            if (r < 0.01f && _ctx.Run.Params != null)
                r = _ctx.Run.Params.KillRadius;
            if (r < 0.01f) return;

            float hold = AimIndex.Hold;

            for (int i = 0; i < sel.Count; i++)
            {
                int slot = sel.Slots[i];
                if ((uint)slot >= (uint)snaps.Count) continue;
                if (!snaps[slot].Alive) continue;
                int drone = _ctx.Run.Info(slot).drone_id;
                if (drone < 0) continue;
                if (!aims.TryAt(_ctx.Run, drone, t, out Vector3 ghost, out float age)) continue;
                if (age > hold) continue;

                float k = 1f - age / hold;
                if (k < 0.08f) k = 0.08f;
                // SpherePool keys materials by exact colour; keep this to a few steps.
                int q = Mathf.Clamp(Mathf.RoundToInt(k * 8f), 1, 8);
                float peak = Mathf.Max(0.02f, Palette.Aim.a);
                float a = peak * (q / 8f);
                _spheres.Show(ghost, r, Palette.A(Palette.Opaque(Palette.Aim), a));
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
            c.a = Palette.RadiusVolumeAlpha;
            return c;
        }

        /// <summary>Rebuild pooled volume materials from the live Palette.</summary>
        public void Restyle()
        {
            _spheres?.InvalidateMaterials();
            Refresh();
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

        public readonly struct PingSpec
        {
            public readonly RelationKind Kind;
            public readonly string Label;
            public readonly string Draws;
            public readonly string Source;

            public PingSpec(RelationKind kind, string label, string draws, string source)
            {
                Kind = kind;
                Label = label;
                Draws = draws;
                Source = source;
            }

            public Color Color => Palette.Opaque(Palette.Ping(Kind));
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
                CueMask.Selection or CueMask.Aim => "sphere",
                CueMask.Velocity or CueMask.Accel or CueMask.Attitude => "arrow",
                CueMask.Links or CueMask.Hops or CueMask.Pings => "line",
                _ => "",
            };
        }

        public string ShapeHint(CueSpec spec)
        {
            if (spec.Bit == CueMask.Selection)
                return SelectionScope;
            if (spec.Bit == CueMask.Aim)
                return "kill-radius sphere";
            if (IsRadius(spec.Bit))
                return SphereOn(spec.Bit) ? "sphere" : "ring";
            return spec.Shape;
        }

        public string DrawnAs(CueSpec spec)
        {
            if (spec.Bit == CueMask.Selection)
                return $"Drawn as a sphere on {SelectionScope}.";
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

            public void InvalidateMaterials()
            {
                foreach (var kv in _mats)
                {
                    if (kv.Value == null) continue;
                    if (Application.isPlaying) UnityEngine.Object.Destroy(kv.Value);
                    else UnityEngine.Object.DestroyImmediate(kv.Value);
                }
                _mats.Clear();
            }

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
