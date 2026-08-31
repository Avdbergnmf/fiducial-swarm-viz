// Reads binary trajectory data and JSON metadata from disk into immutable RunData.
// Resolves paths relative to StreamingAssets or absolute paths, and validates contract integrity.

using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace SwarmViewer
{
    /// <summary>
    /// Reads a run from disk. Needs com.unity.nuget.newtonsoft-json from the
    /// Package Manager -- JsonUtility cannot handle the nested meta file.
    /// </summary>
    public static class RunLoader
    {
        /// <param name="prefix">Path or stem without extension, e.g. "fixture".</param>
        public static RunData Load(string prefix)
        {
            string metaPath = prefix + ".meta.json";
            string binPath = prefix + ".bin";

            if (!File.Exists(metaPath))
                throw new FileNotFoundException($"[viewer] Meta JSON file not found at: {metaPath}");
            if (!File.Exists(binPath))
                throw new FileNotFoundException($"[viewer] Binary trajectory file not found at: {binPath}");

            var meta = JsonConvert.DeserializeObject<RunMeta>(File.ReadAllText(metaPath));
            if (meta == null) throw new InvalidDataException("[viewer] could not parse " + metaPath);

            byte[] raw = File.ReadAllBytes(binPath);
            int expected = meta.frame_count * meta.slot_count * meta.stride * sizeof(float);
            if (raw.Length != expected)
                throw new InvalidDataException(
                    $"[viewer] {binPath}: {raw.Length} bytes but meta implies {expected}. " +
                    "The two files are out of sync -- rerun the sidecar.");

            var geo = new float[raw.Length / sizeof(float)];
            Buffer.BlockCopy(raw, 0, geo, 0, raw.Length);

            var runData = new RunData(meta, geo);

            Debug.Log($"[viewer] Resolved meta path: {metaPath}, bin path: {binPath}");
            Debug.Log($"[viewer] fixture: {meta.frame_count} frames, {meta.slot_count} slots, {meta.duration:F1}s, bin {raw.Length} bytes");

            // Validate the first alive friendly position
            int friendlySlot = -1;
            for (int i = 0; i < meta.entities.Count; i++)
            {
                if (meta.entities[i].drone_id >= 0 || meta.entities[i].Kind == EntityKind.Friendly)
                {
                    friendlySlot = meta.entities[i].slot;
                    break;
                }
            }

            if (friendlySlot >= 0 && runData.Sample(0f, friendlySlot, out var p, out _, out _))
            {
                float horizRadius = Mathf.Sqrt(p.x * p.x + p.z * p.z);
                Debug.Log($"[viewer] friendly slot {friendlySlot} p=({p.x:F2}, {p.y:F2}, {p.z:F2})  horizRadius={horizRadius:F2}");

                if (p.y < 0f)
                {
                    throw new InvalidOperationException(
                        $"[viewer] FATAL: Friendly slot {friendlySlot} has negative altitude y={p.y:F2}. " +
                        "Double NED conversion detected!");
                }

                if (Mathf.Abs(p.y - 30f) > 15f)
                {
                    throw new InvalidOperationException(
                        $"[viewer] FATAL: Friendly slot {friendlySlot} expected y ≈ 30, got {p.y:F2}. Coordinate parse/path is wrong!");
                }

                if (Mathf.Abs(horizRadius - 60f) > 20f)
                {
                    throw new InvalidOperationException(
                        $"[viewer] FATAL: Friendly slot {friendlySlot} expected horizRadius ≈ 60, got {horizRadius:F2}. Coordinate parse/path is wrong!");
                }
            }

            return runData;
        }

        /// <summary>True when both the meta JSON and trajectory bin exist for this stem.</summary>
        public static bool Exists(string prefix)
        {
            if (string.IsNullOrEmpty(prefix)) return false;
            string resolved = Resolve(prefix);
            return File.Exists(resolved + ".meta.json") && File.Exists(resolved + ".bin");
        }

        /// <summary>Resolves path stem. Checks StreamingAssets first, then project-relative or absolute path.</summary>
        public static string Resolve(string prefix)
        {
            if (string.IsNullOrEmpty(prefix)) prefix = "fixture";
            if (Path.IsPathRooted(prefix)) return prefix;

            // 1. Check StreamingAssets path
            string streaming = Path.Combine(Application.streamingAssetsPath, prefix);
            if (File.Exists(streaming + ".meta.json")) return streaming;

            // 2. Check relative to project root
            string beside = Path.GetFullPath(Path.Combine(Application.dataPath, "..", prefix));
            if (File.Exists(beside + ".meta.json")) return beside;

            // 3. Fallback to streamingAssets path
            return streaming;
        }
    }
}

