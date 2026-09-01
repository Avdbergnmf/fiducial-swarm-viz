// Raycasts against the entity picking layer to handle hover highlights, click
// selection (plain / Shift multi-select), double-click inspector, and empty
// double-click camera resets.
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
        [SerializeField] OrbitCameraController orbitCamera;
        [SerializeField] UIDocument uiDocument;
        [SerializeField] EntityInspectorView inspector;

        ViewerContext _ctx;
        Camera _cam;

        EntityView _hoveredEntity;
        float _lastEmptyClickTime;
        float _lastEntityClickTime;
        int _lastEntityClickSlot = -1;

        public EntityView HoveredEntity => _hoveredEntity;

        public void Bind(ViewerContext ctx)
        {
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

            if (pickingLayer.value == 0)
            {
                int layer = LayerMask.NameToLayer("Picking");
                pickingLayer = layer >= 0 ? (1 << layer) : ~0;
            }
        }

        void Update()
        {
            if (_ctx == null || _cam == null) return;

            var mouse = Mouse.current;
            if (mouse == null) return;

            Vector2 mousePos = mouse.position.ReadValue();

            if (IsPointerOverUI(mousePos))
            {
                ClearHover();
                return;
            }

            EntityView hitEntity = PickEntity(mousePos);

            UpdateHover(hitEntity);

            if (mouse.leftButton.wasPressedThisFrame)
            {
                if (hitEntity != null) HandleEntityClick(hitEntity);
                else HandleEmptyClick();
            }
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
            _lastEmptyClickTime = 0f;

            // Click-again-to-deselect fights double-click. Empty click / Esc clears.
            if (sel.Count != 1 || sel.Primary != slot)
                sel.SelectOnly(slot);

            if (isDouble)
                inspector?.Open();
        }

        void HandleEmptyClick()
        {
            _lastEntityClickSlot = -1;
            float now = Time.unscaledTime;
            if (now - _lastEmptyClickTime < doubleClickThreshold)
            {
                _ctx.Selection.Clear();
                if (orbitCamera != null)
                    orbitCamera.ResetToDefaultView();
                _lastEmptyClickTime = 0f;
            }
            else
            {
                _ctx.Selection.Clear();
                _lastEmptyClickTime = now;
            }
        }

        bool IsPointerOverUI(Vector2 screenPos)
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
