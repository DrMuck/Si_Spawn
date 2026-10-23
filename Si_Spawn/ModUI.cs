using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MelonLoader;

namespace Si_Spawn
{
    // Si_ModUI export: mirrors the !spawn chat commands only; chat stays fully usable without the UI.
    // Admin-only mod: every entry is admin, the catalog carries "admin": true so the client shows it on the admin tab.
    public partial class SpawnMod
    {
        static string? _cache; static string _cacheKey = "";

        public static string? ModUIData()
        {
            try
            {
                // ModUIData runs every few seconds; the document only changes with the config or the catalog.
                string key = Cfg.Enabled + "|" + Cfg.DefaultTeam + "|" + Cfg.DefaultPlacement + "|" + Cfg.DefaultDistance.ToString(CultureInfo.InvariantCulture) + "|" + Cfg.MaxCount + "|" + Catalog.Entries.Count;
                if (_cache != null && key == _cacheKey) return _cache;
                var x = ModUI.ModUIExport.New()
                    .Enabled(Cfg.Enabled)
                    .Info("Admin spawning " + (Cfg.Enabled ? "ON" : "OFF") + " | catalog " + Catalog.Entries.Count + " prefabs | max " + Cfg.MaxCount + " per command")
                    .Info("Click a unit to spawn it with the Team / Placement settings below. Chat: !spawn <name> [count]")
                    .Switch("!spawn team", "Team", "Which team the spawned objects belong to. MINE = your current team, DEFAULT = the prefab's own faction.", admin: true, current: Cfg.DefaultTeam,
                            ("own", "MINE"), ("sol", "SOL"), ("cent", "CENTAURI"), ("alien", "ALIEN"), ("wild", "WILDLIFE"), ("default", "DEFAULT"))
                    .Switch("!spawn at", "Placement", "FRONT = distance ahead of your unit, HERE = your unit's position, AIM = where your unit looks, PLAYER / XZ = the last target chosen below.", admin: true, current: Cfg.DefaultPlacement,
                            ("front", "FRONT"), ("here", "HERE"), ("aim", "AIM"), ("player", "PLAYER"), ("xz", "XZ"))
                    .Value("!spawn dist", "Distance (m)", "Metres ahead for FRONT and beside a player for PLAYER placement.", admin: true, current: Cfg.DefaultDistance)
                    .Picker("!spawn near", "Spawn near player", "Places the next spawns next to the chosen player (sticky until you change the placement).", admin: true, arg: "player")
                    .Value("!spawn xz", "Map coordinates", "Type x z (two numbers) to place spawns at map coordinates (sticky).", admin: true, current: "")
                    .Action("!spawn undo", "Undo last spawn", "Destroys the last object you spawned.", admin: true)
                    .Action("!spawn clear", "Remove all spawned", "Destroys everything spawned through this mod in the current round.", admin: true)
                    .Action("!spawn status", "Status", "Your current team / placement settings and the spawn count of this round.", admin: true)
                    .Picker("!spawn list", "List names in chat", "Prints spawnable names of one group to the chat.", admin: true, arg: "choice",
                            ("sol", "Sol"), ("cent", "Centauri"), ("alien", "Alien"), ("wild", "Wildlife"), ("structures", "Structures"), ("other", "Other"))
                    .OnOff("!spawn enabled", "Admin spawning", "Master switch of this mod.", admin: true, on: Cfg.Enabled)
                    .Action("!spawn reload", "Reload config", "Re-read Si_Spawn_Config.json.", admin: true);
                var catalog = x.Catalog("!spawn", Cfg.Enabled)
                    .Extra("admin", true).Extra("all", true).Extra("credits", false)
                    .Extra("offText", "Admin spawning is switched off (Admin spawning: ON below).");
                var entries = Catalog.Entries;
                foreach (var faction in entries.GroupBy(e => e.Faction))
                {
                    catalog.Faction(faction.Key);
                    foreach (var cat in faction.GroupBy(e => e.Category))
                    {
                        catalog.Category(cat.Key);
                        // Display names collide inside a faction only for variants; keep the prefab name visible then.
                        var dupes = new HashSet<string>(cat.GroupBy(e => e.Display).Where(g => g.Count() > 1).Select(g => g.Key));
                        foreach (var e in cat) catalog.Item(e.Prefab, 0, dupes.Contains(e.Display) ? e.Display + " [" + e.Prefab + "]" : e.Display);
                    }
                }
                _cache = catalog.Done().Build(); _cacheKey = key;
                return _cache;
            }
            catch (Exception exception) { MelonLogger.Warning("[SPAWN] ModUIData: " + exception.Message); return null; }
        }

        // The admin's own sticky settings highlight the switch rows (Si_ModUI per-player hook).
        public static string? ModUIPlayerData(ulong steamId)
        {
            try
            {
                var s = StateOf(steamId);
                if (s == null) return null;
                return ModUI.ModUIExport.PlayerValues(
                    ("!spawn team", s.TeamMode),
                    ("!spawn at", s.At),
                    ("!spawn dist", s.Dist.ToString("0.#", CultureInfo.InvariantCulture)),
                    ("!spawn xz", s.At == "xz" ? Placement.F(s.X) + " " + Placement.F(s.Z) : ""));
            }
            catch (Exception exception) { MelonLogger.Warning("[SPAWN] ModUIPlayerData: " + exception.Message); return null; }
        }
    }
}
