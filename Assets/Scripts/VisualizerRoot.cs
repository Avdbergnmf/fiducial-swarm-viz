// Root coordinator component that initializes data loading, manages the playback clock, and drives views.
// Supports dynamic run loading, clean view rebinding, and error reporting.

using System;
using UnityEngine;

namespace SwarmViewer
{
    /// <summary>
    /// The composition root. The only class that knows how the pieces fit together.
    /// Put it on one GameObject; make every view a child of it and they get bound
    /// automatically. Adding a layer never means editing this file.
    /// </summary>
    public sealed class VisualizerRoot : MonoBehaviour
    {
        [Tooltip("If true, a validation Error aborts the load. Turn off to inspect a broken run.")]
        [SerializeField] bool throwOnValidationError = false;

        [Tooltip("Example-brain geometry checks. Leave applies=false for your own brain.")]
        [SerializeField] RunExpectations runExpectations = new()
        {
            applies = false,
            expectedRingRadius = 60f,
            expectedAltitude = 30f,
            radiusTolerance = 8f,
            altitudeTolerance = 5f,
        };

        public ViewerContext Context { get; private set; }

        void Reset() => EnsurePalette();
        void Awake() => EnsurePalette();

        void EnsurePalette()
        {
            if (GetComponent<Palette>() == null)
                gameObject.AddComponent<Palette>();
        }

        /// <summary>Opt-in behaviour checks used on load and by the picker Validate button.</summary>
        public RunExpectations Expectations => runExpectations;

        void Start()
        {
            var settings = ViewerSettings.Load();
            string last = settings.lastRunStem;
            if (RunLoader.Exists(last) && LoadRun(last, out _))
                return;

            string message = string.IsNullOrEmpty(last)
                ? "No last run saved. Select a run to load."
                : $"Could not load last run:\n{last}";
            Debug.LogWarning("[viewer] " + message);

            var picker = GetComponent<RunPickerView>();
            if (picker != null)
                picker.OpenDialog(message);
        }

        public bool LoadRun(string prefix, out string error)
        {
            error = string.Empty;

            // 1. Teardown existing run if any
            if (Context != null)
            {
                Context.Clock.Pause();
                Context.Selection.Clear();
                Context.Clock.OnTimeChanged -= Context.State.Evaluate;
            }

            // 2. Load new RunData
            RunData run;
            string resolved;
            try
            {
                resolved = RunLoader.Resolve(prefix);
                run = RunLoader.Load(resolved, runExpectations, out var validation);
                if (throwOnValidationError && validation != null && validation.HasErrors)
                {
                    error = "validation failed: " + validation.Summary();
                    Debug.LogError($"[viewer] load aborted ({error}). Uncheck Throw On Validation Error to inspect anyway.");
                    return false;
                }
            }
            catch (Exception e)
            {
                error = e.Message;
                Debug.LogError($"[viewer] could not load '{prefix}': {e.Message}");
                return false;
            }

            RememberRun(resolved);

            // 3. Setup fresh state, clock, selection
            var state = new RunState(run);
            var clock = new PlaybackClock(run);
            var selection = new SelectionModel();

            Context = new ViewerContext
            {
                Run = run,
                State = state,
                Clock = clock,
                Selection = selection,
            };

            // Time flows into state
            clock.OnTimeChanged += state.Evaluate;

            // Selecting a drone makes it the observer
            selection.OnSelectionChanged += slot =>
            {
                if (slot < 0) return;
                var info = state.Info(slot);
                if (info.IsFriendly) selection.Observer = info.drone_id;
            };

            // 4. Bind all IRunView components in scene
            var allComponents = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include);
            foreach (var comp in allComponents)
            {
                if (comp is IRunView view)
                    view.Bind(Context);
            }

            // 5. Initial evaluation at t = 0
            state.Evaluate(0f);
            clock.Play();

            return true;
        }

        /// <summary>
        /// Manual attitude check. A quaternion can be unit length and still have the
        /// wrong handedness — no automated test catches that. Pause on a drone in
        /// straight transit, select it, then run this from the component context menu.
        /// transform.forward and velocity should roughly agree.
        /// </summary>
        [ContextMenu("Validate Attitude Visually")]
        public void ValidateAttitudeVisually()
        {
            if (Context == null)
            {
                Debug.LogWarning("[viewer] attitude check: no run loaded");
                return;
            }

            int slot = Context.Selection.Primary;
            EntityView view = null;
            var views = FindObjectsByType<EntityView>(FindObjectsInactive.Exclude);
            if (slot >= 0)
            {
                for (int i = 0; i < views.Length; i++)
                    if (views[i].Slot == slot) { view = views[i]; break; }
            }

            if (view == null)
            {
                for (int i = 0; i < views.Length; i++)
                {
                    if (views[i].Current.Alive && views[i].Current.Velocity.sqrMagnitude > 1f)
                    {
                        view = views[i];
                        slot = view.Slot;
                        break;
                    }
                }
            }

            Vector3 fwd, vel;
            if (view != null && view.Current.Alive)
            {
                fwd = view.transform.forward;
                vel = view.Current.Velocity;
            }
            else if (slot >= 0 && Context.Run.Sample(Context.State.FrameIndex, slot, out _, out var rot, out vel))
            {
                fwd = rot * Vector3.forward;
            }
            else
            {
                Debug.LogWarning("[viewer] attitude check: pick a living entity (preferably a transiting civilian) and try again");
                return;
            }

            float speed = vel.magnitude;
            float angle = speed > 0.05f ? Vector3.Angle(fwd, vel) : float.NaN;
            Debug.Log(
                $"[viewer] attitude check slot={slot} (manual — a unit quaternion can still be rotated wrongly)\n" +
                $"  transform.forward = {fwd}\n" +
                $"  velocity          = {vel}  (|v|={speed:F2} m/s)\n" +
                $"  angle between     = {(float.IsNaN(angle) ? "n/a (almost stationary)" : angle.ToString("F1") + " deg")}\n" +
                "  In straight transit these should roughly agree. If they do not, fix ned_quat_to_unity in the sidecar, not here.");
        }

        static void RememberRun(string stem)
        {
            var settings = ViewerSettings.Load();
            if (settings.lastRunStem == stem) return;
            settings.lastRunStem = stem ?? "";
            settings.Save();
        }

        void Update()
        {
            if (Context == null) return;
            Context.Clock.Tick(Time.deltaTime);
        }

        void OnDestroy()
        {
            if (Context != null)
                Context.Clock.OnTimeChanged -= Context.State.Evaluate;
        }
    }
}
