// UI Toolkit timeline: transport, speed, pointer-captured track scrubbing.
//
// Pointer capture (explain this out loud):
//   PointerDown on the track CapturePointer so PointerMove/Up keep arriving even
//   when the cursor leaves the 28 px track. CaptureOut is the failsafe if Unity
//   cancels capture. We Pause on down and restore play on up so scrubbing is not
//   fighting Tick(). Overlay wrappers are picking-mode Ignore; this track is not,
//   so clicks here do not fall through to entity picking.

using UnityEngine;
using UnityEngine.UIElements;

namespace SwarmViewer
{
    public sealed class TimelineView : MonoBehaviour, IRunView
    {
        [SerializeField] UIDocument uiDocument;

        ViewerContext _ctx;
        VisualElement _root;
        bool _uiWired;

        Button _playPauseButton;
        Button _stepBackFiveButton;
        Button _stepForwardFiveButton;
        VisualElement _trackContainer;
        VisualElement _trackFill;
        VisualElement _trackPlayhead;
        Label _trackTooltip;
        Label _timeLabel;
        Button _speedButton;

        bool _isScrubbing;
        bool _wasPlayingBeforeScrub;

        void OnEnable()
        {
            TryWireUi();
        }

        void Start()
        {
            TryWireUi();
        }

        public void Bind(ViewerContext ctx)
        {
            UnhookClock();
            _ctx = ctx;
            TryWireUi();
            HookClock();
            RefreshNow();
        }

        void TryWireUi()
        {
            if (uiDocument == null)
                uiDocument = GetComponent<UIDocument>(); // if not assigned, get it from the component

            if (uiDocument == null)
            {
                Debug.LogWarning("[viewer] TimelineView: no UIDocument.");
                return;
            }

            var root = uiDocument.rootVisualElement;
            if (root == null)
            {
                Debug.LogWarning("[viewer] TimelineView: rootVisualElement is null — UIDocument not ready yet. Will retry from OnEnable.");
                return;
            }

            if (_uiWired && _root == root) return;

            // Get all the elements by name
            _root = root;
            _playPauseButton = UiQuery.Named<Button>(_root, "playPauseButton");
            _stepBackFiveButton = UiQuery.Named<Button>(_root, "stepBackFiveButton");
            _stepForwardFiveButton = UiQuery.Named<Button>(_root, "stepForwardFiveButton");
            _trackContainer = UiQuery.Named<VisualElement>(_root, "trackContainer");
            _trackFill = UiQuery.Named<VisualElement>(_root, "trackFill");
            _trackPlayhead = UiQuery.Named<VisualElement>(_root, "trackPlayhead");
            _trackTooltip = UiQuery.Named<Label>(_root, "trackTooltip");
            _timeLabel = UiQuery.Named<Label>(_root, "timeLabel");
            _speedButton = UiQuery.Named<Button>(_root, "speedButton");

            RegisterCallbacks();
            _uiWired = true;
        }

        void RegisterCallbacks()
        {
            if (_playPauseButton != null)
            {
                _playPauseButton.clicked += () =>
                {
                    if (_ctx == null) return;
                    var clock = _ctx.Clock;
                    if (clock.Time >= clock.Duration - 0.05f)
                    {
                        clock.Seek(0f);
                        clock.Play();
                    }
                    else
                    {
                        clock.TogglePlay();
                    }
                };
            }

            if (_stepBackFiveButton != null)
                _stepBackFiveButton.clicked += () => _ctx?.Clock.StepSeconds(-5f);

            if (_stepForwardFiveButton != null)
                _stepForwardFiveButton.clicked += () => _ctx?.Clock.StepSeconds(5f);

            if (_speedButton != null)
                _speedButton.clicked += () => _ctx?.Clock.CycleSpeedNext();

            if (_trackContainer != null)
            {
                _trackContainer.RegisterCallback<PointerDownEvent>(OnTrackPointerDown);
                _trackContainer.RegisterCallback<PointerMoveEvent>(OnTrackPointerMove);
                _trackContainer.RegisterCallback<PointerUpEvent>(OnTrackPointerUp);
                _trackContainer.RegisterCallback<PointerCaptureOutEvent>(OnTrackCaptureOut);

                _trackContainer.RegisterCallback<PointerEnterEvent>(evt =>
                {
                    if (_trackTooltip != null) _trackTooltip.style.display = DisplayStyle.Flex;
                });
                _trackContainer.RegisterCallback<PointerLeaveEvent>(evt =>
                {
                    if (!_isScrubbing && _trackTooltip != null)
                        _trackTooltip.style.display = DisplayStyle.None;
                });
            }
        }

        void HookClock()
        {
            if (_ctx?.Clock == null) return;
            var clock = _ctx.Clock;
            clock.OnTimeChanged += UpdateTimeDisplay;
            clock.OnPlayStateChanged += UpdatePlayPauseButton;
            clock.OnSpeedChanged += UpdateSpeedDisplay;
        }

        void UnhookClock()
        {
            if (_ctx?.Clock == null) return;
            var clock = _ctx.Clock;
            clock.OnTimeChanged -= UpdateTimeDisplay;
            clock.OnPlayStateChanged -= UpdatePlayPauseButton;
            clock.OnSpeedChanged -= UpdateSpeedDisplay;
        }

        void RefreshNow()
        {
            if (_ctx?.Clock == null) return;
            var clock = _ctx.Clock;
            UpdateTimeDisplay(clock.Time);
            UpdatePlayPauseButton(clock.IsPlaying);
            UpdateSpeedDisplay(clock.Speed);
        }

        [ContextMenu("Dump UI Element Tree")]
        public void DumpElementTree()
        {
            if (uiDocument == null)
                uiDocument = GetComponent<UIDocument>();
            UiQuery.DumpTree(uiDocument != null ? uiDocument.rootVisualElement : null);
        }

        void OnTrackPointerDown(PointerDownEvent evt)
        {
            if (_ctx == null || _trackContainer == null) return;
            if (evt.button != 0) return;

            _isScrubbing = true;
            _wasPlayingBeforeScrub = _ctx.Clock.IsPlaying;
            _ctx.Clock.Pause();

            _trackContainer.CapturePointer(evt.pointerId);
            SeekFromPointer(evt.localPosition.x);
            evt.StopPropagation();
        }

        void OnTrackPointerMove(PointerMoveEvent evt)
        {
            if (_ctx == null || _trackContainer == null) return;

            float width = _trackContainer.resolvedStyle.width;
            if (width <= 1f) return;

            float u = Mathf.Clamp01(evt.localPosition.x / width);

            if (_trackTooltip != null)
            {
                _trackTooltip.text = $"{u * _ctx.Clock.Duration:F1} s";
                _trackTooltip.style.left = new Length(u * 100f, LengthUnit.Percent);
            }

            if (_isScrubbing)
            {
                SeekFromPointer(evt.localPosition.x);
                evt.StopPropagation();
            }
        }

        void OnTrackPointerUp(PointerUpEvent evt)
        {
            if (!_isScrubbing) return;
            EndScrub(evt.pointerId);
            evt.StopPropagation();
        }

        void OnTrackCaptureOut(PointerCaptureOutEvent evt)
        {
            if (!_isScrubbing) return;
            EndScrub(evt.pointerId);
        }

        void EndScrub(int pointerId)
        {
            _isScrubbing = false;
            if (_trackContainer != null && _trackContainer.HasPointerCapture(pointerId))
                _trackContainer.ReleasePointer(pointerId);

            if (_trackTooltip != null)
                _trackTooltip.style.display = DisplayStyle.None;

            if (_wasPlayingBeforeScrub && _ctx != null)
                _ctx.Clock.Play();
        }

        void SeekFromPointer(float localX)
        {
            if (_trackContainer == null || _ctx == null) return;
            float width = _trackContainer.resolvedStyle.width;
            if (width <= 1f) return;

            float u = Mathf.Clamp01(localX / width);
            _ctx.Clock.SeekNormalised(u);
        }

        void UpdateTimeDisplay(float t)
        {
            if (_ctx == null) return;
            float duration = _ctx.Clock.Duration;

            if (_timeLabel != null)
                _timeLabel.text = $"{t:F1} s / {duration:F1} s";

            float pct = duration > 0f ? (t / duration) * 100f : 0f;

            if (_trackFill != null)
                _trackFill.style.width = new Length(pct, LengthUnit.Percent);

            if (_trackPlayhead != null)
                _trackPlayhead.style.left = new Length(pct, LengthUnit.Percent);

            if (!_isScrubbing)
                UpdatePlayPauseButton(_ctx.Clock.IsPlaying);
        }

        void UpdatePlayPauseButton(bool isPlaying)
        {
            if (_playPauseButton == null || _ctx == null) return;

            if (_ctx.Clock.Time >= _ctx.Clock.Duration - 0.05f)
                _playPauseButton.text = "Replay";
            else
                _playPauseButton.text = isPlaying ? "Pause" : "Play";
        }

        void UpdateSpeedDisplay(float speed)
        {
            if (_speedButton != null)
                _speedButton.text = $"{speed}x";
        }

        void OnDestroy()
        {
            UnhookClock();
        }
    }
}
