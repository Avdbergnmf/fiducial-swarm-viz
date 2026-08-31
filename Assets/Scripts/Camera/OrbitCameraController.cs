// RuneScape-style orbit camera: smooth tracking of the selection centroid,
// WASD pan (only with an empty selection; Shift sprints pan), MMB / arrows orbit,
// scroll zoom. Orbit and zoom speeds are never multiplied by Shift.

using UnityEngine;
using UnityEngine.InputSystem;

namespace SwarmViewer
{
    public sealed class OrbitCameraController : MonoBehaviour, IRunView
    {
        [Header("Orbit Controls")]
        [SerializeField] float yawSpeed = 0.2f;
        [SerializeField] float pitchSpeed = 0.2f;
        [SerializeField] float keyOrbitSpeed = 90f;
        [SerializeField] float zoomSensitivity = 0.05f;
        [SerializeField] float panSpeed = 60f;
        [SerializeField] float panSprintMultiplier = 3f;
        [SerializeField] float followSmoothRate = 8f;

        [Header("Clamps")]
        [SerializeField] float minPitch = 5f;
        [SerializeField] float maxPitch = 85f;

        ViewerContext _ctx;
        Camera _cam;

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

        /// <summary>Per-entity camera-focus weight. Change this one constant later.</summary>
        const float FocusWeight = 1f;

        public void Bind(ViewerContext ctx)
        {
            if (_ctx != null)
                _ctx.Selection.OnSelectionChanged -= HandleSelectionChanged;

            _ctx = ctx;
            if (_cam == null) _cam = GetComponent<Camera>() ?? Camera.main;

            CalculateDefaultView(ctx.Run.Meta);
            ResetToDefaultView();

            _lastSelectionCount = 0;
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

            if (mouse != null && mouse.middleButton.isPressed)
            {
                Vector2 delta = mouse.delta.ReadValue();
                _yaw += delta.x * yawSpeed;
                _pitch -= delta.y * pitchSpeed;
            }

            if (keyboard != null)
            {
                float keyDt = Time.unscaledDeltaTime;
                if (keyboard.leftArrowKey.isPressed) _yaw -= keyOrbitSpeed * keyDt;
                if (keyboard.rightArrowKey.isPressed) _yaw += keyOrbitSpeed * keyDt;
                if (keyboard.upArrowKey.isPressed) _pitch -= keyOrbitSpeed * keyDt;
                if (keyboard.downArrowKey.isPressed) _pitch += keyOrbitSpeed * keyDt;
            }

            _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);

            if (mouse != null)
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f)
                {
                    _distance -= scroll * zoomSensitivity;
                    _distance = Mathf.Clamp(_distance, _minDistance, _maxDistance);
                }
            }

            bool tracking = _ctx != null && _ctx.Selection != null && _ctx.Selection.Count > 0;
            if (!tracking && keyboard != null)
            {
                Vector3 move = Vector3.zero;
                if (keyboard.wKey.isPressed) move += Vector3.forward;
                if (keyboard.sKey.isPressed) move += Vector3.back;
                if (keyboard.aKey.isPressed) move += Vector3.left;
                if (keyboard.dKey.isPressed) move += Vector3.right;

                if (move.sqrMagnitude > 0.001f)
                {
                    move.Normalize();
                    Vector3 camFwd = _cam.transform.forward;
                    camFwd.y = 0f;
                    camFwd.Normalize();

                    Vector3 camRight = _cam.transform.right;
                    camRight.y = 0f;
                    camRight.Normalize();

                    bool sprint = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
                    float pan = panSpeed * (sprint ? panSprintMultiplier : 1f);

                    Vector3 panDir = camFwd * move.z + camRight * move.x;
                    _targetFocus += panDir * (pan * (_distance / 100f) * Time.unscaledDeltaTime);
                }
            }
        }

        void UpdateFocusPosition()
        {
            if (TryGetSelectionFocus(out Vector3 centroid))
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
