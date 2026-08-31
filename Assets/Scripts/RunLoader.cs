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
    /// Load constructs; <see cref="RunValidator"/> checks. This class does not
    /// throw on validation Errors — the caller decides.
    /// </summary>
    public static class RunLoader
    {
        /// <param name="prefix">Path or stem without extension, e.g. "fixture".</param>
        public static RunData Load(string prefix) =>
            Load(prefix, default, out _);

        public static RunData Load(string prefix, RunExpectations expectations, out ValidationResult validation) =>
            Load(prefix, expectations, out validation, log: true);

        public static RunData Load(string prefix, RunExpectations expectations, out ValidationResult validation, bool log)
        {
            string metaPath = prefix + ".meta.json";
            string binPath = prefix + ".bin";

            if (!File.Exists(metaPath))
                throw new FileNotFoundException($"[viewer] Meta JSON file not found at: {metaPath}");
            if (!File.Exists(binPath))
                throw new FileNotFoundException($"[viewer] Binary trajectory file not found at: {binPath}");

            // Load meta file (log lines, events, links, beliefs)
            var meta = JsonConvert.DeserializeObject<RunMeta>(File.ReadAllText(metaPath));
            if (meta == null) throw new InvalidDataException("[viewer] could not parse " + metaPath);

            byte[] raw = File.ReadAllBytes(binPath);
            int expected = meta.frame_count * meta.slot_count * meta.stride * sizeof(float);
            if (raw.Length != expected)
                throw new InvalidDataException(
                    $"[viewer] {binPath}: {raw.Length} bytes but meta implies {expected}. " +
                    "The two files are out of sync — run scripts/sync_viewer_data.ps1 after the sidecar.");

            // Load binary trajectory data into float array. Each frame has slot_count * stride floats. (eleven floats total w/ positions, rotations, velocities)
            var geometry = new float[raw.Length / sizeof(float)]; 
            Buffer.BlockCopy(raw, 0, geometry, 0, raw.Length);

            // instantiate RunData with meta and geometry
            var runData = new RunData(meta, geometry);

            if (log)
            {
                string generated = meta.provenance != null ? meta.provenance.generated_at : "(no provenance)";
                Debug.Log($"[viewer] loaded {prefix}: {meta.frame_count} frames, {meta.slot_count} slots, " +
                          $"{meta.duration:F1}s, bin {raw.Length} bytes, generated {generated}");
            }

            validation = RunValidator.Validate(runData, expectations);
            if (log)
                RunValidator.Log(validation);
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
