# Si_Spawn

Admin spawning for Silica dedicated servers: the game's console `Spawn` (any networked unit or structure, for any
team, at a position) as an Admin Mod chat command, with a unit catalog in Si_ModUI.

Requires the [Admin Mod](https://github.com/data-bomb/Silica) (`Power.Cheat`). Optional: Si_ModUI 1.5.0 or newer
(server + client) for the clickable catalog.

## Chat commands (admins with the Cheat power)

| Command | Effect |
|---|---|
| `!spawn <name> [count]` | Spawn a unit or structure. `name` = prefab name or display name, spaces optional (`!spawn light quad`, `!spawn LightQuad`, `!spawn Crab 5`). |
| `!spawn list [sol\|cent\|alien\|wild\|structures\|other\|text]` | Print spawnable names to the chat. |
| `!spawn team own\|sol\|cent\|alien\|wild\|default` | Team of the next spawns (sticky per admin). `own` = your team, `default` = the prefab's own faction. |
| `!spawn at front\|here\|aim\|player\|base\|xz` | Placement relative to your unit: `front` = distance ahead, `here` = beside your unit, `aim` = where your unit looks (ray along the view direction, terrain and structures); `player`, `base`, `xz` re-select the last target chosen with the commands below. |
| `!spawn near <player>` | Placement: next to that player's unit (sticky). |
| `!spawn base sol\|cent\|alien\|wild` | Placement: beside that team's HQ or Nest, outside its footprint, facing the base (sticky). Falls back to any alive structure of the team. |
| `!spawn xz <x> <z>` | Placement: map coordinates, snapped to the terrain (sticky). |
| `!spawn dist <m>` | Distance for `front` and `near` (default 12 m). |
| `!spawn undo` | Destroy the last object you spawned (walks your own stack). |
| `!spawn clear` | Destroy everything spawned through this mod in the current round. |
| `!spawn status` | Settings, catalog size, spawn count of the round. |
| `!spawn enabled on\|off`, `!spawn reload` | Master switch (saved to the config), re-read the config. |

**Free space.** Every mode yields an anchor (your unit, the target player's unit, the HQ/Nest, the aim point or the
coordinates) and the object goes to the nearest free terrain spot around it: rings starting right outside the anchor's
physical radius, angles fanning out from the preferred direction, a candidate passes when the terrain is not steeper
than about 40 degrees and an overlap sphere of the prefab's size hits nothing but terrain (units, vehicles, structures
and static props all block). Several objects (`count`) each get their own spot. If nothing within ~14 rings is free the
object is placed on the nearest candidate anyway and the admin is told.
Commanders control no unit: use `base <team>`, `near <player>` or `xz`. Spawns are announced like other admin actions
(`AnnounceToAll`). Objects are removed by setting their networked health to zero, the same way Si_SpawnConfigs does.

Name resolution: exact prefab name, exact display name, prefix, substring. A display name that exists for several
factions (Harvester) is resolved by the team you spawn for; otherwise the command lists the candidates.

If databomb's Si_SpawnConfigs is installed too, its bare `!spawn <object>` is replaced by this one (logged at start);
its `!undospawn`, `!saveconfig`, `!loadconfig`, `!addconfig` stay.

## Si_ModUI

`ModUIData()` exports the full catalog (faction > category > unit; display name shown, prefab name sent) as an
admin catalog (`admin`, `all`, `credits: false`), plus the settings as switch / value / picker rows: Team, Placement,
Distance, Spawn near player, Map coordinates, Undo, Remove all, Status, List, Admin spawning on/off, Reload.
`ModUIPlayerData(steamId)` highlights the admin's own sticky Team / Placement / Distance.

## Config

`UserData/cfg/Spawn/Si_Spawn_Config.json` (written with defaults on first start):

| Key | Default | Meaning |
|---|---|---|
| `Enabled` | true | Master switch. |
| `DefaultTeam` | `own` | Team mode for an admin who has not chosen one. |
| `DefaultPlacement` | `front` | `front`, `here` or `aim`. |
| `DefaultDistance` | 12 | Metres for `front` / `near`. |
| `MaxAimDistance` | 600 | Length of the `aim` ray before falling back to `front`. |
| `MaxCount` | 25 | Upper bound for `!spawn <name> <count>`. |
| `AnnounceToAll` | true | Broadcast spawns as admin actions. |
| `DestroyOnRoundEnd` | false | Kill every spawned object when the round ends. |

## Build

`Si_Spawn/Si_Spawn.csproj` (netstandard2.1) references `include/<branch>/*.dll`, copied from the server install of
that Steam branch (`test` = default value, `default` = the public branch):

```
dotnet build -c Release                           # test branch  -> bin/Release/netstandard2.1/Si_Spawn.dll
dotnet build -c Release -p:SilicaBranch=default   # default      -> bin/Release/default/Si_Spawn.dll
```

The mod binds to nothing branch-specific (`Game.SpawnPrefab`, `GameDatabase.AllNetworkedPrefabs`, Physics raycasts);
both builds pass a reference check against SilicaCore 0.9.47 and 1.0.4. Null slots in `AllNetworkedPrefabs`
(0.9.47) are skipped; lookups go by slot index, never by `GetSpawnablePrefabIndex(string)`.
