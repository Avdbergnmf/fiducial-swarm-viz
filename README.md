# fiducial-swarm-viz

3D playback viewer for `swarm_sim` recordings (drone interception runs) from
the [fiducial-take-home](../../README.md) challenge. Reads two files per run
(`<stem>.bin` + `<stem>.meta.json`, written by `tools/build_viewer_data.py` in
the parent repo — see its FORMAT.md) and renders them; it does no parsing of
raw sim traces itself.

**Unity 6000.5.10f1.** Open via Unity Hub with that version (or a newer
6000.5.x Editor).

## Quick start

1. Add this folder as a project in Unity Hub, open it.
2. Open `Assets/Scenes/SampleScene.unity` — the only scene.
3. Press Play. It loads the last-viewed run (`viewer.settings.json` →
   `lastRunStem`); if none is found, a run-picker dialog opens. Sample runs
   ship in `Assets/StreamingAssets/`: `fixture`, `s0`, `worst`.

Dependency: **com.unity.nuget.newtonsoft-json** (already in `Packages/`) —
`JsonUtility` can't parse the nested meta JSON.

## Layout

    Assets/Scripts/
      Camera/       orbit camera, hover outline, entity picking
      Cues/         reach/kill-envelope overlays, legend, run params
      Environment/  scene construction (SceneBuilder, EnvironmentView)
      Playback/     playback clock + input
      UI/           EntityInspector, Legend, RunPicker, SceneState, Timeline,
                     plus BudgetOverlay, DisagreeView, FilterChips, LogList,
                     ScoringView, UiScale
      (flat files)  data model, indices, cross-cutting singletons — see below

## Class structure

Composition-root pattern: one class wires everything, so adding a feature
never means editing existing ones.

- **`VisualizerRoot`** — the composition root. Loads a run, builds a fresh
  `RunState` / `PlaybackClock` / `SelectionModel`, wraps them in a
  `ViewerContext`, and calls `Bind(ctx)` on every `IRunView` in the scene.
- **`IRunView`** (in `ViewerContext.cs`) — the only extension point. Implement
  it on a `MonoBehaviour` under the root and it's auto-bound.
- **`RunLoader` / `RunData` / `RunModel`** — load `<stem>.bin` +
  `<stem>.meta.json` into an immutable `RunData`; `RunModel` is the plain-data
  shape of the meta JSON. `RunValidator` runs load-time sanity checks.
- **`RunState`** — evaluates `RunData` at a point in time into per-entity
  snapshots (position/rotation/velocity) for views to render.
- **`PlaybackClock`** — sole owner of current playback time (play/pause/
  speed/step).
- **`SelectionModel`** (in `ViewerContext.cs`) — click selection, multi-select,
  back/forward history, ground-truth vs. fleet-belief view mode.
- **`SceneBuilder` / `EntityView`** — instantiate and update per-entity
  GameObjects from `RunState` snapshots each frame.
- **`SwarmCoord`** — sim NED coordinates → Unity's left-handed Y-up, mirrored
  from `tools/build_viewer_data.py` in the parent repo.
- **`Palette`** — single source of truth for what each colour means, shared by
  3D bodies, UI, and the legend.
- **`*Index` classes** (`AimIndex`, `BeliefIndex`, `CommitIndex`,
  `EntityEventIndex`, `HearIndex`, `RelationIndex`, `TelemetryIndex`,
  `YieldIndex`) — one lookup per per-category record type in the meta JSON.
