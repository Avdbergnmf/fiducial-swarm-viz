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

        struct RunEntry
        {
            public string Name;
            public string StemPath;
            public string Source;
            public string Scenario;
            public float Duration;
            public int Entities;
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

        readonly List<RunEntry> _availableRuns = new();
        string _selectedStem;

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
            if (_runListScroll != null)
                _runListScroll.Clear();
            if (_errorLabel != null)
                _errorLabel.text = "";

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
                        Entities = meta.slot_count
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
            _selectedStem = stem;

            if (_runListScroll != null)
            {
                foreach (var child in _runListScroll.Children())
                    child.RemoveFromClassList("run-item-selected");
            }

            itemElement?.AddToClassList("run-item-selected");
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
