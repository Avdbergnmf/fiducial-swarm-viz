// Tiny top-down of one craft: body, heading, and asset direction arrow. Click selects that slot.
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
        bool _hasAsset;
        Vector3 _craftPos;
        Vector3 _assetPos;
        float _distanceToAsset;

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
            _hasAsset = false;
            UpdateTooltip();
            MarkDirtyRepaint();
        }

        public void Set(Vector3 velocity, Color color, bool alive, bool hasAsset, Vector3 craftPos, Vector3 assetPos)
        {
            _vel = velocity;
            _color = color;
            _alive = alive;
            _hasAsset = hasAsset;
            _craftPos = craftPos;
            _assetPos = assetPos;
            if (hasAsset)
                _distanceToAsset = Vector3.Distance(craftPos, assetPos);
            UpdateTooltip();
            MarkDirtyRepaint();
        }

        void UpdateTooltip()
        {
            if (_hasAsset)
                tooltip = $"Top-down view · Distance to asset: {_distanceToAsset:F1} m. Click to select craft.";
            else
                tooltip = "Top-down of this craft. Click to select it in the scene.";
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
            float rad = Mathf.Min(r.width, r.height) * 0.40f;

            DrawCircle(painter, c, rad, new Color(1f, 1f, 1f, 0.04f), fill: true);
            DrawCircle(painter, c, rad, new Color(1f, 1f, 1f, 0.12f), fill: false);

            // World +Z (north) toward the top of the box.
            painter.strokeColor = new Color(1f, 1f, 1f, 0.22f);
            painter.lineWidth = 1f;
            painter.BeginPath();
            painter.MoveTo(c);
            painter.LineTo(c + Vector2.up * (-rad * 0.92f));
            painter.Stroke();

            // Arrow pointing toward Defended Asset
            if (_hasAsset)
            {
                Vector3 toAsset3D = _assetPos - _craftPos;
                Vector2 toAsset2D = new Vector2(toAsset3D.x, -toAsset3D.z);
                float dist = toAsset2D.magnitude;
                Vector2 assetDir = dist > 0.01f ? toAsset2D / dist : Vector2.up;
                Vector2 assetPerp = new Vector2(-assetDir.y, assetDir.x);

                // Scale arrow: larger when closer (range e.g. 10m..150m)
                float tCloseness = 1f - Mathf.Clamp01((dist - 10f) / 140f);
                float arrowSize = Mathf.Lerp(6f, 12f, tCloseness);

                Vector2 arrowTip = c + assetDir * (rad * 0.88f);
                Vector2 arrowBase = arrowTip - assetDir * arrowSize;
                Vector2 arrowL = arrowBase + assetPerp * (arrowSize * 0.45f);
                Vector2 arrowR = arrowBase - assetPerp * (arrowSize * 0.45f);

                Color assetColor = new Color(0.85f, 0.82f, 0.77f, Mathf.Lerp(0.55f, 0.95f, tCloseness));
                painter.fillColor = assetColor;
                painter.BeginPath();
                painter.MoveTo(arrowTip);
                painter.LineTo(arrowL);
                painter.LineTo(arrowR);
                painter.ClosePath();
                painter.Fill();
            }

            // Heading & craft triangle
            Vector2 hx = new Vector2(_vel.x, -_vel.z);
            float speed = hx.magnitude;
            Vector2 dir = speed > 0.05f ? hx / speed : Vector2.up * -1f;
            Vector2 perp = new Vector2(-dir.y, dir.x);

            Color body = _alive ? _color : Palette.A(_color, 0.45f);
            float bodyLen = rad * 0.65f;
            float bodyW = rad * 0.32f;
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

