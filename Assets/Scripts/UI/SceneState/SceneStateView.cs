// Aircraft, Events, Score, Logs, and Cues each get their own floating window. Open from the
// matching button in the scale bar. Aircraft list supports click / Ctrl-toggle /
// Shift-range. The Called column is what the current observer declared; Color
// paints the 3D view that way (B is the same toggle).

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace SwarmViewer
{
    public sealed class SceneStateView : MonoBehaviour, IRunView
    {
        enum EventSort { Time, Kind, Severity, Involved, Text }
        enum AircraftSort { Name, Kind, Called, Slot, Speed, Status }
        enum LogSort { Time, Drone, Text }

        struct AircraftRow
        {
            public int Slot;
            public VisualElement Root;
            public VisualElement Dot;
            public Label Called;
            public Label Speed;
            public Label Status;
        }

        [SerializeField] UIDocument uiDocument;
        [SerializeField] EntityInspectorView inspector;
        [Tooltip("Seconds before a log line to land when its row is clicked. Events jump one recorded frame earlier so the craft is still there.")]
        [SerializeField] float eventLeadIn = 2f;

        ViewerContext _ctx;
        VisualElement _root;
        bool _uiWired;

        Button _aircraftOpenBtn;
        Button _eventsOpenBtn;
        Button _logsOpenBtn;
        Button _cuesOpenBtn;
        Button _cuesMuteBtn;

        FloatingPanel _aircraftFloat;
        FloatingPanel _eventsFloat;
        FloatingPanel _logsFloat;
        FloatingPanel _cuesFloat;
        bool _aircraftVisible;
        bool _eventsVisible;
        bool _logsVisible;
        bool _cuesVisible;

        Label _aircraftHeader;
        Label _aircraftMix;
        TextField _aircraftFilter;
        FilterChips _aircraftKindChips;
        FilterChips _aircraftStatusChips;
        Button _aircraftColName;
        Button _aircraftColKind;
        Button _aircraftColCalled;
        Button _aircraftColSlot;
        Button _aircraftColSpeed;
        Button _aircraftColStatus;
        Button _aircraftBeliefBtn;
        ScrollView _aircraftScroll;

        Label _eventsHeader;
        TextField _eventsFilter;
        FilterChips _eventKindChips;
        Button _eventsColTime;
        Button _eventsColKind;
        Button _eventsColSev;
        Button _eventsColWho;
        Button _eventsColText;
        ScrollView _eventsScroll;

        Label _logsHeader;
        TextField _logsFilter;
        FilterChips _logDroneChips;
        Button _logsColTime;
        Button _logsColDrone;
        Button _logsColText;
        ScrollView _logsScroll;
        LogList _logList;

        VisualElement _cuesChipRow;
        Label _cuesFooter;
        VisualElement _cuesDetailSwatch;
        Label _cuesDetailTitle;
        Label _cuesDetailShape;
        VisualElement _cuesDetailKey;
        Label _cuesDetailDraws;
        Label _cuesDetailSource;
        Button[] _cueChips;
        CueOverlay _cues;
        int _cueExplained = -1;
        int _pingExplained = -1;

        readonly List<AircraftRow> _aircraftRows = new();
        readonly List<int> _displayedAircraft = new();
        readonly List<EventInfo> _filteredEvents = new();
        readonly List<VisualElement> _eventRows = new();
        readonly Dictionary<int, int> _droneToSlot = new();

        AircraftSort _aircraftSort = AircraftSort.Kind;
        bool _aircraftSortAsc = true;
        int _aircraftAnchorSlot = -1;
        int _lastAircraftClickSlot = -1;
        float _lastAircraftClickTime;

        EventSort _eventSort = EventSort.Time;
        bool _eventSortAsc = true;
        EventInfo _nowEvent;
        bool _eventHighlightDirty;

        LogSort _logSort = LogSort.Time;
        bool _logSortAsc = true;
        int _aliveFingerprint = int.MinValue;

        void OnEnable() => TryWireUi();
        void Start() => TryWireUi();

        public void Bind(ViewerContext ctx)
        {
            Unhook();
            _ctx = ctx;
            _aircraftAnchorSlot = -1;
            _lastAircraftClickSlot = -1;
            _aliveFingerprint = int.MinValue;
            TryWireUi();
            Hook();
            RebuildDroneMap();
            RebuildFilterChoices();
            if (_logList != null)
            {
                _logList.Bind(_ctx);
                _logList.LeadIn = eventLeadIn;
                _logList.SlotOfDrone = id => _droneToSlot.TryGetValue(id, out int s) ? s : -1;
                _logList.SetSource(_ctx?.Run?.Meta?.logs);
            }
            if (_aircraftVisible) RebuildAircraft();
            if (_eventsVisible) RebuildEvents();
            if (_logsVisible) RefreshLogHeaders();
            RefreshAircraftBeliefBtn();
            RefreshCueChips();
        }

        void TryWireUi()
        {
            if (uiDocument == null)
                uiDocument = GetComponent<UIDocument>();
            if (inspector == null)
            {
                inspector = GetComponent<EntityInspectorView>();
                if (inspector == null)
                {
                    var found = FindObjectsByType<EntityInspectorView>(FindObjectsInactive.Include);
                    if (found.Length > 0) inspector = found[0];
                }
            }
            if (uiDocument == null)
            {
                Debug.LogWarning("[viewer] SceneStateView: no UIDocument.");
                return;
            }

            var root = uiDocument.rootVisualElement;
            if (root == null)
            {
                Debug.LogWarning("[viewer] SceneStateView: rootVisualElement is null — UIDocument not ready yet.");
                return;
            }

            if (_uiWired && _root == root) return;

            _root = root;
            _aircraftOpenBtn = UiQuery.Named<Button>(_root, "aircraftOpenBtn");
            _eventsOpenBtn = UiQuery.Named<Button>(_root, "sceneStateOpenBtn");
            _logsOpenBtn = UiQuery.Named<Button>(_root, "logsOpenBtn");
            _cuesOpenBtn = UiQuery.Named<Button>(_root, "cuesOpenBtn");
            _cuesMuteBtn = UiQuery.Named<Button>(_root, "cuesMuteBtn");

            var aircraftPanel = UiQuery.Named<VisualElement>(_root, "aircraftPanel");
            var eventsPanel = UiQuery.Named<VisualElement>(_root, "eventsPanel");
            var logsPanel = UiQuery.Named<VisualElement>(_root, "logsPanel");
            var cuesPanel = UiQuery.Named<VisualElement>(_root, "cuesPanel");

            _aircraftHeader = UiQuery.Named<Label>(_root, "aircraftHeader");
            _aircraftMix = UiQuery.Named<Label>(_root, "aircraftMix");
            _aircraftFilter = UiQuery.Named<TextField>(_root, "aircraftFilter");
            _aircraftKindChips = new FilterChips(UiQuery.Named<VisualElement>(_root, "aircraftKindChips"));
            _aircraftStatusChips = new FilterChips(UiQuery.Named<VisualElement>(_root, "aircraftStatusChips"));
            _aircraftColName = UiQuery.Named<Button>(_root, "aircraftColName");
            _aircraftColKind = UiQuery.Named<Button>(_root, "aircraftColKind");
            _aircraftColCalled = UiQuery.Named<Button>(_root, "aircraftColCalled");
            _aircraftColSlot = UiQuery.Named<Button>(_root, "aircraftColSlot");
            _aircraftColSpeed = UiQuery.Named<Button>(_root, "aircraftColSpeed");
            _aircraftColStatus = UiQuery.Named<Button>(_root, "aircraftColStatus");
            _aircraftBeliefBtn = UiQuery.Named<Button>(_root, "aircraftBeliefBtn");
            _aircraftScroll = UiQuery.Named<ScrollView>(_root, "aircraftScroll");

            _eventsHeader = UiQuery.Named<Label>(_root, "eventsHeader");
            _eventsFilter = UiQuery.Named<TextField>(_root, "eventsFilter");
            _eventKindChips = new FilterChips(UiQuery.Named<VisualElement>(_root, "eventKindChips"));
            _eventsColTime = UiQuery.Named<Button>(_root, "eventsColTime");
            _eventsColKind = UiQuery.Named<Button>(_root, "eventsColKind");
            _eventsColSev = UiQuery.Named<Button>(_root, "eventsColSev");
            _eventsColWho = UiQuery.Named<Button>(_root, "eventsColWho");
            _eventsColText = UiQuery.Named<Button>(_root, "eventsColText");
            _eventsScroll = UiQuery.Named<ScrollView>(_root, "eventsScroll");

            _logsHeader = UiQuery.Named<Label>(_root, "logsHeader");
            _logsFilter = UiQuery.Named<TextField>(_root, "logsFilter");
            _logDroneChips = new FilterChips(UiQuery.Named<VisualElement>(_root, "logDroneChips"));
            _logsColTime = UiQuery.Named<Button>(_root, "logsColTime");
            _logsColDrone = UiQuery.Named<Button>(_root, "logsColDrone");
            _logsColText = UiQuery.Named<Button>(_root, "logsColText");
            _logsScroll = UiQuery.Named<ScrollView>(_root, "logsScroll");
            _logList = new LogList(
                _logsScroll,
                _logsHeader,
                _logsFilter,
                UiQuery.Named<VisualElement>(_root, "logVerbChips"),
                UiQuery.Named<Button>(_root, "logsRawBtn"))
            {
                ShowDrone = true,
                Title = "Logs",
                LeadIn = eventLeadIn,
                Allow = line => _logDroneChips == null || _logDroneChips.Allows(DroneChip(line.drone)),
                Compare = CompareLogs,
            };

            _cuesChipRow = UiQuery.Named<VisualElement>(_root, "cuesChipRow");
            _cuesFooter = UiQuery.Named<Label>(_root, "cuesFooter");
            _cuesDetailSwatch = UiQuery.Named<VisualElement>(_root, "cuesDetailSwatch");
            _cuesDetailTitle = UiQuery.Named<Label>(_root, "cuesDetailTitle");
            _cuesDetailShape = UiQuery.Named<Label>(_root, "cuesDetailShape");
            _cuesDetailKey = UiQuery.Named<VisualElement>(_root, "cuesDetailKey");
            _cuesDetailDraws = UiQuery.Named<Label>(_root, "cuesDetailDraws");
            _cuesDetailSource = UiQuery.Named<Label>(_root, "cuesDetailSource");
            HideDetailChrome();

            _aircraftFloat = AttachWindow(aircraftPanel, "aircraftDragHandle", "aircraftCloseBtn",
                () => { _aircraftVisible = false; SetOpen(_aircraftOpenBtn, false); },
                () =>
                {
                    _aircraftVisible = true;
                    SetOpen(_aircraftOpenBtn, true);
                    RebuildAircraft();
                    RefreshAircraftBeliefBtn();
                });
            _eventsFloat = AttachWindow(eventsPanel, "eventsDragHandle", "eventsCloseBtn",
                () => { _eventsVisible = false; SetOpen(_eventsOpenBtn, false); },
                () =>
                {
                    _eventsVisible = true;
                    SetOpen(_eventsOpenBtn, true);
                    RebuildEvents();
                });
            _logsFloat = AttachWindow(logsPanel, "logsDragHandle", "logsCloseBtn",
                () => { _logsVisible = false; SetOpen(_logsOpenBtn, false); },
                () =>
                {
                    _logsVisible = true;
                    SetOpen(_logsOpenBtn, true);
                    RebuildLogs();
                });
            _cuesFloat = AttachWindow(cuesPanel, "cuesDragHandle", "cuesCloseBtn",
                () => { _cuesVisible = false; SetOpen(_cuesOpenBtn, false); },
                () =>
                {
                    _cuesVisible = true;
                    SetOpen(_cuesOpenBtn, true);
                    RefreshCueChips();
                });

            RegisterCallbacks();
            _uiWired = true;
        }

        FloatingPanel AttachWindow(VisualElement panel, string dragName, string closeName, Action onHidden, Action onShown)
        {
            if (panel == null) return null;
            var handle = UiQuery.Named<VisualElement>(_root, dragName);
            var close = UiQuery.Named<Button>(_root, closeName);
            var window = new FloatingPanel();
            window.Attach(panel, handle, close);
            window.Hidden += onHidden;
            window.Shown += onShown;
            return window;
        }

        void RegisterCallbacks()
        {
            if (_aircraftOpenBtn != null) _aircraftOpenBtn.clicked += ToggleAircraft;
            if (_eventsOpenBtn != null) _eventsOpenBtn.clicked += ToggleEvents;
            if (_logsOpenBtn != null) _logsOpenBtn.clicked += ToggleLogs;
            if (_cuesOpenBtn != null)
            {
                _cuesOpenBtn.tooltip = "Range rings and motion arrows   C";
                _cuesOpenBtn.clicked += ToggleCues;
            }
            if (_cuesMuteBtn != null)
            {
                _cuesMuteBtn.tooltip = "Temporarily hide every overlay. Chips stay as they were; Show brings them back. Shift+C.";
                _cuesMuteBtn.clicked += ToggleCueMute;
            }
            BuildCueChips();

            if (_aircraftFilter != null)
                _aircraftFilter.RegisterValueChangedCallback(_ => { if (_aircraftVisible) RebuildAircraft(); });
            if (_aircraftKindChips != null)
                _aircraftKindChips.Changed += () => { if (_aircraftVisible) RebuildAircraft(); };
            if (_aircraftStatusChips != null)
                _aircraftStatusChips.Changed += () => { if (_aircraftVisible) RebuildAircraft(); };
            if (_aircraftColName != null)
            {
                _aircraftColName.clicked += () => SortAircraft(AircraftSort.Name);
                _aircraftColName.tooltip = "Brain id for friendlies (Drone 0…n−1), same number as the Logs column. Hostiles and civilians use the simulator entity id (1-based).";
            }
            if (_aircraftColKind != null) _aircraftColKind.clicked += () => SortAircraft(AircraftSort.Kind);
            if (_aircraftColCalled != null) _aircraftColCalled.clicked += () => SortAircraft(AircraftSort.Called);
            if (_aircraftColSlot != null)
            {
                _aircraftColSlot.clicked += () => SortAircraft(AircraftSort.Slot);
                _aircraftColSlot.tooltip = "Viewer column in the recording, 0-based. For the initial fleet this equals the drone id. It is not the simulator entity id (that is one higher).";
            }
            if (_aircraftColSpeed != null) _aircraftColSpeed.clicked += () => SortAircraft(AircraftSort.Speed);
            if (_aircraftColStatus != null) _aircraftColStatus.clicked += () => SortAircraft(AircraftSort.Status);
            if (_aircraftBeliefBtn != null) _aircraftBeliefBtn.clicked += ToggleAircraftBeliefs;

            if (_eventsFilter != null)
                _eventsFilter.RegisterValueChangedCallback(_ => { if (_eventsVisible) RebuildEvents(); });
            if (_eventKindChips != null)
                _eventKindChips.Changed += () => { if (_eventsVisible) RebuildEvents(); };
            if (_eventsColTime != null) _eventsColTime.clicked += () => SortEvents(EventSort.Time);
            if (_eventsColKind != null) _eventsColKind.clicked += () => SortEvents(EventSort.Kind);
            if (_eventsColSev != null) _eventsColSev.clicked += () => SortEvents(EventSort.Severity);
            if (_eventsColWho != null) _eventsColWho.clicked += () => SortEvents(EventSort.Involved);
            if (_eventsColText != null) _eventsColText.clicked += () => SortEvents(EventSort.Text);

            if (_logDroneChips != null)
                _logDroneChips.Changed += () => { if (_logsVisible) RebuildLogs(); };
            if (_logsColTime != null) _logsColTime.clicked += () => SortLogs(LogSort.Time);
            if (_logsColDrone != null) _logsColDrone.clicked += () => SortLogs(LogSort.Drone);
            if (_logsColText != null) _logsColText.clicked += () => SortLogs(LogSort.Text);
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

        void ToggleAircraft()
        {
            if (_aircraftVisible) _aircraftFloat?.Hide();
            else OpenAircraft();
        }

        void ToggleEvents()
        {
            if (_eventsVisible) _eventsFloat?.Hide();
            else OpenEvents();
        }

        void ToggleLogs()
        {
            if (_logsVisible) _logsFloat?.Hide();
            else OpenLogs();
        }

        void OpenAircraft()
        {
            _aircraftVisible = true;
            _aircraftFloat?.Show();
            SetOpen(_aircraftOpenBtn, true);
            RebuildAircraft();
            RefreshAircraftBeliefBtn();
        }

        void ToggleAircraftBeliefs()
        {
            if (_ctx?.Selection == null || _ctx.Run == null) return;
            if (_ctx.Selection.BeliefViewOn)
            {
                _ctx.Selection.SetBeliefView(false);
                return;
            }

            int drone = _ctx.Selection.Observer;
            int primary = _ctx.Selection.Primary;
            if (primary >= 0)
            {
                int id = _ctx.Run.Info(primary).drone_id;
                if (id >= 0) drone = id;
            }
            _ctx.Selection.SetBeliefView(true, drone);
        }

        void RefreshAircraftBeliefBtn()
        {
            if (_aircraftBeliefBtn == null) return;
            bool on = _ctx?.Selection != null && _ctx.Selection.BeliefViewOn;
            _aircraftBeliefBtn.EnableInClassList("scene-state-toggle--open", on);
            int observer = _ctx?.Selection != null ? _ctx.Selection.Observer : -1;
            _aircraftBeliefBtn.text = on && observer >= 0 ? $"Color {observer}" : "Color";
            _aircraftBeliefBtn.tooltip = on && observer >= 0
                ? $"Bodies are what drone {observer} declared. Grey is undeclared. The legend (bottom left) tracks this. Click to restore ground truth. B also toggles."
                : "Paint every craft as the selected observer declared it. Grey is undeclared. The legend (bottom left) tracks the mapping. B also toggles.";
        }

        void OnViewModeChanged(ViewMode _)
        {
            RefreshAircraftBeliefBtn();
            if (_aircraftVisible) RefreshAircraftLive();
        }

        void OnObserverChanged(int _)
        {
            RefreshAircraftBeliefBtn();
            if (_aircraftVisible)
            {
                RefreshAircraftLive();
                RefreshAircraftObserverMix();
            }
        }

        void OpenEvents()
        {
            _eventsVisible = true;
            _eventsFloat?.Show();
            SetOpen(_eventsOpenBtn, true);
            RebuildEvents();
        }

        void OpenLogs()
        {
            _logsVisible = true;
            _logsFloat?.Show();
            SetOpen(_logsOpenBtn, true);
            RebuildLogs();
        }

        /// <summary>Cues window. Public because the C key in PlaybackInput calls it.</summary>
        public void ToggleCues()
        {
            if (_cuesVisible) _cuesFloat?.Hide();
            else OpenCues();
        }

        /// <summary>Hide / restore overlays. Public because Shift+C calls it.</summary>
        public void ToggleCueMute()
        {
            Cues.ToggleMute();
            RefreshCueChips();
        }

        void OpenCues()
        {
            _cuesVisible = true;
            _cuesFloat?.Show();
            SetOpen(_cuesOpenBtn, true);
            RefreshCueChips();
        }

        /// <summary>
        /// The renderer that owns the lines. Made here if the scene has no
        /// CueOverlay, so the panel never depends on a scene edit landing.
        /// </summary>
        CueOverlay Cues
        {
            get
            {
                if (_cues != null) return _cues;
                _cues = GetComponent<CueOverlay>();
                if (_cues == null)
                {
                    _cues = gameObject.AddComponent<CueOverlay>();
                    if (_ctx != null) _cues.Bind(_ctx);
                }
                return _cues;
            }
        }

        void BuildCueChips()
        {
            if (_cuesChipRow == null) return;
            _cuesChipRow.Clear();
            _cueChips = new Button[CueOverlay.Specs.Length];
            for (int i = 0; i < CueOverlay.Specs.Length; i++)
            {
                int index = i;
                var spec = CueOverlay.Specs[i];
                var btn = new Button();
                UiQuery.HitSelf(btn);
                btn.AddToClassList("filter-chip");
                btn.AddToClassList("cue-chip");
                btn.Add(CueLegend.Swatch(spec.Color));
                var caption = new Label(spec.Label);
                caption.AddToClassList("cue-chip-label");
                caption.pickingMode = PickingMode.Ignore;
                btn.Add(caption);
                // Unavailable cues stay clickable: the toggle no-ops, and the click
                // still earns you the explanation of what is missing and why.
                btn.clicked += () =>
                {
                    Cues.Toggle(CueOverlay.Specs[index].Bit);
                    _cueExplained = index;
                    RefreshCueChips();
                };
                _cuesChipRow.Add(btn);
                _cueChips[i] = btn;
            }
        }

        void RefreshCueChips()
        {
            if (_cueChips == null) return;
            var cues = Cues;
            bool muted = cues.Muted;
            _cuesChipRow?.EnableInClassList("filter-chips--muted", muted);
            _cuesChipRow?.parent?.EnableInClassList("cues-muted", muted);
            if (_cuesMuteBtn != null)
            {
                _cuesMuteBtn.text = muted ? "Show" : "Hide";
                _cuesMuteBtn.EnableInClassList("scene-state-toggle--open", muted);
            }
            for (int i = 0; i < _cueChips.Length; i++)
            {
                var spec = CueOverlay.Specs[i];
                bool avail = cues.Available(spec.Bit);
                bool on = avail && cues.IsOn(spec.Bit);
                _cueChips[i].EnableInClassList("filter-chip--on", on);
                _cueChips[i].EnableInClassList("filter-chip--unavailable", !avail);
                Color hue = muted ? Palette.Gray(spec.Color) : spec.Color;
                _cueChips[i].style.backgroundColor = on
                    ? Palette.A(hue, 0.42f)
                    : StyleKeyword.Null;
                if (_cueChips[i].childCount > 0)
                    Palette.Fill(_cueChips[i][0], hue);
            }
            if (_cuesFooter != null) _cuesFooter.text = cues.FooterText();
            RefreshCueDetail(cues);
        }

        /// <summary>Explains the last chip clicked, and only that one.</summary>
        void RefreshCueDetail(CueOverlay cues)
        {
            if (_cuesDetailTitle == null) return;

            if ((uint)_cueExplained >= (uint)CueOverlay.Specs.Length)
            {
                _cuesDetailTitle.text = "Pick a cue";
                HideDetailChrome();
                if (_cuesDetailDraws != null)
                    _cuesDetailDraws.text = "Each one says what it draws and which recorded field it came from.";
                if (_cuesDetailSource != null) _cuesDetailSource.text = "";
                return;
            }

            var spec = CueOverlay.Specs[_cueExplained];
            bool avail = cues.Available(spec.Bit);
            string state = !avail ? "unavailable in this run"
                : cues.Muted ? (cues.IsOn(spec.Bit) ? "on, hidden" : "off")
                : cues.IsOn(spec.Bit) ? "on" : "off";
            _cuesDetailTitle.text = $"{spec.Label} — {state}";

            if (_cuesDetailSwatch != null)
            {
                _cuesDetailSwatch.style.display = DisplayStyle.Flex;
                Palette.Fill(_cuesDetailSwatch, cues.Muted ? Palette.Gray(spec.Color) : spec.Color);
            }

            if (_cuesDetailShape != null)
            {
                _cuesDetailShape.text = cues.DrawnAs(spec);
                _cuesDetailShape.style.display = string.IsNullOrEmpty(_cuesDetailShape.text)
                    ? DisplayStyle.None
                    : DisplayStyle.Flex;
            }

            if (_cuesDetailKey != null)
            {
                _cuesDetailKey.Clear();
                if (CueOverlay.IsRadius(spec.Bit))
                    AddSphereToggle(_cuesDetailKey, cues, spec.Bit);
                if (spec.Bit == CueMask.Selection)
                    AddSelectionToggles(_cuesDetailKey, cues);
                else if (spec.Bit == CueMask.Pings)
                    AddPingKindChips(_cuesDetailKey, cues);
                else if (spec.Bit == CueMask.Hops)
                    CueLegend.AddHopKey(_cuesDetailKey, labeled: true);
                else if (spec.Bit == CueMask.Cover)
                    CueLegend.AddCoverKey(_cuesDetailKey);
                else if (spec.Bit == CueMask.Reach)
                    AddReachHorizonSlider(_cuesDetailKey, cues);
                _cuesDetailKey.style.display = _cuesDetailKey.childCount > 0
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            }

            CueOverlay.PingSpec pingSpec = default;
            bool pingKind = spec.Bit == CueMask.Pings
                && (uint)_pingExplained < (uint)CueOverlay.PingKinds.Length;
            if (pingKind)
            {
                pingSpec = CueOverlay.PingKinds[_pingExplained];
                bool kindOn = cues.PingKindOn(pingSpec.Kind);
                _cuesDetailTitle.text = $"Pings · {pingSpec.Label} — {(kindOn ? "on" : "off")}";
                if (_cuesDetailSwatch != null)
                    Palette.Fill(_cuesDetailSwatch, cues.Muted ? Palette.Gray(pingSpec.Color) : pingSpec.Color);
            }

            if (_cuesDetailDraws != null)
                _cuesDetailDraws.text = pingKind ? pingSpec.Draws : spec.Draws;

            if (_cuesDetailSource != null)
            {
                string value = pingKind ? cues.PingKindValue(pingSpec.Kind) : cues.ValueText(spec.Bit);
                string source = pingKind ? pingSpec.Source : spec.Source;
                if (string.IsNullOrEmpty(value) && !pingKind)
                    _cuesDetailSource.text = source + "\nThis run: nothing recorded, so there is nothing to draw.";
                else if (spec.Bit == CueMask.Selection)
                    _cuesDetailSource.text = source + $"\nShowing: {value}.";
                else
                    _cuesDetailSource.text = source + (string.IsNullOrEmpty(value) ? "" : $"\nThis run: {value}.");
            }
        }

        void AddPingKindChips(VisualElement parent, CueOverlay cues)
        {
            parent.AddToClassList("cue-detail-key");
            parent.AddToClassList("filter-chips");
            bool muted = cues.Muted;
            for (int i = 0; i < CueOverlay.PingKinds.Length; i++)
            {
                int index = i;
                var spec = CueOverlay.PingKinds[i];
                bool on = cues.PingKindOn(spec.Kind);
                var btn = new Button();
                UiQuery.HitSelf(btn);
                btn.AddToClassList("filter-chip");
                btn.AddToClassList("cue-chip");
                Color hue = muted ? Palette.Gray(spec.Color) : spec.Color;
                btn.Add(CueLegend.Swatch(hue));
                var caption = new Label(spec.Label);
                caption.AddToClassList("cue-chip-label");
                caption.pickingMode = PickingMode.Ignore;
                btn.Add(caption);
                btn.EnableInClassList("filter-chip--on", on);
                if (on)
                    btn.style.backgroundColor = Palette.A(hue, 0.42f);
                btn.clicked += () =>
                {
                    Cues.TogglePingKind(CueOverlay.PingKinds[index].Kind);
                    _pingExplained = index;
                    RefreshCueChips();
                };
                parent.Add(btn);
            }
        }

        void AddSphereToggle(VisualElement parent, CueOverlay cues, CueMask bit)
        {
            var toggle = new Toggle("Sphere");
            toggle.AddToClassList("cue-detail-toggle");
            toggle.pickingMode = PickingMode.Position;
            toggle.tooltip = "On: the hex volume at this radius. Off: a ring at the same radius. One or the other, never both.";
            toggle.SetValueWithoutNotify(cues.SphereOn(bit));
            toggle.RegisterValueChangedCallback(evt =>
            {
                cues.SetSphere(bit, evt.newValue);
                RefreshCueDetail(cues);
            });
            parent.Add(toggle);
        }

        void AddReachHorizonSlider(VisualElement parent, CueOverlay cues)
        {
            var box = new VisualElement();
            box.AddToClassList("cue-detail-picks");
            box.pickingMode = PickingMode.Ignore;
            var caption = new Label($"Horizon {cues.ReachHorizon:0.0} s");
            caption.AddToClassList("cue-detail-picks-label");
            caption.pickingMode = PickingMode.Ignore;
            box.Add(caption);
            var sl = new Slider(CueOverlay.ReachHorizonMin, CueOverlay.ReachHorizonMax);
            sl.AddToClassList("cue-horizon-slider");
            sl.pickingMode = PickingMode.Position;
            sl.tooltip = "Seconds of coast. Friendlies: xy from lat, height from maxa. Hostiles and civilians: ghost airframe.";
            sl.SetValueWithoutNotify(cues.ReachHorizon);
            sl.RegisterValueChangedCallback(evt =>
            {
                cues.SetReachHorizon(evt.newValue);
                caption.text = $"Horizon {cues.ReachHorizon:0.0} s";
            });
            box.Add(sl);
            parent.Add(box);
        }

        void AddSelectionToggles(VisualElement parent, CueOverlay cues)
        {
            var box = new VisualElement();
            box.AddToClassList("cue-detail-picks");
            box.pickingMode = PickingMode.Ignore;
            var head = new Label("Show pick volume on");
            head.AddToClassList("cue-detail-picks-label");
            head.pickingMode = PickingMode.Ignore;
            box.Add(head);
            AddPickToggle(box, cues, "Hover", CueOverlay.PickHover, cues.PickHoverOn,
                "The sphere under the pointer. Off if it is covering what you are trying to see.");
            AddPickToggle(box, cues, "Selected", CueOverlay.PickSelected, cues.PickSelectedOn,
                "Every craft currently in the selection.");
            AddPickToggle(box, cues, "Unselected", CueOverlay.PickUnselected, cues.PickUnselectedOn,
                "Every living craft that is not selected. With Selected, this is everyone.");
            parent.Add(box);
        }

        void AddPickToggle(VisualElement parent, CueOverlay cues, string label, int flag, bool on, string tip)
        {
            var toggle = new Toggle(label);
            toggle.AddToClassList("cue-detail-toggle");
            toggle.pickingMode = PickingMode.Position;
            toggle.tooltip = tip;
            toggle.SetValueWithoutNotify(on);
            toggle.RegisterValueChangedCallback(evt =>
            {
                cues.SetPickShow(flag, evt.newValue);
                RefreshCueDetail(cues);
            });
            parent.Add(toggle);
        }

        void HideDetailChrome()
        {
            if (_cuesDetailSwatch != null)
                _cuesDetailSwatch.style.display = DisplayStyle.None;
            if (_cuesDetailShape != null)
            {
                _cuesDetailShape.text = "";
                _cuesDetailShape.style.display = DisplayStyle.None;
            }
            if (_cuesDetailKey != null)
            {
                _cuesDetailKey.Clear();
                _cuesDetailKey.style.display = DisplayStyle.None;
            }
        }

        static void SetOpen(Button btn, bool on) =>
            btn?.EnableInClassList("scene-state-toggle--open", on);

        void OnStateChanged()
        {
            if (_aircraftVisible)
            {
                int fp = AliveFingerprint();
                if (fp != _aliveFingerprint)
                    RebuildAircraft();
                else
                    RefreshAircraftLive();
            }
            if (_eventsVisible)
                UpdateEventHighlights();
            if (_logsVisible)
                _logList?.Highlight();
        }

        void OnSelectionChanged(int _)
        {
            if (_aircraftVisible)
                UpdateAircraftHighlights();
        }

        int AliveFingerprint()
        {
            if (_ctx?.State == null) return 0;
            unchecked
            {
                int h = 17;
                var ents = _ctx.State.Entities;
                for (int i = 0; i < ents.Count; i++)
                    if (ents[i].Alive)
                        h = h * 31 + i;
                return h;
            }
        }

        void RebuildDroneMap()
        {
            _droneToSlot.Clear();
            if (_ctx?.Run == null) return;
            int n = _ctx.Run.SlotCount;
            for (int i = 0; i < n; i++)
            {
                int drone = _ctx.Run.Info(i).drone_id;
                if (drone >= 0 && !_droneToSlot.ContainsKey(drone))
                    _droneToSlot[drone] = i;
            }
        }

        void RebuildFilterChoices()
        {
            var kinds = new List<string>();
            var seenKind = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (_ctx?.Run != null)
            {
                int n = _ctx.Run.SlotCount;
                for (int i = 0; i < n; i++)
                {
                    string name = KindName(_ctx.Run.Info(i).Kind);
                    if (seenKind.Add(name))
                        kinds.Add(name);
                }
                kinds.Sort(StringComparer.OrdinalIgnoreCase);
            }
            _aircraftKindChips?.SetChoices(kinds);
            _aircraftStatusChips?.SetChoices(new[] { "Alive", "Gone" });

            var eventKinds = new List<string>();
            var seenEvent = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var events = _ctx?.Run?.Meta?.events;
            if (events != null)
            {
                for (int i = 0; i < events.Count; i++)
                {
                    string kind = events[i]?.kind;
                    if (string.IsNullOrEmpty(kind) || !seenEvent.Add(kind)) continue;
                    eventKinds.Add(kind);
                }
                eventKinds.Sort(StringComparer.OrdinalIgnoreCase);
            }
            _eventKindChips?.SetChoices(eventKinds);

            var drones = new List<string>();
            var seenDrone = new HashSet<int>();
            var logs = _ctx?.Run?.Meta?.logs;
            if (logs != null)
            {
                for (int i = 0; i < logs.Count; i++)
                {
                    int id = logs[i].drone;
                    if (!seenDrone.Add(id)) continue;
                    drones.Add(DroneChip(id));
                }
                drones.Sort(StringComparer.OrdinalIgnoreCase);
            }
            _logDroneChips?.SetChoices(drones);
        }

        void SortAircraft(AircraftSort col)
        {
            if (_aircraftSort == col)
                _aircraftSortAsc = !_aircraftSortAsc;
            else
            {
                _aircraftSort = col;
                _aircraftSortAsc = true;
            }
            if (_aircraftVisible)
                RebuildAircraft();
        }

        void SortEvents(EventSort col)
        {
            if (_eventSort == col)
                _eventSortAsc = !_eventSortAsc;
            else
            {
                _eventSort = col;
                _eventSortAsc = col != EventSort.Severity;
            }
            if (_eventsVisible)
                RebuildEvents();
        }

        void SortLogs(LogSort col)
        {
            if (_logSort == col)
                _logSortAsc = !_logSortAsc;
            else
            {
                _logSort = col;
                _logSortAsc = true;
            }
            if (_logsVisible)
                RebuildLogs();
        }

        void RebuildAircraft()
        {
            _aliveFingerprint = AliveFingerprint();
            _aircraftRows.Clear();
            _displayedAircraft.Clear();
            if (_aircraftScroll == null) return;
            _aircraftScroll.contentContainer.Clear();
            RefreshAircraftHeaders();

            if (_ctx?.Run == null)
            {
                SetAircraftHeader(0, 0, 0, 0, 0, 0);
                AddEmpty(_aircraftScroll, "No run loaded.");
                return;
            }

            string query = _aircraftFilter != null ? (_aircraftFilter.value ?? "").Trim() : "";
            int n = _ctx.Run.SlotCount;
            var ents = _ctx.State != null ? _ctx.State.Entities : null;
            var slots = new List<int>(n);
            for (int i = 0; i < n; i++)
            {
                bool alive = ents != null && i < ents.Count && ents[i].Alive;
                if (!AircraftPasses(i, alive, query)) continue;
                slots.Add(i);
            }

            slots.Sort(CompareAircraft);
            _displayedAircraft.AddRange(slots);

            int friendly = 0, hostile = 0, civilian = 0, wreckage = 0;
            for (int i = 0; i < slots.Count; i++)
            {
                switch (_ctx.Run.Info(slots[i]).Kind)
                {
                    case EntityKind.Friendly: friendly++; break;
                    case EntityKind.Hostile: hostile++; break;
                    case EntityKind.Civilian: civilian++; break;
                    case EntityKind.Wreckage: wreckage++; break;
                }
            }
            SetAircraftHeader(slots.Count, n, friendly, hostile, civilian, wreckage);

            if (slots.Count == 0)
            {
                AddEmpty(_aircraftScroll, n == 0 ? "No aircraft in this run." : "No aircraft match this filter.");
                return;
            }

            for (int i = 0; i < slots.Count; i++)
            {
                int slot = slots[i];
                var info = _ctx.Run.Info(slot);
                bool alive = ents != null && slot < ents.Count && ents[slot].Alive;

                var row = new VisualElement();
                row.AddToClassList("scene-state-drone");
                row.EnableInClassList("scene-state-drone--gone", !alive);
                row.userData = slot;

                var dot = new VisualElement();
                dot.AddToClassList("scene-state-dot");
                PaintAircraftDot(dot, slot, info);
                dot.pickingMode = PickingMode.Ignore;

                var name = new Label(info.Label);
                name.AddToClassList("scene-state-drone-name");
                name.pickingMode = PickingMode.Ignore;
                name.tooltip = info.IdBlurb;
                row.tooltip = info.IdBlurb;

                var kind = new Label(KindName(info.Kind));
                kind.AddToClassList("scene-state-drone-kind");
                kind.pickingMode = PickingMode.Ignore;

                var called = new Label();
                called.AddToClassList("scene-state-drone-called");
                called.pickingMode = PickingMode.Ignore;
                ApplyCalled(called, slot, info);

                var slotLabel = new Label(info.slot.ToString());
                slotLabel.AddToClassList("scene-state-drone-slot");
                slotLabel.pickingMode = PickingMode.Ignore;

                var speed = new Label();
                speed.AddToClassList("scene-state-drone-speed");
                speed.pickingMode = PickingMode.Ignore;
                speed.text = alive ? FormatSpeed(ents[slot].Velocity.magnitude) : "—";

                var status = new Label(alive ? "Alive" : "Gone");
                status.AddToClassList("scene-state-drone-status");
                status.pickingMode = PickingMode.Ignore;

                row.Add(dot);
                row.Add(name);
                row.Add(kind);
                row.Add(called);
                row.Add(slotLabel);
                row.Add(speed);
                row.Add(status);
                row.RegisterCallback<ClickEvent>(OnAircraftClicked);
                _aircraftScroll.Add(row);
                _aircraftRows.Add(new AircraftRow
                {
                    Slot = slot,
                    Root = row,
                    Dot = dot,
                    Called = called,
                    Speed = speed,
                    Status = status,
                });
            }

            UpdateAircraftHighlights();
        }

        bool AircraftPasses(int slot, bool alive, string query)
        {
            var info = _ctx.Run.Info(slot);
            string kind = KindName(info.Kind);
            string status = alive ? "Alive" : "Gone";
            if (_aircraftKindChips != null && !_aircraftKindChips.Allows(kind)) return false;
            if (_aircraftStatusChips != null && !_aircraftStatusChips.Allows(status)) return false;
            if (string.IsNullOrEmpty(query)) return true;
            if (Contains(info.Label, query) || Contains(kind, query) || Contains(status, query)) return true;
            if ($"{info.slot}".IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (info.drone_id >= 0 && $"{info.drone_id}".IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return false;
        }

        void SetAircraftHeader(int shown, int total, int friendly, int hostile, int civilian, int wreckage)
        {
            if (_aircraftHeader != null)
            {
                _aircraftHeader.text = total <= 0
                    ? "Aircraft"
                    : shown == total
                        ? $"Aircraft · {shown}"
                        : $"Aircraft · {shown}/{total}";
            }

            if (_aircraftMix == null) return;
            if (shown == 0)
            {
                _aircraftMix.text = "";
                return;
            }

            var parts = new List<string>(4);
            if (friendly > 0) parts.Add($"{friendly} friendly");
            if (hostile > 0) parts.Add($"{hostile} hostile");
            if (civilian > 0) parts.Add($"{civilian} civilian");
            if (wreckage > 0) parts.Add($"{wreckage} wreckage");
            string mix = string.Join(" · ", parts);
            int observer = _ctx?.Selection != null ? _ctx.Selection.Observer : -1;
            if (observer >= 0)
                mix = string.IsNullOrEmpty(mix)
                    ? $"called as drone {observer}"
                    : mix + $" · called as drone {observer}";
            _aircraftMix.text = mix;
        }

        void RefreshAircraftObserverMix()
        {
            if (!_aircraftVisible || _aircraftMix == null || _ctx?.Run == null) return;
            int n = _ctx.Run.SlotCount;
            int friendly = 0, hostile = 0, civilian = 0, wreckage = 0;
            for (int i = 0; i < _displayedAircraft.Count; i++)
            {
                switch (_ctx.Run.Info(_displayedAircraft[i]).Kind)
                {
                    case EntityKind.Friendly: friendly++; break;
                    case EntityKind.Hostile: hostile++; break;
                    case EntityKind.Civilian: civilian++; break;
                    case EntityKind.Wreckage: wreckage++; break;
                }
            }
            SetAircraftHeader(_displayedAircraft.Count, n, friendly, hostile, civilian, wreckage);
        }

        int CompareAircraft(int a, int b)
        {
            var ia = _ctx.Run.Info(a);
            var ib = _ctx.Run.Info(b);
            var ents = _ctx.State != null ? _ctx.State.Entities : null;
            bool aliveA = ents != null && a < ents.Count && ents[a].Alive;
            bool aliveB = ents != null && b < ents.Count && ents[b].Alive;
            float speedA = aliveA ? ents[a].Velocity.magnitude : -1f;
            float speedB = aliveB ? ents[b].Velocity.magnitude : -1f;

            int c = _aircraftSort switch
            {
                AircraftSort.Name => string.Compare(ia.Label, ib.Label, StringComparison.OrdinalIgnoreCase),
                AircraftSort.Called => string.Compare(CalledText(a, ia), CalledText(b, ib), StringComparison.OrdinalIgnoreCase),
                AircraftSort.Slot => ia.slot.CompareTo(ib.slot),
                AircraftSort.Speed => speedA.CompareTo(speedB),
                AircraftSort.Status => aliveA.CompareTo(aliveB),
                _ => KindOrder(ia.Kind).CompareTo(KindOrder(ib.Kind)),
            };
            if (c == 0)
                c = KindOrder(ia.Kind).CompareTo(KindOrder(ib.Kind));
            if (c == 0)
            {
                int idA = ia.drone_id >= 0 ? ia.drone_id : ia.trace_id;
                int idB = ib.drone_id >= 0 ? ib.drone_id : ib.trace_id;
                c = idA.CompareTo(idB);
            }
            if (c == 0)
                c = a.CompareTo(b);
            return _aircraftSortAsc ? c : -c;
        }

        void RefreshAircraftLive()
        {
            if (_ctx?.State == null) return;
            var ents = _ctx.State.Entities;
            for (int i = 0; i < _aircraftRows.Count; i++)
            {
                var row = _aircraftRows[i];
                if ((uint)row.Slot >= (uint)ents.Count) continue;
                bool alive = ents[row.Slot].Alive;
                row.Root.EnableInClassList("scene-state-drone--gone", !alive);
                row.Speed.text = alive ? FormatSpeed(ents[row.Slot].Velocity.magnitude) : "—";
                row.Status.text = alive ? "Alive" : "Gone";
                if (row.Called != null)
                    ApplyCalled(row.Called, row.Slot, _ctx.Run.Info(row.Slot));
                if (row.Dot != null)
                    PaintAircraftDot(row.Dot, row.Slot, _ctx.Run.Info(row.Slot));
            }
        }

        void UpdateAircraftHighlights()
        {
            var sel = _ctx?.Selection;
            for (int i = 0; i < _aircraftRows.Count; i++)
            {
                bool on = sel != null && sel.IsSelected(_aircraftRows[i].Slot);
                _aircraftRows[i].Root.EnableInClassList("scene-state-drone--selected", on);
            }
        }

        void OnAircraftClicked(ClickEvent evt)
        {
            if (evt.currentTarget is VisualElement row && row.userData is int slot)
            {
                float now = Time.unscaledTime;
                bool isDouble = evt.clickCount >= 2 ||
                    (slot == _lastAircraftClickSlot && now - _lastAircraftClickTime < 0.35f);

                if (isDouble)
                {
                    _lastAircraftClickSlot = -1;
                    _lastAircraftClickTime = 0f;
                    _ctx?.Selection?.SelectOnly(slot);
                    _aircraftAnchorSlot = slot;
                    inspector?.Open();
                }
                else
                {
                    _lastAircraftClickSlot = slot;
                    _lastAircraftClickTime = now;
                    ApplyAircraftClick(evt, slot);
                }
            }
            evt.StopPropagation();
        }

        void ApplyAircraftClick(ClickEvent evt, int slot)
        {
            var sel = _ctx?.Selection;
            if (sel == null) return;

            bool range = evt.shiftKey;
            bool toggle = evt.actionKey;

            if (range && _aircraftAnchorSlot >= 0)
            {
                int a = _displayedAircraft.IndexOf(_aircraftAnchorSlot);
                int b = _displayedAircraft.IndexOf(slot);
                if (a >= 0 && b >= 0)
                {
                    if (a > b)
                    {
                        int tmp = a;
                        a = b;
                        b = tmp;
                    }
                    var rangeSlots = new List<int>(b - a + 1);
                    for (int i = a; i <= b; i++)
                        rangeSlots.Add(_displayedAircraft[i]);
                    sel.Replace(rangeSlots);
                    return;
                }
            }

            if (toggle)
                sel.Toggle(slot);
            else
                sel.SelectOnly(slot);

            _aircraftAnchorSlot = slot;
        }

        void RefreshAircraftHeaders()
        {
            SetHeader(_aircraftColName, _aircraftSort == AircraftSort.Name, _aircraftSortAsc, "Name");
            SetHeader(_aircraftColKind, _aircraftSort == AircraftSort.Kind, _aircraftSortAsc, "Kind");
            SetHeader(_aircraftColCalled, _aircraftSort == AircraftSort.Called, _aircraftSortAsc, "Called");
            SetHeader(_aircraftColSlot, _aircraftSort == AircraftSort.Slot, _aircraftSortAsc, "Slot");
            SetHeader(_aircraftColSpeed, _aircraftSort == AircraftSort.Speed, _aircraftSortAsc, "Speed");
            SetHeader(_aircraftColStatus, _aircraftSort == AircraftSort.Status, _aircraftSortAsc, "Status");
        }

        void RebuildEvents()
        {
            _eventRows.Clear();
            _eventHighlightDirty = true;
            if (_eventsScroll == null) return;
            _eventsScroll.contentContainer.Clear();
            RefreshEventHeaders();

            if (_ctx?.Run?.Meta?.events == null)
            {
                if (_eventsHeader != null) _eventsHeader.text = "Events";
                AddEmpty(_eventsScroll, "No run loaded.");
                return;
            }

            var events = _ctx.Run.Meta.events;
            string query = _eventsFilter != null ? (_eventsFilter.value ?? "").Trim() : "";

            _filteredEvents.Clear();
            for (int i = 0; i < events.Count; i++)
            {
                var e = events[i];
                if (e == null) continue;
                string kind = string.IsNullOrEmpty(e.kind) ? "event" : e.kind;
                if (_eventKindChips != null && !_eventKindChips.Allows(kind))
                    continue;
                if (!MatchesEventFilter(e, query))
                    continue;
                _filteredEvents.Add(e);
            }

            _filteredEvents.Sort(CompareEvents);

            int total = events.Count;
            int shown = _filteredEvents.Count;
            if (_eventsHeader != null)
            {
                _eventsHeader.text = shown == total
                    ? $"Events · {shown}"
                    : $"Events · {shown}/{total}";
            }

            if (shown == 0)
            {
                AddEmpty(_eventsScroll, total == 0 ? "No events in this run." : "No events match this filter.");
                return;
            }

            for (int i = 0; i < _filteredEvents.Count; i++)
            {
                var e = _filteredEvents[i];
                var row = new VisualElement();
                row.AddToClassList("scene-state-event");
                row.userData = e;

                row.Add(Cell($"{e.t:F1}s", "scene-state-cell-time"));
                row.Add(Cell(string.IsNullOrEmpty(e.kind) ? "event" : e.kind, "scene-state-cell-kind"));

                var sev = new Label(SeverityLabel(e.severity));
                sev.AddToClassList("scene-state-cell");
                sev.AddToClassList("scene-state-cell-sev");
                sev.AddToClassList("scene-state-sev");
                sev.AddToClassList("scene-state-sev--" + Mathf.Clamp(e.severity, 0, 3));
                sev.pickingMode = PickingMode.Ignore;
                row.Add(sev);

                row.Add(Cell(FormatInvolved(e), "scene-state-cell-who"));

                var text = Cell(e.text ?? "", "scene-state-cell-text");
                text.AddToClassList("scene-state-cell--wrap");
                row.Add(text);

                row.RegisterCallback<ClickEvent>(OnEventClicked);
                _eventsScroll.Add(row);
                _eventRows.Add(row);
            }

            UpdateEventHighlights();
        }

        void RefreshEventHeaders()
        {
            SetHeader(_eventsColTime, _eventSort == EventSort.Time, _eventSortAsc, "Time");
            SetHeader(_eventsColKind, _eventSort == EventSort.Kind, _eventSortAsc, "Kind");
            SetHeader(_eventsColSev, _eventSort == EventSort.Severity, _eventSortAsc, "Sev");
            SetHeader(_eventsColWho, _eventSort == EventSort.Involved, _eventSortAsc, "Involved");
            SetHeader(_eventsColText, _eventSort == EventSort.Text, _eventSortAsc, "Text");
        }

        int CompareEvents(EventInfo a, EventInfo b)
        {
            int c = _eventSort switch
            {
                EventSort.Kind => string.Compare(a.kind, b.kind, StringComparison.OrdinalIgnoreCase),
                EventSort.Severity => a.severity.CompareTo(b.severity),
                EventSort.Involved => string.Compare(FormatInvolved(a), FormatInvolved(b), StringComparison.OrdinalIgnoreCase),
                EventSort.Text => string.Compare(a.text, b.text, StringComparison.OrdinalIgnoreCase),
                _ => a.t.CompareTo(b.t),
            };
            if (c == 0)
                c = a.t.CompareTo(b.t);
            if (c == 0)
                c = a.frame.CompareTo(b.frame);
            return _eventSortAsc ? c : -c;
        }

        bool MatchesEventFilter(EventInfo e, string query)
        {
            if (string.IsNullOrEmpty(query)) return true;
            if (Contains(e.kind, query) || Contains(e.text, query)) return true;
            if (Contains(SeverityLabel(e.severity), query)) return true;
            if ($"{e.t:F1}".IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (Contains(FormatInvolved(e), query)) return true;
            if (e.slots == null) return false;
            for (int i = 0; i < e.slots.Length; i++)
            {
                int slot = e.slots[i];
                if ($"{slot}".IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
                if ((uint)slot < (uint)_ctx.Run.SlotCount && Contains(_ctx.Run.Info(slot).Label, query))
                    return true;
            }
            return false;
        }

        string FormatInvolved(EventInfo e)
        {
            if (e.slots == null || e.slots.Length == 0 || _ctx?.Run == null)
                return "—";

            int n = e.slots.Length;
            int show = Mathf.Min(n, 3);
            var parts = new List<string>(show + 1);
            for (int i = 0; i < show; i++)
            {
                int slot = e.slots[i];
                if ((uint)slot >= (uint)_ctx.Run.SlotCount)
                    parts.Add($"#{slot}");
                else
                    parts.Add(_ctx.Run.Info(slot).Label);
            }
            if (n > show)
                parts.Add($"+{n - show}");
            return string.Join(", ", parts);
        }

        void OnEventClicked(ClickEvent evt)
        {
            if (evt.currentTarget is VisualElement row && row.userData is EventInfo e)
                JumpToEvent(e);
            evt.StopPropagation();
        }

        void JumpToEvent(EventInfo e)
        {
            if (_ctx == null || e == null) return;
            _ctx.Clock.SeekJustBefore(e.t);

            if (e.slots == null || e.slots.Length == 0) return;
            int primary = PrimaryEventSlot(e);
            if (primary < 0) return;
            _ctx.Selection.SelectOnly(primary);
            if ((uint)primary < (uint)_ctx.Run.SlotCount
                && _ctx.Run.Info(primary).Kind == EntityKind.Civilian)
                return;
            for (int i = 0; i < e.slots.Length; i++)
            {
                int slot = e.slots[i];
                if (slot == primary) continue;
                if ((uint)slot < (uint)_ctx.Run.SlotCount)
                    _ctx.Selection.Add(slot);
            }
        }

        int PrimaryEventSlot(EventInfo e)
        {
            if (e.slots == null || _ctx?.Run == null) return -1;
            for (int i = 0; i < e.slots.Length; i++)
            {
                int slot = e.slots[i];
                if ((uint)slot < (uint)_ctx.Run.SlotCount && _ctx.Run.Info(slot).Kind == EntityKind.Civilian)
                    return slot;
            }
            for (int i = 0; i < e.slots.Length; i++)
                if ((uint)e.slots[i] < (uint)_ctx.Run.SlotCount)
                    return e.slots[i];
            return -1;
        }

        void UpdateEventHighlights()
        {
            if (_ctx == null || _eventRows.Count == 0) return;
            float t = _ctx.Clock.Time;
            EventInfo now = LatestAtOrBefore(_ctx.Run?.Meta?.events, t, e => e.t);

            if (!_eventHighlightDirty && ReferenceEquals(now, _nowEvent)) return;
            _eventHighlightDirty = false;
            _nowEvent = now;

            for (int i = 0; i < _eventRows.Count; i++)
            {
                var row = _eventRows[i];
                if (row.userData is not EventInfo e) continue;
                bool isNow = ReferenceEquals(e, now);
                bool future = e.t > t + 0.001f;
                row.EnableInClassList("scene-state-event--now", isNow);
                row.EnableInClassList("scene-state-event--past", !isNow && !future);
                row.EnableInClassList("scene-state-event--future", !isNow && future);
            }
        }

        void RebuildLogs()
        {
            RefreshLogHeaders();
            _logList?.Rebuild();
        }

        void RefreshLogHeaders()
        {
            SetHeader(_logsColTime, _logSort == LogSort.Time, _logSortAsc, "Time");
            SetHeader(_logsColDrone, _logSort == LogSort.Drone, _logSortAsc, "Drone");
            SetHeader(_logsColText, _logSort == LogSort.Text, _logSortAsc, "Text");
        }

        int CompareLogs(LogLine a, LogLine b)
        {
            int c = _logSort switch
            {
                LogSort.Drone => a.drone.CompareTo(b.drone),
                LogSort.Text => string.Compare(a.text, b.text, StringComparison.OrdinalIgnoreCase),
                _ => a.t.CompareTo(b.t),
            };
            if (c == 0)
                c = a.t.CompareTo(b.t);
            if (c == 0)
                c = a.drone.CompareTo(b.drone);
            return _logSortAsc ? c : -c;
        }

        static T LatestAtOrBefore<T>(IReadOnlyList<T> items, float t, Func<T, float> timeOf) where T : class
        {
            if (items == null) return null;
            T now = null;
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item == null) continue;
                float at = timeOf(item);
                if (at <= t + 0.001f)
                    now = item;
                else
                    break;
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

        static string FormatSpeed(float speed) => $"{speed:F0} m/s";

        static string DroneChip(int droneId) => $"Drone {droneId}";

        static bool Contains(string hay, string needle) =>
            !string.IsNullOrEmpty(hay) && hay.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

        void ApplyCalled(Label label, int slot, EntityInfo info)
        {
            if (label == null) return;
            string text = CalledText(slot, info);
            label.text = text;
            bool mismatch = text != "—" && text != "self" &&
                            !BeliefIndex.Agrees(CalledClass(slot, info), info,
                                _ctx?.State != null && _ctx.State.IsCompromisedNow(slot));
            label.EnableInClassList("scene-state-drone-called--mismatch", mismatch);
        }

        string CalledText(int slot, EntityInfo info)
        {
            int observer = _ctx?.Selection != null ? _ctx.Selection.Observer : -1;
            if (observer < 0) return "—";
            if (info != null && info.drone_id == observer) return "self";
            var cls = CalledClass(slot, info);
            return cls == BeliefClass.Unknown ? "—" : BeliefIndex.Label(cls);
        }

        BeliefClass CalledClass(int slot, EntityInfo info)
        {
            int observer = _ctx?.Selection != null ? _ctx.Selection.Observer : -1;
            if (observer < 0 || _ctx?.Run == null) return BeliefClass.Unknown;
            if (info != null && info.drone_id == observer) return BeliefClass.Friendly;
            float t = _ctx.Clock != null ? _ctx.Clock.Time : 0f;
            return _ctx.Run.Beliefs.At(observer, slot, t);
        }

        static int KindOrder(EntityKind kind) => kind switch
        {
            EntityKind.Friendly => 0,
            EntityKind.Hostile => 1,
            EntityKind.Civilian => 2,
            EntityKind.Wreckage => 3,
            _ => 4,
        };

        static string KindName(EntityKind kind) => kind switch
        {
            EntityKind.Friendly => "Friendly",
            EntityKind.Hostile => "Hostile",
            EntityKind.Civilian => "Civilian",
            EntityKind.Wreckage => "Wreckage",
            _ => "Unknown",
        };

        void PaintAircraftDot(VisualElement dot, int slot, EntityInfo info)
        {
            if (dot == null || info == null) return;
            bool compromised = _ctx?.State != null && _ctx.State.IsCompromisedNow(slot);
            bool belief = _ctx?.Selection != null && _ctx.Selection.BeliefViewOn;
            int observer = _ctx?.Selection != null ? _ctx.Selection.Observer : -1;
            bool isObserver = observer >= 0 && info.drone_id == observer;
            BeliefClass declared = BeliefClass.Unknown;
            if (belief && observer >= 0 && _ctx?.Run?.Beliefs != null)
                declared = _ctx.Run.Beliefs.At(observer, slot, _ctx.Clock.Time);
            Palette.Fill(dot, Palette.Body(
                belief ? ViewMode.FleetBelief : ViewMode.GroundTruth,
                info.Kind, declared, compromised, isObserver));
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
