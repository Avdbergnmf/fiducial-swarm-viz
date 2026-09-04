// Screen-space radio-budget chips. Hover any friendly to read remaining
// bytes; a drone that is nearly out of budget keeps a chip even unselected.

using System.Collections.Generic;
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
        VisualElement _host;
        readonly List<Chip> _pool = new();
        int _used;
        bool _wired;

        sealed class Chip
        {
            public Label Label;
            public int Slot;
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
            _wired = _host != null;
        }

        void Paint()
        {
            _used = 0;
            if (!_wired || _ctx?.State == null || _cam == null)
            {
                HideUnused();
                return;
            }

            var snaps = _ctx.State.Entities;
            int hover = -1;
            var hovered = _picker != null ? _picker.HoveredEntity : null;
            if (hovered != null && hovered.Current.Alive)
                hover = hovered.Slot;

            for (int s = 0; s < snaps.Count; s++)
            {
                if (!snaps[s].Alive) continue;
                var b = _ctx.State.Budget(s);
                if (!b.Has) continue;
                bool hoveredSlot = s == hover;
                if (!b.Low && !hoveredSlot) continue;
                Place(s, snaps[s].Position, b, hoveredSlot);
            }

            HideUnused();
        }

        void Place(int slot, Vector3 world, BudgetSnap b, bool hovered)
        {
            Vector3 sp = _cam.WorldToScreenPoint(world);
            if (sp.z < 0.4f) return;

            var chip = Next(slot);
            chip.Slot = slot;
            string name = _ctx.Run.Info(slot).Label;
            chip.Label.text = hovered || b.Low
                ? $"{name}  {b.Label()}"
                : b.Label();
            chip.Label.tooltip = b.Detail();
            chip.Label.EnableInClassList("budget-chip--low", b.Low && !b.Empty);
            chip.Label.EnableInClassList("budget-chip--empty", b.Empty);
            chip.Label.EnableInClassList("budget-chip--hover", hovered && !b.Low);

            Vector2 local = ScreenToLayout(new Vector2(sp.x, sp.y + 28f));
            chip.Label.style.left = local.x;
            chip.Label.style.top = local.y;
            chip.Label.style.display = DisplayStyle.Flex;
        }

        Chip Next(int slot)
        {
            if (_used < _pool.Count)
            {
                var reuse = _pool[_used++];
                reuse.Slot = slot;
                return reuse;
            }

            var label = new Label();
            label.AddToClassList("budget-chip");
            label.pickingMode = PickingMode.Position;
            int captured = slot;
            label.RegisterCallback<ClickEvent>(evt =>
            {
                var chip = ChipOf(evt.currentTarget);
                if (chip != null)
                    _ctx?.Selection?.SelectOnly(chip.Slot);
                evt.StopPropagation();
            });
            _host.Add(label);
            var created = new Chip { Label = label, Slot = captured };
            _pool.Add(created);
            _used++;
            return created;
        }

        Chip ChipOf(object target)
        {
            for (int i = 0; i < _pool.Count; i++)
                if (ReferenceEquals(_pool[i].Label, target))
                    return _pool[i];
            return null;
        }

        void HideUnused()
        {
            for (int i = _used; i < _pool.Count; i++)
                _pool[i].Label.style.display = DisplayStyle.None;
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
