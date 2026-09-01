// Wrap-row toggle chips. None selected means no restriction; any selected is OR.

using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace SwarmViewer
{
    public sealed class FilterChips
    {
        public event Action Changed;

        readonly VisualElement _root;
        readonly HashSet<string> _selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public FilterChips(VisualElement root)
        {
            _root = root;
            if (_root != null)
                _root.AddToClassList("filter-chips");
        }

        public void SetChoices(IReadOnlyList<string> choices)
        {
            if (_root == null) return;

            var keep = new HashSet<string>(_selected, StringComparer.OrdinalIgnoreCase);
            _root.Clear();
            _selected.Clear();
            if (choices == null) return;

            for (int i = 0; i < choices.Count; i++)
            {
                string key = choices[i];
                if (string.IsNullOrEmpty(key)) continue;

                bool on = keep.Contains(key);
                if (on)
                    _selected.Add(key);

                var btn = new Button { text = key };
                btn.AddToClassList("filter-chip");
                btn.EnableInClassList("filter-chip--on", on);
                string captured = key;
                btn.clicked += () => Toggle(captured, btn);
                _root.Add(btn);
            }
        }

        public bool Allows(string value)
        {
            if (_selected.Count == 0) return true;
            return !string.IsNullOrEmpty(value) && _selected.Contains(value);
        }

        void Toggle(string key, Button btn)
        {
            if (!_selected.Add(key))
                _selected.Remove(key);
            btn.EnableInClassList("filter-chip--on", _selected.Contains(key));
            Changed?.Invoke();
        }
    }
}
