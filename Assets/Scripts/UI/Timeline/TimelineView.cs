// UI Toolkit timeline: transport, speed, pointer-captured track scrubbing.
//
// Pointer capture (explain this out loud):
//   PointerDown on the track CapturePointer so PointerMove/Up keep arriving even
//   when the cursor leaves the 28 px track. CaptureOut is the failsafe if Unity
//   cancels capture. We Pause on down and restore play on up so scrubbing is not
//   fighting Tick(). Overlay wrappers are picking-mode Ignore; this track is not,
//   so clicks here do not fall through to entity picking.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace SwarmViewer
{
    public sealed class TimelineView : MonoBehaviour, IRunView
    {
        [SerializeField] UIDocument uiDocument;

        ViewerContext _ctx;
        ScoringView _scoring;
        VisualElement _root;
        VisualElement _bar;
        bool _uiWired;
        bool _ownControl;
        int _jumpGen;

        Button _playPauseButton;
        Button _stepBackFiveButton;
        Button _stepForwardFiveButton;
        VisualElement _stepHost;
        VisualElement _stepFlyout;
        VisualElement _speedHost;
        VisualElement _speedFlyout;
        VisualElement _trackContainer;
        VisualElement _trackFill;
        VisualElement _trackScoreMarks;
        VisualElement _trackPlayhead;
        Label _trackTooltip;
        Label _timeLabel;
        Button _speedButton;
        readonly List<Button> _speedChoices = new();
        readonly List<Button> _stepChoices = new();
        int _speedHideGen;
        int _stepHideGen;

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
            UnhookScoring();
            _ctx = ctx;
            TryWireUi();
            HookClock();
            HookScoring();
            RefreshNow();
            _trackScoreMarks?.MarkDirtyRepaint();
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
            _bar = UiQuery.Named<VisualElement>(_root, "timelineRoot");
            _playPauseButton = UiQuery.Named<Button>(_root, "playPauseButton");
            _stepHost = UiQuery.Named<VisualElement>(_root, "stepHost");
            _stepFlyout = UiQuery.Named<VisualElement>(_root, "stepFlyout");
            _stepBackFiveButton = UiQuery.Named<Button>(_root, "stepBackFiveButton");
            _stepForwardFiveButton = UiQuery.Named<Button>(_root, "stepForwardFiveButton");
            _trackContainer = UiQuery.Named<VisualElement>(_root, "trackContainer");
            _trackFill = UiQuery.Named<VisualElement>(_root, "trackFill");
            _trackScoreMarks = UiQuery.Named<VisualElement>(_root, "trackScoreMarks");
            _trackPlayhead = UiQuery.Named<VisualElement>(_root, "trackPlayhead");
            _trackTooltip = UiQuery.Named<Label>(_root, "trackTooltip");
            _timeLabel = UiQuery.Named<Label>(_root, "timeLabel");
            _speedHost = UiQuery.Named<VisualElement>(_root, "speedHost");
            _speedFlyout = UiQuery.Named<VisualElement>(_root, "speedFlyout");
            _speedButton = UiQuery.Named<Button>(_root, "speedButton");

            BuildSpeedFlyout();
            BuildStepFlyout();
            RegisterCallbacks();
            _uiWired = true;
            if (_ctx?.Clock != null)
                RefreshNow();
        }

        void RegisterCallbacks()
        {
            if (_playPauseButton != null)
            {
                _playPauseButton.clicked += () =>
                {
                    if (_ctx == null) return;
                    var clock = _ctx.Clock;
                    Own(() =>
                    {
                        if (clock.Time >= clock.Duration - 0.05f)
                        {
                            clock.Seek(0f);
                            clock.Play();
                        }
                        else
                        {
                            clock.TogglePlay();
                        }
                    });
                };
            }

            if (_stepBackFiveButton != null)
                _stepBackFiveButton.clicked += () => Own(() =>
                {
                    _ctx?.Clock.Pause();
                    _ctx?.Clock.StepBack();
                });

            if (_stepForwardFiveButton != null)
                _stepForwardFiveButton.clicked += () => Own(() =>
                {
                    _ctx?.Clock.Pause();
                    _ctx?.Clock.StepForward();
                });

            ArmSpeedFlyout();
            ArmStepFlyout();

            if (_trackScoreMarks != null)
            {
                _trackScoreMarks.generateVisualContent += PaintScoreMarks;
                _trackScoreMarks.RegisterCallback<GeometryChangedEvent>(_ =>
                    _trackScoreMarks.MarkDirtyRepaint());
            }

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
            clock.OnStepChanged += UpdateStepDisplay;
            clock.OnUserTimeJumped += FlashExternalJump;
        }

        void UnhookClock()
        {
            if (_ctx?.Clock == null) return;
            var clock = _ctx.Clock;
            clock.OnTimeChanged -= UpdateTimeDisplay;
            clock.OnPlayStateChanged -= UpdatePlayPauseButton;
            clock.OnSpeedChanged -= UpdateSpeedDisplay;
            clock.OnStepChanged -= UpdateStepDisplay;
            clock.OnUserTimeJumped -= FlashExternalJump;
        }

        void HookScoring()
        {
            _scoring = GetComponent<ScoringView>();
            if (_scoring != null)
                _scoring.MarksChanged += OnScoreMarksChanged;
        }

        void UnhookScoring()
        {
            if (_scoring != null)
                _scoring.MarksChanged -= OnScoreMarksChanged;
            _scoring = null;
        }

        void OnScoreMarksChanged() => _trackScoreMarks?.MarkDirtyRepaint();

        void PaintScoreMarks(MeshGenerationContext ctx)
        {
            var ledger = _scoring != null ? _scoring.Marks : _ctx?.Run?.Meta?.scoring?.ledger;
            if (ledger == null || ledger.Count == 0) return;
            float duration = _ctx.Clock != null ? _ctx.Clock.Duration : 0f;
            if (duration < 0.01f) return;

            var rect = _trackScoreMarks.contentRect;
            if (rect.width < 2f) return;

            var painter = ctx.painter2D;
            painter.lineWidth = 2f;
            for (int i = 0; i < ledger.Count; i++)
            {
                var e = ledger[i];
                if (e == null) continue;
                if (_scoring != null && !_scoring.AllowsKind(e.kind)) continue;
                float x = Mathf.Clamp01(e.t / duration) * rect.width;
                painter.strokeColor = Palette.Opaque(Palette.ScoreKind(e.kind));
                painter.BeginPath();
                painter.MoveTo(new Vector2(x, 3f));
                painter.LineTo(new Vector2(x, 11f));
                painter.Stroke();
            }
        }

        void RefreshNow()
        {
            if (_ctx?.Clock == null) return;
            var clock = _ctx.Clock;
            UpdateTimeDisplay(clock.Time);
            UpdatePlayPauseButton(clock.IsPlaying);
            UpdateSpeedDisplay(clock.Speed);
            UpdateStepDisplay(clock.StepSecondsAmount);
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
            Own(() =>
            {
                _ctx.Clock.Pause();
                SeekFromPointer(evt.localPosition.x);
            });

            _trackContainer.CapturePointer(evt.pointerId);
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
                Own(() => SeekFromPointer(evt.localPosition.x));
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
                Own(() => _ctx.Clock.Play());
        }

        void Own(System.Action action)
        {
            _ownControl = true;
            try { action(); }
            finally { _ownControl = false; }
        }

        void FlashExternalJump()
        {
            if (_ownControl || _isScrubbing || _bar == null) return;
            _bar.AddToClassList("timeline-root--jump");
            int gen = ++_jumpGen;
            _bar.schedule.Execute(() =>
            {
                if (gen == _jumpGen)
                    _bar?.RemoveFromClassList("timeline-root--jump");
            }).StartingIn(420);
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
                _speedButton.text = PlaybackClock.FormatSpeed(speed);
            MarkChoices(_speedChoices, speed, PlaybackClock.SpeedSteps);
        }

        void UpdateStepDisplay(float seconds)
        {
            string label = PlaybackClock.FormatStep(seconds);
            if (_stepBackFiveButton != null)
                _stepBackFiveButton.text = "-" + label;
            if (_stepForwardFiveButton != null)
                _stepForwardFiveButton.text = "+" + label;
            MarkChoices(_stepChoices, seconds, PlaybackClock.StepSteps);
        }

        void BuildSpeedFlyout()
        {
            FillFlyout(_speedFlyout, _speedChoices, PlaybackClock.SpeedSteps, PlaybackClock.FormatSpeed, v =>
            {
                if (_ctx?.Clock != null) _ctx.Clock.Speed = v;
            });
        }

        void BuildStepFlyout()
        {
            FillFlyout(_stepFlyout, _stepChoices, PlaybackClock.StepSteps, PlaybackClock.FormatStep, v =>
            {
                _ctx?.Clock?.SetStepSeconds(v);
            });
        }

        static void FillFlyout(VisualElement flyout, List<Button> store, float[] values,
            Func<float, string> label, Action<float> pick)
        {
            store.Clear();
            if (flyout == null) return;
            flyout.Clear();
            flyout.style.display = DisplayStyle.None;
            for (int i = 0; i < values.Length; i++)
            {
                float value = values[i];
                var btn = new Button(() => pick(value)) { text = label(value) };
                UiQuery.HitSelf(btn);
                btn.AddToClassList("timeline-button");
                btn.AddToClassList("timeline-flyout-btn");
                if (i == values.Length - 1)
                    btn.AddToClassList("timeline-flyout-btn--end");
                flyout.Add(btn);
                store.Add(btn);
            }
        }

        static void MarkChoices(List<Button> buttons, float current, float[] values)
        {
            for (int i = 0; i < buttons.Count && i < values.Length; i++)
                buttons[i].EnableInClassList("timeline-flyout-btn--on",
                    Mathf.Abs(values[i] - current) < 0.01f);
        }

        void ArmSpeedFlyout()
        {
            if (_speedHost == null || _speedFlyout == null) return;
            _speedHost.RegisterCallback<PointerEnterEvent>(_ =>
            {
                _speedHideGen++;
                _speedFlyout.style.display = DisplayStyle.Flex;
            }, TrickleDown.TrickleDown);
            _speedHost.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                int gen = ++_speedHideGen;
                _speedFlyout.schedule.Execute(() =>
                {
                    if (gen == _speedHideGen)
                        _speedFlyout.style.display = DisplayStyle.None;
                }).StartingIn(160);
            });
        }

        void ArmStepFlyout()
        {
            if (_stepHost == null || _stepFlyout == null) return;
            _stepHost.RegisterCallback<PointerEnterEvent>(_ =>
            {
                _stepHideGen++;
                _stepFlyout.style.display = DisplayStyle.Flex;
            }, TrickleDown.TrickleDown);
            _stepHost.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                int gen = ++_stepHideGen;
                _stepFlyout.schedule.Execute(() =>
                {
                    if (gen == _stepHideGen)
                        _stepFlyout.style.display = DisplayStyle.None;
                }).StartingIn(160);
            });
        }

        void OnDestroy()
        {
            UnhookClock();
            UnhookScoring();
        }
    }
}
