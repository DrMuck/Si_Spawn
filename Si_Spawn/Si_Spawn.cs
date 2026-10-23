/*
 Si_Spawn - admin spawning of units and structures (the game's console "Spawn" as a chat command).

   !spawn <name> [count]      spawn a unit/structure (prefab or display name, spaces optional) at the current placement
   !spawn team <own|sol|cent|alien|wild|default>
   !spawn at <front|here|aim> placement relative to your unit (sticky per admin)
   !spawn near <player>       placement: next to a player (sticky)
   !spawn xz <x> <z>          placement: map coordinates (sticky)
   !spawn dist <m>            distance for "front" / "near"
   !spawn undo                destroy the last object you spawned
   !spawn clear               destroy everything spawned by this mod this round
   !spawn list [filter]       names (filter: sol|cent|alien|wild|structures or part of a name)
   !spawn status | enabled on|off | reload

 Requires the Admin Mod (Power.Cheat). Exports its catalog + settings to Si_ModUI (ModUI.cs).
*/

using MelonLoader;
using SilicaAdminMod;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

[assembly: MelonInfo(typeof(Si_Spawn.SpawnMod), "Si_Spawn", Si_Spawn.SpawnMod.Version, "DrMuck")]
[assembly: MelonGame("Bohemia Interactive", "Silica")]

namespace Si_Spawn
{
    public partial class SpawnMod : MelonMod
    {
        public const string Version = "1.0.1";
        const string Tag = "<color=#7FD7FF>[SPAWN]</color> ";
        internal static SpawnConfig Cfg = new SpawnConfig();

        static readonly Dictionary<ulong, AdminState> _admins = new Dictionary<ulong, AdminState>();
        static readonly List<SpawnRecord> _spawned = new List<SpawnRecord>();
        static bool _registered, _tookOver;

        public override void OnLateInitializeMelon()
        {
            Cfg = SpawnConfig.Load();
            if (!RegisteredMelons.Any(m => m.Info.Name == "Admin Mod"))
            {
                MelonLogger.Warning("[SPAWN] Admin Mod not found: no commands registered");
                return;
            }
            // Registered straight into the Admin Mod's command list, without the "sam_spawn" console alias that
            // HelperMethods.RegisterAdminCommand adds: Si_SpawnConfigs owns that console key when it loads first
            // (duplicate key exception), and would lose its own later commands if we took the key first.
            try
            {
                TakeOverCommand();
                AdminMethods.AdminCommands.Add(new AdminCommand
                {
                    AdminCommandText = "spawn",
                    AdminCallback = Command_Spawn,
                    AdminPower = Power.Cheat,
                    AdminCommandDescription = "Spawns units/structures at your position. Usage: !spawn <name> [count] | !spawn team|at|near|xz|dist|undo|clear|list|status"
                });
                _registered = true;
                MelonLogger.Msg("[SPAWN] Si_Spawn v" + Version + " loaded, !spawn registered (chat only, no console alias)");
            }
            catch (Exception ex) { MelonLogger.Error("[SPAWN] registration failed: " + ex); }
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            if (!_registered) return;
            TakeOverCommand();
            GameEvents.OnGameStarted -= OnGameStarted; GameEvents.OnGameStarted += OnGameStarted;
            GameEvents.OnGameEnded -= OnGameEnded; GameEvents.OnGameEnded += OnGameEnded;
        }

        static void OnGameStarted(GameMode mode)
        {
            _spawned.Clear();
            foreach (var a in _admins.Values) a.Stack.Clear();
        }

        static void OnGameEnded(GameMode mode, Team winner)
        {
            if (Cfg.DestroyOnRoundEnd) { int n = DestroyAll(); if (n > 0) MelonLogger.Msg("[SPAWN] round end: destroyed " + n + " spawned objects"); }
            _spawned.Clear();
            foreach (var a in _admins.Values) a.Stack.Clear();
        }

        // Si_SpawnConfigs (databomb) registers a bare "!spawn <objectname>" too; the Admin Mod answers with the first
        // registration in its list, so an older duplicate is removed once all mods have initialised.
        static void TakeOverCommand()
        {
            try
            {
                var list = AdminMethods.AdminCommands;
                if (list == null) return;
                var ours = typeof(SpawnMod).Assembly;
                var dupes = list.Where(c => c != null && string.Equals(c.AdminCommandText, "spawn", StringComparison.OrdinalIgnoreCase)
                                             && c.AdminCallback != null && c.AdminCallback.Method.DeclaringType?.Assembly != ours).ToList();
                foreach (var d in dupes)
                {
                    list.Remove(d);
                    if (!_tookOver) MelonLogger.Msg("[SPAWN] replaced the !spawn chat command of " + (d.AdminCallback.Method.DeclaringType?.Assembly.GetName().Name ?? "another mod") + " (its other commands stay)");
                    _tookOver = true;
                }
            }
            catch (Exception ex) { MelonLogger.Warning("[SPAWN] command take-over failed: " + ex.Message); }
        }

        // ------------------------------------------------------------------ command

        public static void Command_Spawn(Player? caller, string args)
        {
            try { Handle(caller, args ?? ""); }
            catch (Exception ex)
            {
                MelonLogger.Error("[SPAWN] " + ex);
                Reply(caller, "Error: " + ex.GetBaseException().Message);
            }
        }

        static void Handle(Player? caller, string args)
        {
            string[] tokens = args.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string sub = tokens.Length > 1 ? tokens[1].ToLowerInvariant() : "";
            string rest = tokens.Length > 2 ? string.Join(" ", tokens, 2, tokens.Length - 2) : "";

            switch (sub)
            {
                case "": case "help": case "?": Help(caller); return;
                case "list": List(caller, rest); return;
                case "status": Status(caller); return;
                case "reload":
                    Cfg = SpawnConfig.Load();
                    Reply(caller, "config reloaded");
                    return;
                case "enabled": case "on": case "off":
                {
                    string v = sub == "enabled" ? rest.ToLowerInvariant() : sub;
                    if (v != "on" && v != "off") { Reply(caller, "enabled: " + (Cfg.Enabled ? "on" : "off") + " (usage: !spawn enabled on|off)"); return; }
                    Cfg.Enabled = v == "on"; Cfg.Save();
                    Reply(caller, "spawning " + (Cfg.Enabled ? "enabled" : "disabled"));
                    if (caller != null && Cfg.AnnounceToAll) HelperMethods.AlertAdminAction(caller, (Cfg.Enabled ? "enabled" : "disabled") + " admin spawning");
                    return;
                }
            }

            if (caller == null) { Reply(caller, "console: only 'list' and 'status' work without a player"); return; }
            var state = StateOf(caller);

            switch (sub)
            {
                case "team":
                {
                    string v = rest.ToLowerInvariant();
                    if (v == "mine" || v == "me" || v == "my") v = "own";
                    if (v == "centauri") v = "cent";
                    if (v == "wildlife") v = "wild";
                    if (v == "none" || v == "prefab") v = "default";
                    if (v != "own" && v != "sol" && v != "cent" && v != "alien" && v != "wild" && v != "default")
                    { Reply(caller, "team: " + state.TeamMode + " (usage: !spawn team own|sol|cent|alien|wild|default)"); return; }
                    state.TeamMode = v;
                    Reply(caller, "spawning for " + TeamLabel(caller, state, null));
                    return;
                }
                case "at":
                {
                    string[] a = rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    string v = a.Length > 0 ? a[0].ToLowerInvariant() : "";
                    if (v == "front" || v == "here" || v == "aim") { state.At = v; Reply(caller, "placement: " + PlacementLabel(state)); return; }
                    if (v == "player" || v == "near")
                    {
                        if (a.Length > 1) { Near(caller, state, string.Join(" ", a, 1, a.Length - 1)); return; }
                        if (state.TargetPlayer.Length == 0) { Reply(caller, "no player chosen yet: !spawn near <player>"); return; }
                        state.At = "player"; Reply(caller, "placement: " + PlacementLabel(state)); return;
                    }
                    if (v == "xz")
                    {
                        if (a.Length > 2) { Xz(caller, state, a[1], a[2]); return; }
                        state.At = "xz"; Reply(caller, "placement: " + PlacementLabel(state)); return;
                    }
                    if (a.Length == 2 && TryFloat(a[0], out _) && TryFloat(a[1], out _)) { Xz(caller, state, a[0], a[1]); return; }
                    if (v.Length > 0) { Near(caller, state, rest); return; }
                    Reply(caller, "placement: " + PlacementLabel(state) + " (usage: !spawn at front|here|aim | !spawn near <player> | !spawn xz <x> <z>)");
                    return;
                }
                case "near": Near(caller, state, rest); return;
                case "xz":
                {
                    string[] a = rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (a.Length < 2) { Reply(caller, "usage: !spawn xz <x> <z>"); return; }
                    Xz(caller, state, a[0], a[1]);
                    return;
                }
                case "dist": case "distance":
                {
                    if (!TryFloat(rest, out float d)) { Reply(caller, "distance: " + Placement.F(state.Dist) + " m (usage: !spawn dist <metres>)"); return; }
                    state.Dist = Mathf.Clamp(d, 0f, 500f);
                    Reply(caller, "distance " + Placement.F(state.Dist) + " m");
                    return;
                }
                case "undo": Undo(caller, state); return;
                case "clear":
                {
                    int n = DestroyAll();
                    Reply(caller, n == 0 ? "nothing to remove" : "removed " + n + " spawned objects");
                    if (n > 0 && Cfg.AnnounceToAll) HelperMethods.AlertAdminAction(caller, "removed " + n + " spawned objects");
                    return;
                }
            }

            // !spawn <name words> [count]
            int count = 1;
            var nameTokens = tokens.Skip(1).ToList();
            if (nameTokens.Count > 1 && int.TryParse(nameTokens[nameTokens.Count - 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int n2))
            {
                count = n2; nameTokens.RemoveAt(nameTokens.Count - 1);
            }
            Spawn(caller, state, string.Join(" ", nameTokens), count);
        }

        // ------------------------------------------------------------------ actions

        static void Spawn(Player caller, AdminState state, string name, int count)
        {
            if (!Cfg.Enabled) { Reply(caller, "spawning is disabled (!spawn enabled on)"); return; }
            if (!Catalog.Ready && Catalog.Entries.Count == 0) { Reply(caller, "catalog not available yet (no map loaded?)"); return; }
            count = Math.Max(1, Math.Min(count, Cfg.MaxCount));

            Team? preferred = PreferredTeam(caller, state);
            var entry = Catalog.Find(name, preferred, out var ambiguous);
            if (entry == null)
            {
                if (ambiguous.Count > 0)
                    Reply(caller, "'" + name + "' matches " + ambiguous.Count + ": " + string.Join(", ", ambiguous.Take(8).Select(e => e.Prefab)) + (ambiguous.Count > 8 ? ", ..." : ""));
                else
                    Reply(caller, "unknown name '" + name + "' (try !spawn list " + name.Split(' ')[0] + ")");
                return;
            }
            Team? team = ResolveTeam(caller, state, entry, out string teamLabel);
            if (!Placement.Resolve(caller, state, Cfg, out Vector3 pos, out Quaternion rot, out string error)) { Reply(caller, error); return; }

            var prefab = Catalog.PrefabOf(entry);
            if (prefab == null) { Reply(caller, "prefab slot " + entry.Index + " is empty"); return; }
            var points = count == 1 ? new List<Vector3> { pos } : Placement.Grid(pos, rot, count, entry.Radius);
            int ok = 0;
            foreach (var p in points)
            {
                GameObject? go = null;
                try { go = Game.SpawnPrefab(prefab, null, team, p, rot, true, true); }
                catch (Exception ex) { MelonLogger.Warning("[SPAWN] SpawnPrefab " + entry.Prefab + " failed: " + ex.Message); }
                if (go == null) continue;
                ok++;
                var rec = new SpawnRecord { Object = go, Name = entry.Display, Team = teamLabel, Admin = SteamOf(caller), AdminName = caller.PlayerName ?? "", Time = Time.realtimeSinceStartup };
                _spawned.Add(rec);
                state.Stack.Add(rec);
            }
            if (ok == 0) { Reply(caller, "spawn of " + entry.Display + " failed (see server log)"); return; }
            string what = (ok > 1 ? ok + "x " : "") + entry.Display + (entry.Display != entry.Prefab ? " (" + entry.Prefab + ")" : "");
            string where = state.At == "here" ? "here" : state.At == "aim" ? "at the aim point" : state.At == "player" ? "near " + state.TargetPlayer : state.At == "xz" ? "at " + Placement.F(pos.x) + " " + Placement.F(pos.z) : Placement.F(state.Dist) + " m ahead";
            MelonLogger.Msg("[SPAWN] " + caller.PlayerName + " spawned " + what + " for " + teamLabel + " at " + Placement.F(pos.x) + " " + Placement.F(pos.y) + " " + Placement.F(pos.z));
            if (Cfg.AnnounceToAll) HelperMethods.AlertAdminAction(caller, "spawned " + what + " for " + teamLabel);
            else Reply(caller, "spawned " + what + " for " + teamLabel + " " + where);
        }

        static void Undo(Player caller, AdminState state)
        {
            while (state.Stack.Count > 0)
            {
                var rec = state.Stack[state.Stack.Count - 1];
                state.Stack.RemoveAt(state.Stack.Count - 1);
                _spawned.Remove(rec);
                if (Kill(rec))
                {
                    Reply(caller, "removed " + rec.Name);
                    if (Cfg.AnnounceToAll) HelperMethods.AlertAdminAction(caller, "removed the spawned " + rec.Name);
                    return;
                }
            }
            Reply(caller, "nothing to undo");
        }

        static int DestroyAll()
        {
            int n = 0;
            foreach (var rec in _spawned.ToArray()) if (Kill(rec)) n++;
            _spawned.Clear();
            foreach (var a in _admins.Values) a.Stack.Clear();
            return n;
        }

        // Same approach as the game's own object removal in Si_SpawnConfigs: networked health to zero.
        static bool Kill(SpawnRecord rec)
        {
            try
            {
                var go = rec.Object;
                if (go == null) return false;
                var bgo = go.GetComponent<BaseGameObject>();
                if (bgo == null || bgo.IsDestroyed) return false;
                var dm = bgo.DamageManager;
                if (dm == null) return false;
                dm.SetHealth01(0f);
                return true;
            }
            catch (Exception ex) { MelonLogger.Warning("[SPAWN] remove " + rec.Name + " failed: " + ex.Message); return false; }
        }

        static void Near(Player caller, AdminState state, string who)
        {
            if (string.IsNullOrWhiteSpace(who)) { Reply(caller, "usage: !spawn near <player>"); return; }
            var target = FindPlayer(who.Trim(), out string why);
            if (target == null) { Reply(caller, why); return; }
            state.TargetPlayer = target.PlayerName ?? who.Trim();
            state.At = "player";
            Reply(caller, "placement: " + PlacementLabel(state));
        }

        static void Xz(Player caller, AdminState state, string xs, string zs)
        {
            if (!TryFloat(xs, out float x) || !TryFloat(zs, out float z)) { Reply(caller, "usage: !spawn xz <x> <z>"); return; }
            state.X = x; state.Z = z; state.At = "xz";
            Reply(caller, "placement: " + PlacementLabel(state));
        }

        static void List(Player? caller, string filter)
        {
            var all = Catalog.Entries;
            if (all.Count == 0) { Reply(caller, "catalog not available yet (no map loaded?)"); return; }
            string f = filter.Trim().ToLowerInvariant();
            IEnumerable<Catalog.Entry> sel = all;
            string title = "all";
            if (f == "sol") { sel = all.Where(e => e.Faction == "Sol"); title = "Sol"; }
            else if (f == "cent" || f == "centauri") { sel = all.Where(e => e.Faction == "Centauri"); title = "Centauri"; }
            else if (f == "alien") { sel = all.Where(e => e.Faction == "Alien"); title = "Alien"; }
            else if (f == "wild" || f == "wildlife") { sel = all.Where(e => e.Faction == "Wildlife"); title = "Wildlife"; }
            else if (f == "other") { sel = all.Where(e => e.Faction != "Sol" && e.Faction != "Centauri" && e.Faction != "Alien" && e.Faction != "Wildlife"); title = "other"; }
            else if (f == "structures" || f == "structure") { sel = all.Where(e => e.IsStructure); title = "structures"; }
            else if (f.Length > 0) { string q = Catalog.Normalize(f); sel = all.Where(e => e.Key.Contains(q) || e.DisplayKey.Contains(q)); title = "'" + filter.Trim() + "'"; }
            var list = sel.ToList();
            if (list.Count == 0) { Reply(caller, "no spawnable prefab matches " + title); return; }
            Reply(caller, list.Count + " spawnable (" + title + "), type !spawn <name>:");
            int lines = 0;
            foreach (var g in list.GroupBy(e => e.Faction + " " + e.Category))
            {
                var names = g.Select(e => e.Display == e.Prefab ? e.Prefab : e.Display).Distinct().ToList();
                foreach (string line in Wrap(g.Key + ": ", names, 110))
                {
                    Reply(caller, line);
                    if (++lines >= 14 && caller != null) { Reply(caller, "... narrow the filter for the rest (sol|cent|alien|wild|structures|<text>)"); return; }
                }
            }
        }

        static void Status(Player? caller)
        {
            string s = "spawning " + (Cfg.Enabled ? "ON" : "OFF") + " | catalog " + Catalog.Entries.Count + " | spawned this round " + _spawned.Count(r => r.Object != null);
            if (caller != null)
            {
                var st = StateOf(caller);
                s += " | you: " + TeamLabel(caller, st, null) + ", " + PlacementLabel(st) + ", undo stack " + st.Stack.Count;
            }
            Reply(caller, s);
        }

        static void Help(Player? caller)
        {
            Reply(caller, "!spawn <name> [count]  -  !spawn list [sol|cent|alien|wild|structures|text]");
            Reply(caller, "!spawn team own|sol|cent|alien|wild|default  -  !spawn at front|here|aim  -  !spawn near <player>  -  !spawn xz <x> <z>  -  !spawn dist <m>");
            Reply(caller, "!spawn undo  -  !spawn clear  -  !spawn status  -  !spawn enabled on|off");
        }

        // ------------------------------------------------------------------ helpers

        internal static AdminState StateOf(Player caller)
        {
            ulong id = SteamOf(caller);
            if (!_admins.TryGetValue(id, out var s))
            {
                s = new AdminState { TeamMode = Cfg.DefaultTeam, At = Cfg.DefaultPlacement, Dist = Cfg.DefaultDistance };
                _admins[id] = s;
            }
            return s;
        }
        internal static AdminState? StateOf(ulong steamId) => _admins.TryGetValue(steamId, out var s) ? s : null;

        internal static ulong SteamOf(Player p)
        {
            try { return p.PlayerID.SteamID.m_SteamID; } catch { return 0; }
        }

        static Team? PreferredTeam(Player caller, AdminState state)
        {
            switch (state.TeamMode)
            {
                case "own": try { return caller.Team; } catch { return null; }
                case "default": return null;
                default: return FindTeam(state.TeamMode);
            }
        }

        static Team? ResolveTeam(Player caller, AdminState state, Catalog.Entry entry, out string label)
        {
            Team? team = state.TeamMode == "default" ? entry.DefaultTeam : PreferredTeam(caller, state);
            if (team == null && state.TeamMode == "own") team = entry.DefaultTeam;   // admin without a team: prefab default
            label = team == null ? "no team" : NameOf(team);
            return team;
        }

        static string TeamLabel(Player caller, AdminState state, Catalog.Entry? entry)
        {
            switch (state.TeamMode)
            {
                case "own": { Team? t = null; try { t = caller.Team; } catch { } return "your team" + (t != null ? " (" + NameOf(t) + ")" : ""); }
                case "default": return "the prefab's default team";
                default: { var t = FindTeam(state.TeamMode); return t != null ? NameOf(t) : state.TeamMode + " (team not found!)"; }
            }
        }

        static string PlacementLabel(AdminState s)
        {
            switch (s.At)
            {
                case "here": return "at your unit";
                case "aim": return "at your aim point";
                case "player": return "near " + s.TargetPlayer + " (" + Placement.F(s.Dist) + " m)";
                case "xz": return "at " + Placement.F(s.X) + " " + Placement.F(s.Z);
                default: return Placement.F(s.Dist) + " m in front of your unit";
            }
        }

        internal static string NameOf(Team t)
        {
            try { string s = t.TeamShortName; if (!string.IsNullOrEmpty(s)) return s; } catch { }
            try { return t.name; } catch { return "team"; }
        }

        internal static Team? FindTeam(string word)
        {
            string w = word.ToLowerInvariant();
            if (w == "cent") w = "centauri";
            if (w == "wild") w = "wildlife";
            List<Team> teams;
            try { teams = Team.Teams; } catch { return null; }
            if (teams == null) return null;
            Team? best = null; int bestScore = 0;
            foreach (var t in teams)
            {
                if (t == null) continue;
                string shortName = "", longName = "", asset = "";
                try { shortName = (t.TeamShortName ?? "").ToLowerInvariant(); } catch { }
                try { longName = (t.TeamName ?? "").ToLowerInvariant(); } catch { }
                try { asset = (t.name ?? "").ToLowerInvariant(); } catch { }
                int score = 0;
                if (shortName == w) score = 5;
                else if (shortName.StartsWith(w)) score = 4;
                else if (longName == w) score = 3;
                else if (longName.StartsWith(w)) score = 2;
                else if (asset.EndsWith("_" + w) || asset == w) score = 1;
                if (score > bestScore) { best = t; bestScore = score; }
            }
            return best;
        }

        internal static Player? FindPlayer(string query, out string why)
        {
            why = "";
            List<Player> players;
            try { players = Player.Players; } catch { why = "player list unavailable"; return null; }
            if (players == null) { why = "player list unavailable"; return null; }
            string q = query.Trim();
            var exact = players.Where(p => p != null && string.Equals(p.PlayerName, q, StringComparison.OrdinalIgnoreCase)).ToList();
            if (exact.Count == 1) return exact[0];
            var partial = players.Where(p => p != null && (p.PlayerName ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            if (partial.Count == 1) return partial[0];
            why = partial.Count == 0 ? "no player matches '" + q + "'" : "'" + q + "' matches " + partial.Count + " players: " + string.Join(", ", partial.Take(5).Select(p => p.PlayerName));
            return null;
        }

        static bool TryFloat(string s, out float v) => float.TryParse((s ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v);

        static IEnumerable<string> Wrap(string prefix, List<string> items, int width)
        {
            var line = new System.Text.StringBuilder(prefix);
            bool first = true;
            foreach (string it in items)
            {
                if (!first && line.Length + it.Length + 2 > width) { yield return line.ToString(); line.Clear().Append("   "); first = true; }
                if (!first) line.Append(", ");
                line.Append(it); first = false;
            }
            if (line.Length > 0) yield return line.ToString();
        }

        static void Reply(Player? p, string msg)
        {
            if (p == null) { MelonLogger.Msg("[SPAWN] " + msg); return; }
            HelperMethods.SendChatMessageToPlayer(p, Tag + msg);
        }
    }
}
