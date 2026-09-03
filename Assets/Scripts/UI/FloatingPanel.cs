// Title-bar drag and edge/corner resize for a position:absolute overlay panel.
// Geometry is remembered per panel under its UXML name, so every window that
// uses this class keeps its size and place across runs without opting in.
//
// left/top are panel pixels. UI scale shrinks that coordinate space (smaller
// reference resolution), so a window that sat on the right walks off the
// viewport unless we remap with the parent and pull it back on screen.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace SwarmViewer
{
    [Flags]
    enum ResizeEdge
    {
        None = 0,
        N = 1,
        S = 2,
        E = 4,
        W = 8,
    }

    public sealed class FloatingPanel
    {
        public const float MinWidth = 280f;
        public const float MinHeight = 220f;
        const float KeepOnScreen = 48f;

        static readonly List<FloatingPanel> Live = new();
        static readonly List<FloatingPanel> Stash = new();
        static bool StashValid;
        static bool Restoring;

        public event Action Hidden;
        public event Action Shown;

        VisualElement _panel;
        VisualElement _dragHandle;
        bool _attached;
        bool _shown;

        string _key;
        bool _restorePending;
        Rect _rect;
        bool _hasRect;
        float _layoutW;
        float _layoutH;

        bool _dragging;
        bool _resizing;
        ResizeEdge _resizeEdge;
        VisualElement _pointerOwner;
        Vector2 _pointerStart;
        float _startLeft, _startTop, _startWidth, _startHeight;

        public bool IsShown => _shown;

        public void Attach(VisualElement panel, VisualElement dragHandle, Button closeBtn)
        {
            if (panel == null || _attached) return;
            _panel = panel;
            _dragHandle = dragHandle;
            _attached = true;
            _key = panel.name;
            _restorePending = !string.IsNullOrEmpty(_key);

            _panel.RegisterCallback<PointerDownEvent>(OnPanelPointerDown, TrickleDown.TrickleDown);

            if (_dragHandle != null)
            {
                PanelCursors.Apply(_dragHandle, ResizeEdge.None);
                _dragHandle.RegisterCallback<PointerDownEvent>(OnDragPointerDown);
                _dragHandle.RegisterCallback<PointerMoveEvent>(OnPointerMove);
                _dragHandle.RegisterCallback<PointerUpEvent>(OnPointerUp);
                _dragHandle.RegisterCallback<PointerCaptureOutEvent>(OnCaptureOut);
                _dragHandle.RegisterCallback<PointerEnterEvent>(OnDragEnter);
                _dragHandle.RegisterCallback<PointerLeaveEvent>(OnHandleLeave);
            }

            if (closeBtn != null)
                closeBtn.clicked += Hide;

            AddGrips();
            Live.Add(this);
            var parent = _panel.parent;
            if (parent != null)
                parent.RegisterCallback<GeometryChangedEvent>(OnParentGeometry);
            RememberParentSize();
        }

        /// <summary>
        /// Shift every window with the panel coordinate space. Call after UI
        /// scale changes; GeometryChanged on the parent also drives this.
        /// </summary>
        public static void ReflowAll()
        {
            for (int i = 0; i < Live.Count; i++)
                Live[i]?.Reflow();
        }

        /// <summary>
        /// Shift+Esc: hide every open window, or restore that same set if
        /// nothing has been opened since the last hide.
        /// </summary>
        public static void ToggleAll()
        {
            if (AnyShown())
                HideAll();
            else
                RestoreStash();
        }

        static bool AnyShown()
        {
            for (int i = 0; i < Live.Count; i++)
                if (Live[i] != null && Live[i]._shown) return true;
            return false;
        }

        /// <summary>Hide every floating window. Shift+Esc.</summary>
        public static void HideAll()
        {
            Stash.Clear();
            for (int i = 0; i < Live.Count; i++)
            {
                var p = Live[i];
                if (p != null && p._shown)
                    Stash.Add(p);
            }
            StashValid = Stash.Count > 0;
            for (int i = Stash.Count - 1; i >= 0; i--)
                Stash[i].Hide();
        }

        static void RestoreStash()
        {
            if (!StashValid || Stash.Count == 0) return;
            Restoring = true;
            try
            {
                for (int i = 0; i < Stash.Count; i++)
                {
                    var p = Stash[i];
                    if (p == null || p._panel == null) continue;
                    p.Show();
                }
            }
            finally
            {
                Restoring = false;
                Stash.Clear();
                StashValid = false;
            }
        }

        public void Show()
        {
            if (_panel == null) return;
            bool became = !_shown;
            if (became && !Restoring)
                StashValid = false;

            _shown = true;
            _panel.style.display = DisplayStyle.Flex;
            _panel.BringToFront();

            // Restored on first show rather than in Attach: ApplyRect clamps against
            // the parent, and during wiring the parent has no resolved size yet.
            if (_restorePending)
            {
                _restorePending = false;
                if (ViewerSettings.Load().TryGetPanelRect(_key, out var saved))
                    ApplyRect(saved.x, saved.y, saved.width, saved.height, fullyOnScreen: true);
                else
                    Reflow();
            }
            else
                Reflow();

            if (became)
                Shown?.Invoke();
        }

        public void Hide()
        {
            if (_panel == null) return;
            if (!_shown)
            {
                _panel.style.display = DisplayStyle.None;
                return;
            }

            _shown = false;
            _panel.style.display = DisplayStyle.None;
            PanelCursors.ClearHardware();
            Hidden?.Invoke();
        }

        /// <summary>
        /// Drop this window from the live list. Call before destroying the panel
        /// so parent GeometryChanged and ReflowAll do not keep a dead handle.
        /// </summary>
        public void Detach()
        {
            Stash.Remove(this);
            if (Stash.Count == 0) StashValid = false;
            Hide();
            if (_panel != null)
            {
                var parent = _panel.parent;
                if (parent != null)
                    parent.UnregisterCallback<GeometryChangedEvent>(OnParentGeometry);
            }
            Live.Remove(this);
            Hidden = null;
            Shown = null;
            _panel = null;
            _dragHandle = null;
            _attached = false;
            _shown = false;
        }

        void AddGrips()
        {
            if (_panel.Q("floatGripE") != null) return;

            AddGrip("floatGripN", "float-grip--n", ResizeEdge.N);
            AddGrip("floatGripS", "float-grip--s", ResizeEdge.S);
            AddGrip("floatGripE", "float-grip--e", ResizeEdge.E);
            AddGrip("floatGripW", "float-grip--w", ResizeEdge.W);
            AddGrip("floatGripNE", "float-grip--ne", ResizeEdge.N | ResizeEdge.E);
            AddGrip("floatGripNW", "float-grip--nw", ResizeEdge.N | ResizeEdge.W);
            AddGrip("floatGripSE", "float-grip--se", ResizeEdge.S | ResizeEdge.E);
            AddGrip("floatGripSW", "float-grip--sw", ResizeEdge.S | ResizeEdge.W);
        }

        void AddGrip(string name, string extraClass, ResizeEdge edge)
        {
            var grip = new VisualElement { name = name, pickingMode = PickingMode.Position };
            grip.AddToClassList("float-grip");
            grip.AddToClassList(extraClass);
            grip.userData = (int)edge;
            PanelCursors.Apply(grip, edge);
            grip.RegisterCallback<PointerDownEvent>(OnResizePointerDown);
            grip.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            grip.RegisterCallback<PointerUpEvent>(OnPointerUp);
            grip.RegisterCallback<PointerCaptureOutEvent>(OnCaptureOut);
            grip.RegisterCallback<PointerEnterEvent>(OnGripEnter);
            grip.RegisterCallback<PointerLeaveEvent>(OnHandleLeave);
            _panel.Add(grip);
        }

        void OnPanelPointerDown(PointerDownEvent evt)
        {
            if (evt.button == 0)
                _panel.BringToFront();
        }

        void OnDragPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0 || _panel == null) return;
            BeginPointer(evt, _dragHandle, dragging: true, ResizeEdge.None);
        }

        void OnResizePointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0 || _panel == null) return;
            var grip = (VisualElement)evt.currentTarget;
            var edge = grip.userData is int flags ? (ResizeEdge)flags : ResizeEdge.None;
            BeginPointer(evt, grip, dragging: false, edge);
        }

        void BeginPointer(PointerDownEvent evt, VisualElement owner, bool dragging, ResizeEdge edge)
        {
            _dragging = dragging;
            _resizing = !dragging;
            _resizeEdge = edge;
            _pointerOwner = owner;
            _pointerStart = new Vector2(evt.position.x, evt.position.y);
            _startLeft = Resolved(_panel.resolvedStyle.left, 14f);
            _startTop = Resolved(_panel.resolvedStyle.top, 52f);
            _startWidth = Mathf.Max(MinWidth, Resolved(_panel.resolvedStyle.width, MinWidth));
            _startHeight = Mathf.Max(MinHeight, Resolved(_panel.resolvedStyle.height, MinHeight));
            _panel.BringToFront();
            owner.CapturePointer(evt.pointerId);
            PanelCursors.SetHardware(edge);
            evt.StopPropagation();
        }

        void OnGripEnter(PointerEnterEvent evt)
        {
            if (evt.currentTarget is VisualElement grip && grip.userData is int flags)
                PanelCursors.SetHardware((ResizeEdge)flags);
        }

        void OnDragEnter(PointerEnterEvent evt)
        {
            PanelCursors.SetHardware(ResizeEdge.None);
        }

        void OnHandleLeave(PointerLeaveEvent evt)
        {
            if (!_dragging && !_resizing)
                PanelCursors.ClearHardware();
        }

        void OnPointerMove(PointerMoveEvent evt)
        {
            if ((!_dragging && !_resizing) || _panel == null) return;

            float dx = evt.position.x - _pointerStart.x;
            float dy = evt.position.y - _pointerStart.y;

            if (_dragging)
            {
                ApplyRect(_startLeft + dx, _startTop + dy, _startWidth, _startHeight);
            }
            else
            {
                float left = _startLeft;
                float top = _startTop;
                float width = _startWidth;
                float height = _startHeight;

                if ((_resizeEdge & ResizeEdge.E) != 0)
                    width = _startWidth + dx;
                if ((_resizeEdge & ResizeEdge.S) != 0)
                    height = _startHeight + dy;
                if ((_resizeEdge & ResizeEdge.W) != 0)
                {
                    width = _startWidth - dx;
                    if (width < MinWidth)
                    {
                        dx = _startWidth - MinWidth;
                        width = MinWidth;
                    }
                    left = _startLeft + dx;
                }
                if ((_resizeEdge & ResizeEdge.N) != 0)
                {
                    height = _startHeight - dy;
                    if (height < MinHeight)
                    {
                        dy = _startHeight - MinHeight;
                        height = MinHeight;
                    }
                    top = _startTop + dy;
                }

                ApplyRect(left, top, width, height);
            }

            PanelCursors.SetHardware(_resizeEdge);
            evt.StopPropagation();
        }

        void OnPointerUp(PointerUpEvent evt)
        {
            if (!_dragging && !_resizing) return;
            EndPointer(evt.pointerId);
            evt.StopPropagation();
        }

        void OnCaptureOut(PointerCaptureOutEvent evt)
        {
            if (_dragging || _resizing)
                EndPointer(evt.pointerId);
        }

        void EndPointer(int pointerId)
        {
            _dragging = false;
            _resizing = false;
            _resizeEdge = ResizeEdge.None;
            if (_pointerOwner != null && _pointerOwner.HasPointerCapture(pointerId))
                _pointerOwner.ReleasePointer(pointerId);
            _pointerOwner = null;
            PanelCursors.ClearHardware();

            if (_hasRect)
                ViewerSettings.Load().SetPanelRect(_key, _rect);
        }

        void OnParentGeometry(GeometryChangedEvent evt)
        {
            if (evt.newRect.width < 32f || evt.newRect.height < 32f) return;
            Reflow();
        }

        void RememberParentSize()
        {
            if (!TryParentSize(out float pw, out float ph)) return;
            _layoutW = pw;
            _layoutH = ph;
        }

        bool TryParentSize(out float pw, out float ph)
        {
            pw = 0f;
            ph = 0f;
            var parent = _panel != null ? _panel.parent : null;
            if (parent == null) return false;
            pw = parent.resolvedStyle.width;
            ph = parent.resolvedStyle.height;
            return pw >= 32f && ph >= 32f;
        }

        void Reflow()
        {
            if (_panel == null) return;
            if (!TryParentSize(out float nw, out float nh)) return;

            if (!_hasRect)
            {
                if (_shown) CaptureResolvedRect();
                _layoutW = nw;
                _layoutH = nh;
                if (!_hasRect) return;
            }

            float sx = _layoutW > 32f ? nw / _layoutW : 1f;
            float sy = _layoutH > 32f ? nh / _layoutH : 1f;
            if (Mathf.Abs(sx - 1f) < 0.002f && Mathf.Abs(sy - 1f) < 0.002f)
                return;

            ApplyRect(_rect.x * sx, _rect.y * sy, _rect.width, _rect.height, fullyOnScreen: true);
            if (_hasRect)
                ViewerSettings.Load().SetPanelRect(_key, _rect);
        }

        void CaptureResolvedRect()
        {
            float left = Resolved(_panel.resolvedStyle.left, 14f);
            float top = Resolved(_panel.resolvedStyle.top, 52f);
            float width = Mathf.Max(MinWidth, Resolved(_panel.resolvedStyle.width, MinWidth));
            float height = Mathf.Max(MinHeight, Resolved(_panel.resolvedStyle.height, MinHeight));
            _rect = new Rect(left, top, width, height);
            _hasRect = true;
        }

        void ApplyRect(float left, float top, float width, float height, bool fullyOnScreen = false)
        {
            width = Mathf.Max(MinWidth, width);
            height = Mathf.Max(MinHeight, height);

            var parent = _panel.parent;
            float pw = parent != null ? parent.resolvedStyle.width : 1920f;
            float ph = parent != null ? parent.resolvedStyle.height : 1080f;
            if (pw < 32f) pw = 1920f;
            if (ph < 32f) ph = 1080f;

            if (fullyOnScreen)
            {
                left = width <= pw ? Mathf.Clamp(left, 0f, pw - width) : 0f;
                top = height <= ph ? Mathf.Clamp(top, 0f, ph - height) : 0f;
            }
            else
            {
                left = Mathf.Clamp(left, KeepOnScreen - width, Mathf.Max(KeepOnScreen, pw - KeepOnScreen));
                top = Mathf.Clamp(top, 0f, Mathf.Max(0f, ph - KeepOnScreen));
            }

            _panel.style.left = left;
            _panel.style.top = top;
            _panel.style.width = width;
            _panel.style.height = height;

            // Kept here rather than read back from resolvedStyle, which lags a layout pass.
            _rect = new Rect(left, top, width, height);
            _hasRect = true;
            _layoutW = pw;
            _layoutH = ph;
        }

        static float Resolved(float value, float fallback) =>
            float.IsNaN(value) || value < 8f ? fallback : value;
    }
}
