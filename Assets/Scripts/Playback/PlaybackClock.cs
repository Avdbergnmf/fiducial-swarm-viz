// The sole owner of current playback time in the viewer.
// Dispatches OnTimeChanged, OnPlayStateChanged, and OnSpeedChanged to drive state evaluation across the viewer.

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

        public static readonly float[] SpeedSteps = { 0.25f, 0.5f, 1f, 2f, 4f };

        public event Action<float> OnTimeChanged;
        public event Action<bool> OnPlayStateChanged;
        public event Action<float> OnSpeedChanged;

        public PlaybackClock(RunData run)
        {
            _run = run;
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

        public void Seek(float t)
        {
            float c = Mathf.Clamp(t, 0f, _run.Duration);
            if (Mathf.Approximately(c, _time)) return;
            _time = c;
            OnTimeChanged?.Invoke(_time);
        }

        public void SeekNormalised(float u) => Seek(u * _run.Duration);
        public void StepFrames(int n) { Pause(); Seek(_time + n / _run.TraceHz); }
        public void StepSeconds(float s) { Pause(); Seek(_time + s); }

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
        public void SeekToEvent(EventInfo e, float leadIn = 2f) { Pause(); Seek(e.t - leadIn); }

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
