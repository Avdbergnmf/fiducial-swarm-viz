// Diagnostic overlay: range rings, motion arrows, radio links, picket ring.
// One IRunView, one line pool, one toggle panel. A new cue is a CueSpec row
// plus a few lines in Draw(). Numbers come from RunParams; unknown cues stay off.

using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

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
        Heading = 1 << 6,
        Links = 1 << 7,
        Picket = 1 << 8,
    }

    public sealed class CueOverlay : MonoBehaviour, IRunView
    {
        const CueMask DefaultMask = CueMask.Kill | CueMask.Velocity;
        const int RingVerts = 48;
        const float VelScale = 0.45f;   // metres of arrow per m/s
        const float AccelScale = 1.1f;  // metres of arrow per m/s^2
        const float HeadingLen = 5f;
        const float AccelNoise = 0.4f;  // hide jitter below this m/s^2

        [SerializeField] UIDocument uiDocument;

        static readonly CueSpec[] Specs =
        {
            new(CueMask.Kill, "Kill", "Hard collision radius (trace header). Sphere on the selected craft."),
            new(CueMask.Sense, "Sense", "Sensor disc from the brain's boot params. Ring at the craft's altitude."),
            new(CueMask.Comm, "Comm", "Radio range: boot params, or the longest recorded link if those are missing."),
            new(CueMask.Separate, "Sep", "Brain separation margin (kill × 4 today). The disc avoidance is supposed to hold."),
            new(CueMask.Velocity, "Vel", "Recorded velocity. Length scaled; not a command."),
            new(CueMask.Accel, "Acc", "Δv between recorded frames. Commanded accel is not in the trace."),
            new(CueMask.Heading, "Nose", "Attitude forward. Compare to Vel if the quaternion conversion looks wrong."),
            new(CueMask.Links, "Links", "Live radio links from the recording. With a selection, only that craft's links."),
            new(CueMask.Picket, "Picket", "Brain ring around the asset (asset radius + comm/2)."),
        };

        ViewerContext _ctx;
        EntityPicker _picker;
        EntityView[] _bySlot;
        LinePool _lines;
        Material _lineMat;

        VisualElement _root;
        VisualElement _panel;
        Button _openBtn;
        Label _footer;
        Button[] _chips;
        FloatingPanel _window;
        bool _uiWired;
        bool _panelOpen;
        int _wireAttempts;
        CueMask _mask = DefaultMask;

        public void Bind(ViewerContext ctx)
        {
            Unhook();
            _ctx = ctx;
            _bySlot = null;
            _mask = LoadMask();
            TryWireUi();
            Hook();
            RefreshChips();
            Refresh();
        }

        void OnEnable() => TryWireUi();
        void Start() => TryWireUi();
        void LateUpdate()
        {
            if (_uiWired) return;
            if (uiDocument != null && uiDocument.rootVisualElement == null) return;
            if (_wireAttempts > 8) return;
            _wireAttempts++;
            TryWireUi();
        }

        /// <summary>Scale-bar Cues button and the C key both land here.</summary>
        public void Toggle()
        {
            TryWireUi();
            if (_window == null) return;
            if (_panelOpen) _window.Hide();
            else OpenPanel();
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

        void TryWireUi()
        {
            if (uiDocument == null)
                uiDocument = GetComponent<UIDocument>();
            if (uiDocument == null) return;
            var root = uiDocument.rootVisualElement;
            if (root == null) return;
            if (_uiWired && _root == root) return;

            _root = root;
            _openBtn = UiQuery.Named<Button>(root, "cuesOpenBtn");
            _panel = UiQuery.Named<VisualElement>(root, "cuesPanel");
            var drag = UiQuery.Named<VisualElement>(root, "cuesDragHandle");
            var close = UiQuery.Named<Button>(root, "cuesCloseBtn");
            var chipRow = UiQuery.Named<VisualElement>(root, "cuesChipRow");
            _footer = UiQuery.Named<Label>(root, "cuesFooter");

            // Root can exist a frame before the tree is populated; wait rather
            // than marking wired and never attaching the click handler.
            if (_openBtn == null || _panel == null) return;

            _uiWired = true;
            _openBtn.tooltip = "Range rings and motion arrows   C";
            _openBtn.clicked += Toggle;

            BuildChips(chipRow);
            _window = new FloatingPanel();
            _window.Attach(_panel, drag, close);
            _window.Hidden += () =>
            {
                _panelOpen = false;
                SetOpenButton(false);
            };
            _panel.style.display = DisplayStyle.None;
            RefreshChips();
        }

        void BuildChips(VisualElement row)
        {
            _chips = new Button[Specs.Length];
            if (row == null) return;
            row.Clear();
            for (int i = 0; i < Specs.Length; i++)
            {
                int idx = i;
                var spec = Specs[i];
                var btn = new Button { text = spec.Label, tooltip = spec.Hint };
                btn.AddToClassList("filter-chip");
                btn.clicked += () => ToggleBit(Specs[idx].Bit);
                row.Add(btn);
                _chips[i] = btn;
            }
        }

        void OpenPanel()
        {
            _panelOpen = true;
            _window.Show();
            SetOpenButton(true);
            RefreshChips();
        }

        void SetOpenButton(bool on) =>
            _openBtn?.EnableInClassList("scene-state-toggle--open", on);

        void ToggleBit(CueMask bit)
        {
            if (!Available(bit)) return;
            _mask ^= bit;
            var settings = ViewerSettings.Load();
            settings.cueMask = (int)_mask;
            settings.Save();
            RefreshChips();
            Refresh();
        }

        static CueMask LoadMask()
        {
            int stored = ViewerSettings.Load().cueMask;
            return stored == 0 ? DefaultMask : (CueMask)stored;
        }

        bool On(CueMask bit) => (_mask & bit) != 0 && Available(bit);

        bool Available(CueMask bit)
        {
            var p = _ctx?.Run?.Params;
            return bit switch
            {
                CueMask.Kill => true,
                CueMask.Sense => p != null && p.Has(p.SenseRadius),
                CueMask.Comm => p != null && p.Has(p.CommDraw),
                CueMask.Separate => p != null && p.Has(p.SeparationMargin),
                CueMask.Picket => p != null && p.Has(p.RingRadius),
                CueMask.Links => _ctx?.Run?.Meta?.links != null && _ctx.Run.Meta.links.Count > 0,
                _ => true,
            };
        }

        void RefreshChips()
        {
            if (_chips == null) return;
            for (int i = 0; i < Specs.Length; i++)
            {
                var spec = Specs[i];
                var btn = _chips[i];
                if (btn == null) continue;
                bool avail = _ctx != null && Available(spec.Bit);
                btn.SetEnabled(avail);
                btn.EnableInClassList("filter-chip--on", avail && (_mask & spec.Bit) != 0);
                btn.tooltip = avail ? spec.Hint : spec.Hint + " — unknown in this run";
            }

            if (_footer != null)
                _footer.text = FooterText();
        }

        string FooterText()
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

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.cKey.wasPressedThisFrame) return;
            if (IsAnyTextFieldFocused()) return;
            Toggle();
        }

        bool IsAnyTextFieldFocused()
        {
            if (uiDocument == null || uiDocument.rootVisualElement == null) return false;
            var focused = uiDocument.rootVisualElement.focusController?.focusedElement;
            return focused is TextField || focused is TextInputBaseField<string>;
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
            if (On(CueMask.Heading))
                _lines.Arrow(pos, snap.Rotation * Vector3.forward * HeadingLen, new Color(0.45f, 0.55f, 1f, 1f), 0.12f);
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

        readonly struct CueSpec
        {
            public readonly CueMask Bit;
            public readonly string Label;
            public readonly string Hint;
            public CueSpec(CueMask bit, string label, string hint)
            {
                Bit = bit;
                Label = label;
                Hint = hint;
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
