// Creates entity GameObjects and environment geometry (ground, defended asset, arena outline),
// keeping entity transforms and material colors in sync with simulation snapshots.

using UnityEngine;

namespace SwarmViewer
{
    /// <summary>
    /// Creates one GameObject per entity and keeps them in step with RunState.
    /// The first and simplest IRunView; every later layer follows this shape.
    ///
    /// GameObjects rather than DrawMeshInstanced on purpose: they are clickable,
    /// inspectable in the hierarchy while paused, and things can be parented to
    /// them. Forty of them costs nothing. If a generated scenario ever brings a
    /// fleet big enough to stutter, swap this one class for an instanced renderer
    /// and nothing else in the viewer changes.
    /// </summary>
    public sealed class SceneBuilder : MonoBehaviour, IRunView
    {
        [Tooltip("Optional. Falls back to a primitive capsule if empty.")]
        [SerializeField] EntityView entityPrefab;
        [SerializeField] Transform container;
        [SerializeField] float scale = 5f;

        [Header("Colour means one thing throughout. Put this in the legend.")]
        [SerializeField] Color friendly = new(0.25f, 0.60f, 1.00f);
        [SerializeField] Color hostile = new(1.00f, 0.25f, 0.20f);
        [SerializeField] Color civilian = new(0.80f, 0.80f, 0.80f);
        [SerializeField] Color wreckage = new(0.45f, 0.35f, 0.25f);
        [SerializeField] Color unknown = new(0.35f, 0.35f, 0.40f);
        [SerializeField] Color compromised = new(1.00f, 0.75f, 0.15f);
        [SerializeField] Color selected = Color.white;

        ViewerContext _ctx;
        EntityView[] _views;
        Material _capsuleMat;
        Material _trailMat;

        public void Bind(ViewerContext ctx)
        {
            _ctx = ctx;
            if (container == null) container = transform;

            var meta = ctx.Run.Meta;

            InitMaterials();
            SpawnWorld(meta);

            _views = new EntityView[meta.slot_count];

            for (int s = 0; s < meta.slot_count; s++)
            {
                var view = entityPrefab != null
                    ? Instantiate(entityPrefab, container)
                    : CreateFallback(container);

                view.transform.localScale = Vector3.one * scale;
                view.Init(s, meta.entities[s]);
                view.gameObject.SetActive(false);
                _views[s] = view;
            }

            ctx.State.Changed += Refresh;
            ctx.Selection.OnSelectionChanged += _ => Refresh();
            ctx.Selection.OnViewModeChanged += _ => Refresh();
            Refresh();
        }

        void InitMaterials()
        {
            Shader litShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            _capsuleMat = new Material(litShader) { name = "EntityCapsuleMat" };
            _capsuleMat.SetFloat("_Smoothness", 0.3f);

            Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Sprites/Default")
                ?? Shader.Find("Unlit/Color");
            _trailMat = new Material(unlitShader) { name = "EntityTrailMat" };
        }

        void SpawnWorld(RunMeta meta)
        {
            var worldRoot = new GameObject("World").transform;
            worldRoot.SetParent(transform, false);

            Shader litShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

            // 1. Ground Plane (scaled to cover arena)
            float minX = meta.arena?.min != null && meta.arena.min.Length > 0 ? meta.arena.min[0] : -200f;
            float maxX = meta.arena?.max != null && meta.arena.max.Length > 0 ? meta.arena.max[0] : 200f;
            float minZ = meta.arena?.min != null && meta.arena.min.Length > 2 ? meta.arena.min[2] : -200f;
            float maxZ = meta.arena?.max != null && meta.arena.max.Length > 2 ? meta.arena.max[2] : 200f;

            float width = maxX - minX;
            float length = maxZ - minZ;
            float centerX = (minX + maxX) * 0.5f;
            float centerZ = (minZ + maxZ) * 0.5f;

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.SetParent(worldRoot, false);
            ground.transform.position = new Vector3(centerX, 0f, centerZ);
            ground.transform.localScale = new Vector3(width / 10f, 1f, length / 10f);
            Destroy(ground.GetComponent<Collider>());

            var groundMat = new Material(litShader) { name = "GroundMat" };
            Color groundColor = new Color(0.12f, 0.12f, 0.14f);
            groundMat.SetColor("_BaseColor", groundColor);
            groundMat.SetColor("_Color", groundColor);
            groundMat.SetFloat("_Smoothness", 0.05f);
            ground.GetComponent<MeshRenderer>().sharedMaterial = groundMat;

            // 2. Defended Asset (cylinder at asset.position, radius ~30, height ~2)
            Vector3 assetPos = Vector3.zero;
            if (meta.asset?.position != null && meta.asset.position.Length >= 3)
                assetPos = new Vector3(meta.asset.position[0], meta.asset.position[1], meta.asset.position[2]);
            float assetRadius = meta.asset != null && meta.asset.radius > 0f ? meta.asset.radius : 30f;

            var assetGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            assetGo.name = "Asset";
            assetGo.transform.SetParent(worldRoot, false);
            assetGo.transform.position = new Vector3(assetPos.x, 1f, assetPos.z);
            assetGo.transform.localScale = new Vector3(assetRadius * 2f, 1f, assetRadius * 2f);
            Destroy(assetGo.GetComponent<Collider>());

            var assetMat = new Material(litShader) { name = "AssetMat" };
            Color assetColor = new Color(1.0f, 0.65f, 0.10f);
            assetMat.SetColor("_BaseColor", assetColor);
            assetMat.SetColor("_Color", assetColor);
            assetMat.SetFloat("_Smoothness", 0.2f);
            assetGo.GetComponent<MeshRenderer>().sharedMaterial = assetMat;

            // 3. Arena outline (thin edge cubes at ground level)
            var borderMat = new Material(litShader) { name = "ArenaBorderMat" };
            Color borderColor = new Color(0.35f, 0.40f, 0.45f);
            borderMat.SetColor("_BaseColor", borderColor);
            borderMat.SetColor("_Color", borderColor);
            borderMat.SetFloat("_Smoothness", 0.1f);

            CreateBorderCube("NorthBorder", worldRoot, new Vector3(centerX, 0.1f, maxZ), new Vector3(width, 0.2f, 1f), borderMat);
            CreateBorderCube("SouthBorder", worldRoot, new Vector3(centerX, 0.1f, minZ), new Vector3(width, 0.2f, 1f), borderMat);
            CreateBorderCube("EastBorder", worldRoot, new Vector3(maxX, 0.1f, centerZ), new Vector3(1f, 0.2f, length), borderMat);
            CreateBorderCube("WestBorder", worldRoot, new Vector3(minX, 0.1f, centerZ), new Vector3(1f, 0.2f, length), borderMat);
        }

        static void CreateBorderCube(string name, Transform parent, Vector3 pos, Vector3 scale, Material mat)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent, false);
            cube.transform.position = pos;
            cube.transform.localScale = scale;
            Destroy(cube.GetComponent<Collider>());
            cube.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        void OnDestroy()
        {
            if (_ctx != null) _ctx.State.Changed -= Refresh;
        }

        void Refresh()
        {
            var snaps = _ctx.State.Entities;
            for (int s = 0; s < _views.Length; s++)
            {
                _views[s].Apply(snaps[s]);
                if (snaps[s].Alive) _views[s].SetColor(ColourFor(s));
            }
        }

        Color ColourFor(int slot)
        {
            if (slot == _ctx.Selection.SelectedSlot) return selected;

            // Fleet-belief colouring plugs in here once beliefs are wired: read
            // ctx.State's belief lookup for ctx.Selection.Observer and fall back
            // to `unknown` where that drone has declared nothing.
            if (_ctx.Selection.Mode == ViewMode.GroundTruth && _ctx.State.IsCompromisedNow(slot))
                return compromised;

            return _ctx.State.Info(slot).Kind switch
            {
                EntityKind.Friendly => friendly,
                EntityKind.Hostile => hostile,
                EntityKind.Civilian => civilian,
                EntityKind.Wreckage => wreckage,
                _ => unknown,
            };
        }

        EntityView CreateFallback(Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.transform.SetParent(parent, false);
            Destroy(go.GetComponent<Collider>());

            var meshRenderer = go.GetComponent<MeshRenderer>();
            if (meshRenderer != null && _capsuleMat != null)
            {
                meshRenderer.sharedMaterial = _capsuleMat;
            }

            var trail = go.AddComponent<TrailRenderer>();
            trail.time = 8f;
            trail.minVertexDistance = 0.5f;
            trail.startWidth = 1.2f;
            trail.endWidth = 0.05f;
            if (_trailMat != null)
            {
                trail.sharedMaterial = _trailMat;
            }

            return go.AddComponent<EntityView>();
        }
    }
}

