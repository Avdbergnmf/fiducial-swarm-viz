// Circular heading dial for the main camera: one ring, one north needle.
// Collapse is the +/– in the corner; the dial itself does not eat clicks.

using UnityEngine;
using UnityEngine.UIElements;

namespace SwarmViewer
{
    public sealed class CameraCompass : MonoBehaviour
    {
        [SerializeField] UIDocument uiDocument;

        Camera _camera;
        VisualElement _root;
        VisualElement _dial;
        Button _toggle;
        Vector2 _north = new(0f, -1f);
        bool _collapsed;
        bool _wired;

        void OnEnable() => TryWire();
        void Start() => TryWire();

        void OnDisable()
        {
            if (_dial != null)
            {
                _dial.generateVisualContent -= Paint;
                _dial.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            }
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
                _camera = GetComponent<Camera>() ?? Camera.main;
            if (_camera == null || _collapsed) return;

            Vector3 forward = Vector3.ProjectOnPlane(_camera.transform.forward, Vector3.up);
            if (forward.sqrMagnitude < 1e-5f) return;
            forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            Vector3 worldNorth = Vector3.forward;
            Vector2 next = new(
                Vector3.Dot(worldNorth, right),
                -Vector3.Dot(worldNorth, forward));
            if (next.sqrMagnitude < 1e-5f) return;
            next.Normalize();

            if ((_north - next).sqrMagnitude < 1e-6f) return;
            _north = next;
            _dial.MarkDirtyRepaint();
        }

        void TryWire()
        {
            if (_wired) return;
            if (uiDocument == null)
                uiDocument = FindAnyObjectByType<UIDocument>();
            if (uiDocument == null) return;

            var documentRoot = uiDocument.rootVisualElement;
            if (documentRoot == null) return;

            _root = UiQuery.Named<VisualElement>(documentRoot, "cameraCompassRoot");
            _dial = UiQuery.Named<VisualElement>(documentRoot, "cameraCompassDial");
            _toggle = UiQuery.Named<Button>(documentRoot, "cameraCompassToggle");
            if (_root == null || _dial == null || _toggle == null)
                return;

            _camera = GetComponent<Camera>() ?? Camera.main;
            _dial.generateVisualContent += Paint;
            _dial.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            _toggle.clicked += Toggle;
            UiQuery.HitSelf(_toggle);
            _collapsed = ViewerSettings.Load().compassCollapsed;
            _wired = true;
            ApplyCollapsed();
            _dial.MarkDirtyRepaint();
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
                _dial?.MarkDirtyRepaint();
        }

        void OnGeometryChanged(GeometryChangedEvent _) => _dial?.MarkDirtyRepaint();

        void Paint(MeshGenerationContext context)
        {
            if (_collapsed || _dial == null) return;
            Rect rect = _dial.contentRect;
            if (rect.width < 8f || rect.height < 8f) return;

            Vector2 center = rect.center;
            float radius = Mathf.Min(rect.width, rect.height) * 0.5f - 2f;
            Vector2 tip = center + _north * radius;
            Vector2 perp = new(-_north.y, _north.x);
            Vector2 left = center - _north * (radius * 0.28f) + perp * (radius * 0.22f);
            Vector2 right = center - _north * (radius * 0.28f) - perp * (radius * 0.22f);
            var painter = context.painter2D;

            painter.strokeColor = new Color(1f, 1f, 1f, 0.22f);
            painter.lineWidth = 1.25f;
            DrawCircle(painter, center, radius);

            painter.fillColor = new Color(0.95f, 0.82f, 0.38f, 0.95f);
            painter.BeginPath();
            painter.MoveTo(tip);
            painter.LineTo(left);
            painter.LineTo(right);
            painter.ClosePath();
            painter.Fill();
        }

        static void DrawCircle(Painter2D painter, Vector2 center, float radius)
        {
            const int segments = 48;
            painter.BeginPath();
            for (int i = 0; i <= segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                Vector2 point = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                if (i == 0) painter.MoveTo(point);
                else painter.LineTo(point);
            }
            painter.ClosePath();
            painter.Stroke();
        }
    }
}
