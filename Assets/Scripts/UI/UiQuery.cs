// Shared UI Toolkit helpers: loud name lookups and a tree dump for the context menu.

using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace SwarmViewer
{
    public static class UiQuery
    {
        public static T Named<T>(VisualElement root, string name) where T : VisualElement
        {
            if (root == null)
            {
                Debug.LogWarning($"[viewer] UI query '{name}' skipped: root VisualElement is null.");
                return null;
            }

            var el = root.Q<T>(name);
            if (el == null)
                Debug.LogWarning($"[viewer] UI element '{name}' ({typeof(T).Name}) not found. UXML name does not match the code.");
            return el;
        }

        /// <summary>
        /// UITK Buttons put a TextElement over the chrome, so :hover on the
        /// Button never matches. Ignore-picking the children makes the Button
        /// itself the hit target. Call on each new Button; <see cref="ButtonsReceiveHover"/>
        /// covers a whole UXML tree.
        /// </summary>
        public static void HitSelf(Button btn)
        {
            if (btn == null) return;
            for (int i = 0; i < btn.childCount; i++)
                btn[i].pickingMode = PickingMode.Ignore;
        }

        public static void ButtonsReceiveHover(VisualElement root)
        {
            if (root == null) return;
            if (root is Button btn)
                HitSelf(btn);
            int n = root.childCount;
            for (int i = 0; i < n; i++)
                ButtonsReceiveHover(root[i]);
        }

        public static void DumpTree(VisualElement root)
        {
            if (root == null)
            {
                Debug.LogWarning("[viewer] UI tree dump: root is null (UIDocument not ready).");
                return;
            }

            var sb = new StringBuilder(2048);
            sb.AppendLine("[viewer] UI element tree:");
            Append(root, sb, 0);
            Debug.Log(sb.ToString());
        }

        static void Append(VisualElement el, StringBuilder sb, int depth)
        {
            sb.Append(' ', depth * 2);
            string name = string.IsNullOrEmpty(el.name) ? "(unnamed)" : el.name;
            var bound = el.worldBound;
            sb.Append(el.GetType().Name)
                .Append(" '").Append(name).Append("'")
                .Append(" pick=").Append(el.pickingMode)
                .Append(" display=").Append(el.resolvedStyle.display)
                .Append(" world=(")
                .Append(bound.x.ToString("F0")).Append(',')
                .Append(bound.y.ToString("F0")).Append(' ')
                .Append(bound.width.ToString("F0")).Append('x')
                .Append(bound.height.ToString("F0")).Append(')')
                .AppendLine();

            int n = el.childCount;
            for (int i = 0; i < n; i++)
                Append(el[i], sb, depth + 1);
        }
    }
}
