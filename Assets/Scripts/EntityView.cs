// Visual representation of a single entity airframe in the scene.
// Manages position, rotation, renderer material properties, and flight trail.

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

        [SerializeField] Renderer[] renderers;
        [SerializeField] TrailRenderer trail;

        MaterialPropertyBlock _block;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        Color _color = Color.clear;

        public void Init(int slot, EntityInfo info)
        {
            Slot = slot;
            Info = info;
            name = $"{slot:D2} {info.Label}";
            if (renderers == null || renderers.Length == 0)
                renderers = GetComponentsInChildren<Renderer>();

            if (trail == null)
                trail = GetComponentInChildren<TrailRenderer>();

            _block = new MaterialPropertyBlock();
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
        }

        public void SetColor(Color c)
        {
            if (_color == c) return;
            _color = c;
            _block.SetColor(BaseColorId, c);
            _block.SetColor(ColorId, c);
            if (renderers != null)
            {
                foreach (var r in renderers)
                {
                    if (r != null) r.SetPropertyBlock(_block);
                }
            }
            if (trail != null)
            {
                trail.startColor = new Color(c.r, c.g, c.b, 0.85f);
                trail.endColor = new Color(c.r, c.g, c.b, 0f);
            }
        }

        /// <summary>Handy while debugging the coordinate conversion: if the blue ray
        /// does not point along the direction of travel in a straight transit, the
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

