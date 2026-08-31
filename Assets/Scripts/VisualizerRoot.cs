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
        public ViewerContext Context { get; private set; }

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
                run = RunLoader.Load(resolved);
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

            // Primary slot drives the belief observer (must stay single-valued).
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


