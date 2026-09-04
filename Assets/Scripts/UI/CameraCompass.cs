// Circular heading dial: a ring and a north arrow. The arrow sits at the top
// of a full-size plate; we rotate the plate by camera yaw so north stays north.
// Lives above the timeline overlay so the – button is clickable.

using UnityEngine;
using UnityEngine.UIElements;

namespace SwarmViewer
{
    [DefaultExecutionOrder(200)]
    public sealed class CameraCompass : MonoBehaviour
    {
        [SerializeField] UIDocument uiDocument;

        Camera _camera;
        VisualElement _root;
        VisualElement _needle;
        Button _toggle;
        float _angle = float.NaN;
        bool _collapsed;
        bool _wired;

        void OnEnable() => TryWire();
        void Start() => TryWire();

        void OnDisable()
        {
            if (_toggle != null)
                _toggle.clicked -= Toggle;
            _wired = false;
        }

        void LateUpdate()
        {
            if (!_wired)
            {
                TryWire();
                if (!_wired) return;
            }

            if (_camera == null)
            {
                _camera = GetComponent<Camera>();
                if (_camera == null)
                    _camera = Camera.main;
            }
            if (_camera == null || _collapsed || _needle == null) return;

            // Orbit camera has no roll: yaw 0 looks world +Z (north). UITK
            // rotate is clockwise-positive, so north stays at the rim opposite
            // the heading.
            float yaw = _camera.transform.eulerAngles.y;
            float deg = -yaw;
            if (!float.IsNaN(_angle) && Mathf.Abs(Mathf.DeltaAngle(_angle, deg)) < 0.4f)
                return;
            _angle = deg;
            _needle.style.rotate = new Rotate(new Angle(deg, AngleUnit.Degree));
        }

        void TryWire()
        {
            if (_wired) return;
            if (uiDocument == null)
            {
                var docs = FindObjectsByType<UIDocument>(FindObjectsInactive.Include);
                if (docs != null && docs.Length > 0)
                    uiDocument = docs[0];
            }
            if (uiDocument == null) return;

            var documentRoot = uiDocument.rootVisualElement;
            if (documentRoot == null) return;

            _root = UiQuery.Named<VisualElement>(documentRoot, "cameraCompassRoot");
            _needle = UiQuery.Named<VisualElement>(documentRoot, "cameraCompassNeedle");
            _toggle = UiQuery.Named<Button>(documentRoot, "cameraCompassToggle");
            if (_root == null || _needle == null || _toggle == null)
                return;

            _needle.style.transformOrigin = new TransformOrigin(Length.Percent(50), Length.Percent(50));
            _needle.usageHints = UsageHints.DynamicTransform;
            _camera = GetComponent<Camera>();
            if (_camera == null)
                _camera = Camera.main;
            _toggle.clicked += Toggle;
            UiQuery.HitSelf(_toggle);
            _collapsed = ViewerSettings.Load().compassCollapsed;
            _wired = true;
            ApplyCollapsed();
        }

        void Toggle()
        {
            _collapsed = !_collapsed;
            var settings = ViewerSettings.Load();
            if (settings.compassCollapsed != _collapsed)
            {
                settings.compassCollapsed = _collapsed;
                settings.Save();
            }
            ApplyCollapsed();
        }

        void ApplyCollapsed()
        {
            if (_root == null || _toggle == null) return;
            _root.EnableInClassList("camera-compass-root--collapsed", _collapsed);
            _toggle.text = _collapsed ? "+" : "–";
            _toggle.tooltip = _collapsed ? "Show compass" : "Hide compass";
            UiQuery.HitSelf(_toggle);
            if (!_collapsed)
                _angle = float.NaN;
        }
    }
}
