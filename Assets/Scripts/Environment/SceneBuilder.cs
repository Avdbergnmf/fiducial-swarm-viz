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

        [Tooltip("World-metre pick sphere, independent of airframe scale. Fat click target.")]
        [SerializeField] float pickColliderRadius = 5f;

        [Header("Trails")]
        [Tooltip("Width multiplier times airframe scale. 5 at scale=1 matches the old scale=5 look.")]
        [SerializeField] float trailWidth = 5f;
        [Tooltip("Seconds of path kept. Prefab default was 8.")]
        [SerializeField] float trailTime = 4f;

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
        Material _friendly;
        Material _hostile;
        Material _civilian;
        Material _wreckage;
        Material _unknown;
        Material _compromised;
        Material _outlineSel;
        Material _outlineHover;

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
            if (run.KillRadius <= 0f)
                Debug.LogWarning(
                    $"[viewer] kill_radius unknown in meta; using inspector fallback {killRadius} m for the sphere.");
            if (killRadiusPrefab == null)
                Debug.LogWarning("[viewer] SceneBuilder.killRadiusPrefab is not assigned; no kill-radius sphere.");

            StampMaterials();

            _views = new EntityView[meta.slot_count];

            for (int s = 0; s < meta.slot_count; s++)
            {
                var view = Instantiate(entityPrefab, container);
                float sAbs = Mathf.Max(0.01f, scale);
                view.transform.localScale = Vector3.one * sAbs;
                view.Init(s, meta.entities[s]);
                view.SetOutlineMaterials(_outlineSel, _outlineHover);
                view.ConfigureTrail(trailWidth * sAbs, trailTime);
                view.ConfigurePickCollider(pickColliderRadius / sAbs);

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
            ctx.Selection.OnObserverChanged += OnObserverChanged;
            Refresh();
        }

        void OnSelectionChanged(int _) => Refresh();
        void OnViewModeChanged(ViewMode _) => Refresh();
        void OnObserverChanged(int _) => Refresh();

        public void Teardown()
        {
            if (_ctx != null)
            {
                _ctx.State.Changed -= Refresh;
                _ctx.Selection.OnSelectionChanged -= OnSelectionChanged;
                _ctx.Selection.OnViewModeChanged -= OnViewModeChanged;
                _ctx.Selection.OnObserverChanged -= OnObserverChanged;
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

            EntityView.ReleasePickGhosts();
            ReleaseStamped();
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
            if (_ctx.Selection.Mode == ViewMode.FleetBelief)
                return MaterialForBelief(slot);

            if (_ctx.Selection.Mode == ViewMode.GroundTruth && _ctx.State.IsCompromisedNow(slot) && _compromised != null)
                return _compromised;

            return MaterialForKind(_ctx.State.Info(slot).Kind);
        }

        void StampMaterials()
        {
            ReleaseStamped();
            _friendly = Stamp(friendlyMat, Palette.Friendly);
            _hostile = Stamp(hostileMat, Palette.Hostile);
            _civilian = Stamp(civilianMat, Palette.Civilian);
            _wreckage = Stamp(wreckageMat, Palette.Wreckage);
            _unknown = Stamp(unknownMat, Palette.Unknown);
            _compromised = Stamp(compromisedMat, Palette.Compromised);
            _outlineSel = Stamp(selectedOutlineMaterial, Palette.Selected);
            _outlineHover = Stamp(hoverOutlineMaterial, Palette.Hover);
        }

        static Material Stamp(Material src, Color color)
        {
            if (src == null) return null;
            var copy = new Material(src) { hideFlags = HideFlags.HideAndDontSave };
            Palette.Tint(copy, color);
            return copy;
        }

        void ReleaseStamped()
        {
            DestroyIfOwned(_friendly); _friendly = null;
            DestroyIfOwned(_hostile); _hostile = null;
            DestroyIfOwned(_civilian); _civilian = null;
            DestroyIfOwned(_wreckage); _wreckage = null;
            DestroyIfOwned(_unknown); _unknown = null;
            DestroyIfOwned(_compromised); _compromised = null;
            DestroyIfOwned(_outlineSel); _outlineSel = null;
            DestroyIfOwned(_outlineHover); _outlineHover = null;
        }

        static void DestroyIfOwned(Material mat)
        {
            if (mat != null) Destroy(mat);
        }

        Material MaterialForBelief(int slot)
        {
            int observer = _ctx.Selection.Observer;
            var info = _ctx.State.Info(slot);
            if (observer >= 0 && info.drone_id == observer)
                return _friendly;

            if (observer < 0)
                return _unknown;

            var cls = _ctx.Run.Beliefs.At(observer, slot, _ctx.Clock.Time);
            return cls switch
            {
                BeliefClass.Friendly => _friendly,
                BeliefClass.Enemy => _hostile,
                BeliefClass.Neutral => _civilian,
                BeliefClass.Compromised => _compromised != null ? _compromised : _hostile,
                _ => _unknown,
            };
        }

        Material MaterialForKind(EntityKind kind) => kind switch
        {
            EntityKind.Friendly => _friendly,
            EntityKind.Hostile => _hostile,
            EntityKind.Civilian => _civilian,
            EntityKind.Wreckage => _wreckage,
            _ => _unknown,
        };
    }
}
