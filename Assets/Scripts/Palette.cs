// One colour, one meaning. 3D bodies, list dots, inspector chrome, overlay
// lines and the on-screen legend all read from here. View mode changes the
// *caption*, not the hue: blue is still "friendly / declared mate".

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace SwarmViewer
{
    public static class Palette
    {
        public static readonly Color Friendly = Rgb(74, 158, 255);
        public static readonly Color Hostile = Rgb(232, 72, 64);
        public static readonly Color Civilian = Rgb(232, 197, 71);
        public static readonly Color Wreckage = Rgb(138, 120, 100);
        public static readonly Color Unknown = Rgb(140, 144, 152);
        public static readonly Color Compromised = Rgb(224, 70, 200);

        public static readonly Color Asset = Rgb(216, 208, 196, 0.05f);
        public static readonly Color Selected = Rgb(242, 246, 255);
        public static readonly Color Hover = Rgb(186, 204, 230);

        public static readonly Color Kill = Rgb(232, 72, 64, 0.08f);
        public static readonly Color Sense = Rgb(89, 217, 255);
        public static readonly Color Comm = Rgb(184, 115, 255);
        public static readonly Color Separate = Rgb(255, 140, 38);
        public static readonly Color Velocity = Rgb(140, 240, 255);
        public static readonly Color Accel = Rgb(180, 255, 74);
        public static readonly Color Attitude = Rgb(115, 140, 255);
        public static readonly Color Links = new Color(0.35f, 0.90f, 0.45f, 0.7f);
        public static readonly Color Intercept = Rgb(255, 51, 46);
        public static readonly Color Yield = Rgb(255, 184, 46);
        public static readonly Color Picket = Rgb(208, 208, 220);
        public static readonly Color Aim = Rgb(255, 90, 210);

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
            CueMask.Ghosts => Hover,
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
        /// Ghost volumes (kill sphere, asset cylinder). Colour alpha is the fill;
        /// URP Lit ignores that alpha unless the surface is actually Transparent.
        /// </summary>
        public static void TintVolume(Material mat, Color color)
        {
            if (mat == null) return;
            MakeTransparent(mat);
            Tint(mat, color);
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
