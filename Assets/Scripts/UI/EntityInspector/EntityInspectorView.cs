// Draggable per-entity inspector. Double-click a craft to open it. While open,
// it follows Selection.Primary (first selected) so multi-select stays simple.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace SwarmViewer
{
    public sealed class EntityInspectorView : MonoBehaviour, IRunView
    {
        static readonly string[] KindClasses =
        {
            "inspector-panel--unknown",
            "inspector-panel--friendly",
            "inspector-panel--hostile",
            "inspector-panel--civilian",
            "inspector-panel--wreckage",
        };

        static readonly string[] DotClasses =
        {
            "inspector-kind-dot--unknown",
            "inspector-kind-dot--friendly",
            "inspector-kind-dot--hostile",
            "inspector-kind-dot--civilian",
            "inspector-kind-dot--wreckage",
        };

        [SerializeField] UIDocument uiDocument;
        [Tooltip("Seconds before an event to land when its row is clicked.")]
        [SerializeField] float eventLeadIn = 2f;

        ViewerContext _ctx;
        VisualElement _root;
        bool _uiWired;
        bool _visible;
        int _boundSlot = -1;

        VisualElement _panel;
        VisualElement _dragHandle;
        VisualElement _kindDot;
        Button _closeBtn;
        Label _title;
        Label _subtitle;
        Label _status;
        Label _speed;
        Label _altitude;
        Label _position;
        Label _heading;
        Label _kind;
        Label _slot;
        Label _trace;
        Label _drone;
        Label _lifetime;
        Label _killRadius;
        VisualElement _compromisedRow;
        Label _compromised;
        Label _eventsHeader;
        ScrollView _eventsScroll;
        Label _logHeader;
        ScrollView _logScroll;

        readonly List<VisualElement> _eventRows = new();
        IReadOnlyList<EntityEventRecord> _events;
        IReadOnlyList<LogLine> _logs;
        int _logShown;
        int _nowEventIndex = -1;

        FloatingPanel _window;

        void OnEnable() => TryWireUi();
        void Start() => TryWireUi();

        public void Bind(ViewerContext ctx)
        {
            Unhook();
            _ctx = ctx;
            TryWireUi();
            Hook();
            Close();
        }

        /// <summary>Opens on the current primary selection.</summary>
        public void Open()
        {
            if (_ctx?.Selection == null) return;
            int slot = _ctx.Selection.Primary;
            if (slot < 0) return;
            Show();
            BindSlot(slot, force: true);
        }

        public void Close()
        {
            _visible = false;
            ClearBound();
            if (_window != null)
                _window.Hide();
            else if (_panel != null)
                _panel.style.display = DisplayStyle.None;
        }

        void ClearBound()
        {
            _boundSlot = -1;
            _events = null;
            _logs = null;
            _logShown = 0;
            _nowEventIndex = -1;
        }

        void OnWindowHidden()
        {
            if (!_visible) return;
            _visible = false;
            ClearBound();
        }

        void Show()
        {
            if (_panel == null) TryWireUi();
            if (_panel == null) return;
            _visible = true;
            if (_window != null)
                _window.Show();
            else
                _panel.style.display = DisplayStyle.Flex;
        }

        void TryWireUi()
        {
            if (uiDocument == null)
                uiDocument = GetComponent<UIDocument>();
            if (uiDocument == null)
            {
                Debug.LogWarning("[viewer] EntityInspectorView: no UIDocument.");
                return;
            }

            var root = uiDocument.rootVisualElement;
            if (root == null)
            {
                Debug.LogWarning("[viewer] EntityInspectorView: rootVisualElement is null — UIDocument not ready yet.");
                return;
            }

            if (_uiWired && _root == root) return;

            _root = root;
            _panel = UiQuery.Named<VisualElement>(_root, "inspectorPanel");
            _dragHandle = UiQuery.Named<VisualElement>(_root, "inspectorDragHandle");
            _kindDot = UiQuery.Named<VisualElement>(_root, "inspectorKindDot");
            _closeBtn = UiQuery.Named<Button>(_root, "inspectorCloseBtn");
            _title = UiQuery.Named<Label>(_root, "inspectorTitle");
            _subtitle = UiQuery.Named<Label>(_root, "inspectorSubtitle");
            _status = UiQuery.Named<Label>(_root, "inspectorStatus");
            _speed = UiQuery.Named<Label>(_root, "inspectorSpeed");
            _altitude = UiQuery.Named<Label>(_root, "inspectorAltitude");
            _position = UiQuery.Named<Label>(_root, "inspectorPosition");
            _heading = UiQuery.Named<Label>(_root, "inspectorHeading");
            _kind = UiQuery.Named<Label>(_root, "inspectorKind");
            _slot = UiQuery.Named<Label>(_root, "inspectorSlot");
            _trace = UiQuery.Named<Label>(_root, "inspectorTrace");
            _drone = UiQuery.Named<Label>(_root, "inspectorDrone");
            _lifetime = UiQuery.Named<Label>(_root, "inspectorLifetime");
            _killRadius = UiQuery.Named<Label>(_root, "inspectorKillRadius");
            _compromisedRow = UiQuery.Named<VisualElement>(_root, "inspectorCompromisedRow");
            _compromised = UiQuery.Named<Label>(_root, "inspectorCompromised");
            _eventsHeader = UiQuery.Named<Label>(_root, "inspectorEventsHeader");
            _eventsScroll = UiQuery.Named<ScrollView>(_root, "inspectorEventsScroll");
            _logHeader = UiQuery.Named<Label>(_root, "inspectorLogHeader");
            _logScroll = UiQuery.Named<ScrollView>(_root, "inspectorLogScroll");

            RegisterCallbacks();
            if (_panel != null && !_visible)
                _panel.style.display = DisplayStyle.None;
            _uiWired = true;
        }

        void RegisterCallbacks()
        {
            if (_panel != null)
            {
                _window = new FloatingPanel();
                _window.Attach(_panel, _dragHandle, _closeBtn);
                _window.Hidden += OnWindowHidden;
            }
        }

        void Hook()
        {
            if (_ctx == null) return;
            if (_ctx.State != null)
                _ctx.State.Changed += OnStateChanged;
            if (_ctx.Selection != null)
                _ctx.Selection.OnSelectionChanged += OnSelectionChanged;
        }

        void Unhook()
        {
            if (_ctx == null) return;
            if (_ctx.State != null)
                _ctx.State.Changed -= OnStateChanged;
            if (_ctx.Selection != null)
                _ctx.Selection.OnSelectionChanged -= OnSelectionChanged;
        }

        void OnDestroy() => Unhook();

        void OnStateChanged()
        {
            if (!_visible || _boundSlot < 0) return;
            RefreshLive();
            RefreshLog(force: false);
            UpdateEventHighlights();
        }

        void OnSelectionChanged(int primary)
        {
            if (!_visible) return;
            if (primary < 0)
            {
                Close();
                return;
            }
            if (primary != _boundSlot)
                BindSlot(primary, force: true);
        }

        void BindSlot(int slot, bool force)
        {
            if (_ctx?.Run == null || slot < 0) return;
            if (!force && slot == _boundSlot) return;

            _boundSlot = slot;
            var info = _ctx.Run.Info(slot);
            string kindName = KindName(info.Kind);

            if (_title != null) _title.text = info.Label;
            if (_subtitle != null)
                _subtitle.text = $"{kindName} · slot {info.slot}";
            if (_kind != null) _kind.text = kindName;
            if (_slot != null) _slot.text = info.slot.ToString();
            if (_trace != null) _trace.text = info.trace_id.ToString();
            if (_drone != null) _drone.text = info.drone_id >= 0 ? info.drone_id.ToString() : "—";
            if (_lifetime != null) _lifetime.text = FormatLifetime(info);
            if (_killRadius != null)
            {
                float r = _ctx.Run.KillRadius;
                _killRadius.text = r > 0f ? $"{r:G} m" : "—";
            }

            bool compromised = info.compromised_from >= 0;
            if (_compromisedRow != null)
                _compromisedRow.style.display = compromised ? DisplayStyle.Flex : DisplayStyle.None;
            if (compromised && _compromised != null)
                _compromised.text = $"from {_ctx.Run.TimeOfFrame(info.compromised_from):F1} s";

            ApplyKindChrome(info.Kind);

            _events = _ctx.Run.EventsFor(slot);
            _logs = info.drone_id >= 0 ? _ctx.Run.LogsForDrone(info.drone_id) : null;
            RebuildEvents();
            RefreshLog(force: true);
            RefreshLive();
        }

        void ApplyKindChrome(EntityKind kind)
        {
            if (_panel != null)
            {
                for (int i = 0; i < KindClasses.Length; i++)
                    _panel.RemoveFromClassList(KindClasses[i]);
                int idx = KindClassIndex(kind);
                if (idx >= 0 && idx < KindClasses.Length)
                    _panel.AddToClassList(KindClasses[idx]);
            }

            if (_kindDot != null)
            {
                for (int i = 0; i < DotClasses.Length; i++)
                    _kindDot.RemoveFromClassList(DotClasses[i]);
                int idx = KindClassIndex(kind);
                if (idx >= 0 && idx < DotClasses.Length)
                    _kindDot.AddToClassList(DotClasses[idx]);
            }
        }

        static int KindClassIndex(EntityKind kind) => kind switch
        {
            EntityKind.Friendly => 1,
            EntityKind.Hostile => 2,
            EntityKind.Civilian => 3,
            EntityKind.Wreckage => 4,
            _ => 0,
        };

        static string KindName(EntityKind kind) => kind switch
        {
            EntityKind.Friendly => "Friendly",
            EntityKind.Hostile => "Hostile",
            EntityKind.Civilian => "Civilian",
            EntityKind.Wreckage => "Wreckage",
            _ => "Unknown",
        };

        string FormatLifetime(EntityInfo info)
        {
            if (info.first_frame < 0 && info.last_frame < 0) return "—";
            var run = _ctx.Run;
            float t0 = info.first_frame >= 0 ? run.TimeOfFrame(info.first_frame) : 0f;
            float t1 = info.last_frame >= 0 ? run.TimeOfFrame(info.last_frame) : run.Duration;
            return $"{t0:F1}–{t1:F1} s";
        }

        void RefreshLive()
        {
            if (_ctx?.State == null || _boundSlot < 0) return;
            var snap = _ctx.State.Entities[_boundSlot];

            if (_status != null)
            {
                _status.text = snap.Alive ? "Alive" : "Gone";
                _status.EnableInClassList("inspector-value--alive", snap.Alive);
                _status.EnableInClassList("inspector-value--gone", !snap.Alive);
            }

            bool hasPose = snap.Alive || snap.Position.sqrMagnitude > 0.01f;
            if (!hasPose)
            {
                SetDash(_speed);
                SetDash(_altitude);
                SetDash(_position);
                SetDash(_heading);
                return;
            }

            float speed = snap.Velocity.magnitude;
            if (_speed != null)
                _speed.text = $"{speed:F1} m/s";
            if (_altitude != null)
                _altitude.text = $"{snap.Position.y:F1} m";
            if (_position != null)
                _position.text = $"{snap.Position.x:F1}, {snap.Position.z:F1}";
            if (_heading != null)
            {
                if (speed < 0.05f)
                    _heading.text = "—";
                else
                {
                    float deg = Mathf.Atan2(snap.Velocity.x, snap.Velocity.z) * Mathf.Rad2Deg;
                    if (deg < 0f) deg += 360f;
                    _heading.text = $"{deg:000}°";
                }
            }
        }

        static void SetDash(Label label)
        {
            if (label != null) label.text = "—";
        }

        void RebuildEvents()
        {
            _eventRows.Clear();
            _nowEventIndex = int.MinValue;
            if (_eventsScroll == null) return;
            _eventsScroll.contentContainer.Clear();

            int n = _events != null ? _events.Count : 0;
            if (_eventsHeader != null)
                _eventsHeader.text = n == 0 ? "Events" : $"Events · {n}";

            if (n == 0)
            {
                var empty = new Label("No events for this entity.");
                empty.AddToClassList("inspector-empty");
                empty.pickingMode = PickingMode.Ignore;
                _eventsScroll.Add(empty);
                return;
            }

            for (int i = 0; i < n; i++)
            {
                var rec = _events[i];
                var row = new VisualElement();
                row.AddToClassList("inspector-event");
                row.userData = rec;

                var head = new VisualElement();
                head.AddToClassList("inspector-event-head");
                head.pickingMode = PickingMode.Ignore;

                var time = new Label($"{rec.time:F1}s");
                time.AddToClassList("inspector-event-time");
                time.pickingMode = PickingMode.Ignore;

                var kind = new Label(string.IsNullOrEmpty(rec.kind) ? "event" : rec.kind);
                kind.AddToClassList("inspector-event-kind");
                kind.pickingMode = PickingMode.Ignore;

                var sev = new Label(SeverityLabel(rec.severity));
                sev.AddToClassList("inspector-sev");
                sev.AddToClassList("inspector-sev--" + Mathf.Clamp(rec.severity, 0, 3));
                sev.pickingMode = PickingMode.Ignore;

                head.Add(time);
                head.Add(kind);
                head.Add(sev);
                row.Add(head);

                if (!string.IsNullOrEmpty(rec.text))
                {
                    var text = new Label(rec.text);
                    text.AddToClassList("inspector-event-text");
                    text.pickingMode = PickingMode.Ignore;
                    row.Add(text);
                }

                row.RegisterCallback<ClickEvent>(OnEventClicked);
                _eventsScroll.Add(row);
                _eventRows.Add(row);
            }

            UpdateEventHighlights();
        }

        void OnEventClicked(ClickEvent evt)
        {
            if (evt.currentTarget is VisualElement row && row.userData is EntityEventRecord rec)
                JumpToEvent(rec);
            evt.StopPropagation();
        }

        void JumpToEvent(EntityEventRecord rec)
        {
            if (_ctx == null || rec == null || _boundSlot < 0) return;

            _ctx.Clock.SeekBefore(rec.time, eventLeadIn);

            var sel = _ctx.Selection;
            sel.SelectOnly(_boundSlot);
            if (rec.otherSlots == null) return;
            for (int i = 0; i < rec.otherSlots.Length; i++)
                sel.Add(rec.otherSlots[i]);
        }

        void UpdateEventHighlights()
        {
            if (_ctx == null || _eventRows.Count == 0) return;
            float t = _ctx.Clock.Time;
            int now = -1;
            for (int i = 0; i < _eventRows.Count; i++)
            {
                if (_eventRows[i].userData is not EntityEventRecord rec) continue;
                if (rec.time <= t + 0.001f) now = i;
            }

            if (now == _nowEventIndex) return;
            _nowEventIndex = now;

            for (int i = 0; i < _eventRows.Count; i++)
            {
                var row = _eventRows[i];
                row.EnableInClassList("inspector-event--now", i == now);
                row.EnableInClassList("inspector-event--past", now >= 0 && i < now);
                row.EnableInClassList("inspector-event--future", now < 0 || i > now);
            }
        }

        void RefreshLog(bool force)
        {
            if (_logScroll == null) return;

            int total = _logs != null ? _logs.Count : 0;
            int visible = 0;
            if (_logs != null && _ctx != null)
            {
                float t = _ctx.Clock.Time;
                while (visible < total && _logs[visible].t <= t + 0.001f)
                    visible++;
            }

            if (_logHeader != null)
            {
                if (total == 0)
                    _logHeader.text = "Log";
                else
                    _logHeader.text = $"Log · {visible}/{total}";
            }

            if (!force && visible == _logShown) return;

            if (force || visible < _logShown)
            {
                _logScroll.contentContainer.Clear();
                _logShown = 0;
            }

            if (total == 0)
            {
                if (_logShown == 0 && _logScroll.contentContainer.childCount == 0)
                {
                    var empty = new Label("No brain log — only friendlies write one.");
                    empty.AddToClassList("inspector-empty");
                    empty.pickingMode = PickingMode.Ignore;
                    _logScroll.Add(empty);
                }
                return;
            }

            if (visible == 0 && _logShown == 0 && _logScroll.contentContainer.childCount == 0)
            {
                var waiting = new Label("Nothing logged yet at this time.");
                waiting.AddToClassList("inspector-empty");
                waiting.name = "inspectorLogWaiting";
                waiting.pickingMode = PickingMode.Ignore;
                _logScroll.Add(waiting);
                return;
            }

            if (_logShown == 0 && _logScroll.contentContainer.childCount > 0)
                _logScroll.contentContainer.Clear();

            bool appended = false;
            for (int i = _logShown; i < visible; i++)
            {
                AppendLogLine(_logs[i]);
                appended = true;
            }
            _logShown = visible;

            if (appended)
            {
                var last = _logScroll.contentContainer.childCount > 0
                    ? _logScroll.contentContainer[_logScroll.contentContainer.childCount - 1]
                    : null;
                if (last != null)
                    _logScroll.schedule.Execute(() => _logScroll.ScrollTo(last));
            }
        }

        void AppendLogLine(LogLine line)
        {
            var row = new VisualElement();
            row.AddToClassList("inspector-log-line");
            row.pickingMode = PickingMode.Ignore;

            var time = new Label($"{line.t:F1}s");
            time.AddToClassList("inspector-log-time");
            time.pickingMode = PickingMode.Ignore;

            var text = new Label(line.text ?? "");
            text.AddToClassList("inspector-log-text");
            text.pickingMode = PickingMode.Ignore;

            row.Add(time);
            row.Add(text);
            _logScroll.Add(row);
        }

        static string SeverityLabel(int severity) => severity switch
        {
            0 => "info",
            1 => "loss",
            2 => "bad",
            3 => "worst",
            _ => severity.ToString(),
        };
    }
}
