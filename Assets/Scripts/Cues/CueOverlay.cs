// Diagnostic overlay: range rings, motion arrows, radio links, picket ring.
//
// Drawing only. The Cues panel and its chips live in SceneStateView alongside
// Aircraft, Events and Logs, so one component owns the windows instead of two
// racing to wire the same UIDocument. A new cue is a CueSpec row plus a few
// lines in DrawEntity. Numbers come from RunParams, and a cue whose number this
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
                "A sphere at the hard collision distance around each selected craft. Two spheres touching is a hit.",
                "The trace header field kill_radius. This is the simulator's rule, not something the brain chose."),

            new(CueMask.Sense, "Sense range",
                "A flat ring at the craft's own altitude: how far it can see. Selected friendlies only.",
                "The brain's boot params log line, sense=. If a run never logged it the cue stays unavailable rather than drawing a radius from memory."),

            new(CueMask.Comm, "Comm range",
                "A flat ring at the craft's altitude: how far it can talk. Selected friendlies only.",
                "params comm= when the brain logged it. Otherwise measured — the longest distance any recorded link actually spanned — which is a floor on the true range, not the range itself. The footer marks that case."),

            new(CueMask.Separate, "Separation",
                "A ring at the spacing the brain tries to keep from unknown traffic. Selected friendlies only. Known mates use a larger keep-out (fsep=) that this cue does not draw.",
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
                "A line between two friendlies who can hear each other right now. The radio is a broadcast: anyone in range may get the frame. These lines are that reachability, not a transcript of what crossed, and not a relay graph — this brain does not forward. With a selection, only that craft's links.",
                "links[] in the trace: drone ids a, b and a closed interval [t_start, t_end], from the simulator's add/remove deltas. Recorded edges, not distance inferred from comm_radius. Payloads are not in the recording; heartbeats, hostile reports and claims stay inside the brains."),

            new(CueMask.Picket, "Picket ring",
                "The ring the brain holds around the asset.",
                "params ring= for the radius and alt= for the height, centred on the asset position from the trace header."),
        };

        ViewerContext _ctx;
        EntityPicker _picker;
        EntityView[] _bySlot;
        LinePool _lines;
        Material _lineMat;
        CueMask _mask = DefaultMask;

        public void Bind(ViewerContext ctx)
        {
            Unhook();
            _ctx = ctx;
            _bySlot = null;
            _mask = LoadMask();
            Hook();
            Refresh();
        }

        void OnDestroy()
        {
            Unhook();
            if (_lineMat != null) Destroy(_lineMat);
            _lines?.Dispose();
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
        }

        static CueMask LoadMask()
        {
            int stored = ViewerSettings.Load().cueMask;
            return stored == 0 ? DefaultMask : (CueMask)stored;
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
                CueMask.Links => _ctx.Run.Meta?.links is { Count: > 0 },
                _ => true,
            };
        }

        /// <summary>
        /// What this run actually carries for one cue, so the explanation can quote a
        /// number instead of describing one. Empty when the run has nothing to quote.
        /// </summary>
        public string ValueText(CueMask bit)
        {
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
                CueMask.Links => _ctx.Run.Meta?.links is { Count: > 0 } l ? $"{l.Count} link records" : "",
                _ => "",
            };
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
            return JoinNonEmpty(" · ", sense, comm, sep, ring);
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
                return;
            }

            EnsureViews();
            EnsureLines();
            var p = _ctx.Run.Params;
            var snaps = _ctx.State.Entities;
            float t = _ctx.Clock.Time;

            bool killOn = On(CueMask.Kill);
            if (_bySlot != null)
            {
                for (int i = 0; i < _bySlot.Length; i++)
                    _bySlot[i]?.SetKillCueEnabled(killOn);
            }

            _lines.Begin();

            int hover = HoverSlot();
            for (int i = 0; i < _ctx.Selection.Count; i++)
                DrawEntity(snaps, _ctx.Selection.Slots[i], p, selected: true);
            if (hover >= 0 && !_ctx.Selection.IsSelected(hover))
                DrawEntity(snaps, hover, p, selected: false);

            if (On(CueMask.Links))
                DrawLinks(snaps, t);
            if (On(CueMask.Picket) && p.Has(p.RingRadius))
                DrawPicket(p);

            _lines.End();
        }

        int HoverSlot()
        {
            if (_picker == null)
                _picker = FindAnyObjectByType<EntityPicker>();
            var view = _picker != null ? _picker.HoveredEntity : null;
            return view != null ? view.Slot : -1;
        }

        void DrawEntity(System.Collections.Generic.IReadOnlyList<EntitySnapshot> snaps, int slot, RunParams p, bool selected)
        {
            if ((uint)slot >= (uint)snaps.Count) return;
            var snap = snaps[slot];
            if (!snap.Alive) return;

            var info = _ctx.Run.Info(slot);
            bool friendly = info.drone_id >= 0;
            Vector3 pos = snap.Position;

            if (selected && friendly)
            {
                if (On(CueMask.Sense) && p.Has(p.SenseRadius))
                    _lines.Circle(pos, p.SenseRadius, new Color(0.35f, 0.85f, 1f, 0.85f), 0.35f);
                if (On(CueMask.Comm) && p.Has(p.CommDraw))
                    _lines.Circle(pos, p.CommDraw, new Color(0.72f, 0.45f, 1f, 0.85f), 0.35f);
                if (On(CueMask.Separate) && p.Has(p.SeparationMargin))
                    _lines.Circle(pos, p.SeparationMargin, new Color(1f, 0.55f, 0.15f, 0.95f), 0.22f);
            }

            if (On(CueMask.Velocity) && snap.Velocity.sqrMagnitude > 0.01f)
                _lines.Arrow(pos, snap.Velocity * VelScale, new Color(0.55f, 0.95f, 1f, 1f), 0.18f);
            if (On(CueMask.Accel) && snap.Acceleration.sqrMagnitude > AccelNoise * AccelNoise)
                _lines.Arrow(pos, snap.Acceleration * AccelScale, new Color(1f, 0.88f, 0.2f, 1f), 0.18f);
            if (On(CueMask.Attitude))
                _lines.Arrow(pos, snap.Rotation * Vector3.forward * AttitudeLen, new Color(0.45f, 0.55f, 1f, 1f), 0.12f);
        }

        void DrawLinks(System.Collections.Generic.IReadOnlyList<EntitySnapshot> snaps, float t)
        {
            var links = _ctx.Run.Meta.links;
            var color = new Color(0.35f, 0.9f, 0.45f, 0.55f);
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

        void DrawPicket(RunParams p)
        {
            var asset = _ctx.Run.Meta.asset;
            Vector3 c = Vector3.zero;
            if (asset?.position != null && asset.position.Length >= 3)
                c = new Vector3(asset.position[0], asset.position[1], asset.position[2]);
            c.y = p.Has(p.RingAltitude) ? p.RingAltitude : c.y;
            _lines.Circle(c, p.RingRadius, new Color(0.85f, 0.85f, 0.9f, 0.7f), 0.4f);
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
                var lr = Next(color, width, loop: false);
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
    }
}
