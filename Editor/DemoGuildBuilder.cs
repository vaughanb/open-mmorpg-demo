using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// What a guild on this island can be: three skills to spend its levels on, and a set
    /// of crests to fly.
    ///
    /// The guild system itself was never switched off - `SocialSystemSetting` has carried
    /// fifty members, a role table, a fifty-level exp tree and a thousand-gold founding fee
    /// since the demo was built, and the guild windows are in the HUD and not hidden. A
    /// player could found one at any point. There was simply **nothing inside it**:
    /// `guildSkills` and `guildIcons` were both empty, so a guild levelled up, collected
    /// skill points, and had nothing to spend them on.
    ///
    /// **A guild earns a skill point per level** (`GuildData.IncreaseGuildExp`), and it
    /// earns exp from the share of a kill its members choose to give it - `ShareExpPercentage`
    /// per member, capped at the setting's 20%. So the loop is entirely the kit's and needs
    /// no seeding: join, set a share, kill things, spend the points here.
    /// </summary>
    public static class DemoGuildBuilder
    {
        private const string ResourcesDir = "Assets/OpenMMORPG/Demo/GameData/Resources";
        private const string SkillDir = ResourcesDir + "/GuildSkills";
        private const string IconDir = ResourcesDir + "/GuildIcons";
        private const string CrestDir = "Assets/OpenMMORPG/Demo/Textures/Icons/Guild";

        /// <summary>
        /// Three passive skills and no active one.
        ///
        /// An active guild skill needs a buff, a cooldown readout and somewhere to put the
        /// button; a passive one is a number that changes. The demo is showing that guild
        /// progression exists and what it is spent on, and three numbers do that without
        /// asking for art the island does not have. The kit supports `Active` and the field
        /// is right there when somebody wants it.
        ///
        /// Five levels each against a fifty-level guild: deliberately not enough to cap
        /// everything, so the points are a choice rather than a queue.
        /// </summary>
        private struct SkillSpec
        {
            public string Name;
            public string Title;
            public string Description;
            public int MaxLevel;
            /// <summary>Extra members allowed, per level.</summary>
            public int Members;
            /// <summary>Extra experience for every member, per level, as a percentage.</summary>
            public float Exp;
            /// <summary>Extra gold for every member, per level, as a percentage.</summary>
            public float Gold;
        }

        private static readonly SkillSpec[] Skills =
        {
            new SkillSpec
            {
                Name = "Fellowship", Title = "Fellowship", MaxLevel = 5, Members = 4,
                Description = "Word gets around. Room for four more at every rank.",
            },
            new SkillSpec
            {
                Name = "SharedLessons", Title = "Shared Lessons", MaxLevel = 5, Exp = 3f,
                Description = "What one of you learns the hard way, the rest of you learn over a drink. Every member earns 3% more experience at every rank.",
            },
            new SkillSpec
            {
                Name = "CommonPurse", Title = "Common Purse", MaxLevel = 5, Gold = 3f,
                Description = "Buying as a company gets a better price than buying as a stranger. Every member earns 3% more gold at every rank.",
            },
        };

        /// <summary>
        /// The crests a guild can fly. A colour and a charge each, drawn rather than
        /// painted - the same bargain the skill icons made: a generated set so the feature
        /// is not blank, replaced the moment somebody draws a better one, and never painted
        /// over once they have.
        /// </summary>
        private struct CrestSpec
        {
            public string Name;
            public string Title;
            public Color Field;
            public Charge Charge;
        }

        private enum Charge { Fess, Pale, Cross, Chevron, Lozenge, Roundel }

        private static readonly CrestSpec[] Crests =
        {
            new CrestSpec { Name = "CrestVerdant", Title = "The Green Bar", Field = new Color(0.24f, 0.45f, 0.26f), Charge = Charge.Fess },
            new CrestSpec { Name = "CrestTide", Title = "The Tide", Field = new Color(0.18f, 0.35f, 0.52f), Charge = Charge.Pale },
            new CrestSpec { Name = "CrestWard", Title = "The Ward", Field = new Color(0.55f, 0.18f, 0.20f), Charge = Charge.Cross },
            new CrestSpec { Name = "CrestHeadland", Title = "The Headland", Field = new Color(0.42f, 0.33f, 0.18f), Charge = Charge.Chevron },
            new CrestSpec { Name = "CrestBarrow", Title = "The Barrow", Field = new Color(0.30f, 0.24f, 0.38f), Charge = Charge.Lozenge },
            new CrestSpec { Name = "CrestSun", Title = "The Sun", Field = new Color(0.62f, 0.48f, 0.16f), Charge = Charge.Roundel },
        };

        private const int CrestSize = 128;

        [MenuItem("Open MMORPG/Demo/Build Guild", priority = 155)]
        public static void Build()
        {
            DemoItemBuilder.EnsureFolder(SkillDir);
            DemoItemBuilder.EnsureFolder(IconDir);
            DemoItemBuilder.EnsureFolder(CrestDir);

            foreach (SkillSpec spec in Skills)
                BuildSkill(spec);

            int drawn = 0, kept = 0;
            foreach (CrestSpec spec in Crests)
                BuildCrest(spec, ref drawn, ref kept);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[{nameof(DemoGuildBuilder)}] {Skills.Length} guild skill(s) and {Crests.Length} crest(s) " +
                      $"({drawn} drawn, {kept} kept). Run Wire Game Database to register them, " +
                      "and Build NPCs And Quests for the keeper's guild strongbox.");
        }

        private static void BuildSkill(SkillSpec spec)
        {
            var skill = Create<GuildSkill>($"{SkillDir}/{spec.Name}.asset");
            var serialized = new SerializedObject(skill);
            serialized.FindProperty("id").stringValue = spec.Name;
            serialized.FindProperty("defaultTitle").stringValue = spec.Title;
            serialized.FindProperty("defaultDescription").stringValue = spec.Description;
            serialized.FindProperty("maxLevel").intValue = spec.MaxLevel;
            serialized.FindProperty("skillType").enumValueIndex = (int)GuildSkillType.Passive;

            // `IncrementalInt`/`IncrementalFloat` are {baseAmount, amountIncreaseEachLevel}.
            // The base is what level 1 gives, so a skill that grants four members a rank
            // wants both set to four - leave the base at zero and the first point bought
            // does nothing, which reads as the skill being broken.
            WriteIncremental(serialized, "increaseMaxMember", spec.Members);
            WriteIncremental(serialized, "increaseExpGainPercentage", spec.Exp);
            WriteIncremental(serialized, "increaseGoldGainPercentage", spec.Gold);

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(skill);
        }

        /// <summary>
        /// Writes one incremental, base and per-level together.
        ///
        /// **The two halves of an `IncrementalInt` are not the same type.** `baseAmount` is
        /// an `int` and `amountIncreaseEachLevel` is a `float` - so a writer that decides
        /// once, on the base, and then writes `intValue` to both leaves the increment
        /// untouched: `SerializedProperty.intValue` on a float property is a silent no-op.
        /// Fellowship came out granting four members at rank one and four at rank five, and
        /// the only way to see it was to ask `GetIncreaseMaxMember(5)` what it would
        /// actually pay. Each field is written by its own type.
        /// </summary>
        private static void WriteIncremental(SerializedObject serialized, string field, float perLevel)
        {
            SerializedProperty property = serialized.FindProperty(field);
            if (property == null)
                return;
            SerializedProperty baseAmount = property.FindPropertyRelative("baseAmount");
            SerializedProperty each = property.FindPropertyRelative("amountIncreaseEachLevel");
            if (baseAmount == null || each == null)
                return;
            if (baseAmount.propertyType == SerializedPropertyType.Integer)
                baseAmount.intValue = Mathf.RoundToInt(perLevel);
            else
                baseAmount.floatValue = perLevel;
            each.floatValue = perLevel;
        }

        // ---- crests ----------------------------------------------------------

        private static void BuildCrest(CrestSpec spec, ref int drawn, ref int kept)
        {
            string png = $"{CrestDir}/{spec.Name}.png";
            Sprite sprite;
            if (System.IO.File.Exists(png))
            {
                sprite = AdoptCrest(png);
                ++kept;
            }
            else
            {
                sprite = DrawCrest(spec, png);
                ++drawn;
            }

            var icon = Create<GuildIcon>($"{IconDir}/{spec.Name}.asset");
            var serialized = new SerializedObject(icon);
            serialized.FindProperty("id").stringValue = spec.Name;
            serialized.FindProperty("defaultTitle").stringValue = spec.Title;
            serialized.FindProperty("icon").objectReferenceValue = sprite;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(icon);
        }

        /// <summary>
        /// A shield, a field colour and one white charge on it.
        ///
        /// Drawn at four samples a pixel rather than one, because a shield is mostly
        /// diagonal edges and a crest is displayed small: aliased, the taper to the point
        /// reads as a staircase at any size the UI actually shows it.
        /// </summary>
        private static Sprite DrawCrest(CrestSpec spec, string path)
        {
            var pixels = new Color32[CrestSize * CrestSize];
            Color rim = new Color(spec.Field.r * 0.45f, spec.Field.g * 0.45f, spec.Field.b * 0.45f);
            for (int y = 0; y < CrestSize; ++y)
            {
                for (int x = 0; x < CrestSize; ++x)
                {
                    float field = 0f, edge = 0f, charge = 0f;
                    for (int s = 0; s < 4; ++s)
                    {
                        float ox = (s % 2) * 0.5f + 0.25f;
                        float oy = (s / 2) * 0.5f + 0.25f;
                        // -1..1 across, +1 at the top of the shield. **Row 0 is the bottom
                        // row**, both in `SetPixels32` and in the PNG that comes out of
                        // `EncodeToPNG`, so v rises with y - written the other way round
                        // the whole set came out standing on its point.
                        float u = (x + ox) / CrestSize * 2f - 1f;
                        float v = (y + oy) / CrestSize * 2f - 1f;
                        if (!InShield(u, v, 0f))
                            continue;
                        field += 0.25f;
                        if (!InShield(u, v, -0.07f))
                            edge += 0.25f;
                        else if (OnCharge(spec.Charge, u, v))
                            charge += 0.25f;
                    }
                    if (field <= 0f)
                        continue;
                    Color colour = Color.Lerp(spec.Field, Color.white, charge * 0.92f);
                    colour = Color.Lerp(colour, rim, edge);
                    pixels[y * CrestSize + x] = new Color(colour.r, colour.g, colour.b, field);
                }
            }
            WritePng(path, CrestSize, pixels);
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        /// <summary>
        /// The shield outline: straight sides down to the waist, then a taper to a point.
        /// `inset` shrinks it, which is how the darker rim is found - the band between the
        /// full shape and a slightly smaller one.
        /// </summary>
        private static bool InShield(float u, float v, float inset)
        {
            float top = 0.86f + inset;
            float bottom = -0.92f - inset;
            if (v > top || v < bottom)
                return false;
            float half = 0.72f + inset;
            if (v < -0.05f)
            {
                float t = (v + 0.05f) / (bottom + 0.05f);
                half *= Mathf.Sqrt(Mathf.Max(0f, 1f - t * t));
            }
            return Mathf.Abs(u) <= half;
        }

        private static bool OnCharge(Charge charge, float u, float v)
        {
            switch (charge)
            {
                case Charge.Fess: return Mathf.Abs(v - 0.18f) < 0.15f;
                case Charge.Pale: return Mathf.Abs(u) < 0.17f;
                case Charge.Cross: return Mathf.Abs(u) < 0.15f || Mathf.Abs(v - 0.18f) < 0.15f;
                // Apex up: v falls as |u| rises. The sign the other way is a chevron
                // reversed, which is a real charge and not the one anyone pictures.
                case Charge.Chevron: return Mathf.Abs(v + Mathf.Abs(u) * 0.85f - 0.45f) < 0.15f;
                case Charge.Lozenge: return Mathf.Abs(u) * 1.35f + Mathf.Abs(v - 0.15f) < 0.46f;
                case Charge.Roundel: return Mathf.Sqrt(u * u + (v - 0.15f) * (v - 0.15f)) < 0.33f;
            }
            return false;
        }

        /// <summary>
        /// Takes a crest somebody has drawn as it is, correcting only its import settings.
        /// A PNG dropped into the project imports as a plain texture and loads as a **null**
        /// Sprite, which arrives later as a blank crest with nothing in the console.
        /// </summary>
        private static Sprite AdoptCrest(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null &&
                (importer.textureType != TextureImporterType.Sprite ||
                 importer.spriteImportMode != SpriteImportMode.Single ||
                 !importer.alphaIsTransparency))
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null)
                Debug.LogWarning($"[{nameof(DemoGuildBuilder)}] {path} is there but did not load as a " +
                                 "Sprite, so that crest is blank. Check its import settings.");
            return sprite;
        }

        private static void WritePng(string path, int size, Color32[] pixels)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply();
            System.IO.File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                return;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = false;
            importer.sRGBTexture = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        // ---- for the database ------------------------------------------------

        public static List<Object> AllSkills()
        {
            return All("t:GuildSkill", SkillDir);
        }

        public static List<Object> AllIcons()
        {
            return All("t:GuildIcon", IconDir);
        }

        private static List<Object> All(string filter, string folder)
        {
            var found = new List<Object>();
            foreach (string guid in AssetDatabase.FindAssets(filter, new[] { folder }))
                found.Add(AssetDatabase.LoadAssetAtPath<Object>(AssetDatabase.GUIDToAssetPath(guid)));
            return found;
        }

        private static T Create<T>(string path) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null)
                return existing;
            var created = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(created, path);
            return created;
        }
    }
}
