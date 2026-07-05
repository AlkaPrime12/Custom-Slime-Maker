using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MelonLoader;
using MelonLoader.Utils;

namespace CustomSlimeCreator.Core
{
    /// <summary>Loads and saves <see cref="SlimeConfig"/> JSON files from UserData/CustomSlimeCreator.</summary>
    public static class ConfigStore
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true,
            IncludeFields = true,
        };

        private static string _folder;

        public static string Folder
        {
            get
            {
                if (_folder == null)
                {
                    _folder = Path.Combine(MelonEnvironment.UserDataDirectory, "CustomSlimeCreator");
                    Directory.CreateDirectory(_folder);
                }
                return _folder;
            }
        }

        public static List<SlimeConfig> LoadAll()
        {
            var result = new List<SlimeConfig>();
            try
            {
                foreach (var file in Directory.GetFiles(Folder, "*.json").OrderBy(f => f))
                {
                    // fusions.json is a separate registry (List<FusionEntry>), not a slime config — skip it.
                    if (string.Equals(Path.GetFileName(file), "fusions.json", System.StringComparison.OrdinalIgnoreCase)) continue;
                    try
                    {
                        var cfg = JsonSerializer.Deserialize<SlimeConfig>(File.ReadAllText(file), Options);
                        if (cfg != null && !string.IsNullOrWhiteSpace(cfg.Name))
                            result.Add(cfg);
                    }
                    catch (System.Exception ex)
                    {
                        MelonLogger.Warning($"[CustomSlimeCreator] Failed to read '{Path.GetFileName(file)}': {ex.Message}");
                    }
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"[CustomSlimeCreator] LoadAll failed: {ex.Message}");
            }
            return result;
        }

        public static void Save(SlimeConfig cfg)
        {
            var path = Path.Combine(Folder, Sanitize(cfg.Name) + ".json");
            File.WriteAllText(path, JsonSerializer.Serialize(cfg, Options));
            MelonLogger.Msg($"[CustomSlimeCreator] Saved preset '{cfg.Name}'.");
        }

        public static void Delete(string name)
        {
            var path = Path.Combine(Folder, Sanitize(name) + ".json");
            if (File.Exists(path))
            {
                File.Delete(path);
                MelonLogger.Msg($"[CustomSlimeCreator] Deleted preset '{name}'.");
            }
        }

        public static string Sanitize(string name)
        {
            if (string.IsNullOrEmpty(name)) return "unnamed";
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }
    }
}
