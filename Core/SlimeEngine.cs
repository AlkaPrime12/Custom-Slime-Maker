using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using UnityEngine;
using Object = UnityEngine.Object;
using CustomSlimeCreator;

namespace CustomSlimeCreator.Core
{
    internal class CustomSlime
    {
        public string Key;
        public SlimeDefinition Def;
        public SlimeAppearance App;
        public GameObject Prefab;
        public SlimeConfig Config;
        public IdentifiableType Plort; // the custom plort IdentifiableType (null if none)
        public int BaseStructCount;   // structures [0..BaseStructCount) are the body; the rest are added parts
        public string PartSig = "";   // if the parts change, the slime is rebuilt from scratch
        public readonly List<GameObject> Instances = new List<GameObject>();
    }

    /// <summary>
    /// Standalone custom-slime engine. Clones a game slime's definition, appearance
    /// and materials, recolors + applies effects to the CLONES (never the originals), and spawns them.
    /// The appearance is forced onto every spawned instance so color/effect changes always show.
    /// </summary>
    public static class SlimeEngine
    {
        // --- slime shader property / keyword names (from the SR2 slime shader) ---
        private const string TopColor = "_TopColor", MiddleColor = "_MiddleColor", BottomColor = "_BottomColor", SpecColor = "_SpecColor";
        private const string TwinOn = "_ENABLETWINEFFECT_ON", TwinTop = "_TwinTopColor", TwinMid = "_TwinMiddleColor", TwinBot = "_TwinBottomColor", NoiseEdge = "_NoiseEdge";
        private const string SloomberOn = "_BODYCOLORING_SLOOMBER", SloomberTop = "_SloomberTopColor", SloomberMid = "_SloomberMiddleColor", SloomberBot = "_SloomberBottomColor", SloomberStarMask = "_SloomberStarMask", SloomberOverlay = "_SloomberColorOverlay";

        private static List<SlimeDefinition> _allDefs;
        private static SlimeAppearanceDirector _director;
        private static Material _twinMat, _sloomberMat;
        private static GameObject _holder;
        private static bool _logged;
        private static CustomSlime _pendingIconCapture;
        private static int _iconCaptureDelay;
        private static readonly Dictionary<string, CustomSlime> Built = new Dictionary<string, CustomSlime>();
        // --- 3D preview ---
        private static Camera _previewCam;
        private static RenderTexture _previewRT;
        private static GameObject _previewGO;
        private static CustomSlime _previewSlime; // which slime is currently shown in the preview
        private static SlimeConfig _previewConfig; // last config used for preview
        private static int _previewIconPending;   // > 0 means capture the preview as icon on next frame
        /// <summary>Last captured icon sprite.</summary>
        public static Sprite CurrentIcon { get; private set; }

        public static bool Ready { get; private set; }
        public static IEnumerable<string> BuiltKeys => Built.Keys;
        public static bool IsBuilt(string name) => Built.ContainsKey(name ?? "");
        /// <summary>Read-only access to the built slimes dictionary (for fusion lookups).</summary>
        internal static System.Collections.Generic.IReadOnlyDictionary<string, CustomSlime> BuiltDict => Built;
        /// <summary>The preview RenderTexture (null if not set up).</summary>
        public static RenderTexture PreviewRT => _previewRT;

        /// <summary>Called from Update each frame.</summary>
        public static void Tick()
        {
            // Only touch the preview camera while the editor is open — otherwise it renders every
            // frame in the background (wasteful, and previously washed out the whole game).
            if (!UI.EditorUI.IsVisible) return;

            if (_pendingIconCapture != null)
            {
                if (_iconCaptureDelay > 0) { _iconCaptureDelay--; return; }
                var cs = _pendingIconCapture;
                _pendingIconCapture = null;
                CaptureIcon(cs);
            }
            if (_previewCam != null && _previewRT != null && _previewGO != null)
                _previewCam.Render();
            if (_previewIconPending > 0)
            {
                _previewIconPending--;
                if (_previewIconPending == 0 && _previewSlime != null)
                    CaptureIconFromPreview(_previewSlime);
            }
        }

        // Stop the preview slime from wandering/falling — it's just for looking at.
        private static void FreezePreview(GameObject go)
        {
            try
            {
                foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true))
                { try { rb.isKinematic = true; rb.detectCollisions = false; } catch { } }
                foreach (var col in go.GetComponentsInChildren<Collider>(true))
                { try { col.enabled = false; } catch { } }
                // Freeze the idle/wander animation so the slime holds still in the preview.
                foreach (var an in go.GetComponentsInChildren<Animator>(true))
                { try { an.speed = 0f; } catch { } }
            }
            catch { }
        }

        // Frame the shot using the config's icon offsets/zoom (driven by the 4 preview arrows).
        private static void AimPreviewCamera(SlimeConfig cfg)
        {
            if (_previewCam == null || cfg == null) return;
            try
            {
                const float baseY = -4999.6f;
                float ox = cfg.IconOffX, oy = cfg.IconOffY;
                _previewCam.orthographicSize = Mathf.Clamp(cfg.IconZoom <= 0 ? 0.75f : cfg.IconZoom, 0.3f, 2f);
                _previewCam.transform.position = new Vector3(ox, baseY + oy, -5);
                _previewCam.transform.LookAt(new Vector3(ox, baseY + oy, 0));
            }
            catch { }
        }

        /// <summary>Re-aim the live preview camera (called by the editor's centering arrows / zoom).</summary>
        public static void NudgePreview(SlimeConfig cfg) => AimPreviewCamera(cfg);

        // ------------------------------------------------------------------ readiness

        // The main menu has a handful of preview slimes loaded; a real save has the full set.
        // Gate on the game's own SlimeDefinitions list being fully populated so we don't cache a
        // partial list (which caused only "Shadow, Yolky" to appear and broke group registration).
        private const int MinDefs = 40; // main menu has ~9 slimes; a real save has ~198

        public static bool EnsureReady()
        {
            if (Ready && _allDefs != null && _allDefs.Count >= MinDefs) return true;
            try
            {
                // Reliable discovery (found ~198 in-game). The main menu only has ~9 preview slimes,
                // so require a full set before caching — otherwise we'd lock onto a partial list.
                var defs = Resources.FindObjectsOfTypeAll<SlimeDefinition>();
                if (defs == null || defs.Length < MinDefs) return false;

                _allDefs = new List<SlimeDefinition>();
                for (int i = 0; i < defs.Length; i++)
                    if (defs[i] != null) _allDefs.Add(defs[i]);

                try
                {
                    var dirs = Resources.FindObjectsOfTypeAll<SlimeAppearanceDirector>();
                    if (dirs != null && dirs.Length > 0)
                    {
                        for (int i = 0; i < dirs.Length && _director == null; i++)
                            if (dirs[i] != null && dirs[i].name == "MainSlimeAppearanceDirector") _director = dirs[i];
                        if (_director == null) _director = dirs[0];
                    }
                }
                catch { }

                Ready = _allDefs.Count >= MinDefs;
                if (Ready && !_logged)
                {
                    _logged = true;
                    var presets = AvailablePresets();
                    MelonLogger.Msg($"[CustomSlimeCreator] Engine ready: {_allDefs.Count} slimes. Base presets ({presets.Count}): {string.Join(", ", presets.Take(40))}");
                }
                return Ready;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[CustomSlimeCreator] EnsureReady: {ex.Message}");
                return false;
            }
        }

        // More slimes stream in after the first "ready" moment, so refresh the cached list on demand
        // (called from the UI / on build) — otherwise the preset list stays stuck at a partial set.
        private static void RefreshDefs()
        {
            try
            {
                // THROTTLE: Resources.FindObjectsOfTypeAll is expensive and RefreshDefs is called from OnGUI hot
                // paths (per row, per frame). Only actually rescan every few seconds — this was the main menu lag.
                if (_allDefs != null && _allDefs.Count > 0 && Time.realtimeSinceStartup < _nextDefsRefresh) return;
                _nextDefsRefresh = Time.realtimeSinceStartup + 3f;
                var defs = Resources.FindObjectsOfTypeAll<SlimeDefinition>();
                if (defs == null) return;
                if (_allDefs == null || defs.Length > _allDefs.Count)
                {
                    var list = new List<SlimeDefinition>();
                    for (int i = 0; i < defs.Length; i++)
                        if (defs[i] != null) list.Add(defs[i]);
                    _allDefs = list;
                }
            }
            catch { }
        }
        private static float _nextDefsRefresh;

        /// <summary>Preset names actually present in the loaded game (base, non-largo slimes).</summary>
        public static List<string> AvailablePresets()
        {
            var list = new List<string>();
            if (!EnsureReady()) return list;
            RefreshDefs();
            foreach (var d in _allDefs)
            {
                if (d == null) continue;
                bool largo = false; try { largo = d.IsLargo; } catch { }
                if (largo) continue;
                var name = PresetNameOf(d);
                if (!string.IsNullOrEmpty(name) && !list.Contains(name)) list.Add(name);
            }
            list.Sort();
            return list;
        }

        /// <summary>Human-friendly preset key from a slime's ReferenceId ("SlimeDefinition.Pink" -> "Pink"), falling back to its object name.</summary>
        private static string PresetNameOf(SlimeDefinition d)
        {
            var rid = SafeRefId(d);
            if (!string.IsNullOrEmpty(rid))
            {
                var i = rid.LastIndexOf('.');
                var core = i >= 0 ? rid.Substring(i + 1) : rid;
                if (core.ToLower().EndsWith("slime")) core = core.Substring(0, core.Length - 5);
                if (!string.IsNullOrWhiteSpace(core)) return core;
            }
            var n = SafeName(d);
            if (!string.IsNullOrEmpty(n))
            {
                if (n.ToLower().EndsWith("slime")) n = n.Substring(0, n.Length - 5);
                return n;
            }
            return null;
        }

        /// <summary>Find a SlimeDefinition by preset name (e.g. "Pink", "Tabby").</summary>
        internal static SlimeDefinition FindDefByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            // Check built custom slimes first
            foreach (var kv in Built)
                if (kv.Key.Equals(name, StringComparison.OrdinalIgnoreCase)) return kv.Value.Def;
            if (!EnsureReady()) return null;
            RefreshDefs();
            foreach (var d in _allDefs)
            {
                if (d == null) continue;
                if (string.Equals(PresetNameOf(d), name, StringComparison.OrdinalIgnoreCase)) return d;
                var rid = SafeRefId(d);
                if (!string.IsNullOrEmpty(rid) && rid.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) return d;
            }
            return null;
        }

        // ------------------------------------------------------------------ build / update

        private static bool _autoLoaded;

        /// <summary>
        /// Builds + registers every saved custom slime once the game is ready. Registering their
        /// definitions before the save is pulled is what lets saved custom slimes load without the
        /// "KeyNotFound" crash. Runs once per session.
        /// </summary>
        public static void AutoLoadSavedOnce()
        {
            if (_autoLoaded) return;
            if (!EnsureReady()) return;
            if (GameAccess.GC == null) return;
            _autoLoaded = true;
            int n = 0;
            foreach (var cfg in ConfigStore.LoadAll())
            {
                try { if (BuildOrUpdate(cfg, out _)) n++; } catch { }
            }
            if (n > 0) MelonLogger.Msg($"[CustomSlimeCreator] Auto-registered {n} saved custom slime(s).");

            // Recreate previously-discovered fusions so their largo defs exist (same deterministic refId) BEFORE the
            // save is pulled — otherwise a saved fused largo in the world can't resolve and reverts on load.
            RebuildSavedFusions();
        }

        /// <summary>Rebuilds every discovered fusion from the registry so saved fused largos persist across sessions.</summary>
        private static void RebuildSavedFusions()
        {
            int n = 0;
            try
            {
                foreach (var f in FusionRegistry.All)
                {
                    var da = ResolveSide(f.AKey, f.ACustom);
                    var db = ResolveSide(f.BKey, f.BCustom);
                    if (da != null && db != null && TryGetOrCreateFusion(da, db) != null) n++;
                }
            }
            catch (Exception ex) { MelonLogger.Warning("[CustomSlimeCreator] RebuildSavedFusions: " + ex.Message); }
            if (n > 0) MelonLogger.Msg($"[CustomSlimeCreator] Rebuilt {n} saved fusion(s).");
        }

        /// <summary>Resolves one side of a fusion (custom slime by key, or vanilla slime by preset name).</summary>
        private static SlimeDefinition ResolveSide(string key, bool custom)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (custom) { var cs = FindBuilt(key); return cs != null ? cs.Def : null; }
            return FindBaseDef(key);
        }

        public static bool BuildOrUpdate(SlimeConfig cfg, out string error)
        {
            error = null;
            if (cfg == null) { error = "No config."; return false; }
            if (!EnsureReady()) { error = "Game not ready — enter a save first."; return false; }

            var key = cfg.Name;
            var sig = PartSig(cfg);
            // Parts/effects are baked into the cloned appearance; if they changed, rebuild from scratch —
            // but keep the already-spawned instances so we can re-point them at the new look (otherwise
            // the ones lying around the map stay as the old version, looking like a different species).
            List<GameObject> carried = null;
            if (Built.TryGetValue(key, out var existing) && existing.PartSig != sig)
            {
                carried = new List<GameObject>(existing.Instances);
                Built.Remove(key);
            }

            if (!Built.TryGetValue(key, out var cs))
            {
                var baseDef = FindBaseDef(cfg.BasePreset);
                if (baseDef == null) { error = "Base preset not found in game."; return false; }

                var baseApp = FirstAppearance(baseDef);
                if (baseApp == null) { error = "Base slime has no appearance."; return false; }
                if (baseDef.prefab == null) { error = "Base slime has no prefab."; return false; }

                SlimeAppearance app;
                GameObject prefab;
                SlimeDefinition def;
                try
                {
                    app = CloneAppearance(baseApp);
                    prefab = ClonePrefab(baseDef.prefab, key);
                    def = Object.Instantiate(baseDef);
                    def.hideFlags = HideFlags.DontUnloadUnusedAsset;
                    TrySet(() => def.name = "Custom" + key + "Slime");
                    // Give the clone its OWN identity (deterministic from the name). Without this the clone keeps
                    // the base's referenceId + cached hash, so on reload saved instances resolve to the BASE def —
                    // that's why custom slimes "kept the color but lost the name/icon/diet". Deterministic id = the
                    // rebuilt def matches saved actors across sessions.
                    GameAccess.ForceReferenceId(def, "SlimeDefinition.Custom" + key);
                    // Custom display name (shown in the vacpack / pedia instead of the base slime's name).
                    TrySet(() => { var ln = GameAccess.MakeName(cfg.DisplayName); if (ln != null) def.localizedName = ln; });
                    var arr = new Il2CppReferenceArray<SlimeAppearance>(1);
                    arr[0] = app;
                    TrySet(() => def.AppearancesDefault = arr);
                    TrySet(() => def.prefab = prefab);
                    TrySet(() => FixDiet(def));
                    WirePrefab(prefab, def, app);

                    // Register the definition into the game's data model so it saves, is vaccable and
                    // is resolvable by the save system (this is what prevents the save-time KeyNotFound crash).
                    GameAccess.RegisterSlime(def);
                }
                catch (Exception ex)
                {
                    error = "Clone failed: " + ex.Message;
                    MelonLogger.Error("[CustomSlimeCreator] " + ex);
                    return false;
                }

                // Diet: make the slime EAT the chosen food groups (add them to its diet — NOT the slime
                // into the food group, which would make the game think the slime itself is food).
                TrySet(() => SetupDiet(def, cfg));

                // Pre-populate the EatMap from the base slime as a safety net.
                // CalculateAllEats will clear and rebuild it; if it fails (empty result)
                // the postfix patch recovers by copying from a base slime's EatMap.
                TrySet(() => CopyEatMap(baseDef, def));

                cs = new CustomSlime { Key = key, Def = def, App = app, Prefab = prefab, Config = cfg };
                if (carried != null) cs.Instances.AddRange(carried); // keep live spawns across the rebuild
                cs.BaseStructCount = StructCount(app);
                ApplyBodyEffects(cs, cfg); // auto-adds Rad aura, crystal shards, etc. (before user parts)
                ApplyParts(cs, cfg);       // appends part structures (wings/ears/spikes/aura...) after the body
                cs.PartSig = sig;
                Built[key] = cs;
                MelonDebug.Msg($"[CustomSlimeMaker] Built custom slime '{key}' from '{SafeName(baseDef)}' with {cfg.Parts.Count} part(s).");

                // Plort: create + register a custom plort IdentifiableType, wire it into Diet.ProduceIdents
                TrySet(() => { cs.Plort = CreatePlort(cs); GeneratePlortIcon(cs); });

                // Apply config flags (CanLargofy, FavoriteFoods, etc.)
                TrySet(() => ApplyConfigFlags(def, cfg));

                // Largos: if CreateAllLargos is on, pair this slime with every base slime
                if (cfg.CreateAllLargos)
                    TrySet(() => BuildLargos(cs));
            }

            cs.Config = cfg;
            Recolor(cs);             // repaints only the body; parts keep their own colors
            TrySet(() => RecolorPlort(cs)); // recolor existing plort (if any)
            // Re-apply the plort's market value + custom icon every build (survives restarts, updates on edit).
            if (cs.Plort != null) TrySet(() => GameAccess.SetPlortValue(cs.Plort, cs.Config.PlortValue));
            if (cs.Plort != null) TrySet(() => GeneratePlortIcon(cs));
            RefreshInstances(cs);    // re-point every spawned instance at the current def/appearance
            // Always show the last saved icon right away (so it's there on game entry); only regenerate
            // when the look actually changed (and that capture only runs while the F2 preview is open).
            bool haveIcon = LoadIconPng(cs);
            // If there's no saved icon yet, auto-render one from the 3D model NOW so it exists (and persists) even
            // without opening the editor — needed for the vacpack icon and the Fusions tab photos.
            if (!haveIcon) { TrySet(() => AutoRenderSlimeIcon(cs)); haveIcon = CurrentIcon != null; }
            if (!haveIcon || !SavedSigMatches(cs)) { _pendingIconCapture = cs; _iconCaptureDelay = 5; }
            return true;
        }

        /// <summary>Renders a custom slime's 3D model to an icon and persists it (fallback when the user hasn't
        /// framed one in the editor). The editor's framed capture overrides this later.</summary>
        private static void AutoRenderSlimeIcon(CustomSlime cs)
        {
            if (cs == null || cs.Prefab == null) return;
            var tex = RenderPrefabToTexture(cs.Prefab, 128);
            if (tex == null) return;
            var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
            ApplyIcon(cs.Def, sprite);
            try { cs.App._icon = sprite; } catch { }
            CurrentIcon = sprite;
            SaveIcon(cs, tex);
            _iconSpriteCache.Remove("c:" + cs.Key); // let the Fusions tab pick up the new icon
        }

        // -------------------------------------------------------------- icon persistence

        private static string IconDir
        {
            get { var d = System.IO.Path.Combine(ConfigStore.Folder, "icons"); System.IO.Directory.CreateDirectory(d); return d; }
        }
        // NOTE: we persist icons as RAW pixels (.icraw), NOT PNG. UnityEngine.ImageConversion.EncodeToPNG/LoadImage
        // throw "Method not found: ReadOnlySpan.GetPinnableReference" in this Il2CppInterop build, so PNG never saved
        // or loaded (that's why icons didn't persist). GetPixels32/SetPixels32 work fine, so we use those.
        private static string IconPngPath(string name) => System.IO.Path.Combine(IconDir, ConfigStore.Sanitize(name) + ".ic2");
        private static string IconSigPath(string name) => System.IO.Path.Combine(IconDir, ConfigStore.Sanitize(name) + ".sig");

        /// <summary>Writes a texture as raw pixels: [int w][int h][w*h * RGBA bytes]. Avoids ImageConversion (broken).</summary>
        private static void SaveTexRaw(string path, Texture2D tex)
        {
            try
            {
                var px = tex.GetPixels32();
                int w = tex.width, h = tex.height, n = px.Length;
                var buf = new byte[8 + n * 4];
                System.BitConverter.GetBytes(w).CopyTo(buf, 0);
                System.BitConverter.GetBytes(h).CopyTo(buf, 4);
                for (int i = 0; i < n; i++) { var c = px[i]; int o = 8 + i * 4; buf[o] = c.r; buf[o + 1] = c.g; buf[o + 2] = c.b; buf[o + 3] = c.a; }
                System.IO.File.WriteAllBytes(path, buf);
            }
            catch (Exception ex) { MelonLogger.Warning("[CustomSlimeCreator] SaveTexRaw: " + ex.Message); }
        }

        /// <summary>Reads a texture written by SaveTexRaw. Returns null if missing/invalid.</summary>
        private static Texture2D LoadTexRaw(string path)
        {
            try
            {
                if (!System.IO.File.Exists(path)) return null;
                var buf = System.IO.File.ReadAllBytes(path);
                if (buf.Length < 8) return null;
                int w = System.BitConverter.ToInt32(buf, 0), h = System.BitConverter.ToInt32(buf, 4);
                int n = w * h;
                if (w <= 0 || h <= 0 || buf.Length < 8 + n * 4) return null;
                var px = new Il2CppStructArray<Color32>(n);
                for (int i = 0; i < n; i++) { int o = 8 + i * 4; px[i] = new Color32(buf[o], buf[o + 1], buf[o + 2], buf[o + 3]); }
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                tex.SetPixels32(px);
                tex.Apply();
                tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
                return tex;
            }
            catch (Exception ex) { MelonLogger.Warning("[CustomSlimeCreator] LoadTexRaw: " + ex.Message); return null; }
        }

        // Loads the last saved PNG (regardless of look signature) so the icon is there immediately on
        // game entry. Returns true if an icon was applied.
        /// <summary>
        /// Sets an icon so it shows EVERYWHERE (vacpack/pedia/map) and persists. The key part is clearing
        /// <c>_requiresFullArt</c> + calling <c>SetIconAndArt</c>: cloned defs inherit the base's "use full-art
        /// addressable" flag, so the game ignored our plain <c>icon</c> sprite and tried to load the base's art.
        /// </summary>
        internal static void ApplyIcon(IdentifiableType t, Sprite sprite)
        {
            if (t == null || sprite == null) return;
            try { t.SetIconAndArt(sprite, null); } catch { }
            try { t._requiresFullArt = false; } catch { }
            try { t.icon = sprite; } catch { }
        }

        private static bool LoadIconPng(CustomSlime cs)
        {
            try
            {
                var tex = LoadTexRaw(IconPngPath(cs.Key));
                if (tex == null) return false;
                var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
                ApplyIcon(cs.Def, sprite);
                try { cs.App._icon = sprite; } catch { }
                CurrentIcon = sprite;
                return true;
            }
            catch { return false; }
        }

        private static bool SavedSigMatches(CustomSlime cs)
        {
            try { var sp = IconSigPath(cs.Key); return System.IO.File.Exists(sp) && System.IO.File.ReadAllText(sp) == cs.Config.LookSig(); }
            catch { return false; }
        }

        private static void SaveIcon(CustomSlime cs, Texture2D tex)
        {
            try
            {
                SaveTexRaw(IconPngPath(cs.Key), tex);
                System.IO.File.WriteAllText(IconSigPath(cs.Key), cs.Config.LookSig());
            }
            catch (Exception ex) { MelonLogger.Warning("[CustomSlimeCreator] SaveIcon: " + ex.Message); }
        }

        public static bool Spawn(SlimeConfig cfg, out string error)
        {
            if (!BuildOrUpdate(cfg, out error)) return false;
            var cs = Built[cfg.Name];
            try
            {
                var cam = Camera.main;
                Vector3 pos = cam != null
                    ? cam.transform.position + cam.transform.forward * 3f + Vector3.up * 0.5f
                    : new Vector3(0, 5, 0);

                var sc = SceneContext.Instance;
                if (sc == null || sc.GameModel == null || sc.RegionRegistry == null)
                { error = "Scene not ready — load into the world first."; return false; }

                GameObject go;
                try
                {
                    var model = sc.GameModel.InstantiateActorModel(cs.Def, sc.RegionRegistry.CurrentSceneGroup, pos, Quaternion.identity, false);
                    go = InstantiationHelpers.InstantiateActorFromModel(model);
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[CustomSlimeCreator] Actor system spawn failed ({ex.Message}), falling back to direct Instantiate.");
                    go = Object.Instantiate(cs.Prefab, pos, Quaternion.identity);
                }

                if (go == null) { error = "Spawn produced null GameObject."; return false; }

                go.SetActive(true);
                ForceAppearance(go, cs);

                // Ensure def is set on all key components
                var eat = go.GetComponent<SlimeEat>();
                if (eat != null) TrySet(() => eat.SlimeDefinition = cs.Def);
                var id = go.GetComponent<IdentifiableActor>();
                if (id != null) TrySet(() => id.identType = cs.Def);

                cs.Instances.Add(go);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                MelonLogger.Error("[CustomSlimeCreator] Spawn: " + ex);
                return false;
            }
        }

        // ------------------------------------------------------------------ appearance building

        private static SlimeAppearance CloneAppearance(SlimeAppearance baseApp)
        {
            var app = Object.Instantiate(baseApp);
            app.hideFlags = HideFlags.DontUnloadUnusedAsset;
            app.name = "CSC_App_" + Guid.NewGuid().ToString("N"); // unique name to prevent save-time key collision

            var newStructs = new List<SlimeAppearanceStructure>();
            foreach (var bs in baseApp.Structures)
            {
                var ns = new SlimeAppearanceStructure(bs);
                if (bs.DefaultMaterials != null)
                {
                    var newMats = new List<Material>();
                    foreach (var m in bs.DefaultMaterials)
                        newMats.Add(m != null ? Object.Instantiate(m) : null);
                    ns.DefaultMaterials = newMats.ToArray();
                }
                newStructs.Add(ns);
            }
            app.Structures = newStructs.ToArray();
            return app;
        }

        private static GameObject ClonePrefab(GameObject basePrefab, string key)
        {
            // Instantiate under an inactive holder so the clone's Awake() doesn't run.
            var go = Object.Instantiate(basePrefab, Holder);
            go.name = "CustomSlimePrefab_" + key;
            go.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return go;
        }

        private static void WirePrefab(GameObject prefab, SlimeDefinition def, SlimeAppearance app)
        {
            TrySet(() => { var a = prefab.GetComponent<SlimeAppearanceApplicator>(); if (a != null) { a.SlimeDefinition = def; a.Appearance = app; } });
            TrySet(() => { var ia = prefab.GetComponent<IdentifiableActor>(); if (ia != null) ia.identType = def; });
            TrySet(() => { var se = prefab.GetComponent<SlimeEat>(); if (se != null) se.SlimeDefinition = def; });
        }

        // Object.Instantiate can leave the cloned diet's arrays null, which makes SlimeEat.CalculateAllEats
        // throw and freeze the slime. Ensure they are empty (not null) so the slime can eat + behave.
        private static void FixDiet(SlimeDefinition def)
        {
            var diet = def.Diet;
            if (diet == null) return;
            if (diet.MajorFoodIdentifiableTypeGroups == null) diet.MajorFoodIdentifiableTypeGroups = new Il2CppReferenceArray<IdentifiableTypeGroup>(0);
            if (diet.FavoriteIdents == null) diet.FavoriteIdents = new Il2CppReferenceArray<IdentifiableType>(0);
            if (diet.AdditionalFoodIdents == null) diet.AdditionalFoodIdents = new Il2CppReferenceArray<IdentifiableType>(0);
            if (diet.ProduceIdents == null) diet.ProduceIdents = new Il2CppReferenceArray<IdentifiableType>(0);
            try { if (diet.EatMap == null) diet.EatMap = new Il2CppSystem.Collections.Generic.List<SlimeDiet.EatMapEntry>(); } catch { }
        }

        // Make the slime eat the chosen food groups (kept on top of the inherited base diet).
        private static void SetupDiet(SlimeDefinition def, SlimeConfig cfg)
        {
            var diet = def.Diet;
            if (diet == null) { MelonLogger.Warning("[CSC] SetupDiet: diet is null!"); return; }
            var list = new List<IdentifiableTypeGroup>();
            // Inherit existing food groups from the cloned base diet
            if (diet.MajorFoodIdentifiableTypeGroups != null)
                foreach (var gg in diet.MajorFoodIdentifiableTypeGroups) if (gg != null) list.Add(gg);
            // Add food groups from the config (matched by IdentifiableTypeGroup.name)
            if (cfg.FoodGroups != null && cfg.FoodGroups.Count > 0)
            {
                var groups = Resources.FindObjectsOfTypeAll<IdentifiableTypeGroup>();
                foreach (var fg in cfg.FoodGroups)
                {
                    bool found = false;
                    for (int i = 0; i < groups.Length; i++)
                    {
                        var g = groups[i];
                        string n = null; try { n = g != null ? g.name : null; } catch { }
                        if (n == fg)
                        {
                            try { g._isFood = true; } catch { }
                            if (!list.Contains(g)) list.Add(g);
                            found = true;
                            break;
                        }
                    }
                    if (!found) MelonLogger.Warning($"[CSC] SetupDiet: food group '{fg}' not found in game!");
                }
            }
            // Convert to Il2CppReferenceArray explicitly (managed→Il2Cpp assignment can be unreliable)
            var il2cpp = new Il2CppReferenceArray<IdentifiableTypeGroup>(list.Count);
            for (int i = 0; i < list.Count; i++) il2cpp[i] = list[i];
            diet.MajorFoodIdentifiableTypeGroups = il2cpp;
            MelonDebug.Msg($"[CustomSlimeMaker] SetupDiet: {list.Count} food group(s) set for '{def.name}'.");
        }

        // CalculateAllEats produces an EMPTY EatMap for cloned Il2Cpp diets regardless of
        // valid food groups. Copy from the original base slime so the custom slime can eat.
        // IMPORTANT: entries are CLONED, never shared — the base slime's EatMap holds the same entry
        // objects, so sharing them and then rewriting ProducesIdent/BecomesIdent would corrupt the base
        // slime (e.g. real Pink slimes would start dropping our custom plort).
        private static void CopyEatMap(SlimeDefinition src, SlimeDefinition dst)
        {
            var srcMap = src?.Diet?.EatMap;
            var dstMap = dst?.Diet?.EatMap;
            if (srcMap == null || dstMap == null || srcMap.Count == 0) return;
            int n = 0;
            dstMap.Clear();
            foreach (var entry in srcMap)
                if (entry != null) { dstMap.Add(CloneEatEntry(entry)); n++; }
            MelonDebug.Msg($"[CSC] Copied {n} EatMap entries to '{dst.name}'.");
        }

        /// <summary>Clones the FOOD-producing entries (not largo-forming ones) of a slime's diet into a managed list,
        /// forcing them to produce <paramref name="plort"/>. Used to give a fusion largo both parents' cuisines.</summary>
        private static void CollectFoodEntries(List<SlimeDiet.EatMapEntry> dst, SlimeDefinition src, IdentifiableType plort)
        {
            var em = src != null && src.Diet != null ? src.Diet.EatMap : null;
            if (dst == null || em == null) return;
            for (int i = 0; i < em.Count; i++)
            {
                var e = em[i]; if (e == null) continue;
                IdentifiableType becomes = null; try { becomes = e.BecomesIdent; } catch { }
                if (becomes != null) continue; // skip largo-forming entries — we only want plain food→plort
                IdentifiableType eats = null; try { eats = e.EatsIdent; } catch { }
                if (eats == null) continue;
                var ne = CloneEatEntry(e);
                try { ne.ProducesIdent = plort; } catch { }
                try { ne.BecomesIdent = null; } catch { }
                dst.Add(ne);
            }
        }

        /// <summary>Deep-copies one EatMapEntry so we can safely edit it without touching the base slime's diet.</summary>
        private static SlimeDiet.EatMapEntry CloneEatEntry(SlimeDiet.EatMapEntry s)
        {
            var e = new SlimeDiet.EatMapEntry();
            try { e.EatsIdent = s.EatsIdent; } catch { }
            try { e.IsFavorite = s.IsFavorite; } catch { }
            try { e.FavoriteProductionCount = s.FavoriteProductionCount; } catch { }
            try { e.ProductionCount = s.ProductionCount; } catch { }
            try { e.ProducesIdent = s.ProducesIdent; } catch { }
            try { e.BecomesIdent = s.BecomesIdent; } catch { }
            try { e.Driver = s.Driver; } catch { }
            try { e.ExtraDrive = s.ExtraDrive; } catch { }
            try { e.MinDrive = s.MinDrive; } catch { }
            return e;
        }

        /// <summary>
        /// Rewrites the diet's EatMap so every entry that produced the base plort now produces <paramref name="plort"/>.
        /// Plort production is driven by <c>EatMapEntry.ProducesIdent</c>, NOT <c>Diet.ProduceIdents</c> — without this
        /// the slime keeps dropping the base plort (e.g. Pink Plort) whatever we set elsewhere. Safe because CopyEatMap
        /// already cloned the entries.
        /// </summary>
        private static int RewriteProduce(SlimeDefinition def, IdentifiableType plort)
        {
            if (def == null || plort == null) return 0;
            var em = def.Diet?.EatMap;
            if (em == null) return 0;
            int changed = 0;
            for (int i = 0; i < em.Count; i++)
            {
                var e = em[i]; if (e == null) continue;
                IdentifiableType prod = null; try { prod = e.ProducesIdent; } catch { }
                if (prod != null && prod != plort) { try { e.ProducesIdent = plort; changed++; } catch { } }
            }
            return changed;
        }

        // ------------------------------------------------------------------ plort creation

        /// <summary>Finds a vanilla plort IdentifiableType by name (e.g. "PinkPlort", "PlortPink").</summary>
        private static IdentifiableType FindPlort(string nameHint)
        {
            var all = Resources.FindObjectsOfTypeAll<IdentifiableType>();
            if (all == null) return null;
            IdentifiableType partial = null;
            for (int i = 0; i < all.Length; i++)
            {
                var it = all[i]; if (it == null) continue;
                string nm = null; try { nm = it.name; } catch { }
                if (string.IsNullOrEmpty(nm)) continue;
                if (nm.Equals(nameHint, StringComparison.OrdinalIgnoreCase)) return it;
                if (partial == null && nm.IndexOf(nameHint, StringComparison.OrdinalIgnoreCase) >= 0) partial = it;
            }
            return partial;
        }

        /// <summary>
        /// Creates a custom plort IdentifiableType: clones a vanilla plort, recolors, registers, and
        /// wires it into the slime's Diet.ProduceIdents. Returns the created plort or null on failure.
        /// </summary>
        internal static IdentifiableType CreatePlort(CustomSlime cs)
        {
            try
            {
                var cfg = cs.Config;
                if (!cfg.HasPlort) return null;

                // Find a vanilla plort to clone (try the base preset's own plort first, then PinkPlort)
                string plortName = cs.Config.BasePreset + "Plort";
                var basePlort = FindPlort(plortName) ?? FindPlort("PinkPlort") ?? FindPlort("Plort");
                if (basePlort == null)
                {
                    MelonLogger.Warning($"[CustomSlimeCreator] No vanilla plort found to clone (tried '{plortName}').");
                    return null;
                }

                // Clone the plort IdentifiableType
                var plort = Object.Instantiate(basePlort);
                plort.hideFlags = HideFlags.DontUnloadUnusedAsset;
                plort.name = "Custom" + cs.Key + "Plort";

                // Give the plort its OWN identity. Object.Instantiate copies the base plort's referenceId + cached
                // hash, which is exactly why a picked-up plort was still named/valued as the base ("Pink Plort").
                // ForceReferenceId writes the real 'referenceId' field and clears the hash so the game recomputes it.
                GameAccess.ForceReferenceId(plort, "IdentifiableType.Custom" + cs.Key + "Plort");

                TrySet(() =>
                {
                    var ln = GameAccess.MakeName(cfg.DisplayName + " Plort");
                    if (ln != null) plort.localizedName = ln;
                });

                // Clone the plort's PREFAB and re-point its identity to us. Instantiate shares the base plort's
                // prefab, whose child IdentifiableActor says "PinkPlort" — so a produced/picked-up plort registered
                // as the base. With our own prefab + identType the produced plort is 100% ours. Colors are applied
                // by RecolorPlort right after (on THIS cloned prefab — never the shared native one).
                try
                {
                    if (basePlort.prefab != null)
                    {
                        var pf = Object.Instantiate(basePlort.prefab, Holder);
                        pf.name = "CustomPlortPrefab_" + cs.Key;
                        pf.hideFlags = HideFlags.DontUnloadUnusedAsset;
                        foreach (var ia in pf.GetComponentsInChildren<IdentifiableActor>(true))
                            if (ia != null) { try { ia.identType = plort; } catch { } }
                        plort.prefab = pf;
                    }
                }
                catch (Exception ex) { MelonLogger.Warning("[CSC] plort prefab clone: " + ex.Message); }

                // Market value is set via the economy director (GameAccess.SetPlortValue), called from
                // BuildOrUpdate so it's re-applied every session — IdentifiableType has no value field of its own.

                // Register it
                GameAccess.RegisterIdentifiable(plort, new[] {
                    "PlortGroup", "VaccablePlortGroup", "IdentifiableTypesGroup", "PlortsGroup"
                });

                // Wire into the slime's diet so it PRODUCES this plort — ONLY ours (a base slime produces exactly
                // one plort). Replacing (not appending) the inherited base plort keeps plort→slime lookups unambiguous.
                var diet = cs.Def.Diet;
                if (diet != null)
                {
                    var arr = new Il2CppReferenceArray<IdentifiableType>(1);
                    arr[0] = plort;
                    diet.ProduceIdents = arr;
                }

                // Make the slime actually PRODUCE our plort — production is driven by EatMapEntry.ProducesIdent.
                int rewritten = RewriteProduce(cs.Def, plort);
                MelonDebug.Msg($"[CustomSlimeMaker] Created plort '{plort.name}' (from '{basePlort.name}', value={cfg.PlortValue}); {rewritten} EatMap produce(s) rewired.");
                return plort;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[CustomSlimeCreator] CreatePlort: {ex.Message}");
                return null;
            }
        }

        private static string VanillaIconPath(string preset) => System.IO.Path.Combine(IconDir, "vanilla_" + ConfigStore.Sanitize(preset) + ".ic2");

        /// <summary>Renders a vanilla slime's 3D model to a cached icon (once), so the Fusions tab can show it without
        /// touching the game's crash-prone atlas textures. No-op if already cached.</summary>
        private static void EnsureVanillaIcon(SlimeDefinition def, string preset)
        {
            try
            {
                if (def == null || string.IsNullOrEmpty(preset)) return;
                var path = VanillaIconPath(preset);
                if (System.IO.File.Exists(path)) return;
                GameObject prefab = null; try { prefab = def.prefab; } catch { }
                if (prefab == null) return;
                var tex = RenderPrefabToTexture(prefab, 128);
                if (tex != null) SaveTexRaw(path, tex);
            }
            catch { }
        }

        private static string PlortIconPngPath(string key) => System.IO.Path.Combine(IconDir, ConfigStore.Sanitize(key) + "_plort.ic2");
        private static string PlortIconSigPath(string key) => System.IO.Path.Combine(IconDir, ConfigStore.Sanitize(key) + "_plort.sig");
        private static string PlortLookSig(SlimeConfig c)
        { string C(Col x) => x.r + "," + x.g + "," + x.b; return "v2|" + c.BasePreset + "|" + C(c.PlortTop) + C(c.PlortMiddle) + C(c.PlortBottom); }

        /// <summary>
        /// Gives the plort a real icon by RENDERING its 3D model (the recolored plort prefab) to a texture — same
        /// technique as the slime icon, so it shows the actual custom-coloured plort, not a fake drawing. Cached to a
        /// PNG (keyed by plort colours) so it's only re-rendered when the colours change — cheap on load/edit.
        /// </summary>
        private static void GeneratePlortIcon(CustomSlime cs)
        {
            try
            {
                var plort = cs.Plort;
                if (plort == null) return;

                // Reuse the cached render (raw pixels) if the plort's look hasn't changed — avoids re-rendering
                // every build (that was a lag source) and avoids the broken ImageConversion.
                var raw = PlortIconPngPath(cs.Key); var sigP = PlortIconSigPath(cs.Key);
                bool sigOk = false;
                try { sigOk = System.IO.File.Exists(sigP) && System.IO.File.ReadAllText(sigP) == PlortLookSig(cs.Config); } catch { }
                if (sigOk)
                {
                    var t = LoadTexRaw(raw);
                    if (t != null)
                    {
                        var sp = Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100f);
                        sp.hideFlags = HideFlags.DontUnloadUnusedAsset;
                        ApplyIcon(plort, sp);
                        return;
                    }
                }

                // Render the plort's 3D model fresh.
                var tex = plort.prefab != null ? RenderPrefabToTexture(plort.prefab, 128) : null;
                if (tex == null) return;
                var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                sprite.name = cs.Config.DisplayName + "Plort_Icon";
                sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
                ApplyIcon(plort, sprite);
                SaveTexRaw(raw, tex);
                try { System.IO.File.WriteAllText(sigP, PlortLookSig(cs.Config)); } catch { }
            }
            catch (Exception ex) { MelonLogger.Warning($"[CustomSlimeCreator] GeneratePlortIcon: {ex.Message}"); }
        }

        /// <summary>Renders a prefab's 3D model to a transparent-background icon texture (self-contained temp cam).</summary>
        private static Texture2D RenderPrefabToTexture(GameObject prefab, int size)
        {
            if (prefab == null) return null;
            GameObject go = null, camObj = null, lightObj = null; RenderTexture rt = null;
            var prevActive = RenderTexture.active;
            try
            {
                var basePos = new Vector3(0, -6000, 0);
                go = Object.Instantiate(prefab, basePos, Quaternion.Euler(0, 180, 0));
                go.hideFlags = HideFlags.HideAndDontSave;
                go.SetActive(true);
                FreezePreview(go);

                // Frame the model from its renderer bounds.
                Bounds b = new Bounds(basePos, Vector3.one * 0.4f); bool has = false;
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null) continue;
                    if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds);
                }
                Vector3 center = has ? b.center : basePos;
                // Frame the whole model with a bit of margin (the render was cropping / off-center before). Clamp
                // guards against an oversized bound from a stray particle/effect renderer.
                float extent = has ? Mathf.Clamp(Mathf.Max(b.extents.x, b.extents.y), 0.05f, 3f) : 0.5f;

                camObj = new GameObject("CSC_IconCam");
                var cam = camObj.AddComponent<Camera>();
                cam.enabled = false;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                cam.orthographic = true;
                cam.orthographicSize = extent * 1.35f; // margin so nothing is cropped
                cam.nearClipPlane = 0.01f; cam.farClipPlane = 100f;
                // Look slightly down at the model (nicer 3/4 view than dead-on).
                cam.transform.position = center + new Vector3(0f, extent * 0.35f, -6f);
                cam.transform.LookAt(center);

                rt = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32); rt.Create();
                cam.targetTexture = rt;

                // Bright POINT lights (NOT directional — a directional light washes out the whole game scene).
                // Two close key/fill lights so the model reads clearly instead of coming out dark.
                lightObj = new GameObject("CSC_IconLight");
                var key = lightObj.AddComponent<Light>();
                key.type = LightType.Point; key.range = 15f; key.intensity = 6f; key.color = Color.white;
                key.transform.position = center + new Vector3(1.2f, 1.2f, -2.5f);
                var fillObj = new GameObject("CSC_IconFill");
                fillObj.transform.SetParent(lightObj.transform, false);
                var fill = fillObj.AddComponent<Light>();
                fill.type = LightType.Point; fill.range = 15f; fill.intensity = 3f; fill.color = Color.white;
                fill.transform.position = center + new Vector3(-1.5f, -0.5f, -2.5f);

                cam.Render();
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                tex.Apply();
                tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
                return tex;
            }
            catch (Exception ex) { MelonLogger.Warning("[CSC] RenderPrefabToTexture: " + ex.Message); return null; }
            finally
            {
                RenderTexture.active = prevActive;
                try { if (go != null) Object.Destroy(go); } catch { }
                try { if (camObj != null) Object.Destroy(camObj); } catch { }
                try { if (lightObj != null) Object.Destroy(lightObj); } catch { }
                try { if (rt != null) { rt.Release(); Object.Destroy(rt); } } catch { }
            }
        }

        /// <summary>
        /// Recolors an existing plort IdentifiableType's prefab materials without recreating it.
        /// Called on every BuildOrUpdate so plort colors stay in sync with the editor.
        /// </summary>
        internal static void RecolorPlort(CustomSlime cs)
        {
            try
            {
                var cfg = cs.Config;
                if (!cfg.HasPlort || cs.Plort == null) return;
                var prefab = cs.Plort.prefab;
                if (prefab == null) return;
                var renderers = prefab.GetComponentsInChildren<Renderer>(true);
                Color top = cfg.PlortTop.ToColor(), mid = cfg.PlortMiddle.ToColor(), bot = cfg.PlortBottom.ToColor();
                foreach (var r in renderers)
                {
                    if (r == null) continue;
                    // Recolor EVERY material slot (plorts can have several), on instanced copies.
                    var mats = r.sharedMaterials;
                    if (mats != null && mats.Length > 0)
                    {
                        var newMats = new Il2CppReferenceArray<Material>(mats.Length);
                        for (int i = 0; i < mats.Length; i++)
                        {
                            var src = mats[i];
                            if (src == null) { newMats[i] = null; continue; }
                            var m = Object.Instantiate(src);
                            PaintPlortMaterial(m, top, mid, bot);
                            newMats[i] = m;
                        }
                        r.sharedMaterials = newMats;
                    }
                    else if (r.sharedMaterial != null)
                    {
                        var m = Object.Instantiate(r.sharedMaterial);
                        PaintPlortMaterial(m, top, mid, bot);
                        r.sharedMaterial = m;
                    }
                }
            }
            catch (Exception ex) { MelonLogger.Warning($"[CustomSlimeCreator] RecolorPlort: {ex.Message}"); }
        }

        // Plorts don't use the slime shader's _TopColor/etc., so we recolor robustly: set common color props AND
        // enumerate the shader's ACTUAL colour properties and tint them (top/bottom by name hint, else middle).
        private static void PaintPlortMaterial(Material m, Color top, Color mid, Color bot)
        {
            if (m == null) return;
            SetColorSafe(m, TopColor, top);
            SetColorSafe(m, MiddleColor, mid);
            SetColorSafe(m, BottomColor, bot);
            SetColorSafe(m, SpecColor, mid);
            foreach (var p in new[] { "_Color", "_BaseColor", "_MainColor", "_TintColor", "_Tint", "_PlortColor", "_AlbedoColor", "_EmissionColor" })
                SetColorSafe(m, p, mid);
            try { m.color = mid; } catch { }
            // Enumerate the shader's real colour properties so we catch whatever this plort shader actually uses.
            try
            {
                var sh = m.shader;
                if (sh != null)
                {
                    int n = sh.GetPropertyCount();
                    for (int i = 0; i < n; i++)
                    {
                        try
                        {
                            if (sh.GetPropertyType(i) != UnityEngine.Rendering.ShaderPropertyType.Color) continue;
                            var name = sh.GetPropertyName(i);
                            if (string.IsNullOrEmpty(name)) continue;
                            var ln = name.ToLower();
                            Color c = ln.Contains("top") || ln.Contains("light") ? top
                                    : (ln.Contains("bot") || ln.Contains("dark") || ln.Contains("shadow")) ? bot : mid;
                            m.SetColor(name, c);
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        // ------------------------------------------------------------------ config flags

        /// <summary>Applies boolean config flags (CanLargofy, Vaccable, etc.) to the slime definition.</summary>
        private static void ApplyConfigFlags(SlimeDefinition def, SlimeConfig cfg)
        {
            TrySet(() => def.CanLargofy = cfg.CanLargofy);
            // Vaccable is handled by group registration — the slime is in VaccableBaseSlimeGroup.
            // SinkInShallowWater is handled by group registration.
            // SupportRadiant — set if the game supports it on this def.
            TrySet(() =>
            {
                // FavoriteFoods: resolve IdentifiableTypes by name from the game's resources
                if (cfg.FavoriteFoods != null && cfg.FavoriteFoods.Count > 0 && def.Diet != null)
                {
                    var allIdents = Resources.FindObjectsOfTypeAll<IdentifiableType>();
                    var favs = new List<IdentifiableType>();
                    foreach (var favName in cfg.FavoriteFoods)
                        for (int i = 0; i < allIdents.Length; i++)
                        {
                            var it = allIdents[i]; if (it == null) continue;
                            string nm = null; try { nm = it.name; } catch { }
                            if (string.Equals(nm, favName, StringComparison.OrdinalIgnoreCase)) { favs.Add(it); break; }
                        }
                    if (favs.Count > 0)
                        def.Diet.FavoriteIdents = favs.ToArray();
                }
            });
        }

        // ------------------------------------------------------------------ largo / fusion

        private static readonly Dictionary<string, SlimeDefinition> Fusions = new Dictionary<string, SlimeDefinition>();
        private static bool _creatingFusion;

        /// <summary>The custom slime whose definition is <paramref name="def"/>, or null if it isn't one of ours.</summary>
        private static CustomSlime CustomFor(SlimeDefinition def)
        {
            if (def == null) return null;
            foreach (var kv in Built) if (kv.Value != null && kv.Value.Def == def) return kv.Value;
            return null;
        }

        private static bool IsCustom(SlimeDefinition def) => CustomFor(def) != null;

        /// <summary>
        /// Entry point for the GetLargo* patches. If a fusion between <paramref name="a"/> and <paramref name="b"/>
        /// involves at least one CUSTOM slime and doesn't exist yet, builds + registers it on demand (and records it
        /// as discovered). Returns the fusion largo, or null to leave the game's default behaviour untouched.
        /// </summary>
        internal static SlimeDefinition TryGetOrCreateFusion(SlimeDefinition a, SlimeDefinition b)
        {
            try
            {
                if (a == null || b == null || a == b) return null;
                if (_creatingFusion) return null;
                bool la = false, lb = false; try { la = a.IsLargo; } catch { } try { lb = b.IsLargo; } catch { }
                if (la || lb) return null;
                if (!IsCustom(a) && !IsCustom(b)) return null; // only auto-create when a custom slime is involved
                // NOTE: we intentionally do NOT require CanLargofy — this lets custom fusions include the normally
                // non-largofying "flat" slimes (Puddle/water, Fire), which the user wants to be fusable.

                string key = FusionRegistry.PairKey(SideKey(a), SideKey(b));
                if (Fusions.TryGetValue(key, out var existing) && existing != null) return existing;

                _creatingFusion = true;
                try { return CreateFusionLargo(a, b, key); }
                finally { _creatingFusion = false; }
            }
            catch (Exception ex) { MelonLogger.Warning("[CustomSlimeCreator] TryGetOrCreateFusion: " + ex.Message); return null; }
        }

        /// <summary>Fusion resolved by the two plorts being combined (maps each plort back to its slime).</summary>
        internal static SlimeDefinition TryGetOrCreateFusionByPlorts(IdentifiableType p1, IdentifiableType p2)
        {
            var a = SlimeForPlort(p1); var b = SlimeForPlort(p2);
            if (a == null || b == null) return null;
            return TryGetOrCreateFusion(a, b);
        }

        /// <summary>The (non-largo) slime definition that produces <paramref name="plort"/>, custom or vanilla.</summary>
        private static SlimeDefinition SlimeForPlort(IdentifiableType plort)
        {
            if (plort == null) return null;
            foreach (var kv in Built) if (kv.Value != null && kv.Value.Plort == plort) return kv.Value.Def;
            RefreshDefs();
            if (_allDefs != null)
                foreach (var d in _allDefs)
                {
                    if (d == null) continue;
                    bool largo = false; try { largo = d.IsLargo; } catch { }
                    if (largo) continue;
                    try { var pr = d.Diet != null ? d.Diet.ProduceIdents : null; if (pr != null) for (int i = 0; i < pr.Length; i++) if (pr[i] == plort) return d; } catch { }
                }
            return null;
        }

        // A real vanilla largo to clone from, so our fusions inherit proper largo behaviour (NOT vaccable, bigger,
        // largo animations, 2-plort production, becomes-Tarr-on-3rd-plort). Cached.
        private static SlimeDefinition _largoTemplate;
        private static SlimeDefinition LargoTemplate()
        {
            if (_largoTemplate != null) return _largoTemplate;
            RefreshDefs();
            if (_allDefs != null)
                foreach (var d in _allDefs)
                {
                    if (d == null) continue;
                    bool lg = false; try { lg = d.IsLargo; } catch { }
                    if (lg) { _largoTemplate = d; break; }
                }
            return _largoTemplate;
        }

        // The plain Tarr slime (not GlitchTarr) — what a largo becomes if it eats a 3rd, non-component plort. Cached.
        private static SlimeDefinition _tarrDef;
        private static SlimeDefinition TarrDef()
        {
            if (_tarrDef != null) return _tarrDef;
            RefreshDefs();
            if (_allDefs != null)
                foreach (var d in _allDefs)
                {
                    if (d == null) continue;
                    string n = SafeName(d) ?? ""; string rid = SafeRefId(d) ?? "";
                    if (n == "Tarr" || rid.EndsWith(".Tarr")) { _tarrDef = d; break; }
                }
            return _tarrDef;
        }

        // Makes a largo turn into a normal Tarr when it eats any plort that isn't one of its two components: adds an
        // EatMap entry (eats=thatPlort → becomes=Tarr) for every other slime's plort.
        private static void AddTarrEntries(SlimeDefinition largo, IdentifiableType plortA, IdentifiableType plortB)
        {
            try
            {
                var tarr = TarrDef(); if (tarr == null) return;
                var em = largo != null && largo.Diet != null ? largo.Diet.EatMap : null; if (em == null) return;

                SlimeDiet.EatMapEntry tmpl = null;
                var have = new HashSet<IdentifiableType>();
                for (int i = 0; i < em.Count; i++)
                {
                    var e = em[i]; if (e == null) continue;
                    try { var ei = e.EatsIdent; if (ei != null) have.Add(ei); } catch { }
                    if (tmpl == null) { try { if (e.BecomesIdent != null) tmpl = e; } catch { } }
                }
                RefreshDefs();
                if (_allDefs == null) return;
                int added = 0;
                foreach (var d in _allDefs)
                {
                    if (d == null) continue;
                    bool lg = false; try { lg = d.IsLargo; } catch { }
                    if (lg) continue;
                    var p = PlortOf(d);
                    if (p == null || p == plortA || p == plortB || have.Contains(p)) continue;
                    var ne = tmpl != null ? CloneEatEntry(tmpl) : new SlimeDiet.EatMapEntry();
                    try { ne.EatsIdent = p; } catch { }
                    try { ne.BecomesIdent = tarr; } catch { }
                    try { ne.ProducesIdent = null; } catch { }
                    em.Add(ne); have.Add(p); added++;
                }
                if (added > 0) MelonDebug.Msg($"[CustomSlimeMaker] Added {added} Tarr entries to '{SafeName(largo)}'.");
            }
            catch (Exception ex) { MelonLogger.Warning("[CustomSlimeCreator] AddTarrEntries: " + ex.Message); }
        }

        // Builds + registers a fusion largo that uses attributes of BOTH parents (diet, both plorts, blended look).
        private static SlimeDefinition CreateFusionLargo(SlimeDefinition a, SlimeDefinition b, string key)
        {
            // Body-for-appearance = a recognizable base (prefer the VANILLA side).
            var bodyDef = !IsCustom(a) ? a : (!IsCustom(b) ? b : a);
            var otherDef = bodyDef == a ? b : a;

            // Clone from a BASE SLIME (not a vanilla largo). Cloning a vanilla largo (e.g. SaberTangle) leaked its
            // identity into the game's model dictionaries → "KeyNotFound: SaberTangle" when the largo later
            // transformed, and SaberTangle data overwriting saves on reload. We spawn + transform the largo
            // ourselves (respawn), so we don't need the vanilla largo machinery.
            var cloneBase = bodyDef;
            var largo = Object.Instantiate(cloneBase);
            largo.hideFlags = HideFlags.DontUnloadUnusedAsset;
            string idSafe = key.Replace("+", "_").Replace(" ", "");
            largo.name = "CustomFusion_" + idSafe;
            GameAccess.ForceReferenceId(largo, "SlimeDefinition.CustomFusion_" + idSafe);

            TrySet(() => largo.IsLargo = true);
            TrySet(() =>
            {
                var bases = new Il2CppReferenceArray<SlimeDefinition>(2);
                bases[0] = a; bases[1] = b;
                largo.BaseSlimes = bases;
            });

            // Diet: let the GAME build the largo diet from the two base slimes — this gives both cuisines, both
            // plorts AND the native "eat a 3rd plort → become Tarr" behaviour. Fall back to a manual merge if the
            // native call leaves the EatMap empty (can happen with custom bases).
            var plortA = PlortOf(a); var plortB = PlortOf(b);
            TrySet(() => largo.LoadDietFromBaseSlimes());
            TrySet(() => FixDiet(largo));
            TrySet(() =>
            {
                if (largo.Diet == null) return;
                int cnt = 0; try { cnt = largo.Diet.EatMap != null ? largo.Diet.EatMap.Count : 0; } catch { }
                if (cnt > 0) return; // native load worked

                var groups = new List<IdentifiableTypeGroup>();
                AddGroups(groups, a); AddGroups(groups, b);
                var garr = new Il2CppReferenceArray<IdentifiableTypeGroup>(groups.Count);
                for (int i = 0; i < groups.Count; i++) garr[i] = groups[i];
                largo.Diet.MajorFoodIdentifiableTypeGroups = garr;

                var produces = new List<IdentifiableType>();
                if (plortA != null) produces.Add(plortA);
                if (plortB != null && plortB != plortA) produces.Add(plortB);
                var parr = new Il2CppReferenceArray<IdentifiableType>(produces.Count);
                for (int i = 0; i < produces.Count; i++) parr[i] = produces[i];
                largo.Diet.ProduceIdents = parr;

                var merged = new List<SlimeDiet.EatMapEntry>();
                CollectFoodEntries(merged, a, plortA ?? plortB);
                CollectFoodEntries(merged, b, plortB ?? plortA);
                var em = largo.Diet.EatMap;
                if (em != null) { em.Clear(); foreach (var ent in merged) em.Add(ent); }
            });
            // Eating any OTHER (3rd) plort turns the largo into a normal Tarr.
            TrySet(() => AddTarrEntries(largo, plortA, plortB));

            // Appearance that mixes BOTH slimes (recognizable body + the other's parts, colors blended).
            TrySet(() =>
            {
                var app = BuildFusionAppearance(bodyDef, otherDef);
                if (app != null)
                {
                    var arr = new Il2CppReferenceArray<SlimeAppearance>(1);
                    arr[0] = app;
                    largo.AppearancesDefault = arr;
                }
            });

            // Combined name — strip the word "Slime" from each side first so we don't get "Slimelime..." mush.
            string aDisp = DisplayOf(a), bDisp = DisplayOf(b);
            string combined = NameGenerator.Combine(CleanForMerge(aDisp), CleanForMerge(bDisp));
            TrySet(() => { var ln = GameAccess.MakeName(combined); if (ln != null) largo.localizedName = ln; });

            // Register with FULL groups so the largo SAVES/RESTORES like a normal slime (missing groups was why
            // fused largos vanished on reload). Non-suckability is handled separately via Vacuumable.Size = LARGE.
            GameAccess.RegisterSlime(largo, true);
            GameAccess.RegisterLargo(largo, a, b, plortA, plortB);
            Fusions[key] = largo;

            // Pre-render icons for the two parents so the Fusions tab can show both (custom = its saved photo,
            // vanilla = a rendered photo of its 3D model).
            if (!IsCustom(a)) TrySet(() => EnsureVanillaIcon(a, SideKey(a)));
            if (!IsCustom(b)) TrySet(() => EnsureVanillaIcon(b, SideKey(b)));

            FusionRegistry.Add(new FusionEntry
            {
                Key = key,
                DisplayName = combined,
                AKey = SideKey(a), ADisplay = aDisp, ACustom = IsCustom(a),
                BKey = SideKey(b), BDisplay = bDisp, BCustom = IsCustom(b),
            });

            MelonDebug.Msg($"[CustomSlimeMaker] Fusion: {aDisp} × {bDisp} = '{combined}' ({largo.name}).");
            return largo;
        }

        // ------------------------------------------------------------------ fusion eat-wiring (from SlimeEat patches)

        /// <summary>
        /// Called from the SlimeEat eat-gate patches. If <paramref name="instance"/> is a slime that could fuse by
        /// eating plort <paramref name="id"/> (custom involved, can largofy, not already a largo), makes sure the
        /// fusion largo + a matching EatMap entry exist and that this instance's runtime eat set knows the plort.
        /// Returns true if this slime can now eat <paramref name="id"/> to fuse.
        /// </summary>
        internal static bool EnsureFusionEatable(SlimeEat instance, IdentifiableType id)
        {
            try
            {
                if (instance == null || id == null) return false;
                var eater = instance.SlimeDefinition;
                if (eater == null) return false;
                bool lg = false; try { lg = eater.IsLargo; } catch { }
                if (lg) return false;
                bool can = true; try { can = eater.CanLargofy; } catch { }
                if (!can) return false;

                var other = SlimeForPlort(id);
                if (other == null || other == eater) return false;
                if (!IsCustom(eater) && !IsCustom(other)) return false; // only fusions that involve a custom slime

                var largo = TryGetOrCreateFusion(eater, other);
                if (largo == null) return false;

                AddFusionEatEntry(eater, id, largo);

                // Make sure THIS spawned instance's runtime eat set actually contains the plort.
                bool known = false; try { var ae = instance._allEats; known = ae != null && ae.ContainsKey(id); } catch { }
                if (!known) { try { instance.CalculateAllEats(); } catch { } }
                return true;
            }
            catch { return false; }
        }

        /// <summary>The fusion EatMap entry for (this slime eats <paramref name="id"/>), or null.</summary>
        internal static SlimeDiet.EatMapEntry FusionEntryFor(SlimeEat instance, IdentifiableType id)
        {
            try
            {
                var em = instance != null && instance.SlimeDefinition != null ? instance.SlimeDefinition.Diet?.EatMap : null;
                if (em == null) return null;
                for (int i = 0; i < em.Count; i++)
                {
                    var e = em[i]; if (e == null) continue;
                    IdentifiableType ei = null, bi = null;
                    try { ei = e.EatsIdent; } catch { }
                    try { bi = e.BecomesIdent; } catch { }
                    if (ei == id && bi != null) return e;
                }
            }
            catch { }
            return null;
        }

        // Adds an EatMap entry "eats otherPlort -> becomes largo" to a slime's diet (deduped). Copies driver/counts
        // from an existing largo-forming entry so the eat behaves like a normal largo transformation.
        private static bool AddFusionEatEntry(SlimeDefinition eater, IdentifiableType otherPlort, SlimeDefinition largo)
        {
            var em = eater != null && eater.Diet != null ? eater.Diet.EatMap : null;
            if (em == null || otherPlort == null || largo == null) return false;
            for (int i = 0; i < em.Count; i++)
            {
                var e = em[i]; if (e == null) continue;
                IdentifiableType ei = null; try { ei = e.EatsIdent; } catch { }
                // OVERWRITE any inherited becomes (the cloned EatMap maps e.g. RockPlort->PinkRock; we want OUR
                // fusion instead, otherwise a custom slime eating a vanilla plort turns into the vanilla largo).
                if (ei == otherPlort) { try { e.BecomesIdent = largo; } catch { } return false; }
            }
            SlimeDiet.EatMapEntry tmpl = null;
            for (int i = 0; i < em.Count; i++)
            {
                var e = em[i]; if (e == null) continue;
                IdentifiableType b = null; try { b = e.BecomesIdent; } catch { }
                if (b != null) { tmpl = e; break; }
            }
            var ne = tmpl != null ? CloneEatEntry(tmpl) : new SlimeDiet.EatMapEntry();
            try { ne.EatsIdent = otherPlort; } catch { }
            try { ne.BecomesIdent = largo; } catch { }
            try { ne.ProducesIdent = null; } catch { }
            em.Add(ne);
            return true;
        }

        // ------------------------------------------------------------------ fusion transform (in-place)

        /// <summary>
        /// Called from the EatAndTransform prefix. If a custom slime (or a slime eating a custom plort) is eating a
        /// plort it can fuse with, transforms the SAME actor IN PLACE into the fusion largo (swap def + appearance +
        /// identity, no respawn — the native transform and a fresh actor spawn both fail on our home-made largos).
        /// The fusion is recomputed from the ACTUAL plort eaten, so eating a vanilla plort gives Custom×Vanilla
        /// (not the inherited PinkVanilla largo). Returns true if we handled it (caller should skip the native path).
        /// </summary>
        internal static bool TryInPlaceFusion(SlimeEat eater, GameObject food)
        {
            try
            {
                if (eater == null) return false;
                var eaterDef = eater.SlimeDefinition;
                if (eaterDef == null) return false;

                // Identify the eaten plort from the food GameObject.
                IdentifiableType foodId = null;
                if (food != null)
                {
                    try { var a = food.GetComponent<IdentifiableActor>(); if (a != null) foodId = a.identType; } catch { }
                    if (foodId == null) try { var a = food.GetComponentInChildren<IdentifiableActor>(true); if (a != null) foodId = a.identType; } catch { }
                }
                if (foodId == null) return false;

                bool eaterIsLargo = false; try { eaterIsLargo = eaterDef.IsLargo; } catch { }
                if (eaterIsLargo)
                {
                    // Only intercept OUR fusion largos; leave vanilla largos to the game.
                    string en = null; try { en = eaterDef.name; } catch { }
                    if (string.IsNullOrEmpty(en) || !en.StartsWith("CustomFusion")) return false;
                    // Eating one of its OWN two component plorts → nothing.
                    if (IsComponentPlort(eaterDef, foodId)) return false;
                    // Eating any THIRD plort → become a normal Tarr. We do it ourselves (the native Tarr transform
                    // crashes on our largos: "KeyNotFound" in GameModel.DestroyIdentifiableModel).
                    var tarr = TarrDef();
                    if (tarr == null) return false;
                    QueueFusion(eater, food, tarr);
                    return true;
                }

                // Base slime eating a plort → fuse (recompute from the actual plort so vanilla plorts give Custom×Vanilla).
                var other = SlimeForPlort(foodId);
                if (other == null || other == eaterDef) return false;
                if (!IsCustom(eaterDef) && !IsCustom(other)) return false; // only custom-involved fusions

                var largo = TryGetOrCreateFusion(eaterDef, other);
                if (largo == null) return false;

                QueueFusion(eater, food, largo); // do the actual transform NEXT frame (safe — not mid eat-callback)
                return true;
            }
            catch (Exception ex) { MelonLogger.Warning("[CustomSlimeCreator] TryInPlaceFusion: " + ex.Message); return false; }
        }

        /// <summary>Gives a spawned largo GameObject the vanilla largo vac behaviour: the vac tugs it toward the
        /// nozzle but can't suck it in. Driven by Vacuumable.Size = LARGE.</summary>
        internal static void MakeLargoUnvaccable(GameObject go)
        {
            if (go == null) return;
            TrySet(() =>
            {
                var v = go.GetComponent<Vacuumable>();
                if (v == null) v = go.GetComponentInChildren<Vacuumable>(true);
                if (v != null) v.Size = VacuumableSize.LARGE;
            });
        }

        /// <summary>True if a Vacuumable belongs to one of our fusion largos (used by the Vacuumable.OnEnable patch
        /// so reloaded largos also get the tug-but-don't-suck behaviour).</summary>
        internal static bool IsFusionLargoVacuumable(Vacuumable v)
        {
            try
            {
                if (v == null) return false;
                var go = v.gameObject;
                var ia = go != null ? go.GetComponent<IdentifiableActor>() : null;
                if (ia == null && go != null) ia = go.GetComponentInParent<IdentifiableActor>();
                var id = ia != null ? ia.identType : null;
                string n = id != null ? id.name : null;
                return n != null && n.StartsWith("CustomFusion");
            }
            catch { return false; }
        }

        /// <summary>True if <paramref name="plort"/> is one of the fusion largo's two component slimes' plorts.</summary>
        private static bool IsComponentPlort(SlimeDefinition largo, IdentifiableType plort)
        {
            try
            {
                var bases = largo.BaseSlimes;
                if (bases == null) return false;
                for (int i = 0; i < bases.Length; i++)
                {
                    var bd = bases[i]; if (bd == null) continue;
                    if (PlortOf(bd) == plort) return true;
                }
            }
            catch { }
            return false;
        }

        private struct FusionReq { public SlimeEat Eater; public GameObject Food; public SlimeDefinition Largo; }
        private static readonly List<FusionReq> _fusionQueue = new List<FusionReq>();
        private static void QueueFusion(SlimeEat eater, GameObject food, SlimeDefinition largo)
            => _fusionQueue.Add(new FusionReq { Eater = eater, Food = food, Largo = largo });

        /// <summary>Runs queued fusion transforms (called every frame from OnUpdate) — deferred so we're not inside
        /// the game's eat callback when we destroy/spawn actors (that was a crash source).</summary>
        public static void ProcessFusionQueue()
        {
            if (_fusionQueue.Count == 0) return;
            var reqs = new List<FusionReq>(_fusionQueue);
            _fusionQueue.Clear();
            foreach (var r in reqs)
            {
                try { DoFusionTransform(r.Eater, r.Food, r.Largo); }
                catch (Exception ex) { MelonLogger.Warning("[CustomSlimeCreator] fusion transform: " + ex.Message); }
            }
        }

        private static void DoFusionTransform(SlimeEat eater, GameObject food, SlimeDefinition largo)
        {
            GameObject slimeGo = null; try { slimeGo = eater != null ? eater.gameObject : null; } catch { }
            try { if (food != null) Object.Destroy(food); } catch { } // consume the eaten plort (native path skipped)
            if (slimeGo == null || largo == null) return;

            // Preferred: spawn a REAL largo actor (proper non-vaccable, big, animated largo) and remove the old slime.
            if (RespawnAsLargo(slimeGo, largo)) return;

            // Fallback: swap the def/appearance on the same GameObject if the spawn path is unavailable.
            var app = FirstAppearance(largo);
            TrySet(() => { var a = slimeGo.GetComponent<SlimeAppearanceApplicator>(); if (a != null) { a.SlimeDefinition = largo; if (app != null) a.Appearance = app; try { a.ApplyAppearance(); } catch { } } });
            TrySet(() => { var id = slimeGo.GetComponent<IdentifiableActor>(); if (id != null) id.identType = largo; });
            TrySet(() => { var se = slimeGo.GetComponent<SlimeEat>(); if (se != null) { se.SlimeDefinition = largo; try { se.CalculateAllEats(); } catch { } } });
            TrySet(() => slimeGo.transform.localScale = slimeGo.transform.localScale * 1.4f);
            MelonDebug.Msg($"[CustomSlimeMaker] In-place fusion → {SafeName(largo)}");
        }

        // Spawns the fusion largo as a proper actor at the old slime's spot, forces our mixed appearance, removes the
        // old slime. Returns false (caller falls back to in-place) if the scene/spawn isn't available.
        private static bool RespawnAsLargo(GameObject oldGo, SlimeDefinition largo)
        {
            try
            {
                var sc = SceneContext.Instance;
                if (sc == null || sc.GameModel == null || sc.RegionRegistry == null) return false;
                Vector3 pos; Quaternion rot;
                try { pos = oldGo.transform.position; rot = oldGo.transform.rotation; } catch { return false; }

                GameObject go;
                try
                {
                    var model = sc.GameModel.InstantiateActorModel(largo, sc.RegionRegistry.CurrentSceneGroup, pos, rot, false);
                    go = InstantiationHelpers.InstantiateActorFromModel(model);
                }
                catch (Exception ex) { MelonLogger.Warning("[CustomSlimeCreator] largo spawn failed (using in-place): " + ex.Message); return false; }
                if (go == null) return false;
                go.SetActive(true);

                var app = FirstAppearance(largo);
                TrySet(() => { var a = go.GetComponent<SlimeAppearanceApplicator>(); if (a != null) { a.SlimeDefinition = largo; if (app != null) a.Appearance = app; try { a.ApplyAppearance(); } catch { } } });
                TrySet(() => { var id = go.GetComponent<IdentifiableActor>(); if (id != null) id.identType = largo; });
                // Make it a proper largo: bigger, and rebuild its eat set so it can eat a 3rd plort (→ Tarr).
                bool isFusion = false; try { isFusion = (SafeName(largo) ?? "").StartsWith("CustomFusion"); } catch { }
                TrySet(() => { var se = go.GetComponent<SlimeEat>(); if (se != null) { se.SlimeDefinition = largo; try { se.CalculateAllEats(); } catch { } } });
                if (isFusion)
                {
                    TrySet(() => go.transform.localScale = go.transform.localScale * 1.6f); // largos are ~1.6× a base slime
                    MakeLargoUnvaccable(go); // vac tugs but can't suck it in (like a vanilla largo)
                }
                try { Object.Destroy(oldGo); } catch { }
                MelonDebug.Msg($"[CustomSlimeMaker] Fusion → {SafeName(largo)}");
                return true;
            }
            catch (Exception ex) { MelonLogger.Warning("[CustomSlimeCreator] RespawnAsLargo: " + ex.Message); return false; }
        }

        // Recognizable body (bodyDef) + the other slime's extra parts + colors blended 50/50 from both.
        private static SlimeAppearance BuildFusionAppearance(SlimeDefinition bodyDef, SlimeDefinition otherDef)
        {
            var bodyApp = FirstAppearance(bodyDef);
            if (bodyApp == null) return null;
            var mixed = CloneAppearance(bodyApp);

            GetColors(bodyDef, out var topA, out var midA, out var botA);
            GetColors(otherDef, out var topB, out var midB, out var botB);
            Color top = Color.Lerp(topA, topB, 0.5f), mid = Color.Lerp(midA, midB, 0.5f), bot = Color.Lerp(botA, botB, 0.5f);
            var structs = mixed.Structures;
            if (structs != null)
                for (int i = 0; i < structs.Length; i++)
                {
                    var s = structs[i]; if (s == null || s.DefaultMaterials == null) continue;
                    var t = SlimeAppearanceElement.ElementType.BODY;
                    try { if (s.Element != null) t = s.Element.Type; } catch { }
                    if (t != SlimeAppearanceElement.ElementType.BODY) continue; // only blend the body; parts keep their look
                    foreach (var m in s.DefaultMaterials)
                    { SetColorSafe(m, TopColor, top); SetColorSafe(m, MiddleColor, mid); SetColorSafe(m, BottomColor, bot); }
                }
            TrySet(() => { mixed._colorPalette = new SlimeAppearance.Palette { Top = top, Middle = mid, Bottom = bot, Ammo = mid }; });

            // Append the OTHER slime's distinctive extra parts so the largo visibly shows both.
            try { AddAllExtrasFromApp(mixed, FirstAppearance(otherDef), false, top, mid, bot); } catch { }
            return mixed;
        }

        private static void GetColors(SlimeDefinition def, out Color top, out Color mid, out Color bot)
        {
            var cs = CustomFor(def);
            if (cs != null) { top = cs.Config.Top.ToColor(); mid = cs.Config.Middle.ToColor(); bot = cs.Config.Bottom.ToColor(); return; }
            top = mid = bot = Color.gray;
            try
            {
                var app = FirstAppearance(def);
                // Sample the actual body material — vanilla slimes often leave _colorPalette blank.
                Material m = null;
                if (app != null && app.Structures != null && app.Structures.Length > 0)
                {
                    var mats = app.Structures[0].DefaultMaterials;
                    if (mats != null && mats.Length > 0) m = mats[0];
                }
                if (m != null)
                {
                    if (m.HasProperty(TopColor)) top = m.GetColor(TopColor);
                    if (m.HasProperty(MiddleColor)) mid = m.GetColor(MiddleColor);
                    if (m.HasProperty(BottomColor)) bot = m.GetColor(BottomColor);
                }
            }
            catch { }
        }

        private static void AddGroups(List<IdentifiableTypeGroup> list, SlimeDefinition def)
        {
            try { var d = def.Diet; if (d != null && d.MajorFoodIdentifiableTypeGroups != null) foreach (var g in d.MajorFoodIdentifiableTypeGroups) if (g != null && !list.Contains(g)) list.Add(g); } catch { }
        }

        private static IdentifiableType PlortOf(SlimeDefinition def)
        {
            var cs = CustomFor(def); if (cs != null && cs.Plort != null) return cs.Plort;
            try { var p = def.Diet != null ? def.Diet.ProduceIdents : null; if (p != null && p.Length > 0) return p[0]; } catch { }
            return null;
        }

        private static string SideKey(SlimeDefinition def)
        {
            var cs = CustomFor(def); if (cs != null) return cs.Key;
            return PresetNameOf(def) ?? SafeName(def) ?? "?";
        }

        private static string DisplayOf(SlimeDefinition def)
        {
            var cs = CustomFor(def); if (cs != null) return cs.Config != null ? (cs.Config.DisplayName ?? cs.Key) : cs.Key;
            try { var s = def.localizedName != null ? def.localizedName.GetLocalizedString() : null; if (!string.IsNullOrEmpty(s)) return s; } catch { }
            return PresetNameOf(def) ?? SafeName(def) ?? "Slime";
        }

        // Removes the word "Slime" (any language spacing) so name merging produces readable fused names.
        private static string CleanForMerge(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            s = s.Replace("Slime", "").Replace("slime", "").Replace("  ", " ").Trim();
            return string.IsNullOrEmpty(s) ? "Slime" : s;
        }

        /// <summary>Pre-builds fusions with every eligible slime (only used when the "All largos" option is on).</summary>
        private static void BuildLargos(CustomSlime cs)
        {
            if (!cs.Config.CanLargofy) return;
            RefreshDefs();
            int built = 0;
            foreach (var baseDef in _allDefs)
            {
                if (baseDef == null || baseDef == cs.Def) continue;
                bool isLargo = false; try { isLargo = baseDef.IsLargo; } catch { }
                if (isLargo) continue;
                bool can = true; try { can = baseDef.CanLargofy; } catch { }
                if (!can) continue;
                if (TryGetOrCreateFusion(cs.Def, baseDef) != null) built++;
            }
            if (built > 0) MelonDebug.Msg($"[CustomSlimeMaker] Pre-built {built} fusion(s) for '{cs.Key}'.");
        }

        private static void ForceAppearance(GameObject go, CustomSlime cs)
        {
            TrySet(() =>
            {
                var a = go.GetComponent<SlimeAppearanceApplicator>();
                if (a != null)
                {
                    a.SlimeDefinition = cs.Def;
                    a.Appearance = cs.App;
                    try { a.ApplyAppearance(); } catch { }
                }
            });
        }

        private static void RefreshInstances(CustomSlime cs)
        {
            cs.Instances.RemoveAll(g => g == null);
            foreach (var g in cs.Instances) ForceAppearance(g, cs);
        }

        // ------------------------------------------------------------------ coloring

        // Repaints only the body structures (the first BaseStructCount). Added parts keep their own colors.
        private static void Recolor(CustomSlime cs)
        {
            var app = cs.App; var cfg = cs.Config;
            Color top = cfg.Top.ToColor(), mid = cfg.Middle.ToColor(), bot = cfg.Bottom.ToColor(), vac = cfg.Vac.ToColor();
            try
            {
                var structs = app.Structures;
                int count = Math.Min(cs.BaseStructCount, structs != null ? structs.Length : 0);
                for (int i = 0; i < count; i++)
                {
                    var s = structs[i];
                    if (s == null || s.DefaultMaterials == null) continue;
                    foreach (var m in s.DefaultMaterials)
                        PaintMaterial(m, cfg, top, mid, bot);
                }
            }
            catch (Exception ex) { MelonLogger.Warning("[CustomSlimeCreator] recolor: " + ex.Message); }

            TrySet(() =>
            {
                app._colorPalette = new SlimeAppearance.Palette { Top = top, Middle = mid, Bottom = bot, Ammo = vac };
                app._splatColor = vac;
            });
        }

        // ------------------------------------------------------------------ parts

        /// <summary>Body-part element types that can be transplanted from other slimes.</summary>
        public static readonly string[] PartTypes =
        {
            "WINGS", "EARS", "TAIL", "FOREHEAD", "TOP", "SIDE", "ANTENNAE",
            "WHISKERS", "GLASSES", "AURA", "HYPERAURA", "SLOOMBERAURA", "ORBITAL", "CORE", "SURFACE",
        };

        private static int StructCount(SlimeAppearance app)
        {
            try { return app.Structures != null ? app.Structures.Length : 0; } catch { return 0; }
        }

        // Signature of the STRUCTURE (base + parts + body effects). Colors/twin/sloomber are handled by
        // Recolor without a rebuild, so they are NOT included here.
        private static string PartSig(SlimeConfig cfg)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(cfg.BasePreset);
            sb.Append('|').Append(cfg.RadAuraEffect).Append(cfg.CrystalShardsEffect).Append(cfg.RockPlatingEffect)
              .Append(cfg.AnglerLureEffect).Append(cfg.HunterPatternEffect).Append(cfg.RingtailPatternEffect);
            if (cfg.Parts != null) foreach (var p in cfg.Parts) sb.Append('|').Append(p.Sig());
            return sb.ToString();
        }

        private static void ApplyParts(CustomSlime cs, SlimeConfig cfg)
        {
            if (cfg.Parts == null) return;
            foreach (var part in cfg.Parts)
            {
                try { AddPart(cs.App, part); }
                catch (Exception ex) { MelonLogger.Warning($"[CustomSlimeCreator] part {part.Type} from {part.Donor}: {ex.Message}"); }
            }
        }

        // Each "Body Effect" toggle copies ALL of a donor slime's distinctive extra parts (whatever
        // element types they actually are) — this is what "mix in the look of slime X" means, and it's
        // robust to the exact element naming (Crystal's spikes, Rock's plates, an aura, etc.).
        private static readonly (string field, string donor)[] BodyFx =
        {
            ("Rad",     "Hyper"),   // glowing aura
            ("Crystal", "Crystal"), // shards
            ("Rock",    "Rock"),    // plating
            ("Angler",  "Angler"),  // lure
            ("Hunter",  "Hunter"),  // pattern parts
            ("Ringtail","Ringtail") // swirl/tail
        };

        private static void ApplyBodyEffects(CustomSlime cs, SlimeConfig cfg)
        {
            var top = cfg.Top.ToColor(); var mid = cfg.Middle.ToColor(); var bot = cfg.Bottom.ToColor();
            bool[] on = { cfg.RadAuraEffect, cfg.CrystalShardsEffect, cfg.RockPlatingEffect,
                          cfg.AnglerLureEffect, cfg.HunterPatternEffect, cfg.RingtailPatternEffect };
            for (int i = 0; i < BodyFx.Length && i < on.Length; i++)
                if (on[i]) { try { AddAllExtras(cs.App, BodyFx[i].donor, true, top, mid, bot); } catch (Exception ex) { MelonLogger.Warning($"[CSC] body fx {BodyFx[i].donor}: {ex.Message}"); } }
        }

        // Copy every non-body/face structure of a donor slime into this appearance.
        private static void AddAllExtras(SlimeAppearance app, string donorPreset, bool recolor, Color top, Color mid, Color bot)
        {
            AddAllExtrasFromApp(app, FirstAppearance(FindBaseDef(donorPreset)), recolor, top, mid, bot);
        }

        // Same, but from an already-resolved donor appearance (used by fusion so it works for custom donors too).
        private static void AddAllExtrasFromApp(SlimeAppearance app, SlimeAppearance donorApp, bool recolor, Color top, Color mid, Color bot)
        {
            if (donorApp == null || donorApp.Structures == null) return;
            int added = 0;
            foreach (var s in donorApp.Structures)
            {
                if (s == null || s.Element == null) continue;
                var t = s.Element.Type;
                if (t == SlimeAppearanceElement.ElementType.BODY || t == SlimeAppearanceElement.ElementType.FACE ||
                    t == SlimeAppearanceElement.ElementType.FACE_ATTACH || t == SlimeAppearanceElement.ElementType.NONE) continue;
                AppendStructure(app, CloneStructure(s, recolor, top, mid, bot, CountType(app, t)));
                added++;
            }
            MelonDebug.Msg($"[CustomSlimeCreator] Added {added} extra part(s) from donor appearance.");
        }

        private static void AddPart(SlimeAppearance app, PartConfig part)
        {
            if (!Enum.TryParse(part.Type, true, out SlimeAppearanceElement.ElementType type)) return;
            var donorApp = FirstAppearance(FindBaseDef(part.Donor));
            if (donorApp == null) return;

            SlimeAppearanceStructure donor = null;
            foreach (var s in donorApp.Structures)
                if (s != null && s.Element != null && s.Element.Type == type) { donor = s; break; }
            if (donor == null) { MelonLogger.Warning($"[CustomSlimeCreator] '{part.Donor}' has no {part.Type} part."); return; }

            AppendStructure(app, CloneStructure(donor, part.Recolor, part.Top.ToColor(), part.Middle.ToColor(), part.Bottom.ToColor(), CountType(app, type)));
            MelonDebug.Msg($"[CustomSlimeCreator] Added {part.Type} from '{part.Donor}'.");
        }

        private static int CountType(SlimeAppearance app, SlimeAppearanceElement.ElementType type)
        {
            int n = 0;
            try { foreach (var s in app.Structures) if (s != null && s.Element != null && s.Element.Type == type) n++; } catch { }
            return n;
        }

        private static SlimeAppearanceStructure CloneStructure(SlimeAppearanceStructure donor, bool recolor, Color top, Color mid, Color bot, int dupIndex)
        {
            var ns = new SlimeAppearanceStructure(donor);
            if (donor.DefaultMaterials != null)
            {
                var mats = new List<Material>();
                foreach (var m in donor.DefaultMaterials)
                {
                    var nm = m != null ? Object.Instantiate(m) : null;
                    if (nm != null && recolor) PaintColors(nm, top, mid, bot);
                    mats.Add(nm);
                }
                ns.DefaultMaterials = mats.ToArray();
            }
            if (dupIndex > 0) TryOffsetStructure(ns, dupIndex); // 2nd/3rd of the same part -> nudge so they don't overlap
            return ns;
        }

        // Clone the element's prefabs and nudge them up/down so multiple wings/parts fan out.
        private static void TryOffsetStructure(SlimeAppearanceStructure ns, int dupIndex)
        {
            try
            {
                if (ns.Element == null || ns.Element.Prefabs == null) return;
                var el = Object.Instantiate(ns.Element);
                float dy = (dupIndex % 2 == 1 ? 1f : -1f) * 0.15f * ((dupIndex + 1) / 2);
                var newPrefabs = new List<SlimeAppearanceObject>();
                foreach (var pf in el.Prefabs)
                {
                    if (pf == null) { newPrefabs.Add(null); continue; }
                    var clone = Object.Instantiate(pf.gameObject);
                    clone.SetActive(false);
                    Object.DontDestroyOnLoad(clone);
                    clone.transform.localPosition = clone.transform.localPosition + new Vector3(0, dy, 0);
                    newPrefabs.Add(clone.GetComponent<SlimeAppearanceObject>());
                }
                el.Prefabs = newPrefabs.ToArray();
                ns.Element = el;
            }
            catch { }
        }

        private static void AppendStructure(SlimeAppearance app, SlimeAppearanceStructure ns)
        {
            var list = new List<SlimeAppearanceStructure>();
            foreach (var s in app.Structures) list.Add(s);
            list.Add(ns);
            app.Structures = list.ToArray();
        }

        private static readonly Dictionary<string, List<string>> _partsCache = new Dictionary<string, List<string>>();
        /// <summary>Element-type names a donor slime actually has (for the Parts dropdown filtering). Cached — the
        /// Parts tab calls this every OnGUI frame, and the underlying scan is expensive.</summary>
        public static List<string> AvailablePartsFor(string donorPreset)
        {
            if (donorPreset != null && _partsCache.TryGetValue(donorPreset, out var hit)) return hit;
            var result = new List<string>();
            try
            {
                var donorApp = FirstAppearance(FindBaseDef(donorPreset));
                if (donorApp == null) { if (donorPreset != null) _partsCache[donorPreset] = result; return result; }
                foreach (var s in donorApp.Structures)
                {
                    if (s == null || s.Element == null) continue;
                    var name = s.Element.Type.ToString();
                    if (name == "BODY" || name == "FACE" || name == "FACE_ATTACH" || name == "NONE") continue;
                    if (!result.Contains(name)) result.Add(name);
                }
            }
            catch { }
            if (donorPreset != null) _partsCache[donorPreset] = result;
            return result;
        }

        private static void PaintColors(Material m, Color top, Color mid, Color bot)
        {
            SetColorSafe(m, TopColor, top);
            SetColorSafe(m, MiddleColor, mid);
            SetColorSafe(m, BottomColor, bot);
            SetColorSafe(m, SpecColor, mid);
        }

        // ------------------------------------------------------------------ coloring (body)

        private static void PaintMaterial(Material m, SlimeConfig cfg, Color top, Color mid, Color bot)
        {
            if (m == null) return;
            SetColorSafe(m, TopColor, top);
            SetColorSafe(m, MiddleColor, mid);
            SetColorSafe(m, BottomColor, bot);
            SetColorSafe(m, SpecColor, mid);

            // Twin swirl
            if (cfg.TwinEffect)
            {
                EnableKeywordSafe(m, TwinOn);
                SetColorSafe(m, TwinTop, top);
                SetColorSafe(m, TwinMid, mid);
                SetColorSafe(m, TwinBot, bot);
                CopyTexture(m, TwinMat(), NoiseEdge);
            }
            else DisableKeywordSafe(m, TwinOn);

            // Sloomber stars
            if (cfg.SloomberEffect)
            {
                EnableKeywordSafe(m, SloomberOn);
                SetColorSafe(m, SloomberTop, top);
                SetColorSafe(m, SloomberMid, mid);
                SetColorSafe(m, SloomberBot, bot);
                CopyTexture(m, SloomberMat(), SloomberStarMask);
                CopyTexture(m, SloomberMat(), SloomberOverlay);
            }
            else DisableKeywordSafe(m, SloomberOn);
        }

        // ------------------------------------------------------------------ lookup helpers

        private static SlimeDefinition FindBaseDef(string preset)
        {
            RefreshDefs();
            if (string.IsNullOrWhiteSpace(preset)) preset = "Pink";
            var p = preset.ToLower();
            SlimeDefinition exact = null, contains = null, pink = null, any = null;
            foreach (var d in _allDefs)
            {
                if (d == null) continue;
                bool largo = false; try { largo = d.IsLargo; } catch { }
                if (largo) continue;
                if (any == null) any = d;

                var core = (PresetNameOf(d) ?? "").ToLower();
                var rid = (SafeRefId(d) ?? "").ToLower();
                var nm = (SafeName(d) ?? "").ToLower();

                if (core == "pink" || rid.EndsWith(".pink") || nm == "pinkslime" || nm == "pink") pink = pink ?? d;
                if (core == p) exact = exact ?? d;
                else if (contains == null && (core.Contains(p) || rid.Contains(p) || nm.Contains(p))) contains = d;
            }
            return exact ?? contains ?? pink ?? any;
        }

        private static Material TwinMat() { if (_twinMat == null) _twinMat = BaseBodyMat("Twin"); return _twinMat; }
        private static Material SloomberMat() { if (_sloomberMat == null) _sloomberMat = BaseBodyMat("Sloomber"); return _sloomberMat; }

        private static Material BaseBodyMat(string preset)
        {
            try
            {
                var app = FirstAppearance(FindBaseDef(preset));
                if (app == null) return null;
                var structs = app.Structures;
                if (structs == null || structs.Length == 0) return null;
                var mats = structs[0].DefaultMaterials;
                return (mats != null && mats.Length > 0) ? mats[0] : null;
            }
            catch { return null; }
        }

        private static SlimeAppearance FirstAppearance(SlimeDefinition def)
        {
            try { return (def != null && def.AppearancesDefault != null && def.AppearancesDefault.Length > 0) ? def.AppearancesDefault[0] : null; }
            catch { return null; }
        }

        /// <summary>Log all field names on a SlimeDefinition to help find display name / icon fields.</summary>
        private static void LogDefFields(SlimeDefinition def, string label)
        {
            try
            {
                var t = Traverse.Create(def);
                MelonLogger.Msg($"[CSC] Fields for '{label}':");
                foreach (var fName in t.Fields())
                {
                    try
                    {
                        var f = t.Field(fName);
                        var val = f.GetValue();
                        var s = val != null ? val.ToString() : "null";
                        if (s.Length > 80) s = s.Substring(0, 80) + "...";
                        MelonLogger.Msg($"  {fName} = {s}");
                    }
                    catch { MelonLogger.Msg($"  {fName} = <error>"); }
                }
            }
            catch (System.Exception ex) { MelonLogger.Warning($"[CSC] LogDefFields: {ex.Message}"); }
        }

        /// <summary>Ensure the 3D preview camera + RT + clone exist for the given slime.</summary>
        internal static void EnsurePreview(CustomSlime cs)
        {
            try
            {
                if (_previewGO != null) { Object.DestroyImmediate(_previewGO); _previewGO = null; }
                if (_previewCam == null)
                {
                    var camObj = new GameObject("CSC_PreviewCam");
                    Object.DontDestroyOnLoad(camObj);
                    _previewCam = camObj.AddComponent<Camera>();
                    _previewCam.enabled = false; // manual Render only — never auto-render to screen
                    _previewCam.clearFlags = CameraClearFlags.SolidColor;
                    // TRANSPARENT background so the icon is just the slime (fits the vac frame instead of
                    // showing a dark square inside the square).
                    _previewCam.backgroundColor = new UnityEngine.Color(0f, 0f, 0f, 0f);
                    _previewCam.orthographic = true;
                    _previewCam.orthographicSize = 0.7f; // zoom so the slime fills the frame
                    _previewCam.nearClipPlane = 0.1f;
                    _previewCam.farClipPlane = 20f;
                    // Aim slightly above the prefab origin (which sits at the slime's base) to center the body.
                    _previewCam.transform.position = new Vector3(0, -4999.6f, -5);
                    _previewCam.transform.LookAt(new Vector3(0, -4999.6f, 0));

                    // POINT light (local) — a Directional light would illuminate the whole game scene
                    // and wash it out. A point light far below the map only lights the preview.
                    var lightObj = new GameObject("CSC_PreviewLight");
                    Object.DontDestroyOnLoad(lightObj);
                    var light = lightObj.AddComponent<Light>();
                    light.type = LightType.Point;
                    light.range = 12f;
                    light.intensity = 4f;
                    light.color = UnityEngine.Color.white;
                    light.transform.position = new Vector3(1.5f, -4998.5f, -2.5f);

                    _previewRT = new RenderTexture(128, 128, 24, RenderTextureFormat.ARGB32);
                    _previewRT.Create();
                    _previewCam.targetTexture = _previewRT;
                }

                // Face the camera (slimes face +Z; the camera looks from -Z), so rotate 180°.
                _previewGO = Object.Instantiate(cs.Prefab, new Vector3(0, -5000, 0), Quaternion.Euler(0, 180, 0));
                _previewGO.transform.localScale = cs.Prefab.transform.localScale * 0.8f; // a bit smaller in the frame
                FreezePreview(_previewGO);
                _previewGO.SetActive(true);
                ForceAppearance(_previewGO, cs);
                _previewSlime = cs;
                AimPreviewCamera(cs.Config);
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"[CSC] EnsurePreview: {ex.Message}");
            }
        }

        /// <summary>Capture the current preview render as the slime's icon.</summary>
        private static void CaptureIconFromPreview(CustomSlime cs)
        {
            try
            {
                if (_previewRT == null || _previewCam == null || _previewGO == null) return;

                _previewCam.Render();
                int w = _previewRT.width, h = _previewRT.height;
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                RenderTexture.active = _previewRT;
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                // Sprites sample bottom-up, so ReadPixels output is already the right way up for a
                // game Sprite — do NOT flip it here (flipping makes the in-game icon upside down).
                tex.hideFlags = HideFlags.DontUnloadUnusedAsset;

                var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                sprite.name = cs.Config.DisplayName + "_Icon";
                sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;

                // Set it via SetIconAndArt (+ clear _requiresFullArt) so it shows in the vacpack/pedia and persists.
                ApplyIcon(cs.Def, sprite);
                try { cs.App._icon = sprite; } catch { }
                CurrentIcon = sprite;
                SaveIcon(cs, tex); // persist so it's there on next load without recapturing
                MelonDebug.Msg($"[CSC] Icon captured + saved for '{cs.Config.Name}' ({tex.width}x{tex.height})");
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"[CSC] CaptureIconFromPreview: {ex.Message}");
            }
        }

        /// <summary>Schedule icon capture via the preview (old pixel-recolor fallback).</summary>
        private static void CaptureIcon(CustomSlime cs)
        {
            EnsurePreview(cs);
            _previewIconPending = 3; // capture after 3 frames so the preview is ready
        }

        private static string _previewSig;

        /// <summary>Create or update the 3D preview for a config (called from EditorUI).</summary>
        public static void TouchPreview(SlimeConfig cfg)
        {
            if (!Ready || cfg == null) return;
            _previewConfig = cfg;

            Built.TryGetValue(cfg.Name, out var cs);
            string sig = PartSig(cfg);

            // Only rebuild the (expensive) preview model when the STRUCTURE changes. Color tweaks just
            // recolor the existing model — this is what kills the F2 lag while dragging sliders.
            if (cs == null || _previewGO == null || _previewSlime != cs || _previewSig != sig)
            {
                BuildOrUpdate(cfg, out _);
                Built.TryGetValue(cfg.Name, out cs);
                if (cs == null) return;
                EnsurePreview(cs);
                _previewSig = sig;
            }
            else
            {
                cs.Config = cfg;
                Recolor(cs);
                ForceAppearance(_previewGO, cs);
            }
        }

        /// <summary>Capture the icon from the current 3D preview.</summary>
        public static void CaptureIconFromCurrentPreview()
        {
            if (_previewSlime != null)
                CaptureIconFromPreview(_previewSlime);
        }

        /// <summary>Rough equality check for two SlimeConfigs.</summary>
        private static bool ConfigsEqual(SlimeConfig a, SlimeConfig b)
        {
            if (a.Name != b.Name || a.BasePreset != b.BasePreset) return false;
            if (a.Top.r != b.Top.r || a.Top.g != b.Top.g || a.Top.b != b.Top.b) return false;
            if (a.Middle.r != b.Middle.r || a.Middle.g != b.Middle.g || a.Middle.b != b.Middle.b) return false;
            if (a.Bottom.r != b.Bottom.r || a.Bottom.g != b.Bottom.g || a.Bottom.b != b.Bottom.b) return false;
            if (a.Vac.r != b.Vac.r || a.Vac.g != b.Vac.g || a.Vac.b != b.Vac.b) return false;
            if (a.Parts.Count != b.Parts.Count) return false;
            for (int i = 0; i < a.Parts.Count; i++)
                if (a.Parts[i].Donor != b.Parts[i].Donor || a.Parts[i].Type != b.Parts[i].Type) return false;
            return true;
        }

        private static Transform Holder
        {
            get
            {
                if (_holder == null)
                {
                    _holder = new GameObject("CSC_Holder");
                    _holder.SetActive(false);
                    Object.DontDestroyOnLoad(_holder);
                }
                return _holder.transform;
            }
        }

        // ------------------------------------------------------------------ fusion helpers (called from ModEntry)

        /// <summary>Find a built custom slime by key.</summary>
        internal static CustomSlime FindBuilt(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            Built.TryGetValue(key, out var cs);
            return cs;
        }

        /// <summary>
        /// Icon sprite for a fusion parent, for the Fusions tab. Custom parents load their saved PNG;
        /// vanilla parents use the game's own icon. Cached so the UI doesn't rebuild sprites each frame.
        /// </summary>
        public static Sprite GetIconSprite(string key, bool custom)
        {
            if (string.IsNullOrEmpty(key)) return null;
            string ck = (custom ? "c:" : "v:") + key;
            if (_iconSpriteCache.TryGetValue(ck, out var sp) && sp != null) return sp;
            Sprite result = null;
            try
            {
                if (custom)
                {
                    // Load the custom slime's saved icon (raw pixels) — this is the photo made when the slime was saved.
                    var tex = LoadTexRaw(IconPngPath(key));
                    if (tex != null)
                    {
                        result = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                        result.hideFlags = HideFlags.DontUnloadUnusedAsset;
                    }
                }
                else
                {
                    // Vanilla slime icons live in atlases that native-crash IMGUI, so we RENDER the vanilla slime's 3D
                    // model to our own texture (pre-generated at fusion time via EnsureVanillaIcon) and load that.
                    var tex = LoadTexRaw(VanillaIconPath(key));
                    if (tex != null)
                    {
                        result = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                        result.hideFlags = HideFlags.DontUnloadUnusedAsset;
                    }
                }
            }
            catch { }
            if (result != null) _iconSpriteCache[ck] = result; // only cache hits, so it retries until the icon exists
            return result;
        }
        private static readonly Dictionary<string, Sprite> _iconSpriteCache = new Dictionary<string, Sprite>();

        /// <summary>A representative colour for a fusion parent, for the Fusions tab (safe — never touches a game
        /// texture). Custom → its top colour; vanilla → its sampled body colour.</summary>
        private static readonly Dictionary<string, Color> _sideColorCache = new Dictionary<string, Color>();
        public static Color GetSideColor(string key, bool custom)
        {
            string ck = (custom ? "c:" : "v:") + key;
            if (_sideColorCache.TryGetValue(ck, out var cached)) return cached; // cached — never recompute in OnGUI
            var result = new Color(0.5f, 0.5f, 0.5f, 1f);
            try
            {
                if (custom) { var cs = FindBuilt(key); if (cs != null && cs.Config != null) result = cs.Config.Top.ToColor(); }
                else { var def = FindBaseDef(key); if (def != null) { GetColors(def, out var top, out _, out _); result = top; } }
            }
            catch { }
            _sideColorCache[ck] = result;
            return result;
        }

        // ------------------------------------------------------------------ tiny safe wrappers

        private static string SafeName(SlimeDefinition d) { try { return d != null ? d.name : null; } catch { return null; } }
        private static string SafeRefId(SlimeDefinition d) { try { return d != null ? d.ReferenceId : null; } catch { return null; } }
        private static void SetColorSafe(Material m, string prop, Color c) { try { if (m.HasProperty(prop)) m.SetColor(prop, c); } catch { } }
        private static void EnableKeywordSafe(Material m, string k) { try { m.EnableKeyword(k); } catch { } }
        private static void DisableKeywordSafe(Material m, string k) { try { m.DisableKeyword(k); } catch { } }
        private static void CopyTexture(Material dst, Material src, string prop)
        {
            try { if (src != null && dst.HasProperty(prop) && src.HasProperty(prop)) dst.SetTexture(prop, src.GetTexture(prop)); } catch { }
        }
        private static void TrySet(Action a) { try { a(); } catch (Exception ex) { MelonLogger.Warning("[CustomSlimeCreator] " + ex.Message); } }
    }
}
