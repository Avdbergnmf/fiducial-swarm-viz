// Unity Scene-view camera, adapted for this viewer: MMB pans, RMB orbits,
// WASD/QE fly only while RMB is held, scroll zooms. A selection still tracks
// until the user actually navigates; then the lock drops and we keep going
// from the current pose. Zoom stands down while the cursor is over a widget.

using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace SwarmViewer
{
    public sealed class OrbitCameraController : MonoBehaviour, IRunView
    {
        [Header("Orbit Controls")]
        [SerializeField] float yawSpeed = 0.2f;
        [SerializeField] float pitchSpeed = 0.2f;
        [SerializeField] float keyOrbitSpeed = 90f;
        [SerializeField] float zoomSensitivity = 0.05f;
        [Tooltip("Hold Shift while scrolling to multiply zoom speed. Same idea as pan sprint.")]
        [SerializeField] float zoomSprintMultiplier = 3f;
        [SerializeField] float panSpeed = 60f;
        [SerializeField] float panSprintMultiplier = 3f;
        [Tooltip("MMB pan scale. World travel is also proportional to distance / screen height.")]
        [SerializeField] float panMouseSensitivity = 1f;
        [SerializeField] float followSmoothRate = 8f;

        [Header("Clamps")]
        [SerializeField] float minPitch = 5f;
        [SerializeField] float maxPitch = 85f;

        ViewerContext _ctx;
        Camera _cam;
        EntityPicker _picker;
        UIDocument _uiDocument;

        Vector3 _targetFocus;
        Vector3 _currentFocus;
        float _yaw;
        float _pitch = 45f;
        float _distance = 250f;

        Vector3 _defaultFocus;
        float _defaultYaw;
        float _defaultPitch = 45f;
        float _defaultDistance = 250f;
        float _minDistance = 10f;
        float _maxDistance = 600f;

        int _lastSelectionCount;
        bool _followSelection;
        bool _rmbHeld;
        bool _mmbHeld;

        /// <summary>Per-entity camera-focus weight. Change this one constant later.</summary>
        const float FocusWeight = 1f;

        public void Bind(ViewerContext ctx)
        {
            if (_ctx != null)
                _ctx.Selection.OnSelectionChanged -= HandleSelectionChanged;

            _ctx = ctx;
            if (_cam == null) _cam = GetComponent<Camera>() ?? Camera.main;
            if (_picker == null) _picker = GetComponent<EntityPicker>();
            if (_uiDocument == null) _uiDocument = GetComponent<UIDocument>();

            CalculateDefaultView(ctx.Run.Meta);
            ResetToDefaultView();

            _lastSelectionCount = 0;
            _followSelection = false;
            _rmbHeld = false;
            _mmbHeld = false;
            ctx.Selection.OnSelectionChanged += HandleSelectionChanged;
        }

        void OnDestroy()
        {
            if (_ctx != null)
                _ctx.Selection.OnSelectionChanged -= HandleSelectionChanged;
        }

        void CalculateDefaultView(RunMeta meta)
        {
            Vector3 assetPos = Vector3.zero;
            if (meta.asset?.position != null && meta.asset.position.Length >= 3)
                assetPos = new Vector3(meta.asset.position[0], meta.asset.position[1], meta.asset.position[2]);

            _defaultFocus = assetPos;

            float minX = meta.arena?.min != null && meta.arena.min.Length > 0 ? meta.arena.min[0] : -200f;
            float maxX = meta.arena?.max != null && meta.arena.max.Length > 0 ? meta.arena.max[0] : 200f;
            float minZ = meta.arena?.min != null && meta.arena.min.Length > 2 ? meta.arena.min[2] : -200f;
            float maxZ = meta.arena?.max != null && meta.arena.max.Length > 2 ? meta.arena.max[2] : 200f;

            float arenaWidth = Mathf.Max(maxX - minX, maxZ - minZ, 100f);
            _defaultYaw = 0f;
            _defaultPitch = 45f;
            _defaultDistance = arenaWidth * 0.9f;

            _minDistance = 5f;
            _maxDistance = arenaWidth * 2.5f;
        }

        public void ResetToDefaultView()
        {
            _targetFocus = _defaultFocus;
            _currentFocus = _defaultFocus;
            _yaw = _defaultYaw;
            _pitch = _defaultPitch;
            _distance = _defaultDistance;
            _followSelection = false;
            UpdateCameraTransform();
        }

        void HandleSelectionChanged(int _)
        {
            int count = _ctx != null && _ctx.Selection != null ? _ctx.Selection.Count : 0;
            if (count == 0 && _lastSelectionCount > 0)
            {
                // Empty set: detach WHERE THE CAMERA IS. Do not recentre on the asset.
                _targetFocus = _currentFocus;
            }
            _followSelection = count > 0;
            _lastSelectionCount = count;
        }

        void LateUpdate()
        {
            if (_cam == null) return;

            HandleInput();
            UpdateFocusPosition();
            UpdateCameraTransform();
        }

        void HandleInput()
        {
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            bool overUi = mouse != null && PointerOverUi(mouse);
            bool typing = TextFieldFocused();

            if (mouse != null)
            {
                if (mouse.rightButton.wasPressedThisFrame)
                    _rmbHeld = !overUi;
                if (mouse.rightButton.wasReleasedThisFrame || !mouse.rightButton.isPressed)
                    _rmbHeld = false;

                if (mouse.middleButton.wasPressedThisFrame)
                    _mmbHeld = !overUi;
                if (mouse.middleButton.wasReleasedThisFrame || !mouse.middleButton.isPressed)
                    _mmbHeld = false;

                Vector2 delta = mouse.delta.ReadValue();
                bool dragged = delta.sqrMagnitude > 0.25f;

                if (_rmbHeld && dragged)
                {
                    ReleaseFollow();
                    _yaw += delta.x * yawSpeed;
                    _pitch -= delta.y * pitchSpeed;
                }

                if (_mmbHeld && dragged)
                {
                    float s = panMouseSensitivity * _distance / Mathf.Max(200f, Screen.height);
                    NudgeFocus((-_cam.transform.right * delta.x - _cam.transform.up * delta.y) * s);
                }
            }

            if (keyboard != null && !typing)
            {
                bool chord = keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed
                             || keyboard.leftCommandKey.isPressed || keyboard.rightCommandKey.isPressed;
                if (!chord && keyboard.rKey.wasPressedThisFrame)
                    ResetToDefaultView();

                float keyDt = Time.unscaledDeltaTime;
                bool orbited = false;
                if (keyboard.leftArrowKey.isPressed) { _yaw -= keyOrbitSpeed * keyDt; orbited = true; }
                if (keyboard.rightArrowKey.isPressed) { _yaw += keyOrbitSpeed * keyDt; orbited = true; }
                if (keyboard.upArrowKey.isPressed) { _pitch -= keyOrbitSpeed * keyDt; orbited = true; }
                if (keyboard.downArrowKey.isPressed) { _pitch += keyOrbitSpeed * keyDt; orbited = true; }
                if (orbited) ReleaseFollow();
            }

            _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);

            if (mouse != null)
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f && !overUi)
                {
                    bool zoomSprint = keyboard != null &&
                        (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
                    float zoom = zoomSensitivity * (zoomSprint ? zoomSprintMultiplier : 1f);
                    _distance -= scroll * zoom;
                    _distance = Mathf.Clamp(_distance, _minDistance, _maxDistance);
                }
            }

            if (!_rmbHeld || keyboard == null || typing) return;

            bool blocked = keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed
                         || keyboard.leftCommandKey.isPressed || keyboard.rightCommandKey.isPressed;
            if (blocked) return;

            Vector3 move = Vector3.zero;
            if (keyboard.wKey.isPressed) move += Vector3.forward;
            if (keyboard.sKey.isPressed) move += Vector3.back;
            if (keyboard.aKey.isPressed) move += Vector3.left;
            if (keyboard.dKey.isPressed) move += Vector3.right;
            if (keyboard.eKey.isPressed) move += Vector3.up;
            if (keyboard.qKey.isPressed) move += Vector3.down;

            if (move.sqrMagnitude < 0.001f) return;

            move.Normalize();
            bool sprint = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
            float pan = panSpeed * (sprint ? panSprintMultiplier : 1f);
            float step = pan * (_distance / 100f) * Time.unscaledDeltaTime;

            Vector3 camFwd = _cam.transform.forward;
            Vector3 camRight = _cam.transform.right;
            NudgeFocus((camFwd * move.z + camRight * move.x + Vector3.up * move.y) * step);
        }

        void ReleaseFollow()
        {
            if (!_followSelection) return;
            _followSelection = false;
            _targetFocus = _currentFocus;
        }

        void NudgeFocus(Vector3 delta)
        {
            ReleaseFollow();
            _targetFocus += delta;
            _currentFocus += delta;
        }

        /// <summary>A wheel over a panel belongs to that panel's scroll, not to zoom.</summary>
        bool PointerOverUi(Mouse mouse) =>
            _picker != null && _picker.IsPointerOverUI(mouse.position.ReadValue());

        bool TextFieldFocused()
        {
            if (_uiDocument == null) _uiDocument = GetComponent<UIDocument>();
            if (_uiDocument == null || _uiDocument.rootVisualElement == null) return false;
            var focused = _uiDocument.rootVisualElement.focusController?.focusedElement;
            return focused is TextField || focused is TextInputBaseField<string>;
        }

        void UpdateFocusPosition()
        {
            if (_followSelection && TryGetSelectionFocus(out Vector3 centroid))
                _targetFocus = centroid;

            _currentFocus = Vector3.Lerp(_currentFocus, _targetFocus, Time.unscaledDeltaTime * followSmoothRate);
        }

        bool TryGetSelectionFocus(out Vector3 focus)
        {
            focus = default;
            if (_ctx?.Selection == null || _ctx.Selection.Count == 0) return false;

            Vector3 acc = Vector3.zero;
            float wsum = 0f;
            var slots = _ctx.Selection.Slots;
            for (int i = 0; i < slots.Count; i++)
            {
                int slot = slots[i];
                if (slot < 0 || slot >= _ctx.State.Entities.Count) continue;
                var snap = _ctx.State.Entities[slot];
                if (!snap.Alive) continue;
                acc += snap.Position * FocusWeight;
                wsum += FocusWeight;
            }

            if (wsum <= 0f) return false;
            focus = acc / wsum;
            return true;
        }

        void UpdateCameraTransform()
        {
            Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 dir = rot * -Vector3.forward;
            _cam.transform.position = _currentFocus + dir * _distance;
            _cam.transform.rotation = rot;
        }
    }
}
