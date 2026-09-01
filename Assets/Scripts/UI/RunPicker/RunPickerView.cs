// UI Toolkit run picker. Overlay root is picking-mode Ignore so empty space
// falls through to the 3D view; the modal backdrop keeps Position so it blocks
// world clicks while the dialog is open.
//
// Extra run folder and last loaded run are remembered in
// StreamingAssets/viewer.settings.json. List headers appear only when both
// StreamingAssets and the extra folder contribute runs.

using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.UIElements;

namespace SwarmViewer
{
    public sealed class RunPickerView : MonoBehaviour, IRunView
    {
        [SerializeField] UIDocument uiDocument;
        [SerializeField] VisualizerRoot rootCoordinator;

        struct RunPreview
        {
            public string Scenario;
            public string GeneratedAt;
            public string Brain;
            public string BrainPath;
            public string SimVersion;
            public string Trace;
            public string Folder;
            public float Duration;
            public int Frames;
            public float TraceHz;
            public int FleetSize;
            public int Slots;
            public float KillRadius;
            public float AssetRadius;
            public string Arena;
            public int Friendly, Hostile, Civilian, Wreckage;
            public int Events, Beliefs, Links, Breaches;
            public long BinBytes;
            public SimReport Report;
        }

        struct RunEntry
        {
            public string Name;
            public string StemPath;
            public string Source;
            public string Scenario;
            public float Duration;
            public int Entities;
            public RunPreview Preview;
        }

        ViewerContext _ctx;
        ViewerSettings _settings;
        VisualElement _root;
        bool _uiWired;

        Button _openPickerBtn;
        VisualElement _modalBackdrop;
        ScrollView _runListScroll;
        TextField _pathInputField;
        Button _browseFolderBtn;
        Label _errorLabel;
        Button _cancelBtn;
        Button _loadBtn;
        Button _validateBtn;
        Label _previewTitle;
        ScrollView _previewScroll;
        VisualElement _reportOverlay;
        Label _reportTitle;
        Label _reportSummary;
        ScrollView _reportScroll;
        Button _reportCloseBtn;

        readonly List<RunEntry> _availableRuns = new();
        string _selectedStem;
        ValidationResult _validation;
        string _validationLoadError;
        bool _validationRan;

        void OnEnable()
        {
            TryWireUi();
        }

        void Start()
        {
            TryWireUi();
        }

        public void Bind(ViewerContext ctx)
        {
            UnhookClock();
            _ctx = ctx;
            EnsureCoordinator();
            if (rootCoordinator == null)
                Debug.LogWarning("[viewer] RunPickerView: no VisualizerRoot.");

            TryWireUi();
            HookClock();

            if (_ctx == null || _ctx.Run == null) OpenDialog();
            else CloseDialog();
        }

        void EnsureCoordinator()
        {
            if (rootCoordinator == null)
                rootCoordinator = GetComponent<VisualizerRoot>();
        }

        void TryWireUi()
        {
            EnsureCoordinator();
            if (uiDocument == null)
                uiDocument = GetComponent<UIDocument>();

            if (uiDocument == null)
            {
                Debug.LogWarning("[viewer] RunPickerView: no UIDocument.");
                return;
            }

            var root = uiDocument.rootVisualElement;
            if (root == null)
            {
                Debug.LogWarning("[viewer] RunPickerView: rootVisualElement is null — UIDocument not ready yet. Will retry from OnEnable.");
                return;
            }

            if (_uiWired && _root == root) return;

            _root = root;
            _openPickerBtn = UiQuery.Named<Button>(_root, "openPickerBtn");
            _modalBackdrop = UiQuery.Named<VisualElement>(_root, "modalBackdrop");
            _runListScroll = UiQuery.Named<ScrollView>(_root, "runListScroll");
            _pathInputField = UiQuery.Named<TextField>(_root, "pathInputField");
            _browseFolderBtn = UiQuery.Named<Button>(_root, "browseFolderBtn");
            _errorLabel = UiQuery.Named<Label>(_root, "errorLabel");
            _cancelBtn = UiQuery.Named<Button>(_root, "cancelBtn");
            _loadBtn = UiQuery.Named<Button>(_root, "loadBtn");
            _validateBtn = UiQuery.Named<Button>(_root, "validateBtn");
            _previewTitle = UiQuery.Named<Label>(_root, "previewTitle");
            _previewScroll = UiQuery.Named<ScrollView>(_root, "previewScroll");
            _reportOverlay = UiQuery.Named<VisualElement>(_root, "reportOverlay");
            _reportTitle = UiQuery.Named<Label>(_root, "reportTitle");
            _reportSummary = UiQuery.Named<Label>(_root, "reportSummary");
            _reportScroll = UiQuery.Named<ScrollView>(_root, "reportScroll");
            _reportCloseBtn = UiQuery.Named<Button>(_root, "reportCloseBtn");

            if (_pathInputField != null)
            {
                _pathInputField.isDelayed = true;
                _pathInputField.SetValueWithoutNotify(Settings.extraRunFolder ?? "");
            }

            RegisterCallbacks();
            _uiWired = true;
        }

        ViewerSettings Settings => _settings ??= ViewerSettings.Load();

        void RegisterCallbacks()
        {
            if (_openPickerBtn != null)
                _openPickerBtn.clicked += () => OpenDialog();

            if (_cancelBtn != null)
                _cancelBtn.clicked += CloseDialog;

            if (_loadBtn != null)
                _loadBtn.clicked += OnLoadClicked;

            if (_validateBtn != null)
                _validateBtn.clicked += OnValidateClicked;

            if (_reportCloseBtn != null)
                _reportCloseBtn.clicked += HideReport;

            if (_browseFolderBtn != null)
                _browseFolderBtn.clicked += OnBrowseFolder;

            if (_pathInputField != null)
                _pathInputField.RegisterValueChangedCallback(evt => ApplyExtraFolder(evt.newValue, save: true));
        }

        void HookClock()
        {
            if (_ctx?.Clock == null) return;
            _ctx.Clock.OnPlayStateChanged += OnPlayStateChanged;
            OnPlayStateChanged(_ctx.Clock.IsPlaying);
        }

        void UnhookClock()
        {
            if (_ctx?.Clock == null) return;
            _ctx.Clock.OnPlayStateChanged -= OnPlayStateChanged;
        }

        [ContextMenu("Dump UI Element Tree")]
        public void DumpElementTree()
        {
            if (uiDocument == null)
                uiDocument = GetComponent<UIDocument>();
            UiQuery.DumpTree(uiDocument != null ? uiDocument.rootVisualElement : null);
        }

        void OnPlayStateChanged(bool isPlaying)
        {
            if (_openPickerBtn != null)
                _openPickerBtn.style.display = isPlaying ? DisplayStyle.None : DisplayStyle.Flex;
        }

        public void OpenDialog(string message = null)
        {
            HideReport();
            RefreshRunList();
            if (_modalBackdrop != null)
                _modalBackdrop.style.display = DisplayStyle.Flex;
            if (_errorLabel != null)
            {
                _errorLabel.style.display = DisplayStyle.Flex;
                if (!string.IsNullOrEmpty(message))
                    _errorLabel.text = message;
            }
        }

        public void CloseDialog()
        {
            HideReport();
            if (_modalBackdrop != null)
                _modalBackdrop.style.display = DisplayStyle.None;
        }

        string ExtraFolderPath
        {
            get
            {
                if (_pathInputField != null)
                    return (_pathInputField.value ?? "").Trim();
                return (Settings.extraRunFolder ?? "").Trim();
            }
        }

        void OnBrowseFolder()
        {
            string start = ExtraFolderPath;
            if (string.IsNullOrEmpty(start) || !Directory.Exists(start))
                start = Application.streamingAssetsPath;

            string picked = NativeFolderPicker.Open("Select run folder", start);
            if (string.IsNullOrEmpty(picked)) return;

            _pathInputField?.SetValueWithoutNotify(picked);
            ApplyExtraFolder(picked, save: true);
        }

        void ApplyExtraFolder(string path, bool save)
        {
            path = (path ?? "").Trim();
            if (save && Settings.extraRunFolder != path)
            {
                Settings.extraRunFolder = path;
                Settings.Save();
            }

            RefreshRunList();
        }

        void RefreshRunList()
        {
            string keepStem = _selectedStem;
            if (string.IsNullOrEmpty(keepStem))
                keepStem = Settings.lastRunStem;

            _availableRuns.Clear();
            _selectedStem = null;
            ClearValidation();
            if (_runListScroll != null)
                _runListScroll.Clear();
            if (_errorLabel != null)
                _errorLabel.text = "";
            ShowPreview(null);

            SearchDirectory(Application.streamingAssetsPath, "StreamingAssets");

            string extra = ExtraFolderPath;
            if (!string.IsNullOrEmpty(extra))
            {
                if (!Directory.Exists(extra))
                {
                    if (_errorLabel != null)
                        _errorLabel.text = "Folder not found.";
                }
                else if (!IsSameFolder(extra, Application.streamingAssetsPath))
                {
                    string sourceName = Path.GetFileName(
                        extra.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    if (string.IsNullOrEmpty(sourceName))
                        sourceName = extra;

                    int before = _availableRuns.Count;
                    SearchDirectory(extra, sourceName);
                    if (_availableRuns.Count == before && _errorLabel != null)
                        _errorLabel.text = "No runs in that folder.";
                }
            }

            bool showHeaders = false;
            if (_availableRuns.Count > 0)
            {
                string firstSource = _availableRuns[0].Source;
                for (int i = 1; i < _availableRuns.Count; i++)
                {
                    if (_availableRuns[i].Source != firstSource)
                    {
                        showHeaders = true;
                        break;
                    }
                }
            }

            string lastSource = null;
            VisualElement keepItem = null;
            string keepListedStem = null;
            VisualElement firstItem = null;
            string firstStem = null;

            for (int i = 0; i < _availableRuns.Count; i++)
            {
                var entry = _availableRuns[i];

                if (showHeaders && _runListScroll != null && entry.Source != lastSource)
                {
                    lastSource = entry.Source;
                    var header = new Label(entry.Source);
                    header.AddToClassList("run-list-source");
                    header.pickingMode = PickingMode.Ignore;
                    _runListScroll.Add(header);
                }

                var item = new VisualElement();
                item.AddToClassList("run-item");

                var nameLabel = new Label(entry.Name);
                nameLabel.AddToClassList("run-item-name");

                var metaLabel = new Label($"[{entry.Scenario}] {entry.Entities} entities, {entry.Duration:F1}s");
                metaLabel.AddToClassList("run-item-meta");

                item.Add(nameLabel);
                item.Add(metaLabel);

                string stem = entry.StemPath;
                item.RegisterCallback<ClickEvent>(_ => SelectRun(stem, item));

                _runListScroll?.Add(item);

                if (firstItem == null)
                {
                    firstItem = item;
                    firstStem = stem;
                }

                if (keepItem == null && SameStem(stem, keepStem))
                {
                    keepItem = item;
                    keepListedStem = stem;
                }
            }

            if (keepItem != null)
                SelectRun(keepListedStem, keepItem);
            else if (firstItem != null)
                SelectRun(firstStem, firstItem);
        }

        static bool IsSameFolder(string a, string b)
        {
            try
            {
                return string.Equals(
                    Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }

        static bool SameStem(string listed, string saved)
        {
            if (string.IsNullOrEmpty(listed) || string.IsNullOrEmpty(saved)) return false;
            if (string.Equals(listed, saved, StringComparison.OrdinalIgnoreCase)) return true;
            try
            {
                string listedFull = Path.GetFullPath(listed);
                string savedFull = Path.IsPathRooted(saved)
                    ? Path.GetFullPath(saved)
                    : Path.GetFullPath(Path.Combine(Application.streamingAssetsPath, saved));
                return string.Equals(listedFull, savedFull, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }

        void SearchDirectory(string dir, string source)
        {
            if (!Directory.Exists(dir)) return;

            string[] files = Directory.GetFiles(dir, "*.meta.json");
            foreach (var f in files)
            {
                try
                {
                    string json = File.ReadAllText(f);
                    var meta = JsonConvert.DeserializeObject<RunMeta>(json);
                    if (meta == null) continue;

                    string stem = f.Substring(0, f.Length - ".meta.json".Length);
                    string name = Path.GetFileName(stem);

                    _availableRuns.Add(new RunEntry
                    {
                        Name = name,
                        StemPath = stem,
                        Source = source,
                        Scenario = string.IsNullOrEmpty(meta.scenario) ? "run" : meta.scenario,
                        Duration = meta.duration,
                        Entities = meta.slot_count,
                        Preview = Summarize(meta, stem, source),
                    });
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[viewer] Could not inspect {f}: {e.Message}");
                }
            }
        }

        void SelectRun(string stem, VisualElement itemElement)
        {
            bool same = SameStem(_selectedStem, stem);
            _selectedStem = stem;
            if (!same)
                ClearValidation();

            if (_runListScroll != null)
            {
                foreach (var child in _runListScroll.Children())
                    child.RemoveFromClassList("run-item-selected");
            }

            itemElement?.AddToClassList("run-item-selected");

            RunPreview? preview = null;
            for (int i = 0; i < _availableRuns.Count; i++)
            {
                if (SameStem(_availableRuns[i].StemPath, stem))
                {
                    preview = _availableRuns[i].Preview;
                    if (_previewTitle != null)
                        _previewTitle.text = _availableRuns[i].Name;
                    break;
                }
            }
            ShowPreview(preview);
        }

        static RunPreview Summarize(RunMeta meta, string stem, string folder)
        {
            var p = new RunPreview
            {
                Scenario = FirstNonEmpty(meta.scenario, meta.provenance?.scenario),
                GeneratedAt = meta.provenance?.generated_at,
                BrainPath = FirstNonEmpty(meta.brain, meta.provenance?.brain),
                SimVersion = FirstNonEmpty(meta.sim_version, meta.provenance?.sim_version),
                Trace = meta.source,
                Folder = folder,
                Duration = meta.duration,
                Frames = meta.frame_count,
                TraceHz = meta.trace_hz,
                FleetSize = meta.fleet_size,
                Slots = meta.slot_count,
                KillRadius = meta.kill_radius,
                AssetRadius = meta.asset != null ? meta.asset.radius : 0f,
                Events = meta.events?.Count ?? 0,
                Beliefs = meta.beliefs?.Count ?? 0,
                Links = meta.links?.Count ?? 0,
                BinBytes = -1,
                Report = meta.report,
            };

            if (!string.IsNullOrEmpty(p.BrainPath))
                p.Brain = Path.GetFileName(p.BrainPath.Replace('\\', '/'));

            string binPath = stem + ".bin";
            if (File.Exists(binPath))
                p.BinBytes = new FileInfo(binPath).Length;

            p.Arena = ArenaSummary(meta.arena);

            if (meta.entities != null)
            {
                for (int i = 0; i < meta.entities.Count; i++)
                {
                    switch (meta.entities[i]?.kind)
                    {
                        case "friendly": p.Friendly++; break;
                        case "hostile": p.Hostile++; break;
                        case "civilian": p.Civilian++; break;
                        case "wreckage": p.Wreckage++; break;
                    }
                }
            }

            if (meta.events != null)
            {
                for (int i = 0; i < meta.events.Count; i++)
                    if (meta.events[i]?.kind == "report_breach") p.Breaches++;
            }

            return p;
        }

        static string ArenaSummary(Bounds3 arena)
        {
            if (arena?.min == null || arena.max == null || arena.min.Length < 3 || arena.max.Length < 3)
                return null;
            float w = arena.max[0] - arena.min[0];
            float d = arena.max[2] - arena.min[2];
            float h = arena.max[1] - arena.min[1];
            return $"{w:F0} × {d:F0} m  (ceiling {h:F0} m)";
        }

        static string FirstNonEmpty(string a, string b) =>
            !string.IsNullOrEmpty(a) ? a : b;

        void ShowPreview(RunPreview? preview)
        {
            if (_previewScroll != null)
                _previewScroll.Clear();

            if (preview == null)
            {
                if (_previewTitle != null)
                    _previewTitle.text = "No run selected";
                return;
            }

            var p = preview.Value;
            AddSection("Identity");
            AddRow("Scenario", p.Scenario);
            AddRow("Generated", FormatTimestamp(p.GeneratedAt));
            AddRow("Brain", p.Brain, tooltip: p.BrainPath);
            AddRow("Simulator", p.SimVersion);
            AddRow("Trace", p.Trace);
            AddRow("Folder", p.Folder);

            AddSection("Contents");
            AddRow("Duration", p.Duration > 0f ? $"{p.Duration:F1} s" : null);
            AddRow("Frames", p.Frames > 0 ? $"{p.Frames} @ {p.TraceHz:G} Hz" : null);
            AddRow("Fleet", p.FleetSize > 0 ? $"{p.FleetSize} drones / {p.Slots} slots" : (p.Slots > 0 ? $"{p.Slots} slots" : null));
            AddRow("Mix", MixSummary(p));
            AddRow("Kill radius", p.KillRadius > 0f ? $"{p.KillRadius:G} m" : null);
            AddRow("Asset radius", p.AssetRadius > 0f ? $"{p.AssetRadius:G} m" : null);
            AddRow("Arena", p.Arena);
            AddRow("Events", EventSummary(p));
            AddRow("Links", p.Links.ToString());
            AddRow("Beliefs", p.Beliefs > 0 ? p.Beliefs.ToString() : "0 (none declared)");
            AddRow("Bin", p.BinBytes >= 0 ? $"{p.BinBytes:N0} bytes" : "missing");
            RenderScore(p.Report);
            RenderValidation();
        }

        void RenderScore(SimReport report)
        {
            AddSection("Score");
            var rows = ScoreBreakdown.Rows(report);
            for (int i = 0; i < rows.Count; i++)
                AddRow(rows[i].Key, rows[i].Value, tooltip: rows[i].Detail);
        }

        static string MixSummary(RunPreview p)
        {
            var parts = new List<string>();
            if (p.Friendly > 0) parts.Add($"{p.Friendly} friendly");
            if (p.Hostile > 0) parts.Add($"{p.Hostile} hostile");
            if (p.Civilian > 0) parts.Add($"{p.Civilian} civilian");
            if (p.Wreckage > 0) parts.Add($"{p.Wreckage} wreckage");
            return parts.Count > 0 ? string.Join(", ", parts) : null;
        }

        static string EventSummary(RunPreview p)
        {
            if (p.Events <= 0) return p.Events == 0 ? "0" : null;
            if (p.Breaches > 0) return $"{p.Events} ({p.Breaches} breach)";
            return p.Events.ToString();
        }

        static string FormatTimestamp(string iso)
        {
            if (string.IsNullOrEmpty(iso)) return null;
            if (iso.Length >= 19 && iso[10] == 'T')
                return iso.Substring(0, 10) + " " + iso.Substring(11, 8) + " UTC";
            return iso;
        }

        void AddSection(string title)
        {
            if (_previewScroll == null) return;
            var label = new Label(title);
            label.AddToClassList("preview-section");
            label.pickingMode = PickingMode.Ignore;
            _previewScroll.Add(label);
        }

        void AddRow(string key, string value, string tooltip = null)
        {
            if (_previewScroll == null) return;

            bool unknown = string.IsNullOrEmpty(value);
            var row = new VisualElement();
            row.AddToClassList("preview-row");
            row.pickingMode = PickingMode.Ignore;

            var k = new Label(key);
            k.AddToClassList("preview-key");
            k.pickingMode = PickingMode.Ignore;

            var v = new Label(unknown ? "unknown" : value);
            v.AddToClassList("preview-value");
            if (unknown) v.AddToClassList("preview-unknown");
            if (!string.IsNullOrEmpty(tooltip))
            {
                v.tooltip = tooltip;
                v.pickingMode = PickingMode.Position;
            }
            else
            {
                v.pickingMode = PickingMode.Ignore;
            }

            row.Add(k);
            row.Add(v);
            _previewScroll.Add(row);
        }

        void OnLoadClicked()
        {
            if (string.IsNullOrEmpty(_selectedStem))
            {
                ShowError("Please select a run.");
                return;
            }

            EnsureCoordinator();
            if (rootCoordinator == null)
            {
                ShowError("No VisualizerRoot on this GameObject.");
                return;
            }

            bool success = rootCoordinator.LoadRun(_selectedStem, out string errorMsg);
            if (success) CloseDialog();
            else ShowError(errorMsg);
        }

        void ClearValidation()
        {
            _validation = null;
            _validationLoadError = null;
            _validationRan = false;
            HideReport();
        }

        void OnValidateClicked()
        {
            if (string.IsNullOrEmpty(_selectedStem))
            {
                ShowError("Please select a run.");
                return;
            }

            if (_errorLabel != null)
                _errorLabel.text = "";

            EnsureCoordinator();
            var expectations = rootCoordinator != null ? rootCoordinator.Expectations : default;

            try
            {
                string resolved = RunLoader.Resolve(_selectedStem);
                RunLoader.Load(resolved, expectations, out var validation, log: false);
                _validation = validation;
                _validationLoadError = null;
            }
            catch (Exception e)
            {
                _validation = null;
                _validationLoadError = e.Message;
            }

            _validationRan = true;
            RunPreview? preview = null;
            string runName = null;
            for (int i = 0; i < _availableRuns.Count; i++)
            {
                if (SameStem(_availableRuns[i].StemPath, _selectedStem))
                {
                    preview = _availableRuns[i].Preview;
                    runName = _availableRuns[i].Name;
                    break;
                }
            }
            ShowPreview(preview);
            ShowReport(runName, preview?.Report);
        }

        void RenderValidation()
        {
            AddSection("Validation");

            if (!_validationRan)
            {
                AddNote("Not run — click Validate for a full report (does not load the run).");
                return;
            }

            if (!string.IsNullOrEmpty(_validationLoadError))
            {
                AddNote(_validationLoadError, fail: true);
                return;
            }

            if (_validation != null)
            {
                AddNote(_validation.Summary(),
                    pass: !_validation.HasErrors,
                    fail: _validation.HasErrors,
                    warn: !_validation.HasErrors);
                AddNote("Full report is open — Back to return to the run list.");
            }
        }

        void HideReport()
        {
            if (_reportOverlay != null)
                _reportOverlay.style.display = DisplayStyle.None;
        }

        void ShowReport(string runName, SimReport score)
        {
            if (_reportOverlay == null) return;

            if (_reportTitle != null)
                _reportTitle.text = string.IsNullOrEmpty(runName) ? "Validation report" : $"Validation — {runName}";

            if (_reportScroll != null)
                _reportScroll.Clear();

            if (!string.IsNullOrEmpty(_validationLoadError))
            {
                if (_reportSummary != null)
                    _reportSummary.text = "Could not load this run to validate.";
                AddReportBlock("ERR", "load", _validationLoadError, null, "error");
            }
            else if (_validation == null)
            {
                if (_reportSummary != null)
                    _reportSummary.text = "No result.";
            }
            else
            {
                if (_reportSummary != null)
                    _reportSummary.text = _validation.Summary();
                AppendScoreReport(score);
                for (int i = 0; i < _validation.Issues.Count; i++)
                    AddReportRow(_validation.Issues[i]);
            }

            _reportOverlay.style.display = DisplayStyle.Flex;
        }

        void AppendScoreReport(SimReport report)
        {
            var rows = ScoreBreakdown.Rows(report);
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                string kind = row.Value != null && row.Value.StartsWith("-") ? "warning"
                    : row.Key == "Total" ? "pass"
                    : "info";
                string status = row.Key == "Total" ? "SCORE" : "INFO";
                AddReportBlock(status, row.Key, row.Value, row.Detail, kind);
            }
        }

        void AddReportRow(Issue issue)
        {
            if (issue == null) return;
            string kind, status;
            switch (issue.Severity)
            {
                case Severity.Pass: kind = "pass"; status = "PASS"; break;
                case Severity.Warning: kind = "warning"; status = "WARN"; break;
                case Severity.Error: kind = "error"; status = "ERR"; break;
                default: kind = "info"; status = "SKIP"; break;
            }
            AddReportBlock(status, issue.Check, issue.Message, issue.Parameters, kind);
        }

        void AddReportBlock(string status, string check, string message, string parameters, string kind)
        {
            if (_reportScroll == null) return;

            var row = new VisualElement();
            row.AddToClassList("report-row");
            row.AddToClassList("report-row--" + kind);

            var head = new VisualElement();
            head.AddToClassList("report-row-head");

            var statusLabel = new Label(status);
            statusLabel.AddToClassList("report-status");
            var checkLabel = new Label(check ?? "");
            checkLabel.AddToClassList("report-check");
            head.Add(statusLabel);
            head.Add(checkLabel);
            row.Add(head);

            if (!string.IsNullOrEmpty(parameters))
            {
                var p = new Label(parameters);
                p.AddToClassList("report-params");
                row.Add(p);
            }

            if (!string.IsNullOrEmpty(message))
            {
                var m = new Label(message);
                m.AddToClassList("report-msg");
                row.Add(m);
            }

            _reportScroll.Add(row);
        }

        void AddNote(string text, bool pass = false, bool fail = false, bool warn = false)
        {
            if (_previewScroll == null) return;
            var label = new Label(text);
            label.AddToClassList(pass ? "preview-pass" : fail ? "preview-fail" : warn ? "preview-warn" : "preview-note");
            label.pickingMode = PickingMode.Ignore;
            _previewScroll.Add(label);
        }

        void ShowError(string message)
        {
            if (_errorLabel == null) return;
            _errorLabel.style.display = DisplayStyle.Flex;
            _errorLabel.text = message ?? "";
        }

        void OnDestroy()
        {
            UnhookClock();
        }
    }
}
