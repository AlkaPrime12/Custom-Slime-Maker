using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher;
using Il2CppMonomiPark.SlimeRancher.Input;
using Il2CppMonomiPark.SlimeRancher.Economy;
using Il2CppMonomiPark.SlimeRancher.UI;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CustomSlimeCreator.Core
{
    /// <summary>
    /// Direct access to the game's context/directors. Handles player-input blocking
    /// (so the camera stops while the editor is open) and registering a custom slime definition into
    /// the game's data model so it saves, is vaccable and behaves like a real slime.
    /// </summary>
    public static class GameAccess
    {
        private static GameContext _gc;
        private static Dictionary<string, IdentifiableTypeGroup> _groups;

        public static GameContext GC
        {
            get
            {
                if (_gc != null) return _gc;
                try { var a = Resources.FindObjectsOfTypeAll<GameContext>(); if (a != null && a.Length > 0) _gc = a[0]; } catch { }
                return _gc;
            }
        }

        // ---------------------------------------------------------------- custom name (localization)

        /// <summary>
        /// Builds a real LocalizedString for a custom display name by adding an entry to the game's
        /// "Actor" string table — this is what makes the slime show YOUR name instead of the base's.
        /// </summary>
        public static UnityEngine.Localization.LocalizedString MakeName(string text)
        {
            try
            {
                var db = UnityEngine.Localization.Settings.LocalizationSettings.StringDatabase;
                if (db == null) return null;
                var table = db.GetTable("Actor");
                if (table == null) return null;
                string key = "csc." + Guid.NewGuid().ToString("N");
                var entry = table.AddEntry(key, text ?? "Custom Slime");
                if (entry == null) return null;
                return new UnityEngine.Localization.LocalizedString(table.TableCollectionName, entry.KeyId);
            }
            catch (Exception ex) { MelonLogger.Warning("[CustomSlimeCreator] MakeName: " + ex.Message); return null; }
        }

        // ---------------------------------------------------------------- input / camera

        public static void SetPlayerInput(bool enabled)
        {
            try
            {
                var gc = GC; if (gc == null) return;
                var id = gc.InputDirector; if (id == null) return;
                ToggleMap(id._mainGame, enabled);
                ToggleMap(id._paused, enabled);
            }
            catch { /* input director not ready yet */ }
        }

        private static void ToggleMap(InputActionMapReference r, bool enabled)
        {
            try
            {
                if (r == null || r.Map == null) return;
                if (enabled) r.Map.Enable(); else r.Map.Disable();
            }
            catch { }
        }

        // ---------------------------------------------------------------- identity (referenceId + stable hash)

        /// <summary>
        /// Gives a cloned IdentifiableType (slime def or plort) its OWN identity. Object.Instantiate copies the
        /// base's <c>referenceId</c> AND its cached <c>stableHashedId</c>/<c>initializedHashId</c>, so without this a
        /// clone masquerades as the base everywhere — saves resolve to the base (custom slime "reverts" on reload),
        /// and the market/pedia call the plort by the base's name/value. We overwrite the real <c>referenceId</c>
        /// field (NOT "_referenceId" — that field doesn't exist) and clear the cached hash so the game recomputes it.
        /// </summary>
        public static void ForceReferenceId(IdentifiableType t, string refId)
        {
            if (t == null || string.IsNullOrEmpty(refId)) return;
            try { t.referenceId = refId; } catch (Exception ex) { MelonLogger.Warning("[CSC] set referenceId: " + ex.Message); }
            try { t.initializedHashId = false; } catch { }
            try { t.stableHashedId = 0; } catch { }
            // Read StableHashedId once to force the native getter to recompute + recache the hash from the new id.
            try { var _ = t.StableHashedId; } catch { } // touch to force recompute; no logging (kept quiet)
        }

        // ---------------------------------------------------------------- plort economy (market value)

        /// <summary>
        /// Makes a custom plort fully sellable at the Plort Market: (1) adds it to the market's plort LIST
        /// (MarketUIConfiguration._plorts) so the shop shows/accepts it, (2) adds a value CONFIG to the economy's
        /// PlortsTable so its price varies day-to-day like vanilla, and (3) seeds the runtime current-value map so it
        /// has a price immediately. Re-applied on every build so it survives restarts / economy recalcs.
        /// </summary>
        public static bool SetPlortValue(IdentifiableType plort, int value)
        {
            if (plort == null) return false;
            float sat = System.Math.Max(50f, value * 5f); // how fast the price sinks as you flood the market
            bool ok = false;

            // --- Economy: value config (daily variation) + runtime current value ---
            try
            {
                var econ = SceneContext.Instance != null ? SceneContext.Instance.PlortEconomyDirector : null;
                if (econ != null)
                {
                    // (a) PlortsTable config → the daily market simulation knows this plort + its base value.
                    Try(() =>
                    {
                        var settings = econ._settings;
                        if (settings == null) return;
                        var table = settings.PlortsTable;
                        var arr = table.Plorts;
                        if (!VcContains(arr, plort))
                        {
                            var cfg = new PlortValueConfiguration();
                            cfg.Type = plort; cfg.InitialValue = value; cfg.FullSaturation = sat;
                            table.Plorts = AppendVc(arr, cfg);
                            settings.PlortsTable = table;   // write the (value-type) table back
                            econ._settings = settings;
                        }
                    });
                    // (b) runtime current value → immediate price.
                    Try(() =>
                    {
                        var map = econ._currValueMap;
                        if (map == null) return;
                        var entry = new PlortEconomyDirector.CurrValueEntry((float)value, (float)value, (float)value, sat);
                        if (map.ContainsKey(plort)) map.Remove(plort);
                        map.Add(plort, entry);
                        ok = true;
                    });
                }
            }
            catch (Exception ex) { MelonLogger.Warning("[CustomSlimeMaker] plort economy: " + ex.Message); }

            // --- Market list: add the plort to every MarketUIConfiguration so the shop lists + buys it ---
            try
            {
                var cfgs = Resources.FindObjectsOfTypeAll<MarketUIConfiguration>();
                if (cfgs != null)
                    for (int i = 0; i < cfgs.Length; i++)
                    {
                        var c = cfgs[i]; if (c == null) continue;
                        var arr = c._plorts;
                        if (PeContains(arr, plort)) continue;
                        var pe = new PlortEntry(); pe.IdentType = plort;
                        c._plorts = AppendPe(arr, pe);
                        ok = true;
                    }
            }
            catch (Exception ex) { MelonLogger.Warning("[CustomSlimeMaker] plort market list: " + ex.Message); }

            if (ok) MelonDebug.Msg($"[CustomSlimeMaker] Plort '{SafeName(plort)}' sellable at {value}.");
            return ok;
        }

        private static bool VcContains(Il2CppReferenceArray<PlortValueConfiguration> a, IdentifiableType p)
        { if (a == null) return false; for (int i = 0; i < a.Length; i++) { try { if (a[i] != null && a[i].Type == p) return true; } catch { } } return false; }
        private static Il2CppReferenceArray<PlortValueConfiguration> AppendVc(Il2CppReferenceArray<PlortValueConfiguration> a, PlortValueConfiguration item)
        { int n = a != null ? a.Length : 0; var r = new Il2CppReferenceArray<PlortValueConfiguration>(n + 1); for (int i = 0; i < n; i++) r[i] = a[i]; r[n] = item; return r; }
        private static bool PeContains(Il2CppReferenceArray<PlortEntry> a, IdentifiableType p)
        { if (a == null) return false; for (int i = 0; i < a.Length; i++) { try { if (a[i] != null && a[i].IdentType == p) return true; } catch { } } return false; }
        private static Il2CppReferenceArray<PlortEntry> AppendPe(Il2CppReferenceArray<PlortEntry> a, PlortEntry item)
        { int n = a != null ? a.Length : 0; var r = new Il2CppReferenceArray<PlortEntry>(n + 1); for (int i = 0; i < n; i++) r[i] = a[i]; r[n] = item; return r; }

        // ---------------------------------------------------------------- largo / fusion registry

        /// <summary>
        /// Registers a largo definition into the game's fusion lookup dictionaries so eating the matching plort
        /// natively transforms a slime into it. Adds both key orderings for robustness.
        /// </summary>
        public static bool RegisterLargo(SlimeDefinition largo, SlimeDefinition a, SlimeDefinition b,
                                         IdentifiableType plortA, IdentifiableType plortB)
        {
            var gc = GC;
            if (gc == null || largo == null || a == null || b == null) return false;
            var defs = gc.SlimeDefinitions;
            if (defs == null) return false;
            try
            {
                var byDefs = defs._largoDefinitionByBaseDefinitions;
                if (byDefs != null)
                {
                    AddPairDef(byDefs, new SlimeDefinitions.SlimeDefinitionPair(a, b), largo);
                    AddPairDef(byDefs, new SlimeDefinitions.SlimeDefinitionPair(b, a), largo);
                }
            }
            catch (Exception ex) { MelonLogger.Warning("[CSC] RegisterLargo(defs): " + ex.Message); }
            try
            {
                var byPlorts = defs._largoDefinitionByBasePlorts;
                if (byPlorts != null && plortA != null && plortB != null)
                {
                    AddPairPlort(byPlorts, new SlimeDefinitions.PlortPair(plortA, plortB), largo);
                    AddPairPlort(byPlorts, new SlimeDefinitions.PlortPair(plortB, plortA), largo);
                }
            }
            catch (Exception ex) { MelonLogger.Warning("[CSC] RegisterLargo(plorts): " + ex.Message); }
            return true;
        }

        private static void AddPairDef(Il2CppSystem.Collections.Generic.Dictionary<SlimeDefinitions.SlimeDefinitionPair, SlimeDefinition> d,
                                       SlimeDefinitions.SlimeDefinitionPair key, SlimeDefinition val)
        { try { if (d.ContainsKey(key)) return; d.Add(key, val); } catch { } }

        private static void AddPairPlort(Il2CppSystem.Collections.Generic.Dictionary<SlimeDefinitions.PlortPair, SlimeDefinition> d,
                                         SlimeDefinitions.PlortPair key, SlimeDefinition val)
        { try { if (d.ContainsKey(key)) return; d.Add(key, val); } catch { } }

        private static string SafeName(IdentifiableType t) { try { return t != null ? t.name : null; } catch { return null; } }

        // ---------------------------------------------------------------- registration

        /// <summary>Registers a custom slime definition so it saves/loads, is vaccable and fully functional.
        /// Pass <paramref name="vaccable"/>=false for fusion LARGOS — they're too big to vac and shouldn't be
        /// counted as small slimes.</summary>
        public static bool RegisterSlime(SlimeDefinition def, bool vaccable = true)
        {
            var gc = GC;
            if (gc == null || def == null) return false;
            string refId = null;
            try { refId = def.ReferenceId; } catch { }
            if (string.IsNullOrEmpty(refId)) return false;

            var lookup = gc.LookupDirector;
            var defs = gc.SlimeDefinitions;
            var asd = gc.AutoSaveDirector;

            // 1) Slime definition registry
            Try(() => { if (!defs._slimeDefinitionsByIdentifiable.ContainsKey(def)) defs._slimeDefinitionsByIdentifiable.Add(def, def); });
            Try(() => { if (!ArrayContains(defs.Slimes, def)) defs.Slimes = Append(defs.Slimes, def); });

            // 2) Identifiable lookup by reference id
            Try(() => { if (!lookup._identifiableTypeByRefId.ContainsKey(refId)) lookup._identifiableTypeByRefId.Add(refId, def); });

            // 3) The master identifiable-types group
            Try(() =>
            {
                var grp = asd._configuration._identifiableTypes;
                if (grp != null) lookup.AddIdentifiableTypeToGroup(def, grp);
            });

            // 4) Save reference translation (persistence)
            Try(() =>
            {
                var t = asd._saveReferenceTranslation;
                if (!t._identifiableTypeLookup.ContainsKey(refId)) t._identifiableTypeLookup.Add(refId, def);
                var pid = t._identifiableTypeToPersistenceId;
                if (pid._primaryIndex.Length > 0 && !ArrayContainsStr(pid._primaryIndex, refId))
                    pid._primaryIndex = AppendStr(pid._primaryIndex, refId);
                if (!pid._reverseIndex.ContainsKey(refId))
                    pid._reverseIndex.Add(refId, pid._reverseIndex.Count);
            });

            // 5) Gameplay groups (vaccable / edible / small / base / slimes). Rescan groups fresh —
            // they may not have been loaded the first time this ran.
            _groups = null;
            var wanted = vaccable
                ? new[] { "VaccableBaseSlimeGroup", "BaseSlimeGroup", "SlimesGroup", "SmallSlimeGroup",
                          "EdibleSlimeGroup", "IdentifiableTypesGroup", "SlimesSinkInShallowWaterGroup" }
                // Largos: NOT vaccable and NOT "small" — otherwise they'd be suckable gordos.
                : new[] { "SlimesGroup", "EdibleSlimeGroup", "IdentifiableTypesGroup" };
            var added = new List<string>();
            var missing = new List<string>();
            foreach (var g in wanted)
                if (AddToGroup(def, g)) added.Add(g); else missing.Add(g);

            MelonDebug.Msg($"[CustomSlimeMaker] Registered '{refId}'. Groups: [{string.Join(", ", added)}]"
                + (missing.Count > 0 ? $"  MISSING: [{string.Join(", ", missing)}]" : ""));
            return true;
        }

        private static bool AddToGroup(SlimeDefinition def, string groupName)
        {
            try
            {
                var grp = Group(groupName);
                if (grp == null) return false;
                GC.LookupDirector.AddIdentifiableTypeToGroup(def, grp);
                return true;
            }
            catch (Exception ex) { MelonLogger.Warning($"[CustomSlimeCreator] AddToGroup {groupName}: {ex.Message}"); return false; }
        }

        private static IdentifiableTypeGroup Group(string name)
        {
            if (_groups == null)
            {
                _groups = new Dictionary<string, IdentifiableTypeGroup>();
                try
                {
                    var all = Resources.FindObjectsOfTypeAll<IdentifiableTypeGroup>();
                    if (all != null)
                        for (int i = 0; i < all.Length; i++)
                        {
                            var g = all[i]; if (g == null) continue;
                            string n = null; try { n = g.name; } catch { }
                            if (!string.IsNullOrEmpty(n) && !_groups.ContainsKey(n)) _groups[n] = g;
                        }
                }
                catch { }
            }
            return _groups.TryGetValue(name, out var grp) ? grp : null;
        }

        // ---------------------------------------------------------------- array helpers

        private static bool ArrayContains(Il2CppReferenceArray<SlimeDefinition> arr, SlimeDefinition item)
        {
            if (arr == null) return false;
            for (int i = 0; i < arr.Length; i++) if (arr[i] == item) return true;
            return false;
        }

        private static Il2CppReferenceArray<SlimeDefinition> Append(Il2CppReferenceArray<SlimeDefinition> arr, SlimeDefinition item)
        {
            int n = arr != null ? arr.Length : 0;
            var res = new Il2CppReferenceArray<SlimeDefinition>(n + 1);
            for (int i = 0; i < n; i++) res[i] = arr[i];
            res[n] = item;
            return res;
        }

        private static bool ArrayContainsStr(Il2CppStringArray arr, string item)
        {
            if (arr == null) return false;
            for (int i = 0; i < arr.Length; i++) if (arr[i] == item) return true;
            return false;
        }

        private static Il2CppStringArray AppendStr(Il2CppStringArray arr, string item)
        {
            int n = arr != null ? arr.Length : 0;
            var res = new Il2CppStringArray(n + 1);
            for (int i = 0; i < n; i++) res[i] = arr[i];
            res[n] = item;
            return res;
        }

        // ---------------------------------------------------------------- plort / identifiable registration

        /// <summary>Registers a generic IdentifiableType (e.g. a custom plort) into the game's data model.</summary>
        public static bool RegisterIdentifiable(IdentifiableType ident, string[] groups)
        {
            var gc = GC;
            if (gc == null || ident == null) return false;
            string refId = null;
            try { refId = ident.ReferenceId; } catch { }
            if (string.IsNullOrEmpty(refId)) return false;

            var lookup = gc.LookupDirector;
            var asd = gc.AutoSaveDirector;

            // 1) Identifiable lookup by reference id
            Try(() => { if (!lookup._identifiableTypeByRefId.ContainsKey(refId)) lookup._identifiableTypeByRefId.Add(refId, ident); });

            // 2) The master identifiable-types group
            Try(() =>
            {
                var grp = asd._configuration._identifiableTypes;
                if (grp != null) lookup.AddIdentifiableTypeToGroup(ident, grp);
            });

            // 3) Save reference translation (persistence)
            Try(() =>
            {
                var t = asd._saveReferenceTranslation;
                if (!t._identifiableTypeLookup.ContainsKey(refId)) t._identifiableTypeLookup.Add(refId, ident);
                var pid = t._identifiableTypeToPersistenceId;
                if (pid._primaryIndex.Length > 0 && !ArrayContainsStr(pid._primaryIndex, refId))
                    pid._primaryIndex = AppendStr(pid._primaryIndex, refId);
                if (!pid._reverseIndex.ContainsKey(refId))
                    pid._reverseIndex.Add(refId, pid._reverseIndex.Count);
            });

            // 4) Gameplay groups
            _groups = null;
            if (groups != null)
            {
                var added = new List<string>();
                foreach (var g in groups)
                    if (AddIdentToGroup(ident, g)) added.Add(g);
                MelonDebug.Msg($"[CustomSlimeMaker] Registered ident '{refId}'. Groups: [{string.Join(", ", added)}]");
            }
            return true;
        }

        private static bool AddIdentToGroup(IdentifiableType ident, string groupName)
        {
            try
            {
                var grp = Group(groupName);
                if (grp == null) return false;
                GC.LookupDirector.AddIdentifiableTypeToGroup(ident, grp);
                return true;
            }
            catch { return false; }
        }

        /// <summary>Dump all IdentifiableTypeGroup names + all types containing "Plort" for discovery.</summary>
        public static void LogPlortInfo()
        {
            try
            {
                // Log all group names
                var allGroups = Resources.FindObjectsOfTypeAll<IdentifiableTypeGroup>();
                var groupNames = new List<string>();
                if (allGroups != null)
                    for (int i = 0; i < allGroups.Length; i++)
                        if (allGroups[i] != null && !string.IsNullOrEmpty(allGroups[i].name))
                            groupNames.Add(allGroups[i].name);
                groupNames.Sort();
                MelonLogger.Msg($"[CSC] All IdentifiableTypeGroup names ({groupNames.Count}):");
                foreach (var gn in groupNames)
                    if (gn.IndexOf("Plort", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        gn.IndexOf("produce", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        gn.IndexOf("market", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        gn.IndexOf("currency", StringComparison.OrdinalIgnoreCase) >= 0)
                        MelonLogger.Msg($"  ** {gn}");

                // Find plort IdentifiableTypes
                var allIdents = Resources.FindObjectsOfTypeAll<IdentifiableType>();
                MelonLogger.Msg($"[CSC] All IdentifiableTypes containing 'plort' or 'Plort':");
                int plortCount = 0;
                if (allIdents != null)
                    for (int i = 0; i < allIdents.Length; i++)
                    {
                        var it = allIdents[i];
                        if (it == null) continue;
                        string nm = null; try { nm = it.name; } catch { }
                        if (string.IsNullOrEmpty(nm)) continue;
                        if (nm.IndexOf("Plort", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            plortCount++;
                            if (plortCount <= 30)
                            {
                                string rid = null; try { rid = it.ReferenceId; } catch { }
                                MelonLogger.Msg($"  Plort type: name={nm}, refId={rid}, type={it.GetIl2CppType().FullName}");
                            }
                        }
                    }
                MelonLogger.Msg($"[CSC] Total plort types found: {plortCount}");

                // Find SlimeDefinitions that are largos
                var slimeDefs = Resources.FindObjectsOfTypeAll<SlimeDefinition>();
                int largoCount = 0;
                MelonLogger.Msg($"[CSC] Largo SlimeDefinitions:");
                if (slimeDefs != null)
                    for (int i = 0; i < slimeDefs.Length; i++)
                    {
                        var sd = slimeDefs[i];
                        if (sd == null) continue;
                        bool isLargo = false; try { isLargo = sd.IsLargo; } catch { }
                        if (!isLargo) continue;
                        largoCount++;
                        if (largoCount <= 20)
                        {
                            string nm = null; try { nm = sd.name; } catch { }
                            string rid = null; try { rid = sd.ReferenceId; } catch { }
                            string bases = "";
                            try { if (sd.BaseSlimes != null && sd.BaseSlimes.Length > 0) { var bn = new List<string>(); for (int j = 0; j < sd.BaseSlimes.Length; j++) bn.Add(sd.BaseSlimes[j] != null ? sd.BaseSlimes[j].name : "?"); bases = string.Join(",", bn); } } catch { }
                            MelonLogger.Msg($"  Largo: name={nm}, refId={rid}, bases=[{bases}]");
                        }
                    }
                MelonLogger.Msg($"[CSC] Total largos found: {largoCount}");

                // Check SlimeDefinitions for Largos array
                if (slimeDefs != null && slimeDefs.Length > 0)
                {
                    var first = slimeDefs[0];
                    var t = HarmonyLib.Traverse.Create(first);
                    MelonLogger.Msg($"[CSC] SlimeDefinition fields (from first def):");
                    foreach (var fn in t.Fields())
                    {
                        try { var f = t.Field(fn); var v = f.GetValue(); MelonLogger.Msg($"  {fn} = {v?.ToString() ?? "null"}"); } catch { MelonLogger.Msg($"  {fn} = <error>"); }
                    }
                }

                // Check SlimeDefinitions object for Largos field
                var gc = GC;
                if (gc != null)
                {
                    var defs = gc.SlimeDefinitions;
                    if (defs != null)
                    {
                        var dt = HarmonyLib.Traverse.Create(defs);
                        MelonLogger.Msg($"[CSC] SlimeDefinitions fields:");
                        foreach (var fn in dt.Fields())
                        {
                            try { var f = dt.Field(fn); var v = f.GetValue(); MelonLogger.Msg($"  {fn} = {v?.ToString() ?? "null"}"); } catch { MelonLogger.Msg($"  {fn} = <error>"); }
                        }
                    }
                }
            }
            catch (Exception ex) { MelonLogger.Warning($"[CSC] LogPlortInfo: {ex.Message}"); }
        }

        /// <summary>Dump all fields of a specific IdentifiableType (e.g. PinkPlort) to find market value.</summary>
        public static void LogPlortFields(string plortName = "PinkPlort")
        {
            try
            {
                var all = Resources.FindObjectsOfTypeAll<IdentifiableType>();
                IdentifiableType target = null;
                for (int i = 0; i < all.Length; i++)
                    if (all[i] != null && all[i].name == plortName) { target = all[i]; break; }
                if (target == null) { MelonLogger.Warning($"[CSC] Plort '{plortName}' not found."); return; }

                MelonLogger.Msg($"[CSC] === Fields of IdentifiableType '{target.name}' (refId={target.ReferenceId}) ===");
                var t = HarmonyLib.Traverse.Create(target);
                foreach (var fn in t.Fields())
                {
                    try
                    {
                        var f = t.Field(fn);
                        var v = f.GetValue();
                        MelonLogger.Msg($"  {fn} = {v?.ToString() ?? "null"} (type={v?.GetType()?.FullName ?? "?"})");
                    }
                    catch (Exception ex) { MelonLogger.Msg($"  {fn} = <error: {ex.Message}>"); }
                }

                // (Properties dump omitted due to Il2Cpp BindingFlags constraints)
            }
            catch (Exception ex) { MelonLogger.Warning($"[CSC] LogPlortFields: {ex.Message}"); }
        }

        private static void Try(Action a) { try { a(); } catch (Exception ex) { MelonLogger.Warning("[CustomSlimeCreator] register step: " + ex.Message); } }
    }
}
