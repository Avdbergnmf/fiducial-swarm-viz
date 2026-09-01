// Fleet-wide belief disagreements at the current time.
//
// Scoring declarations (beliefs[]) plus fused hearsay from `call … peer`
// logs. Click a row to select the subject and everyone who has an opinion;
// the lower pane lists every call.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace SwarmViewer
{
    public sealed class DisagreeView : MonoBehaviour, IRunView
    {
        struct Opinion
        {
            public int Drone;
            public BeliefClass Class;
            public bool Hearsay;
            public int Hops;
            public int Origin;
        }

        struct Row
        {
            public int Slot;
            public bool VsTruth;
            public bool Split;
            public bool HasHearsay;
            public string SplitText;
        }

        [SerializeField] UIDocument uiDocument;

        ViewerContext _ctx;
        VisualElement _root;
        bool _wired;

        Button _openBtn;
        FloatingPanel _float;
        bool _visible;
        bool _showAgreements;

        Label _header;
        Label _mix;
        Button _agreeBtn;
        TextField _filter;
        ScrollView _scroll;
        ScrollView _detail;
        Label _detailTitle;

        readonly List<Opinion> _opinions = new();
        readonly List<Row> _rows = new();
        readonly List<int> _fleet = new();
        int _picked = -1;
        int _fingerprint = int.MinValue;

        public void Bind(ViewerContext ctx)
        {
            Unhook();
            _ctx = ctx;
            _picked = -1;
            _fingerprint = int.MinValue;
            TryWire();
            RebuildFleet();
            Hook();
            if (_visible) Rebuild();
        }

        void OnEnable() => TryWire();
        void Start() => TryWire();
        void OnDestroy() => Unhook();

        public void Toggle()
        {
            if (_visible) _float?.Hide();
            else Open();
        }

        void Hook()
        {
            if (_ctx?.State != null) _ctx.State.Changed += OnState;
            if (_ctx?.Selection != null)
                _ctx.Selection.OnSelectionSetChanged += RefreshHighlights;
        }

        void Unhook()
        {
            if (_ctx?.State != null) _ctx.State.Changed -= OnState;
            if (_ctx?.Selection != null)
                _ctx.Selection.OnSelectionSetChanged -= RefreshHighlights;
        }

        void OnState()
        {
            if (_visible) Rebuild();
        }

        void TryWire()
        {
            if (uiDocument == null)
                uiDocument = GetComponent<UIDocument>();
            if (uiDocument == null) return;
            var tree = uiDocument.rootVisualElement;
            if (tree == null) return;
            if (_wired && _root == tree) return;

            _root = tree;
            _openBtn = UiQuery.Named<Button>(_root, "disagreeOpenBtn");
            var panel = UiQuery.Named<VisualElement>(_root, "disagreePanel");
            _header = UiQuery.Named<Label>(_root, "disagreeHeader");
            _mix = UiQuery.Named<Label>(_root, "disagreeMix");
            _agreeBtn = UiQuery.Named<Button>(_root, "disagreeAgreeBtn");
            _filter = UiQuery.Named<TextField>(_root, "disagreeFilter");
            _scroll = UiQuery.Named<ScrollView>(_root, "disagreeScroll");
            _detail = UiQuery.Named<ScrollView>(_root, "disagreeDetailScroll");
            _detailTitle = UiQuery.Named<Label>(_root, "disagreeDetailTitle");

            if (panel != null)
            {
                _float = new FloatingPanel();
                _float.Attach(panel,
                    UiQuery.Named<VisualElement>(_root, "disagreeDragHandle"),
                    UiQuery.Named<Button>(_root, "disagreeCloseBtn"));
                _float.Hidden += () =>
                {
                    _visible = false;
                    SetOpen(_openBtn, false);
                };
            }

            if (!_wired)
            {
                if (_openBtn != null)
                {
                    _openBtn.tooltip = "Who disagrees with whom, including hearsay";
                    _openBtn.clicked += Toggle;
                }
                if (_agreeBtn != null)
                {
                    _agreeBtn.clicked += () =>
                    {
                        _showAgreements = !_showAgreements;
                        _agreeBtn.EnableInClassList("scene-state-toggle--open", _showAgreements);
                        _fingerprint = int.MinValue;
                        if (_visible) Rebuild();
                    };
                }
                if (_filter != null)
                    _filter.RegisterValueChangedCallback(_ => { _fingerprint = int.MinValue; if (_visible) Rebuild(); });
                _wired = true;
            }
        }

        void Open()
        {
            _visible = true;
            _float?.Show();
            SetOpen(_openBtn, true);
            Rebuild();
        }

        static void SetOpen(Button btn, bool on) =>
            btn?.EnableInClassList("scene-state-toggle--open", on);

        void RebuildFleet()
        {
            _fleet.Clear();
            if (_ctx?.Run == null) return;
            for (int s = 0; s < _ctx.Run.SlotCount; s++)
            {
                int id = _ctx.Run.Info(s).drone_id;
                if (id >= 0) _fleet.Add(id);
            }
            _fleet.Sort();
        }

        void Rebuild()
        {
            if (_scroll == null || _ctx?.Run == null) return;
            float t = _ctx.Clock != null ? _ctx.Clock.Time : 0f;
            int fp = Collect(t);
            if (fp == _fingerprint) { RefreshHighlights(); RefreshDetail(t); return; }
            _fingerprint = fp;

            _scroll.contentContainer.Clear();
            string query = _filter != null ? _filter.value : "";
            int shown = 0;
            for (int i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                var info = _ctx.Run.Info(row.Slot);
                if (!string.IsNullOrEmpty(query) &&
                    info.Label.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0 &&
                    row.SplitText.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                _scroll.Add(MakeRow(row, info));
                shown++;
            }

            if (shown == 0)
            {
                var empty = new Label(_rows.Count == 0
                    ? "No calls to compare at this time."
                    : "No disagreements at this time. Fused hearsay that agrees is hidden unless you turn on Agreements.");
                empty.AddToClassList("scene-state-empty");
                empty.pickingMode = PickingMode.Ignore;
                _scroll.Add(empty);
            }

            if (_header != null)
                _header.text = shown == 1 ? "1 row" : shown + " rows";
            RefreshHighlights();
            RefreshDetail(t);
        }

        int Collect(float t)
        {
            _rows.Clear();
            int disagree = 0, agree = 0, hear = 0;
            var run = _ctx.Run;
            bool compromisedNow;

            for (int slot = 0; slot < run.SlotCount; slot++)
            {
                if (_ctx.State != null && slot < _ctx.State.Entities.Count &&
                    !_ctx.State.Entities[slot].Alive)
                    continue;

                CollectOpinions(slot, t);
                if (_opinions.Count == 0) continue;

                bool hasHear = false;
                bool split = false;
                BeliefClass first = _opinions[0].Class;
                for (int i = 0; i < _opinions.Count; i++)
                {
                    if (_opinions[i].Hearsay) hasHear = true;
                    if (_opinions[i].Class != first) split = true;
                }

                var info = run.Info(slot);
                compromisedNow = _ctx.State != null && _ctx.State.IsCompromisedNow(slot);
                bool vsTruth = false;
                for (int i = 0; i < _opinions.Count; i++)
                {
                    if (!BeliefIndex.Agrees(_opinions[i].Class, info, compromisedNow))
                        vsTruth = true;
                }

                if (hasHear) hear += _opinions.Count;
                bool mismatch = vsTruth || split;
                if (mismatch) disagree++;
                else agree++;
                if (!mismatch && !_showAgreements) continue;

                _rows.Add(new Row
                {
                    Slot = slot,
                    VsTruth = vsTruth,
                    Split = split,
                    HasHearsay = hasHear,
                    SplitText = Summarise(),
                });
            }

            if (_mix != null)
            {
                _mix.text = disagree == 0 && agree == 0
                    ? ""
                    : $"{disagree} disagree · {agree} agree · {hear} hearsay calls";
            }

            return disagree * 10007 + agree * 131 + hear + (_showAgreements ? 1 : 0) +
                   (_picked + 3) * 17 + HashFilter();
        }

        int HashFilter()
        {
            string q = _filter != null ? _filter.value : "";
            return string.IsNullOrEmpty(q) ? 0 : q.GetHashCode();
        }

        void CollectOpinions(int slot, float t)
        {
            _opinions.Clear();
            var run = _ctx.Run;
            for (int i = 0; i < _fleet.Count; i++)
            {
                int drone = _fleet[i];
                var declared = run.Beliefs.At(drone, slot, t);
                if (declared != BeliefClass.Unknown)
                {
                    _opinions.Add(new Opinion { Drone = drone, Class = declared });
                    continue;
                }
                var hear = run.Hear != null ? run.Hear.At(drone, slot, t) : null;
                if (hear == null || hear.Value.Class == BeliefClass.Unknown) continue;
                _opinions.Add(new Opinion
                {
                    Drone = drone,
                    Class = hear.Value.Class,
                    Hearsay = true,
                    Hops = hear.Value.Hops,
                    Origin = hear.Value.Origin,
                });
            }
        }

        string Summarise()
        {
            int enemy = 0, friendly = 0, civilian = 0, other = 0, hear = 0;
            for (int i = 0; i < _opinions.Count; i++)
            {
                if (_opinions[i].Hearsay) hear++;
                switch (_opinions[i].Class)
                {
                    case BeliefClass.Enemy: enemy++; break;
                    case BeliefClass.Friendly: friendly++; break;
                    case BeliefClass.Neutral: civilian++; break;
                    default: other++; break;
                }
            }
            var parts = new List<string>(4);
            if (enemy > 0) parts.Add(enemy + " enemy");
            if (friendly > 0) parts.Add(friendly + " friendly");
            if (civilian > 0) parts.Add(civilian + " civilian");
            if (other > 0) parts.Add(other + " other");
            string split = string.Join(" · ", parts);
            if (hear > 0) split += hear == 1 ? " · 1 radio" : $" · {hear} radio";
            return split;
        }

        VisualElement MakeRow(Row data, EntityInfo info)
        {
            var row = new VisualElement();
            row.AddToClassList("scene-state-drone");
            row.userData = data.Slot;
            if (data.VsTruth)
                row.AddToClassList("scene-state-drone-called--mismatch");

            var dot = new VisualElement();
            dot.AddToClassList("scene-state-dot");
            Palette.Fill(dot, Palette.Kind(info.Kind));
            dot.pickingMode = PickingMode.Ignore;

            var name = new Label(info.Label);
            name.AddToClassList("scene-state-drone-name");
            name.pickingMode = PickingMode.Ignore;

            var truth = new Label(BeliefIndex.TruthLabel(info,
                _ctx.State != null && _ctx.State.IsCompromisedNow(data.Slot)));
            truth.AddToClassList("scene-state-drone-kind");
            truth.pickingMode = PickingMode.Ignore;

            var split = new Label(data.SplitText);
            split.AddToClassList("scene-state-drone-called");
            split.style.flexGrow = 1;
            split.pickingMode = PickingMode.Ignore;

            var why = new Label(data.Split ? "split" : data.VsTruth ? "vs truth" : "agree");
            why.AddToClassList("scene-state-drone-status");
            why.pickingMode = PickingMode.Ignore;

            row.Add(dot);
            row.Add(name);
            row.Add(truth);
            row.Add(split);
            row.Add(why);
            row.RegisterCallback<ClickEvent, int>(OnRowClicked, data.Slot);
            return row;
        }

        void OnRowClicked(ClickEvent evt, int slot)
        {
            _picked = slot;
            CollectOpinions(slot, _ctx.Clock != null ? _ctx.Clock.Time : 0f);
            var sel = new List<int> { slot };
            for (int i = 0; i < _opinions.Count; i++)
            {
                int s = _ctx.Run.SlotOfDrone(_opinions[i].Drone);
                if (s >= 0 && !sel.Contains(s)) sel.Add(s);
            }
            _ctx.Selection?.Replace(sel);
            RefreshDetail(_ctx.Clock != null ? _ctx.Clock.Time : 0f);
            RefreshHighlights();
        }

        void RefreshDetail(float t)
        {
            if (_detail == null) return;
            _detail.contentContainer.Clear();
            if (_picked < 0 || _ctx?.Run == null)
            {
                if (_detailTitle != null)
                    _detailTitle.text = "Click a row for every call on that craft";
                return;
            }

            var info = _ctx.Run.Info(_picked);
            CollectOpinions(_picked, t);
            if (_detailTitle != null)
                _detailTitle.text = _opinions.Count == 0
                    ? info.Label + " — no calls"
                    : $"{info.Label} · {_opinions.Count} call" + (_opinions.Count == 1 ? "" : "s");

            bool compromised = _ctx.State != null && _ctx.State.IsCompromisedNow(_picked);
            for (int i = 0; i < _opinions.Count; i++)
            {
                var op = _opinions[i];
                var row = new VisualElement();
                row.AddToClassList("scene-state-drone");
                bool mismatch = !BeliefIndex.Agrees(op.Class, info, compromised);
                row.EnableInClassList("scene-state-drone-called--mismatch", mismatch);

                var who = new Label("Drone " + op.Drone);
                who.AddToClassList("scene-state-drone-name");
                who.pickingMode = PickingMode.Ignore;

                var call = new Label(BeliefIndex.Label(op.Class));
                call.AddToClassList("scene-state-drone-kind");
                call.pickingMode = PickingMode.Ignore;

                string src = op.Hearsay
                    ? (op.Hops > 0
                        ? $"radio · {op.Hops} hop" + (op.Hops == 1 ? "" : "s") + " from " + op.Origin
                        : "radio from " + op.Origin)
                    : "declared";
                var source = new Label(src);
                source.AddToClassList("scene-state-drone-called");
                source.style.flexGrow = 1;
                source.pickingMode = PickingMode.Ignore;

                var vs = new Label(mismatch ? "wrong" : "match");
                vs.AddToClassList("scene-state-drone-status");
                vs.pickingMode = PickingMode.Ignore;

                row.Add(who);
                row.Add(call);
                row.Add(source);
                row.Add(vs);
                int drone = op.Drone;
                row.RegisterCallback<ClickEvent>(_ =>
                {
                    int s = _ctx.Run.SlotOfDrone(drone);
                    if (s >= 0) _ctx.Selection?.SelectOnly(s);
                });
                _detail.Add(row);
            }
        }

        void RefreshHighlights()
        {
            if (_scroll == null) return;
            var sel = _ctx?.Selection;
            foreach (var child in _scroll.Children())
            {
                if (child.userData is int slot)
                    child.EnableInClassList("scene-state-drone--selected",
                        slot == _picked || (sel != null && sel.IsSelected(slot)));
            }
        }
    }
}
