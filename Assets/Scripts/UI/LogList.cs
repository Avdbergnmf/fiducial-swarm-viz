// One log table used by the Logs window and the inspector. Pretty English,
// Raw toggle, verb chips, click-to-jump, past/now/future highlight. The drone
// column and extra filters are opt-in so the inspector can show one craft.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace SwarmViewer
{
    public sealed class LogList
    {
        public bool ShowDrone;
        public bool SelectOnJump = true;
        public bool FollowNow;
        public string Title = "Logs";
        public float LeadIn = 2f;
        public Func<LogLine, bool> Allow;
        public Comparison<LogLine> Compare;
        public Func<int, int> SlotOfDrone;

        readonly ScrollView _scroll;
        readonly Label _header;
        readonly TextField _filter;
        readonly Button _rawBtn;
        readonly FilterChips _verbs;
        readonly List<VisualElement> _rows = new();
        readonly List<LogLine> _shown = new();
        IVisualElementScheduledItem _scrollToNow;

        ViewerContext _ctx;
        IReadOnlyList<LogLine> _logs;
        bool _raw;
        LogLine _now;
        LogLine _pin;
        LogLine _forceLine;
        bool _highlightDirty = true;

        public LogList(ScrollView scroll, Label header, TextField filter,
            VisualElement verbRow, Button rawBtn)
        {
            _scroll = scroll;
            _header = header;
            _filter = filter;
            _rawBtn = rawBtn;
            _verbs = verbRow != null ? new FilterChips(verbRow) : null;

            if (_filter != null)
                _filter.RegisterValueChangedCallback(_ => Rebuild());
            if (_verbs != null)
                _verbs.Changed += Rebuild;
            if (_rawBtn != null)
            {
                _rawBtn.tooltip = "Show exactly what the brain wrote instead of the plain-English reading.";
                _rawBtn.clicked += ToggleRaw;
            }
        }

        public void Bind(ViewerContext ctx) => _ctx = ctx;

        public void SetSource(IReadOnlyList<LogLine> logs)
        {
            _logs = logs;
            RefreshVerbs();
            Rebuild();
        }

        public void Rebuild()
        {
            _scrollToNow?.Pause();
            _scrollToNow = null;
            _rows.Clear();
            _shown.Clear();
            _highlightDirty = true;
            if (_scroll == null) return;
            _scroll.contentContainer.Clear();

            if (_logs == null)
            {
                if (_header != null) _header.text = Title;
                Empty("No log lines.");
                return;
            }

            string query = _filter != null ? (_filter.value ?? "").Trim() : "";
            for (int i = 0; i < _logs.Count; i++)
            {
                var line = _logs[i];
                if (line == null) continue;
                bool forced = ReferenceEquals(line, _forceLine);
                if (!forced && Allow != null && !Allow(line)) continue;
                if (!forced && _verbs != null && !_verbs.Allows(LogPhrase.Verb(line.text))) continue;
                if (!forced && !Matches(line, query)) continue;
                _shown.Add(line);
            }

            if (Compare != null)
                _shown.Sort(Compare);

            int total = _logs.Count;
            int n = _shown.Count;
            if (_header != null)
                _header.text = n == total ? $"{Title} · {n}" : $"{Title} · {n}/{total}";

            if (n == 0)
            {
                Empty(total == 0 ? "No log lines in this run." : "No log lines match this filter.");
                return;
            }

            for (int i = 0; i < n; i++)
                AddRow(_shown[i]);

            Highlight();
        }

        public void Highlight()
        {
            if (_ctx == null || _rows.Count == 0) return;
            float t = _ctx.Clock.Time;
            LogLine now = null;
            for (int i = 0; i < _shown.Count; i++)
            {
                if (_shown[i].t <= t + 0.001f) now = _shown[i];
                else break;
            }

            if (!_highlightDirty && ReferenceEquals(now, _now)) return;
            _highlightDirty = false;
            bool nowChanged = !ReferenceEquals(now, _now);
            _now = now;

            VisualElement nowRow = null;
            for (int i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                if (row.userData is not LogLine line) continue;
                bool isNow = ReferenceEquals(line, now);
                bool isPin = ReferenceEquals(line, _pin);
                bool future = line.t > t + 0.001f;
                row.EnableInClassList("scene-state-event--now", isNow && !isPin);
                row.EnableInClassList("scene-state-event--pin", isPin);
                row.EnableInClassList("scene-state-event--past", !isNow && !isPin && !future);
                row.EnableInClassList("scene-state-event--future", !isNow && !isPin && future);
                if (isNow) nowRow = row;
            }

            if (FollowNow && nowChanged && _pin == null && nowRow != null)
                ScheduleScroll(nowRow);
        }

        public void Reveal(LogLine line)
        {
            if (line == null || _scroll == null) return;
            bool inSource = false;
            if (_logs != null)
            {
                for (int i = 0; i < _logs.Count; i++)
                {
                    if (!ReferenceEquals(_logs[i], line)) continue;
                    inSource = true;
                    break;
                }
            }
            if (!inSource) return;

            _pin = line;
            _forceLine = line;
            _highlightDirty = true;
            Rebuild();
            VisualElement pinRow = null;
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].userData is LogLine rowLine && ReferenceEquals(rowLine, line))
                    pinRow = _rows[i];
            }
            if (pinRow != null)
                ScheduleScroll(pinRow);
        }

        void ScheduleScroll(VisualElement row)
        {
            _scrollToNow?.Pause();
            _scrollToNow = _scroll.schedule.Execute(() =>
            {
                _scrollToNow = null;
                if (row.parent == _scroll.contentContainer)
                    _scroll.ScrollTo(row);
            });
        }

        void ToggleRaw()
        {
            _raw = !_raw;
            _rawBtn?.EnableInClassList("scene-state-toggle--open", _raw);
            Rebuild();
        }

        void RefreshVerbs()
        {
            if (_verbs == null) return;
            var names = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (_logs != null)
            {
                for (int i = 0; i < _logs.Count; i++)
                {
                    string verb = LogPhrase.Verb(_logs[i]?.text);
                    if (!seen.Add(verb)) continue;
                    names.Add(verb);
                }
                names.Sort(StringComparer.OrdinalIgnoreCase);
            }
            _verbs.SetChoices(names, Palette.Log);
        }

        void AddRow(LogLine line)
        {
            var row = new VisualElement();
            row.AddToClassList("scene-state-event");
            row.pickingMode = PickingMode.Position;
            row.userData = line;
            row.tooltip = LogPhrase.Tooltip(line.text);

            row.Add(Cell($"{line.t:F1}s", "scene-state-cell-time"));
            if (ShowDrone)
            {
                var drone = Cell($"Drone {line.drone}", "scene-state-cell-log-drone");
                drone.tooltip = line.drone < 0
                    ? "Log overflow: the rest of the run is silent."
                    : "Brain id, 0-based. Same number as Aircraft → Drone N. The simulator entity id is one higher (drone 15 is sim #16).";
                row.Add(drone);
            }

            var text = Cell(_raw ? (line.text ?? "") : line.Pretty, "scene-state-cell-text");
            text.AddToClassList("scene-state-cell--wrap");
            text.style.color = Palette.Opaque(Palette.Log(LogPhrase.Verb(line.text), line.text));
            row.Add(text);

            row.RegisterCallback<ClickEvent>(OnClicked);
            _scroll.contentContainer.Add(row);
            _rows.Add(row);
        }

        void OnClicked(ClickEvent evt)
        {
            if (evt.currentTarget is VisualElement row && row.userData is LogLine line)
            {
                _pin = null;
                _forceLine = null;
                _highlightDirty = true;
                Jump(line);
            }
            evt.StopPropagation();
        }

        void Jump(LogLine line)
        {
            if (_ctx == null || line == null) return;
            _ctx.Clock.SeekBefore(line.t, LeadIn);
            if (!SelectOnJump || SlotOfDrone == null) return;
            int slot = SlotOfDrone(line.drone);
            if (slot >= 0) _ctx.Selection.SelectOnly(slot);
        }

        bool Matches(LogLine line, string query)
        {
            if (string.IsNullOrEmpty(query)) return true;
            if (Contains(line.text, query) || Contains(line.Pretty, query)) return true;
            if ($"{line.t:F1}".IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            string drone = "Drone " + line.drone;
            if (Contains(drone, query) || $"{line.drone}".IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (SlotOfDrone != null && _ctx?.Run != null)
            {
                int slot = SlotOfDrone(line.drone);
                if ((uint)slot < (uint)_ctx.Run.SlotCount && Contains(_ctx.Run.Info(slot).Label, query))
                    return true;
            }
            return false;
        }

        void Empty(string text)
        {
            var empty = new Label(text);
            empty.AddToClassList("scene-state-empty");
            empty.AddToClassList("inspector-empty");
            empty.pickingMode = PickingMode.Ignore;
            _scroll.contentContainer.Add(empty);
        }

        static Label Cell(string text, string extraClass)
        {
            var label = new Label(text ?? "");
            label.AddToClassList("scene-state-cell");
            label.AddToClassList(extraClass);
            label.pickingMode = PickingMode.Ignore;
            return label;
        }

        static bool Contains(string hay, string needle) =>
            !string.IsNullOrEmpty(hay) && hay.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
