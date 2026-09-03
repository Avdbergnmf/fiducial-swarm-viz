// On-screen colour key and shortcut strip. The one sentence at the top is the
// view-mode contract; swatches underneath are the same hues the scene uses.

using UnityEngine;
using UnityEngine.UIElements;

namespace SwarmViewer
{
    public sealed class LegendView : MonoBehaviour, IRunView
    {
        [SerializeField] UIDocument uiDocument;

        ViewerContext _ctx;
        CueOverlay _cues;
        VisualElement _root;
        Label _mode;
        Label _sentence;
        VisualElement _body;
        VisualElement _craft;
        VisualElement _scene;
        VisualElement _cuesBox;
        Label _cuesTitle;
        VisualElement _keys;
        Label _jlMeaning;
        Button _toggle;
        bool _wired;
        bool _collapsed;

        public void Bind(ViewerContext ctx)
        {
            Unhook();
            _ctx = ctx;
            Hook();
            TryWire();
            Rebuild();
        }

        void OnEnable() => TryWire();
        void Start() => TryWire();
        void OnDestroy() => Unhook();

        public void ToggleCollapsed()
        {
            _collapsed = !_collapsed;
            var settings = ViewerSettings.Load();
            if (settings.legendCollapsed != _collapsed)
            {
                settings.legendCollapsed = _collapsed;
                settings.Save();
            }
            ApplyCollapsed();
        }

        void Hook()
        {
            if (_ctx?.Selection != null)
            {
                _ctx.Selection.OnViewModeChanged += OnMode;
                _ctx.Selection.OnObserverChanged += OnObserver;
            }
            if (_ctx?.Clock != null)
                _ctx.Clock.OnStepChanged += OnStepChanged;
            HookCues();
        }

        void Unhook()
        {
            if (_ctx?.Selection != null)
            {
                _ctx.Selection.OnViewModeChanged -= OnMode;
                _ctx.Selection.OnObserverChanged -= OnObserver;
            }
            if (_ctx?.Clock != null)
                _ctx.Clock.OnStepChanged -= OnStepChanged;
            if (_cues != null)
                _cues.MaskChanged -= Rebuild;
        }

        void OnStepChanged(float _) => UpdateJlMeaning();

        void OnMode(ViewMode _) => Rebuild();
        void OnObserver(int _) => Rebuild();

        void HookCues()
        {
            if (_cues != null)
                _cues.MaskChanged -= Rebuild;
            _cues = GetComponent<CueOverlay>();
            if (_cues == null)
            {
                var found = FindObjectsByType<CueOverlay>(FindObjectsInactive.Include);
                if (found != null && found.Length > 0) _cues = found[0];
            }
            if (_cues != null)
                _cues.MaskChanged += Rebuild;
        }

        void TryWire()
        {
            if (uiDocument == null)
                uiDocument = GetComponent<UIDocument>();
            if (uiDocument == null) return;
            var tree = uiDocument.rootVisualElement;
            if (tree == null) return;

            _root = tree.Q("legendRoot");
            if (_root == null) return;

            if (!_wired)
            {
                _mode = tree.Q<Label>("legendMode");
                _sentence = tree.Q<Label>("legendSentence");
                _body = tree.Q("legendBody");
                _craft = tree.Q("legendCraft");
                _scene = tree.Q("legendScene");
                _cuesBox = tree.Q("legendCues");
                _cuesTitle = tree.Q<Label>("legendCuesTitle");
                _keys = tree.Q("legendKeys");
                _toggle = tree.Q<Button>("legendToggleBtn");
                if (_toggle != null)
                    _toggle.clicked += ToggleCollapsed;
                _collapsed = ViewerSettings.Load().legendCollapsed;
                _wired = true;
            }

            HookCues();
            ApplyCollapsed();
            Rebuild();
        }

        void ApplyCollapsed()
        {
            if (_body != null)
                _body.style.display = _collapsed ? DisplayStyle.None : DisplayStyle.Flex;
            if (_toggle != null)
            {
                _toggle.text = _collapsed ? "+" : "–";
                _toggle.tooltip = _collapsed
                    ? "Show the colour key. ? also toggles."
                    : "Hide the colour key. ? also toggles.";
            }
            if (_root != null)
                _root.EnableInClassList("legend--collapsed", _collapsed);
        }

        void Rebuild()
        {
            if (!_wired || _root == null) return;

            bool belief = _ctx?.Selection != null && _ctx.Selection.BeliefViewOn;
            int observer = _ctx?.Selection != null ? _ctx.Selection.Observer : -1;
            _root.EnableInClassList("legend--belief", belief);

            if (_mode != null)
            {
                _mode.text = belief
                    ? (observer >= 0 ? $"Belief · drone {observer}" : "Belief")
                    : "Ground truth";
            }

            if (_sentence != null)
            {
                _sentence.text = belief
                    ? (observer >= 0
                        ? $"Bodies are what drone {observer} declared. Grey has not been named yet."
                        : "Bodies are what the selected drone declared. Grey has not been named yet.")
                    : "Bodies are what they actually are. Magenta is an insider.";
            }

            FillCraft(belief);
            FillScene(belief);
            FillCues();
            FillKeys();
        }

        void FillCraft(bool belief)
        {
            if (_craft == null) return;
            _craft.Clear();
            if (belief)
            {
                Row(_craft, Palette.Friendly, "Friendly", "declared mate, or this observer");
                Row(_craft, Palette.Hostile, "Hostile", "declared enemy");
                Row(_craft, Palette.Civilian, "Civilian", "declared civilian");
                Row(_craft, Palette.Compromised, "Compromised", "declared insider");
                Row(_craft, Palette.Unknown, "Undeclared", "no scoring call yet");
            }
            else
            {
                Row(_craft, Palette.Friendly, "Friendly", "our drones");
                Row(_craft, Palette.Hostile, "Hostile", "attackers");
                Row(_craft, Palette.Civilian, "Civilian", "traffic");
                Row(_craft, Palette.Wreckage, "Wreckage", "fallen airframe");
                Row(_craft, Palette.Compromised, "Compromised", "insider, when present");
            }
        }

        void FillScene(bool belief)
        {
            if (_scene == null) return;
            _scene.Clear();
            Row(_scene, Palette.Asset, "Asset", "the cylinder we defend");
            Row(_scene, Palette.Selected, "Selected", "white outline, not a class");
            if (!belief)
                Row(_scene, Palette.Unknown, "Unknown", "no class yet");
        }

        void FillCues()
        {
            if (_cuesBox == null) return;
            _cuesBox.Clear();
            HookCues();
            if (_cues == null)
            {
                if (_cuesTitle != null) _cuesTitle.style.display = DisplayStyle.None;
                return;
            }

            int shown = 0;
            bool muted = _cues.Muted;
            for (int i = 0; i < CueOverlay.Specs.Length; i++)
            {
                var spec = CueOverlay.Specs[i];
                if (!_cues.IsOn(spec.Bit) || !_cues.Available(spec.Bit)) continue;
                Color hue = muted ? Palette.Gray(spec.Color) : spec.Color;
                Row(_cuesBox, hue, spec.Label, _cues.ShapeHint(spec));
                shown++;
                if (spec.Bit == CueMask.Pings)
                    CueLegend.AddPingKey(_cuesBox, labeled: false);
                if (spec.Bit == CueMask.Hops)
                    CueLegend.AddHopKey(_cuesBox, labeled: false);
            }

            if (_cuesTitle != null)
            {
                _cuesTitle.style.display = shown > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                _cuesTitle.text = shown <= 0 ? "" : muted ? "Cues hidden" : "Cues on now";
            }
        }

        void FillKeys()
        {
            if (_keys == null) return;
            if (_keys.childCount == 0)
            {
                Key(_keys, "Space", "play");
                Key(_keys, ", .", "frame");
                _jlMeaning = Key(_keys, "J L", "±5s");
                Key(_keys, "B", "belief");
                Key(_keys, "C", "cues");
                Key(_keys, "Shift+C", "hide cues");
                Key(_keys, "Esc", "deselect");
                Key(_keys, "Enter", "inspector");
                Key(_keys, "Ctrl+A", "select all (keep primary)");
                Key(_keys, "[ ]", "prev/next");
                Key(_keys, "?", "legend");
            }
            UpdateJlMeaning();
        }

        void UpdateJlMeaning()
        {
            if (_jlMeaning == null) return;
            float s = _ctx?.Clock != null
                ? _ctx.Clock.StepSecondsAmount
                : PlaybackClock.DefaultStepSeconds;
            _jlMeaning.text = "±" + PlaybackClock.FormatStep(s);
        }

        static Label Key(VisualElement parent, string key, string meaning)
        {
            var chip = new VisualElement();
            chip.AddToClassList("legend-key");
            chip.pickingMode = PickingMode.Ignore;
            var k = new Label(key);
            k.AddToClassList("legend-kbd");
            k.pickingMode = PickingMode.Ignore;
            var m = new Label(meaning);
            m.AddToClassList("legend-key-mean");
            m.pickingMode = PickingMode.Ignore;
            chip.Add(k);
            chip.Add(m);
            parent.Add(chip);
            return m;
        }

        static void Row(VisualElement parent, Color color, string name, string meaning)
        {
            var row = new VisualElement();
            row.AddToClassList("legend-row");
            row.pickingMode = PickingMode.Ignore;
            row.Add(CueLegend.Swatch(color));
            var label = new Label(name);
            label.AddToClassList("legend-name");
            label.pickingMode = PickingMode.Ignore;
            var hint = new Label(meaning);
            hint.AddToClassList("legend-hint");
            hint.pickingMode = PickingMode.Ignore;
            row.Add(label);
            row.Add(hint);
            parent.Add(row);
        }
    }
}
