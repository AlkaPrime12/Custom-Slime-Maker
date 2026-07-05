using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MelonLoader;

namespace CustomSlimeCreator.Core
{
    /// <summary>One discovered fusion (custom×custom or custom×vanilla) shown in the Fusions tab.</summary>
    public class FusionEntry
    {
        public string Key = "";         // unique, order-independent id for the pair
        public string DisplayName = ""; // combined name shown to the player
        // Parent A
        public string AKey = "";        // custom slime name, or vanilla preset name
        public string ADisplay = "";
        public bool ACustom;
        // Parent B
        public string BKey = "";
        public string BDisplay = "";
        public bool BCustom;
    }

    /// <summary>
    /// Persists the set of fusions the player has discovered (to fusions.json) so the "Fusions" tab is
    /// populated across sessions. A fusion is recorded the first time the game forms it.
    /// </summary>
    public static class FusionRegistry
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions { WriteIndented = true, IncludeFields = true };
        private static List<FusionEntry> _entries;

        private static string FilePath => Path.Combine(ConfigStore.Folder, "fusions.json");

        public static List<FusionEntry> All
        {
            get { if (_entries == null) Load(); return _entries; }
        }

        public static int Count => All.Count;

        /// <summary>Stable key for a pair of slimes, independent of which one was eaten first.</summary>
        public static string PairKey(string a, string b)
        {
            a = a ?? ""; b = b ?? "";
            return string.CompareOrdinal(a, b) <= 0 ? a + "+" + b : b + "+" + a;
        }

        public static bool Has(string key) => All.Any(e => e.Key == key);

        /// <summary>Adds a fusion if it isn't already recorded, and persists. Returns true if newly added.</summary>
        public static bool Add(FusionEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.Key)) return false;
            if (Has(entry.Key)) return false;
            All.Add(entry);
            Save();
            MelonLogger.Msg($"[CustomSlimeCreator] Fusion discovered: '{entry.DisplayName}' ({entry.ADisplay} × {entry.BDisplay}).");
            return true;
        }

        private static void Load()
        {
            _entries = new List<FusionEntry>();
            try
            {
                if (File.Exists(FilePath))
                {
                    var list = JsonSerializer.Deserialize<List<FusionEntry>>(File.ReadAllText(FilePath), Options);
                    if (list != null) _entries = list;
                }
            }
            catch (System.Exception ex) { MelonLogger.Warning("[CustomSlimeCreator] FusionRegistry load: " + ex.Message); }
        }

        private static void Save()
        {
            try { File.WriteAllText(FilePath, JsonSerializer.Serialize(_entries, Options)); }
            catch (System.Exception ex) { MelonLogger.Warning("[CustomSlimeCreator] FusionRegistry save: " + ex.Message); }
        }
    }
}
