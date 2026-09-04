// Screen-space hover chips. A living craft under the cursor shows who it is
// (and flight mode / radio budget when those exist). A ping stroke shows every
// currently visible ping between that same pair, stacked in one card. Low
// radio budget still pins a chip on a friendly that is not hovered.

using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace SwarmViewer
{
    public sealed class BudgetOverlay : MonoBehaviour, IRunView
    {
        [SerializeField] UIDocument uiDocument;

        ViewerContext _ctx;
        Camera _cam;
        EntityPicker _picker;
        CueOverlay _cues;
        VisualElement _host;
        readonly List<Chip> _pool = new();
        readonly List<RelationPing> _pingGroup = new();
        readonly List<int> _pingSel = new();
        readonly StringBuilder _tip = new();
        int _used;
        bool _wired;

        sealed class Chip
        {
            public VisualElement Root;
            public Label Title;
            public VisualElement Body;
            public readonly List<Label> Rows = new();
            public int Slot;
            public int SlotB;
            public bool Ping;
        }

        public void Bind(ViewerContext ctx)
        {
            _ctx = ctx;
            TryWire();
        }

        void OnEnable() => TryWire();
        void Start() => TryWire();

        void LateUpdate()
        {
            if (!_wired) TryWire();
            Paint();
        }

        void TryWire()
        {
            if (uiDocument == null)
                uiDocument = GetComponent<UIDocument>();
            if (uiDocument == null) return;
            var root = uiDocument.rootVisualElement;
            if (root == null) return;

            _host = root.Q("budgetHud");
            if (_host == null)
            {
                _host = new VisualElement { name = "budgetHud" };
                _host.pickingMode = PickingMode.Ignore;
                _host.AddToClassList("budget-hud");
                root.Add(_host);
            }

            _cam = GetComponent<Camera>();
            if (_cam == null) _cam = Camera.main;
            _picker = GetComponent<EntityPicker>();
            if (_picker == null)
            {
                var pickers = FindObjectsByType<EntityPicker>(FindObjectsInactive.Include);
                if (pickers != null && pickers.Length > 0)
                    _picker = pickers[0];
            }
            _cues = GetComponent<CueOverlay>();
            if (_cues == null)
            {
                var cues = FindObjectsByType<CueOverlay>(FindObjectsInactive.Include);
                if (cues != null && cues.Length > 0)
                    _cues = cues[0];
            }
            _wired = _host != null;
        }

        void Paint()
        {
            _used = 0;
            if (!_wired || _ctx?.State == null || _ctx.Run == null || _cam == null)
            {
                HideUnused();
                return;
            }

            int pingA = -1, pingB = -1;
            bool pingCard = TryPlacePing(out pingA, out pingB);

            int hover = -1;
            if (!pingCard)
            {
                var hovered = _picker != null ? _picker.HoveredEntity : null;
                if (hovered != null && hovered.Current.Alive)
                    hover = hovered.Slot;
            }

            var snaps = _ctx.State.Entities;
            for (int s = 0; s < snaps.Count; s++)
            {
                if (!snaps[s].Alive) continue;
                if (s == pingA || s == pingB) continue;

                bool hoveredSlot = s == hover;
                var b = _ctx.State.Budget(s);
                if (hoveredSlot)
                    PlaceEntity(s, snaps[s].Position, b, hovered: true);
                else if (b.Has && b.Low)
                    PlaceEntity(s, snaps[s].Position, b, hovered: false);
            }

            HideUnused();
        }

        bool TryPlacePing(out int slotA, out int slotB)
        {
            slotA = -1;
            slotB = -1;
            if (_cues == null || !_cues.TryHoverPingGroup(_pingGroup, out Vector3 mid))
                return false;
            if (_pingGroup.Count == 0) return false;

            Vector3 sp = _cam.WorldToScreenPoint(mid);
            if (sp.z < 0.4f) return false;

            var seed = _pingGroup[0];
            slotA = seed.FromSlot;
            slotB = seed.ToSlot;

            var chip = Next();
            chip.Ping = true;
            chip.Slot = slotA;
            chip.SlotB = slotB;
            StylePing(chip);
            PlaceAt(chip, new Vector2(sp.x, sp.y));
            return true;
        }

        void PlaceEntity(int slot, Vector3 world, BudgetSnap b, bool hovered)
        {
            Vector3 sp = _cam.WorldToScreenPoint(world);
            if (sp.z < 0.4f) return;

            var chip = Next();
            chip.Ping = false;
            chip.Slot = slot;
            chip.SlotB = -1;
            StyleEntity(chip, slot, b, hovered);
            PlaceAt(chip, new Vector2(sp.x, sp.y + 28f));
        }

        void StyleEntity(Chip chip, int slot, BudgetSnap b, bool hovered)
        {
            var info = _ctx.Run.Info(slot);
            string name = info.Label;
            float t = _ctx.State.Time;

            chip.Root.EnableInClassList("hover-chip--ping", false);
            chip.Root.EnableInClassList("budget-chip--hover", hovered && !b.Low);
            chip.Root.EnableInClassList("budget-chip--low", b.Low && !b.Empty);
            chip.Root.EnableInClassList("budget-chip--empty", b.Empty);

            if (!hovered)
            {
                chip.Title.text = $"{name}  {b.Label()}";
                ShowRows(chip, 0);
            }
            else
            {
                chip.Title.text = name;
                int n = 0;
                if (info.IsFriendly)
                {
                    var logs = _ctx.Run.LogsForDrone(info.drone_id);
                    SetRow(chip, n++, LogPhrase.StanceTitle(logs, t), Color.clear);
                }
                if (b.Has)
                    SetRow(chip, n++, b.Label(), Color.clear);
                ShowRows(chip, n);
            }

            _tip.Clear();
            _tip.Append(info.IdBlurb);
            if (b.Has)
            {
                _tip.Append('\n').Append('\n');
                _tip.Append(b.Detail());
            }
            chip.Root.tooltip = _tip.ToString();
        }

        void StylePing(Chip chip)
        {
            chip.Root.EnableInClassList("hover-chip--ping", true);
            chip.Root.EnableInClassList("budget-chip--hover", false);
            chip.Root.EnableInClassList("budget-chip--low", false);
            chip.Root.EnableInClassList("budget-chip--empty", false);

            float now = _ctx.State.Time;
            var first = _pingGroup[0];
            string aName = _ctx.Run.Info(first.FromSlot).Label;
            string bName = _ctx.Run.Info(first.ToSlot).Label;
            int count = _pingGroup.Count;

            if (count == 1)
            {
                chip.Title.text = $"{CueOverlay.PingKindLabel(first.Kind)}  {aName} → {bName}";
            }
            else
            {
                string lo = _ctx.Run.Info(Mathf.Min(first.FromSlot, first.ToSlot)).Label;
                string hi = _ctx.Run.Info(Mathf.Max(first.FromSlot, first.ToSlot)).Label;
                chip.Title.text = $"{lo} · {hi}";
            }

            _tip.Clear();
            int rows = 0;
            for (int i = 0; i < count; i++)
            {
                var ping = _pingGroup[i];
                string from = _ctx.Run.Info(ping.FromSlot).Label;
                string to = _ctx.Run.Info(ping.ToSlot).Label;
                string kind = CueOverlay.PingKindLabel(ping.Kind);
                string cap = LogPhrase.PingCaption(ping.Kind, ping.Line != null ? ping.Line.text : "",
                    now, ping.T, ping.Hold);
                string row = count == 1
                    ? cap
                    : string.IsNullOrEmpty(cap) ? kind : $"{kind}  {cap}";
                if (!string.IsNullOrEmpty(row))
                    SetRow(chip, rows++, row, Palette.Opaque(Palette.Ping(ping.Kind)));

                if (i > 0) _tip.Append('\n').Append('\n');
                _tip.Append(kind).Append("  ").Append(from).Append(" → ").Append(to);
                if (ping.Line != null)
                {
                    _tip.Append('\n');
                    _tip.Append(ping.Line.Pretty);
                }
            }
            ShowRows(chip, rows);
            chip.Root.tooltip = _tip.ToString();
        }

        void PlaceAt(Chip chip, Vector2 screen)
        {
            Vector2 local = ScreenToLayout(screen);
            chip.Root.style.left = local.x;
            chip.Root.style.top = local.y;
            chip.Root.style.display = DisplayStyle.Flex;
        }

        void SetRow(Chip chip, int i, string text, Color color)
        {
            var row = EnsureRow(chip, i);
            row.text = text;
            row.style.color = color.a > 0.01f ? new StyleColor(color) : StyleKeyword.Null;
        }

        Label EnsureRow(Chip chip, int i)
        {
            while (chip.Rows.Count <= i)
            {
                var row = new Label();
                row.AddToClassList("hover-chip__row");
                row.pickingMode = PickingMode.Ignore;
                chip.Body.Add(row);
                chip.Rows.Add(row);
            }
            return chip.Rows[i];
        }

        void ShowRows(Chip chip, int n)
        {
            for (int i = 0; i < chip.Rows.Count; i++)
                chip.Rows[i].style.display = i < n ? DisplayStyle.Flex : DisplayStyle.None;
        }

        Chip Next()
        {
            if (_used < _pool.Count)
                return _pool[_used++];

            var root = new VisualElement();
            root.AddToClassList("budget-chip");
            root.AddToClassList("hover-chip");
            root.pickingMode = PickingMode.Position;

            var title = new Label();
            title.AddToClassList("hover-chip__title");
            title.pickingMode = PickingMode.Ignore;
            root.Add(title);

            var body = new VisualElement();
            body.AddToClassList("hover-chip__body");
            body.pickingMode = PickingMode.Ignore;
            root.Add(body);

            root.RegisterCallback<ClickEvent>(OnChipClick);
            _host.Add(root);

            var created = new Chip { Root = root, Title = title, Body = body };
            _pool.Add(created);
            _used++;
            return created;
        }

        void OnChipClick(ClickEvent evt)
        {
            var chip = ChipOf(evt.currentTarget);
            if (chip == null || _ctx?.Selection == null)
            {
                evt.StopPropagation();
                return;
            }

            if (chip.Ping)
            {
                _pingSel.Clear();
                _pingSel.Add(chip.Slot);
                if (chip.SlotB >= 0 && chip.SlotB != chip.Slot)
                    _pingSel.Add(chip.SlotB);
                _ctx.Selection.Replace(_pingSel);
            }
            else
            {
                _ctx.Selection.SelectOnly(chip.Slot);
            }
            evt.StopPropagation();
        }

        Chip ChipOf(object target)
        {
            for (int i = 0; i < _pool.Count; i++)
                if (ReferenceEquals(_pool[i].Root, target))
                    return _pool[i];
            return null;
        }

        void HideUnused()
        {
            for (int i = _used; i < _pool.Count; i++)
                _pool[i].Root.style.display = DisplayStyle.None;
        }

        Vector2 ScreenToLayout(Vector2 screen)
        {
            var parent = _host.parent != null ? _host.parent : _host;
            Rect wb = parent.worldBound;
            float nx = Screen.width > 1f ? screen.x / Screen.width : 0f;
            float ny = Screen.height > 1f ? 1f - screen.y / Screen.height : 0f;
            var world = new Vector2(wb.xMin + nx * wb.width, wb.yMin + ny * wb.height);
            return parent.WorldToLocal(world);
        }
    }
}
