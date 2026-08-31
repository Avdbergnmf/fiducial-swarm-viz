// Persistent viewer settings stored next to run fixtures in StreamingAssets.

using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace SwarmViewer
{
    [Serializable]
    public sealed class ViewerSettings
    {
        public string extraRunFolder = "";
        public string lastRunStem = "";

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
