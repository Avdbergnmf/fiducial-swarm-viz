// Load-time sanity checks for a converted run. Does not throw: one pass, every
// check recorded (pass, skip, warning, or error). RunLoader logs non-pass
// lines; VisualizerRoot decides whether Errors abort the load.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace SwarmViewer
{
    public enum Severity { Info, Warning, Error, Pass }

    public sealed class Issue
    {
        public Severity Severity;
        public string Check;
        public string Message;
        public string Parameters;
    }

    public sealed class ValidationResult
    {
        public readonly List<Issue> Issues = new();

        public bool HasErrors
        {
            get
            {
                for (int i = 0; i < Issues.Count; i++)
                    if (Issues[i].Severity == Severity.Error) return true;
                return false;
            }
        }

        public string Summary()
        {
            int errors = 0, warnings = 0, infos = 0, passed = 0;
            for (int i = 0; i < Issues.Count; i++)
            {
                switch (Issues[i].Severity)
                {
                    case Severity.Error: errors++; break;
                    case Severity.Warning: warnings++; break;
                    case Severity.Pass: passed++; break;
                    default: infos++; break;
                }
            }
            return $"{passed} passed, {errors} error(s), {warnings} warning(s), {infos} skipped";
        }
    }

    /// <summary>
    /// Opt-in behaviour checks. NOT inferred from the filename: a 60 m hover ring
    /// is a property of the example brain on s1, not of s1 itself.
    /// </summary>
    [Serializable]
    public struct RunExpectations
    {
        public bool applies;
        public float expectedRingRadius;
        public float expectedAltitude;
        public float radiusTolerance;
        public float altitudeTolerance;
    }

    public static class RunValidator
    {
        const float QuaternionTolerance = 0.01f;
        const float ArenaMarginMetres = 40f;
        const float FrameCountSlack = 2f;
        const float FallbackMaxSpeed = 100f;

        public static ValidationResult Validate(RunData run, RunExpectations expectations = default)
        {
            var result = new ValidationResult();
            if (run == null)
            {
                Add(result, Severity.Info, "validate", "no RunData; nothing to check");
                return result;
            }

            CheckAltitudeSign(run, result);
            CheckUnitQuaternions(run, result);
            CheckFiniteSamples(run, result);
            CheckSlotIndices(run, result);
            CheckLifetimes(run, result);

            CheckArenaBounds(run, result);
            CheckFleetSize(run, result);
            CheckFrameCountDuration(run, result);
            CheckSpeeds(run, result);
            CheckKillRadius(run, result);
            CheckAsset(run, result);

            if (expectations.applies)
            {
                CheckHoverRing(run, expectations, result);
            }
            else
            {
                Add(result, Severity.Info, "hover_ring",
                    "skipped — RunExpectations.applies is false (not inferred from the filename).",
                    $"would check horiz radius {expectations.expectedRingRadius:G} m ± {ParamOr(expectations.radiusTolerance, 8f)} m, " +
                    $"altitude y={expectations.expectedAltitude:G} m ± {ParamOr(expectations.altitudeTolerance, 5f)} m");
            }

            return result;
        }

        public static void Log(ValidationResult result)
        {
            if (result == null) return;
            Debug.Log($"[viewer] validation: {result.Summary()}");
            for (int i = 0; i < result.Issues.Count; i++)
            {
                var issue = result.Issues[i];
                if (issue.Severity == Severity.Pass) continue;
                string line = $"[viewer] {issue.Check}: {issue.Message}";
                switch (issue.Severity)
                {
                    case Severity.Error: Debug.LogError(line); break;
                    case Severity.Warning: Debug.LogWarning(line); break;
                    default: Debug.Log(line); break;
                }
            }
        }

        static float ParamOr(float value, float fallback) => value > 0f ? value : fallback;

        static void Add(ValidationResult result, Severity severity, string check, string message, string parameters = null)
        {
            result.Issues.Add(new Issue
            {
                Severity = severity,
                Check = check,
                Message = message,
                Parameters = parameters,
            });
        }

        static int[] SampleFrames(RunData run)
        {
            int n = run.FrameCount;
            if (n <= 0) return Array.Empty<int>();
            if (n == 1) return new[] { 0 };
            var set = new SortedSet<int> { 0, n / 4, n / 2, (3 * n) / 4, n - 1 };
            var list = new List<int>(set.Count);
            foreach (int f in set)
                if (f >= 0 && f < n) list.Add(f);
            return list.ToArray();
        }

        static string FrameList(int[] frames)
        {
            if (frames == null || frames.Length == 0) return "(none)";
            var parts = new string[frames.Length];
            for (int i = 0; i < frames.Length; i++)
                parts[i] = frames[i].ToString();
            return string.Join(", ", parts);
        }

        // --- Tier 1 ----------------------------------------------------------

        static void CheckAltitudeSign(RunData run, ValidationResult result)
        {
            const string parameters = "rule: Unity y > 0 at t=0 for every living entity (unity.y = -ned.z)";
            if (run.SlotCount <= 0 || run.FrameCount <= 0)
            {
                Add(result, Severity.Info, "altitude_sign", "no entities/frames; skipped", parameters);
                return;
            }

            int alive = 0, bad = 0;
            float minY = float.PositiveInfinity;
            float worstY = 0f;
            int worstSlot = -1;
            for (int s = 0; s < run.SlotCount; s++)
            {
                if (!run.IsAlive(0, s)) continue;
                alive++;
                if (!run.Sample(0f, s, out var p, out _, out _)) continue;
                if (p.y < minY) minY = p.y;
                if (p.y <= 0f)
                {
                    bad++;
                    if (worstSlot < 0 || p.y < worstY) { worstY = p.y; worstSlot = s; }
                }
            }

            if (alive == 0)
            {
                Add(result, Severity.Info, "altitude_sign", "no entities alive at t=0; skipped", parameters);
                return;
            }

            if (bad > 0)
            {
                Add(result, Severity.Error, "altitude_sign",
                    $"{bad}/{alive} entities alive at t=0 have y <= 0 (worst slot {worstSlot} y={worstY:F2}). " +
                    "NED conversion was applied twice or not at all.",
                    parameters);
                return;
            }

            Add(result, Severity.Pass, "altitude_sign",
                $"{alive}/{alive} alive at t=0 have y > 0 (min y={minY:F2} m).",
                parameters);
        }

        static void CheckUnitQuaternions(RunData run, ValidationResult result)
        {
            string parameters = $"|q| within {QuaternionTolerance} of 1; sample frames of alive entities";
            int[] frames = SampleFrames(run);
            if (frames.Length == 0 || run.SlotCount <= 0)
            {
                Add(result, Severity.Info, "unit_quaternions", "nothing to sample; skipped", parameters);
                return;
            }

            parameters += $" (frames {FrameList(frames)})";

            int checkedN = 0, bad = 0;
            float worst = 0f;
            int worstSlot = -1, worstFrame = -1;
            for (int i = 0; i < frames.Length; i++)
            {
                int f = frames[i];
                for (int s = 0; s < run.SlotCount; s++)
                {
                    if (!run.IsAlive(f, s)) continue;
                    if (!run.Sample(f, s, out _, out var q, out _)) continue;
                    checkedN++;
                    float mag = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
                    float err = Mathf.Abs(mag - 1f);
                    if (err > worst) { worst = err; worstSlot = s; worstFrame = f; }
                    if (err > QuaternionTolerance)
                        bad++;
                }
            }

            if (checkedN == 0)
            {
                Add(result, Severity.Info, "unit_quaternions", "no alive samples; skipped", parameters);
                return;
            }

            if (bad > 0)
            {
                Add(result, Severity.Error, "unit_quaternions",
                    $"{bad}/{checkedN} sampled attitudes have |q| off 1 by more than {QuaternionTolerance} " +
                    $"(worst slot {worstSlot} frame {worstFrame}, |1-|q||={worst:F4}).",
                    parameters);
                return;
            }

            Add(result, Severity.Pass, "unit_quaternions",
                $"{checkedN} attitudes within {QuaternionTolerance} of unit (worst |1-|q||={worst:F4} at slot {worstSlot} frame {worstFrame}).",
                parameters);
        }

        static void CheckFiniteSamples(RunData run, ValidationResult result)
        {
            int[] frames = SampleFrames(run);
            string parameters = $"no NaN/Inf in pos, vel, quat; frames {FrameList(frames)}";
            if (frames.Length == 0)
            {
                Add(result, Severity.Info, "finite", "no frames; skipped", parameters);
                return;
            }

            int checkedN = 0, bad = 0;
            int worstSlot = -1, worstFrame = -1;
            string worstWhat = "";
            for (int i = 0; i < frames.Length; i++)
            {
                int f = frames[i];
                for (int s = 0; s < run.SlotCount; s++)
                {
                    if (!run.IsAlive(f, s)) continue;
                    if (!run.Sample(f, s, out var p, out var q, out var v)) continue;
                    checkedN++;
                    if (!Finite(p) || !Finite(v) || !Finite(q))
                    {
                        bad++;
                        if (worstSlot < 0)
                        {
                            worstSlot = s;
                            worstFrame = f;
                            worstWhat = !Finite(p) ? "position" : !Finite(v) ? "velocity" : "rotation";
                        }
                    }
                }
            }

            if (bad > 0)
            {
                Add(result, Severity.Error, "finite",
                    $"{bad}/{checkedN} sampled pose(s) contain NaN or Infinity " +
                    $"(first: slot {worstSlot} frame {worstFrame} {worstWhat}).",
                    parameters);
                return;
            }

            Add(result, Severity.Pass, "finite",
                $"{checkedN} sampled poses are finite.",
                parameters);
        }

        static bool Finite(Vector3 v) =>
            !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) ||
              float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));

        static bool Finite(Quaternion q) =>
            !(float.IsNaN(q.x) || float.IsNaN(q.y) || float.IsNaN(q.z) || float.IsNaN(q.w) ||
              float.IsInfinity(q.x) || float.IsInfinity(q.y) || float.IsInfinity(q.z) || float.IsInfinity(q.w));

        static void CheckSlotIndices(RunData run, ValidationResult result)
        {
            const string parameters = "entities[i].slot == i; count == slot_count";
            var ents = run.Meta.entities;
            if (ents == null)
            {
                Add(result, Severity.Error, "slot_index", "meta.entities is null", parameters);
                return;
            }

            bool failed = false;
            if (ents.Count != run.SlotCount)
            {
                Add(result, Severity.Error, "slot_index",
                    $"entities.Count ({ents.Count}) != slot_count ({run.SlotCount})",
                    parameters);
                failed = true;
            }

            int n = Mathf.Min(ents.Count, run.SlotCount);
            int bad = 0;
            int first = -1;
            for (int i = 0; i < n; i++)
            {
                if (ents[i] == null || ents[i].slot != i)
                {
                    bad++;
                    if (first < 0) first = i;
                }
            }

            if (bad > 0)
            {
                Add(result, Severity.Error, "slot_index",
                    $"{bad} entities have slot != list index (first i={first}, slot={ents[first]?.slot}).",
                    parameters);
                failed = true;
            }

            if (!failed)
            {
                Add(result, Severity.Pass, "slot_index",
                    $"{ents.Count} entities, each entities[i].slot == i.",
                    parameters);
            }
        }

        static void CheckLifetimes(RunData run, ValidationResult result)
        {
            string parameters = $"first_frame <= last_frame, both in [0, {run.FrameCount})";
            var ents = run.Meta.entities;
            if (ents == null)
            {
                Add(result, Severity.Info, "lifetime", "no entities; skipped", parameters);
                return;
            }

            int nframes = run.FrameCount;
            int bad = 0;
            string firstMsg = null;
            for (int i = 0; i < ents.Count; i++)
            {
                var e = ents[i];
                if (e == null) continue;
                bool ok = e.first_frame <= e.last_frame
                          && e.first_frame >= 0 && e.last_frame >= 0
                          && e.first_frame < nframes && e.last_frame < nframes;
                if (!ok)
                {
                    bad++;
                    firstMsg ??= $"slot {e.slot} first_frame={e.first_frame} last_frame={e.last_frame} frame_count={nframes}";
                }
            }

            if (bad > 0)
            {
                Add(result, Severity.Error, "lifetime",
                    $"{bad} entit(y/ies) have first_frame > last_frame or out of [0, frame_count). {firstMsg}",
                    parameters);
                return;
            }

            Add(result, Severity.Pass, "lifetime",
                $"{ents.Count} entities have first_frame <= last_frame inside [0, {nframes}).",
                parameters);
        }

        // --- Tier 2 ----------------------------------------------------------

        static void CheckArenaBounds(RunData run, ValidationResult result)
        {
            var arena = run.Meta.arena;
            if (arena?.min == null || arena.max == null || arena.min.Length < 3 || arena.max.Length < 3)
            {
                Add(result, Severity.Warning, "arena",
                    "meta.arena min/max missing — treated as unknown, not a default box. " +
                    "Load continues; EnvironmentView uses its inspector fallback.",
                    "AABB + 40 m margin; wreckage may leave");
                return;
            }

            Vector3 min = new(arena.min[0], arena.min[1], arena.min[2]);
            Vector3 max = new(arena.max[0], arena.max[1], arena.max[2]);
            string parameters =
                $"AABB ({min.x:F0},{min.y:F0},{min.z:F0})..({max.x:F0},{max.y:F0},{max.z:F0}) plus {ArenaMarginMetres:F0} m margin";

            int[] frames = SampleFrames(run);
            int checkedN = 0, outside = 0;
            int worstSlot = -1, worstFrame = -1;
            Vector3 worstP = default;

            for (int i = 0; i < frames.Length; i++)
            {
                int f = frames[i];
                for (int s = 0; s < run.SlotCount; s++)
                {
                    if (!run.IsAlive(f, s)) continue;
                    if (!run.Sample(f, s, out var p, out _, out _)) continue;
                    checkedN++;
                    if (p.x < min.x - ArenaMarginMetres || p.x > max.x + ArenaMarginMetres ||
                        p.y < min.y - ArenaMarginMetres || p.y > max.y + ArenaMarginMetres ||
                        p.z < min.z - ArenaMarginMetres || p.z > max.z + ArenaMarginMetres)
                    {
                        outside++;
                        if (worstSlot < 0) { worstSlot = s; worstFrame = f; worstP = p; }
                    }
                }
            }

            if (outside > 0)
            {
                Add(result, Severity.Warning, "arena",
                    $"{outside}/{checkedN} sampled position(s) lie outside the arena plus {ArenaMarginMetres:F0} m " +
                    $"(first slot {worstSlot} frame {worstFrame} p={worstP}). Wreckage can legitimately leave.",
                    parameters);
                return;
            }

            Add(result, Severity.Pass, "arena",
                $"{checkedN} sampled positions inside the arena plus {ArenaMarginMetres:F0} m (frames {FrameList(frames)}).",
                parameters);
        }

        static void CheckFleetSize(RunData run, ValidationResult result)
        {
            string parameters = $"count(drone_id >= 0) == meta.fleet_size ({run.Meta.fleet_size})";
            var ents = run.Meta.entities;
            if (ents == null)
            {
                Add(result, Severity.Info, "fleet_size", "no entities; skipped", parameters);
                return;
            }

            int drones = 0;
            for (int i = 0; i < ents.Count; i++)
                if (ents[i] != null && ents[i].drone_id >= 0) drones++;

            if (drones != run.Meta.fleet_size)
            {
                Add(result, Severity.Warning, "fleet_size",
                    $"entities with drone_id >= 0: {drones}, meta.fleet_size: {run.Meta.fleet_size}",
                    parameters);
                return;
            }

            Add(result, Severity.Pass, "fleet_size",
                $"{drones} friendlies with drone_id >= 0, matches fleet_size.",
                parameters);
        }

        static void CheckFrameCountDuration(RunData run, ValidationResult result)
        {
            string parameters = $"frame_count ≈ duration × trace_hz (slack {FrameCountSlack + 1f:G} frames)";
            if (run.TraceHz <= 0f)
            {
                Add(result, Severity.Info, "frame_count", "trace_hz is 0; skipped", parameters);
                return;
            }

            float expected = run.Duration * run.TraceHz;
            float delta = Mathf.Abs(run.FrameCount - expected);
            parameters += $"; duration={run.Duration:F2}s, trace_hz={run.TraceHz}, expected={expected:F1}";

            if (delta > FrameCountSlack + 1f)
            {
                Add(result, Severity.Warning, "frame_count",
                    $"frame_count {run.FrameCount} vs duration×trace_hz {expected:F1}. Off by {delta:F1} frames.",
                    parameters);
                return;
            }

            Add(result, Severity.Pass, "frame_count",
                $"frame_count {run.FrameCount} vs {expected:F1} expected (delta {delta:F1}).",
                parameters);
        }

        static void CheckSpeeds(RunData run, ValidationResult result)
        {
            int[] frames = SampleFrames(run);
            string parameters =
                $"speed < {FallbackMaxSpeed:G} m/s FALLBACK (max_speed is not in the trace); frames {FrameList(frames)}";
            if (frames.Length == 0)
            {
                Add(result, Severity.Info, "speed", "no frames; skipped", parameters);
                return;
            }

            float bound = FallbackMaxSpeed;
            int checkedN = 0, wild = 0;
            float worst = 0f;
            int worstSlot = -1, worstFrame = -1;

            for (int i = 0; i < frames.Length; i++)
            {
                int f = frames[i];
                for (int s = 0; s < run.SlotCount; s++)
                {
                    if (!run.IsAlive(f, s)) continue;
                    if (!run.Sample(f, s, out _, out _, out var v)) continue;
                    checkedN++;
                    float speed = v.magnitude;
                    if (speed > worst) { worst = speed; worstSlot = s; worstFrame = f; }
                    if (speed > bound)
                        wild++;
                }
            }

            if (wild > 0)
            {
                Add(result, Severity.Warning, "speed",
                    $"{wild}/{checkedN} sampled velocities exceed {bound:F0} m/s " +
                    $"(worst slot {worstSlot} frame {worstFrame} {worst:F1} m/s). " +
                    "A wild speed usually means position and velocity columns got swapped.",
                    parameters);
                return;
            }

            Add(result, Severity.Pass, "speed",
                $"{checkedN} samples, max {worst:F1} m/s at slot {worstSlot} frame {worstFrame} (bound {bound:F0} m/s fallback).",
                parameters);
        }

        static void CheckKillRadius(RunData run, ValidationResult result)
        {
            const string parameters = "meta.kill_radius > 0 means present; 0 is unknown, not 0 m";
            if (run.Meta.kill_radius > 0f)
            {
                Add(result, Severity.Pass, "kill_radius",
                    $"kill_radius = {run.Meta.kill_radius:G} m (from meta).",
                    parameters);
                return;
            }

            Add(result, Severity.Warning, "kill_radius",
                "kill_radius is missing or 0 in the meta file — treated as unknown, not 0 m. " +
                "Load continues; the sphere uses SceneBuilder's inspector fallback.",
                parameters);
        }

        static void CheckAsset(RunData run, ValidationResult result)
        {
            var asset = run.Meta.asset;
            if (asset?.position == null || asset.position.Length < 3)
            {
                Add(result, Severity.Warning, "asset_position",
                    "meta.asset.position missing — treated as unknown, not origin. " +
                    "Load continues; EnvironmentView places the marker at (0,0,0).",
                    "position[3] present");
            }
            else
            {
                var p = asset.position;
                Add(result, Severity.Pass, "asset_position",
                    $"asset at ({p[0]:G}, {p[1]:G}, {p[2]:G}) m.",
                    "position[3] present");
            }

            if (asset == null || asset.radius <= 0f)
            {
                Add(result, Severity.Warning, "asset_radius",
                    "meta.asset.radius missing or 0 — treated as unknown, not 0 m. " +
                    "Load continues; EnvironmentView uses its inspector fallback.",
                    "radius > 0");
            }
            else
            {
                Add(result, Severity.Pass, "asset_radius",
                    $"asset radius = {asset.radius:G} m.",
                    "radius > 0");
            }
        }

        // --- Tier 3 ----------------------------------------------------------

        static void CheckHoverRing(RunData run, RunExpectations exp, ValidationResult result)
        {
            float rTol = ParamOr(exp.radiusTolerance, 8f);
            float aTol = ParamOr(exp.altitudeTolerance, 5f);
            string parameters =
                $"opt-in; horiz sqrt(x²+z²) vs {exp.expectedRingRadius:G} m ± {rTol:G} m, " +
                $"y vs {exp.expectedAltitude:G} m ± {aTol:G} m (not |p|)";

            var ents = run.Meta.entities;
            if (ents == null)
            {
                Add(result, Severity.Info, "hover_ring", "no entities; skipped", parameters);
                return;
            }

            Vector3 asset = Vector3.zero;
            if (run.Meta.asset?.position != null && run.Meta.asset.position.Length >= 3)
                asset = new Vector3(run.Meta.asset.position[0], run.Meta.asset.position[1], run.Meta.asset.position[2]);

            int checkedN = 0, badR = 0, badY = 0;
            float worstRErr = 0f, worstYErr = 0f;
            float minR = float.PositiveInfinity, maxR = 0f, minY = float.PositiveInfinity, maxY = float.NegativeInfinity;

            for (int i = 0; i < ents.Count; i++)
            {
                var e = ents[i];
                if (e == null || e.drone_id < 0) continue;
                if (!run.IsAlive(0, e.slot)) continue;
                if (!run.Sample(0f, e.slot, out var p, out _, out _)) continue;
                checkedN++;

                float horiz = Mathf.Sqrt((p.x - asset.x) * (p.x - asset.x) + (p.z - asset.z) * (p.z - asset.z));
                if (horiz < minR) minR = horiz;
                if (horiz > maxR) maxR = horiz;
                if (p.y < minY) minY = p.y;
                if (p.y > maxY) maxY = p.y;

                float rErr = Mathf.Abs(horiz - exp.expectedRingRadius);
                float yErr = Mathf.Abs(p.y - exp.expectedAltitude);
                if (rErr > rTol) { badR++; if (rErr > worstRErr) worstRErr = rErr; }
                if (yErr > aTol) { badY++; if (yErr > worstYErr) worstYErr = yErr; }
            }

            if (checkedN == 0)
            {
                Add(result, Severity.Info, "hover_ring",
                    "applies=true but no friendlies (drone_id >= 0) alive at t=0; skipped",
                    parameters);
                return;
            }

            if (badR > 0)
            {
                Add(result, Severity.Warning, "hover_ring",
                    $"{badR}/{checkedN} friendlies at t=0 are not at horizontal radius {exp.expectedRingRadius} m " +
                    $"(tolerance {rTol} m, worst error {worstRErr:F2} m). " +
                    "This is an example-brain expectation, not a scenario invariant.",
                    parameters);
            }

            if (badY > 0)
            {
                Add(result, Severity.Warning, "hover_ring",
                    $"{badY}/{checkedN} friendlies at t=0 are not at altitude y={exp.expectedAltitude} m " +
                    $"(tolerance {aTol} m, worst error {worstYErr:F2} m).",
                    parameters);
            }

            if (badR == 0 && badY == 0)
            {
                Add(result, Severity.Pass, "hover_ring",
                    $"{checkedN} friendlies at t=0: radius {minR:F2}–{maxR:F2} m, y {minY:F2}–{maxY:F2} m.",
                    parameters);
            }
        }
    }
}
