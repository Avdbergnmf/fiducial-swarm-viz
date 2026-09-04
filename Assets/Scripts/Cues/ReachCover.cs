// Theoretical picket kill envelope, computed in the viewer from recorded
// numbers. Not a control-loop copy: UniqueOwner, scramble delay, and the
// 0.1 s cylinder evidence are all left out on purpose.
//
// Threat: a hostile appears on a ray through the asset, flying params maxv
// (the same proxy as D37) straight at it. First sight is the outer
// intersection of that ray with a living friendly's sense sphere. Divert
// is Reach() around the defender's ballistic point (pose + velocity × t),
// accel lateral_limit, speed cap maxv, matching flight.cpp. A parked
// picket (v=0) is the old from-rest belt. A kill is getting within
// kill_radius of some point on the remaining inbound before the hostile's
// ground track enters the asset cylinder.
//
// Each inbound direction is a cell on a sphere around the asset. The union
// of cells any living friendly can still catch is the safety area. Closed
// means the picket-elevation ring of that sphere has no hole.

using System.Collections.Generic;
using UnityEngine;

namespace SwarmViewer
{
    public readonly struct CoverDefender
    {
        public readonly int Slot;
        public readonly Vector3 Position;
        public readonly Vector3 Velocity;

        public CoverDefender(int slot, Vector3 position, Vector3 velocity)
        {
            Slot = slot;
            Position = position;
            Velocity = velocity;
        }
    }

    public sealed class CoverResult
    {
        public bool Ready;
        public int Live;
        public int Azimuths;
        public int Elevations;
        public int RingElev;
        public float DrawRadius;
        public Vector3 Asset;
        public float AssetRadius;
        public float Sense;
        public float MaxSpeed;
        public float Accel;
        public float Kill;
        public bool[] Covered;
        public float GapDeg;
        public bool Closed;

        public bool Cell(int elev, int az)
        {
            if (Covered == null) return false;
            return Covered[elev * Azimuths + az];
        }
    }

    public static class ReachCover
    {
        public const int Azimuths = 72;
        public const int Elevations = 9;
        public const float ElevMinDeg = 0f;
        public const float ElevMaxDeg = 40f;

        const int TimeSamples = 12;

        public static Vector3 InboundDir(int elev, int az)
        {
            float el = Mathf.Lerp(ElevMinDeg, ElevMaxDeg, Elevations == 1 ? 0f : elev / (float)(Elevations - 1));
            el *= Mathf.Deg2Rad;
            float a = (az / (float)Azimuths) * Mathf.PI * 2f;
            float ce = Mathf.Cos(el);
            return new Vector3(ce * Mathf.Cos(a), Mathf.Sin(el), ce * Mathf.Sin(a));
        }

        public static int RingElevation(IReadOnlyList<CoverDefender> defenders, Vector3 asset)
        {
            float sum = 0f;
            int n = 0;
            for (int i = 0; i < defenders.Count; i++)
            {
                Vector3 d = defenders[i].Position - asset;
                float horiz = Mathf.Sqrt(d.x * d.x + d.z * d.z);
                if (horiz < 8f) continue;
                sum += Mathf.Atan2(d.y, horiz);
                n++;
            }
            float want = n > 0 ? sum / n : 15f * Mathf.Deg2Rad;
            float wantDeg = want * Mathf.Rad2Deg;
            int best = 0;
            float bestD = 1e9f;
            for (int e = 0; e < Elevations; e++)
            {
                float el = Mathf.Lerp(ElevMinDeg, ElevMaxDeg, Elevations == 1 ? 0f : e / (float)(Elevations - 1));
                float d = Mathf.Abs(el - wantDeg);
                if (d < bestD) { bestD = d; best = e; }
            }
            return best;
        }

        public static bool CollectDefenders(
            IReadOnlyList<EntitySnapshot> snaps,
            RunData run,
            List<CoverDefender> into)
        {
            into.Clear();
            if (snaps == null || run == null) return false;
            for (int i = 0; i < snaps.Count; i++)
            {
                if (!snaps[i].Alive) continue;
                if (run.Info(i).drone_id < 0) continue;
                into.Add(new CoverDefender(i, snaps[i].Position, snaps[i].Velocity));
            }
            return into.Count > 0;
        }

        public static CoverResult Evaluate(
            IReadOnlyList<CoverDefender> defenders,
            Vector3 asset,
            float assetRadius,
            float sense,
            float maxSpeed,
            float accel,
            float kill,
            float ringRadius)
        {
            var r = new CoverResult
            {
                Azimuths = Azimuths,
                Elevations = Elevations,
                Asset = asset,
                AssetRadius = assetRadius,
                Sense = sense,
                MaxSpeed = maxSpeed,
                Accel = accel,
                Kill = kill,
                Covered = new bool[Elevations * Azimuths],
                Live = defenders != null ? defenders.Count : 0,
            };

            if (defenders == null || defenders.Count == 0) return r;
            if (sense < 1f || maxSpeed < 0.1f || accel < 0.1f) return r;
            if (assetRadius < 0.1f) return r;

            r.Ready = true;
            r.DrawRadius = ringRadius > 1f ? ringRadius + sense : sense * 2f;
            if (r.DrawRadius < assetRadius + 8f) r.DrawRadius = assetRadius + sense;
            r.RingElev = RingElevation(defenders, asset);

            for (int e = 0; e < Elevations; e++)
            {
                for (int a = 0; a < Azimuths; a++)
                {
                    Vector3 u = InboundDir(e, a);
                    bool ok = false;
                    for (int i = 0; i < defenders.Count; i++)
                    {
                        if (CanCatch(asset, assetRadius, defenders[i].Position,
                                     defenders[i].Velocity, u,
                                     sense, maxSpeed, accel, kill))
                        {
                            ok = true;
                            break;
                        }
                    }
                    r.Covered[e * Azimuths + a] = ok;
                }
            }

            int holes = 0;
            int re = r.RingElev;
            for (int a = 0; a < Azimuths; a++)
                if (!r.Cell(re, a)) holes++;
            r.GapDeg = holes * (360f / Azimuths);
            r.Closed = holes == 0;
            return r;
        }

        /// <summary>
        /// Outer intersection of the inbound ray (asset + s·u, s>0) with the
        /// sense sphere around the picket. False if the ray never hits it
        /// on the way in.
        /// </summary>
        public static bool FirstSight(Vector3 asset, Vector3 picket, Vector3 u,
            float sense, out Vector3 hit, out float s)
        {
            hit = default;
            s = 0f;
            Vector3 d = picket - asset;
            float b = Vector3.Dot(u, d);
            float disc = b * b - Vector3.Dot(d, d) + sense * sense;
            if (disc < 0f) return false;
            s = b + Mathf.Sqrt(disc);
            if (s < 0.5f) return false;
            hit = asset + u * s;
            return true;
        }

        public static bool CanCatch(Vector3 asset, float assetRadius, Vector3 picket,
            Vector3 defenderVel, Vector3 u, float sense, float maxSpeed,
            float accel, float kill)
        {
            if (!FirstSight(asset, picket, u, sense, out Vector3 hit, out _))
                return false;

            Vector3 vel = -u * maxSpeed;
            int cyl = CylinderCase(asset, assetRadius, hit, vel, out float tHit);
            if (cyl == 0) return true; // never enters the cylinder — not a hole
            if (cyl == 1 || tHit <= 0.04f)
                return Vector3.Distance(picket, hit) <= kill + 0.5f;

            for (int i = 0; i <= TimeSamples; i++)
            {
                float t = tHit * (i / (float)TimeSamples);
                if (t < 0.04f) continue;
                Vector3 meet = hit + vel * t;
                Vector3 ballistic = picket + defenderVel * t;
                float need = Vector3.Distance(ballistic, meet) - kill;
                if (need <= 0f) return true;
                if (Reach(t, accel, maxSpeed) >= need) return true;
            }
            return false;
        }

        /// <summary>Same closed-form as flight.cpp Reach: from rest, accel a, cap vmax.</summary>
        public static float Reach(float t, float a, float vmax)
        {
            if (t <= 0f || a < 1e-6f) return 0f;
            if (vmax < 1e-3f) return 0.5f * a * t * t;
            float tv = vmax / a;
            if (t <= tv) return 0.5f * a * t * t;
            return vmax * t - 0.5f * vmax * vmax / a;
        }

        /// <returns>0 miss, 1 already inside, 2 hits at t.</returns>
        static int CylinderCase(Vector3 asset, float radius, Vector3 pos, Vector3 vel, out float t)
        {
            t = 0f;
            Vector3 h0 = new Vector3(pos.x - asset.x, 0f, pos.z - asset.z);
            Vector3 vh = new Vector3(vel.x, 0f, vel.z);
            float r2 = radius * radius;
            float h2 = h0.sqrMagnitude;
            if (h2 <= r2) return 1;

            float a = vh.sqrMagnitude;
            float b = 2f * Vector3.Dot(h0, vh);
            float c = h2 - r2;
            if (a < 1e-8f) return 0;
            float disc = b * b - 4f * a * c;
            if (disc < 0f) return 0;
            float s = Mathf.Sqrt(disc);
            float t0 = (-b - s) / (2f * a);
            float t1 = (-b + s) / (2f * a);
            float pick = t0 > 0.02f ? t0 : t1;
            if (pick <= 0.02f) return 1;
            t = pick;
            return 2;
        }
    }
}
