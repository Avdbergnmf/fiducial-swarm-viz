// Tiny top-down of one craft: body plus heading. Click selects that slot.
// UI Y grows down, so world +Z (north) is drawn toward the top of the box.

using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace SwarmViewer
{
    public sealed class HeadingPreview : VisualElement
    {
        public event Action Clicked;

        Vector3 _vel;
        Color _color = Palette.Unknown;
        bool _alive;

        public HeadingPreview()
        {
            pickingMode = PickingMode.Position;
            generateVisualContent += Paint;
            RegisterCallback<ClickEvent>(OnClick);
            RegisterCallback<GeometryChangedEvent>(_ => MarkDirtyRepaint());
            tooltip = "Top-down of this craft. Click to select it in the scene.";
        }

        public void Set(Vector3 velocity, Color color, bool alive)
        {
            _vel = velocity;
            _color = color;
            _alive = alive;
            MarkDirtyRepaint();
        }

        void OnClick(ClickEvent evt)
        {
            Clicked?.Invoke();
            evt.StopPropagation();
        }

        void Paint(MeshGenerationContext ctx)
        {
            var r = contentRect;
            if (r.width < 8f || r.height < 8f) return;

            var painter = ctx.painter2D;
            Vector2 c = r.center;
            float rad = Mathf.Min(r.width, r.height) * 0.38f;

            DrawCircle(painter, c, rad, new Color(1f, 1f, 1f, 0.04f), fill: true);
            DrawCircle(painter, c, rad, new Color(1f, 1f, 1f, 0.12f), fill: false);

            // World +Z (north) toward the top of the box.
            painter.strokeColor = new Color(1f, 1f, 1f, 0.28f);
            painter.BeginPath();
            painter.MoveTo(c);
            painter.LineTo(c + Vector2.up * (-rad * 0.92f));
            painter.Stroke();

            Vector2 hx = new Vector2(_vel.x, -_vel.z);
            float speed = hx.magnitude;
            Vector2 dir = speed > 0.05f ? hx / speed : Vector2.up * -1f;
            Vector2 perp = new Vector2(-dir.y, dir.x);

            Color body = _alive ? _color : Palette.A(_color, 0.45f);
            float bodyLen = rad * 0.72f;
            float bodyW = rad * 0.34f;
            Vector2 nose = c + dir * bodyLen;
            Vector2 left = c - dir * (bodyLen * 0.42f) + perp * bodyW;
            Vector2 right = c - dir * (bodyLen * 0.42f) - perp * bodyW;

            painter.fillColor = body;
            painter.BeginPath();
            painter.MoveTo(nose);
            painter.LineTo(left);
            painter.LineTo(right);
            painter.ClosePath();
            painter.Fill();

            if (speed > 0.05f)
            {
                painter.strokeColor = Palette.A(Palette.Opaque(body), _alive ? 0.95f : 0.5f);
                painter.lineWidth = 2f;
                painter.BeginPath();
                painter.MoveTo(c);
                painter.LineTo(c + dir * (rad * 0.95f));
                painter.Stroke();
            }
        }

        static void DrawCircle(Painter2D painter, Vector2 c, float rad, Color color, bool fill)
        {
            const int n = 32;
            painter.BeginPath();
            for (int i = 0; i <= n; i++)
            {
                float a = (i / (float)n) * Mathf.PI * 2f;
                var p = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rad;
                if (i == 0) painter.MoveTo(p);
                else painter.LineTo(p);
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
