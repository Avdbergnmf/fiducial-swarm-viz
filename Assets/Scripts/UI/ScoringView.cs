// Mission score ledger: sortable list, kind chips that also tick the
// timeline, and a switchable running-total graph. The sidecar writes
// scoring into run.meta.json; this is the readout.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace SwarmViewer
{
    public sealed class ScoringView : MonoBehaviour, IRunView
    {
        enum SortCol { Time, Kind, Points, Text }

        [SerializeField] UIDocument uiDocument;

        ViewerContext _ctx;
        VisualElement _root;
        bool _wired;

        Button _openBtn;
        Button _tableBtn;
        Button _graphBtn;
        FloatingPanel _float;
        bool _visible;
        bool _graphMode;

        Label _header;
        Label _mix;
        TextField _filter;
        FilterChips _kindChips;
        Button _colTime;
        Button _colKind;
        Button _colPts;
        Button _colText;
        VisualElement _cols;
        ScrollView _scroll;
        VisualElement _graphWrap;
        VisualElement _graphHost;
        Label _detailTitle;
        Label _detailBody;
        ScoreGraph _graph;

        readonly List<ScoreLedgerEntry> _ledger = new();
        readonly List<ScoreLedgerEntry> _filtered = new();
        readonly List<VisualElement> _rows = new();
        ScoreLedgerEntry _selected;
        ScoreLedgerEntry _now;
        bool _highlightDirty;
        SortCol _sort = SortCol.Time;
        bool _sortAsc = true;

        /// <summary>Kind chips also gate the ticks on the timeline track.</summary>
        public event Action MarksChanged;

        public void Bind(ViewerContext ctx)
        {
            Unhook();
            _ctx = ctx;
            _selected = null;
            _now = null;
            _highlightDirty = true;
            TryWire();
            Hook();
            CollectLedger();
            RebuildKindChips();
            if (_visible) Rebuild();
            else ApplyDetail(null);
            MarksChanged?.Invoke();
        }

        void OnEnable() => TryWire();
        void Start() => TryWire();
        void OnDestroy() => Unhook();

        void Hook()
        {
            if (_ctx?.Clock != null)
                _ctx.Clock.OnTimeChanged += OnTime;
        }

        void Unhook()
        {
            if (_ctx?.Clock != null)
                _ctx.Clock.OnTimeChanged -= OnTime;
        }

        void OnTime(float _)
        {
            if (_visible) UpdateHighlights();
            if (_graph != null)
            {
                _graph.Time = _ctx != null ? _ctx.Clock.Time : 0f;
                _graph.MarkDirtyRepaint();
            }
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
            _openBtn = UiQuery.Named<Button>(_root, "scoreOpenBtn");
            var panel = UiQuery.Named<VisualElement>(_root, "scorePanel");
            _tableBtn = UiQuery.Named<Button>(_root, "scoreTableBtn");
            _graphBtn = UiQuery.Named<Button>(_root, "scoreGraphBtn");
            _header = UiQuery.Named<Label>(_root, "scoreHeader");
            _mix = UiQuery.Named<Label>(_root, "scoreMix");
            _filter = UiQuery.Named<TextField>(_root, "scoreFilter");
            _kindChips = new FilterChips(UiQuery.Named<VisualElement>(_root, "scoreKindChips"));
            _colTime = UiQuery.Named<Button>(_root, "scoreColTime");
            _colKind = UiQuery.Named<Button>(_root, "scoreColKind");
            _colPts = UiQuery.Named<Button>(_root, "scoreColPts");
            _colText = UiQuery.Named<Button>(_root, "scoreColText");
            _cols = UiQuery.Named<VisualElement>(_root, "scoreCols");
            _scroll = UiQuery.Named<ScrollView>(_root, "scoreScroll");
            _graphWrap = UiQuery.Named<VisualElement>(_root, "scoreGraphWrap");
            _graphHost = UiQuery.Named<VisualElement>(_root, "scoreGraphHost");
            _detailTitle = UiQuery.Named<Label>(_root, "scoreDetailTitle");
            _detailBody = UiQuery.Named<Label>(_root, "scoreDetailBody");

            if (panel != null)
            {
                _float = new FloatingPanel();
                _float.Attach(panel,
                    UiQuery.Named<VisualElement>(_root, "scoreDragHandle"),
                    UiQuery.Named<Button>(_root, "scoreCloseBtn"));
                _float.Hidden += () =>
                {
                    _visible = false;
                    SetOpen(_openBtn, false);
                };
                _float.Shown += () =>
                {
                    _visible = true;
                    SetOpen(_openBtn, true);
                    Rebuild();
                };
            }

            if (_graphHost != null && _graph == null)
            {
                _graph = new ScoreGraph();
                _graph.Picked += OnGraphPicked;
                _graph.Seeked += OnGraphSeek;
                _graphHost.Add(_graph);
            }

            if (!_wired)
            {
                if (_openBtn != null)
                {
                    _openBtn.tooltip = "Mission score ledger: each event that moved the total";
                    _openBtn.clicked += Toggle;
                }
                if (_tableBtn != null)
                    _tableBtn.clicked += () => SetGraphMode(false);
                if (_graphBtn != null)
                    _graphBtn.clicked += () => SetGraphMode(true);
                if (_filter != null)
                    _filter.RegisterValueChangedCallback(_ => { if (_visible) Rebuild(); });
                if (_kindChips != null)
                    _kindChips.Changed += OnKindsChanged;
                if (_colTime != null) _colTime.clicked += () => SortBy(SortCol.Time);
                if (_colKind != null) _colKind.clicked += () => SortBy(SortCol.Kind);
                if (_colPts != null) _colPts.clicked += () => SortBy(SortCol.Points);
                if (_colText != null) _colText.clicked += () => SortBy(SortCol.Text);
                _wired = true;
            }

            ApplyMode();
        }

        void OnKindsChanged()
        {
            MarksChanged?.Invoke();
            if (_visible) Rebuild();
        }

        /// <summary>None selected means every kind. Same contract as the other lists.</summary>
        public bool AllowsKind(string kind) => _kindChips == null || _kindChips.Allows(kind);

        /// <summary>Ledger including synthesised awareness/comms rows.</summary>
        public IReadOnlyList<ScoreLedgerEntry> Marks => _ledger;

        void Toggle()
        {
            if (_visible) _float?.Hide();
            else Open();
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

        void SetGraphMode(bool graph)
        {
            if (_graphMode == graph) return;
            _graphMode = graph;
            ApplyMode();
            if (_visible) Rebuild();
        }

        void ApplyMode()
        {
            _tableBtn?.EnableInClassList("scene-state-toggle--open", !_graphMode);
            _graphBtn?.EnableInClassList("scene-state-toggle--open", _graphMode);
            if (_cols != null)
                _cols.style.display = _graphMode ? DisplayStyle.None : DisplayStyle.Flex;
            if (_scroll != null)
                _scroll.style.display = _graphMode ? DisplayStyle.None : DisplayStyle.Flex;
            if (_graphWrap != null)
                _graphWrap.style.display = _graphMode ? DisplayStyle.Flex : DisplayStyle.None;
        }

        void RebuildKindChips()
        {
            var kinds = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _ledger.Count; i++)
            {
                string kind = _ledger[i]?.kind;
                if (string.IsNullOrEmpty(kind) || !seen.Add(kind)) continue;
                kinds.Add(kind);
            }
            kinds.Sort(StringComparer.OrdinalIgnoreCase);
            _kindChips?.SetChoices(kinds);
        }

        void CollectLedger()
        {
            _ledger.Clear();
            var scoring = _ctx?.Run?.Meta?.scoring;
            var src = scoring?.ledger;
            if (src != null)
            {
                for (int i = 0; i < src.Count; i++)
                    if (src[i] != null)
                        _ledger.Add(src[i]);
            }

            var report = _ctx?.Run?.Meta?.report;
            float tEnd = _ctx?.Clock != null ? _ctx.Clock.Duration : 0f;
            if (report != null && report.sim_time_s > tEnd)
                tEnd = report.sim_time_s;
            for (int i = 0; i < _ledger.Count; i++)
                if (_ledger[i].t > tEnd)
                    tEnd = _ledger[i].t;
            int frame = _ctx?.Run != null ? Mathf.Max(0, _ctx.Run.FrameCount - 1) : 0;
            var totals = scoring?.totals;
            MaybeAddTerm("awareness", totals?.awareness ?? report?.score?.awareness, tEnd, frame,
                "awareness (declare_track over the whole run)");
            MaybeAddTerm("comms", totals?.comms ?? report?.score?.comms, tEnd, frame,
                "comms (bytes per drone per second, whole run)");
            MaybeAddTerm("detection", report?.score?.detection, tEnd, frame,
                "compromise detection");
        }

        void MaybeAddTerm(string kind, float? points, float t, int frame, string text)
        {
            if (points == null) return;
            for (int i = 0; i < _ledger.Count; i++)
                if (string.Equals(_ledger[i].kind, kind, StringComparison.OrdinalIgnoreCase))
                    return;
            _ledger.Add(new ScoreLedgerEntry
            {
                t = t,
                frame = frame,
                kind = kind,
                points = points.Value,
                attributable = true,
                text = text,
            });
        }

        void SortBy(SortCol col)
        {
            if (_sort == col)
                _sortAsc = !_sortAsc;
            else
            {
                _sort = col;
                _sortAsc = col != SortCol.Points;
            }
            if (_visible) Rebuild();
        }

        void Rebuild()
        {
            _rows.Clear();
            _highlightDirty = true;
            if (_scroll == null) return;
            _scroll.contentContainer.Clear();
            RefreshHeaders();

            var scoring = _ctx?.Run?.Meta?.scoring;
            if (_ledger.Count == 0 && scoring?.ledger == null && _ctx?.Run == null)
            {
                if (_header != null) _header.text = "Score";
                if (_mix != null) _mix.text = "";
                AddEmpty(_scroll, "No run loaded.");
                ApplyDetail(null);
                PaintGraph(_filtered);
                return;
            }

            if (_ledger.Count == 0 && scoring == null)
            {
                if (_header != null) _header.text = "Score";
                if (_mix != null) _mix.text = "";
                AddEmpty(_scroll, "This run has no scoring block. Re-convert the trace.");
                ApplyDetail(null);
                PaintGraph(_filtered);
                return;
            }

            string query = _filter != null ? (_filter.value ?? "").Trim() : "";
            _filtered.Clear();
            for (int i = 0; i < _ledger.Count; i++)
            {
                var e = _ledger[i];
                if (e == null) continue;
                if (!AllowsKind(e.kind)) continue;
                if (!Matches(e, query)) continue;
                _filtered.Add(e);
            }
            _filtered.Sort(Compare);

            if (_selected != null && !_filtered.Contains(_selected))
                _selected = null;

            int total = _ledger.Count;
            int shown = _filtered.Count;
            if (_header != null)
            {
                _header.text = shown == total
                    ? $"Score · {shown}"
                    : $"Score · {shown}/{total}";
            }
            if (_mix != null)
                _mix.text = MixText(scoring, _ctx?.Run?.Meta?.report);

            if (!_graphMode)
            {
                if (shown == 0)
                {
                    AddEmpty(_scroll, total == 0
                        ? "No scoring events in this run."
                        : "No scoring events match this filter.");
                }
                else
                {
                    for (int i = 0; i < _filtered.Count; i++)
                        _scroll.Add(MakeRow(_filtered[i]));
                }
            }

            PaintGraph(_filtered);
            if (_selected == null && _filtered.Count > 0)
                _selected = LatestAtOrBefore(_filtered, _ctx != null ? _ctx.Clock.Time : 0f);
            ApplyDetail(_selected);
            UpdateHighlights();
        }

        VisualElement MakeRow(ScoreLedgerEntry e)
        {
            var row = new VisualElement();
            row.AddToClassList("scene-state-event");
            if (!e.attributable)
                row.AddToClassList("scene-state-event--theirs");
            row.userData = e;

            row.Add(Cell($"{e.t:F1}s", "scene-state-cell-time"));

            var kind = Cell(KindLabel(e.kind), "scene-state-cell-score-kind");
            kind.style.color = Palette.Opaque(Palette.ScoreKind(e.kind));
            row.Add(kind);

            var pts = Cell(Pts(e.points), "scene-state-cell-pts");
            pts.AddToClassList(e.points < 0f ? "scene-state-cell-pts--loss" : "scene-state-cell-pts--gain");
            row.Add(pts);

            var text = Cell(e.text ?? "", "scene-state-cell-text");
            text.AddToClassList("scene-state-cell--wrap");
            row.Add(text);

            row.RegisterCallback<ClickEvent>(OnRowClicked);
            _rows.Add(row);
            return row;
        }

        void OnRowClicked(ClickEvent evt)
        {
            if (evt.currentTarget is VisualElement row && row.userData is ScoreLedgerEntry e)
                Select(e, seek: true);
            evt.StopPropagation();
        }

        void OnGraphPicked(ScoreLedgerEntry e)
        {
            if (e == null) return;
            Select(e, seek: true);
        }

        void OnGraphSeek(float t)
        {
            if (_ctx?.Clock == null) return;
            _ctx.Clock.Pause();
            _ctx.Clock.Seek(t);
        }

        void Select(ScoreLedgerEntry e, bool seek)
        {
            _selected = e;
            ApplyDetail(e);
            UpdateHighlights();
            if (_graph != null)
            {
                _graph.Selected = e;
                _graph.MarkDirtyRepaint();
            }
            if (!seek || e == null || _ctx == null) return;
            _ctx.Clock.SeekJustBefore(e.t);
            SelectSlots(e);
        }

        void SelectSlots(ScoreLedgerEntry e)
        {
            var slots = ResolveSlots(e);
            if (slots.Count == 0 || _ctx?.Selection == null) return;
            for (int i = 0; i < slots.Count; i++)
            {
                if (i == 0) _ctx.Selection.SelectOnly(slots[i]);
                else _ctx.Selection.Add(slots[i]);
            }
        }

        List<int> ResolveSlots(ScoreLedgerEntry e)
        {
            var slots = new List<int>();
            var run = _ctx?.Run;
            if (e == null || run == null) return slots;

            if (e.slots != null)
            {
                for (int i = 0; i < e.slots.Length; i++)
                {
                    int s = e.slots[i];
                    if ((uint)s < (uint)run.SlotCount && !slots.Contains(s))
                        slots.Add(s);
                }
                if (slots.Count > 0) return slots;
            }

            AddDronesFromDetail(e.detail, run, slots);
            ScanNamed(e.text, run, slots);
            if (e.detail?["target"] != null)
                ScanNamed(e.detail["target"].ToString(), run, slots);
            AddFromRunEvents(e, run, slots);
            return slots;
        }

        static void AddFromRunEvents(ScoreLedgerEntry e, RunData run, List<int> slots)
        {
            var events = run.Meta?.events;
            if (events == null || e == null) return;
            for (int i = 0; i < events.Count; i++)
            {
                var ev = events[i];
                if (ev?.slots == null || ev.slots.Length == 0) continue;
                if (Mathf.Abs(ev.t - e.t) > 0.05f) continue;
                bool kindMatch = Contains(ev.kind, e.kind) || Contains(e.kind, ev.kind);
                bool textMatch = Contains(ev.text, e.kind) || Contains(e.text, ev.text);
                if (!kindMatch && !textMatch) continue;
                for (int s = 0; s < ev.slots.Length; s++)
                {
                    int slot = ev.slots[s];
                    if ((uint)slot < (uint)run.SlotCount && !slots.Contains(slot))
                        slots.Add(slot);
                }
            }
        }

        static void AddDronesFromDetail(JObject detail, RunData run, List<int> slots)
        {
            if (detail == null) return;
            if (detail["by_drones"] is JArray drones)
            {
                for (int i = 0; i < drones.Count; i++)
                {
                    if (drones[i].Type != JTokenType.Integer && drones[i].Type != JTokenType.Float)
                        continue;
                    int slot = run.SlotOfDrone(drones[i].Value<int>());
                    if (slot >= 0 && !slots.Contains(slot))
                        slots.Add(slot);
                }
            }
        }

        static void ScanNamed(string text, RunData run, List<int> slots)
        {
            if (string.IsNullOrEmpty(text)) return;
            int i = 0;
            while (i < text.Length)
            {
                int droneAt = text.IndexOf("drone ", i, StringComparison.OrdinalIgnoreCase);
                int hostAt = text.IndexOf("hostile_", i, StringComparison.OrdinalIgnoreCase);
                int next = MinPos(droneAt, hostAt);
                if (next < 0) break;

                if (next == droneAt)
                {
                    int n = ReadInt(text, droneAt + 6);
                    if (n >= 0)
                    {
                        int slot = run.SlotOfDrone(n);
                        if (slot >= 0 && !slots.Contains(slot))
                            slots.Add(slot);
                    }
                    i = droneAt + 6;
                }
                else
                {
                    int n = ReadInt(text, hostAt + 8);
                    if (n >= 0)
                    {
                        int slot = HostileSlot(run, n);
                        if (slot >= 0 && !slots.Contains(slot))
                            slots.Add(slot);
                    }
                    i = hostAt + 8;
                }
            }
        }

        static int HostileSlot(RunData run, int index)
        {
            int n = 0;
            for (int s = 0; s < run.SlotCount; s++)
            {
                if (run.Info(s).Kind != EntityKind.Hostile) continue;
                if (n == index) return s;
                n++;
            }
            return -1;
        }

        static int MinPos(int a, int b)
        {
            if (a < 0) return b;
            if (b < 0) return a;
            return Mathf.Min(a, b);
        }

        static int ReadInt(string text, int start)
        {
            if ((uint)start >= (uint)text.Length) return -1;
            int i = start;
            while (i < text.Length && text[i] == ' ') i++;
            int n = 0;
            int digits = 0;
            while (i < text.Length && text[i] >= '0' && text[i] <= '9')
            {
                n = n * 10 + (text[i] - '0');
                i++;
                digits++;
            }
            return digits > 0 ? n : -1;
        }

        void PaintGraph(List<ScoreLedgerEntry> dots)
        {
            if (_graph == null) return;
            float duration = _ctx?.Clock != null ? _ctx.Clock.Duration : 0f;
            if (dots != null)
            {
                for (int i = 0; i < dots.Count; i++)
                    if (dots[i] != null && dots[i].t > duration)
                        duration = dots[i].t;
            }
            _graph.Duration = duration;
            _graph.Time = _ctx?.Clock != null ? _ctx.Clock.Time : 0f;
            _graph.Dots = dots;
            _graph.Selected = _selected;
            _graph.MarkDirtyRepaint();
        }

        void ApplyDetail(ScoreLedgerEntry e)
        {
            if (_detailTitle == null) return;
            if (e == null)
            {
                _detailTitle.text = "Click a row or the graph";
                if (_detailBody != null) _detailBody.text = "";
                return;
            }

            _detailTitle.text = $"{KindLabel(e.kind)}  {Pts(e.points)}  ·  {e.t:F1}s";
            if (_detailBody == null) return;

            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(e.text))
                sb.AppendLine(e.text);
            if (!e.attributable)
                sb.AppendLine("Not attributed to us.");
            AppendDetail(sb, e.detail);
            _detailBody.text = sb.ToString().TrimEnd();
        }

        void UpdateHighlights()
        {
            if (_ctx == null) return;
            float t = _ctx.Clock.Time;
            var now = LatestAtOrBefore(_filtered, t);
            if (!_highlightDirty && ReferenceEquals(now, _now) && _rows.Count > 0)
            {
                // still need selected class
            }
            _highlightDirty = false;
            _now = now;

            for (int i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                if (row.userData is not ScoreLedgerEntry e) continue;
                bool isNow = ReferenceEquals(e, now);
                bool isSel = ReferenceEquals(e, _selected);
                bool future = e.t > t + 0.001f;
                row.EnableInClassList("scene-state-event--now", isNow || isSel);
                row.EnableInClassList("scene-state-event--past", !isNow && !isSel && !future);
                row.EnableInClassList("scene-state-event--future", !isNow && !isSel && future);
            }
        }

        void RefreshHeaders()
        {
            SetHeader(_colTime, _sort == SortCol.Time, _sortAsc, "Time");
            SetHeader(_colKind, _sort == SortCol.Kind, _sortAsc, "Kind");
            SetHeader(_colPts, _sort == SortCol.Points, _sortAsc, "Pts");
            SetHeader(_colText, _sort == SortCol.Text, _sortAsc, "Text");
        }

        int Compare(ScoreLedgerEntry a, ScoreLedgerEntry b)
        {
            int c = _sort switch
            {
                SortCol.Kind => string.Compare(a.kind, b.kind, StringComparison.OrdinalIgnoreCase),
                SortCol.Points => a.points.CompareTo(b.points),
                SortCol.Text => string.Compare(a.text, b.text, StringComparison.OrdinalIgnoreCase),
                _ => a.t.CompareTo(b.t),
            };
            if (c == 0) c = a.t.CompareTo(b.t);
            if (c == 0) c = a.frame.CompareTo(b.frame);
            return _sortAsc ? c : -c;
        }

        static bool Matches(ScoreLedgerEntry e, string query)
        {
            if (string.IsNullOrEmpty(query)) return true;
            if (Contains(e.kind, query) || Contains(KindLabel(e.kind), query)) return true;
            if (Contains(e.text, query)) return true;
            if ($"{e.t:F1}".IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (Pts(e.points).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        static string MixText(ScoringBlock scoring, SimReport report)
        {
            var t = scoring?.totals;
            var score = report?.score;
            var parts = new List<string>();
            float? total = t?.total ?? score?.total;
            float? mission = t?.mission ?? score?.mission;
            float? aware = t?.awareness ?? score?.awareness;
            float? comms = t?.comms ?? score?.comms;
            if (total != null) parts.Add(Pts(total.Value) + " total");
            if (mission != null) parts.Add(Pts(mission.Value) + " mission");
            if (aware != null) parts.Add(Pts(aware.Value) + " awareness");
            if (comms != null) parts.Add(Pts(comms.Value) + " comms");
            if (t != null)
                parts.Add(t.verified ? "verified" : "ledger ≠ report");
            return string.Join(" · ", parts);
        }

        static ScoreLedgerEntry LatestAtOrBefore(List<ScoreLedgerEntry> items, float t)
        {
            ScoreLedgerEntry now = null;
            float best = float.NegativeInfinity;
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item == null || item.t > t + 0.001f) continue;
                if (item.t < best) continue;
                best = item.t;
                now = item;
            }
            return now;
        }

        static void SetHeader(Button btn, bool active, bool asc, string name)
        {
            if (btn == null) return;
            btn.text = active ? name + (asc ? " ▲" : " ▼") : name;
            btn.EnableInClassList("scene-state-col--active", active);
        }

        static Label Cell(string text, string extraClass)
        {
            var label = new Label(text ?? "");
            label.AddToClassList("scene-state-cell");
            label.AddToClassList(extraClass);
            label.pickingMode = PickingMode.Ignore;
            return label;
        }

        static void AddEmpty(ScrollView scroll, string text)
        {
            var empty = new Label(text);
            empty.AddToClassList("scene-state-empty");
            empty.pickingMode = PickingMode.Ignore;
            scroll.Add(empty);
        }

        static string KindLabel(string kind) =>
            string.IsNullOrEmpty(kind) ? "event" : kind.Replace('_', ' ');

        static string Pts(float p)
        {
            string n = p.ToString("0.##", CultureInfo.InvariantCulture);
            return p > 0f ? "+" + n : n;
        }

        static bool Contains(string hay, string needle) =>
            !string.IsNullOrEmpty(hay) && hay.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

        static void AppendDetail(StringBuilder sb, JObject detail)
        {
            if (detail == null || !detail.HasValues) return;
            foreach (var prop in detail.Properties())
            {
                sb.Append(PrettyKey(prop.Name));
                sb.Append(": ");
                sb.AppendLine(FormatVal(prop.Value));
            }
        }

        static string PrettyKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            return key.Replace('_', ' ');
        }

        static string FormatVal(object value)
        {
            if (value == null) return "—";
            if (value is JValue j)
            {
                if (j.Value == null || j.Type == JTokenType.Null) return "—";
                if (j.Value is IFormattable jf)
                    return jf.ToString(null, CultureInfo.InvariantCulture);
                if (j.Value is bool jb) return jb ? "yes" : "no";
                return Convert.ToString(j.Value, CultureInfo.InvariantCulture) ?? "—";
            }
            if (value is JToken token)
                return token.Type == JTokenType.Null
                    ? "—"
                    : token.ToString(Newtonsoft.Json.Formatting.None);
            if (value is bool b) return b ? "yes" : "no";
            if (value is IFormattable f)
                return f.ToString(null, CultureInfo.InvariantCulture);
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "—";
        }

        /// <summary>
        /// Step chart of the filtered ledger. Hover draws a guide and the
        /// score at that time; click seeks, snapping to an event whose diamond
        /// fills the snap radius. Painter2D, off the 3D overlay path.
        /// </summary>
        sealed class ScoreGraph : VisualElement
        {
            public const float SnapPx = 8f;

            public float Duration;
            public float Time;
            public IReadOnlyList<ScoreLedgerEntry> Dots;
            public ScoreLedgerEntry Selected;
            public event Action<ScoreLedgerEntry> Picked;
            public event Action<float> Seeked;

            readonly Label _readout;
            readonly List<ScoreLedgerEntry> _ordered = new();
            bool _hovering;
            Vector2 _hover;
            ScoreLedgerEntry _snap;

            public ScoreGraph()
            {
                generateVisualContent += Paint;
                RegisterCallback<ClickEvent>(OnClick);
                RegisterCallback<PointerMoveEvent>(OnMove);
                RegisterCallback<PointerLeaveEvent>(OnLeave);
                RegisterCallback<GeometryChangedEvent>(_ => MarkDirtyRepaint());
                pickingMode = PickingMode.Position;
                AddToClassList("score-graph");
                style.flexGrow = 1;
                style.width = new Length(100, LengthUnit.Percent);
                style.height = new Length(100, LengthUnit.Percent);

                _readout = new Label();
                _readout.AddToClassList("score-graph-readout");
                _readout.pickingMode = PickingMode.Ignore;
                _readout.style.display = DisplayStyle.None;
                Add(_readout);
            }

            void OnMove(PointerMoveEvent evt)
            {
                _hovering = true;
                _hover = evt.localPosition;
                UpdateSnapAndReadout();
                MarkDirtyRepaint();
            }

            void OnLeave(PointerLeaveEvent evt)
            {
                _hovering = false;
                _snap = null;
                _readout.style.display = DisplayStyle.None;
                MarkDirtyRepaint();
            }

            void OnClick(ClickEvent evt)
            {
                var plot = PlotRect();
                if (plot.width < 2f || Duration < 0.01f) return;
                _hovering = true;
                _hover = evt.localPosition;
                UpdateSnapAndReadout();
                if (_snap != null)
                    Picked?.Invoke(_snap);
                else
                    Seeked?.Invoke(TimeAt(evt.localPosition.x, plot));
                evt.StopPropagation();
            }

            void UpdateSnapAndReadout()
            {
                OrderDots();
                var plot = PlotRect();
                if (!_hovering || plot.width < 2f || Duration < 0.01f)
                {
                    _snap = null;
                    _readout.style.display = DisplayStyle.None;
                    return;
                }

                _snap = Nearest(_hover, plot);
                float t = TimeAt(_hover.x, plot);
                _readout.text = $"{t:0.00}s   {Pts(ScoreAt(t))}";
                _readout.style.display = DisplayStyle.Flex;
            }

            Rect PlotRect()
            {
                var r = contentRect;
                const float left = 46f, right = 12f, top = 28f, bottom = 24f;
                return new Rect(
                    r.x + left,
                    r.y + top,
                    Mathf.Max(8f, r.width - left - right),
                    Mathf.Max(8f, r.height - top - bottom));
            }

            void Paint(MeshGenerationContext ctx)
            {
                OrderDots();
                var plot = PlotRect();
                if (plot.width < 4f || plot.height < 4f) return;

                Range(out float y0, out float y1);
                var painter = ctx.painter2D;

                painter.fillColor = new Color(1f, 1f, 1f, 0.025f);
                painter.BeginPath();
                painter.MoveTo(new Vector2(plot.xMin, plot.yMin));
                painter.LineTo(new Vector2(plot.xMax, plot.yMin));
                painter.LineTo(new Vector2(plot.xMax, plot.yMax));
                painter.LineTo(new Vector2(plot.xMin, plot.yMax));
                painter.ClosePath();
                painter.Fill();

                DrawGridAndAxes(ctx, painter, plot, y0, y1);
                DrawAreaAndStep(painter, plot, y0, y1);
                DrawPlayhead(painter, plot);
                DrawHover(painter, plot);
                DrawDots(painter, plot, y0, y1);
            }

            void DrawGridAndAxes(MeshGenerationContext ctx, Painter2D painter, Rect plot, float y0, float y1)
            {
                var yTicks = Ticks(y0, y1, 5);
                var xTicks = Ticks(0f, Duration, 6);
                var grid = new Color(1f, 1f, 1f, 0.06f);
                var axis = new Color(1f, 1f, 1f, 0.38f);
                var label = new Color(0.78f, 0.82f, 0.88f, 0.85f);

                painter.strokeColor = grid;
                painter.lineWidth = 1f;
                for (int i = 0; i < yTicks.Count; i++)
                {
                    float y = MapY(yTicks[i], y0, y1, plot);
                    painter.BeginPath();
                    painter.MoveTo(new Vector2(plot.xMin, y));
                    painter.LineTo(new Vector2(plot.xMax, y));
                    painter.Stroke();
                }
                for (int i = 0; i < xTicks.Count; i++)
                {
                    float x = MapX(xTicks[i], plot);
                    painter.BeginPath();
                    painter.MoveTo(new Vector2(x, plot.yMin));
                    painter.LineTo(new Vector2(x, plot.yMax));
                    painter.Stroke();
                }

                if (y0 < -0.01f && y1 > 0.01f)
                {
                    float z = MapY(0f, y0, y1, plot);
                    painter.strokeColor = new Color(1f, 1f, 1f, 0.22f);
                    painter.BeginPath();
                    painter.MoveTo(new Vector2(plot.xMin, z));
                    painter.LineTo(new Vector2(plot.xMax, z));
                    painter.Stroke();
                }

                painter.strokeColor = axis;
                painter.lineWidth = 1.25f;
                painter.BeginPath();
                painter.MoveTo(new Vector2(plot.xMin, plot.yMin));
                painter.LineTo(new Vector2(plot.xMin, plot.yMax));
                painter.LineTo(new Vector2(plot.xMax, plot.yMax));
                painter.Stroke();

                ctx.DrawText("score", new Vector2(plot.xMin - 40f, plot.yMin - 16f), 10f, label, (FontAsset)null);
                ctx.DrawText("time", new Vector2(plot.xMax - 22f, plot.yMax + 8f), 10f, label, (FontAsset)null);

                for (int i = 0; i < yTicks.Count; i++)
                {
                    float v = yTicks[i];
                    float y = MapY(v, y0, y1, plot);
                    painter.strokeColor = axis;
                    painter.lineWidth = 1f;
                    painter.BeginPath();
                    painter.MoveTo(new Vector2(plot.xMin - 4f, y));
                    painter.LineTo(new Vector2(plot.xMin, y));
                    painter.Stroke();
                    ctx.DrawText(TickLabel(v), new Vector2(4f, y - 7f), 10f, label, (FontAsset)null);
                }

                for (int i = 0; i < xTicks.Count; i++)
                {
                    float v = xTicks[i];
                    float x = MapX(v, plot);
                    painter.BeginPath();
                    painter.MoveTo(new Vector2(x, plot.yMax));
                    painter.LineTo(new Vector2(x, plot.yMax + 4f));
                    painter.Stroke();
                    string s = Duration >= 20f ? v.ToString("0", CultureInfo.InvariantCulture)
                        : v.ToString("0.0", CultureInfo.InvariantCulture);
                    ctx.DrawText(s, new Vector2(x - 8f, plot.yMax + 6f), 10f, label, (FontAsset)null);
                }
            }

            void DrawAreaAndStep(Painter2D painter, Rect plot, float y0, float y1)
            {
                if (Duration < 0.01f) return;

                float zeroY = MapY(Mathf.Clamp(0f, y0, y1), y0, y1, plot);
                var pts = new List<Vector2>(8 + _ordered.Count * 2);
                float last = 0f;
                pts.Add(new Vector2(plot.xMin, MapY(0f, y0, y1, plot)));
                for (int i = 0; i < _ordered.Count; i++)
                {
                    var e = _ordered[i];
                    float x = MapX(e.t, plot);
                    pts.Add(new Vector2(x, MapY(last, y0, y1, plot)));
                    last += e.points;
                    pts.Add(new Vector2(x, MapY(last, y0, y1, plot)));
                }
                pts.Add(new Vector2(plot.xMax, MapY(last, y0, y1, plot)));

                painter.fillColor = new Color(0.55f, 0.72f, 0.92f, 0.12f);
                painter.BeginPath();
                painter.MoveTo(new Vector2(plot.xMin, zeroY));
                for (int i = 0; i < pts.Count; i++)
                    painter.LineTo(pts[i]);
                painter.LineTo(new Vector2(plot.xMax, zeroY));
                painter.ClosePath();
                painter.Fill();

                painter.strokeColor = new Color(0.78f, 0.86f, 0.96f, 0.95f);
                painter.lineWidth = 1.6f;
                painter.BeginPath();
                painter.MoveTo(pts[0]);
                for (int i = 1; i < pts.Count; i++)
                    painter.LineTo(pts[i]);
                painter.Stroke();
            }

            void DrawPlayhead(Painter2D painter, Rect plot)
            {
                if (Duration < 0.01f) return;
                float px = MapX(Time, plot);
                painter.strokeColor = new Color(1f, 1f, 1f, 0.5f);
                painter.lineWidth = 1f;
                painter.BeginPath();
                painter.MoveTo(new Vector2(px, plot.yMin));
                painter.LineTo(new Vector2(px, plot.yMax));
                painter.Stroke();
            }

            void DrawHover(Painter2D painter, Rect plot)
            {
                if (!_hovering) return;
                var r = contentRect;
                float x = Mathf.Clamp(_hover.x, plot.xMin, plot.xMax);
                painter.strokeColor = new Color(1f, 1f, 1f, 0.28f);
                painter.lineWidth = 1f;
                painter.BeginPath();
                painter.MoveTo(new Vector2(x, r.yMin));
                painter.LineTo(new Vector2(x, r.yMax));
                painter.Stroke();
            }

            void DrawDots(Painter2D painter, Rect plot, float y0, float y1)
            {
                if (Duration < 0.01f) return;
                float run = 0f;
                for (int i = 0; i < _ordered.Count; i++)
                {
                    var e = _ordered[i];
                    run += e.points;
                    float x = MapX(e.t, plot);
                    float y = MapY(run, y0, y1, plot);
                    bool snap = ReferenceEquals(e, _snap);
                    bool sel = ReferenceEquals(e, Selected);
                    float r = snap ? SnapPx : sel ? 4.5f : 3.5f;
                    var c = new Vector2(x, y);
                    var fill = Palette.Opaque(Palette.ScoreKind(e.kind));
                    if (snap)
                    {
                        fill.r = Mathf.Min(1f, fill.r * 1.15f + 0.12f);
                        fill.g = Mathf.Min(1f, fill.g * 1.15f + 0.12f);
                        fill.b = Mathf.Min(1f, fill.b * 1.15f + 0.12f);
                    }
                    painter.fillColor = fill;
                    painter.BeginPath();
                    painter.MoveTo(c + new Vector2(-r, 0f));
                    painter.LineTo(c + new Vector2(0f, -r));
                    painter.LineTo(c + new Vector2(r, 0f));
                    painter.LineTo(c + new Vector2(0f, r));
                    painter.ClosePath();
                    painter.Fill();
                    if (!snap && !sel) continue;
                    painter.strokeColor = Color.white;
                    painter.lineWidth = snap ? 1.5f : 1.1f;
                    float o = snap ? 0.6f : 1.5f;
                    painter.BeginPath();
                    painter.MoveTo(c + new Vector2(-(r + o), 0f));
                    painter.LineTo(c + new Vector2(0f, -(r + o)));
                    painter.LineTo(c + new Vector2(r + o, 0f));
                    painter.LineTo(c + new Vector2(0f, r + o));
                    painter.ClosePath();
                    painter.Stroke();
                }
            }

            void OrderDots()
            {
                _ordered.Clear();
                if (Dots == null) return;
                for (int i = 0; i < Dots.Count; i++)
                    if (Dots[i] != null)
                        _ordered.Add(Dots[i]);
                _ordered.Sort((a, b) =>
                {
                    int c = a.t.CompareTo(b.t);
                    if (c != 0) return c;
                    c = a.frame.CompareTo(b.frame);
                    if (c != 0) return c;
                    return string.Compare(a.kind, b.kind, StringComparison.OrdinalIgnoreCase);
                });
            }

            ScoreLedgerEntry Nearest(Vector2 local, Rect plot)
            {
                ScoreLedgerEntry best = null;
                float bestD = SnapPx;
                float run = 0f;
                Range(out float y0, out float y1);
                for (int i = 0; i < _ordered.Count; i++)
                {
                    var e = _ordered[i];
                    run += e.points;
                    var c = new Vector2(MapX(e.t, plot), MapY(run, y0, y1, plot));
                    float d = Mathf.Abs(local.x - c.x) + Mathf.Abs(local.y - c.y);
                    if (d >= bestD) continue;
                    bestD = d;
                    best = e;
                }
                return best;
            }

            float ScoreAt(float t)
            {
                float s = 0f;
                for (int i = 0; i < _ordered.Count; i++)
                    if (_ordered[i].t <= t + 0.001f)
                        s += _ordered[i].points;
                return s;
            }

            float TimeAt(float x, Rect plot)
            {
                float u = plot.width > 0.01f ? (x - plot.xMin) / plot.width : 0f;
                return Mathf.Clamp01(u) * Duration;
            }

            float MapX(float t, Rect plot)
            {
                float u = Duration > 0.01f ? Mathf.Clamp01(t / Duration) : 0f;
                return plot.xMin + u * plot.width;
            }

            void Range(out float y0, out float y1)
            {
                y0 = 0f;
                y1 = 0f;
                float run = 0f;
                for (int i = 0; i < _ordered.Count; i++)
                {
                    run += _ordered[i].points;
                    if (run < y0) y0 = run;
                    if (run > y1) y1 = run;
                }
                if (Mathf.Abs(y1 - y0) < 1f)
                {
                    y0 -= 10f;
                    y1 += 10f;
                }
                float pad = (y1 - y0) * 0.08f;
                y0 -= pad;
                y1 += pad;
            }

            static float MapY(float v, float y0, float y1, Rect plot)
            {
                float u = (v - y0) / (y1 - y0);
                return plot.yMax - u * plot.height;
            }

            static List<float> Ticks(float a, float b, int target)
            {
                var ticks = new List<float>();
                float span = Mathf.Abs(b - a);
                if (span < 1e-4f)
                {
                    ticks.Add(a);
                    return ticks;
                }
                if (a > b)
                {
                    float tmp = a;
                    a = b;
                    b = tmp;
                }
                float step = NiceStep(span, target);
                float start = Mathf.Ceil(a / step) * step;
                if (Mathf.Abs(start - a) < step * 0.01f) start = a;
                for (float v = start; v <= b + step * 0.01f; v += step)
                    ticks.Add(v);
                return ticks;
            }

            static float NiceStep(float span, int target)
            {
                float raw = span / Mathf.Max(1, target);
                float mag = Mathf.Pow(10f, Mathf.Floor(Mathf.Log10(Mathf.Max(raw, 1e-6f))));
                float n = raw / mag;
                float nice = n <= 1.5f ? 1f : n <= 3.5f ? 2f : n <= 7.5f ? 5f : 10f;
                return nice * mag;
            }

            static string TickLabel(float v)
            {
                if (Mathf.Abs(v) >= 100f) return v.ToString("0", CultureInfo.InvariantCulture);
                if (Mathf.Abs(v - Mathf.Round(v)) < 0.05f)
                    return v.ToString("0", CultureInfo.InvariantCulture);
                return v.ToString("0.#", CultureInfo.InvariantCulture);
            }
        }
    }
}
