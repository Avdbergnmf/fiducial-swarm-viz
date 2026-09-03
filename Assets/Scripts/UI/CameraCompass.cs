using UnityEngine;
using UnityEngine.UIElements;

namespace SwarmViewer
{
    public sealed class CameraCompass : VisualElement
    {
        const float Radius = 26f;
        bool _collapsed;
        Button _toggle;
        Label _northLabel;

        public CameraCompass()
        {
            name = "cameraCompass";
            pickingMode = PickingMode.Position;
            style.width = 76f;
            style.height = 76f;
            style.backgroundColor = new Color(0.06f, 0.08f, 0.11f, 0.86f);
            style.borderTopLeftRadius = 6f;
            style.borderTopRightRadius = 6f;
            style.borderBottomLeftRadius = 6f;
            style.borderBottomRightRadius = 6f;
            style.borderTopWidth = 1f;
            style.borderBottomWidth = 1f;
            style.borderLeftWidth = 1f;
            style.borderRightWidth = 1f;
            style.borderTopColor = new Color(1f, 1f, 1f, 0.14f);
            style.borderBottomColor = new Color(1f, 1f, 1f, 0.14f);
            style.borderLeftColor = new Color(1f, 1f, 1f, 0.14f);
            style.borderRightColor = new Color(1f, 1f, 1f, 0.14f);

            _toggle = new Button(Toggle) { text = "-" };
            _toggle.name = "cameraCompassToggle";
            _toggle.tooltip = "Hide compass";
            _toggle.pickingMode = PickingMode.Position;
            _toggle.style.position = Position.Absolute;
            _toggle.style.top = 3f;
            _toggle.style.right = 3f;
            _toggle.style.width = 16f;
            _toggle.style.height = 16f;
            _toggle.style.paddingLeft = 0f;
            _toggle.style.paddingRight = 0f;
            _toggle.style.paddingTop = 0f;
            _toggle.style.paddingBottom = 0f;
            _toggle.style.fontSize = 11f;
            Add(_toggle);

            _northLabel = new Label("N") { name = "cameraCompassNorth" };
            _northLabel.pickingMode = PickingMode.Ignore;
            _northLabel.style.position = Position.Absolute;
            _northLabel.style.fontSize = 11f;
            _northLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _northLabel.style.color = new Color(0.95f, 0.88f, 0.58f, 0.95f);
            Add(_northLabel);

            generateVisualContent += Paint;
            schedule.Execute(Refresh).Every(33);
            Refresh();
        }

        void Toggle()
        {
            _collapsed = !_collapsed;
            _toggle.text = _collapsed ? "+" : "-";
            _toggle.tooltip = _collapsed ? "Show compass" : "Hide compass";
            style.width = _collapsed ? 22f : 76f;
            style.height = _collapsed ? 22f : 76f;
            if (parent != null)
            {
                parent.style.width = _collapsed ? 22f : 76f;
                parent.style.height = _collapsed ? 22f : 76f;
            }
            MarkDirtyRepaint();
        }

        void Refresh()
        {
            MarkDirtyRepaint();
        }

        void Paint(MeshGenerationContext context)
        {
            if (_collapsed) return;
            var camera = Camera.main;
            if (camera == null) return;

            var rect = contentRect;
            Vector2 center = new Vector2(rect.width * 0.5f, rect.height * 0.56f);
            var painter = context.painter2D;
            DrawCircle(painter, center, Radius, new Color(1f, 1f, 1f, 0.04f), false);

            Vector3 northLocal = camera.transform.InverseTransformDirection(Vector3.forward);
            Vector2 direction = new Vector2(northLocal.x, -northLocal.y);
            if (direction.sqrMagnitude < 0.001f) return;
            direction.Normalize();
            Vector2 perpendicular = new Vector2(-direction.y, direction.x);
            Vector2 tip = center + direction * (Radius - 4f);
            Vector2 left = tip - direction * 8f + perpendicular * 4f;
            Vector2 right = tip - direction * 8f - perpendicular * 4f;

            painter.strokeColor = new Color(1f, 1f, 1f, 0.32f);
            painter.lineWidth = 1f;
            painter.BeginPath();
            painter.MoveTo(center);
            painter.LineTo(tip);
            painter.Stroke();

            painter.fillColor = new Color(0.9f, 0.82f, 0.48f, 0.95f);
            painter.BeginPath();
            painter.MoveTo(tip);
            painter.LineTo(left);
            painter.LineTo(right);
            painter.ClosePath();
            painter.Fill();

            _northLabel.style.left = center.x + direction.x * (Radius + 1f) - 4f;
            _northLabel.style.top = center.y - direction.y * (Radius + 1f) - 7f;
        }

        static void DrawCircle(Painter2D painter, Vector2 center, float radius, Color color, bool fill)
        {
            const int segments = 32;
            painter.BeginPath();
            for (int i = 0; i <= segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                Vector2 point = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                if (i == 0) painter.MoveTo(point);
                else painter.LineTo(point);
            }
            painter.ClosePath();
            if (fill)
            {
                painter.fillColor = color;
                painter.Fill();
            }
            else
            {
                painter.strokeColor = color;
                painter.lineWidth = 1f;
                painter.Stroke();
            }
        }
    }
}
