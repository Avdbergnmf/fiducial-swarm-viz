// Runtime UI Toolkit cannot use USS cursor keywords (resize-horizontal, etc.).
// Those only exist in the Editor and log "need to be defined using a texture" in Play mode.
// These are small generated textures assigned on the grips and title bar.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UiCursor = UnityEngine.UIElements.Cursor;

namespace SwarmViewer
{
    static class PanelCursors
    {
        const int Size = 32;
        static readonly Vector2 Hotspot = new Vector2(15f, 15f);

        static Texture2D _ns, _ew, _nwse, _nesw, _move;
        static bool _ready;

        public static void Apply(VisualElement el, ResizeEdge edge)
        {
            if (el == null) return;
            Ensure();
            var cursor = new UiCursor { texture = TextureFor(edge), hotspot = Hotspot };
            el.style.cursor = new StyleCursor(cursor);
        }

        public static void SetHardware(ResizeEdge edge)
        {
            Ensure();
            UnityEngine.Cursor.SetCursor(TextureFor(edge), Hotspot, CursorMode.Auto);
        }

        public static void ClearHardware()
        {
            UnityEngine.Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        }

        static Texture2D TextureFor(ResizeEdge edge)
        {
            bool n = (edge & ResizeEdge.N) != 0;
            bool s = (edge & ResizeEdge.S) != 0;
            bool e = (edge & ResizeEdge.E) != 0;
            bool w = (edge & ResizeEdge.W) != 0;
            int bits = (n ? 1 : 0) + (s ? 1 : 0) + (e ? 1 : 0) + (w ? 1 : 0);
            if (bits == 0) return _move;
            if ((n || s) && !(e || w)) return _ns;
            if ((e || w) && !(n || s)) return _ew;
            if ((n && e) || (s && w)) return _nesw;
            return _nwse;
        }

        static void Ensure()
        {
            if (_ready) return;
            _ew = Make(DrawEW);
            _ns = Make(DrawNS);
            _nwse = Make(DrawNWSE);
            _nesw = Make(DrawNESW);
            _move = Make(DrawMove);
            _ready = true;
        }

        static Texture2D Make(System.Action<List<Vector2Int>> draw)
        {
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
                name = "PanelCursor",
            };

            var clear = new Color(0f, 0f, 0f, 0f);
            var pixels = new Color[Size * Size];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = clear;
            tex.SetPixels(pixels);

            var core = new List<Vector2Int>(128);
            draw(core);
            Blit(tex, core);
            tex.Apply();
            return tex;
        }

        static void Blit(Texture2D tex, List<Vector2Int> core)
        {
            var black = new Color(0f, 0f, 0f, 1f);
            var white = Color.white;
            for (int i = 0; i < core.Count; i++)
            {
                var p = core[i];
                for (int oy = -1; oy <= 1; oy++)
                    for (int ox = -1; ox <= 1; ox++)
                        Put(tex, p.x + ox, p.y + oy, black);
            }
            for (int i = 0; i < core.Count; i++)
                Put(tex, core[i].x, core[i].y, white);
        }

        // Draw in top-down coordinates; Unity textures are bottom-up.
        static void Put(Texture2D tex, int x, int y, Color c)
        {
            if ((uint)x >= Size || (uint)y >= Size) return;
            tex.SetPixel(x, Size - 1 - y, c);
        }

        static void Line(List<Vector2Int> dst, int x0, int y0, int x1, int y1)
        {
            int dx = Mathf.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Mathf.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;
            for (;;)
            {
                dst.Add(new Vector2Int(x0, y0));
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        static void DrawEW(List<Vector2Int> c)
        {
            Line(c, 7, 15, 24, 15);
            Line(c, 7, 16, 24, 16);
            Line(c, 8, 15, 14, 9);
            Line(c, 8, 16, 14, 22);
            Line(c, 9, 15, 14, 10);
            Line(c, 9, 16, 14, 21);
            Line(c, 23, 15, 17, 9);
            Line(c, 23, 16, 17, 22);
            Line(c, 22, 15, 17, 10);
            Line(c, 22, 16, 17, 21);
        }

        static void DrawNS(List<Vector2Int> c)
        {
            Line(c, 15, 7, 15, 24);
            Line(c, 16, 7, 16, 24);
            Line(c, 15, 8, 9, 14);
            Line(c, 16, 8, 22, 14);
            Line(c, 15, 9, 10, 14);
            Line(c, 16, 9, 21, 14);
            Line(c, 15, 23, 9, 17);
            Line(c, 16, 23, 22, 17);
            Line(c, 15, 22, 10, 17);
            Line(c, 16, 22, 21, 17);
        }

        static void DrawNWSE(List<Vector2Int> c)
        {
            Line(c, 8, 8, 23, 23);
            Line(c, 9, 8, 23, 22);
            Line(c, 8, 9, 22, 23);
            Line(c, 8, 8, 8, 14);
            Line(c, 8, 8, 14, 8);
            Line(c, 23, 23, 23, 17);
            Line(c, 23, 23, 17, 23);
        }

        static void DrawNESW(List<Vector2Int> c)
        {
            Line(c, 23, 8, 8, 23);
            Line(c, 22, 8, 8, 22);
            Line(c, 23, 9, 9, 23);
            Line(c, 23, 8, 23, 14);
            Line(c, 23, 8, 17, 8);
            Line(c, 8, 23, 8, 17);
            Line(c, 8, 23, 14, 23);
        }

        static void DrawMove(List<Vector2Int> c)
        {
            DrawNS(c);
            DrawEW(c);
        }
    }
}
