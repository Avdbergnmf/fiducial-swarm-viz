// Root coordinator component that initializes data loading, manages the playback clock, and drives views.
// Handles playback user input and displays a lightweight debug HUD.

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
        [Tooltip("Path without extension, relative to StreamingAssets or the project folder.")]
        [SerializeField] string runPrefix = "fixture";

        public ViewerContext Context { get; private set; }

        void Start()
        {
            RunData run;
            try
            {
                run = RunLoader.Load(RunLoader.Resolve(runPrefix));
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[viewer] could not load '{runPrefix}': {e.Message}");
                enabled = false;
                return;
            }

            var state = new RunState(run);
            var clock = new PlaybackClock(run);
            var selection = new SelectionModel();

            Context = new ViewerContext
            {
                Run = run, State = state, Clock = clock, Selection = selection,
            };

            // The one place time flows into the world.
            clock.OnTimeChanged += state.Evaluate;

            // Selecting a drone makes it the observer, so a belief view follows the
            // click without a second control to operate.
            selection.OnSelectionChanged += slot =>
            {
                if (slot < 0) return;
                var info = state.Info(slot);
                if (info.IsFriendly) selection.Observer = info.drone_id;
            };

            foreach (var view in GetComponentsInChildren<IRunView>(true))
                view.Bind(Context);

            state.Evaluate(0f);
            clock.Play();
        }

        void Update()
        {
            if (Context == null) return;
            Context.Clock.Tick(Time.deltaTime);
            HandleKeys();
        }

        void HandleKeys()
        {
            var clock = Context.Clock;

            if (Input.GetKeyDown(KeyCode.Space)) clock.TogglePlay();

            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (Input.GetKeyDown(KeyCode.RightArrow))
            { if (shift) clock.StepSeconds(1f); else clock.StepFrames(1); }
            if (Input.GetKeyDown(KeyCode.LeftArrow))
            { if (shift) clock.StepSeconds(-1f); else clock.StepFrames(-1); }

            if (Input.GetKeyDown(KeyCode.Alpha1)) clock.Speed = 0.25f;
            if (Input.GetKeyDown(KeyCode.Alpha2)) clock.Speed = 1f;
            if (Input.GetKeyDown(KeyCode.Alpha3)) clock.Speed = 2f;

            if (Input.GetKeyDown(KeyCode.Tab))
                Context.Selection.Mode = Context.Selection.Mode == ViewMode.GroundTruth
                    ? ViewMode.FleetBelief : ViewMode.GroundTruth;

            if (Input.GetKeyDown(KeyCode.Escape)) Context.Selection.Clear();
        }

        void OnGUI()
        {
            if (Context == null) return;
            var c = Context.Clock;
            GUI.Label(new Rect(10, 10, 400, 20),
                $"t = {c.Time,6:F2} / {c.Duration:F0} s   frame {c.FrameIndex}   " +
                $"x{c.Speed}   {(c.IsPlaying ? "playing" : "paused")}");
            GUI.Label(new Rect(10, 30, 600, 20),
                "space play/pause   arrows step frame   shift+arrows step second   1/2/3 speed   tab view");
        }

        void OnDestroy()
        {
            if (Context != null) Context.Clock.OnTimeChanged -= Context.State.Evaluate;
        }
    }
}

