// On-the-fly UI scale. PanelSettings is Scale With Screen Size, so
// PanelSettings.scale is not the live multiplier — shrinking the reference
// resolution is. We mutate the live asset during Play and restore it on disable
// so the file is not left dirtied. Persisted as viewer.settings.json uiScale.

using UnityEngine;
using UnityEngine.UIElements;

namespace SwarmViewer
{
    public sealed class UiScaleView : MonoBehaviour
    {
        [SerializeField] UIDocument uiDocument;

        Button _downBtn;
        Button _upBtn;
        Label _label;
        bool _wired;

        Vector2Int _baseRefRes;
        float _baseScale = 1f;
        bool _capturedBase;

        void OnEnable()
        {
            TryWire();
            Apply(ViewerSettings.Load().ResolvedUiScale(), save: false);
        }

        void Start()
        {
            TryWire();
            Apply(ViewerSettings.Load().ResolvedUiScale(), save: false);
        }

        void LateUpdate()
        {
            if (!_wired)
            {
                TryWire();
                if (_wired)
                    Apply(ViewerSettings.Load().ResolvedUiScale(), save: false);
            }
        }

        void OnDisable()
        {
            RestorePanel();
        }

        void TryWire()
        {
            if (_wired) return;
            if (uiDocument == null)
                uiDocument = GetComponent<UIDocument>();
            if (uiDocument == null) return;

            var root = uiDocument.rootVisualElement;
            if (root == null) return;

            _downBtn = UiQuery.Named<Button>(root, "uiScaleDownBtn");
            _upBtn = UiQuery.Named<Button>(root, "uiScaleUpBtn");
            _label = UiQuery.Named<Label>(root, "uiScaleLabel");
            if (_downBtn == null || _upBtn == null) return;

            _downBtn.RegisterCallback<ClickEvent>(_ => Nudge(-ViewerSettings.UiScaleStep));
            _upBtn.RegisterCallback<ClickEvent>(_ => Nudge(ViewerSettings.UiScaleStep));
            UiQuery.ButtonsReceiveHover(root);
            _wired = true;
        }

        void Nudge(float delta)
        {
            float current = ViewerSettings.Load().ResolvedUiScale();
            float next = Mathf.Clamp(
                Mathf.Round((current + delta) / ViewerSettings.UiScaleStep) * ViewerSettings.UiScaleStep,
                ViewerSettings.UiScaleMin,
                ViewerSettings.UiScaleMax);
            Apply(next, save: true);
        }

        void Apply(float scale, bool save)
        {
            ApplyToLivePanel(scale);

            if (_label != null)
                _label.text = Mathf.RoundToInt(scale * 100f) + "%";

            if (_downBtn != null)
                _downBtn.SetEnabled(scale > ViewerSettings.UiScaleMin + 0.001f);
            if (_upBtn != null)
                _upBtn.SetEnabled(scale < ViewerSettings.UiScaleMax - 0.001f);

            var settings = ViewerSettings.Load();
            if (!Mathf.Approximately(settings.uiScale, scale))
            {
                settings.uiScale = scale;
                if (save)
                    settings.Save();
            }

            // Scale With Screen Size shrinks the panel's coordinate space.
            // Floating left/top are in that space; reflow after layout so
            // windows stay in the viewport instead of walking off the right.
            var root = uiDocument != null ? uiDocument.rootVisualElement : null;
            if (root != null)
            {
                root.schedule.Execute(FloatingPanel.ReflowAll);
                root.schedule.Execute(FloatingPanel.ReflowAll).ExecuteLater(16);
            }
        }

        void ApplyToLivePanel(float userScale)
        {
            if (uiDocument == null) return;
            var ps = uiDocument.panelSettings;
            if (ps == null) return;

            if (!_capturedBase)
            {
                _baseRefRes = ps.referenceResolution;
                if (_baseRefRes.x < 8) _baseRefRes = new Vector2Int(1920, 1080);
                _baseScale = ps.scale > 0.01f ? ps.scale : 1f;
                _capturedBase = true;
            }

            // Scale With Screen Size computes size from referenceResolution.
            // Smaller reference → larger UI. Constant Pixel Size uses .scale.
            if (ps.scaleMode == PanelScaleMode.ScaleWithScreenSize)
            {
                ps.referenceResolution = new Vector2Int(
                    Mathf.Max(320, Mathf.RoundToInt(_baseRefRes.x / userScale)),
                    Mathf.Max(180, Mathf.RoundToInt(_baseRefRes.y / userScale)));
                ps.scale = _baseScale;
            }
            else
            {
                ps.scale = _baseScale * userScale;
            }
        }

        void RestorePanel()
        {
            if (!_capturedBase || uiDocument == null) return;
            var ps = uiDocument.panelSettings;
            if (ps == null) return;
            ps.referenceResolution = _baseRefRes;
            ps.scale = _baseScale;
        }
    }
}
