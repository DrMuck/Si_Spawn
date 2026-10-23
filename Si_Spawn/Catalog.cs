using System;
using System.Collections.Generic;
using System.Linq;
using MelonLoader;
using UnityEngine;

namespace Si_Spawn
{
    /// <summary>
    /// Everything the server can spawn: GameDatabase.AllNetworkedPrefabs filtered to prefabs that carry a Unit or
    /// Structure component. Built once (the database does not change between maps), indexed by prefab slot so a
    /// null slot (0.9.47) can never break a lookup. Names are resolved loosely: prefab name, display name, with
    /// spaces/underscores ignored, exact before prefix before substring.
    /// </summary>
    internal static class Catalog
    {
        public sealed class Entry
        {
            public int Index;                 // slot in AllNetworkedPrefabs
            public string Prefab = "";        // GameObject name (unique, the token the ModUI sends)
            public string Display = "";       // ObjectInfo.DisplayName (may collide across factions, e.g. Harvester)
            public string Faction = "Other";  // Sol | Centauri | Alien | Wildlife | <short name> | Other
            public string Category = "Other"; // Structures | Infantry | Vehicles | Aircraft | Creatures | Other
            public Team? DefaultTeam;
            public float Radius = 2f;
            public bool IsStructure;
            public string Key = "";           // normalized prefab name
            public string DisplayKey = "";    // normalized display name
        }

        static List<Entry>? _entries;
        static int _builtCount = -1;

        public static IReadOnlyList<Entry> Entries
        {
            get { Build(); return _entries ?? (IReadOnlyList<Entry>)Array.Empty<Entry>(); }
        }

        public static bool Ready => _entries != null;

        public static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char c in s)
                if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            return sb.ToString();
        }

        static void Build()
        {
            List<GameObject>? list;
            try
            {
                var db = GameDatabase.Database;
                if (db == null) return;
                list = db.AllNetworkedPrefabs;
            }
            catch { return; }
            if (list == null) return;
            if (_entries != null && _builtCount == list.Count) return;

            var entries = new List<Entry>();
            int nullSlots = 0;
            for (int i = 0; i < list.Count; i++)
            {
                GameObject go;
                try { go = list[i]; } catch { continue; }
                if (go == null) { nullSlots++; continue; }
                BaseGameObject? bgo = null;
                try { bgo = go.GetComponent<BaseGameObject>(); } catch { }
                if (bgo == null) continue;
                string category = CategoryOf(bgo, out bool isStructure, out bool isUnit);
                if (!isStructure && !isUnit) continue;

                var e = new Entry { Index = i, Prefab = go.name ?? ("prefab" + i), Category = category, IsStructure = isStructure };
                try
                {
                    var info = bgo.ObjectInfo;
                    if (info != null && !string.IsNullOrEmpty(info.DisplayName)) e.Display = info.DisplayName;
                    if (info != null) e.DefaultTeam = info.DefaultTeam;
                }
                catch { }
                if (e.Display.Length == 0) e.Display = e.Prefab;
                if (e.DefaultTeam == null) { try { e.DefaultTeam = bgo.DefaultTeam; } catch { } }
                try { float r = bgo.PhysicalRadius; if (r > 0.1f) e.Radius = r; } catch { }
                e.Faction = FactionOf(e.DefaultTeam);
                e.Key = Normalize(e.Prefab);
                e.DisplayKey = Normalize(e.Display);
                entries.Add(e);
            }
            entries.Sort((a, b) =>
            {
                int c = FactionOrder(a.Faction).CompareTo(FactionOrder(b.Faction)); if (c != 0) return c;
                c = CategoryOrder(a.Category).CompareTo(CategoryOrder(b.Category)); if (c != 0) return c;
                c = string.Compare(a.Display, b.Display, StringComparison.OrdinalIgnoreCase); if (c != 0) return c;
                return string.Compare(a.Prefab, b.Prefab, StringComparison.OrdinalIgnoreCase);
            });
            _entries = entries;
            _builtCount = list.Count;
            MelonLogger.Msg("[SPAWN] Catalog: " + entries.Count + " spawnable prefabs out of " + list.Count + " networked (" +
                            string.Join(", ", entries.GroupBy(x => x.Faction).Select(g => g.Key + " " + g.Count())) +
                            (nullSlots > 0 ? ", " + nullSlots + " null slots skipped" : "") + ")");
        }

        // Walk the type chain by name so the build does not bind to classes that may differ between game branches.
        static string CategoryOf(BaseGameObject bgo, out bool isStructure, out bool isUnit)
        {
            isStructure = false; isUnit = false;
            string category = "Other";
            for (Type? t = bgo.GetType(); t != null && t != typeof(object); t = t.BaseType)
            {
                switch (t.Name)
                {
                    case "Structure": isStructure = true; return "Structures";
                    case "Unit": isUnit = true; return category;
                    case "Human": case "Soldier": if (category == "Other") category = "Infantry"; break;
                    case "VehicleAir": case "BomberCraft": if (category == "Other" || category == "Vehicles") category = "Aircraft"; break;
                    case "Vehicle": if (category == "Other") category = "Vehicles"; break;
                    case "Alien": case "Creature": if (category == "Other") category = "Creatures"; break;
                }
            }
            return category;
        }

        public static string FactionOf(Team? team)
        {
            if (team == null) return "Other";
            string s = "";
            try { s = team.TeamShortName ?? ""; } catch { }
            if (s.Length == 0) { try { s = team.name ?? ""; } catch { } }
            string l = s.ToLowerInvariant();
            if (l.Contains("sol")) return "Sol";
            if (l.Contains("cent")) return "Centauri";
            if (l.Contains("wild")) return "Wildlife";
            if (l.Contains("alien")) return "Alien";
            return s.Length > 0 ? s : "Other";
        }

        static int FactionOrder(string f)
        {
            switch (f) { case "Sol": return 0; case "Centauri": return 1; case "Alien": return 2; case "Wildlife": return 3; case "Other": return 9; default: return 5; }
        }
        static int CategoryOrder(string c)
        {
            switch (c) { case "Structures": return 0; case "Infantry": return 1; case "Vehicles": return 2; case "Aircraft": return 3; case "Creatures": return 4; default: return 9; }
        }

        /// <summary>Resolve a typed name. Exact prefab > exact display > prefix > substring; ties on display name are
        /// broken by the preferred team (the team the admin spawns for), then reported as ambiguous.</summary>
        public static Entry? Find(string query, Team? preferTeam, out List<Entry> ambiguous)
        {
            ambiguous = new List<Entry>();
            string q = Normalize(query);
            if (q.Length == 0) return null;
            var all = Entries;
            var exact = all.Where(e => e.Key == q).ToList();
            if (exact.Count == 1) return exact[0];
            if (exact.Count == 0) exact = all.Where(e => e.DisplayKey == q).ToList();
            if (exact.Count == 0) exact = all.Where(e => e.Key.StartsWith(q) || e.DisplayKey.StartsWith(q)).ToList();
            if (exact.Count == 0) exact = all.Where(e => e.Key.Contains(q) || e.DisplayKey.Contains(q)).ToList();
            if (exact.Count == 0) return null;
            if (exact.Count == 1) return exact[0];
            if (preferTeam != null)
            {
                var mine = exact.Where(e => e.DefaultTeam == preferTeam).ToList();
                if (mine.Count == 1) return mine[0];
            }
            // several hits that share one display name and faction (variants) -> take the shortest prefab name
            var byDisplay = exact.GroupBy(e => e.DisplayKey + "|" + e.Faction).ToList();
            if (byDisplay.Count == 1) return exact.OrderBy(e => e.Prefab.Length).First();
            ambiguous = exact;
            return null;
        }

        public static GameObject? PrefabOf(Entry e)
        {
            try { return GameDatabase.GetSpawnablePrefab(e.Index); } catch { return null; }
        }
    }
}
