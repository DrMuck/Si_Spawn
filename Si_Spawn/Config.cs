using System;
using System.IO;
using MelonLoader;
using MelonLoader.Utils;
using Newtonsoft.Json;

namespace Si_Spawn
{
    /// <summary>
    /// UserData/cfg/Spawn/Si_Spawn_Config.json (the server's own cfg/ folder, next to the other mods).
    /// </summary>
    public class SpawnConfig
    {
        public bool Enabled = true;
        // Placement defaults for an admin who has not changed them in the session.
        public string DefaultTeam = "own";          // own | sol | cent | alien | wild | default
        public string DefaultPlacement = "front";   // front | here | aim
        public float DefaultDistance = 12f;         // metres in front of the admin's unit ("front")
        public float MaxAimDistance = 600f;         // "aim": how far the look ray is traced before falling back to "front"
        public int MaxCount = 25;                   // upper bound for "!spawn <name> <count>"
        public bool AnnounceToAll = true;           // broadcast "<admin> spawned ..." like other admin actions
        public bool DestroyOnRoundEnd = false;      // kill every object spawned by this mod when the round ends

        public static string Path => System.IO.Path.Combine(MelonEnvironment.UserDataDirectory, "cfg", "Spawn", "Si_Spawn_Config.json");

        public static SpawnConfig Load()
        {
            SpawnConfig cfg = new SpawnConfig();
            try
            {
                if (File.Exists(Path))
                {
                    cfg = JsonConvert.DeserializeObject<SpawnConfig>(File.ReadAllText(Path)) ?? new SpawnConfig();
                    MelonLogger.Msg("[SPAWN] Config loaded from " + Path);
                }
                else
                {
                    cfg.Save();
                    MelonLogger.Msg("[SPAWN] Default config written to " + Path);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[SPAWN] Config load failed (" + ex.Message + "), using defaults");
            }
            cfg.Sanitize();
            return cfg;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                File.WriteAllText(Path, JsonConvert.SerializeObject(this, Formatting.Indented));
            }
            catch (Exception ex) { MelonLogger.Warning("[SPAWN] Config save failed: " + ex.Message); }
        }

        void Sanitize()
        {
            if (DefaultDistance < 0f) DefaultDistance = 0f;
            if (DefaultDistance > 500f) DefaultDistance = 500f;
            if (MaxAimDistance < 10f) MaxAimDistance = 10f;
            if (MaxCount < 1) MaxCount = 1;
            if (MaxCount > 200) MaxCount = 200;
            DefaultTeam = (DefaultTeam ?? "own").Trim().ToLowerInvariant();
            DefaultPlacement = (DefaultPlacement ?? "front").Trim().ToLowerInvariant();
            if (DefaultPlacement != "front" && DefaultPlacement != "here" && DefaultPlacement != "aim") DefaultPlacement = "front";
        }
    }
}
