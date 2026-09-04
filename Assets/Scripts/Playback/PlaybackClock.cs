// The sole owner of current playback time in the viewer.
// Dispatches OnTimeChanged, OnPlayStateChanged, OnSpeedChanged, and OnStepChanged.

using System;
using UnityEngine;

namespace SwarmViewer
{
    /// <summary>
    /// The only thing in the viewer that owns "when". Views never advance time and
    /// never cache their own copy of it -- they react to OnTimeChanged. Keeping
    /// this in one place is why scrubbing, stepping and playback all behave the
    /// same everywhere.
    /// </summary>
    public sealed class PlaybackClock
    {
        readonly RunData _run;
        float _time;
        float _speed = 1f;
        bool _playing;
        float _stepSeconds = DefaultStepSeconds;

        public static readonly float[] SpeedSteps = { 0.25f, 0.5f, 1f, 2f, 4f };
        public static readonly float[] StepSteps = { 0.5f, 1f, 2.5f, 5f, 10f };
        public const float DefaultStepSeconds = 5f;

        public static string FormatSpeed(float speed) =>
            Mathf.Abs(speed - Mathf.Round(speed)) < 0.01f ? $"{speed:0}x" : $"{speed:g}x";

        public static string FormatStep(float seconds) =>
            Mathf.Abs(seconds - Mathf.Round(seconds)) < 0.01f
                ? $"{seconds:0}s"
                : $"{seconds:0.#}s";

        public event Action<float> OnTimeChanged;
        public event Action<bool> OnPlayStateChanged;
        public event Action<float> OnSpeedChanged;
        public event Action<float> OnStepChanged;
        /// <summary>Seek / step from the user. Not fired by Tick while playing.</summary>
        public event Action OnUserTimeJumped;

        public PlaybackClock(RunData run)
        {
            _run = run;
            float saved = ViewerSettings.Load().playbackStepSeconds;
            if (saved > 0.01f)
                _stepSeconds = NearestStep(saved);
        }

        public float Time => _time;
        public float Duration => _run.Duration;
        public float Normalised => _run.Duration > 0f ? _time / _run.Duration : 0f;
        public int FrameIndex => Mathf.RoundToInt(_time * _run.TraceHz);

        public bool IsPlaying
        {
            get => _playing;
            private set
            {
                if (_playing == value) return;
                _playing = value;
                OnPlayStateChanged?.Invoke(_playing);
            }
        }

        public float Speed
        {
            get => _speed;
            set
            {
                float clamped = Mathf.Clamp(value, 0.1f, 10f);
                if (Mathf.Approximately(_speed, clamped)) return;
                _speed = clamped;
                OnSpeedChanged?.Invoke(_speed);
            }
        }

        public void Play()
        {
            if (_time >= _run.Duration) _time = 0f;
            IsPlaying = true;
        }

        public void Pause() => IsPlaying = false;
        public void TogglePlay()
        {
            if (IsPlaying) Pause();
            else Play();
        }

        public void Seek(float t) => Seek(t, announceJump: true);

        void Seek(float t, bool announceJump)
        {
            float c = Mathf.Clamp(t, 0f, _run.Duration);
            if (!Mathf.Approximately(c, _time))
            {
                _time = c;
                OnTimeChanged?.Invoke(_time);
            }
            if (announceJump)
                OnUserTimeJumped?.Invoke();
        }

        public void SeekNormalised(float u) => Seek(u * _run.Duration);
        public void StepFrames(int n) { Pause(); Seek(_time + n / _run.TraceHz, announceJump: false); }
        public void StepSeconds(float s) => Seek(_time + s, announceJump: false);

        public float StepSecondsAmount => _stepSeconds;

        public void SetStepSeconds(float seconds)
        {
            float next = NearestStep(seconds);
            if (Mathf.Approximately(_stepSeconds, next)) return;
            _stepSeconds = next;
            var settings = ViewerSettings.Load();
            settings.playbackStepSeconds = next;
            settings.Save();
            OnStepChanged?.Invoke(_stepSeconds);
        }

        public void StepBack() => StepSeconds(-_stepSeconds);
        public void StepForward() => StepSeconds(_stepSeconds);

        static float NearestStep(float seconds)
        {
            float best = StepSteps[0];
            float err = Mathf.Abs(seconds - best);
            for (int i = 1; i < StepSteps.Length; i++)
            {
                float d = Mathf.Abs(seconds - StepSteps[i]);
                if (d < err)
                {
                    err = d;
                    best = StepSteps[i];
                }
            }
            return best;
        }

        public void CycleSpeedNext()
        {
            for (int i = 0; i < SpeedSteps.Length; i++)
            {
                if (SpeedSteps[i] > _speed + 0.01f)
                {
                    Speed = SpeedSteps[i];
                    return;
                }
            }
            Speed = SpeedSteps[0];
        }

        public void CycleSpeedPrev()
        {
            for (int i = SpeedSteps.Length - 1; i >= 0; i--)
            {
                if (SpeedSteps[i] < _speed - 0.01f)
                {
                    Speed = SpeedSteps[i];
                    return;
                }
            }
            Speed = SpeedSteps[SpeedSteps.Length - 1];
        }

        /// <summary>Land before an event, not on it: you want the approach.</summary>
        public void SeekBefore(float t, float leadIn = 2f) { Pause(); Seek(t - leadIn); }

        public void SeekToEvent(EventInfo e, float leadIn = 2f) => SeekBefore(e.t, leadIn);

        /// <summary>
        /// Last recorded frame still before t. Death and collision events are
        /// stamped on the first frame the craft is gone; landing on that frame
        /// makes them unselectable. One trace step back keeps them pickable.
        /// </summary>
        public void SeekJustBefore(float t)
        {
            Pause();
            float step = _run.TraceHz > 0.5f ? 1f / _run.TraceHz : 0.1f;
            Seek(t - step);
        }

        /// <summary>Pumped once per frame by VisualizerRoot.</summary>
        public void Tick(float deltaTime)
        {
            if (!_playing) return;
            float next = _time + deltaTime * _speed;
            if (next >= _run.Duration)
            {
                next = _run.Duration;
                IsPlaying = false;
            }
            _time = next;
            OnTimeChanged?.Invoke(_time);
        }
    }
}
