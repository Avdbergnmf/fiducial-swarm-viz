// Data structures representing the parsed run metadata from *.meta.json.
// Plain data models with no runtime simulation or visual behavior.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Newtonsoft.Json.Linq;

namespace SwarmViewer
{
    // Mirrors run.meta.json (see FORMAT.md). Plain data, no behaviour.
    // Fields the minimal viewer does not use yet are still parsed, so extending
    // means writing a view, not changing the model.

    public enum EntityKind { Unknown, Friendly, Hostile, Civilian, Wreckage }

    public enum BeliefClass { Unknown = 0, Friendly = 1, Enemy = 2, Neutral = 3, Compromised = 4 }

    [Serializable]
    public class EntityInfo
    {
        public int slot;
        public int trace_id;
        public string kind;
        public int drone_id = -1;
        public int first_frame = -1;
        public int last_frame = -1;
        public int compromised_from = -1;      // ground truth; not shown in a belief view

        [NonSerialized] public EntityKind Kind;

        public bool IsFriendly => drone_id >= 0;
        public string Label => IsFriendly ? $"Drone {drone_id}" : $"{Kind} {trace_id}";

        /// <summary>
        /// The three ids on one craft, in English. Logs and the aircraft list
        /// use drone_id (0-based). The recording's entity id is 1-based, so
        /// drone 15 is sim #16 — that is the off-by-one, not a different craft.
        /// </summary>
        public string IdBlurb => IsFriendly
            ? $"Drone {drone_id} is the brain id (0-based). Logs, radio, and this list all use it.\n" +
              $"Viewer slot {slot} is the compact index in the recording.\n" +
              $"Sim entity {trace_id} is 1-based. Drone {drone_id} is sim #{trace_id}."
            : $"{Kind} {trace_id} is the simulator entity (1-based). Slot {slot}. Not a fleet drone — no log channel.";
    }

    [Serializable]
    public class EventInfo
    {
        public float t;
        public int frame;
        public string kind;                    // spawn | death | collision | breach | detect
        public int severity;                   // 0 info .. 3 mission-losing
        public int[] slots;
        public string text;
    }

    [Serializable] public class LinkInfo { public int a, b; public float t_start, t_end; }
    [Serializable] public class BeliefChange { public float t; public int observer, slot; public string @class; }
    [Serializable]
    public class LogLine
    {
        public float t;
        public int drone;
        public string text;

        [NonSerialized] string _pretty;

        /// <summary>The line in English. Built once, on first display.</summary>
        public string Pretty => _pretty ??= LogPhrase.Humanize(text);
    }

    [Serializable] public class TelemetrySample { public float t; public int drone; public int bytes_sent, budget_remaining; }
    [Serializable] public class Bounds3 { public float[] min, max; }
    [Serializable] public class AssetInfo { public float[] position; public float radius; }

    [Serializable]
    public class ProvenanceInfo // (where this file came from)
    {
        public string trace;
        public string scenario;
        public string brain;
        public string sim_version;
        public int header_schema;
        public string generated_at;
    }

    public class RunMeta
    {
        public int format_version;
        public string source, scenario, sim_version, brain;
        public float dt, trace_hz, duration;
        public int frame_count, slot_count, stride, fleet_size;
        public Bounds3 arena;
        public AssetInfo asset;
        public float kill_radius;              // 0 when the field is absent (older meta files)
        public ProvenanceInfo provenance;

        public List<EntityInfo> entities = new();
        public List<EventInfo> events = new();
        public List<LinkInfo> links = new();
        public List<BeliefChange> beliefs = new();
        public List<LogLine> logs = new();
        public List<TelemetrySample> telemetry = new();

        public SimReport report;
        public ScoringBlock scoring;
    }

    /// <summary>
    /// Per-event mission ledger from the sidecar (build_scoring). The running
    /// series is cumulative mission; ledger kinds are intercept, breach,
    /// civilian_lost, friendly_lost. Missing on older meta files.
    /// </summary>
    public class ScoringBlock
    {
        public string formula;
        public Dictionary<string, float> weights;
        public ScoringTotals totals;
        public ScoringBucket attributable;
        public ScoringBucket unattributable;
        public List<ScoreLedgerEntry> ledger = new();
        public List<ScoreRunningPoint> running = new();
    }

    public class ScoringTotals
    {
        public float mission;
        public float ledger_sum;
        public float? awareness, comms, total;
        public bool verified;
    }

    public class ScoringBucket
    {
        public int count;
        public float points;
    }

    public class ScoreLedgerEntry
    {
        public float t;
        public int frame;
        public string kind;
        public float points;
        public bool attributable = true;
        public string text;
        public JObject detail;
        public int[] slots;
    }

    public class ScoreRunningPoint
    {
        public float t;
        public float mission;
    }

    /// <summary>
    /// The simulator's --report, copied into meta by the sidecar. Authoritative
    /// for score, intercepts, and why each friendly was lost. Missing on older
    /// meta files that predate that copy. Newtonsoft reads this; Unity does not.
    /// </summary>
    public class SimReport
    {
        public string outcome, scenario, brain, sim_version;
        public float sim_time_s;
        public ScoreTerms score;
        public MissionReport mission;
        public AwarenessReport awareness;
        public CommsReport comms;
        public ComputeReport compute;
        public IntegrityReport integrity;
    }

    public class ScoreTerms
    {
        public float? total, mission, awareness, comms, learning, detection;
    }

    public class MissionReport
    {
        public float asset_survival_time_s;
        public int civilians_lost;
        public int friendlies_alive;
        public int friendlies_lost_credited;
        public int friendlies_lost_wasted;
        public int hostiles_destroyed, hostiles_reached_asset, hostiles_surviving, hostiles_total;
        public int wreckage_kills, wreckage_spawned;
        public List<FriendlyLoss> friendly_losses = new();
        public List<InterceptRecord> intercepts = new();
        public Dictionary<string, int> losses_by_cause = new();
    }

    public class FriendlyLoss
    {
        public string cause;
        public bool credited;
        public int drone;
        public float t;
    }

    public class InterceptRecord
    {
        public string hostile;
        public float reward;
        public float t_engage_s, t_free_s, t_kill, t_spawn, urgency_ratio;
        public int[] by_drones;
    }

    public class AwarenessReport
    {
        public float belief_accuracy;
        public int correct_declarations, wrong_declarations, samples;
        public int compromises, compromises_detected, false_accusations;
        public float? compromise_detect_latency_s;
    }

    public class CommsReport
    {
        public float bytes_per_drone_per_s;
        public int frames_sent, frames_dropped_budget, frames_dropped_loss;
        public float? propagation_p95_s;
    }

    public class ComputeReport
    {
        public float budget_us, mean_tick_us, p99_tick_us;
        public int overruns;
    }

    public class IntegrityReport
    {
        public int brain_crashes;
    }

    public readonly struct ScoreRow
    {
        public readonly string Key;
        public readonly string Value;
        public readonly string Detail;

        public ScoreRow(string key, string value, string detail = null)
        {
            Key = key;
            Value = value;
            Detail = detail;
        }
    }

    /// <summary>
    /// Score rows from the simulator report. Intercept rewards and term totals
    /// come from the file; breach/waste/civilian lines are counts, not
    /// re-multiplied defaults, because a scenario may override the weights.
    /// </summary>
    public static class ScoreBreakdown
    {
        public static List<ScoreRow> Rows(SimReport report)
        {
            var rows = new List<ScoreRow>();
            if (report == null)
            {
                rows.Add(new ScoreRow("Report", "missing",
                    "The sidecar copies --report into meta.report. Re-convert this trace to get a score."));
                return rows;
            }

            var score = report.score;
            rows.Add(new ScoreRow("Total", Pts(score?.total), OutcomeLine(report)));
            rows.Add(new ScoreRow("Mission", Pts(score?.mission), MissionLine(report.mission)));

            var intercepts = report.mission?.intercepts;
            if (intercepts != null)
            {
                for (int i = 0; i < intercepts.Count; i++)
                    rows.Add(InterceptRow(intercepts[i]));
            }

            var mission = report.mission;
            if (mission != null)
            {
                if (mission.hostiles_reached_asset > 0)
                    rows.Add(new ScoreRow("Breaches",
                        $"{mission.hostiles_reached_asset} of {mission.hostiles_total}",
                        "Each pays P_breach (200 by default). A breach does not end the run."));

                if (mission.hostiles_surviving > 0)
                    rows.Add(new ScoreRow("Still airborne",
                        mission.hostiles_surviving.ToString(),
                        "A hostile alive at the end is worth nothing — no penalty, no credit."));

                AppendLosses(rows, mission);

                if (mission.civilians_lost > 0)
                    rows.Add(new ScoreRow("Civilians lost",
                        mission.civilians_lost.ToString(),
                        "Each pays P_civilian (150 by default), on top of any wasted drone that hit them."));
            }

            var aware = report.awareness;
            rows.Add(new ScoreRow("Awareness", Pts(score?.awareness), AwarenessLine(aware)));
            if (aware != null && (aware.compromises > 0 || aware.false_accusations > 0 || score?.detection != null))
                rows.Add(new ScoreRow("Detection", Pts(score?.detection), DetectionLine(aware)));

            rows.Add(new ScoreRow("Comms", Pts(score?.comms), CommsLine(report.comms)));
            if (score?.learning != null)
                rows.Add(new ScoreRow("Learning", Pts(score.learning), null));

            var compute = report.compute;
            if (compute != null)
                rows.Add(new ScoreRow("Compute",
                    $"{compute.mean_tick_us.ToString("G3", CultureInfo.InvariantCulture)} µs mean",
                    "Measured, not scored. Budget " +
                    $"{compute.budget_us.ToString("G4", CultureInfo.InvariantCulture)} µs, " +
                    $"{compute.overruns} overrun(s)."));

            if (report.integrity != null && report.integrity.brain_crashes > 0)
                rows.Add(new ScoreRow("Crashes", report.integrity.brain_crashes.ToString(),
                    "A crash is the loss of that drone."));

            return rows;
        }

        static ScoreRow InterceptRow(InterceptRecord hit)
        {
            if (hit == null) return new ScoreRow("Intercept", "?", null);
            string who = string.IsNullOrEmpty(hit.hostile) ? "hostile" : hit.hostile;
            var sb = new StringBuilder();
            if (hit.by_drones != null && hit.by_drones.Length > 0)
            {
                sb.Append("drone ");
                for (int i = 0; i < hit.by_drones.Length; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(hit.by_drones[i]);
                }
                sb.Append(" · ");
            }
            if (hit.t_engage_s > 0f && hit.t_free_s > 0f)
                sb.Append(hit.t_engage_s.ToString("F1", CultureInfo.InvariantCulture))
                    .Append(" s of ")
                    .Append(hit.t_free_s.ToString("F1", CultureInfo.InvariantCulture))
                    .Append(" s free");
            return new ScoreRow($"Kill {who}", Pts(hit.reward), sb.Length > 0 ? sb.ToString() : null);
        }

        static void AppendLosses(List<ScoreRow> rows, MissionReport mission)
        {
            var losses = mission.friendly_losses;
            if (losses == null || losses.Count == 0)
            {
                if (mission.friendlies_lost_wasted > 0)
                    rows.Add(new ScoreRow("Wasted drones",
                        mission.friendlies_lost_wasted.ToString(),
                        CauseSummary(mission.losses_by_cause)));
                return;
            }

            for (int i = 0; i < losses.Count; i++)
            {
                var loss = losses[i];
                string cause = Cause(loss.cause);
                if (loss.credited)
                {
                    rows.Add(new ScoreRow($"Drone {loss.drone}", "credited",
                        $"Hit {cause} — one friendly per kill is free."));
                }
                else
                {
                    rows.Add(new ScoreRow($"Drone {loss.drone}", "wasted",
                        $"Hit {cause}. Wasted losses pay P_waste (40 by default)."));
                }
            }
        }

        static string OutcomeLine(SimReport report)
        {
            string outcome = string.IsNullOrEmpty(report.outcome) ? "" : report.outcome.Replace('_', ' ');
            var mission = report.mission;
            if (mission == null || mission.asset_survival_time_s <= 0f) return outcome;
            string held = "held " + mission.asset_survival_time_s.ToString("F1", CultureInfo.InvariantCulture) + " s";
            return string.IsNullOrEmpty(outcome) ? held : outcome + " · " + held;
        }

        static string MissionLine(MissionReport m)
        {
            if (m == null) return null;
            return $"{m.hostiles_destroyed} killed, {m.hostiles_reached_asset} through, " +
                   $"{m.friendlies_lost_wasted} wasted, {m.civilians_lost} civilian";
        }

        static string AwarenessLine(AwarenessReport a)
        {
            if (a == null) return "declare_track, scored once a second on the most recent call.";
            string line = $"{a.correct_declarations} right, {a.wrong_declarations} wrong, {a.samples} samples";
            if (a.wrong_declarations > 0 && a.correct_declarations < a.wrong_declarations)
                line += " · clamped at 0 (guessing cannot go below zero)";
            return line;
        }

        static string DetectionLine(AwarenessReport a)
        {
            if (a == null) return null;
            var sb = new StringBuilder();
            sb.Append(a.compromises_detected).Append(" of ").Append(a.compromises).Append(" named");
            if (a.compromise_detect_latency_s != null)
                sb.Append(" in ").Append(a.compromise_detect_latency_s.Value.ToString("F1", CultureInfo.InvariantCulture)).Append(" s");
            if (a.false_accusations > 0)
                sb.Append(" · ").Append(a.false_accusations).Append(" false accusation(s)");
            return sb.ToString();
        }

        static string CommsLine(CommsReport c)
        {
            if (c == null) return "Zero if the fleet never transmits.";
            string line = c.bytes_per_drone_per_s.ToString("F0", CultureInfo.InvariantCulture) + " B/s per drone";
            if (c.propagation_p95_s != null)
                line += " · p95 " + c.propagation_p95_s.Value.ToString("F2", CultureInfo.InvariantCulture) + " s";
            return line;
        }

        static string CauseSummary(Dictionary<string, int> byCause)
        {
            if (byCause == null || byCause.Count == 0) return null;
            var sb = new StringBuilder();
            foreach (var kv in byCause)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(kv.Value).Append(' ').Append(Cause(kv.Key));
            }
            return sb.ToString();
        }

        static string Cause(string raw) => raw switch
        {
            "pair_hostile" => "a hostile",
            "pair_friendly" => "another friendly",
            "pair_neutral" => "a civilian",
            "wreckage" => "wreckage",
            "ground" => "the ground",
            "arena" => "the arena edge",
            null or "" => "something",
            _ => raw.Replace('_', ' '),
        };

        static string Pts(float? value)
        {
            if (value == null) return "—";
            float v = value.Value;
            string n = v.ToString("0.#", CultureInfo.InvariantCulture);
            if (v > 0.05f) return "+" + n;
            return n;
        }
    }
}

