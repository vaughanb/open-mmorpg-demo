using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// The skin tone choice: the ramp, the measurements that make it land the same on every
    /// surface, the component on each body and the slider on the create screen.
    ///
    /// Skin could not be done the way hair was. The kit's body-part system recolours objects
    /// it instantiates as fake equipment, and skin is not one of those: it is on the
    /// character's own bare meshes, and it is also baked into the sleeves of three outfits
    /// where the forearms show through. See <see cref="MultiplayerARPG.Demo.DemoSkinTone"/>,
    /// which is what actually paints it.
    ///
    /// What this tool contributes is **the numbers**. The body sheet and the outfit sheet are
    /// different textures whose skin areas do not average to the same colour, so one tint
    /// would put a face and its own arms in two different places. Each material is measured
    /// here - its own texture sampled at the UVs of the submesh that wears it, area-weighted,
    /// averaged in linear space - and the runtime divides the target tone by that mean. The
    /// measurement is taken on the **arms** wherever a material has an arms mesh, because the
    /// cuff is the one seam where two different sheets meet on the same character and have to
    /// agree.
    /// </summary>
    public static class DemoSkinToneBuilder
    {
        private const string EntityDir = "Assets/OpenMMORPG/Demo/Prefabs/GamePlay/CharacterEntities";
        private const string ModelDir = "Assets/OpenMMORPG/Demo/Prefabs/GamePlay/CharacterModels";
        private const string ItemDir = "Assets/OpenMMORPG/Demo/GameData/Resources/Items";
        private const string CanvasHomePath = "Assets/OpenMMORPG/Demo/Prefabs/UI/Home/CanvasHome.prefab";

        /// <summary>Prefixes of every material that draws skin. Both packs name them this way.</summary>
        private static readonly string[] SkinMaterialPrefixes = { "MI_Body_", "MI_Skin_" };

        public struct Tone
        {
            public string Title;
            /// <summary>Authored in sRGB, which is how anyone would pick it; the runtime converts.</summary>
            public Color Colour;
        }

        /// <summary>
        /// The ramp, lightest first, so the slider runs light on the left to dark on the
        /// right. Nine stops: enough that dragging feels continuous, few enough that each
        /// one is a visibly different person.
        ///
        /// They follow the skin locus rather than a straight line between two ends - real
        /// skin gets more saturated through the middle of the range and desaturates again at
        /// the extremes, and a plain lerp from pale to dark runs through a dead grey-brown
        /// that looks like dirt rather than like skin.
        ///
        /// The **stock** look is not a stop chosen by hand: <see cref="NearestTone"/> finds
        /// whichever of these is closest to the tone the art already ships in, and that
        /// becomes the slider's starting position, so a player who never touches it gets the
        /// character the demo has always had.
        /// </summary>
        /// <summary>
        /// **How light the ramp may go is not a taste decision, and the limit is set by the
        /// clothes.** A tone is reached by multiplying the art's own skin, so the pale end
        /// runs out where the brightest skin texel reaches 1 and the highlights clip - past
        /// that the shading flattens and the character becomes a paper cut-out.
        ///
        /// Measured, the bare body could go to (0.78, 0.74, 0.68) linear, but the skin baked
        /// into the outfit sleeves only reaches **(0.67, 0.47, 0.43)** - a brighter sheet
        /// over a darker mean, so less room to climb. The ramp has to respect the tighter of
        /// the two or a fair-skinned character would have arms a shade paler than the hands
        /// coming out of them, which is the exact fault this whole feature exists to avoid.
        ///
        /// That is why the ramp is **not symmetrical about the stock tone**: three stops
        /// lighter, five darker. Going darker is a multiply below 1 and has no limit at all.
        /// An earlier ramp reached for a true porcelain, needed 2.2x of red, and rendered as
        /// a flat white silhouette; the one before that was pulled back by
        /// <see cref="ClampTones"/> until it was no longer lighter than the stop below it.
        /// These values sit inside the ceiling with room to spare, and the clamp is left in
        /// as the guard that says so if the art ever changes.
        /// </summary>
        public static readonly Tone[] Tones =
        {
            new Tone { Title = "Fair",   Colour = Hex(0xD2B0A2) },
            new Tone { Title = "Light",  Colour = Hex(0xC29A80) },
            new Tone { Title = "Warm",   Colour = Hex(0xB78764) },
            new Tone { Title = "Tan",    Colour = Hex(0xAC7A54) },
            new Tone { Title = "Olive",  Colour = Hex(0x99694A) },
            new Tone { Title = "Bronze", Colour = Hex(0x8E6040) },
            new Tone { Title = "Umber",  Colour = Hex(0x70482F) },
            new Tone { Title = "Deep",   Colour = Hex(0x543422) },
            new Tone { Title = "Ebony",  Colour = Hex(0x3A2317) },
        };

        private static Color Hex(int rgb)
        {
            return new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
        }

        /// <summary>What one skin material's own texture actually contains, where it is worn.</summary>
        public class Measured
        {
            public Color Mean;
            /// <summary>The brightest skin texel. What may be multiplied before anything clips.</summary>
            public Color Max;
        }

        [MenuItem("Open MMORPG/Demo/Build Skin Tones")]
        public static void Build()
        {
            Dictionary<string, Measured> measured = MeasureSkinMaterials();
            if (measured.Count == 0)
            {
                Debug.LogError($"[{nameof(DemoSkinToneBuilder)}] Found no skin materials to measure; nothing built.");
                return;
            }

            var clampReport = new System.Text.StringBuilder();
            Color[] tones = ClampTones(measured, clampReport);
            int bodies = WriteComponents(measured, tones);
            bool ui = BuildSlider();

            var report = new System.Text.StringBuilder();
            foreach (KeyValuePair<string, Measured> pair in measured)
            {
                report.Append("\n  ").Append(pair.Key)
                      .Append(" mean ").Append(pair.Value.Mean.ToString("F4"))
                      .Append(" max ").Append(pair.Value.Max.ToString("F4"))
                      .Append(" headroom x").Append(Headroom(pair.Value).ToString("F2"));
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[{nameof(DemoSkinToneBuilder)}] Skin tones built: {Tones.Length} tones on {bodies} " +
                      $"bodies, {(ui ? "slider on the create screen" : "NO SLIDER")}." +
                      $"{clampReport}\nMeasured skin materials (linear):{report}");
        }

        /// <summary>The largest tint any channel of this material can take before a skin texel clips.</summary>
        private static Vector3 HeadroomPerChannel(Measured measured)
        {
            return new Vector3(
                measured.Max.r > 0.0001f ? 1f / measured.Max.r : 999f,
                measured.Max.g > 0.0001f ? 1f / measured.Max.g : 999f,
                measured.Max.b > 0.0001f ? 1f / measured.Max.b : 999f);
        }

        private static float Headroom(Measured measured)
        {
            Vector3 h = HeadroomPerChannel(measured);
            return Mathf.Min(h.x, Mathf.Min(h.y, h.z));
        }

        /// <summary>
        /// Pulls any tone back to what the art can actually reach, and says so.
        ///
        /// A tone is out of reach when getting there would need a tint big enough to push
        /// the material's brightest skin texel past 1. The whole colour is scaled by the
        /// worst offending channel rather than each channel being clipped on its own,
        /// because clipping one channel changes the hue - a tone too bright in red would
        /// come back green. Scaling keeps the tone and only gives up some of its lightness.
        ///
        /// The ceiling is the tightest across **every** skin material, so the body and the
        /// sleeves cannot end up on different tones at the pale end of the slider.
        /// </summary>
        private static Color[] ClampTones(Dictionary<string, Measured> measured, System.Text.StringBuilder report)
        {
            var tones = new Color[Tones.Length];
            for (int i = 0; i < Tones.Length; ++i)
            {
                Color authored = Tones[i].Colour;
                Color linear = authored.linear;
                float scale = 1f;
                foreach (KeyValuePair<string, Measured> pair in measured)
                {
                    Color mean = pair.Value.Mean;
                    Vector3 headroom = HeadroomPerChannel(pair.Value);
                    scale = Mathf.Min(scale, ChannelScale(linear.r, mean.r, headroom.x));
                    scale = Mathf.Min(scale, ChannelScale(linear.g, mean.g, headroom.y));
                    scale = Mathf.Min(scale, ChannelScale(linear.b, mean.b, headroom.z));
                }
                if (scale >= 0.999f)
                {
                    tones[i] = authored;
                    continue;
                }
                Color pulled = new Color(linear.r * scale, linear.g * scale, linear.b * scale, 1f).gamma;
                tones[i] = pulled;
                report.Append($"\n  Pulled \"{Tones[i].Title}\" back to {(int)(scale * 100)}% of its authored lightness " +
                              $"({ColorUtility.ToHtmlStringRGB(authored)} -> {ColorUtility.ToHtmlStringRGB(pulled)}); " +
                              "any brighter and the skin highlights clip.");
            }
            return tones;
        }

        /// <summary>
        /// How far this tone's channel has to be scaled to stay reachable: the tint it asks
        /// for is target/mean, and the most that channel can take is its headroom.
        /// </summary>
        private static float ChannelScale(float target, float mean, float headroom)
        {
            if (mean <= 0.0001f || target <= 0f)
                return 1f;
            float wanted = target / mean;
            return wanted <= headroom ? 1f : headroom / wanted;
        }

        // ------------------------------------------------------------------
        // Measuring
        // ------------------------------------------------------------------

        /// <summary>
        /// The mean skin colour of every skin material in the demo, in linear space.
        ///
        /// Read off the meshes rather than off the texture as a whole: these are atlases, and
        /// the skin occupies only part of one. Sampling at the triangle centroids of the
        /// submesh that wears the material, weighted by triangle area, gives the colour that
        /// material actually shows and nothing else.
        /// </summary>
        private static Dictionary<string, Measured> MeasureSkinMaterials()
        {
            var means = new Dictionary<string, Measured>();
            var fromArms = new HashSet<string>();

            foreach (string path in ModelPaths())
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                    continue;
                foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
                {
                    Mesh mesh = MeshOf(renderer);
                    if (mesh == null)
                        continue;
                    // The cuff is the seam that has to agree, so an arms mesh wins over any
                    // other mesh wearing the same material.
                    bool isArms = renderer.name.IndexOf("Arms", System.StringComparison.OrdinalIgnoreCase) >= 0;
                    Material[] materials = renderer.sharedMaterials;
                    for (int i = 0; i < materials.Length && i < mesh.subMeshCount; ++i)
                    {
                        Material material = materials[i];
                        if (material == null || !IsSkin(material.name))
                            continue;
                        if (means.ContainsKey(material.name) && (!isArms || fromArms.Contains(material.name)))
                            continue;
                        Measured measured = MeasureSubmesh(mesh, i, material);
                        if (measured == null)
                            continue;
                        means[material.name] = measured;
                        if (isArms)
                            fromArms.Add(material.name);
                    }
                }
            }
            return means;
        }

        /// <summary>Both player bodies, and every garment the demo can equip.</summary>
        private static IEnumerable<string> ModelPaths()
        {
            var paths = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { ModelDir }))
                paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            foreach (string guid in AssetDatabase.FindAssets("t:ArmorItem", new[] { ItemDir }))
            {
                var item = AssetDatabase.LoadAssetAtPath<BaseEquipmentItem>(AssetDatabase.GUIDToAssetPath(guid));
                if (item == null || item.EquipmentModels == null)
                    continue;
                foreach (EquipmentModel model in item.EquipmentModels)
                {
                    if (model.MeshPrefab == null)
                        continue;
                    string path = AssetDatabase.GetAssetPath(model.MeshPrefab);
                    if (!string.IsNullOrEmpty(path) && !paths.Contains(path))
                        paths.Add(path);
                }
            }
            return paths;
        }

        private static bool IsSkin(string materialName)
        {
            foreach (string prefix in SkinMaterialPrefixes)
            {
                if (materialName.StartsWith(prefix))
                    return true;
            }
            return false;
        }

        private static Mesh MeshOf(Renderer renderer)
        {
            var skinned = renderer as SkinnedMeshRenderer;
            if (skinned != null)
                return skinned.sharedMesh;
            var filter = renderer.GetComponent<MeshFilter>();
            return filter == null ? null : filter.sharedMesh;
        }

        /// <summary>
        /// The mean and the peak of one material's skin, in linear space.
        ///
        /// Several samples per triangle rather than one at the centroid: the mean barely
        /// moves either way, but the **peak** does. A highlight that sits inside a large
        /// face is invisible to a single centroid sample, and the peak is what decides how
        /// far the pale end of the ramp may go - so missing one would let a tone through
        /// that clips in exactly the place a centroid never looked.
        /// </summary>
        private static Measured MeasureSubmesh(Mesh mesh, int submesh, Material material)
        {
            if (!material.HasProperty("_BaseMap"))
                return null;
            Texture texture = material.GetTexture("_BaseMap");
            if (texture == null)
                return null;
            Texture2D readable = LoadReadable(AssetDatabase.GetAssetPath(texture));
            if (readable == null)
                return null;
            try
            {
                int[] triangles = mesh.GetTriangles(submesh);
                Vector2[] uv = mesh.uv;
                Vector3[] vertices = mesh.vertices;
                if (triangles.Length == 0 || uv.Length == 0)
                    return null;
                // Barycentric sample points: the centroid and five spread around it.
                Vector3[] weights =
                {
                    new Vector3(1f / 3f, 1f / 3f, 1f / 3f),
                    new Vector3(0.6f, 0.2f, 0.2f),
                    new Vector3(0.2f, 0.6f, 0.2f),
                    new Vector3(0.2f, 0.2f, 0.6f),
                    new Vector3(0.45f, 0.45f, 0.1f),
                    new Vector3(0.1f, 0.45f, 0.45f),
                };
                double r = 0, g = 0, b = 0, weight = 0;
                var max = new Color(0f, 0f, 0f, 1f);
                for (int i = 0; i + 2 < triangles.Length; i += 3)
                {
                    int a = triangles[i], c = triangles[i + 1], d = triangles[i + 2];
                    if (a >= uv.Length || c >= uv.Length || d >= uv.Length)
                        continue;
                    float area = Vector3.Cross(vertices[c] - vertices[a], vertices[d] - vertices[a]).magnitude * 0.5f;
                    if (area <= 0f)
                        continue;
                    foreach (Vector3 bary in weights)
                    {
                        Vector2 point = uv[a] * bary.x + uv[c] * bary.y + uv[d] * bary.z;
                        // Averaged in linear space. A mean taken on sRGB values reads far too
                        // light, which is the same trap the hair shades ran into.
                        Color linear = readable.GetPixelBilinear(point.x, point.y).linear;
                        r += linear.r * area;
                        g += linear.g * area;
                        b += linear.b * area;
                        weight += area;
                        max.r = Mathf.Max(max.r, linear.r);
                        max.g = Mathf.Max(max.g, linear.g);
                        max.b = Mathf.Max(max.b, linear.b);
                    }
                }
                if (weight <= 0)
                    return null;
                return new Measured
                {
                    Mean = new Color((float)(r / weight), (float)(g / weight), (float)(b / weight), 1f),
                    Max = max,
                };
            }
            finally
            {
                Object.DestroyImmediate(readable);
            }
        }

        /// <summary>
        /// Loads a texture's file straight off disk into a readable copy, so the measurement
        /// never has to turn `Read/Write Enabled` on in the import settings and leave it on.
        /// </summary>
        private static Texture2D LoadReadable(string path)
        {
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
                return null;
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (ImageConversion.LoadImage(texture, System.IO.File.ReadAllBytes(path)))
                return texture;
            Object.DestroyImmediate(texture);
            return null;
        }

        /// <summary>
        /// Which tone the art already is. The stock body is the yardstick, because that is
        /// what every character looked like before there was a choice.
        /// </summary>
        private static int NearestTone(Color stockMean, Color[] tones)
        {
            int best = 1;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < tones.Length; ++i)
            {
                Color tone = tones[i].linear;
                float distance = (tone.r - stockMean.r) * (tone.r - stockMean.r) +
                                 (tone.g - stockMean.g) * (tone.g - stockMean.g) +
                                 (tone.b - stockMean.b) * (tone.b - stockMean.b);
                if (distance >= bestDistance)
                    continue;
                bestDistance = distance;
                best = i + 1;
            }
            return best;
        }

        // ------------------------------------------------------------------
        // The bodies
        // ------------------------------------------------------------------

        private static int WriteComponents(Dictionary<string, Measured> means, Color[] tones)
        {
            var skinMaterials = new List<MultiplayerARPG.Demo.DemoSkinTone.SkinMaterial>();
            foreach (KeyValuePair<string, Measured> pair in means)
            {
                skinMaterials.Add(new MultiplayerARPG.Demo.DemoSkinTone.SkinMaterial
                {
                    materialName = pair.Key,
                    measuredMean = pair.Value.Mean,
                });
            }

            var titles = new string[Tones.Length];
            for (int i = 0; i < Tones.Length; ++i)
                titles[i] = Tones[i].Title;

            int written = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { EntityDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                // The template carries a PlayerCharacterEntity, so the "players only" test
                // below passes for it. It is an input, not an entity - see IsTemplate.
                if (DemoEntityBuilder.IsTemplate(path))
                    continue;
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    // Players only. An NPC or a monster has no create screen and no saved
                    // tone, and a component that can never be driven is one that only looks
                    // like it might do something.
                    var player = root.GetComponent<BasePlayerCharacterEntity>();
                    var existing = root.GetComponent<MultiplayerARPG.Demo.DemoSkinTone>();
                    if (player == null)
                    {
                        if (existing == null)
                            continue;
                        Object.DestroyImmediate(existing);
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                        continue;
                    }

                    MultiplayerARPG.Demo.DemoSkinTone skin = existing != null
                        ? existing
                        : root.AddComponent<MultiplayerARPG.Demo.DemoSkinTone>();
                    skin.skinMaterials = skinMaterials.ToArray();
                    skin.tones = (Color[])tones.Clone();
                    skin.toneTitles = titles;
                    skin.defaultTone = DefaultToneFor(root, means, tones);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    written++;
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
            return written;
        }

        /// <summary>
        /// The slider's starting position for one body: the ramp stop nearest that body's own
        /// stock skin, so a player who leaves the slider alone gets the character the demo
        /// already had. Measured per body because the male and female sheets are not the same
        /// file, even though they turn out to be within a per-cent of each other.
        /// </summary>
        private static int DefaultToneFor(GameObject entity, Dictionary<string, Measured> means, Color[] tones)
        {
            var model = entity.GetComponentInChildren<BaseCharacterModel>(true);
            if (model != null)
            {
                foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer.name.IndexOf("Arms", System.StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    foreach (Material material in renderer.sharedMaterials)
                    {
                        Measured measured;
                        if (material != null && means.TryGetValue(material.name, out measured))
                            return NearestTone(measured.Mean, tones);
                    }
                }
            }
            // No arms found: fall back to whatever body material was measured at all.
            foreach (KeyValuePair<string, Measured> pair in means)
            {
                if (pair.Key.StartsWith("MI_Body_"))
                    return NearestTone(pair.Value.Mean, tones);
            }
            return 1;
        }

        // ------------------------------------------------------------------
        // The create screen
        // ------------------------------------------------------------------

        /// <summary>
        /// The window goes at the foot of the right column, under the size slider it is
        /// paired with. Where exactly is <see cref="DemoCreateColumn.Restack"/>'s business;
        /// this is only how tall it is.
        /// </summary>
        private const string WindowName = "Window--Skin";
        private const float WindowHeight = 84f;

        /// <summary>The colour the slider's fill takes: the skin it is choosing.</summary>
        private static readonly Color SliderFill = new Color(0.78f, 0.62f, 0.44f, 1f);

        /// <summary>
        /// Puts the window on the create screen and points the runtime driver at what is in
        /// it. The window itself is built by <see cref="DemoCreateColumn"/>, which the size
        /// slider below it shares, so the two cannot drift apart.
        /// </summary>
        private static bool BuildSlider()
        {
            GameObject canvas = PrefabUtility.LoadPrefabContents(CanvasHomePath);
            try
            {
                Transform create = DemoCreateColumn.FindDeep(canvas.transform, "UICharacterCreate");
                if (create == null)
                {
                    Debug.LogError($"[{nameof(DemoSkinToneBuilder)}] No UICharacterCreate in {CanvasHomePath}.");
                    return false;
                }

                GameObject window = DemoCreateColumn.Clone(create, "Window--Beard", WindowName, "Skin", WindowHeight);
                if (window == null)
                    return false;

                // The swatch takes the left of the readout row, so the name is inset past it.
                Text label = DemoCreateColumn.Label(window.transform, "ToneName",
                    new Vector2(34f, -52f), new Vector2(-8f, -32f), TextAnchor.MiddleLeft);
                Image swatch = DemoCreateColumn.Swatch(window.transform, new Vector2(10f, -34f), new Vector2(18f, 16f));
                Slider slider = DemoCreateColumn.Build(window.transform, "ToneSlider",
                    new Vector2(10f, -78f), new Vector2(-10f, -58f), SliderFill, Tones.Length);

                var driver = create.GetComponent<MultiplayerARPG.Demo.DemoSkinToneSlider>();
                if (driver == null)
                    driver = create.gameObject.AddComponent<MultiplayerARPG.Demo.DemoSkinToneSlider>();
                driver.slider = slider;
                driver.label = label;
                driver.swatch = swatch;

                DemoCreateColumn.Restack(create);
                DemoCreateColumn.PlaceBackButton(DemoCreateColumn.FindDeep(canvas.transform, "UICharacterList"));
                PrefabUtility.SaveAsPrefabAsset(canvas, CanvasHomePath);
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(canvas);
            }
        }
    }
}
