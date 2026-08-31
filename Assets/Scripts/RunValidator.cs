// Load-time sanity checks for a converted run. Does not throw: one pass, every
// issue collected. RunLoader logs the result; VisualizerRoot decides whether
// Errors abort the load.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace SwarmViewer
{
    public enum Severity { Info, Warning, Error }

    public sealed class Issue
    {
        public Severity Severity;
        public string Check;
        public string Message;
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
            int errors = 0, warnings = 0, infos = 0;
            for (int i = 0; i < Issues.Count; i++)
            {
                switch (Issues[i].Severity)
                {
                    case Severity.Error: errors++; break;
                    case Severity.Warning: warnings++; break;
                    default: infos++; break;
                }
            }
            return $"{errors} error(s), {warnings} warning(s), {infos} info";
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
                CheckHoverRing(run, expectations, result);

            return result;
        }

        public static void Log(ValidationResult result)
        {
            if (result == null) return;
            if (result.Issues.Count == 0)
            {
                Debug.Log("[viewer] validation: all checks passed");
                return;
            }

            Debug.Log($"[viewer] validation: {result.Summary()}");
            for (int i = 0; i < result.Issues.Count; i++)
            {
                var issue = result.Issues[i];
                string line = $"[viewer] {issue.Check}: {issue.Message}";
                switch (issue.Severity)
                {
                    case Severity.Error: Debug.LogError(line); break;
                    case Severity.Warning: Debug.LogWarning(line); break;
                    default: Debug.Log(line); break;
                }
            }
        }

        static void Add(ValidationResult result, Severity severity, string check, string message)
        {
            result.Issues.Add(new Issue { Severity = severity, Check = check, Message = message });
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

        // --- Tier 1 ----------------------------------------------------------

        static void CheckAltitudeSign(RunData run, ValidationResult result)
        {
            if (run.SlotCount <= 0 || run.FrameCount <= 0)
            {
                Add(result, Severity.Info, "altitude_sign", "no entities/frames; skipped");
                return;
            }

            int alive = 0, bad = 0;
            float worstY = 0f;
            int worstSlot = -1;
            for (int s = 0; s < run.SlotCount; s++)
            {
                if (!run.IsAlive(0, s)) continue;
                alive++;
                if (!run.Sample(0f, s, out var p, out _, out _)) continue;
                if (p.y <= 0f)
                {
                    bad++;
                    if (worstSlot < 0 || p.y < worstY) { worstY = p.y; worstSlot = s; }
                }
            }

            if (alive == 0)
            {
                Add(result, Severity.Info, "altitude_sign", "no entities alive at t=0; skipped");
                return;
            }

            if (bad > 0)
            {
                Add(result, Severity.Error, "altitude_sign",
                    $"{bad}/{alive} entities alive at t=0 have y <= 0 (worst slot {worstSlot} y={worstY:F2}). " +
                    "NED conversion was applied twice or not at all (unity.y = -ned.z).");
            }
        }

        static void CheckUnitQuaternions(RunData run, ValidationResult result)
        {
            int[] frames = SampleFrames(run);
            if (frames.Length == 0 || run.SlotCount <= 0)
            {
                Add(result, Severity.Info, "unit_quaternions", "nothing to sample; skipped");
                return;
            }

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
                    if (err > QuaternionTolerance)
                    {
                        bad++;
                        if (err > worst) { worst = err; worstSlot = s; worstFrame = f; }
                    }
                }
            }

            if (checkedN == 0)
            {
                Add(result, Severity.Info, "unit_quaternions", "no alive samples; skipped");
                return;
            }

            if (bad > 0)
            {
                Add(result, Severity.Error, "unit_quaternions",
                    $"{bad}/{checkedN} sampled attitudes have |q| off 1 by more than {QuaternionTolerance} " +
                    $"(worst slot {worstSlot} frame {worstFrame}, |1-|q||={worst:F4}). " +
                    "Broken attitude conversion is otherwise silent.");
            }
        }

        static void CheckFiniteSamples(RunData run, ValidationResult result)
        {
            int[] frames = SampleFrames(run);
            if (frames.Length == 0)
            {
                Add(result, Severity.Info, "finite", "no frames; skipped");
                return;
            }

            int bad = 0;
            int worstSlot = -1, worstFrame = -1;
            string worstWhat = "";
            for (int i = 0; i < frames.Length; i++)
            {
                int f = frames[i];
                for (int s = 0; s < run.SlotCount; s++)
                {
                    if (!run.IsAlive(f, s)) continue;
                    if (!run.Sample(f, s, out var p, out var q, out var v)) continue;
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
                    $"{bad} sampled pose(s) contain NaN or Infinity " +
                    $"(first: slot {worstSlot} frame {worstFrame} {worstWhat}).");
            }
        }

        static bool Finite(Vector3 v) =>
            !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) ||
              float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));

        static bool Finite(Quaternion q) =>
            !(float.IsNaN(q.x) || float.IsNaN(q.y) || float.IsNaN(q.z) || float.IsNaN(q.w) ||
              float.IsInfinity(q.x) || float.IsInfinity(q.y) || float.IsInfinity(q.z) || float.IsInfinity(q.w));

        static void CheckSlotIndices(RunData run, ValidationResult result)
        {
            var ents = run.Meta.entities;
            if (ents == null)
            {
                Add(result, Severity.Error, "slot_index", "meta.entities is null");
                return;
            }

            if (ents.Count != run.SlotCount)
            {
                Add(result, Severity.Error, "slot_index",
                    $"entities.Count ({ents.Count}) != slot_count ({run.SlotCount})");
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
                    $"{bad} entities have slot != list index (first i={first}, slot={ents[first]?.slot}). " +
                    "The binary layout depends on entities[i].slot == i.");
            }
        }

        static void CheckLifetimes(RunData run, ValidationResult result)
        {
            var ents = run.Meta.entities;
            if (ents == null)
            {
                Add(result, Severity.Info, "lifetime", "no entities; skipped");
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
                    $"{bad} entit(y/ies) have first_frame > last_frame or out of [0, frame_count). {firstMsg}");
            }
        }

        // --- Tier 2 ----------------------------------------------------------

        static void CheckArenaBounds(RunData run, ValidationResult result)
        {
            var arena = run.Meta.arena;
            if (arena?.min == null || arena.max == null || arena.min.Length < 3 || arena.max.Length < 3)
            {
                Add(result, Severity.Warning, "arena",
                    "meta.arena min/max missing — treated as unknown, not a default box. " +
                    "Load continues; EnvironmentView uses its inspector fallback. " +
                    "A silent -200..200 would look like s1's measured arena.");
                return;
            }

            Vector3 min = new(arena.min[0], arena.min[1], arena.min[2]);
            Vector3 max = new(arena.max[0], arena.max[1], arena.max[2]);
            int[] frames = SampleFrames(run);
            int outside = 0;
            int worstSlot = -1, worstFrame = -1;
            Vector3 worstP = default;

            for (int i = 0; i < frames.Length; i++)
            {
                int f = frames[i];
                for (int s = 0; s < run.SlotCount; s++)
                {
                    if (!run.IsAlive(f, s)) continue;
                    if (!run.Sample(f, s, out var p, out _, out _)) continue;
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
                    $"{outside} sampled position(s) lie outside the arena AABB plus {ArenaMarginMetres:F0} m " +
                    $"(first slot {worstSlot} frame {worstFrame} p={worstP}). Wreckage can legitimately leave.");
            }
        }

        static void CheckFleetSize(RunData run, ValidationResult result)
        {
            var ents = run.Meta.entities;
            if (ents == null)
            {
                Add(result, Severity.Info, "fleet_size", "no entities; skipped");
                return;
            }

            int drones = 0;
            for (int i = 0; i < ents.Count; i++)
                if (ents[i] != null && ents[i].drone_id >= 0) drones++;

            if (drones != run.Meta.fleet_size)
            {
                Add(result, Severity.Warning, "fleet_size",
                    $"entities with drone_id >= 0: {drones}, meta.fleet_size: {run.Meta.fleet_size}");
            }
        }

        static void CheckFrameCountDuration(RunData run, ValidationResult result)
        {
            if (run.TraceHz <= 0f)
            {
                Add(result, Severity.Info, "frame_count", "trace_hz is 0; skipped");
                return;
            }

            float expected = run.Duration * run.TraceHz;
            // First frame is often at t≈dt, last at duration, so N ≈ duration*hz (+/- 1).
            float delta = Mathf.Abs(run.FrameCount - expected);
            if (delta > FrameCountSlack + 1f)
            {
                Add(result, Severity.Warning, "frame_count",
                    $"frame_count {run.FrameCount} vs duration*trace_hz {expected:F1} " +
                    $"(duration={run.Duration:F2}s, trace_hz={run.TraceHz}). Off by {delta:F1} frames.");
            }
        }

        static void CheckSpeeds(RunData run, ValidationResult result)
        {
            int[] frames = SampleFrames(run);
            if (frames.Length == 0)
            {
                Add(result, Severity.Info, "speed", "no frames; skipped");
                return;
            }

            // max_speed is NOT in the trace header. 100 m/s is a loose fallback,
            // not the real 20 m/s s1 limit from --dump-params.
            float bound = FallbackMaxSpeed;
            int wild = 0;
            float worst = 0f;
            int worstSlot = -1, worstFrame = -1;

            for (int i = 0; i < frames.Length; i++)
            {
                int f = frames[i];
                for (int s = 0; s < run.SlotCount; s++)
                {
                    if (!run.IsAlive(f, s)) continue;
                    if (!run.Sample(f, s, out _, out _, out var v)) continue;
                    float speed = v.magnitude;
                    if (speed > bound)
                    {
                        wild++;
                        if (speed > worst) { worst = speed; worstSlot = s; worstFrame = f; }
                    }
                }
            }

            if (wild > 0)
            {
                Add(result, Severity.Warning, "speed",
                    $"{wild} sampled velocity(ies) exceed {bound:F0} m/s " +
                    $"(worst slot {worstSlot} frame {worstFrame} {worst:F1} m/s). " +
                    "Bound is a FALLBACK, not a real limit — max_speed is not in the trace. " +
                    "A wild speed usually means position and velocity columns got swapped.");
            }
        }

        static void CheckKillRadius(RunData run, ValidationResult result)
        {
            // Json.NET maps a missing float to 0. That is not "the radius is 0 m".
            if (run.Meta.kill_radius > 0f)
                return;

            Add(result, Severity.Warning, "kill_radius",
                "kill_radius is missing or 0 in the meta file — treated as unknown, not 0 m. " +
                "Load continues; the sphere uses SceneBuilder's inspector fallback. " +
                "A silent 0 would draw a zero-radius sphere and look like a real limit.");
        }

        static void CheckAsset(RunData run, ValidationResult result)
        {
            var asset = run.Meta.asset;
            if (asset?.position == null || asset.position.Length < 3)
            {
                Add(result, Severity.Warning, "asset",
                    "meta.asset.position missing — treated as unknown, not origin. " +
                    "Load continues; EnvironmentView places the marker at (0,0,0).");
            }

            if (asset == null || asset.radius <= 0f)
            {
                Add(result, Severity.Warning, "asset",
                    "meta.asset.radius missing or 0 — treated as unknown, not 0 m. " +
                    "Load continues; EnvironmentView uses its inspector fallback. " +
                    "A silent 30 would look like s1's measured asset radius.");
            }
        }

        // --- Tier 3 ----------------------------------------------------------

        static void CheckHoverRing(RunData run, RunExpectations exp, ValidationResult result)
        {
            var ents = run.Meta.entities;
            if (ents == null)
            {
                Add(result, Severity.Info, "hover_ring", "no entities; skipped");
                return;
            }

            Vector3 asset = Vector3.zero;
            if (run.Meta.asset?.position != null && run.Meta.asset.position.Length >= 3)
                asset = new Vector3(run.Meta.asset.position[0], run.Meta.asset.position[1], run.Meta.asset.position[2]);

            float rTol = exp.radiusTolerance > 0f ? exp.radiusTolerance : 8f;
            float aTol = exp.altitudeTolerance > 0f ? exp.altitudeTolerance : 5f;
            int checkedN = 0, badR = 0, badY = 0;
            float worstRErr = 0f, worstYErr = 0f;

            for (int i = 0; i < ents.Count; i++)
            {
                var e = ents[i];
                if (e == null || e.drone_id < 0) continue;
                if (!run.IsAlive(0, e.slot)) continue;
                if (!run.Sample(0f, e.slot, out var p, out _, out _)) continue;
                checkedN++;

                float horiz = Mathf.Sqrt((p.x - asset.x) * (p.x - asset.x) + (p.z - asset.z) * (p.z - asset.z));
                float rErr = Mathf.Abs(horiz - exp.expectedRingRadius);
                float yErr = Mathf.Abs(p.y - exp.expectedAltitude);
                if (rErr > rTol) { badR++; if (rErr > worstRErr) worstRErr = rErr; }
                if (yErr > aTol) { badY++; if (yErr > worstYErr) worstYErr = yErr; }
            }

            if (checkedN == 0)
            {
                Add(result, Severity.Info, "hover_ring",
                    "applies=true but no friendlies (drone_id >= 0) alive at t=0; skipped");
                return;
            }

            if (badR > 0)
            {
                Add(result, Severity.Warning, "hover_ring",
                    $"{badR}/{checkedN} friendlies at t=0 are not at horizontal radius {exp.expectedRingRadius} m " +
                    $"(tolerance {rTol} m, worst error {worstRErr:F2} m). " +
                    "This is an example-brain expectation, not a scenario invariant.");
            }

            if (badY > 0)
            {
                Add(result, Severity.Warning, "hover_ring",
                    $"{badY}/{checkedN} friendlies at t=0 are not at altitude y={exp.expectedAltitude} m " +
                    $"(tolerance {aTol} m, worst error {worstYErr:F2} m). Horizontal radius is sqrt(x^2+z^2), not |p|.");
            }
        }
    }
}
