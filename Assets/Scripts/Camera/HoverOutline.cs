// Outline visuals live on EntityView (selected vs hover materials). This component
// stays as an IRunView so existing scene references do not break; hover is driven
// by EntityPicker calling EntityView.SetHovered directly.

using UnityEngine;

namespace SwarmViewer
{
    public sealed class HoverOutline : MonoBehaviour, IRunView
    {
        public void Bind(ViewerContext ctx) { }

        public void SetEntityHovered(EntityView view, bool hovered)
        {
            if (view == null) return;
            view.SetHovered(hovered);
        }
    }
}
