// Handles keyboard shortcuts for playback control using the New Input System (UnityEngine.InputSystem).
// Guards against firing shortcuts when UI text fields have focus.

using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace SwarmViewer
{
    public sealed class PlaybackInput : MonoBehaviour, IRunView
    {
        ViewerContext _ctx;
        UIDocument _uiDocument;

        public void Bind(ViewerContext ctx)
        {
            _ctx = ctx;
            if (_uiDocument == null)
                _uiDocument = FindFirstObjectByType<UIDocument>();
        }

        void Update()
        {
            if (_ctx == null) return;

            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            // Guard against keyboard inputs while typing in a UI Toolkit TextField or input element
            if (IsAnyTextFieldFocused()) return;

            var clock = _ctx.Clock;
            bool shift = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;

            // Space / K : Play / Pause
            if (keyboard.spaceKey.wasPressedThisFrame || keyboard.kKey.wasPressedThisFrame)
            {
                clock.TogglePlay();
            }

            // J / L : Step 5 seconds backward / forward
            if (keyboard.jKey.wasPressedThisFrame)
            {
                clock.StepSeconds(-5f);
            }
            if (keyboard.lKey.wasPressedThisFrame)
            {
                clock.StepSeconds(5f);
            }

            // Frame stepping / Speed cycling with ',' and '.'
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

            // Home / 0 / Numpad 0 : Seek to start
            if (keyboard.homeKey.wasPressedThisFrame || keyboard.digit0Key.wasPressedThisFrame || keyboard.numpad0Key.wasPressedThisFrame)
            {
                clock.Seek(0f);
            }

            // Esc : Clear selection
            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                _ctx.Selection.Clear();
            }
        }

        bool IsAnyTextFieldFocused()
        {
            if (_uiDocument != null && _uiDocument.rootVisualElement != null)
            {
                var focused = _uiDocument.rootVisualElement.focusController?.focusedElement;
                if (focused is TextField || focused is TextInputBaseField<string>)
                    return true;
            }
            return false;
        }
    }
}
