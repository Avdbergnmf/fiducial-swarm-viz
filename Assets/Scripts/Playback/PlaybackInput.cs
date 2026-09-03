// Handles keyboard shortcuts for playback control using the New Input System (UnityEngine.InputSystem).
// Guards against firing shortcuts when UI text fields have focus.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace SwarmViewer
{
    public sealed class PlaybackInput : MonoBehaviour, IRunView
    {
        [SerializeField] UIDocument uiDocument;

        ViewerContext _ctx;
        Button _selBackBtn;
        Button _selFwdBtn;
        bool _wired;
        readonly List<int> _selectAllBuf = new();

        public void Bind(ViewerContext ctx)
        {
            Unhook();
            _ctx = ctx;
            Hook();
            TryWire();
            GetComponent<LegendView>()?.Bind(_ctx);
            GetComponent<DisagreeView>()?.Bind(_ctx);
            GetComponent<ScoringView>()?.Bind(_ctx);
            GetComponent<TimelineView>()?.Bind(_ctx);
            RefreshSelButtons();
        }

        void OnEnable() => TryWire();
        void Start() => TryWire();
        void OnDestroy() => Unhook();

        void Hook()
        {
            if (_ctx?.Selection == null) return;
            _ctx.Selection.OnSelectionSetChanged += RefreshSelButtons;
        }

        void Unhook()
        {
            if (_ctx?.Selection == null) return;
            _ctx.Selection.OnSelectionSetChanged -= RefreshSelButtons;
        }

        void TryWire()
        {
            if (uiDocument == null)
                uiDocument = GetComponent<UIDocument>();
            if (uiDocument == null) return;
            var root = uiDocument.rootVisualElement;
            if (root == null) return;

            if (GetComponent<LegendView>() == null)
                gameObject.AddComponent<LegendView>();
            if (GetComponent<DisagreeView>() == null)
                gameObject.AddComponent<DisagreeView>();
            if (GetComponent<ScoringView>() == null)
                gameObject.AddComponent<ScoringView>();

            if (!_wired)
            {
                _selBackBtn = UiQuery.Named<Button>(root, "selBackBtn");
                if (_selBackBtn != null)
                {
                    _selBackBtn.tooltip = "Previous selection   mouse back, [, or Ctrl+Z";
                    _selBackBtn.clicked += () => _ctx?.Selection?.Back();
                }
                _selFwdBtn = UiQuery.Named<Button>(root, "selFwdBtn");
                if (_selFwdBtn != null)
                {
                    _selFwdBtn.tooltip = "Next selection   mouse forward, ], or Ctrl+Y";
                    _selFwdBtn.clicked += () => _ctx?.Selection?.Forward();
                }
                _wired = true;
            }

            RefreshSelButtons();
        }

        void ToggleBeliefMode()
        {
            if (_ctx?.Selection == null || _ctx.Run == null) return;
            if (_ctx.Selection.BeliefViewOn)
            {
                _ctx.Selection.SetBeliefView(false);
                return;
            }

            int drone = -1;
            int primary = _ctx.Selection.Primary;
            if (primary >= 0)
                drone = _ctx.Run.Info(primary).drone_id;
            _ctx.Selection.SetBeliefView(true, drone);
        }

        void RefreshSelButtons()
        {
            bool back = _ctx?.Selection != null && _ctx.Selection.CanBack;
            bool fwd = _ctx?.Selection != null && _ctx.Selection.CanForward;
            if (_selBackBtn != null) _selBackBtn.SetEnabled(back);
            if (_selFwdBtn != null) _selFwdBtn.SetEnabled(fwd);
        }

        void Update()
        {
            if (_ctx == null) return;

            // Thumb buttons work like the browser: even with a filter field focused.
            var mouse = Mouse.current;
            if (mouse != null)
            {
                if (mouse.backButton.wasPressedThisFrame)
                    _ctx.Selection.Back();
                if (mouse.forwardButton.wasPressedThisFrame)
                    _ctx.Selection.Forward();
            }

            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (IsAnyTextFieldFocused()) return;

            var clock = _ctx.Clock;
            bool shift = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;

            if (keyboard.spaceKey.wasPressedThisFrame || keyboard.kKey.wasPressedThisFrame)
                clock.TogglePlay();

            if (keyboard.jKey.wasPressedThisFrame)
                clock.StepBack();
            if (keyboard.lKey.wasPressedThisFrame)
                clock.StepForward();

            if (keyboard.commaKey.wasPressedThisFrame)
            {
                if (shift) clock.CycleSpeedPrev();
                else clock.StepFrames(-1);
            }
            if (keyboard.periodKey.wasPressedThisFrame)
            {
                if (shift) clock.CycleSpeedNext();
                else clock.StepFrames(1);
            }

            if (keyboard.homeKey.wasPressedThisFrame || keyboard.digit0Key.wasPressedThisFrame || keyboard.numpad0Key.wasPressedThisFrame)
                clock.Seek(0f);

            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                if (shift) FloatingPanel.ToggleAll();
                else _ctx.Selection.Clear();
            }

            bool ctrl = keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed
                        || keyboard.leftCommandKey.isPressed || keyboard.rightCommandKey.isPressed;
            if (keyboard.leftBracketKey.wasPressedThisFrame ||
                (ctrl && keyboard.zKey.wasPressedThisFrame && !shift))
                _ctx.Selection.Back();
            if (keyboard.rightBracketKey.wasPressedThisFrame ||
                (ctrl && keyboard.yKey.wasPressedThisFrame) ||
                (ctrl && shift && keyboard.zKey.wasPressedThisFrame))
                _ctx.Selection.Forward();

            if (keyboard.bKey.wasPressedThisFrame)
                ToggleBeliefMode();

            if (keyboard.cKey.wasPressedThisFrame)
            {
                if (shift) GetComponent<SceneStateView>()?.ToggleCueMute();
                else GetComponent<SceneStateView>()?.ToggleCues();
            }

            if (ctrl && keyboard.aKey.wasPressedThisFrame && !shift)
                SelectAllAlive();

            if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
                GetComponent<EntityInspectorView>()?.OpenSelected();

            if (keyboard.slashKey.wasPressedThisFrame || keyboard.hKey.wasPressedThisFrame)
                GetComponent<LegendView>()?.ToggleCollapsed();
        }

        void SelectAllAlive()
        {
            if (_ctx?.Selection == null || _ctx.State == null) return;
            var ents = _ctx.State.Entities;
            int keep = _ctx.Selection.Primary;
            _selectAllBuf.Clear();
            for (int i = 0; i < ents.Count; i++)
            {
                if (ents[i].Alive)
                    _selectAllBuf.Add(i);
            }
            // Primary is slots[0]. Walking the roster would steal it
            // (inspector, belief, hops) even when that drone is in the set.
            if (keep >= 0)
            {
                int at = _selectAllBuf.IndexOf(keep);
                if (at > 0)
                {
                    _selectAllBuf.RemoveAt(at);
                    _selectAllBuf.Insert(0, keep);
                }
            }
            _ctx.Selection.Replace(_selectAllBuf);
        }

        bool IsAnyTextFieldFocused()
        {
            if (uiDocument != null && uiDocument.rootVisualElement != null)
            {
                var focused = uiDocument.rootVisualElement.focusController?.focusedElement;
                if (focused is TextField || focused is TextInputBaseField<string>)
                    return true;
            }
            return false;
        }
    }
}
