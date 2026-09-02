// Uploads active VolumeFixture meshes as a tiny centre/radius table so the
// shader can draw a contact line where two transparent shells meet. Scene
// depth cannot see those shells (they do not write Z). Identity is per-renderer
// via a property block so shared materials still skip themselves.

using UnityEngine;

namespace SwarmViewer
{
    [ExecuteAlways]
    [DefaultExecutionOrder(32000)]
    public sealed class VolumeFixtureRegistry : MonoBehaviour
    {
        public const int Max = 64;
        const string ShaderName = "Custom/VolumeFixture";

        static readonly int CountId = Shader.PropertyToID("_FixtureCount");
        static readonly int PosRadId = Shader.PropertyToID("_FixturePosRad");
        static readonly int MetaId = Shader.PropertyToID("_FixtureMeta");
        static readonly int SelfPosRadId = Shader.PropertyToID("_SelfPosRad");
        static readonly int SelfMetaId = Shader.PropertyToID("_SelfMeta");

        static VolumeFixtureRegistry _instance;
        static readonly Vector4[] PosRad = new Vector4[Max];
        static readonly Vector4[] Meta = new Vector4[Max];

        MaterialPropertyBlock _block;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void BootPlay() => Ensure();

        public static void Ensure()
        {
            if (_instance != null) return;
            _instance = FindAnyObjectByType<VolumeFixtureRegistry>();
            if (_instance != null) return;

            var go = new GameObject("VolumeFixtureRegistry")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            _instance = go.AddComponent<VolumeFixtureRegistry>();
        }

        void OnEnable()
        {
            _instance = this;
            _block ??= new MaterialPropertyBlock();
        }

        void OnDisable()
        {
            if (_instance == this)
                _instance = null;
            Shader.SetGlobalInt(CountId, 0);
        }

        void LateUpdate()
        {
            int n = 0;
            var rends = FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < rends.Length && n < Max; i++)
            {
                var rend = rends[i];
                if (!UsesFixture(rend)) continue;
                if (!Decode(rend, out Vector4 posRad, out Vector4 meta)) continue;

                PosRad[n] = posRad;
                Meta[n] = meta;
                n++;
            }

            for (int i = n; i < Max; i++)
            {
                PosRad[i] = Vector4.zero;
                Meta[i] = Vector4.zero;
            }

            Shader.SetGlobalInt(CountId, n);
            Shader.SetGlobalVectorArray(PosRadId, PosRad);
            Shader.SetGlobalVectorArray(MetaId, Meta);

            int written = 0;
            for (int i = 0; i < rends.Length && written < n; i++)
            {
                var rend = rends[i];
                if (!UsesFixture(rend)) continue;
                if (!Decode(rend, out Vector4 posRad, out Vector4 meta)) continue;

                rend.GetPropertyBlock(_block);
                _block.SetVector(SelfPosRadId, posRad);
                _block.SetVector(SelfMetaId, meta);
                rend.SetPropertyBlock(_block);
                written++;
            }
        }

        static bool UsesFixture(Renderer rend)
        {
            if (rend == null || !rend.enabled) return false;
            var mat = rend.sharedMaterial;
            return mat != null && mat.shader != null && mat.shader.name == ShaderName;
        }

        /// <summary>
        /// Unity default sphere is 1 m across; default cylinder is 1 m across and 2 m tall.
        /// World size follows lossyScale so parented kill spheres stay honest.
        /// </summary>
        static bool Decode(MeshRenderer rend, out Vector4 posRad, out Vector4 meta)
        {
            posRad = Vector4.zero;
            meta = Vector4.zero;
            var filter = rend.GetComponent<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            Vector3 scale = rend.transform.lossyScale;
            Vector3 pos = rend.transform.position;
            float radius = 0.5f * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            if (radius < 1e-4f) return false;

            posRad = new Vector4(pos.x, pos.y, pos.z, radius);
            if (IsCylinder(mesh))
            {
                float halfH = Mathf.Abs(scale.y);
                meta = new Vector4(1f, halfH, 0f, 0f);
            }
            else
            {
                meta = Vector4.zero;
            }
            return true;
        }

        static bool IsCylinder(Mesh mesh)
        {
            if (mesh == null) return false;
            string name = mesh.name;
            return name.StartsWith("Cylinder") || name.Contains("Cylinder");
        }
    }
}
