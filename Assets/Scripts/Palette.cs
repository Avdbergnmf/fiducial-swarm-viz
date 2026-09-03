// One colour, one meaning. 3D bodies, list dots, inspector chrome, overlay
// lines and the on-screen legend all read from here. View mode changes the
// *caption*, not the hue: blue is still "friendly / declared mate".
//
// Drop this on the Visualizer. Inspector fields override the compiled defaults
// so you can raise a volume's alpha or fresnel without editing code. Volume
// shells share one look (fill / rim / hex); cue colour alpha is separate.

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace SwarmViewer
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-50)]
    public sealed class Palette : MonoBehaviour
    {
        static Palette _live;

        [Header("Craft")]
        [SerializeField] Color friendly = Rgb(74, 158, 255);
        [SerializeField] Color hostile = Rgb(232, 72, 64);
        [SerializeField] Color civilian = Rgb(232, 197, 71);
        [SerializeField] Color wreckage = Rgb(138, 120, 100);
        [SerializeField] Color unknown = Rgb(140, 144, 152);
        [SerializeField] Color compromised = Rgb(224, 70, 200);

        [Header("Scene")]
        [SerializeField] Color asset = Rgb(216, 208, 196, 0.05f);
        [SerializeField] Color selected = Rgb(242, 246, 255);
        [SerializeField] Color hover = Rgb(186, 204, 230);

        [Header("Cues")]
        [SerializeField] Color kill = Rgb(232, 72, 64, 0.08f);
        [SerializeField] Color sense = Rgb(89, 217, 255);
        [SerializeField] Color comm = Rgb(184, 115, 255);
        [SerializeField] Color separate = Rgb(255, 140, 38);
        [SerializeField] Color velocity = Rgb(140, 240, 255);
        [SerializeField] Color accel = Rgb(180, 255, 74);
        [SerializeField] Color attitude = Rgb(115, 140, 255);
        [SerializeField] Color links = new Color(0.35f, 0.90f, 0.45f, 0.7f);
        [SerializeField] Color intercept = Rgb(255, 51, 46);
        [SerializeField] Color yieldColor = Rgb(255, 184, 46);
        [SerializeField] Color picket = Rgb(208, 208, 220);
        [SerializeField] [Tooltip("Hue plus peak alpha of the believed-aim sphere.")]
        Color aim = Rgb(255, 90, 210, 0.35f);

        [Header("Volume shells (kill / sense / aim / pick / asset)")]
        [SerializeField] [Range(0f, 0.4f)]
        [Tooltip("Interior wash. Independent of the colour's alpha.")]
        float fill = 0.04f;
        [SerializeField] [Range(0.5f, 12f)] float rimPower = 3f;
        [SerializeField] [Range(0f, 8f)] float rimBoost = 2f;
        [SerializeField] [Range(0.5f, 12f)] float hexScale = 3f;
        [SerializeField] [Range(0f, 0.4f)] float hexSpeed = 0.06f;
        [SerializeField] [Range(0.005f, 0.2f)] float hexThickness = 0.05f;
        [SerializeField] [Range(0f, 3f)] float hexIntensity = 0.5f;
        [SerializeField] [Range(0.5f, 1f)] float hexSpacing = 0.85f;
        [SerializeField] [Range(0.05f, 2f)] float intersectWidth = 0.45f;
        [SerializeField] [Range(0f, 8f)] float intersectBoost = 2.5f;
        [SerializeField] [Range(0.5f, 12f)] float intersectPower = 4f;

        [Header("Volume alpha")]
        [SerializeField] [Range(0.02f, 1f)]
        [Tooltip("Sense / comm / separate / picket spheres when Sphere is on.")]
        float radiusVolumeAlpha = 0.10f;
        [SerializeField] [Range(0.02f, 1f)]
        [Tooltip("Selection pick-volume ghosts.")]
        float pickVolumeAlpha = 0.12f;

        static readonly int FillId = Shader.PropertyToID("_Fill");
        static readonly int RimPowerId = Shader.PropertyToID("_RimPower");
        static readonly int RimBoostId = Shader.PropertyToID("_RimBoost");
        static readonly int HexScaleId = Shader.PropertyToID("_HexScale");
        static readonly int HexSpeedId = Shader.PropertyToID("_HexSpeed");
        static readonly int HexThicknessId = Shader.PropertyToID("_HexThickness");
        static readonly int HexIntensityId = Shader.PropertyToID("_HexIntensity");
        static readonly int HexSpacingId = Shader.PropertyToID("_HexSpacing");
        static readonly int IntersectWidthId = Shader.PropertyToID("_IntersectWidth");
        static readonly int IntersectBoostId = Shader.PropertyToID("_IntersectBoost");
        static readonly int IntersectPowerId = Shader.PropertyToID("_IntersectPower");

        public static Color Friendly => _live != null ? _live.friendly : Rgb(74, 158, 255);
        public static Color Hostile => _live != null ? _live.hostile : Rgb(232, 72, 64);
        public static Color Civilian => _live != null ? _live.civilian : Rgb(232, 197, 71);
        public static Color Wreckage => _live != null ? _live.wreckage : Rgb(138, 120, 100);
        public static Color Unknown => _live != null ? _live.unknown : Rgb(140, 144, 152);
        public static Color Compromised => _live != null ? _live.compromised : Rgb(224, 70, 200);
        public static Color Asset => _live != null ? _live.asset : Rgb(216, 208, 196, 0.05f);
        public static Color Selected => _live != null ? _live.selected : Rgb(242, 246, 255);
        public static Color Hover => _live != null ? _live.hover : Rgb(186, 204, 230);
        public static Color Kill => _live != null ? _live.kill : Rgb(232, 72, 64, 0.08f);
        public static Color Sense => _live != null ? _live.sense : Rgb(89, 217, 255);
        public static Color Comm => _live != null ? _live.comm : Rgb(184, 115, 255);
        public static Color Separate => _live != null ? _live.separate : Rgb(255, 140, 38);
        public static Color Velocity => _live != null ? _live.velocity : Rgb(140, 240, 255);
        public static Color Accel => _live != null ? _live.accel : Rgb(180, 255, 74);
        public static Color Attitude => _live != null ? _live.attitude : Rgb(115, 140, 255);
        public static Color Links => _live != null ? _live.links : new Color(0.35f, 0.90f, 0.45f, 0.7f);
        public static Color Intercept => _live != null ? _live.intercept : Rgb(255, 51, 46);
        public static Color Yield => _live != null ? _live.yieldColor : Rgb(255, 184, 46);
        public static Color Picket => _live != null ? _live.picket : Rgb(208, 208, 220);
        public static Color Aim => _live != null ? _live.aim : Rgb(255, 90, 210, 0.35f);

        public static float RadiusVolumeAlpha => _live != null ? _live.radiusVolumeAlpha : 0.10f;
        public static float PickVolumeAlpha => _live != null ? _live.pickVolumeAlpha : 0.12f;

        void OnEnable()
        {
            _live = this;
            Push();
        }

        void OnDisable()
        {
            if (_live == this) _live = null;
        }

        void OnValidate()
        {
            if (isActiveAndEnabled) Push();
        }

        void Push()
        {
            var cues = GetComponent<CueOverlay>();
            if (cues != null) cues.Restyle();
            else
            {
                var found = FindObjectsByType<CueOverlay>(FindObjectsInactive.Include);
                for (int i = 0; i < found.Length; i++)
                    found[i].Restyle();
            }

            EntityView.RetintVolumes();

            var env = GetComponent<EnvironmentView>();
            if (env != null) env.Retint();
            else
            {
                var found = FindObjectsByType<EnvironmentView>(FindObjectsInactive.Include);
                for (int i = 0; i < found.Length; i++)
                    found[i].Retint();
            }
        }

        public static Color Kind(EntityKind kind) => kind switch
        {
            EntityKind.Friendly => Friendly,
            EntityKind.Hostile => Hostile,
            EntityKind.Civilian => Civilian,
            EntityKind.Wreckage => Wreckage,
            _ => Unknown,
        };

        public static Color Belief(BeliefClass cls) => cls switch
        {
            BeliefClass.Friendly => Friendly,
            BeliefClass.Enemy => Hostile,
            BeliefClass.Neutral => Civilian,
            BeliefClass.Compromised => Compromised,
            _ => Unknown,
        };

        public static Color Cue(CueMask bit) => bit switch
        {
            CueMask.Kill => Kill,
            CueMask.Sense => Sense,
            CueMask.Comm => Comm,
            CueMask.Separate => Separate,
            CueMask.Velocity => Velocity,
            CueMask.Accel => Accel,
            CueMask.Attitude => Attitude,
            CueMask.Links => Links,
            CueMask.Hops => Hop(2),
            CueMask.Picket => Picket,
            CueMask.Intercept => Intercept,
            CueMask.Yield => Yield,
            CueMask.Pings => Rgb(255, 255, 255),
            CueMask.Selection => Hover,
            CueMask.Aim => Aim,
            _ => Color.white,
        };

        public static Color Hop(int hops) => hops switch
        {
            <= 1 => Links,
            2 => Yield,
            3 => Separate,
            _ => Compromised,
        };

        public static Color Ping(RelationKind kind) => kind switch
        {
            RelationKind.Call => Separate,
            RelationKind.Drop => Unknown,
            RelationKind.Wreck => Wreckage,
            RelationKind.Near => Velocity,
            RelationKind.Ram => Intercept,
            RelationKind.Duplicate => Compromised,
            RelationKind.Gone => Comm,
            RelationKind.Live => Links,
            _ => Color.white,
        };

        /// <summary>Score-ledger kinds. Same hues as the matching craft / cue.</summary>
        public static Color ScoreKind(string kind) => kind switch
        {
            "intercept" => Links,
            "breach" => Hostile,
            "civilian_lost" => Civilian,
            "friendly_lost" => Friendly,
            "awareness" => Sense,
            "comms" => Comm,
            "detection" => Compromised,
            _ => Unknown,
        };

        /// <summary>Body colour in the current view. Compromised wins in ground truth.</summary>
        public static Color Body(ViewMode mode, EntityKind kind, BeliefClass declared, bool compromised, bool isObserver)
        {
            if (mode == ViewMode.FleetBelief)
                return isObserver ? Friendly : Belief(declared);
            if (compromised) return Compromised;
            return Kind(kind);
        }

        public static Color Rgb(int r, int g, int b, float a = 1f) =>
            new Color(r / 255f, g / 255f, b / 255f, a);

        public static Color Gray(Color c)
        {
            float g = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
            return new Color(g, g, g, c.a);
        }

        public static Color A(Color c, float a)
        {
            c.a = a;
            return c;
        }

        public static Color Opaque(Color c)
        {
            c.a = 1f;
            return c;
        }

        public static void Tint(Material mat, Color color)
        {
            if (mat == null) return;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        }

        /// <summary>
        /// Ghost volumes (kill sphere, asset cylinder, cue shells). Colour alpha
        /// scales the whole look; Fill / Rim / Hex come from this component.
        /// </summary>
        public static void TintVolume(Material mat, Color color)
        {
            if (mat == null) return;
            MakeTransparent(mat);
            Tint(mat, color);
            ApplyVolumeLook(mat);
        }

        public static void ApplyVolumeLook(Material mat)
        {
            if (mat == null || !mat.HasProperty(FillId)) return;
            var p = _live;
            mat.SetFloat(FillId, p != null ? p.fill : 0.04f);
            mat.SetFloat(RimPowerId, p != null ? p.rimPower : 3f);
            mat.SetFloat(RimBoostId, p != null ? p.rimBoost : 2f);
            mat.SetFloat(HexScaleId, p != null ? p.hexScale : 3f);
            mat.SetFloat(HexSpeedId, p != null ? p.hexSpeed : 0.06f);
            mat.SetFloat(HexThicknessId, p != null ? p.hexThickness : 0.05f);
            mat.SetFloat(HexIntensityId, p != null ? p.hexIntensity : 0.5f);
            mat.SetFloat(HexSpacingId, p != null ? p.hexSpacing : 0.85f);
            mat.SetFloat(IntersectWidthId, p != null ? p.intersectWidth : 0.45f);
            mat.SetFloat(IntersectBoostId, p != null ? p.intersectBoost : 2.5f);
            mat.SetFloat(IntersectPowerId, p != null ? p.intersectPower : 4f);
        }

        public static void MakeTransparent(Material mat)
        {
            if (mat == null) return;
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);
            if (mat.HasProperty("_BlendModePreserveSpecular"))
                mat.SetFloat("_BlendModePreserveSpecular", 0f);
            if (mat.HasProperty("_SrcBlend"))
                mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend"))
                mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            if (mat.HasProperty("_SrcBlendAlpha"))
                mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlendAlpha"))
                mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
            if (mat.HasProperty("_ReceiveShadows")) mat.SetFloat("_ReceiveShadows", 0f);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = 3000;
            mat.SetShaderPassEnabled("ShadowCaster", false);
            mat.SetShaderPassEnabled("DepthOnly", false);
        }

        public static void Fill(VisualElement el, Color color)
        {
            if (el == null) return;
            el.style.backgroundColor = Opaque(color);
        }

        public static void Border(VisualElement el, Color color, float width = 3f)
        {
            if (el == null) return;
            var c = Opaque(color);
            el.style.borderLeftColor = c;
            el.style.borderLeftWidth = width;
        }
    }
}
