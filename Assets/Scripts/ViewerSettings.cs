// Persistent viewer settings stored next to run fixtures in StreamingAssets.
// Read and written by Newtonsoft, never by Unity's serializer, which is why this
// class is free to hold a dictionary and carries no [Serializable].

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace SwarmViewer
{
    public sealed class ViewerSettings
    {
        public string extraRunFolder = "";
        public string lastRunStem = "";
        public float uiScale; // 0 = unset (older files); ResolvedUiScale() supplies the default
        public int cueMask;   // 0 = default (Kill | Velocity); otherwise CueMask flags

        /// <summary>
        /// Where each floating window was left, keyed by its UXML name and packed
        /// as "left,top,width,height" in rounded points. A packed string rather
        /// than a nested object so one window stays one readable line in the file.
        /// </summary>
        public Dictionary<string, string> panelRects = new();
        public bool legendCollapsed;
        /// <summary>Legacy: when the old Ghosts cue was on, draw on every living craft.</summary>
        public bool pickVolumesAll;
        /// <summary>Legacy scale-bar master switch. Migrated into cueMask + pickVolumeShow, then cleared.</summary>
        public bool pickVolumesOn;
        /// <summary>
        /// Selection cue: which crafts get the pick-volume sphere.
        /// CueOverlay.PickHover / PickSelected / PickUnselected. Null = not yet migrated.
        /// </summary>
        public int? pickVolumeShow;
        /// <summary>
        /// Radius cues that also draw a sphere. Null means the default (Kill only,
        /// matching the old always-on kill mesh). 0 is a real choice: rings only.
        /// </summary>
        public int? cueSphereMask;
        /// <summary>J / L and the ± buttons. 0 means the default 5 s.</summary>
        public float playbackStepSeconds;

        public const float UiScaleMin = 0.8f;
        public const float UiScaleMax = 2.0f;
        public const float UiScaleDefault = 1.25f;
        public const float UiScaleStep = 0.1f;

        public float ResolvedUiScale()
        {
            float s = uiScale > 0.01f ? uiScale : UiScaleDefault;
            return Mathf.Clamp(s, UiScaleMin, UiScaleMax);
        }

        public bool TryGetPanelRect(string key, out Rect rect)
        {
            rect = default;
            if (string.IsNullOrEmpty(key) || panelRects == null) return false;
            if (!panelRects.TryGetValue(key, out string packed) || packed == null) return false;

            var parts = packed.Split(',');
            if (parts.Length != 4) return false;
            var f = new float[4];
            for (int i = 0; i < 4; i++)
                if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out f[i]))
                    return false;

            rect = new Rect(f[0], f[1], f[2], f[3]);
            return true;
        }

        /// <summary>Writes through to disk, and does nothing if the geometry is unchanged.</summary>
        public void SetPanelRect(string key, Rect rect)
        {
            if (string.IsNullOrEmpty(key)) return;
            panelRects ??= new Dictionary<string, string>();

            string packed = string.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3}",
                Mathf.RoundToInt(rect.x), Mathf.RoundToInt(rect.y),
                Mathf.RoundToInt(rect.width), Mathf.RoundToInt(rect.height));

            if (panelRects.TryGetValue(key, out string existing) && existing == packed) return;
            panelRects[key] = packed;
            Save();
        }

        const string FileName = "viewer.settings.json";

        static ViewerSettings _cached;

        public static string FilePath => Path.Combine(Application.streamingAssetsPath, FileName);

        public static ViewerSettings Load()
        {
            if (_cached != null) return _cached;

            try
            {
                if (File.Exists(FilePath))
                    _cached = JsonConvert.DeserializeObject<ViewerSettings>(File.ReadAllText(FilePath));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[viewer] Could not load {FileName}: {e.Message}");
            }

            _cached ??= new ViewerSettings();
            return _cached;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Application.streamingAssetsPath);
                File.WriteAllText(FilePath, JsonConvert.SerializeObject(this, Formatting.Indented));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[viewer] Could not save {FileName}: {e.Message}");
            }
        }
    }
}
