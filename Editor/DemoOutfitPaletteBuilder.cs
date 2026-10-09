using System.IO;
using UnityEditor;
using UnityEngine;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// Recoloured copies of an outfit, so an enemy wearing a class's armour does not look like a player in
    /// it (user, 2026-10-05: "change the bandit characters' armor colors so they don't just look like the
    /// player character... maybe make them black").
    ///
    /// **A copy, never the outfit's own material.** `MI_Ranger` is shared by every ranger garment - the
    /// armour items a player wears and the pieces a bandit drops - so recolouring it would recolour the
    /// players. The palette is a second texture and a second material beside it, and only the bodies whose
    /// `Variant.Palette` names it (`DemoCharacterBuilder`) are switched to it.
    ///
    /// **The texture is recoloured by hue, not tinted.** One sheet carries the cloth, the leather and the
    /// metal fittings; a tint over all of it turns buckles to soot and leather to mud. The ranger sheet's
    /// colours sit in clean bands (measured: leather 20-35 degrees of hue, the jerkin and hood 80-100, the
    /// sleeves and trousers 120-135, metal unsaturated), so the cloth band is taken to near-black keeping its
    /// own shading - the hood a charcoal, the trousers almost black - the leather is darkened by a third, and
    /// the pale fittings (buckles, wraps, the pauldron's cloth) are blackened to a dull grey, so that every
    /// piece of the bandits' set is told from the ranger's at a glance. The band reaches down to 42 degrees because the padding round each
    /// UV island is a blur of the cloth into the leather, olive; left half-converted it made a yellow fringe
    /// that a distant mip would bleed into the seams.
    ///
    /// Generated art is a stand-in, so the texture is only written where there is none - a hand-painted one
    /// is kept - and `Regenerate Bandit Colours` overwrites it. The material is made once and after that only
    /// pointed at the texture, so a tweak to it by hand survives too.
    /// </summary>
    public static class DemoOutfitPaletteBuilder
    {
        /// <summary>The bandits' colours: the ranger's leathers in black.</summary>
        public const string Bandit = "Bandit";

        private const string ArtDir = "Assets/OpenMMORPG/Demo/Art/Characters";
        private const string LibraryMaterialDir = "Assets/Plugins/Quaternius/Characters/Materials";

        /// <summary>The demo's art budget: textures are collected at 1024.</summary>
        private const int MaxSize = 1024;

        // ---- the bandit palette -------------------------------------------------
        // Hues in degrees. The cloth band fades in over ClothFrom and out over ClothTo, and only above the
        // saturation ramp, so grey metal and the near-white highlights are never touched.

        private static readonly Vector2 ClothFrom = new Vector2(42f, 58f);
        private static readonly Vector2 ClothTo = new Vector2(160f, 180f);
        private static readonly Vector2 ClothSaturation = new Vector2(0.12f, 0.25f);

        /// <summary>What the cloth becomes: a cool near-black, at this share of its own brightness.</summary>
        private const float ClothHue = 220f;
        private const float ClothChroma = 0.15f;
        private const float ClothValue = 0.30f;

        /// <summary>The leather band, faded out over this range of hue, and how much darker and greyer it gets.</summary>
        private static readonly Vector2 LeatherTo = new Vector2(40f, 55f);
        private static readonly Vector2 LeatherSaturation = new Vector2(0.20f, 0.35f);
        private const float LeatherValue = 0.62f;
        private const float LeatherChroma = 0.85f;

        /// <summary>
        /// The pale things - the pauldron's cloth, the buckles, the bracers' wraps - blackened: anything
        /// unsaturated and bright, faded in over these ramps, to this share of its brightness. Left as they
        /// were, the bandit's pauldron and boots were the ranger's to the pixel in the bag (2026-10-05).
        /// </summary>
        private static readonly Vector2 PaleSaturation = new Vector2(0.10f, 0.22f);
        private static readonly Vector2 PaleBrightness = new Vector2(0.30f, 0.55f);
        private const float PaleValue = 0.42f;
        private const float PaleChroma = 0.6f;

        [MenuItem("Open MMORPG/Demo/Build Bandit Colours")]
        public static void BuildMenu()
        {
            int models = DemoCharacterBuilder.ApplyPalettes(false);
            Debug.Log($"[{nameof(DemoOutfitPaletteBuilder)}] Bandit colours on {models} model(s). The entities nest the " +
                      "models, so nothing else needs building; Build Map Server only if you want the server's copy to match.");
        }

        /// <summary>
        /// The bandits' black set as items (see `DemoItemBuilder.BanditArmourGrade`), on a project already
        /// built: the six pieces with their garments and icons, their upkeep, the set and its element
        /// (`Build Combat Data`, which is safe to repeat), and the bandits' loot tables. The full pipeline
        /// makes the same through `Build Items`, `Build Gear Upkeep`, `Build Combat Data` and `Wire Game
        /// Database`. The green set's recipes are `Build Progression`'s, and the bench's `Build Craft Stations`.
        /// </summary>
        [MenuItem("Open MMORPG/Demo/Build Bandit Set")]
        public static void BuildBanditSetMenu()
        {
            int items = DemoItemBuilder.BuildPaletteArmour().Count;
            int upkeep = DemoItemBuilder.BuildPaletteArmourUpkeep();
            DemoCombatDataBuilder.Build();
            int drops = DemoDatabaseWiring.RefreshMonsterDrops();
            AssetDatabase.SaveAssets();
            Debug.Log($"[{nameof(DemoOutfitPaletteBuilder)}] Bandit set: {items} piece(s), upkeep on {upkeep}, " +
                      $"{drops} drop table(s) rewritten. Build Map Server for the MMO flow.");
        }

        [MenuItem("Open MMORPG/Demo/Regenerate Bandit Colours (overwrites the texture)")]
        public static void RegenerateMenu()
        {
            if (!EditorUtility.DisplayDialog("Regenerate Bandit Colours",
                    "The bandits' outfit texture and the bandit set's icons will be recoloured again from the ranger's, " +
                    "over any painting done on them by hand.",
                    "Regenerate", "Cancel"))
                return;
            Regenerate();
        }

        internal static void Regenerate()
        {
            int models = DemoCharacterBuilder.ApplyPalettes(true);
            int items = DemoItemBuilder.BuildPaletteArmour(regenerateIcons: true).Count;
            AssetDatabase.SaveAssets();
            Debug.Log($"[{nameof(DemoOutfitPaletteBuilder)}] Bandit colours regenerated and put on {models} model(s); {items} icon(s) redrawn.");
        }

        /// <summary>
        /// Puts a palette's material on every renderer of <paramref name="root"/> wearing the outfit's own
        /// (by name, so it matches the library's material and the collected copy alike). Returns how many
        /// slots it changed.
        /// </summary>
        public static int Apply(GameObject root, string outfit, string palette, bool regenerate = false)
        {
            Material recoloured = MaterialFor(outfit, palette, regenerate);
            if (recoloured == null)
                return 0;
            string sourceName = $"MI_{outfit}";
            int changed = 0;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials = renderer.sharedMaterials;
                bool any = false;
                for (int i = 0; i < materials.Length; ++i)
                {
                    if (materials[i] == null || materials[i].name != sourceName)
                        continue;
                    materials[i] = recoloured;
                    any = true;
                    ++changed;
                }
                if (any)
                    renderer.sharedMaterials = materials;
            }
            return changed;
        }

        /// <summary>Where the recoloured garments go: the armour items' own models, in a palette's colours.</summary>
        private const string GarmentDir = "Assets/OpenMMORPG/Demo/Prefabs/GamePlay/Equipments/Outfits";

        /// <summary>
        /// A garment in a palette's colours, for an armour item - the bandits' black set (2026-10-05): a
        /// prefab variant of the outfit's own model with the palette's material on it, so the mesh, the
        /// rig and everything the kit rebinds stay the model's and only the material differs. Rewritten
        /// each time; there is nothing on it to tune by hand.
        /// </summary>
        public static GameObject GarmentFor(GameObject model, string outfit, string palette)
        {
            if (model == null)
                return null;
            DemoItemBuilder.EnsureFolder(GarmentDir);
            string path = $"{GarmentDir}/{model.name}_{palette}.prefab";
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            try
            {
                if (Apply(instance, outfit, palette) == 0)
                {
                    Debug.LogError($"[{nameof(DemoOutfitPaletteBuilder)}] {model.name} wears no MI_{outfit} to recolour.");
                    return null;
                }
                instance.name = $"{model.name}_{palette}";
                return PrefabUtility.SaveAsPrefabAsset(instance, path);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>
        /// An item icon in a palette's colours, recoloured from another item's icon the same way the
        /// texture is - the icons are renders of the garments, so the same hue bands hold - and written
        /// beside it only if there is none (a re-render from the Equipment Icon Generator, or a painted
        /// one, is kept). Imported as a sprite with the source's settings.
        /// </summary>
        public static Sprite IconFor(string sourceIconPath, string iconPath, bool overwrite = false)
        {
            if (overwrite || !File.Exists(iconPath))
            {
                if (string.IsNullOrEmpty(sourceIconPath) || !File.Exists(sourceIconPath))
                    return null;
                var icon = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                icon.LoadImage(File.ReadAllBytes(sourceIconPath));
                Color[] pixels = icon.GetPixels();
                for (int i = 0; i < pixels.Length; ++i)
                    pixels[i] = Recolour(pixels[i]);
                icon.SetPixels(pixels);
                icon.Apply();
                File.WriteAllBytes(iconPath, icon.EncodeToPNG());
                Object.DestroyImmediate(icon);
                AssetDatabase.ImportAsset(iconPath, ImportAssetOptions.ForceUpdate);
                var from = AssetImporter.GetAtPath(sourceIconPath) as TextureImporter;
                var to = AssetImporter.GetAtPath(iconPath) as TextureImporter;
                if (from != null && to != null)
                {
                    var settings = new TextureImporterSettings();
                    from.ReadTextureSettings(settings);
                    to.SetTextureSettings(settings);
                    to.textureType = TextureImporterType.Sprite;
                    to.spriteImportMode = SpriteImportMode.Single;
                    to.alphaIsTransparency = true;
                    to.maxTextureSize = from.maxTextureSize;
                    to.textureCompression = from.textureCompression;
                    to.SaveAndReimport();
                }
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(iconPath);
        }

        /// <summary>The palette's material, made (and its texture generated) if there is none.</summary>
        public static Material MaterialFor(string outfit, string palette, bool regenerate = false)
        {
            Material source = AssetDatabase.LoadAssetAtPath<Material>($"{ArtDir}/Materials/MI_{outfit}.mat")
                              ?? AssetDatabase.LoadAssetAtPath<Material>($"{LibraryMaterialDir}/MI_{outfit}.mat");
            if (source == null)
            {
                Debug.LogError($"[{nameof(DemoOutfitPaletteBuilder)}] No MI_{outfit} to recolour.");
                return null;
            }
            Texture2D texture = TextureFor(source, outfit, palette, regenerate);
            if (texture == null)
                return null;

            DemoItemBuilder.EnsureFolder($"{ArtDir}/Materials");
            string path = $"{ArtDir}/Materials/MI_{outfit}_{palette}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(source) { name = $"MI_{outfit}_{palette}" };
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_MainTex"))
                material.SetTexture("_MainTex", texture);
            // Garments are single surfaces and are drawn from both sides - DemoCharacterBuilder.ShowBothSidesOfCloth.
            if (material.HasProperty("_Cull"))
                material.SetFloat("_Cull", 0f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Texture2D TextureFor(Material source, string outfit, string palette, bool regenerate)
        {
            DemoItemBuilder.EnsureFolder($"{ArtDir}/Textures");
            string path = $"{ArtDir}/Textures/T_{outfit}_{palette}_BaseColor.png";
            if (!regenerate && File.Exists(path))
                return AssetDatabase.LoadAssetAtPath<Texture2D>(path);

            // The collected copy when there is one: it is already at the demo's size.
            string sourcePath = $"{ArtDir}/Textures/T_{outfit}_BaseColor.png";
            if (!File.Exists(sourcePath))
                sourcePath = AssetDatabase.GetAssetPath(source.GetTexture("_BaseMap"));
            if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
            {
                Debug.LogError($"[{nameof(DemoOutfitPaletteBuilder)}] No base colour texture behind {source.name}.");
                return null;
            }

            // Read from the file rather than the imported texture, which is compressed and not readable.
            var sheet = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            sheet.LoadImage(File.ReadAllBytes(sourcePath));
            int width = sheet.width, height = sheet.height;
            Color[] pixels = sheet.GetPixels();
            Object.DestroyImmediate(sheet);
            while (width > MaxSize && height > 1)
                pixels = Halve(pixels, ref width, ref height);

            for (int i = 0; i < pixels.Length; ++i)
                pixels[i] = Recolour(pixels[i]);

            var result = new Texture2D(width, height, TextureFormat.RGBA32, false);
            result.SetPixels(pixels);
            result.Apply();
            File.WriteAllBytes(path, result.EncodeToPNG());
            Object.DestroyImmediate(result);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            // Imported as the sheet it was made from is: colour, mipmapped, the same compression.
            var from = AssetImporter.GetAtPath(sourcePath) as TextureImporter;
            var to = AssetImporter.GetAtPath(path) as TextureImporter;
            if (from != null && to != null)
            {
                var settings = new TextureImporterSettings();
                from.ReadTextureSettings(settings);
                to.SetTextureSettings(settings);
                to.maxTextureSize = Mathf.Min(from.maxTextureSize, MaxSize);
                to.textureCompression = from.textureCompression;
                to.SaveAndReimport();
            }
            Debug.Log($"[{nameof(DemoOutfitPaletteBuilder)}] Wrote {path} ({width}x{height}) from {sourcePath}.");
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>The bandit palette: cloth to near-black, leather darker, the pale fittings blackened.</summary>
        private static Color Recolour(Color colour)
        {
            Color.RGBToHSV(colour, out float hue, out float saturation, out float value);
            float degrees = hue * 360f;
            float cloth = Ramp(ClothFrom, degrees) * (1f - Ramp(ClothTo, degrees)) * Ramp(ClothSaturation, saturation);
            float leather = (degrees < 60f ? 1f - Ramp(LeatherTo, degrees) : 0f) * Ramp(LeatherSaturation, saturation);
            float pale = (1f - Ramp(PaleSaturation, saturation)) * Ramp(PaleBrightness, value) * (1f - cloth);

            Color darkLeather = Color.HSVToRGB(hue, saturation * LeatherChroma, value * LeatherValue);
            Color blackened = Color.HSVToRGB(hue, saturation * PaleChroma, value * PaleValue);
            Color black = Color.HSVToRGB(ClothHue / 360f, ClothChroma, value * ClothValue);
            Color result = Color.Lerp(colour, darkLeather, leather);
            result = Color.Lerp(result, blackened, pale);
            result = Color.Lerp(result, black, cloth);
            result.a = colour.a;
            return result;
        }

        private static float Ramp(Vector2 range, float x)
        {
            float t = Mathf.Clamp01((x - range.x) / (range.y - range.x));
            return t * t * (3f - 2f * t);
        }

        /// <summary>A 2x2 box filter: the sheet at half the size.</summary>
        private static Color[] Halve(Color[] pixels, ref int width, ref int height)
        {
            int w = width / 2, h = height / 2;
            var half = new Color[w * h];
            for (int y = 0; y < h; ++y)
            {
                for (int x = 0; x < w; ++x)
                {
                    int i = 2 * y * width + 2 * x;
                    half[y * w + x] = (pixels[i] + pixels[i + 1] + pixels[i + width] + pixels[i + width + 1]) * 0.25f;
                }
            }
            width = w;
            height = h;
            return half;
        }
    }
}
