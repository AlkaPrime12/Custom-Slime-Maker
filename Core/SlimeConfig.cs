using System.Collections.Generic;
using UnityEngine;

namespace CustomSlimeCreator.Core
{
    /// <summary>Simple 0-255 RGB color that survives JSON round-trips (UnityEngine.Color does not).</summary>
    public struct Col
    {
        public byte r;
        public byte g;
        public byte b;

        public Col(byte r, byte g, byte b) { this.r = r; this.g = g; this.b = b; }

        public Color ToColor() => new Color(r / 255f, g / 255f, b / 255f, 1f);
        public Color32 ToColor32() => new Color32(r, g, b, 255);

        public static Col From(Color c) => new Col(
            (byte)Mathf.Clamp(Mathf.RoundToInt(c.r * 255f), 0, 255),
            (byte)Mathf.Clamp(Mathf.RoundToInt(c.g * 255f), 0, 255),
            (byte)Mathf.Clamp(Mathf.RoundToInt(c.b * 255f), 0, 255));
    }

    /// <summary>
    /// The full definition of a custom slime, as authored in the in-game editor and persisted to JSON.
    /// Everything here is applied on top of a cloned native slime (Pink by default) via the Prism API.
    /// </summary>
    public class SlimeConfig
    {
        // --- Identity ---
        // Name must be letters only (A-Z / a-z) because Prism uses it to build reference ids.
        public string Name = "MySlime";
        public string DisplayName = "My Slime";
        // Native preset whose colors can be sampled in the editor. Structure is always cloned from Pink in v1.
        public string BasePreset = "Pink";

        // --- Body colors ---
        public Col Top = new Col(120, 225, 120);
        public Col Middle = new Col(85, 200, 85);
        public Col Bottom = new Col(55, 165, 55);
        public Col Vac = new Col(120, 225, 120); // vac beam / palette / ammo color

        // --- Plort ---
        public bool HasPlort = true;
        public int PlortValue = 30; // market base value
        public Col PlortTop = new Col(150, 240, 150);
        public Col PlortMiddle = new Col(95, 205, 95);
        public Col PlortBottom = new Col(60, 165, 60);

        // --- Diet ---
        // Valid group keys: FruitGroup, VeggieGroup, MeatGroup, NectarFoodGroup, ChickGroup
        public List<string> FoodGroups = new List<string> { "FruitGroup" };
        // Reference names of specific favorite foods (optional), e.g. "Carrot", "Pogofruit".
        public List<string> FavoriteFoods = new List<string>();

        // --- Options ---
        public bool CanLargofy = true;
        public bool CreateAllLargos = false;
        public bool EdibleByTarrs = true;
        public bool Vaccable = true;
        public bool SinkInShallowWater = true;
        public bool SupportRadiant = true;

        // --- Zones where this slime naturally spawns (empty = editor spawn only) ---
        public List<string> SpawnZones = new List<string>();

        // --- Shader effects (material keywords like Twin/Sloomber) ---
        public bool TwinEffect = false;
        public bool SloomberEffect = false;

        // --- Body effects (auto-add parts from other slimes, recolored to config) ---
        public bool RadAuraEffect = false;       // glowing aura from Rad slime
        public bool CrystalShardsEffect = false; // crystal spikes from Crystal slime
        public bool RockPlatingEffect = false;   // rock plates from Rock slime
        public bool AnglerLureEffect = false;    // lure from Angler slime
        public bool HunterPatternEffect = false; // spots pattern from Hunter slime
        public bool RingtailPatternEffect = false; // swirl pattern from Ringtail slime

        /// <summary>Maps body-effect names to (donor slime, element type).</summary>
        public static readonly (string name, string donor, string elem)[][] BodyEffectMap = new[]
        {
            new[] { ("Rad Aura", "Rad", "AURA") },
            new[] { ("Crystal Shards", "Crystal", "FOREHEAD") },
            new[] { ("Rock Plating", "Rock", "FOREHEAD") },
            new[] { ("Angler Lure", "Angler", "FOREHEAD") },
            new[] { ("Hunter Pattern", "Hunter", "SURFACE") },
            new[] { ("Ringtail Pattern", "Ringtail", "SURFACE") },
        };

        // --- Parts (wings, ears, spikes, etc. taken from other slimes) ---
        public List<PartConfig> Parts = new List<PartConfig>();

        // --- Icon framing (the 4 arrows in the preview pan the shot; zoom fits it) ---
        public float IconOffX = 0f;
        public float IconOffY = 0f;
        public float IconZoom = 0.55f; // smaller ortho size = slime fills more of the icon square

        /// <summary>
        /// Signature of everything that affects the LOOK. If this is unchanged the saved icon is reused;
        /// if it changes (any color/part/effect/preset/framing) a new icon is generated. The NAME is not
        /// part of it, so renaming keeps the icon.
        /// </summary>
        public string LookSig()
        {
            string C(Col c) => c.r + "," + c.g + "," + c.b;
            var sb = new System.Text.StringBuilder();
            sb.Append(BasePreset).Append('|').Append(C(Top)).Append(C(Middle)).Append(C(Bottom)).Append(C(Vac));
            sb.Append('|').Append(TwinEffect).Append(SloomberEffect);
            sb.Append('|').Append(RadAuraEffect).Append(CrystalShardsEffect).Append(RockPlatingEffect)
              .Append(AnglerLureEffect).Append(HunterPatternEffect).Append(RingtailPatternEffect);
            foreach (var p in Parts) sb.Append('|').Append(p.Sig());
            sb.Append('|').Append(IconOffX).Append(',').Append(IconOffY).Append(',').Append(IconZoom);
            return sb.ToString();
        }

        public SlimeConfig Clone()
        {
            return new SlimeConfig
            {
                Name = Name,
                DisplayName = DisplayName,
                BasePreset = BasePreset,
                Top = Top, Middle = Middle, Bottom = Bottom, Vac = Vac,
                HasPlort = HasPlort, PlortValue = PlortValue,
                PlortTop = PlortTop, PlortMiddle = PlortMiddle, PlortBottom = PlortBottom,
                FoodGroups = new List<string>(FoodGroups),
                FavoriteFoods = new List<string>(FavoriteFoods),
                CanLargofy = CanLargofy, CreateAllLargos = CreateAllLargos,
                EdibleByTarrs = EdibleByTarrs, Vaccable = Vaccable,
                SinkInShallowWater = SinkInShallowWater, SupportRadiant = SupportRadiant,
                SpawnZones = new List<string>(SpawnZones),
                TwinEffect = TwinEffect, SloomberEffect = SloomberEffect,
                RadAuraEffect = RadAuraEffect, CrystalShardsEffect = CrystalShardsEffect,
                RockPlatingEffect = RockPlatingEffect, AnglerLureEffect = AnglerLureEffect,
                HunterPatternEffect = HunterPatternEffect, RingtailPatternEffect = RingtailPatternEffect,
                IconOffX = IconOffX, IconOffY = IconOffY, IconZoom = IconZoom,
                Parts = Parts.ConvertAll(p => p.Clone()),
            };
        }
    }

    /// <summary>A body part (wings, ears, tail, spikes, Rad aura...) copied from another slime.</summary>
    public class PartConfig
    {
        public string Type = "WINGS";      // SlimeAppearanceElement.ElementType name
        public string Donor = "Flutter";   // slime to copy the part from
        public bool Recolor = false;       // tint the part with the colors below
        public Col Top = new Col(230, 230, 230);
        public Col Middle = new Col(200, 200, 200);
        public Col Bottom = new Col(170, 170, 170);

        public string Sig() => $"{Type}|{Donor}|{Recolor}|{Top.r},{Top.g},{Top.b}|{Middle.r},{Middle.g},{Middle.b}|{Bottom.r},{Bottom.g},{Bottom.b}";

        public PartConfig Clone() => new PartConfig
        {
            Type = Type, Donor = Donor, Recolor = Recolor, Top = Top, Middle = Middle, Bottom = Bottom
        };
    }
}
