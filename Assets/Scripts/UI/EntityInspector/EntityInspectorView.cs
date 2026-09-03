// Draggable per-entity inspectors. Enter opens a window for every selected
// craft; double-click opens one. Windows stay on their drone — changing the
// primary does not retarget them, so several can be up at once.

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
        VisualElement _host;
        VisualElement _stockPanel;
        VisualTreeAsset _inspectorAsset;
        bool _uiWired;
        int _boundSlot = -1;

        readonly List<Card> _cards = new();

        VisualElement _panel;
        VisualElement _kindDot;
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
        Label _fix;
        Label _rangeSigma;
        Label _bearingSigma;
        VisualElement _compromisedRow;
        Label _compromised;
        Button _beliefsOpenBtn;
        Button _eventsToggleBtn;
        Label _eventsHeader;
        VisualElement _eventsContent;
        ScrollView _eventsScroll;
        Button _logToggleBtn;
        VisualElement _logSection;
        VisualElement _logContent;
        LogList _logList;
        HeadingPreview _headingViz;

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
        List<VisualElement> _eventRows;
        IReadOnlyList<EntityEventRecord> _events;
        IReadOnlyList<LogLine> _logs;
        int _nowEventIndex = -1;

        sealed class Card
        {
            public int Slot;
            public VisualElement Panel;
            public VisualElement KindDot;
            public Label Title, Subtitle, Status, Speed, Accel, Altitude, Position, Heading;
            public Label Kind, SlotLbl, Trace, Drone, Lifetime, KillRadius, Sense, Comm, Sep, Fix;
            public Label RangeSigma, BearingSigma, Compromised;
            public VisualElement CompromisedRow, LogSection, LogContent, EventsContent;
            public Button BeliefsOpenBtn, EventsToggleBtn, LogToggleBtn;
            public Label EventsHeader;
            public ScrollView EventsScroll;
            public LogList Logs;
            public HeadingPreview HeadingViz;
            public readonly List<VisualElement> EventRows = new();
            public IReadOnlyList<EntityEventRecord> Events;
            public IReadOnlyList<LogLine> Lines;
            public int NowEventIndex = -1;
            public FloatingPanel Window;
            public bool EventsCollapsed;
            public bool LogCollapsed;
        }

        void OnEnable() => TryWireUi();
        void Start() => TryWireUi();

        public void Bind(ViewerContext ctx)
        {
            Unhook();
            _ctx = ctx;
            TryWireUi();
            DiscardAll();
            Hook();
            HideBeliefs();
        }

        /// <summary>Opens a window for the current primary. Does not close others.</summary>
        public void Open()
        {
            if (_ctx?.Selection == null) return;
            int slot = _ctx.Selection.Primary;
            if (slot < 0) return;
            OpenSlot(slot);
        }

        /// <summary>Enter: a window for every selected craft. Does not close any.</summary>
        public void OpenSelected()
        {
            if (_ctx?.Selection == null) return;
            var slots = _ctx.Selection.Slots;
            for (int i = 0; i < slots.Count; i++)
                OpenSlot(slots[i]);
        }

        /// <summary>
        /// Inspectors for both ends of a log ping, then scroll the writer's
        /// log to that line. The subject inspector opens even if it has no log.
        /// </summary>
        public void OpenPing(int fromSlot, int toSlot, LogLine line)
        {
            if (toSlot >= 0 && toSlot != fromSlot)
                OpenSlot(toSlot);
            OpenSlot(fromSlot);
            FindCard(fromSlot)?.Logs?.Reveal(line);
            if (toSlot >= 0 && toSlot != fromSlot)
                FindCard(toSlot)?.Logs?.Reveal(line);
        }

        public void Close() => DiscardAll();

        void OpenSlot(int slot)
        {
            if (_ctx?.Run == null || slot < 0) return;
            if (_host == null) TryWireUi();
            var card = FindCard(slot);
            if (card == null)
            {
                card = SpawnCard(slot);
                if (card == null) return;
                _cards.Add(card);
            }
            Activate(card);
            card.Window?.Show();
            BindSlot(slot, force: true);
        }

        Card FindCard(int slot)
        {
            for (int i = 0; i < _cards.Count; i++)
                if (_cards[i].Slot == slot) return _cards[i];
            return null;
        }

        void DiscardAll()
        {
            for (int i = _cards.Count - 1; i >= 0; i--)
                Discard(_cards[i]);
            _boundSlot = -1;
            _events = null;
            _logs = null;
            _eventRows = null;
            _nowEventIndex = -1;
        }

        void Discard(Card card)
        {
            if (card == null) return;
            _cards.Remove(card);
            card.Window?.Detach();
            if (card.Panel != null && card.Panel != _stockPanel)
                card.Panel.RemoveFromHierarchy();
            else if (card.Panel == _stockPanel)
                card.Panel.style.display = DisplayStyle.None;
        }

        void Activate(Card c)
        {
            _boundSlot = c.Slot;
            _panel = c.Panel;
            _kindDot = c.KindDot;
            _title = c.Title;
            _subtitle = c.Subtitle;
            _status = c.Status;
            _speed = c.Speed;
            _accel = c.Accel;
            _altitude = c.Altitude;
            _position = c.Position;
            _heading = c.Heading;
            _kind = c.Kind;
            _slot = c.SlotLbl;
            _trace = c.Trace;
            _drone = c.Drone;
            _lifetime = c.Lifetime;
            _killRadius = c.KillRadius;
            _sense = c.Sense;
            _comm = c.Comm;
            _sep = c.Sep;
            _fix = c.Fix;
            _rangeSigma = c.RangeSigma;
            _bearingSigma = c.BearingSigma;
            _compromisedRow = c.CompromisedRow;
            _compromised = c.Compromised;
            _beliefsOpenBtn = c.BeliefsOpenBtn;
            _eventsHeader = c.EventsHeader;
            _eventsScroll = c.EventsScroll;
            _eventsToggleBtn = c.EventsToggleBtn;
            _eventsContent = c.EventsContent;
            _logSection = c.LogSection;
            _logToggleBtn = c.LogToggleBtn;
            _logContent = c.LogContent;
            _logList = c.Logs;
            _headingViz = c.HeadingViz;
            _eventRows = c.EventRows;
            _events = c.Events;
            _logs = c.Lines;
            _nowEventIndex = c.NowEventIndex;
            ApplyFoldState(c);
        }

        void StoreBound(Card c)
        {
            c.Events = _events;
            c.Lines = _logs;
            c.NowEventIndex = _nowEventIndex;
        }

        VisualTreeAsset FindInspectorAsset()
        {
            for (var el = _stockPanel; el != null; el = el.parent)
            {
                var src = el.visualTreeAssetSource;
                if (IsInspectorUxml(src)) return src;
                if (el is TemplateContainer tc && IsInspectorUxml(tc.templateSource))
                    return tc.templateSource;
            }
            var inst = _root != null ? _root.Q("entityInspectorInstance") : null;
            if (inst is TemplateContainer boxed && IsInspectorUxml(boxed.templateSource))
                return boxed.templateSource;
            return null;
        }

        static bool IsInspectorUxml(VisualTreeAsset src) =>
            src != null && src.name.IndexOf("EntityInspector", System.StringComparison.OrdinalIgnoreCase) >= 0;

        Card SpawnCard(int slot)
        {
            if (_host == null) return null;
            VisualElement panel = null;
            if (_inspectorAsset != null)
            {
                var tree = _inspectorAsset.Instantiate();
                panel = tree.Q<VisualElement>("inspectorPanel");
                if (panel != null)
                {
                    panel.RemoveFromHierarchy();
                    _host.Add(panel);
                }
            }
            if (panel == null && _stockPanel != null)
            {
                bool taken = false;
                for (int i = 0; i < _cards.Count; i++)
                    if (_cards[i].Panel == _stockPanel) { taken = true; break; }
                if (!taken)
                    panel = _stockPanel;
            }
            if (panel == null)
            {
                Debug.LogWarning("[viewer] EntityInspectorView: could not clone inspector panel.");
                return null;
            }

            panel.name = "inspector-slot-" + slot;
            panel.style.display = DisplayStyle.None;
            int n = _cards.Count;
            panel.style.left = 14 + n * 28;
            panel.style.top = 52 + n * 28;

            var c = WireCard(panel, slot);
            c.Window = new FloatingPanel();
            c.Window.Attach(panel, panel.Q("inspectorDragHandle"), panel.Q<Button>("inspectorCloseBtn"));
            return c;
        }

        Card WireCard(VisualElement panel, int slot)
        {
            var c = new Card { Slot = slot, Panel = panel };
            UiQuery.ButtonsReceiveHover(panel);
            c.KindDot = UiQuery.Named<VisualElement>(panel, "inspectorKindDot");
            c.Title = UiQuery.Named<Label>(panel, "inspectorTitle");
            c.Subtitle = UiQuery.Named<Label>(panel, "inspectorSubtitle");
            c.Status = UiQuery.Named<Label>(panel, "inspectorStatus");
            c.Speed = UiQuery.Named<Label>(panel, "inspectorSpeed");
            c.Accel = UiQuery.Named<Label>(panel, "inspectorAccel");
            c.Altitude = UiQuery.Named<Label>(panel, "inspectorAltitude");
            c.Position = UiQuery.Named<Label>(panel, "inspectorPosition");
            c.Heading = UiQuery.Named<Label>(panel, "inspectorHeading");
            c.Kind = UiQuery.Named<Label>(panel, "inspectorKind");
            c.SlotLbl = UiQuery.Named<Label>(panel, "inspectorSlot");
            c.Trace = UiQuery.Named<Label>(panel, "inspectorTrace");
            c.Drone = UiQuery.Named<Label>(panel, "inspectorDrone");
            c.Lifetime = UiQuery.Named<Label>(panel, "inspectorLifetime");
            c.KillRadius = UiQuery.Named<Label>(panel, "inspectorKillRadius");
            c.Sense = UiQuery.Named<Label>(panel, "inspectorSense");
            c.Comm = UiQuery.Named<Label>(panel, "inspectorComm");
            c.Sep = UiQuery.Named<Label>(panel, "inspectorSep");
            c.Fix = UiQuery.Named<Label>(panel, "inspectorFix");
            c.RangeSigma = UiQuery.Named<Label>(panel, "inspectorRangeSigma");
            c.BearingSigma = UiQuery.Named<Label>(panel, "inspectorBearingSigma");
            c.CompromisedRow = UiQuery.Named<VisualElement>(panel, "inspectorCompromisedRow");
            c.Compromised = UiQuery.Named<Label>(panel, "inspectorCompromised");
            c.BeliefsOpenBtn = UiQuery.Named<Button>(panel, "inspectorBeliefsBtn");
            c.EventsHeader = UiQuery.Named<Label>(panel, "inspectorEventsHeader");
            c.EventsToggleBtn = UiQuery.Named<Button>(panel, "inspectorEventsToggleBtn");
            c.EventsContent = UiQuery.Named<VisualElement>(panel, "inspectorEventsContent");
            c.EventsScroll = UiQuery.Named<ScrollView>(panel, "inspectorEventsScroll");
            c.LogSection = UiQuery.Named<VisualElement>(panel, "inspectorLogSection");
            c.LogToggleBtn = UiQuery.Named<Button>(panel, "inspectorLogToggleBtn");
            c.LogContent = UiQuery.Named<VisualElement>(panel, "inspectorLogContent");
            var logHeader = UiQuery.Named<Label>(panel, "inspectorLogHeader");
            var logScroll = UiQuery.Named<ScrollView>(panel, "inspectorLogScroll");
            c.Logs = new LogList(
                logScroll,
                logHeader,
                null,
                UiQuery.Named<VisualElement>(panel, "inspectorLogVerbs"),
                UiQuery.Named<Button>(panel, "inspectorLogRawBtn"))
            {
                ShowDrone = false,
                SelectOnJump = false,
                FollowNow = true,
                Title = "Log",
                LeadIn = eventLeadIn,
            };
            if (_ctx != null)
                c.Logs.Bind(_ctx);

            c.HeadingViz = new HeadingPreview();
            c.HeadingViz.name = "inspectorHeadingViz";
            c.HeadingViz.AddToClassList("inspector-heading-viz");
            int captured = slot;
            c.HeadingViz.Clicked += () => _ctx?.Selection?.SelectOnly(captured);
            var visualSlot = panel.Q("inspectorVisualSlot");
            if (visualSlot != null)
                visualSlot.Add(c.HeadingViz);
            else if (c.Subtitle?.parent != null)
            {
                int at = c.Subtitle.parent.IndexOf(c.Subtitle);
                c.Subtitle.parent.Insert(at + 1, c.HeadingViz);
            }
            else
                panel.Insert(1, c.HeadingViz);

            if (c.BeliefsOpenBtn != null)
                c.BeliefsOpenBtn.clicked += () => OpenBeliefs(captured);
            if (c.EventsToggleBtn != null)
                c.EventsToggleBtn.clicked += () => ToggleEvents(c);
            if (c.LogToggleBtn != null)
                c.LogToggleBtn.clicked += () => ToggleLogs(c);
            return c;
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
            _host = root.Q("inspectorRoot") ?? root;
            _stockPanel = root.Q<VisualElement>("inspectorPanel");
            if (_stockPanel != null)
                _stockPanel.style.display = DisplayStyle.None;
            _inspectorAsset = FindInspectorAsset();

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
            if (_beliefsPanel != null && !_beliefsVisible)
                _beliefsPanel.style.display = DisplayStyle.None;
            _uiWired = true;
        }

        void RegisterCallbacks()
        {
            if (_beliefsPanel != null)
            {
                _beliefsWindow = new FloatingPanel();
                _beliefsWindow.Attach(_beliefsPanel, _beliefsDragHandle, _beliefsCloseBtn);
                _beliefsWindow.Hidden += OnBeliefsHidden;
                _beliefsWindow.Shown += OnBeliefsShown;
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

        void OnDestroy()
        {
            Unhook();
            DiscardAll();
        }

        void OnStateChanged()
        {
            for (int i = 0; i < _cards.Count; i++)
            {
                var card = _cards[i];
                if (card.Window == null || !card.Window.IsShown) continue;
                Activate(card);
                RefreshLive();
                _logList?.Highlight();
                UpdateEventHighlights();
                StoreBound(card);
            }
            if (_beliefsVisible)
                RefreshPinnedBeliefs(force: false);
        }

        void OnViewModeChanged(ViewMode _) => RefreshPinnedVizBtn();
        void OnObserverChanged(int _) => RefreshPinnedVizBtn();

        void OnSelectionChanged(int _)
        {
            // Inspectors are pinned to the drone they were opened on.
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
            if (_fix != null)
            {
                if (p != null && p.Has(p.FixSigma))
                {
                    _fix.text = $"{p.FixSigma:G} m";
                    _fix.tooltip = "Own-position 1-sigma (params fix=). About 35 cm — too small to draw as a sphere, so it lives here.";
                }
                else
                {
                    _fix.text = "—";
                    _fix.tooltip = "params fix= was not logged on this run.";
                }
            }
            if (_rangeSigma != null)
            {
                if (p != null && p.RangeSigma > 0f)
                {
                    _rangeSigma.text = $"{p.RangeSigma:G} m";
                    _rangeSigma.tooltip = "Incoming radio range 1-sigma (radio range_sigma=). Physical — a compromised drone cannot lie about this.";
                }
                else
                {
                    _rangeSigma.text = "—";
                    _rangeSigma.tooltip = "radio range_sigma= was not logged on this run. Reload after a sim that received a frame.";
                }
            }
            if (_bearingSigma != null)
            {
                if (p != null && p.BearingSigma > 0f)
                {
                    _bearingSigma.text = $"{p.BearingSigma:G} rad";
                    _bearingSigma.tooltip = "Incoming radio bearing 1-sigma (radio bearing_sigma=), world NED.";
                }
                else
                {
                    _bearingSigma.text = "—";
                    _bearingSigma.tooltip = "radio bearing_sigma= was not logged on this run. Reload after a sim that received a frame.";
                }
            }

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
            var card = FindCard(slot);
            if (card != null) StoreBound(card);
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
                _headingViz?.Set(Vector3.zero, Palette.Kind(info.Kind), false);
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

            Color body = _ctx.State.IsCompromisedNow(_boundSlot)
                ? Palette.Compromised
                : Palette.Kind(info.Kind);
            var asset = _ctx.Run.Meta?.asset;
            bool hasAsset = asset?.position != null && asset.position.Length >= 3;
            Vector3 assetPos = hasAsset
                ? new Vector3(asset.position[0], asset.position[1], asset.position[2])
                : Vector3.zero;
            _headingViz?.Set(snap.Rotation, body, snap.Alive, hasAsset,
                             snap.Position, assetPos);
        }

        static void SetDash(Label label)
        {
            if (label != null) label.text = "—";
        }

        void RebuildEvents()
        {
            _eventRows?.Clear();
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

                row.userData = rec;
                int eventSlot = _boundSlot;
                row.RegisterCallback<ClickEvent>(evt =>
                {
                    if (evt.currentTarget is VisualElement el && el.userData is EntityEventRecord r)
                        JumpToEvent(r, eventSlot);
                    evt.StopPropagation();
                });
                _eventsScroll.Add(row);
                _eventRows?.Add(row);
            }

            UpdateEventHighlights();
        }

        void JumpToEvent(EntityEventRecord rec, int slot)
        {
            if (_ctx == null || rec == null || slot < 0) return;

            _ctx.Clock.SeekJustBefore(rec.time);

            var sel = _ctx.Selection;
            sel.SelectOnly(slot);
            if (rec.otherSlots == null) return;
            for (int i = 0; i < rec.otherSlots.Length; i++)
                sel.Add(rec.otherSlots[i]);
        }

        void UpdateEventHighlights()
        {
            if (_ctx == null || _eventRows == null || _eventRows.Count == 0) return;
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

        void ToggleEvents(Card card)
        {
            if (card == null) return;
            card.EventsCollapsed = !card.EventsCollapsed;
            ApplyFoldState(card);
        }

        void ToggleLogs(Card card)
        {
            if (card == null) return;
            card.LogCollapsed = !card.LogCollapsed;
            ApplyFoldState(card);
        }

        static void ApplyFoldState(Card card)
        {
            if (card.EventsContent != null)
                card.EventsContent.style.display = card.EventsCollapsed
                    ? DisplayStyle.None : DisplayStyle.Flex;
            if (card.EventsToggleBtn != null)
                card.EventsToggleBtn.text = card.EventsCollapsed ? "+" : "–";
            if (card.LogContent != null)
                card.LogContent.style.display = card.LogCollapsed
                    ? DisplayStyle.None : DisplayStyle.Flex;
            if (card.LogToggleBtn != null)
                card.LogToggleBtn.text = card.LogCollapsed ? "+" : "–";
        }

        /// <summary>
        /// Put the pinned drone back in the selection (and reopen the inspector).
        /// The panel stays open if you click away; this is how you find it again.
        /// </summary>
        void SelectPinnedDrone()
        {
            if (_beliefsSlot < 0 || _ctx?.Selection == null) return;
            _ctx.Selection.SelectOnly(_beliefsSlot);
            OpenSlot(_beliefsSlot);
        }

        void RefreshBeliefsOpenBtn(EntityInfo info)
        {
            bool friendly = info != null && info.drone_id >= 0;
            if (_beliefsOpenBtn == null) return;
            _beliefsOpenBtn.style.display = friendly ? DisplayStyle.Flex : DisplayStyle.None;
            _beliefsOpenBtn.EnableInClassList("scene-state-toggle--open",
                _beliefsVisible && _beliefsSlot == _boundSlot);
        }

        void RefreshAllBeliefsButtons()
        {
            if (_ctx?.Run == null) return;
            for (int i = 0; i < _cards.Count; i++)
            {
                Activate(_cards[i]);
                RefreshBeliefsOpenBtn(_ctx.Run.Info(_cards[i].Slot));
                StoreBound(_cards[i]);
            }
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
            RefreshAllBeliefsButtons();
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
            RefreshAllBeliefsButtons();
        }

        void OnBeliefsHidden()
        {
            _beliefsVisible = false;
            RefreshAllBeliefsButtons();
        }

        void OnBeliefsShown()
        {
            _beliefsVisible = true;
            RefreshPinnedVizBtn();
            if (_beliefsSlot >= 0)
                RefreshPinnedBeliefs(force: true);
            RefreshAllBeliefsButtons();
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
