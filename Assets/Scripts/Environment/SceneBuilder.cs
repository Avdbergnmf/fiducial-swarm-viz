// Creates entity GameObjects from an assigned prefab and keeps their transforms and materials
// in sync with simulation snapshots. Supports clean teardown and rebuild when loading new runs.

using UnityEngine;

namespace SwarmViewer
{
    public sealed class SceneBuilder : MonoBehaviour, IRunView
    {
        [Header("Prefab & Container")]
        [SerializeField] EntityView entityPrefab;
        [SerializeField] Transform container;
        [SerializeField] float scale = 5f;

        [Header("Outline (selection / hover — not the entity body)")]
        [SerializeField] Material selectedOutlineMaterial;
        [SerializeField] Material hoverOutlineMaterial;

        [Header("Kill radius")]
        [SerializeField] GameObject killRadiusPrefab;
        [SerializeField] float killRadius = 3.0f;

        [Header("Entity Materials (class / belief only — not selection)")]
        [SerializeField] Material friendlyMat;
        [SerializeField] Material hostileMat;
        [SerializeField] Material civilianMat;
        [SerializeField] Material wreckageMat;
        [SerializeField] Material unknownMat;
        [SerializeField] Material compromisedMat;

        ViewerContext _ctx;
        EntityView[] _views;

        public void Bind(ViewerContext ctx)
        {
            Teardown();

            _ctx = ctx;
            if (container == null) container = transform;

            if (entityPrefab == null)
            {
                Debug.LogError("[viewer] SceneBuilder.entityPrefab is not assigned.");
                return;
            }

            var run = ctx.Run;
            var meta = run.Meta;
            float radius = run.KillRadiusOrDefault(killRadius);
            if (killRadiusPrefab == null)
                Debug.LogWarning("[viewer] SceneBuilder.killRadiusPrefab is not assigned; no kill-radius sphere.");

            _views = new EntityView[meta.slot_count];

            for (int s = 0; s < meta.slot_count; s++)
            {
                var view = Instantiate(entityPrefab, container);
                view.transform.localScale = Vector3.one * scale;
                view.Init(s, meta.entities[s]);
                view.SetOutlineMaterials(selectedOutlineMaterial, hoverOutlineMaterial);

                if (killRadiusPrefab != null)
                {
                    var marker = Instantiate(killRadiusPrefab, view.transform);
                    view.AttachKillRadius(marker, radius);
                }

                var events = view.GetComponent<EntityEvents>() ?? view.gameObject.AddComponent<EntityEvents>();
                var info = meta.entities[s];
                events.Populate(
                    info,
                    radius,
                    run.EventsFor(s),
                    info.drone_id >= 0 ? run.LogsForDrone(info.drone_id) : null);

                view.gameObject.SetActive(false);
                _views[s] = view;
            }

            ctx.State.Changed += Refresh;
            ctx.Selection.OnSelectionChanged += OnSelectionChanged;
            ctx.Selection.OnViewModeChanged += OnViewModeChanged;
            Refresh();
        }

        void OnSelectionChanged(int _) => Refresh();
        void OnViewModeChanged(ViewMode _) => Refresh();

        public void Teardown()
        {
            if (_ctx != null)
            {
                _ctx.State.Changed -= Refresh;
                _ctx.Selection.OnSelectionChanged -= OnSelectionChanged;
                _ctx.Selection.OnViewModeChanged -= OnViewModeChanged;
            }

            if (_views != null)
            {
                for (int i = 0; i < _views.Length; i++)
                {
                    if (_views[i] != null)
                        Destroy(_views[i].gameObject);
                }
                _views = null;
            }
        }

        void OnDestroy()
        {
            Teardown();
        }

        void Refresh()
        {
            if (_ctx == null || _views == null) return;

            var snaps = _ctx.State.Entities;
            for (int s = 0; s < _views.Length; s++)
            {
                if (_views[s] == null) continue;
                _views[s].Apply(snaps[s]);
                _views[s].SetSelected(_ctx.Selection.IsSelected(s));
                if (snaps[s].Alive)
                    _views[s].SetMaterial(MaterialFor(s));
            }
        }

        Material MaterialFor(int slot)
        {
            if (_ctx.Selection.Mode == ViewMode.GroundTruth && _ctx.State.IsCompromisedNow(slot) && compromisedMat != null)
                return compromisedMat;

            return _ctx.State.Info(slot).Kind switch
            {
                EntityKind.Friendly => friendlyMat,
                EntityKind.Hostile => hostileMat,
                EntityKind.Civilian => civilianMat,
                EntityKind.Wreckage => wreckageMat,
                _ => unknownMat,
            };
        }
    }
}
