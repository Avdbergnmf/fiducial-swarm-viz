// Raycasts against the entity picking layer to handle hover highlights, click
// selection (plain / Shift multi-select), and double-click camera resets.
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

        ViewerContext _ctx;
        Camera _cam;
        OrbitCameraController _orbitCamera;
        UIDocument _uiDocument;

        EntityView _hoveredEntity;
        float _lastEmptyClickTime;

        public EntityView HoveredEntity => _hoveredEntity;

        public void Bind(ViewerContext ctx)
        {
            ClearHover();
            _ctx = ctx;
            if (_cam == null) _cam = GetComponent<Camera>() ?? Camera.main;
            if (_orbitCamera == null) _orbitCamera = FindFirstObjectByType<OrbitCameraController>();
            if (_uiDocument == null) _uiDocument = FindFirstObjectByType<UIDocument>();

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

            Ray ray = _cam.ScreenPointToRay(mousePos);
            EntityView hitEntity = null;

            if (Physics.Raycast(ray, out RaycastHit hit, 5000f, pickingLayer))
                hitEntity = hit.collider.GetComponentInParent<EntityView>();

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

            if (shift)
            {
                sel.Toggle(slot);
                return;
            }

            // Plain click on the only selected entity: deselect. Otherwise replace.
            if (sel.Count == 1 && sel.Primary == slot)
                sel.Clear();
            else
                sel.SelectOnly(slot);
        }

        void HandleEmptyClick()
        {
            float now = Time.unscaledTime;
            if (now - _lastEmptyClickTime < doubleClickThreshold)
            {
                _ctx.Selection.Clear();
                if (_orbitCamera != null)
                    _orbitCamera.ResetToDefaultView();
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

            if (_uiDocument == null || _uiDocument.rootVisualElement == null)
                return false;

            var root = _uiDocument.rootVisualElement;
            if (root.panel == null) return false;

            // Screen y-up -> panel y-down. Overlay wrappers are picking-mode Ignore,
            // so Pick returns null / the document root over empty world, and the
            // actual Button / track over widgets.
            Vector2 panelPos = RuntimePanelUtils.ScreenToPanel(root.panel, screenPos);
            var picked = root.panel.Pick(panelPos);
            return picked != null && picked != root;
        }
    }
}
