using System;
using System.Collections.Generic;
using System.Linq;
using CustomSlimeCreator.Core;
using MelonLoader;
using UnityEngine;

namespace CustomSlimeCreator.UI
{
    /// <summary>Standalone tabbed in-game editor (F2). Manual GUI.* layout for Il2Cpp safety.</summary>
    public static class EditorUI
    {
        private static bool _visible;
        private static int _tab;
        private static readonly string[] Tabs = { "Look", "Parts", "Options", "Saved", "Fusions" };
        private static readonly string[] TabKeys = { "tab_look", "tab_parts", "tab_options", "tab_saved", "tab_fusions" };

        private static SlimeConfig _data = new SlimeConfig();
        private static string[] _presets = { "Pink" };
        private static List<string> _configNames = new List<string>();
        private static int _configSel;

        private static Rect _win = new Rect(24, 24, 520, 680);
        private static Texture2D _white;
        private static bool _drag;
        private static Vector2 _dragOff;
        private static string _status = "";
        private static float _statusUntil;
        private static bool _previewDirty;
        private static int _previewDebounce;
        private static readonly string[] FoodGroupNames = { "FruitGroup", "VeggieGroup", "MeatGroup", "NectarFoodGroup", "ChickGroup" };
        private static readonly string[] ZoneNames = {
            "Reef", "Strand", "MossBlanket", "FrostReef", "BrumeMire",
            "EmberValley", "PowderfallBluffs", "StarlightStrand", "Abyss", "Lab"
        };

        private static string _focusField, _focusBuffer, _activeSlider;
        private static bool _showTutorial, _tutSeen;
        private static int _tutPage;
        private static string _openDropdown;   // id of the currently expanded dropdown, or null


        private static GUIStyle _label, _section, _statusStyle, _wrap;
        private static bool _styles;
        private const int H = 22;

        public static bool IsVisible => _visible;

        // ------------------------------------------------------------------ lifecycle

        public static void Tick()
        {
            if (!_visible) return;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            GameAccess.SetPlayerInput(false); // re-apply every frame; the game re-enables its maps

            if (_previewDirty)
            {
                _previewDirty = false;
                _previewDebounce = 15; // frames before auto-capture
                SlimeEngine.TouchPreview(_data);
            }
            if (_previewDebounce > 0)
            {
                _previewDebounce--;
                if (_previewDebounce == 0)
                    SlimeEngine.CaptureIconFromCurrentPreview();
            }
        }

        public static void Toggle()
        {
            _visible = !_visible;
            GameAccess.SetPlayerInput(!_visible); // freeze camera/movement while the editor is open
            if (!_visible)
            {
                // Restore the game's cursor lock on close (otherwise the free cursor stays stuck on screen).
                try { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; } catch { }
            }
            if (_visible)
            {
                _focusField = null;
                _openDropdown = null;
                if (!_tutSeen) { _showTutorial = true; _tutPage = 0; }
                RefreshConfigList();
                var live = SlimeEngine.EnsureReady() ? SlimeEngine.AvailablePresets() : null;
                if (live != null && live.Count > 0) _presets = live.ToArray();
                _previewDirty = true;
                SetStatus(SlimeEngine.Ready
                    ? "Edit, then 'Create / Update' to apply live, or 'Spawn'."
                    : "Enter a save first, then reopen to load the game's slimes.");
            }
        }

        private static void RefreshConfigList() => _configNames = ConfigStore.LoadAll().Select(c => c.Name).ToList();

        // ------------------------------------------------------------------ draw

        public static void Draw()
        {
            if (!_visible) return;
            InitStyles();
            var e = Event.current;
            HandleDrag(e);

            GUI.Box(_win, "");
            GUI.Box(new Rect(_win.x, _win.y, _win.width, 22), "  " + Loc.T("title") + "          " + Loc.T("close"));

            float x = _win.x + 10, w = _win.width - 20;
            float y = _win.y + 28;

            if (_showTutorial) { DrawTutorial(e, x, y, w); return; }

            // Tab bar
            float tw = w / Tabs.Length;
            for (int i = 0; i < Tabs.Length; i++)
                if (Btn(e, x + i * tw, y, tw - 2, Loc.T(TabKeys[i]), i == _tab)) { _tab = i; _openDropdown = null; }
            y += H + 6;

            // Tab content
            try
            {
                switch (_tab)
                {
                    case 0: DrawLook(e, x, ref y, w); break;
                    case 1: DrawParts(e, x, ref y, w); break;
                    case 2: DrawOptions(e, x, ref y, w); break;
                    case 3: DrawSaved(e, x, ref y, w); break;
                    case 4: DrawFusions(e, x, ref y, w); break;
                }
            }
            catch (Exception ex) { GUI.Label(new Rect(x, y, w, 40), "UI error: " + ex.Message, _label); }

            // Action bar (bottom, always visible)
            float ay = _win.y + _win.height - 58;
            GUI.Box(new Rect(_win.x + 6, ay - 4, _win.width - 12, 1), "");
            float bw = (w - 16) / 3f;
            if (Btn(e, x, ay, bw, Loc.T("create"), false)) DoBuild();
            if (Btn(e, x + bw + 8, ay, bw, Loc.T("spawn"), false)) DoSpawn();
            if (Btn(e, x + 2 * (bw + 8), ay, bw, Loc.T("save"), false)) DoSave();

            if (!string.IsNullOrEmpty(_status) && Time.realtimeSinceStartup < _statusUntil)
                GUI.Label(new Rect(_win.x + 10, _win.y + _win.height - 24, _win.width - 20, H), _status, _statusStyle);
        }

        private static void DrawLook(Event e, float x, ref float y, float w)
        {
            Section(x, ref y, Loc.T("identity"));
            Label(x, y, Loc.T("name"), 96); _data.Name = TextBox(e, x + 100, ref y, w - 100, _data.Name, "name");
            Label(x, y, Loc.T("display"), 96); _data.DisplayName = TextBox(e, x + 100, ref y, w - 100, _data.DisplayName, "disp");

            int pIdx = Math.Max(0, Array.IndexOf(_presets, _data.BasePreset));
            int np = Dropdown(e, x, ref y, w, Loc.T("preset"), "basepreset", _presets, pIdx);
            if (np != pIdx && np >= 0 && np < _presets.Length) { _data.BasePreset = _presets[np]; _previewDirty = true; }

            Section(x, ref y, Loc.T("colors"));
            ColorRow(e, x, ref y, w, Loc.T("clr_top"), ref _data.Top);
            ColorRow(e, x, ref y, w, Loc.T("clr_mid"), ref _data.Middle);
            ColorRow(e, x, ref y, w, Loc.T("clr_bot"), ref _data.Bottom);
            ColorRow(e, x, ref y, w, Loc.T("clr_vac"), ref _data.Vac);

            // Preview (left) + centering arrows (middle) + generated in-game icon (right).
            Section(x, ref y, Loc.T("preview"));
            const float pvSize = 116f, iconSize = 80f;
            var rt = SlimeEngine.PreviewRT;
            if (rt != null) GUI.DrawTexture(new Rect(x, y, pvSize, pvSize), rt, ScaleMode.StretchToFill, false);
            else GUI.Box(new Rect(x, y, pvSize, pvSize), "...");

            // 4 arrows + zoom to hand-center the shot.
            float cx = x + pvSize + 40;
            if (Btn(e, cx - 14, y + 2, 28, "^", false)) NudgeIcon(0, 0.05f, 0);
            if (Btn(e, cx - 46, y + 26, 28, "<", false)) NudgeIcon(-0.05f, 0, 0);
            if (Btn(e, cx + 18, y + 26, 28, ">", false)) NudgeIcon(0.05f, 0, 0);
            if (Btn(e, cx - 14, y + 50, 28, "v", false)) NudgeIcon(0, -0.05f, 0);
            if (Btn(e, cx - 46, y + 80, 28, "-", false)) NudgeIcon(0, 0, 0.1f);
            if (Btn(e, cx + 18, y + 80, 28, "+", false)) NudgeIcon(0, 0, -0.1f);

            var icon = SlimeEngine.CurrentIcon;
            float ix = x + w - iconSize - 2, iy = y + (pvSize - iconSize) / 2;
            if (icon != null && icon.texture != null) GUI.DrawTexture(new Rect(ix, iy, iconSize, iconSize), icon.texture, ScaleMode.StretchToFill, false);
            else GUI.Box(new Rect(ix, iy, iconSize, iconSize), "Icon");
            GUI.Box(new Rect(ix - 2, iy - 2, iconSize + 4, iconSize + 4), "");
            Label(x, y + pvSize + 2, Loc.T("preview_hint"), w);
            y += pvSize + 26;
        }

        private static void NudgeIcon(float dx, float dy, float dz)
        {
            _data.IconOffX += dx; _data.IconOffY += dy;
            _data.IconZoom = Mathf.Clamp(_data.IconZoom + dz, 0.3f, 2f);
            SlimeEngine.NudgePreview(_data);
            _previewDebounce = 15; // re-capture the icon after re-aiming
        }

        private static void DrawParts(Event e, float x, ref float y, float w)
        {
            Section(x, ref y, Loc.T("parts_title"));

            for (int i = 0; i < _data.Parts.Count; i++)
            {
                var p = _data.Parts[i];
                float rowY = y;

                // Donor slime (dropdown of every preset)
                int di = Math.Max(0, Array.IndexOf(_presets, p.Donor));
                int nd = Dropdown(e, x, ref y, w - 62, Loc.T("from"), "donor" + i, _presets, di);
                if (nd != di && nd < _presets.Length) { p.Donor = _presets[nd]; _previewDirty = true; }
                if (Btn(e, x + w - 58, rowY, 58, Loc.T("remove"), false)) { _data.Parts.RemoveAt(i); i--; _openDropdown = null; _previewDirty = true; continue; }

                // Part type — ONLY the parts this donor actually has
                var avail = SlimeEngine.AvailablePartsFor(p.Donor);
                if (avail.Count == 0) avail.Add(p.Type);
                if (!avail.Contains(p.Type)) p.Type = avail[0];
                var arr = avail.ToArray();
                int ti = Math.Max(0, System.Array.IndexOf(arr, p.Type));
                int nt = Dropdown(e, x, ref y, w, Loc.T("part"), "part" + i, arr, ti);
                if (nt != ti && nt < arr.Length) { p.Type = arr[nt]; _previewDirty = true; }

                p.Recolor = Toggle(e, x, ref y, 140, p.Recolor, Loc.T("recolor"));
                if (p.Recolor)
                {
                    ColorRow(e, x, ref y, w, "Top", ref p.Top);
                    ColorRow(e, x, ref y, w, "Mid", ref p.Middle);
                    ColorRow(e, x, ref y, w, "Bot", ref p.Bottom);
                }
                y += 8;
            }

            if (Btn(e, x, y, 150, Loc.T("add_part"), false)) { _data.Parts.Add(new PartConfig()); _previewDirty = true; }
            y += H;
        }

        private static void DrawOptions(Event e, float x, ref float y, float w)
        {
            Section(x, ref y, Loc.T("shader_fx"));
            float half = w / 2f;
            _data.TwinEffect = Toggle(e, x, ref y, half - 4, _data.TwinEffect, Loc.T("twin_swirl"));
            _data.SloomberEffect = ToggleSameRow(e, x + half, y - H, half - 4, _data.SloomberEffect, Loc.T("sloomber_stars"));

            Section(x, ref y, Loc.T("body_fx"));
            _data.RadAuraEffect = Toggle(e, x, ref y, half - 4, _data.RadAuraEffect, Loc.T("aura_hyper"));
            _data.CrystalShardsEffect = ToggleSameRow(e, x + half, y - H, half - 4, _data.CrystalShardsEffect, Loc.T("crystal_shards"));
            _data.RockPlatingEffect = Toggle(e, x, ref y, half - 4, _data.RockPlatingEffect, Loc.T("rock_plating"));
            _data.AnglerLureEffect = ToggleSameRow(e, x + half, y - H, half - 4, _data.AnglerLureEffect, Loc.T("angler_lure"));
            _data.HunterPatternEffect = Toggle(e, x, ref y, half - 4, _data.HunterPatternEffect, Loc.T("hunter_parts"));
            _data.RingtailPatternEffect = ToggleSameRow(e, x + half, y - H, half - 4, _data.RingtailPatternEffect, Loc.T("ringtail_parts"));

            Section(x, ref y, Loc.T("options"));
            _data.CanLargofy = Toggle(e, x, ref y, half - 4, _data.CanLargofy, Loc.T("can_largofy"));
            _data.CreateAllLargos = ToggleSameRow(e, x + half, y - H, half - 4, _data.CreateAllLargos, Loc.T("all_largos"));
            _data.EdibleByTarrs = Toggle(e, x, ref y, half - 4, _data.EdibleByTarrs, Loc.T("edible_tarr"));
            _data.Vaccable = ToggleSameRow(e, x + half, y - H, half - 4, _data.Vaccable, Loc.T("vaccable"));

            Section(x, ref y, Loc.T("plort"));
            _data.HasPlort = Toggle(e, x, ref y, half - 4, _data.HasPlort, Loc.T("has_plort"));
            _data.SupportRadiant = ToggleSameRow(e, x + half, y - H, half - 4, _data.SupportRadiant, Loc.T("radiant"));
            if (_data.HasPlort)
            {
                Label(x, y, Loc.T("value"), 50);
                if (Btn(e, x + 54, y, 22, "-", false)) _data.PlortValue = Mathf.Max(5, _data.PlortValue - 5);
                GUI.Label(new Rect(x + 80, y, 50, H), _data.PlortValue.ToString());
                if (Btn(e, x + 134, y, 22, "+", false)) _data.PlortValue = Mathf.Min(200, _data.PlortValue + 5);
                y += H;
                ColorRow(e, x, ref y, w, Loc.T("plort_top") + " ", ref _data.PlortTop);
                ColorRow(e, x, ref y, w, Loc.T("plort_mid") + " ", ref _data.PlortMiddle);
                ColorRow(e, x, ref y, w, Loc.T("plort_bot") + " ", ref _data.PlortBottom);
            }

            Section(x, ref y, Loc.T("diet"));
            float fx = x;
            for (int i = 0; i < FoodGroupNames.Length; i++)
            {
                bool has = _data.FoodGroups.Contains(FoodGroupNames[i]);
                bool hit = ToggleSameRow(e, fx, y, 130, has, " " + FoodGroupNames[i].Replace("Group", ""));
                if (hit != has)
                {
                    if (hit) _data.FoodGroups.Add(FoodGroupNames[i]);
                    else _data.FoodGroups.Remove(FoodGroupNames[i]);
                }
                fx += 134;
                if ((i + 1) % 2 == 0) { fx = x; y += H; }
            }
            if (FoodGroupNames.Length % 2 != 0) y += H;

            Section(x, ref y, Loc.T("zones"));
            fx = x;
            for (int i = 0; i < ZoneNames.Length; i++)
            {
                bool has = _data.SpawnZones.Contains(ZoneNames[i]);
                bool hit = ToggleSameRow(e, fx, y, 140, has, " " + ZoneNames[i]);
                if (hit != has)
                {
                    if (hit) _data.SpawnZones.Add(ZoneNames[i]);
                    else _data.SpawnZones.Remove(ZoneNames[i]);
                }
                fx += 144;
                if ((i + 1) % 3 == 0) { fx = x; y += H; }
            }
            if (ZoneNames.Length % 3 != 0) y += H;

            Section(x, ref y, Loc.T("notes"));
            Label(x, y, Loc.T("notes_text"), w); y += H;
        }

        private static void DrawSaved(Event e, float x, ref float y, float w)
        {
            Section(x, ref y, Loc.T("saved_slimes") + " (" + _configNames.Count + ")");
            if (_configNames.Count > 0)
            {
                _configSel = Mathf.Clamp(_configSel, 0, _configNames.Count - 1);
                _configSel = Cycler(e, x, ref y, w, "", _configSel, _configNames.ToArray());
                float bw = (w - 16) / 3f;
                if (Btn(e, x, y, bw, Loc.T("load"), false)) DoLoad();
                if (Btn(e, x + bw + 8, y, bw, Loc.T("delete"), false)) DoDelete();
                if (Btn(e, x + 2 * (bw + 8), y, bw, Loc.T("new"), false)) DoNew();
                y += H + 4;
            }
            else
            {
                Label(x, y, Loc.T("no_saved"), w); y += H;
                if (Btn(e, x, y, 120, Loc.T("new"), false)) DoNew();
                y += H;
            }

            Section(x, ref y, Loc.T("folder"));
            if (Btn(e, x, y, 160, Loc.T("open_folder"), false))
            { try { System.Diagnostics.Process.Start(ConfigStore.Folder); } catch { } }
            y += H;
        }

        // Discovered fusions: each row shows both parents' icons + the fused name. Populated as the player
        // actually forms fusions in the world (custom×custom and custom×vanilla).
        private static void DrawFusions(Event e, float x, ref float y, float w)
        {
            List<FusionEntry> list = null;
            try { list = FusionRegistry.All; } catch { }
            Section(x, ref y, Loc.T("fusions_title") + " (" + (list != null ? list.Count : 0) + ")");
            if (list == null || list.Count == 0) { Label(x, y, Loc.T("fusions_none"), w); y += H + 2; Label(x, y, Loc.T("fusions_hint"), w); y += H; return; }

            const float rowH = 40f, icon = 34f;
            int max = Math.Min(list.Count, 12); // window has no scrollbar; cap the visible rows
            for (int i = 0; i < max; i++)
            {
                var f = list[i];
                if (f == null) continue;
                // Each row is fully guarded — a bad icon/name must never crash the whole GUI (OnGUI exceptions
                // were closing the game when this tab was opened).
                try
                {
                    DrawParentIcon(f.AKey, f.ACustom, x, y, icon);
                    GUI.Label(new Rect(x + icon + 4, y + 8, 14, H), "+", _label);
                    DrawParentIcon(f.BKey, f.BCustom, x + icon + 20, y, icon);
                    float tx = x + 2 * icon + 28;
                    GUI.Label(new Rect(tx, y + 2, w - (tx - x), H), f.DisplayName ?? "", _section);
                    GUI.Label(new Rect(tx, y + 20, w - (tx - x), H), (f.ADisplay ?? "?") + " + " + (f.BDisplay ?? "?"), _label);
                }
                catch { }
                y += rowH;
            }
            if (list.Count > max) { Label(x, y, "… +" + (list.Count - max) + " more", w); y += H; }
        }

        // Draws a fusion parent's icon. CRITICAL: only ever hand IMGUI our OWN Texture2D (custom PNG). Vanilla
        // slime icons are atlas/addressable textures that NATIVE-crash GUI.DrawTexture* — so vanilla parents get a
        // solid colour swatch instead (SlimeEngine.GetSideColor never touches a game texture).
        private static void DrawParentIcon(string key, bool custom, float x, float y, float size)
        {
            var box = new Rect(x, y, size, size);
            GUI.Box(box, "");
            Texture tex = null;
            try { var sp = SlimeEngine.GetIconSprite(key, custom); if (sp != null) tex = sp.texture; } catch { }
            if (tex != null) { try { GUI.DrawTexture(box, tex, ScaleMode.ScaleToFit, true); return; } catch { } }
            try { Solid(box, SlimeEngine.GetSideColor(key, custom)); GUI.Box(box, ""); } catch { }
        }

        // ------------------------------------------------------------------ actions

        private static void DoBuild()
        {
            if (!Valid()) return;
            if (SlimeEngine.BuildOrUpdate(_data, out var err)) { SetStatus($"'{_data.Name}' updated live. Spawn to see it (existing ones recolored too)."); _previewDirty = true; }
            else SetStatus("Can't build: " + err);
        }

        private static void DoSpawn()
        {
            if (!Valid()) return;
            if (SlimeEngine.Spawn(_data, out var err)) SetStatus($"Spawned '{_data.Name}' in front of you.");
            else SetStatus("Can't spawn: " + err);
        }

        private static void DoSave()
        {
            if (!Valid()) return;
            try { ConfigStore.Save(_data); RefreshConfigList(); SetStatus($"Saved '{_data.Name}' to disk."); }
            catch (Exception ex) { SetStatus("Save error: " + ex.Message); }
        }

        private static void DoLoad()
        {
            if (_configSel < 0 || _configSel >= _configNames.Count) return;
            var cfg = ConfigStore.LoadAll().FirstOrDefault(c => c.Name == _configNames[_configSel]);
            if (cfg != null) { _data = cfg; _focusField = null; _tab = 0; _previewDirty = true; SetStatus($"Loaded '{cfg.Name}'."); }
        }

        private static void DoDelete()
        {
            if (_configSel < 0 || _configSel >= _configNames.Count) return;
            var name = _configNames[_configSel];
            ConfigStore.Delete(name); RefreshConfigList(); SetStatus($"Deleted '{name}'.");
        }

        private static void DoNew()
        {
            _data = new SlimeConfig { Name = "Slime" + UnityEngine.Random.Range(100, 999), DisplayName = "New Slime" };
            _focusField = null; _tab = 0; _previewDirty = true;
            SetStatus("New slime. Set a Name (letters), pick colors, then Create / Update.");
        }

        private static bool Valid()
        {
            if (string.IsNullOrWhiteSpace(_data.Name)) { SetStatus("Enter a Name first."); return false; }
            return true;
        }

        // ------------------------------------------------------------------ widgets

        private static void InitStyles()
        {
            if (_styles) return;
            _label = new GUIStyle { fontSize = 12, fixedHeight = H }; _label.normal.textColor = Color.white;
            _section = new GUIStyle { fontSize = 13, fontStyle = FontStyle.Bold }; _section.normal.textColor = Color.cyan;
            _statusStyle = new GUIStyle { fontSize = 12, fixedHeight = H }; _statusStyle.normal.textColor = Color.yellow;
            _wrap = new GUIStyle { fontSize = 13, wordWrap = true }; _wrap.normal.textColor = Color.white;
            _styles = true;
        }

        private static void HandleDrag(Event e)
        {
            var bar = new Rect(_win.x, _win.y, _win.width, 22);
            if (e.type == EventType.MouseDown && bar.Contains(e.mousePosition)) { _drag = true; _dragOff = e.mousePosition - _win.position; e.Use(); }
            if (_drag && e.type == EventType.MouseDrag) { _win.position = e.mousePosition - _dragOff; e.Use(); }
            if (_drag && e.type == EventType.MouseUp) _drag = false;
        }

        private static void Section(float x, ref float y, string t) { y += 4; GUI.Label(new Rect(x, y, 320, H), t, _section); y += 22; }
        private static void Label(float x, float y, string t, float w) => GUI.Label(new Rect(x, y, w, H), t, _label);

        private static bool RawHit(Rect r, Event e) { if (e.type == EventType.MouseDown && r.Contains(e.mousePosition)) { e.Use(); return true; } return false; }

        private static bool Btn(Event e, float x, float y, float w, string text, bool active)
        {
            var r = new Rect(x, y, w, H);
            GUI.Box(r, active ? "» " + text + " «" : text);
            return RawHit(r, e);
        }

        private static bool Toggle(Event e, float x, ref float y, float w, bool val, string text) { var r = ToggleSameRow(e, x, y, w, val, text); y += H; return r; }

        private static bool ToggleSameRow(Event e, float x, float y, float w, bool val, string text)
        {
            var r = new Rect(x, y, w, H);
            GUI.Box(r, (val ? "[X]" : "[  ]") + text);
            return RawHit(r, e) ? !val : val;
        }

        private static int Cycler(Event e, float x, ref float y, float w, string label, int idx, string[] names)
        {
            float lw = string.IsNullOrEmpty(label) ? 0 : 96;
            if (lw > 0) Label(x, y, label, lw);
            float bx = x + lw;
            if (Btn(e, bx, y, 26, "<", false)) idx = names.Length > 0 ? (idx - 1 + names.Length) % names.Length : 0;
            float nameW = w - lw - 56;
            GUI.Box(new Rect(bx + 28, y, nameW, H), names.Length > 0 ? names[Mathf.Clamp(idx, 0, names.Length - 1)] : "-");
            if (Btn(e, bx + 28 + nameW + 2, y, 26, ">", false)) idx = names.Length > 0 ? (idx + 1) % names.Length : 0;
            y += H;
            return idx;
        }

        private static void ColorRow(Event e, float x, ref float y, float w, string label, ref Col c)
        {
            byte or = c.r, og = c.g, ob = c.b;
            Label(x, y, label, 52);
            float sx = x + 54, sw = (w - 54 - 30) / 3f;
            c.r = (byte)MiniSlider(e, label + "r", sx, y, sw - 4, c.r);
            c.g = (byte)MiniSlider(e, label + "g", sx + sw, y, sw - 4, c.g);
            c.b = (byte)MiniSlider(e, label + "b", sx + 2 * sw, y, sw - 4, c.b);
            if (c.r != or || c.g != og || c.b != ob) _previewDirty = true;
            var sw2 = new Rect(x + w - 26, y + 2, 24, H - 4);
            Solid(sw2, c.ToColor()); GUI.Box(sw2, "");
            y += H;
        }

        private static float MiniSlider(Event e, string id, float x, float y, float w, float val)
        {
            var track = new Rect(x, y + 6, w, 6);
            float t = Mathf.Clamp01(val / 255f);
            GUI.Box(track, ""); GUI.Box(new Rect(x + t * w - 4, y + 1, 8, H - 6), "");
            var hot = new Rect(x, y, w, H);
            if (e.type == EventType.MouseDown && hot.Contains(e.mousePosition)) { _activeSlider = id; e.Use(); }
            if (e.type == EventType.MouseUp && _activeSlider == id) _activeSlider = null;
            if (_activeSlider == id && (e.type == EventType.MouseDrag || e.type == EventType.MouseDown))
            { val = Mathf.Clamp01((e.mousePosition.x - x) / w) * 255f; e.Use(); }
            return val;
        }

        private static string TextBox(Event e, float x, ref float y, float w, string val, string field)
        {
            var r = new Rect(x, y, w, H);
            GUI.Box(r, _focusField == field ? _focusBuffer + "|" : val);
            if (RawHit(r, e)) { _focusField = field; _focusBuffer = val ?? ""; }
            y += H;
            if (_focusField == field && e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter || e.keyCode == KeyCode.Escape || e.keyCode == KeyCode.Tab) { _focusField = null; e.Use(); }
                else if (e.keyCode == KeyCode.Backspace && _focusBuffer.Length > 0) { _focusBuffer = _focusBuffer.Substring(0, _focusBuffer.Length - 1); e.Use(); }
                else if (e.character != 0 && !char.IsControl(e.character)) { _focusBuffer += e.character; e.Use(); }
                return _focusBuffer;
            }
            return _focusField == field ? _focusBuffer : val;
        }

        private static Texture2D White
        {
            get
            {
                if (_white == null)
                {
                    _white = new Texture2D(1, 1);
                    _white.SetPixel(0, 0, Color.white);
                    _white.Apply();
                    _white.hideFlags = HideFlags.HideAndDontSave;
                }
                return _white;
            }
        }

        private static void Solid(Rect r, Color c) { var old = GUI.color; GUI.color = c; GUI.DrawTexture(r, White); GUI.color = old; }

        private static void DrawTutorial(Event e, float x, float y, float w)
        {
            var pages = Loc.Tutorial();
            _tutPage = Mathf.Clamp(_tutPage, 0, pages.Length - 1);
            GUI.Label(new Rect(x, y + 4, w, H), Loc.T("tut_title") + "   (" + (_tutPage + 1) + "/" + pages.Length + ")", _section);
            var box = new Rect(x, y + 30, w, _win.height - 110);
            GUI.Box(box, "");
            GUI.Label(new Rect(box.x + 8, box.y + 8, box.width - 16, box.height - 16), pages[_tutPage], _wrap);

            float by = _win.y + _win.height - 40, hw = (w - 8) / 2f;
            if (Btn(e, x, by, hw, Loc.T("tut_skip"), false)) { _showTutorial = false; _tutSeen = true; }
            string next = _tutPage < pages.Length - 1 ? Loc.T("tut_next") : "OK";
            if (Btn(e, x + hw + 8, by, hw, next, false))
            {
                if (_tutPage < pages.Length - 1) _tutPage++;
                else { _showTutorial = false; _tutSeen = true; }
            }
        }

        // Click-to-expand dropdown (only one open at a time). Returns the selected index.
        private static int Dropdown(Event e, float x, ref float y, float w, string label, string id, string[] options, int idx)
        {
            float lw = string.IsNullOrEmpty(label) ? 0 : 64;
            if (lw > 0) Label(x, y, label, lw);
            float bx = x + lw, bw = w - lw;
            string cur = options.Length > 0 ? options[Mathf.Clamp(idx, 0, options.Length - 1)] : "-";
            if (Btn(e, bx, y, bw, cur + "   v", _openDropdown == id)) { _openDropdown = (_openDropdown == id) ? null : id; }
            y += H;
            if (_openDropdown == id)
            {
                int rows = Mathf.Min(options.Length, 7);
                float listH = rows * H;
                GUI.Box(new Rect(bx, y, bw, listH), "");
                for (int i = 0; i < rows; i++)
                    if (GUI.Button(new Rect(bx, y + i * H, bw, H), options[i])) { idx = i; _openDropdown = null; }
                y += listH + 2;
            }
            return idx;
        }

        private static void SetStatus(string msg) { _status = msg; _statusUntil = Time.realtimeSinceStartup + 8f; }
    }
}
