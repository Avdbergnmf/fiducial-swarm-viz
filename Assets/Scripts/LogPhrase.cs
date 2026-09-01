// Reads the brain's log lines back into English.
//
// The wire format is terse on purpose. Log records are the one record type the
// recording does not decimate (FORMAT.md), and the budget is finite: overflow
// shows up as a log row with drone -1 and the rest of the run is silent
// (CHALLENGE.md 11.3). So the brain writes short and the expansion happens
// here, where it costs the run nothing and can be turned off.
//
// Every phrase is a rewording of fields already on the line. Nothing is
// inferred, and the raw text stays one click away behind the Raw toggle. The
// one thing this adds is the frame of reference: the brain writes close= for
// three different things -- closing on the asset, on us, on the tracked craft
// -- and the raw line does not say which.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SwarmViewer
{
    public static class LogPhrase
    {
        /// <summary>First token of a log line: call, drop, near, commit, …</summary>
        public static string Verb(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "other";
            int end = raw.IndexOf(' ');
            return end < 0 ? raw : raw.Substring(0, end);
        }

        /// <summary>
        /// Reconstruct Policy::Stance at time t from the sparse transition logs.
        /// Forming / Picketing / Committed. near and ram are proximity, not stance.
        /// </summary>
        public static string StanceAt(IReadOnlyList<LogLine> logs, float t)
        {
            string last = null;
            if (logs != null)
            {
                for (int i = 0; i < logs.Count; i++)
                {
                    if (logs[i].t > t + 0.001f) break;
                    string v = Verb(logs[i].text);
                    if (v == "commit" || v == "abort" || v == "picket")
                        last = logs[i].text;
                }
            }

            if (last == null)
                return "Flying to its ring slot. Every drone starts Forming; it logs picket once it is within 8 m of the slot.";

            return Verb(last) switch
            {
                "picket" => "On station: holding its ring slot and watching. It has not spent itself on an intercept.",
                "commit" => "Intercept — " + Humanize(last),
                "abort" => "Back on the ring. " + Humanize(last) + ".",
                _ => Humanize(last),
            };
        }

        public static string Humanize(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            try
            {
                switch (Verb(raw))
                {
                    case "call":
                    case "drop":
                    case "wreck": return Classification(raw);
                    case "commit": return Commit(raw);
                    case "abort": return Abort(raw);
                    case "near":
                    case "ram": return Proximity(raw);
                    case "picket": return "In position on the picket ring.";
                    case "params": return Params(raw);
                    case "drone": return Boot(raw);
                    default: return raw;
                }
            }
            catch (Exception)
            {
                // A phrasing bug must never hide the line it was meant to explain.
                return raw;
            }
        }

        /// <summary>Raw line plus a key for the fields it uses.</summary>
        public static string Tooltip(string raw)
        {
            string legend = Legend(Verb(raw));
            return string.IsNullOrEmpty(legend) ? raw : raw + "\n\n" + legend;
        }

        static string Legend(string verb) => verb switch
        {
            "call" or "drop" or "wreck" =>
                "miss   3D closest approach to the asset origin (classification).\n" +
                "       Breach is a vertical cylinder of radius asset_radius, any altitude.\n" +
                "first  the same figure when it was first classified\n" +
                "score  integrated approach evidence; commit needs a Hostile call plus catchable geometry\n" +
                "align  heading vs bearing to the asset in the horizontal plane, 1 = straight at it\n" +
                "close  horizontal closing speed on the asset",
            "commit" =>
                "score  integrated approach evidence\n" +
                "miss   3D closest approach to the asset on its current course\n" +
                "rng    range from us to it\n" +
                "close  relative horizontal closing speed between us and it (both velocities)\n" +
                "ttg    time until it reaches the asset cylinder",
            "near" or "ram" =>
                "rng    range from us to it\n" +
                "close  closing speed between us and it\n" +
                "Bands are 12 m, 6 m and 3 m; one line per band crossed.",
            _ => "",
        };

        // ---- verbs ---------------------------------------------------------

        // "<verb> trk=%u <class> miss=%.1f first=%.1f score=%.2f align=%.2f close=%.1f[ peer]"
        static string Classification(string raw)
        {
            var tok = raw.Split(' ');
            string verb = tok[0];
            string cls = tok.Length > 2 ? tok[2] : "unknown";
            bool peer = raw.EndsWith(" peer", StringComparison.Ordinal);
            string subject = Track(Int(raw, "trk="), peer);

            string head = verb switch
            {
                "drop" => $"Dropped {subject} back to unknown",
                "wreck" => $"Marked {subject} as wreckage",
                _ => $"Called {subject} {ClassWord(cls)}",
            };

            float miss = Num(raw, "miss=");
            float first = Num(raw, "first=");
            string trend = "";
            if (!float.IsNaN(miss) && !float.IsNaN(first))
            {
                float shrink = first - miss;
                if (shrink >= 3f) trend = $" ({Metres(shrink)} closer than at first sight)";
                else if (shrink <= -3f) trend = $" ({Metres(-shrink)} wider than at first sight)";
            }

            return Join(head,
                float.IsNaN(miss) ? "" : $"passes {Metres(miss)} from the asset{trend}",
                Closing(Num(raw, "close="), "the asset"),
                Aim(Num(raw, "align=")),
                Evidence(Num(raw, "score=")));
        }

        // "commit trk=%u score=%.2f miss=%.1f rng=%.0f close=%.1f ttg=%.1f"
        static string Commit(string raw)
        {
            float ttg = Num(raw, "ttg=");
            float miss = Num(raw, "miss=");
            string reach = float.IsNaN(ttg) ? ""
                : float.IsNaN(miss) ? $"reaches the cylinder in {ttg:F1} s"
                : $"reaches the cylinder in {ttg:F1} s, passing {Metres(miss)} from the origin";

            return Join($"Committed to {Track(Int(raw, "trk="), false)}",
                Range(Num(raw, "rng=")),
                Closing(Num(raw, "close="), "us"),
                reach,
                Evidence(Num(raw, "score=")));
        }

        // "abort trk=%u <reason>"
        static string Abort(string raw)
        {
            var tok = raw.Split(' ');
            string reason = tok.Length > 2 ? tok[2] : "";
            return Join($"Broke off {Track(Int(raw, "trk="), false)}", reason switch
            {
                "lost" => "the track disappeared",
                "timeout" => "ran out of time on the intercept",
                "not-hostile" => "it no longer reads as hostile",
                "not-closing" => "the range stopped shrinking",
                "" => "",
                _ => reason,
            });
        }

        // "<near|ram> trk=%u class=%s rng=%.1f close=%.1f"
        static string Proximity(string raw)
        {
            bool ram = Verb(raw) == "ram";
            string subject = Track(Int(raw, "trk="), false);
            string cls = Word(raw, "class=");
            if (!string.IsNullOrEmpty(cls)) subject += $" ({ClassWord(cls)})";

            float rng = Num(raw, "rng=");
            string head = ram
                ? $"Terminal run on {subject}"
                : float.IsNaN(rng) ? $"Close pass on {subject}" : $"Within {Metres(rng)} of {subject}";

            return Join(head,
                ram && !float.IsNaN(rng) ? Metres(rng) + " out" : "",
                Closing(Num(raw, "close="), "us"));
        }

        // "params sense=… comm=… maxv=… maxa=… tilt=… lat=… sep=… fsep=… ring=… alt=…"
        static string Params(string raw) => Join("Boot parameters",
            Field(raw, "sense=", "sense", "m"),
            Field(raw, "comm=", "radio", "m"),
            Field(raw, "maxv=", "max speed", "m/s"),
            Field(raw, "maxa=", "max accel", "m/s\u00b2"),
            Field(raw, "lat=", "lateral limit", "m/s\u00b2"),
            Field(raw, "sep=", "separation", "m"),
            Field(raw, "fsep=", "friendly keep-out", "m"),
            Field(raw, "ring=", "picket ring", "m"),
            Field(raw, "alt=", "ring altitude", "m"));

        // "drone %u/%u up, lateral limit %.2f m/s^2, kill r %.1f, tier %u"
        static string Boot(string raw)
        {
            int slash = raw.IndexOf('/');
            string who = "Online";
            if (slash > 0)
            {
                int id = Int(raw, "drone ");
                int fleet = (int)Num(raw, "/");
                if (id >= 0 && fleet > 0) who = $"Online as drone {id} of {fleet}";
            }
            return Join(who,
                Field(raw, "lateral limit ", "lateral limit", "m/s\u00b2"),
                Field(raw, "kill r ", "kill radius", "m"),
                Field(raw, "tier ", "tier", ""));
        }

        // ---- phrase pieces -------------------------------------------------

        static string Track(int id, bool peer) =>
            peer || id <= 0 ? "a peer-reported track" : $"track {id}";

        static string ClassWord(string cls) => cls switch
        {
            "wreck" => "wreckage",
            null or "" => "unknown",
            _ => cls,
        };

        static string Closing(float close, string what)
        {
            if (float.IsNaN(close)) return "";
            if (close >= 0.5f) return $"closing on {what} at {close:F1} m/s";
            if (close <= -0.5f) return $"pulling away from {what} at {-close:F1} m/s";
            return $"holding range on {what}";
        }

        static string Aim(float align) => float.IsNaN(align) ? "" : align switch
        {
            >= 0.95f => "aimed straight in",
            >= 0.6f => "angled toward the asset",
            >= 0.2f => "drifting toward the asset",
            > -0.2f => "crossing the asset",
            > -0.6f => "drifting away",
            _ => "pointed away",
        };

        static string Evidence(float score) =>
            float.IsNaN(score) ? "" : $"evidence {score:F2}";

        // The brain writes commit's rng at whole-metre precision, so F1 here
        // would invent a digit it never had.
        static string Range(float rng) =>
            float.IsNaN(rng) ? "" : rng.ToString("F0", CultureInfo.InvariantCulture) + " m out";

        static string Metres(float m) =>
            Math.Abs(m) >= 100f
                ? m.ToString("F0", CultureInfo.InvariantCulture) + " m"
                : m.ToString("F1", CultureInfo.InvariantCulture) + " m";

        static string Field(string raw, string key, string label, string unit)
        {
            if (!TryNum(raw, key, out float v)) return "";
            string n = Math.Abs(v - Math.Round(v)) < 0.05
                ? v.ToString("F0", CultureInfo.InvariantCulture)
                : v.ToString("F1", CultureInfo.InvariantCulture);
            return string.IsNullOrEmpty(unit) ? $"{label} {n}" : $"{label} {n} {unit}";
        }

        static string Join(params string[] parts)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < parts.Length; i++)
            {
                if (string.IsNullOrEmpty(parts[i])) continue;
                if (sb.Length > 0) sb.Append(" \u00b7 ");
                sb.Append(parts[i]);
            }
            return sb.ToString();
        }

        // ---- field scanning ------------------------------------------------

        static bool TryNum(string s, string key, out float value)
        {
            value = 0f;
            if (string.IsNullOrEmpty(s)) return false;
            int i = s.IndexOf(key, StringComparison.Ordinal);
            if (i < 0) return false;
            i += key.Length;

            int start = i;
            if (i < s.Length && (s[i] == '-' || s[i] == '+')) i++;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.')) i++;
            if (i == start) return false;

            return float.TryParse(s.Substring(start, i - start), NumberStyles.Float,
                CultureInfo.InvariantCulture, out value);
        }

        static float Num(string s, string key) => TryNum(s, key, out float v) ? v : float.NaN;

        static int Int(string s, string key) => TryNum(s, key, out float v) ? (int)v : -1;

        /// <summary>The bare word after a key, e.g. "unknown" from "class=unknown".</summary>
        static string Word(string s, string key)
        {
            if (string.IsNullOrEmpty(s)) return "";
            int i = s.IndexOf(key, StringComparison.Ordinal);
            if (i < 0) return "";
            i += key.Length;
            int end = s.IndexOf(' ', i);
            return end < 0 ? s.Substring(i) : s.Substring(i, end - i);
        }
    }
}
