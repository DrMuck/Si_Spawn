using System;
using System.Collections.Generic;
using UnityEngine;

namespace Si_Spawn
{
    /// <summary>
    /// Where a spawn goes. The dedicated server has no camera, so "camera position" becomes: the admin's unit
    /// (here), a point in front of it (front), the point the unit looks at (aim, a ray along the view direction),
    /// next to another player (player) or map coordinates (xz). Everything but "here" is snapped to the terrain.
    /// </summary>
    internal static class Placement
    {
        public static bool Resolve(Player caller, AdminState s, SpawnConfig cfg, out Vector3 pos, out Quaternion rot, out string error)
        {
            pos = Vector3.zero; rot = Quaternion.identity; error = "";
            Unit? unit = null;
            try { unit = caller.ControlledUnit; } catch { }
            if (unit != null)
            {
                try { rot = Quaternion.Euler(0f, unit.GetFacingRotation().eulerAngles.y, 0f); } catch { rot = Quaternion.identity; }
            }
            switch (s.At)
            {
                case "player":
                {
                    var target = SpawnMod.FindPlayer(s.TargetPlayer, out string why);
                    if (target == null) { error = "near " + s.TargetPlayer + ": " + why; return false; }
                    Unit? tu = null; try { tu = target.ControlledUnit; } catch { }
                    if (tu == null) { error = target.PlayerName + " controls no unit right now"; return false; }
                    Vector3 fwd = Flat(tu.transform.forward);
                    try { rot = Quaternion.Euler(0f, tu.GetFacingRotation().eulerAngles.y, 0f); fwd = Flat(rot * Vector3.forward); } catch { }
                    pos = Ground(tu.transform.position + fwd * Math.Max(4f, s.Dist), tu.transform.position.y);
                    return true;
                }
                case "xz":
                {
                    if (!Ground(new Vector3(s.X, 0f, s.Z), out pos)) { error = "no terrain at " + F(s.X) + " " + F(s.Z); return false; }
                    return true;
                }
            }
            if (unit == null)
            {
                error = "you control no unit (commanders: use !spawn near <player> or !spawn xz <x> <z>)";
                return false;
            }
            Vector3 origin = unit.transform.position;
            Vector3 forward = Flat(rot * Vector3.forward);
            switch (s.At)
            {
                case "here":
                    pos = origin;
                    return true;
                case "aim":
                {
                    Vector3 eye = origin, dir = forward;
                    try { eye = unit.WorldLineOfSightCenter; } catch { }
                    try { dir = unit.GetViewDirection(); } catch { }
                    if (dir.sqrMagnitude < 0.001f) dir = forward;
                    dir.Normalize();
                    float skip = 1f; try { skip = Math.Max(1f, unit.PhysicalRadius + 0.5f); } catch { }
                    int mask = (int)GamePhysics.TRACELAYERMASK_TERRAIN | (int)GamePhysics.TRACELAYERMASK_STATIC;
                    if (Physics.Raycast(eye + dir * skip, dir, out RaycastHit hit, cfg.MaxAimDistance, mask))
                    {
                        pos = hit.point;
                        return true;
                    }
                    // nothing in range (looking at the sky): fall back to the distance point along the flat direction
                    Vector3 flat = Flat(dir);
                    if (flat.sqrMagnitude < 0.001f) flat = forward;
                    pos = Ground(origin + flat * Math.Max(1f, s.Dist), origin.y);
                    return true;
                }
                default: // front
                    pos = Ground(origin + forward * s.Dist, origin.y);
                    return true;
            }
        }

        /// <summary>Grid of offsets for a multi-spawn: row by row behind the first position, spacing from the prefab radius.</summary>
        public static List<Vector3> Grid(Vector3 center, Quaternion rot, int count, float radius)
        {
            var result = new List<Vector3>();
            float spacing = Math.Max(3f, radius * 2.2f);
            int cols = (int)Math.Ceiling(Math.Sqrt(count));
            Vector3 right = Flat(rot * Vector3.right), fwd = Flat(rot * Vector3.forward);
            for (int i = 0; i < count; i++)
            {
                int r = i / cols, c = i % cols;
                float cx = (c - (cols - 1) / 2f) * spacing;
                Vector3 p = center + right * cx + fwd * (r * spacing);
                result.Add(r == 0 && c == (cols - 1) / 2 ? center : Ground(p, center.y));
            }
            return result;
        }

        public static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude < 0.0001f ? Vector3.forward : v.normalized; }

        /// <summary>Terrain height under x/z (ray from high above); keeps fallbackY when there is no terrain hit.</summary>
        public static Vector3 Ground(Vector3 p, float fallbackY)
        {
            return Ground(p, out Vector3 g) ? g : new Vector3(p.x, fallbackY, p.z);
        }
        public static bool Ground(Vector3 p, out Vector3 ground)
        {
            ground = p;
            try
            {
                if (Physics.Raycast(new Vector3(p.x, 3000f, p.z), Vector3.down, out RaycastHit hit, 6000f, GamePhysics.TRACELAYERMASK_TERRAIN))
                {
                    ground = hit.point;
                    return true;
                }
            }
            catch { }
            return false;
        }

        public static string F(float v) => v.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Per-admin sticky settings for the session (keyed by Steam ID).</summary>
    internal sealed class AdminState
    {
        public string TeamMode = "own";     // own | sol | cent | alien | wild | default
        public string At = "front";         // front | here | aim | player | xz
        public string TargetPlayer = "";
        public float X, Z;
        public float Dist = 12f;
        public readonly List<SpawnRecord> Stack = new List<SpawnRecord>();
    }

    internal sealed class SpawnRecord
    {
        public GameObject? Object;
        public string Name = "";
        public string Team = "";
        public ulong Admin;
        public string AdminName = "";
        public float Time;
    }
}
