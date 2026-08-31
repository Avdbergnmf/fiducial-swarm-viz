// Visual representation of a single entity airframe in the scene.
// Manages position, rotation, class material, trail, outline, and kill-radius marker.
// Selection never touches the entity's own material — only the outline child.

using UnityEngine;

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
        bool _selected;
        bool _hovered;
        GameObject _killRadiusGo;

        public bool IsSelected => _selected;
        public bool IsHovered => _hovered;

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
        /// Parents a pre-instantiated kill-radius sphere. Prefab is assumed 1 m in
        /// diameter (Unity default sphere); we scale to 2 * radius.
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
            foreach (var col in _killRadiusGo.GetComponentsInChildren<Collider>())
                col.enabled = false;
            _killRadiusGo.SetActive(false);
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
        }

        /// <summary>Class / belief colour only. Never used for selection.</summary>
        public void SetMaterial(Material mat)
        {
            if (_currentMaterial == mat || mat == null) return;
            _currentMaterial = mat;

            if (mainRenderer != null)
                mainRenderer.sharedMaterial = mat;

            if (trail != null && mat.HasProperty("_BaseColor"))
            {
                Color c = mat.GetColor("_BaseColor");
                trail.startColor = new Color(c.r, c.g, c.b, 0.85f);
                trail.endColor = new Color(c.r, c.g, c.b, 0f);
            }
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
        }

        public void SetHovered(bool hovered)
        {
            _hovered = hovered;
            ApplyOutline();
        }

        void UpdateKillRadiusVisibility()
        {
            if (_killRadiusGo == null) return;
            _killRadiusGo.SetActive(_selected && Current.Alive);
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
