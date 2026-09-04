// Colour-key widgets shared by the on-screen legend and the Cues panel.
// Hues come from Palette; the cue list, ping kinds and hop steps come from
// CueOverlay. A new cue or ping colour is one edit there, not two UIs.

using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace SwarmViewer
{
    public static class CueLegend
    {
        public static VisualElement Swatch(Color color)
        {
            var dot = new VisualElement();
            dot.AddToClassList("legend-swatch");
            dot.pickingMode = PickingMode.Ignore;
            Palette.Fill(dot, color);
            return dot;
        }

        /// <param name="cues">When set, only enabled kinds are shown (legend).</param>
        public static void AddPingKey(VisualElement parent, bool labeled, CueOverlay cues = null) =>
            AddKey(parent, labeled, CueOverlay.PingKinds,
                p => Palette.Ping(p.Kind), p => p.Label,
                p => cues == null || cues.PingKindOn(p.Kind));

        public static void AddHopKey(VisualElement parent, bool labeled) =>
            AddKey(parent, labeled, CueOverlay.HopSteps, h => Palette.Hop(h.Hops), h => h.Label);

        public static void AddCoverKey(VisualElement parent, bool labeled = true)
        {
            if (parent == null) return;
            if (!labeled)
            {
                var row = new VisualElement();
                row.AddToClassList("legend-row");
                row.pickingMode = PickingMode.Ignore;
                var dots = new VisualElement();
                dots.AddToClassList("legend-ping-dots");
                dots.pickingMode = PickingMode.Ignore;
                var a = Swatch(Palette.Opaque(Palette.CoverSafe));
                a.tooltip = "covered";
                var b = Swatch(Palette.Opaque(Palette.CoverGap));
                b.tooltip = "hole";
                dots.Add(a);
                dots.Add(b);
                row.Add(dots);
                parent.Add(row);
                return;
            }
            parent.AddToClassList("cue-detail-key");
            AddCoverItem(parent, Palette.CoverSafe, "covered");
            AddCoverItem(parent, Palette.CoverGap, "hole");
        }

        static void AddCoverItem(VisualElement parent, Color color, string name)
        {
            var item = new VisualElement();
            item.AddToClassList("cue-detail-key-item");
            item.pickingMode = PickingMode.Ignore;
            item.Add(Swatch(Palette.Opaque(color)));
            var label = new Label(name);
            label.AddToClassList("cue-detail-key-name");
            label.pickingMode = PickingMode.Ignore;
            item.Add(label);
            parent.Add(item);
        }

        public static string DrawnAs(CueOverlay.CueSpec spec)
        {
            if (string.IsNullOrEmpty(spec.Shape)) return "";
            bool an = spec.Shape.Length > 0 && "aeiou".IndexOf(char.ToLowerInvariant(spec.Shape[0])) >= 0;
            return $"Drawn as {(an ? "an" : "a")} {spec.Shape}.";
        }

        static void AddKey<T>(
            VisualElement parent,
            bool labeled,
            T[] items,
            Func<T, Color> colorOf,
            Func<T, string> labelOf,
            Func<T, bool> include = null)
        {
            if (parent == null || items == null || items.Length == 0) return;

            if (!labeled)
            {
                var row = new VisualElement();
                row.AddToClassList("legend-row");
                row.pickingMode = PickingMode.Ignore;
                var dots = new VisualElement();
                dots.AddToClassList("legend-ping-dots");
                dots.pickingMode = PickingMode.Ignore;
                for (int i = 0; i < items.Length; i++)
                {
                    if (include != null && !include(items[i])) continue;
                    var dot = Swatch(colorOf(items[i]));
                    dot.tooltip = labelOf(items[i]);
                    dots.Add(dot);
                }
                if (dots.childCount == 0) return;
                row.Add(dots);
                parent.Add(row);
                return;
            }

            parent.AddToClassList("cue-detail-key");
            for (int i = 0; i < items.Length; i++)
            {
                if (include != null && !include(items[i])) continue;
                var item = new VisualElement();
                item.AddToClassList("cue-detail-key-item");
                item.pickingMode = PickingMode.Ignore;
                item.Add(Swatch(colorOf(items[i])));
                var name = new Label(labelOf(items[i]));
                name.AddToClassList("cue-detail-key-name");
                name.pickingMode = PickingMode.Ignore;
                item.Add(name);
                parent.Add(item);
            }
        }
    }
}
