// Draggable per-entity inspector. Double-click a craft to open it. While open,
// it follows Selection.Primary (first selected) so multi-select stays simple.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace SwarmViewer
{
    public sealed class EntityInspectorView : MonoBehaviour, IRunView
    {
        [SerializeField] UIDocument uiDocument;
        [Tooltip("Seconds before a log line to land. Events jump one recorded frame earlier so the craft is still selectable.")]
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
        Label _accel;
        Label _altitude;
        Label _position;
        Label _heading;
        Label _kind;
        Label _slot;
        Label _trace;
        Label _drone;
        Label _lifetime;
        Label _killRadius;
        Label _sense;
        Label _comm;
        Label _sep;
        VisualElement _compromisedRow;
        Label _compromised;
        Button _beliefsOpenBtn;
        Label _eventsHeader;
        ScrollView _eventsScroll;
        VisualElement _logSection;
        Label _logHeader;
        ScrollView _logScroll;
        LogList _logList;

        VisualElement _beliefsPanel;
        VisualElement _beliefsDragHandle;
        VisualElement _beliefsKindDot;
        Button _beliefsCloseBtn;
        Button _beliefsSelectBtn;
        Label _beliefsTitle;
        Label _beliefsSubtitle;
        Button _beliefsVizBtn;
        Label _beliefsIntent;
        Label _beliefsCallsHeader;
        Label _beliefsCallsHint;
        ScrollView _beliefsCallsScroll;
        Label _beliefsSeesHeader;
        ScrollView _beliefsSeesScroll;
        FloatingPanel _beliefsWindow;
        bool _beliefsVisible;
        int _beliefsSlot = -1;
        IReadOnlyList<LogLine> _beliefsLogs;

        readonly List<(int observer, BeliefClass cls)> _callsBuf = new();
        readonly List<(int slot, BeliefClass cls)> _seesBuf = new();
        int _callsFingerprint = int.MinValue;
        int _seesFingerprint = int.MinValue;
        readonly List<VisualElement> _eventRows = new();
        IReadOnlyList<EntityEventRecord> _events;
        IReadOnlyList<LogLine> _logs;
        int _nowEventIndex = -1;

        FloatingPanel _window;

        void OnEnable() => TryWireUi();
        void Start() => TryWireUi();

        public void Bind(ViewerContext ctx)
        {
            Unhook();
            _ctx = ctx;
            TryWireUi();
            if (_logList != null)
            {
                _logList.Bind(_ctx);
                _logList.LeadIn = eventLeadIn;
            }
            Hook();
            Close();
            HideBeliefs();
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
            _nowEventIndex = -1;
            _callsFingerprint = int.MinValue;
            _seesFingerprint = int.MinValue;
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
            _accel = UiQuery.Named<Label>(_root, "inspectorAccel");
            _altitude = UiQuery.Named<Label>(_root, "inspectorAltitude");
            _position = UiQuery.Named<Label>(_root, "inspectorPosition");
            _heading = UiQuery.Named<Label>(_root, "inspectorHeading");
            _kind = UiQuery.Named<Label>(_root, "inspectorKind");
            _slot = UiQuery.Named<Label>(_root, "inspectorSlot");
            _trace = UiQuery.Named<Label>(_root, "inspectorTrace");
            _drone = UiQuery.Named<Label>(_root, "inspectorDrone");
            _lifetime = UiQuery.Named<Label>(_root, "inspectorLifetime");
            _killRadius = UiQuery.Named<Label>(_root, "inspectorKillRadius");
            _sense = UiQuery.Named<Label>(_root, "inspectorSense");
            _comm = UiQuery.Named<Label>(_root, "inspectorComm");
            _sep = UiQuery.Named<Label>(_root, "inspectorSep");
            _compromisedRow = UiQuery.Named<VisualElement>(_root, "inspectorCompromisedRow");
            _compromised = UiQuery.Named<Label>(_root, "inspectorCompromised");
            _beliefsOpenBtn = UiQuery.Named<Button>(_root, "inspectorBeliefsBtn");
            _eventsHeader = UiQuery.Named<Label>(_root, "inspectorEventsHeader");
            _eventsScroll = UiQuery.Named<ScrollView>(_root, "inspectorEventsScroll");
            _logSection = UiQuery.Named<VisualElement>(_root, "inspectorLogSection");
            _logHeader = UiQuery.Named<Label>(_root, "inspectorLogHeader");
            _logScroll = UiQuery.Named<ScrollView>(_root, "inspectorLogScroll");
            _logList = new LogList(
                _logScroll,
                _logHeader,
                null,
                UiQuery.Named<VisualElement>(_root, "inspectorLogVerbs"),
                UiQuery.Named<Button>(_root, "inspectorLogRawBtn"))
            {
                ShowDrone = false,
                SelectOnJump = false,
                FollowNow = true,
                Title = "Log",
                LeadIn = eventLeadIn,
            };
            if (_ctx != null)
                _logList.Bind(_ctx);

            _beliefsPanel = UiQuery.Named<VisualElement>(_root, "beliefsPanel");
            _beliefsDragHandle = UiQuery.Named<VisualElement>(_root, "beliefsDragHandle");
            _beliefsKindDot = UiQuery.Named<VisualElement>(_root, "beliefsKindDot");
            _beliefsCloseBtn = UiQuery.Named<Button>(_root, "beliefsCloseBtn");
            _beliefsSelectBtn = UiQuery.Named<Button>(_root, "beliefsSelectBtn");
            _beliefsTitle = UiQuery.Named<Label>(_root, "beliefsTitle");
            _beliefsSubtitle = UiQuery.Named<Label>(_root, "beliefsSubtitle");
            _beliefsVizBtn = UiQuery.Named<Button>(_root, "beliefsVizBtn");
            _beliefsIntent = UiQuery.Named<Label>(_root, "beliefsIntent");
            _beliefsCallsHeader = UiQuery.Named<Label>(_root, "beliefsCallsHeader");
            _beliefsCallsHint = UiQuery.Named<Label>(_root, "beliefsCallsHint");
            _beliefsCallsScroll = UiQuery.Named<ScrollView>(_root, "beliefsCallsScroll");
            _beliefsSeesHeader = UiQuery.Named<Label>(_root, "beliefsSeesHeader");
            _beliefsSeesScroll = UiQuery.Named<ScrollView>(_root, "beliefsSeesScroll");

            RegisterCallbacks();
            if (_panel != null && !_visible)
                _panel.style.display = DisplayStyle.None;
            if (_beliefsPanel != null && !_beliefsVisible)
                _beliefsPanel.style.display = DisplayStyle.None;
            if (_beliefsOpenBtn != null && !_beliefsVisible)
                _beliefsOpenBtn.style.display = DisplayStyle.None;
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
            if (_beliefsOpenBtn != null)
                _beliefsOpenBtn.clicked += OpenBeliefsFromInspector;
            if (_beliefsPanel != null)
            {
                _beliefsWindow = new FloatingPanel();
                _beliefsWindow.Attach(_beliefsPanel, _beliefsDragHandle, _beliefsCloseBtn);
                _beliefsWindow.Hidden += OnBeliefsHidden;
            }
            if (_beliefsVizBtn != null)
                _beliefsVizBtn.clicked += TogglePinnedViz;
            if (_beliefsSelectBtn != null)
                _beliefsSelectBtn.clicked += SelectPinnedDrone;
        }

        void Hook()
        {
            if (_ctx == null) return;
            if (_ctx.State != null)
                _ctx.State.Changed += OnStateChanged;
            if (_ctx.Selection != null)
            {
                _ctx.Selection.OnSelectionChanged += OnSelectionChanged;
                _ctx.Selection.OnViewModeChanged += OnViewModeChanged;
                _ctx.Selection.OnObserverChanged += OnObserverChanged;
            }
        }

        void Unhook()
        {
            if (_ctx == null) return;
            if (_ctx.State != null)
                _ctx.State.Changed -= OnStateChanged;
            if (_ctx.Selection != null)
            {
                _ctx.Selection.OnSelectionChanged -= OnSelectionChanged;
                _ctx.Selection.OnViewModeChanged -= OnViewModeChanged;
                _ctx.Selection.OnObserverChanged -= OnObserverChanged;
            }
        }

        void OnDestroy() => Unhook();

        void OnStateChanged()
        {
            if (!_visible || _boundSlot < 0) return;
            RefreshLive();
            _logList?.Highlight();
            UpdateEventHighlights();
            if (_beliefsVisible)
                RefreshPinnedBeliefs(force: false);
        }

        void OnViewModeChanged(ViewMode _) => RefreshPinnedVizBtn();
        void OnObserverChanged(int _) => RefreshPinnedVizBtn();

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

            if (_title != null)
            {
                _title.text = info.Label;
                _title.tooltip = info.IdBlurb;
            }
            if (_subtitle != null)
            {
                _subtitle.text = info.IsFriendly
                    ? $"{kindName} · drone {info.drone_id} · sim #{info.trace_id} · slot {info.slot}"
                    : $"{kindName} · sim #{info.trace_id} · slot {info.slot}";
                _subtitle.tooltip = info.IdBlurb;
            }
            if (_kind != null) _kind.text = kindName;
            if (_slot != null)
            {
                _slot.text = info.slot.ToString();
                _slot.tooltip = "Viewer column in the recording, 0-based. For the initial fleet this equals the drone id.";
            }
            if (_trace != null)
            {
                _trace.text = info.trace_id.ToString();
                _trace.tooltip = info.IsFriendly
                    ? $"Simulator entity id, 1-based. Drone {info.drone_id} is sim #{info.trace_id} — that is why this number is one higher than the log column."
                    : "Simulator entity id, 1-based. Hostiles and civilians have no brain id.";
            }
            if (_drone != null)
            {
                _drone.text = info.drone_id >= 0 ? info.drone_id.ToString() : "—";
                _drone.tooltip = info.drone_id >= 0
                    ? "Brain id, 0-based. Same number as Logs and as 'Drone N' in the aircraft list."
                    : "Not a fleet drone — no log channel.";
            }
            if (_lifetime != null) _lifetime.text = FormatLifetime(info);
            if (_killRadius != null)
            {
                float r = _ctx.Run.KillRadius;
                _killRadius.text = r > 0f ? $"{r:G} m" : "—";
            }

            var p = _ctx.Run.Params;
            if (_sense != null)
                _sense.text = p != null && p.Has(p.SenseRadius) ? $"{p.SenseRadius:G} m" : "—";
            if (_comm != null)
            {
                if (p != null && p.Has(p.CommDraw))
                    _comm.text = p.CommFromLinks ? $"{p.CommDraw:G} m (links)" : $"{p.CommDraw:G} m";
                else
                    _comm.text = "—";
            }
            if (_sep != null)
                _sep.text = p != null && p.Has(p.SeparationMargin) ? $"{p.SeparationMargin:G} m" : "—";

            bool compromised = info.compromised_from >= 0;
            if (_compromisedRow != null)
                _compromisedRow.style.display = compromised ? DisplayStyle.Flex : DisplayStyle.None;
            if (compromised && _compromised != null)
                _compromised.text = $"from {_ctx.Run.TimeOfFrame(info.compromised_from):F1} s";

            ApplyKindChrome(info.Kind, _ctx.State != null && _ctx.State.IsCompromisedNow(slot));

            _events = _ctx.Run.EventsFor(slot);
            _logs = info.drone_id >= 0 ? _ctx.Run.LogsForDrone(info.drone_id) : null;
            RebuildEvents();
            RefreshLog();
            RefreshLive();
            RefreshBeliefsOpenBtn(info);
        }

        void ApplyKindChrome(EntityKind kind, bool compromised = false)
        {
            Color c = compromised ? Palette.Compromised : Palette.Kind(kind);
            Palette.Border(_panel, c);
            Palette.Fill(_kindDot, c);
        }

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
            var info = _ctx.Run.Info(_boundSlot);
            ApplyKindChrome(info.Kind, _ctx.State.IsCompromisedNow(_boundSlot));
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
                SetDash(_accel);
                SetDash(_altitude);
                SetDash(_position);
                SetDash(_heading);
                return;
            }

            var p = _ctx.Run.Params;
            float speed = snap.Velocity.magnitude;
            if (_speed != null)
            {
                _speed.text = p != null && p.Has(p.MaxSpeed)
                    ? $"{speed:F1} / {p.MaxSpeed:G} m/s"
                    : $"{speed:F1} m/s";
                bool hot = p != null && p.Has(p.MaxSpeed) && speed > p.MaxSpeed * 0.98f;
                _speed.EnableInClassList("inspector-value--hot", hot);
            }

            float acc = snap.Acceleration.magnitude;
            if (_accel != null)
            {
                _accel.text = p != null && p.Has(p.LateralLimit)
                    ? $"{acc:F1} / {p.LateralLimit:G} m/s²"
                    : $"{acc:F1} m/s²";
                bool hot = p != null && p.Has(p.LateralLimit) && acc > p.LateralLimit * 0.98f;
                _accel.EnableInClassList("inspector-value--hot", hot);
            }
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

            _ctx.Clock.SeekJustBefore(rec.time);

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

        void RefreshLog()
        {
            int total = _logs != null ? _logs.Count : 0;
            // Only friendlies write a brain log. On everything else the section goes
            // away rather than showing an empty box that says so.
            if (_logSection != null)
                _logSection.style.display = total > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _logList?.SetSource(_logs);
        }

        /// <summary>
        /// Put the pinned drone back in the selection (and reopen the inspector).
        /// The panel stays open if you click away; this is how you find it again.
        /// </summary>
        void SelectPinnedDrone()
        {
            if (_beliefsSlot < 0 || _ctx?.Selection == null) return;
            _ctx.Selection.SelectOnly(_beliefsSlot);
            Open();
        }

        void RefreshBeliefsOpenBtn(EntityInfo info)
        {
            bool friendly = info != null && info.drone_id >= 0;
            if (_beliefsOpenBtn == null) return;
            _beliefsOpenBtn.style.display = friendly ? DisplayStyle.Flex : DisplayStyle.None;
            _beliefsOpenBtn.EnableInClassList("scene-state-toggle--open",
                _beliefsVisible && _beliefsSlot == _boundSlot);
        }

        void OpenBeliefsFromInspector()
        {
            if (_boundSlot < 0 || _ctx?.Run == null) return;
            if (_ctx.Run.Info(_boundSlot).drone_id < 0) return;
            OpenBeliefs(_boundSlot);
        }

        public void OpenBeliefs(int slot)
        {
            if (_ctx?.Run == null || slot < 0) return;
            var info = _ctx.Run.Info(slot);
            if (info.drone_id < 0) return;

            if (_beliefsPanel == null) TryWireUi();
            if (_beliefsPanel == null) return;

            _beliefsVisible = true;
            _beliefsSlot = slot;
            _beliefsLogs = _ctx.Run.LogsForDrone(info.drone_id);
            _callsFingerprint = int.MinValue;
            _seesFingerprint = int.MinValue;

            if (_beliefsTitle != null)
                _beliefsTitle.text = $"Beliefs · {info.Label}";
            if (_beliefsSubtitle != null)
                _beliefsSubtitle.text = $"Pinned · {KindName(info.Kind)} · slot {info.slot}. Stays if you pick someone else.";
            ApplyBeliefsChrome(info.Kind);

            if (_beliefsWindow != null)
                _beliefsWindow.Show();
            else
                _beliefsPanel.style.display = DisplayStyle.Flex;

            RefreshPinnedVizBtn();
            RefreshPinnedBeliefs(force: true);
            if (_boundSlot >= 0)
                RefreshBeliefsOpenBtn(_ctx.Run.Info(_boundSlot));
        }

        void HideBeliefs()
        {
            _beliefsVisible = false;
            _beliefsSlot = -1;
            _beliefsLogs = null;
            _callsFingerprint = int.MinValue;
            _seesFingerprint = int.MinValue;
            if (_beliefsWindow != null)
                _beliefsWindow.Hide();
            else if (_beliefsPanel != null)
                _beliefsPanel.style.display = DisplayStyle.None;
            if (_boundSlot >= 0 && _ctx?.Run != null)
                RefreshBeliefsOpenBtn(_ctx.Run.Info(_boundSlot));
        }

        void OnBeliefsHidden()
        {
            _beliefsVisible = false;
            _beliefsSlot = -1;
            _beliefsLogs = null;
            if (_boundSlot >= 0 && _ctx?.Run != null)
                RefreshBeliefsOpenBtn(_ctx.Run.Info(_boundSlot));
        }

        void TogglePinnedViz()
        {
            if (_ctx?.Selection == null || _ctx.Run == null || _beliefsSlot < 0) return;
            int drone = _ctx.Run.Info(_beliefsSlot).drone_id;
            if (drone < 0) return;
            bool showingThis = _ctx.Selection.IsBeliefViewOf(drone);
            _ctx.Selection.SetBeliefView(!showingThis, drone);
        }

        void RefreshPinnedVizBtn()
        {
            if (_beliefsVizBtn == null || _ctx?.Selection == null || _ctx.Run == null || _beliefsSlot < 0)
            {
                _beliefsVizBtn?.EnableInClassList("scene-state-toggle--open", false);
                return;
            }

            int drone = _ctx.Run.Info(_beliefsSlot).drone_id;
            bool on = _ctx.Selection.IsBeliefViewOf(drone);
            _beliefsVizBtn.EnableInClassList("scene-state-toggle--open", on);
            _beliefsVizBtn.text = on ? $"Coloring as {drone}" : "Color scene";
        }

        void ApplyBeliefsChrome(EntityKind kind)
        {
            Color c = Palette.Kind(kind);
            Palette.Border(_beliefsPanel, c);
            Palette.Fill(_beliefsKindDot, c);
        }

        void RefreshPinnedBeliefs(bool force)
        {
            if (!_beliefsVisible || _ctx?.Run == null || _beliefsSlot < 0) return;
            float t = _ctx.Clock.Time;
            var info = _ctx.Run.Info(_beliefsSlot);

            if (_beliefsIntent != null)
                _beliefsIntent.text = LogPhrase.StanceAt(_beliefsLogs, t);

            bool compromised = _ctx.State != null && _ctx.State.IsCompromisedNow(_beliefsSlot);
            if (_beliefsCallsHint != null)
                _beliefsCallsHint.text = $"this craft is {BeliefIndex.TruthLabel(info, compromised)}";

            _ctx.Run.Beliefs.FillSubject(_beliefsSlot, t, _callsBuf);
            int callsFp = FingerprintCalls(_callsBuf);
            if (force || callsFp != _callsFingerprint)
            {
                _callsFingerprint = callsFp;
                RebuildCallRows(info, compromised);
            }

            _ctx.Run.Beliefs.FillObserver(info.drone_id, t, _seesBuf);
            int seesFp = FingerprintSees(_seesBuf);
            if (force || seesFp != _seesFingerprint)
            {
                _seesFingerprint = seesFp;
                RebuildSeesRows(info.drone_id);
            }
        }

        void RebuildCallRows(EntityInfo subject, bool subjectCompromised)
        {
            if (_beliefsCallsScroll == null) return;
            _beliefsCallsScroll.contentContainer.Clear();
            if (_beliefsCallsHeader != null)
                _beliefsCallsHeader.text = _callsBuf.Count == 0
                    ? "Calls on this craft"
                    : $"Calls on this craft · {_callsBuf.Count}";

            if (_callsBuf.Count == 0)
            {
                AddInspectorEmpty(_beliefsCallsScroll, "Nobody has declared a class for this craft yet.");
                return;
            }

            string actual = BeliefIndex.TruthLabel(subject, subjectCompromised);
            for (int i = 0; i < _callsBuf.Count; i++)
            {
                int observer = _callsBuf[i].observer;
                int slot = _ctx.Run.SlotOfDrone(observer);
                string who = slot >= 0 ? _ctx.Run.Info(slot).Label : $"Drone {observer}";
                bool mismatch = !BeliefIndex.Agrees(_callsBuf[i].cls, subject, subjectCompromised);
                var row = BeliefRow(who, BeliefIndex.Label(_callsBuf[i].cls), actual,
                    _callsBuf[i].cls, mismatch);
                row.userData = observer;
                row.RegisterCallback<ClickEvent>(OnCallClicked);
                _beliefsCallsScroll.Add(row);
            }
        }

        void RebuildSeesRows(int selfDrone)
        {
            if (_beliefsSeesScroll == null) return;
            _beliefsSeesScroll.contentContainer.Clear();

            int shown = 0;
            for (int i = 0; i < _seesBuf.Count; i++)
            {
                int slot = _seesBuf[i].slot;
                if ((uint)slot >= (uint)_ctx.Run.SlotCount) continue;
                var other = _ctx.Run.Info(slot);
                if (other.drone_id == selfDrone) continue;
                shown++;
                bool compromised = _ctx.State != null && _ctx.State.IsCompromisedNow(slot);
                bool mismatch = !BeliefIndex.Agrees(_seesBuf[i].cls, other, compromised);
                var row = BeliefRow(other.Label, BeliefIndex.Label(_seesBuf[i].cls),
                    BeliefIndex.TruthLabel(other, compromised),
                    _seesBuf[i].cls, mismatch);
                row.userData = slot;
                row.RegisterCallback<ClickEvent>(OnSeesClicked);
                _beliefsSeesScroll.Add(row);
            }

            if (_beliefsSeesHeader != null)
                _beliefsSeesHeader.text = shown == 0 ? "What it has declared" : $"What it has declared · {shown}";

            if (shown == 0)
                AddInspectorEmpty(_beliefsSeesScroll, "No current declarations. Unknown is silent.");
        }

        void OnCallClicked(ClickEvent evt)
        {
            if (evt.currentTarget is VisualElement row && row.userData is int observer)
            {
                int slot = _ctx.Run.SlotOfDrone(observer);
                if (slot >= 0)
                    _ctx.Selection.SelectOnly(slot);
            }
            evt.StopPropagation();
        }

        void OnSeesClicked(ClickEvent evt)
        {
            if (evt.currentTarget is VisualElement row && row.userData is int slot)
                _ctx.Selection.SelectOnly(slot);
            evt.StopPropagation();
        }

        static VisualElement BeliefRow(string name, string called, string actual, BeliefClass cls, bool mismatch)
        {
            var row = new VisualElement();
            row.AddToClassList("inspector-belief");
            row.EnableInClassList("inspector-belief--mismatch", mismatch);

            var dot = new VisualElement();
            dot.AddToClassList("inspector-kind-dot");
            Palette.Fill(dot, Palette.Belief(cls));
            dot.pickingMode = PickingMode.Ignore;

            var left = new Label(name);
            left.AddToClassList("inspector-belief-name");
            left.pickingMode = PickingMode.Ignore;

            var calledLbl = new Label(called);
            calledLbl.AddToClassList("inspector-belief-called");
            calledLbl.pickingMode = PickingMode.Ignore;

            var actualLbl = new Label(actual);
            actualLbl.AddToClassList("inspector-belief-actual");
            actualLbl.pickingMode = PickingMode.Ignore;

            row.Add(dot);
            row.Add(left);
            row.Add(calledLbl);
            row.Add(actualLbl);
            return row;
        }

        static int FingerprintCalls(List<(int observer, BeliefClass cls)> list)
        {
            unchecked
            {
                int h = 17;
                for (int i = 0; i < list.Count; i++)
                    h = h * 31 + list[i].observer * 8 + (int)list[i].cls;
                return h;
            }
        }

        static int FingerprintSees(List<(int slot, BeliefClass cls)> list)
        {
            unchecked
            {
                int h = 17;
                for (int i = 0; i < list.Count; i++)
                    h = h * 31 + list[i].slot * 8 + (int)list[i].cls;
                return h;
            }
        }

        static void AddInspectorEmpty(ScrollView scroll, string text)
        {
            var empty = new Label(text);
            empty.AddToClassList("inspector-empty");
            empty.pickingMode = PickingMode.Ignore;
            scroll.Add(empty);
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
