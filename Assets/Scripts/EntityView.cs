// Visual representation of a single entity airframe in the scene.
// Manages position, rotation, class material, trail, outline, and kill-radius marker.
// Selection never touches the entity's own material — only the outline child.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SwarmViewer
{
    /// <summary>
    /// One aircraft in the scene. Holds its identity and its latest snapshot, so
    /// you can click it in the hierarchy mid-run and read what it is. Anything that
    /// wants to hang off a specific aircraft later -- a trail, a label, a selection
    /// outline -- attaches to this GameObject rather than reaching into arrays.
    ///
    /// Deliberately passive: it never looks up time, it is handed a snapshot.
    /// </summary>
    public sealed class EntityView : MonoBehaviour
    {
        public int Slot { get; private set; }
        public EntityInfo Info { get; private set; }
        public EntitySnapshot Current { get; private set; }

        [SerializeField] Renderer mainRenderer;
        [SerializeField] TrailRenderer trail;
        [SerializeField] GameObject outlineObject;
        [SerializeField] Renderer outlineRenderer;

        [Header("Outline materials")]
        [SerializeField] Material selectedOutlineMaterial;
        [SerializeField] Material hoverOutlineMaterial;

        Material _currentMaterial;
        MaterialPropertyBlock _trailBlock;
        bool _selected;
        bool _hovered;
        bool _killCueOn = true;
        bool _pickVolumeCueOn;
        bool _pickHover;
        bool _pickSelected;
        bool _pickUnselected;
        GameObject _killRadiusGo;
        GameObject _pickVolumeGo;
        MeshRenderer _pickVolumeRend;

        static readonly Dictionary<Material, Material> PickGhosts = new();
        static Material _killMat;

        public bool IsSelected => _selected;
        public bool IsHovered => _hovered;

        /// <summary>
        /// The airframe mesh and its live class material. Used to stamp a
        /// transparent ghost at a coasted pose — not the kill-radius sphere.
        /// </summary>
        public bool TryAirframe(out Mesh mesh, out Vector3 worldScale, out Material source)
        {
            mesh = null;
            worldScale = transform.lossyScale;
            source = _currentMaterial;
            if (mainRenderer == null)
                mainRenderer = GetComponent<MeshRenderer>();
            if (mainRenderer == null) return false;
            var filter = mainRenderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) return false;
            mesh = filter.sharedMesh;
            if (source == null) source = mainRenderer.sharedMaterial;
            return source != null;
        }

        public void Init(int slot, EntityInfo info)
        {
            Slot = slot;
            Info = info;
            name = $"{slot:D2} {info.Label}";

            if (mainRenderer == null)
                mainRenderer = GetComponent<MeshRenderer>();
            if (trail == null)
                trail = GetComponentInChildren<TrailRenderer>();
            if (outlineObject == null)
            {
                var outlineT = transform.Find("Outline");
                if (outlineT != null) outlineObject = outlineT.gameObject;
            }
            if (outlineRenderer == null && outlineObject != null)
                outlineRenderer = outlineObject.GetComponent<Renderer>();

            ApplyOutline();
        }

        /// <summary>
        /// World-space trail width is independent of transform scale, so the
        /// airframe scale has to be multiplied in here. Time is seconds of history.
        /// </summary>
        public void ConfigureTrail(float widthMultiplier, float time)
        {
            if (trail == null)
                trail = GetComponentInChildren<TrailRenderer>();
            if (trail == null) return;
            trail.widthMultiplier = Mathf.Max(0.01f, widthMultiplier);
            trail.time = Mathf.Max(0.05f, time);
        }

        /// <summary>
        /// Pick volume in local metres. Larger than the mesh so a click near the
        /// craft still hits; EntityPicker breaks ties by origin, not first hit.
        /// The Selection cue owns when this mesh is visible (hover / selected /
        /// unselected). Tinted from the airframe material so class / belief colour
        /// carries through.
        /// </summary>
        public void ConfigurePickCollider(float localRadius)
        {
            var box = GetComponent<BoxCollider>();
            if (box != null)
                box.enabled = false;

            var sphere = GetComponent<SphereCollider>();
            if (sphere == null)
                sphere = gameObject.AddComponent<SphereCollider>();
            sphere.enabled = true;
            sphere.center = Vector3.zero;
            float r = Mathf.Max(0.05f, localRadius);
            sphere.radius = r;

            if (_pickVolumeGo != null)
                Destroy(_pickVolumeGo);

            _pickVolumeGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _pickVolumeGo.name = "PickVolume";
            var volumeCol = _pickVolumeGo.GetComponent<Collider>();
            if (volumeCol != null)
            {
                volumeCol.enabled = false;
                Object.Destroy(volumeCol);
            }
            var t = _pickVolumeGo.transform;
            t.SetParent(transform, false);
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one * (r * 2f);
            VolumeFixtureRegistry.Ensure();
            _pickVolumeRend = _pickVolumeGo.GetComponent<MeshRenderer>();
            _pickVolumeRend.shadowCastingMode = ShadowCastingMode.Off;
            _pickVolumeRend.receiveShadows = false;
            if (_currentMaterial != null)
                ApplyPickGhost(_currentMaterial);
            _pickVolumeGo.SetActive(false);
        }

        /// <summary>Runtime copies of class materials. Call when the scene is torn down.</summary>
        public static void ReleasePickGhosts()
        {
            foreach (var kv in PickGhosts)
            {
                if (kv.Value != null)
                    Destroy(kv.Value);
            }
            PickGhosts.Clear();
            if (_killMat != null)
            {
                Destroy(_killMat);
                _killMat = null;
            }
        }

        /// <summary>
        /// Parents a pre-instantiated kill-radius sphere. Prefab is assumed 1 m in
        /// diameter (Unity default sphere); we scale to 2 * radius.
        /// <paramref name="radiusMetres"/> is in the parent's local metres —
        /// SceneBuilder divides world kill_radius by airframe scale.
        /// A hit is the other craft's origin inside this sphere, not two
        /// shells merely touching.
        /// </summary>
        public void AttachKillRadius(GameObject instance, float radiusMetres)
        {
            _killRadiusGo = instance;
            if (_killRadiusGo == null) return;
            var t = _killRadiusGo.transform;
            t.SetParent(transform, false);
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one * (radiusMetres * 2f);
            VolumeFixtureRegistry.Ensure();
            TintKillSphere(_killRadiusGo);
            foreach (var col in _killRadiusGo.GetComponentsInChildren<Collider>())
                col.enabled = false;
            _killRadiusGo.SetActive(false);
        }

        static void TintKillSphere(GameObject go)
        {
            var rends = go.GetComponentsInChildren<Renderer>(true);
            if (rends.Length == 0) return;
            if (_killMat == null && rends[0].sharedMaterial != null)
            {
                _killMat = new Material(rends[0].sharedMaterial)
                {
                    name = "KillRadius (runtime)",
                    hideFlags = HideFlags.HideAndDontSave
                };
                Palette.TintVolume(_killMat, Palette.Kill);
            }
            for (int i = 0; i < rends.Length; i++)
            {
                if (_killMat != null)
                    rends[i].sharedMaterial = _killMat;
                rends[i].shadowCastingMode = ShadowCastingMode.Off;
                rends[i].receiveShadows = false;
            }
        }

        public void Apply(in EntitySnapshot snap)
        {
            Current = snap;

            if (!snap.Alive)
            {
                if (trail != null && gameObject.activeSelf)
                    trail.Clear();

                if (gameObject.activeSelf)
                    gameObject.SetActive(false);
                return;
            }

            if (!gameObject.activeSelf)
            {
                if (trail != null)
                    trail.Clear();
                gameObject.SetActive(true);
            }

            transform.SetPositionAndRotation(snap.Position, snap.Rotation);
            UpdateKillRadiusVisibility();
            UpdatePickVolumeVisibility();
        }

        /// <summary>Class / belief colour only. Never used for selection.</summary>
        public void SetMaterial(Material mat)
        {
            if (mat == null || _currentMaterial == mat) return;
            _currentMaterial = mat;

            if (mainRenderer != null)
                mainRenderer.sharedMaterial = mat;

            ApplyTrailColor(mat);
            ApplyPickGhost(mat);
        }

        void ApplyPickGhost(Material source)
        {
            if (_pickVolumeRend == null) return;
            var ghost = GhostOf(source);
            if (ghost != null)
                _pickVolumeRend.sharedMaterial = ghost;
        }

        static Shader _volumeShader;

        static Material GhostOf(Material source)
        {
            if (source == null) return null;
            if (PickGhosts.TryGetValue(source, out var ghost) && ghost != null)
                return ghost;

            if (_volumeShader == null)
                _volumeShader = Shader.Find("Custom/VolumeFixture");

            ghost = new Material(source)
            {
                name = source.name + " (pick ghost)",
                hideFlags = HideFlags.HideAndDontSave
            };
            if (_volumeShader != null)
                ghost.shader = _volumeShader;

            MakeTransparentGhost(ghost, Palette.PickVolumeAlpha);
            PickGhosts[source] = ghost;
            return ghost;
        }

        static void MakeTransparentGhost(Material mat, float alpha)
        {
            Palette.MakeTransparent(mat);
            if (mat.HasProperty("_Cull"))
                mat.SetFloat("_Cull", (float)CullMode.Off);
            Color c = ReadBaseColor(mat);
            c.a = alpha;
            Palette.Tint(mat, c);
            Palette.ApplyVolumeLook(mat);
        }

        /// <summary>
        /// Trail.mat is URP Unlit, which ignores vertex colour, so the ribbon
        /// is tinted via a property block on <c>_BaseColor</c>. The gradient is
        /// still set so a vertex-colour shader would fade too.
        /// </summary>
        void ApplyTrailColor(Material source)
        {
            if (trail == null)
                trail = GetComponentInChildren<TrailRenderer>();
            if (trail == null) return;

            Color c = ReadBaseColor(source);

            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
                new[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0f, 1f) });
            trail.colorGradient = gradient;

            _trailBlock ??= new MaterialPropertyBlock();
            trail.GetPropertyBlock(_trailBlock);
            var tint = new Color(c.r, c.g, c.b, 0.85f);
            _trailBlock.SetColor("_BaseColor", tint);
            _trailBlock.SetColor("_Color", tint);
            trail.SetPropertyBlock(_trailBlock);
        }

        static Color ReadBaseColor(Material mat)
        {
            if (mat.HasProperty("_BaseColor")) return mat.GetColor("_BaseColor");
            if (mat.HasProperty("_Color")) return mat.GetColor("_Color");
            return Color.white;
        }

        public void SetOutlineMaterials(Material selected, Material hover)
        {
            if (selected != null) selectedOutlineMaterial = selected;
            if (hover != null) hoverOutlineMaterial = hover;
            ApplyOutline();
        }

        public void SetSelected(bool selected)
        {
            _selected = selected;
            ApplyOutline();
            UpdateKillRadiusVisibility();
            UpdatePickVolumeVisibility();
        }

        public void SetHovered(bool hovered)
        {
            _hovered = hovered;
            ApplyOutline();
            UpdatePickVolumeVisibility();
        }

        /// <summary>Kill sphere is one cue among several; CueOverlay owns the flag.</summary>
        public void SetKillCueEnabled(bool on)
        {
            _killCueOn = on;
            UpdateKillRadiusVisibility();
        }

        /// <summary>Selection cue. Hover is one of the checkboxes, not a free extra.</summary>
        public void SetPickVolumeCueEnabled(bool on, bool hover, bool selected, bool unselected)
        {
            _pickVolumeCueOn = on;
            _pickHover = hover;
            _pickSelected = selected;
            _pickUnselected = unselected;
            UpdatePickVolumeVisibility();
        }

        void UpdateKillRadiusVisibility()
        {
            if (_killRadiusGo == null) return;
            _killRadiusGo.SetActive(_killCueOn && _selected && Current.Alive);
        }

        void UpdatePickVolumeVisibility()
        {
            if (_pickVolumeGo == null) return;
            if (!_pickVolumeCueOn || !Current.Alive)
            {
                _pickVolumeGo.SetActive(false);
                return;
            }
            bool show = (_hovered && _pickHover)
                || (_selected && _pickSelected)
                || (!_selected && _pickUnselected);
            _pickVolumeGo.SetActive(show);
        }

        /// <summary>
        /// Selected material wins over hover. Neither: outline renderer off.
        /// Hover is independent of selection — both flags can be true at once.
        /// </summary>
        void ApplyOutline()
        {
            if (outlineRenderer == null && outlineObject != null)
                outlineRenderer = outlineObject.GetComponent<Renderer>();

            bool show = _selected || _hovered;
            if (outlineObject != null && outlineObject.activeSelf != show)
                outlineObject.SetActive(show);

            if (outlineRenderer == null) return;
            outlineRenderer.enabled = show;
            if (!show) return;

            if (_selected && selectedOutlineMaterial != null)
                outlineRenderer.sharedMaterial = selectedOutlineMaterial;
            else if (_hovered && hoverOutlineMaterial != null)
                outlineRenderer.sharedMaterial = hoverOutlineMaterial;
        }

        public static void RetintVolumes()
        {
            if (_killMat != null)
                Palette.TintVolume(_killMat, Palette.Kill);
            foreach (var kv in PickGhosts)
            {
                if (kv.Value != null)
                    MakeTransparentGhost(kv.Value, Palette.PickVolumeAlpha);
            }
        }

        /// <summary>Handy while debugging the coordinate conversion: if the blue ray
        /// does not point along the direction of travel (green line) in a straight transit, the
        /// quaternion conversion in the sidecar is wrong.</summary>
        void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying || !Current.Alive) return;
            Gizmos.color = Color.blue;
            Gizmos.DrawRay(transform.position, transform.forward * 5f);
            Gizmos.color = Color.green;
            Gizmos.DrawRay(transform.position, Current.Velocity);
        }
    }
}
