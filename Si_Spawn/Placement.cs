using System;
using System.Collections.Generic;
using UnityEngine;

namespace Si_Spawn
{
    /// <summary>
    /// Where a spawn goes. The dedicated server has no camera, so "camera position" becomes: the admin's unit
    /// (here), a point in front of it (front), the point the unit looks at (aim, a ray along the view direction),
    /// next to another player (player), next to a team's HQ / Nest (base) or map coordinates (xz).
    /// Every mode yields an ANCHOR (position + radius of the thing to keep clear); FindFreeSpot then walks rings
    /// around it until a terrain point without units, structures or static props is found.
    /// </summary>
    internal static class Placement
    {
        public sealed class Anchor
        {
            public Vector3 Position;
            public float Radius;          // radius of the object at the anchor that must stay clear (0 = none)
            public Vector3 Direction;     // preferred direction from the anchor (flat, normalized)
            public Quaternion Rotation;   // facing of the spawned objects
        }

        public static bool Resolve(Player caller, AdminState s, SpawnConfig cfg, out Anchor anchor, out string error)
        {
            anchor = new Anchor { Direction = Vector3.forward, Rotation = Quaternion.identity };
            error = "";
            Unit? unit = null;
            try { unit = caller.ControlledUnit; } catch { }
            if (unit != null)
            {
                try { anchor.Rotation = Quaternion.Euler(0f, unit.GetFacingRotation().eulerAngles.y, 0f); } catch { anchor.Rotation = Quaternion.identity; }
                anchor.Direction = Flat(anchor.Rotation * Vector3.forward);
            }
            switch (s.At)
            {
                case "player":
                {
                    var target = SpawnMod.FindPlayer(s.TargetPlayer, out string why);
                    if (target == null) { error = "near " + s.TargetPlayer + ": " + why; return false; }
                    Unit? tu = null; try { tu = target.ControlledUnit; } catch { }
                    if (tu == null) { error = target.PlayerName + " controls no unit right now"; return false; }
                    anchor.Position = tu.transform.position;
                    anchor.Radius = RadiusOf(tu);
                    try { anchor.Rotation = Quaternion.Euler(0f, tu.GetFacingRotation().eulerAngles.y, 0f); } catch { }
                    anchor.Direction = Flat(anchor.Rotation * Vector3.forward);
                    return true;
                }
                case "base":
                {
                    var team = SpawnMod.FindTeam(s.BaseTeam);
                    if (team == null) { error = "team '" + s.BaseTeam + "' not found"; return false; }
                    var hq = BaseStructureOf(team);
                    if (hq == null) { error = SpawnMod.NameOf(team) + " has no HQ / Nest alive"; return false; }
                    anchor.Position = hq.transform.position;
                    anchor.Radius = RadiusOf(hq);
                    // away from the base, towards the admin's unit if there is one, else the structure's facing
                    Vector3 dir = unit != null ? Flat(unit.transform.position - hq.transform.position) : Flat(hq.transform.forward);
                    anchor.Direction = dir;
                    anchor.Rotation = Quaternion.LookRotation(-dir, Vector3.up);   // spawned objects face the base
                    return true;
                }
                case "xz":
                {
                    if (!Ground(new Vector3(s.X, 0f, s.Z), out Vector3 g)) { error = "no terrain at " + F(s.X) + " " + F(s.Z); return false; }
                    anchor.Position = g;
                    return true;
                }
            }
            if (unit == null)
            {
                error = "you control no unit (commanders: use !spawn base <team>, !spawn near <player> or !spawn xz <x> <z>)";
                return false;
            }
            Vector3 origin = unit.transform.position;
            Vector3 forward = anchor.Direction;
            switch (s.At)
            {
                case "here":
                    anchor.Position = origin;
                    anchor.Radius = RadiusOf(unit);
                    return true;
                case "aim":
                {
                    Vector3 eye = origin, dir = forward;
                    try { eye = unit.WorldLineOfSightCenter; } catch { }
                    try { dir = unit.GetViewDirection(); } catch { }
                    if (dir.sqrMagnitude < 0.001f) dir = forward;
                    dir.Normalize();
                    float skip = Math.Max(1f, RadiusOf(unit) + 0.5f);
                    int mask = (int)GamePhysics.TRACELAYERMASK_TERRAIN | (int)GamePhysics.TRACELAYERMASK_STATIC;
                    if (Physics.Raycast(eye + dir * skip, dir, out RaycastHit hit, cfg.MaxAimDistance, mask))
                    {
                        anchor.Position = hit.point;
                        return true;
                    }
                    // nothing in range (looking at the sky): fall back to the distance point along the flat direction
                    Vector3 flat = Flat(dir);
                    anchor.Position = Ground(origin + flat * Math.Max(1f, s.Dist), origin.y);
                    return true;
                }
                default: // front
                    anchor.Position = Ground(origin + forward * s.Dist, origin.y);
                    return true;
            }
        }

        /// <summary>
        /// A terrain point near the anchor with nothing on it. Rings around the anchor starting right outside its
        /// radius, each ring tried at angles fanning out from the preferred direction; a candidate passes when the
        /// terrain is not too steep and an overlap sphere of the prefab's size hits nothing but terrain. `reserved`
        /// holds spots chosen earlier in the same command (multi-spawn). Returns false (and the raw offset point)
        /// when every candidate is blocked.
        /// </summary>
        public static bool FindFreeSpot(Anchor anchor, float prefabRadius, List<Vector3> reserved, out Vector3 spot, out float distance)
        {
            float r = Math.Max(0.5f, prefabRadius);
            float margin = 1.5f;
            float first = anchor.Radius + r + margin;
            float step = Math.Max(2.5f, r * 1.2f);
            spot = Ground(anchor.Position + anchor.Direction * first, anchor.Position.y);
            distance = first;
            int mask = OverlapMask();
            for (int ring = 0; ring < 14; ring++)
            {
                float d = first + ring * step;
                // angles: 0, +30, -30, +60, -60 ... up to 180
                int steps = ring == 0 && anchor.Radius <= 0f ? 1 : 12;
                for (int k = 0; k < steps; k++)
                {
                    float angle = k == 0 ? 0f : (k + 1) / 2 * 30f * ((k % 2 == 1) ? 1f : -1f);
                    if (Math.Abs(angle) > 180f) break;
                    Vector3 dir = Quaternion.Euler(0f, angle, 0f) * anchor.Direction;
                    Vector3 p = anchor.Position + dir * d;
                    if (!Ground(p, out Vector3 g, out Vector3 normal)) continue;
                    if (normal.y < 0.75f) continue;                                   // steeper than ~40 degrees
                    bool clash = false;
                    foreach (var q in reserved) if (Vector2.Distance(new Vector2(q.x, q.z), new Vector2(g.x, g.z)) < 2f * r + 1f) { clash = true; break; }
                    if (clash) continue;
                    try
                    {
                        var hits = Physics.OverlapSphere(g + Vector3.up * (r * 0.6f + 0.3f), r + 0.5f, mask, QueryTriggerInteraction.Ignore);
                        if (hits != null && hits.Length > 0) continue;
                    }
                    catch { }
                    spot = g; distance = d;
                    return true;
                }
            }
            return false;
        }

        // Everything a spawn must not sit on: units, vehicles, structures, props. Terrain, triggers, UI and water are fine.
        static int _overlapMask;
        static int OverlapMask()
        {
            if (_overlapMask != 0) return _overlapMask;
            int exclude = (int)GamePhysics.TRACELAYERMASK_TERRAIN | (int)GamePhysics.TRACELAYERMASK_TRIGGER | (int)GamePhysics.TRACELAYERMASK_UI;
            try { exclude |= LayerMask.GetMask("Water"); } catch { }
            try { exclude |= LayerMask.GetMask("Ignore Raycast"); } catch { }
            _overlapMask = ~exclude;
            return _overlapMask;
        }

        public static Structure? BaseStructureOf(Team team)
        {
            try
            {
                var info = team.BaseStructure;
                if (info != null)
                {
                    var hq = team.GetFirstStructureOfType(info);
                    if (hq != null) return hq;
                }
                // no HQ / Nest alive: any alive structure of the team
                foreach (var st in team.Structures)
                    if (st != null && !st.IsDestroyed) return st;
            }
            catch { }
            return null;
        }

        public static float RadiusOf(BaseGameObject? o)
        {
            try { if (o != null) return Math.Max(0.5f, o.PhysicalRadius); } catch { }
            return 1f;
        }

        public static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude < 0.0001f ? Vector3.forward : v.normalized; }

        /// <summary>Terrain height under x/z (ray from high above); keeps fallbackY when there is no terrain hit.</summary>
        public static Vector3 Ground(Vector3 p, float fallbackY)
        {
            return Ground(p, out Vector3 g) ? g : new Vector3(p.x, fallbackY, p.z);
        }
        public static bool Ground(Vector3 p, out Vector3 ground) => Ground(p, out ground, out _);
        public static bool Ground(Vector3 p, out Vector3 ground, out Vector3 normal)
        {
            ground = p; normal = Vector3.up;
            try
            {
                if (Physics.Raycast(new Vector3(p.x, 3000f, p.z), Vector3.down, out RaycastHit hit, 6000f, GamePhysics.TRACELAYERMASK_TERRAIN))
                {
                    ground = hit.point; normal = hit.normal;
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
        public string At = "front";         // front | here | aim | player | base | xz
        public string TargetPlayer = "";
        public string BaseTeam = "";        // sol | cent | alien | wild  (for At == "base")
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
