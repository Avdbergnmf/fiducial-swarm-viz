// Raycasts against the entity picking layer to handle hover highlights, click
// selection (plain / Shift multi-select), drag-box selection (origins inside
// the rectangle), double-click inspector, and double-click on a log ping line
// (screen-space slack) to open both drones' inspectors on that log.
// Pick volume is a fat sphere (SceneBuilder.pickColliderRadius). Overlaps go
// to the origin closest to the ray; a miss still selects within pickPixelSlack.
//
// Pointer routing (explain this out loud):
//   1. UI Toolkit owns a panel that covers the whole Game view. panel.Pick()
//      returns the deepest element under the cursor whose pickingMode is Position.
//   2. Overlay roots (run picker, timeline wrappers) use picking-mode: ignore so
//      empty screen does NOT count as "over UI". Buttons and the timeline track
//      keep Position, so they still receive clicks.
//   3. If Pick hits a real widget, we skip the 3D raycast — otherwise the click
//      would both press Play and select a drone behind the button.
//   4. Entity hover is a flag on EntityView, independent of selection. Clearing
//      hover never clears selection; selecting never blocks hover.
//   5. Left press is not a click until release. Past marqueeSlop pixels it is a
//      box: every live origin whose screen point sits in the rectangle is
//      selected (AoE / Scene view). Shift or Ctrl unions with the current set.
//      Origins inside the live rectangle are hovered until release.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace SwarmViewer
{
    public sealed class EntityPicker : MonoBehaviour, IRunView
    {
        [SerializeField] LayerMask pickingLayer;
        [SerializeField] float doubleClickThreshold = 0.35f;
        [Tooltip("If the click misses every collider, still select the nearest origin within this many pixels.")]
        [SerializeField] float pickPixelSlack = 48f;
        [Tooltip("Pixels of movement before a press becomes a selection box instead of a click.")]
        [SerializeField] float marqueeSlop = 6f;
        [SerializeField] OrbitCameraController orbitCamera;
        [SerializeField] UIDocument uiDocument;
        [SerializeField] EntityInspectorView inspector;
        [SerializeField] CueOverlay cues;

        ViewerContext _ctx;
        Camera _cam;
        VisualElement _marqueeBox;

        EntityView _hoveredEntity;
        float _lastEntityClickTime;
        int _lastEntityClickSlot = -1;
        float _lastPingClickTime;
        int _lastPingIndex = -1;

        bool _pressing;
        bool _marquee;
        Vector2 _pressStart;
        Vector2 _pressNow;
        EntityView _pressHit;
        bool _pressIsPing;
        RelationPing _pressPing;
        int _pressPingIndex = -1;
        readonly List<int> _pingSel = new();
        readonly List<int> _boxHits = new();
        readonly List<EntityView> _boxViews = new();
        readonly List<EntityView> _marqueeHovered = new();

        /// <summary>Live origins inside the drag box. Empty when not dragging.</summary>
        public IReadOnlyList<EntityView> MarqueeHovered => _marqueeHovered;

        public EntityView HoveredEntity => _hoveredEntity;

        public void Bind(ViewerContext ctx)
        {
            CancelPress();
            ClearHover();
            _ctx = ctx;
            if (_cam == null) _cam = GetComponent<Camera>() ?? Camera.main;
            if (orbitCamera == null)
                orbitCamera = GetComponent<OrbitCameraController>();
            if (uiDocument == null)
                uiDocument = GetComponent<UIDocument>();
            if (inspector == null)
            {
                var found = FindObjectsByType<EntityInspectorView>(FindObjectsInactive.Include);
                if (found.Length > 0) inspector = found[0];
            }
            if (cues == null)
                cues = GetComponent<CueOverlay>() ?? FindAnyObjectByType<CueOverlay>();

            EnsureMarqueeBox();

            if (pickingLayer.value == 0)
            {
                int layer = LayerMask.NameToLayer("Picking");
                pickingLayer = layer >= 0 ? (1 << layer) : ~0;
            }
        }

        void OnEnable() => EnsureMarqueeBox();

        void OnDisable()
        {
            CancelPress();
            cues?.SetHoverPing(-1);
        }

        void Update()
        {
            if (_ctx == null || _cam == null) return;

            var mouse = Mouse.current;
            if (mouse == null) return;

            Vector2 mousePos = mouse.position.ReadValue();
            var keyboard = Keyboard.current;
            bool escape = keyboard != null && keyboard.escapeKey.wasPressedThisFrame;

            if (_pressing)
            {
                _pressNow = mousePos;
                float slop = Mathf.Max(1f, marqueeSlop);
                if (!_marquee && (mousePos - _pressStart).sqrMagnitude >= slop * slop)
                {
                    _marquee = true;
                    ClearHover();
                    cues?.SetHoverPing(-1);
                }

                SyncMarqueeVisual();
                SyncMarqueeHover();

                if (escape)
                {
                    CancelPress();
                    return;
                }

                if (!mouse.leftButton.isPressed)
                    FinishPress();
                return;
            }

            if (IsPointerOverUI(mousePos))
            {
                ClearHover();
                cues?.SetHoverPing(-1);
                return;
            }

            EntityView hitEntity = PickEntity(mousePos);
            bool pingHit = TryPreferPing(mousePos, hitEntity, out RelationPing ping, out int pingIndex, out _);
            if (pingHit)
                hitEntity = null;
            UpdateHover(hitEntity);
            cues?.SetHoverPing(pingHit ? pingIndex : -1);

            if (mouse.leftButton.wasPressedThisFrame)
            {
                _pressing = true;
                _marquee = false;
                _pressStart = mousePos;
                _pressNow = mousePos;
                _pressHit = hitEntity;
                _pressIsPing = pingHit;
                _pressPing = ping;
                _pressPingIndex = pingHit ? pingIndex : -1;
            }
        }

        void FinishPress()
        {
            bool box = _marquee;
            EntityView hit = _pressHit;
            bool wasPing = _pressIsPing;
            RelationPing pressPing = _pressPing;
            int pressPingIndex = _pressPingIndex;
            Vector2 a = _pressStart;
            Vector2 b = _pressNow;
            CancelPress();

            if (box)
            {
                CollectOriginsInBox(a, b, _boxHits);
                var keyboard = Keyboard.current;
                bool additive = keyboard != null &&
                    (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed
                     || keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed
                     || keyboard.leftCommandKey.isPressed || keyboard.rightCommandKey.isPressed);
                if (additive)
                    _ctx.Selection.Union(_boxHits);
                else
                    _ctx.Selection.Replace(_boxHits);
                _lastEntityClickSlot = -1;
                _lastPingIndex = -1;
                return;
            }

            if (wasPing) HandlePingClick(pressPing, pressPingIndex);
            else if (hit != null) HandleEntityClick(hit);
            else HandleEmptyClick();
        }

        void CancelPress()
        {
            _pressing = false;
            _marquee = false;
            _pressHit = null;
            _pressIsPing = false;
            _pressPingIndex = -1;
            ClearMarqueeHover();
            SyncMarqueeVisual();
        }

        void EnsureMarqueeBox()
        {
            if (uiDocument == null)
                uiDocument = GetComponent<UIDocument>();
            if (uiDocument == null) return;
            var root = uiDocument.rootVisualElement;
            if (root == null) return;

            if (_marqueeBox != null && _marqueeBox.panel != null) return;

            _marqueeBox = root.Q<VisualElement>("marqueeBox");
            if (_marqueeBox == null)
            {
                _marqueeBox = new VisualElement { name = "marqueeBox", pickingMode = PickingMode.Ignore };
                _marqueeBox.AddToClassList("marquee-box");
                root.Add(_marqueeBox);
            }

            _marqueeBox.pickingMode = PickingMode.Ignore;
            SyncMarqueeVisual();
        }

        void SyncMarqueeVisual()
        {
            if (_marqueeBox == null) return;

            if (!_marquee || uiDocument == null || uiDocument.rootVisualElement == null
                || uiDocument.rootVisualElement.panel == null)
            {
                _marqueeBox.style.display = DisplayStyle.None;
                return;
            }

            Vector2 a = ScreenToLayout(_pressStart);
            Vector2 b = ScreenToLayout(_pressNow);
            float xMin = Mathf.Min(a.x, b.x);
            float yMin = Mathf.Min(a.y, b.y);
            float w = Mathf.Abs(a.x - b.x);
            float h = Mathf.Abs(a.y - b.y);
            if (w < 1f && h < 1f)
            {
                _marqueeBox.style.display = DisplayStyle.None;
                return;
            }

            _marqueeBox.style.display = DisplayStyle.Flex;
            _marqueeBox.style.left = xMin;
            _marqueeBox.style.top = yMin;
            _marqueeBox.style.right = StyleKeyword.Auto;
            _marqueeBox.style.bottom = StyleKeyword.Auto;
            _marqueeBox.style.width = w;
            _marqueeBox.style.height = h;
        }

        /// <summary>
        /// Mouse / WorldToScreenPoint are origin-bottom, Y up. UITK style.top is
        /// origin-top, Y down. Map through the parent's worldBound so UI scale
        /// is included and Y is flipped once, on purpose.
        /// </summary>
        Vector2 ScreenToLayout(Vector2 screen)
        {
            var parent = _marqueeBox != null && _marqueeBox.parent != null
                ? _marqueeBox.parent
                : uiDocument.rootVisualElement;
            Rect wb = parent.worldBound;
            float nx = Screen.width > 1f ? screen.x / Screen.width : 0f;
            float ny = Screen.height > 1f ? 1f - screen.y / Screen.height : 0f;
            var world = new Vector2(wb.xMin + nx * wb.width, wb.yMin + ny * wb.height);
            return parent.WorldToLocal(world);
        }

        void CollectViewsInBox(Vector2 a, Vector2 b, List<EntityView> dst)
        {
            dst.Clear();
            float xMin = Mathf.Min(a.x, b.x);
            float xMax = Mathf.Max(a.x, b.x);
            float yMin = Mathf.Min(a.y, b.y);
            float yMax = Mathf.Max(a.y, b.y);

            var views = FindObjectsByType<EntityView>(FindObjectsInactive.Exclude);
            for (int i = 0; i < views.Length; i++)
            {
                var view = views[i];
                if (!IsPickable(view)) continue;
                Vector3 sp = _cam.WorldToScreenPoint(view.transform.position);
                if (sp.z < 0.1f) continue;
                if (sp.x < xMin || sp.x > xMax || sp.y < yMin || sp.y > yMax) continue;
                dst.Add(view);
            }
        }

        void CollectOriginsInBox(Vector2 a, Vector2 b, List<int> dst)
        {
            CollectViewsInBox(a, b, _boxViews);
            dst.Clear();
            for (int i = 0; i < _boxViews.Count; i++)
                dst.Add(_boxViews[i].Slot);
            dst.Sort();
        }

        void SyncMarqueeHover()
        {
            if (!_marquee)
            {
                ClearMarqueeHover();
                return;
            }

            CollectViewsInBox(_pressStart, _pressNow, _boxViews);

            for (int i = 0; i < _marqueeHovered.Count; i++)
            {
                var prev = _marqueeHovered[i];
                if (prev == null) continue;
                bool still = false;
                for (int j = 0; j < _boxViews.Count; j++)
                {
                    if (_boxViews[j] == prev)
                    {
                        still = true;
                        break;
                    }
                }
                if (!still)
                    prev.SetHovered(false);
            }

            _marqueeHovered.Clear();
            for (int i = 0; i < _boxViews.Count; i++)
            {
                var view = _boxViews[i];
                view.SetHovered(true);
                _marqueeHovered.Add(view);
            }
        }

        void ClearMarqueeHover()
        {
            for (int i = 0; i < _marqueeHovered.Count; i++)
            {
                if (_marqueeHovered[i] != null)
                    _marqueeHovered[i].SetHovered(false);
            }
            _marqueeHovered.Clear();
        }

        void UpdateHover(EntityView entity)
        {
            if (_hoveredEntity == entity) return;

            if (_hoveredEntity != null)
                _hoveredEntity.SetHovered(false);

            _hoveredEntity = entity;

            if (_hoveredEntity != null)
                _hoveredEntity.SetHovered(true);
        }

        void ClearHover()
        {
            if (_hoveredEntity == null) return;
            _hoveredEntity.SetHovered(false);
            _hoveredEntity = null;
        }

        void HandleEntityClick(EntityView entity)
        {
            var sel = _ctx.Selection;
            int slot = entity.Slot;
            var keyboard = Keyboard.current;
            bool shift = keyboard != null &&
                         (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
            bool ctrl = keyboard != null &&
                        (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed
                         || keyboard.leftCommandKey.isPressed || keyboard.rightCommandKey.isPressed);

            if (shift || ctrl)
            {
                sel.Toggle(slot);
                return;
            }

            float now = Time.unscaledTime;
            bool isDouble = slot == _lastEntityClickSlot &&
                            now - _lastEntityClickTime < doubleClickThreshold;
            _lastEntityClickTime = now;
            _lastEntityClickSlot = slot;
            _lastPingIndex = -1;

            sel.SelectOnly(slot);

            if (isDouble)
                inspector?.Open();
        }

        void HandlePingClick(RelationPing ping, int pingIndex)
        {
            _lastEntityClickSlot = -1;
            float now = Time.unscaledTime;
            bool isDouble = pingIndex == _lastPingIndex &&
                            now - _lastPingClickTime < doubleClickThreshold;
            _lastPingClickTime = now;
            _lastPingIndex = pingIndex;

            _pingSel.Clear();
            _pingSel.Add(ping.FromSlot);
            if (ping.ToSlot >= 0 && ping.ToSlot != ping.FromSlot)
                _pingSel.Add(ping.ToSlot);
            _ctx.Selection.Replace(_pingSel);

            if (isDouble)
                inspector?.OpenPing(ping.FromSlot, ping.ToSlot, ping.Line);
        }

        bool TryPreferPing(Vector2 mousePos, EntityView entity,
            out RelationPing ping, out int pingIndex, out float pingPixels)
        {
            ping = default;
            pingIndex = -1;
            pingPixels = float.MaxValue;
            if (cues == null || _cam == null) return false;
            if (!cues.TryPickPing(_cam, mousePos, pickPixelSlack, out ping, out pingIndex, out pingPixels))
                return false;
            if (entity == null) return true;
            Vector3 sp = _cam.WorldToScreenPoint(entity.transform.position);
            if (sp.z < 0.1f) return true;
            float entityPixels = Vector2.Distance(mousePos, new Vector2(sp.x, sp.y));
            // Near a craft, keep the drone click. Mid-line belongs to the ping.
            return pingPixels + 8f < entityPixels;
        }

        void HandleEmptyClick()
        {
            _lastEntityClickSlot = -1;
            _lastPingIndex = -1;
            _ctx.Selection.Clear();
        }

        /// <summary>
        /// True when a real widget sits under the cursor. Also used by the orbit
        /// camera, so a wheel over a panel scrolls the panel without zooming.
        /// </summary>
        public bool IsPointerOverUI(Vector2 screenPos)
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return true;

            if (uiDocument == null || uiDocument.rootVisualElement == null)
                return false;

            var root = uiDocument.rootVisualElement;
            if (root.panel == null) return false;

            // Screen y-up -> panel y-down. Overlay wrappers are picking-mode Ignore,
            // so Pick returns null / the document root over empty world, and the
            // actual Button / track over widgets.
            Vector2 panelPos = RuntimePanelUtils.ScreenToPanel(root.panel, screenPos);
            var picked = root.panel.Pick(panelPos);
            return picked != null && picked != root;
        }

        /// <summary>
        /// Fat collider hit, then screen-space slack. Overlaps resolve to the
        /// origin closest to the click ray (AoE-style: nearest unit, not first mesh).
        /// </summary>
        EntityView PickEntity(Vector2 mousePos)
        {
            Ray ray = _cam.ScreenPointToRay(mousePos);
            var hits = Physics.RaycastAll(ray, 5000f, pickingLayer);
            EntityView best = null;
            float bestDist = float.MaxValue;

            for (int i = 0; i < hits.Length; i++)
            {
                var view = hits[i].collider.GetComponentInParent<EntityView>();
                if (!IsPickable(view)) continue;
                float d = DistanceRayToPoint(ray, view.transform.position);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = view;
                }
            }

            if (best != null)
                return best;

            return NearestOnScreen(mousePos, pickPixelSlack);
        }

        EntityView NearestOnScreen(Vector2 mousePos, float maxPixels)
        {
            var views = FindObjectsByType<EntityView>(FindObjectsInactive.Exclude);
            EntityView best = null;
            float bestDist = maxPixels;

            for (int i = 0; i < views.Length; i++)
            {
                var view = views[i];
                if (!IsPickable(view)) continue;
                Vector3 sp = _cam.WorldToScreenPoint(view.transform.position);
                if (sp.z < 0.1f) continue;
                float d = Vector2.Distance(mousePos, new Vector2(sp.x, sp.y));
                if (d < bestDist)
                {
                    bestDist = d;
                    best = view;
                }
            }

            return best;
        }

        static bool IsPickable(EntityView view) =>
            view != null && view.gameObject.activeInHierarchy && view.Current.Alive;

        static float DistanceRayToPoint(Ray ray, Vector3 point) =>
            Vector3.Cross(ray.direction, point - ray.origin).magnitude;
    }
}
