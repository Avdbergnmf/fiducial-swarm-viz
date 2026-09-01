// Manages scene environment objects (ground plane, defended asset marker, arena boundary markers).
// Instantiates the asset from a prefab once; never creates primitives at runtime.

using UnityEngine;
using UnityEngine.Rendering;

namespace SwarmViewer
{
    public sealed class EnvironmentView : MonoBehaviour, IRunView
    {
        [Header("Scene Object References")]
        [SerializeField] Transform groundTransform;
        [SerializeField] Transform northBorder;
        [SerializeField] Transform southBorder;
        [SerializeField] Transform eastBorder;
        [SerializeField] Transform westBorder;

        [Header("Asset")]
        [SerializeField] GameObject assetPrefab;
        [SerializeField] Transform assetParent;

        [Header("Fallbacks (used only when meta omits the field)")]
        [SerializeField] float fallbackArenaMin = -200f;
        [SerializeField] float fallbackArenaMax = 200f;
        [SerializeField] float fallbackAssetRadius = 30f;

        GameObject _assetInstance;
        Material _assetMat;

        public void Bind(ViewerContext ctx)
        {
            EnsureAsset();
            UpdateEnvironment(ctx.Run.Meta);
        }

        void EnsureAsset()
        {
            if (_assetInstance != null) return;
            if (assetPrefab == null)
            {
                Debug.LogWarning("[viewer] EnvironmentView.assetPrefab is not assigned.");
                return;
            }

            Transform parent = assetParent != null ? assetParent : transform;
            _assetInstance = Instantiate(assetPrefab, parent);
            _assetInstance.name = "Asset";
        }

        public void UpdateEnvironment(RunMeta meta)
        {
            bool hasArena = meta.arena?.min != null && meta.arena.max != null
                            && meta.arena.min.Length >= 3 && meta.arena.max.Length >= 3;

            float minX, maxX, minZ, maxZ;
            if (hasArena)
            {
                minX = meta.arena.min[0];
                maxX = meta.arena.max[0];
                minZ = meta.arena.min[2];
                maxZ = meta.arena.max[2];
            }
            else
            {
                minX = minZ = fallbackArenaMin;
                maxX = maxZ = fallbackArenaMax;
                Debug.LogWarning(
                    $"[viewer] meta.arena min/max missing; using inspector fallback " +
                    $"[{minX}, {maxX}] on XZ. Not a measured arena — load continues.");
            }

            float width = maxX - minX;
            float length = maxZ - minZ;
            float centerX = (minX + maxX) * 0.5f;
            float centerZ = (minZ + maxZ) * 0.5f;

            // Scale ground to area size
            if (groundTransform != null)
            {
                groundTransform.position = new Vector3(centerX, 0f, centerZ);
                // Unity standard plane is 10x10 meters at scale (1,1,1)
                groundTransform.localScale = new Vector3(width / 10f, 1f, length / 10f);
            }

            // scale and position asset marker
            Vector3 assetPos = Vector3.zero;
            if (meta.asset?.position != null && meta.asset.position.Length >= 3)
                assetPos = new Vector3(meta.asset.position[0], meta.asset.position[1], meta.asset.position[2]);
            else
                Debug.LogWarning(
                    "[viewer] meta.asset.position missing; placing the asset at origin. Not a measured position — load continues.");

            float assetRadius;
            if (meta.asset != null && meta.asset.radius > 0f)
            {
                assetRadius = meta.asset.radius;
            }
            else
            {
                assetRadius = fallbackAssetRadius;
                Debug.LogWarning(
                    $"[viewer] meta.asset.radius missing or 0; using inspector fallback {assetRadius} m. " +
                    "Not a measured radius — load continues.");
            }

            if (_assetInstance != null)
            {
                // Prefab: Unity cylinder (2 m tall, 1 m diameter, pivot in the
                // middle) plus a disabled sphere. The sim volume is a vertical
                // cylinder of radius asset_radius from the ground to the arena
                // ceiling (D10) — not a sphere, and not the 2 m disc we used
                // to plant at y=1, which made 7 m breaches look like they hit
                // a hat. Scale XZ is 2 * radius because the mesh is 1 m across.
                // Scale Y so the mesh spans floor → ceiling.
                float floorY = 0f;
                float ceilingY = 120f;
                if (hasArena && meta.arena.max.Length >= 2 && meta.arena.max[1] > 1f)
                    ceilingY = meta.arena.max[1];
                float height = ceilingY - floorY;
                _assetInstance.transform.position = new Vector3(assetPos.x, floorY + height * 0.5f, assetPos.z);
                _assetInstance.transform.localScale = new Vector3(
                    assetRadius * 2f, height * 0.5f, assetRadius * 2f);
                TintAsset();
            }

            // scale and position border markers
            if (northBorder != null)
            {
                northBorder.position = new Vector3(centerX, 0.1f, maxZ);
                northBorder.localScale = new Vector3(width, 0.2f, 1f);
            }
            if (southBorder != null)
            {
                southBorder.position = new Vector3(centerX, 0.1f, minZ);
                southBorder.localScale = new Vector3(width, 0.2f, 1f);
            }
            if (eastBorder != null)
            {
                eastBorder.position = new Vector3(maxX, 0.1f, centerZ);
                eastBorder.localScale = new Vector3(1f, 0.2f, length);
            }
            if (westBorder != null)
            {
                westBorder.position = new Vector3(minX, 0.1f, centerZ);
                westBorder.localScale = new Vector3(1f, 0.2f, length);
            }
        }

        void TintAsset()
        {
            if (_assetInstance == null) return;
            var rends = _assetInstance.GetComponentsInChildren<Renderer>(true);
            if (rends.Length == 0) return;
            if (_assetMat == null && rends[0].sharedMaterial != null)
                _assetMat = new Material(rends[0].sharedMaterial) { hideFlags = HideFlags.HideAndDontSave };
            if (_assetMat == null) return;
            Palette.TintVolume(_assetMat, Palette.Asset);
            for (int i = 0; i < rends.Length; i++)
            {
                rends[i].sharedMaterial = _assetMat;
                rends[i].shadowCastingMode = ShadowCastingMode.Off;
                rends[i].receiveShadows = false;
            }
            var cols = _assetInstance.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++)
                cols[i].enabled = false;
        }

        void OnDestroy()
        {
            if (_assetMat != null) Destroy(_assetMat);
        }
    }
}
