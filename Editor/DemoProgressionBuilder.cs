using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// The two things a level and a gathered material were missing: attributes to grow, and
    /// something to make.
    ///
    /// Both were holes rather than absent systems. The kit supports attributes and crafting,
    /// the HUD already carries the Attributes tab and the Craft window, and the demo had
    /// **zero** `Attribute` and `ItemCraftFormula` assets - so levelling raised hidden numbers
    /// and the Craft window opened empty. `DemoHudBuilder.HideUnresolvableAttributes` even hid
    /// the attribute rows on purpose, precisely because there were none to resolve.
    ///
    /// Written from the tables below on every run, like <see cref="DemoSkillBuilder"/> and
    /// unlike the icon and scene builders: this is generated *data*, not art or hand placement,
    /// so there is nothing here for a person to have tuned in the inspector that a rebuild
    /// would be destroying. Retune by editing the tables.
    /// </summary>
    public static class DemoProgressionBuilder
    {
        private const string AttributeDir = "Assets/OpenMMORPG/Demo/GameData/Resources/Attributes";
        private const string FormulaDir = "Assets/OpenMMORPG/Demo/GameData/Resources/ItemCraftFormulas";
        private const string CharacterDir = "Assets/OpenMMORPG/Demo/GameData/Resources/PlayerCharacters";
        private const string DatabasePath = "Assets/OpenMMORPG/Demo/GameData/GameDatabase.asset";
        private const string AttributeIconDir = "Assets/OpenMMORPG/Demo/Textures/Icons/Attributes";
        private const string ItemDir = "Assets/OpenMMORPG/Demo/GameData/Resources/Items";
        private const string ItemIconDir = "Assets/OpenMMORPG/Demo/Textures/Icons/Items";

        /// <summary>
        /// Materials that are **made** rather than gathered.
        ///
        /// `DemoHarvestBuilder` owns the ones that come out of the ground - Timber, Stone,
        /// Browncap - because it also owns the nodes they come out of. This owns the ones
        /// that only exist because somebody crafted them, which is what gives the recipe list
        /// a second tier: hides become leather, and leather becomes half the leatherwork in
        /// the village. Without a step like that, every recipe is raw-material-to-finished-good
        /// and the system has nothing to show beyond a shopping list.
        /// </summary>
        private struct CraftedMaterial
        {
            public string Name;
            public string Title;
            public string Description;
            public int Price;
            public float Weight;
        }

        private static readonly CraftedMaterial[] CraftedMaterials =
        {
            new CraftedMaterial
            {
                Name = "Leather", Title = "Leather", Price = 14, Weight = 0.6f,
                Description = "Scraped, stretched and cured. The making of half the village's kit.",
            },
        };

        // ------------------------------------------------------------------
        // Attributes
        // ------------------------------------------------------------------

        /// <summary>
        /// What one point of an attribute is worth.
        ///
        /// **Damage is left on the default element deliberately.** `DamageIncremental`'s own
        /// note says an empty `damageElement` falls back to the game instance's default, and
        /// the demo defines no `DamageElement` assets - so this grants melee-and-everything
        /// damage without inventing an element system that nothing else in the demo uses.
        ///
        /// The numbers are per point, and a class collects a few points a level (see
        /// <see cref="Growth"/>), so a level is worth roughly: a Warrior +30 hp and +4 damage,
        /// a Mage +16 mp. Chosen so the Attributes tab visibly moves on every level rather
        /// than to balance anything - the demo has no balance to protect.
        /// </summary>
        private struct AttributeSpec
        {
            public string Name;
            public string Title;
            public string Description;
            public float Hp, HpRecovery, Mp, MpRecovery, Stamina, StaminaRecovery;
            public float Accuracy, Evasion, CriRate, AtkSpeed, WeightLimit;
            public float DamageMin, DamageMax;
        }

        private static readonly AttributeSpec[] Attributes =
        {
            new AttributeSpec
            {
                Name = "Strength", Title = "Strength",
                Description = "How hard you hit, and how much you can carry.",
                DamageMin = 1.5f, DamageMax = 2.5f, WeightLimit = 6f, Hp = 3f,
            },
            new AttributeSpec
            {
                Name = "Dexterity", Title = "Dexterity",
                Description = "Landing the blow, and getting out of the way of one.",
                DamageMin = 0.6f, DamageMax = 1.0f, Accuracy = 2f, Evasion = 1.5f,
                CriRate = 0.003f, AtkSpeed = 0.01f,
            },
            new AttributeSpec
            {
                Name = "Intelligence", Title = "Intelligence",
                Description = "The size of the well a spell is drawn from, and how fast it refills.",
                Mp = 8f, MpRecovery = 0.4f,
            },
            new AttributeSpec
            {
                Name = "Vitality", Title = "Vitality",
                Description = "Staying upright.",
                Hp = 12f, HpRecovery = 0.5f, Stamina = 4f, StaminaRecovery = 0.3f,
            },
        };

        /// <summary>
        /// How many points of each attribute a class has at level 1, and gains per level.
        ///
        /// The kit grants these **automatically** through `PlayerCharacter.attributes`, which
        /// is an `AttributeIncremental[]` - there is no free-points-to-spend on level up in
        /// this kit outside quest rewards (`quest.rewardStatPoints`) and refunds from resetting.
        /// So the Attributes tab reads as a class's character sheet rather than as a shop, and
        /// the three classes are meant to look obviously different on it.
        /// </summary>
        private struct Growth
        {
            public string Character;
            public string Attribute;
            public float Base;
            public float PerLevel;
        }

        private static readonly Growth[] Growths =
        {
            new Growth { Character = "Warrior", Attribute = "Strength",     Base = 5f, PerLevel = 2.0f },
            new Growth { Character = "Warrior", Attribute = "Vitality",     Base = 4f, PerLevel = 1.5f },
            new Growth { Character = "Warrior", Attribute = "Dexterity",    Base = 2f, PerLevel = 0.5f },
            new Growth { Character = "Warrior", Attribute = "Intelligence", Base = 1f, PerLevel = 0.2f },

            new Growth { Character = "Ranger",  Attribute = "Dexterity",    Base = 5f, PerLevel = 2.0f },
            new Growth { Character = "Ranger",  Attribute = "Strength",     Base = 3f, PerLevel = 1.0f },
            new Growth { Character = "Ranger",  Attribute = "Vitality",     Base = 3f, PerLevel = 1.0f },
            new Growth { Character = "Ranger",  Attribute = "Intelligence", Base = 1f, PerLevel = 0.3f },

            new Growth { Character = "Mage",    Attribute = "Intelligence", Base = 5f, PerLevel = 2.0f },
            new Growth { Character = "Mage",    Attribute = "Vitality",     Base = 2f, PerLevel = 0.8f },
            new Growth { Character = "Mage",    Attribute = "Dexterity",    Base = 2f, PerLevel = 0.6f },
            new Growth { Character = "Mage",    Attribute = "Strength",     Base = 1f, PerLevel = 0.3f },
        };

        // ------------------------------------------------------------------
        // Crafting
        // ------------------------------------------------------------------

        /// <summary>
        /// What can be made, and out of what.
        ///
        /// **Every ingredient is something the demo already drops.** The island's harvestables
        /// give Timber, Stone and Browncap; deer give Venison and DeerHide; wolves give WolfPelt
        /// and WolfFang. Before this they were loot with no use but the merchant, which is a
        /// gathering loop with no other end - the recipes are what close it.
        ///
        /// **`Anywhere` decides whether a recipe needs a station**, and this builder is the
        /// only thing that writes it. `DemoCraftStationBuilder` places the stations and binds
        /// recipes to them, but does not touch this flag - it used to, which meant the answer
        /// depended on which of the two ran last.
        ///
        /// Most of them need a place: a bow needs a vice, a blade needs an anvil, a stew needs
        /// a fire. `RangerBracers` is the exception on purpose, so the demo shows both halves
        /// of the system - cutting and tying hide strips is something a hunter does sitting on
        /// a rock, and it is the one recipe here that would look silly requiring a workshop.
        /// </summary>
        private struct Recipe
        {
            public string Name;
            public string Product;
            public int Amount;
            public int Gold;
            public string[] Materials;   // item name, count, item name, count, ...
            public int[] Counts;
            public bool Anywhere;
        }

        private static readonly Recipe[] Recipes =
        {
            // ---- made in the field ------------------------------------------
            //
            // Two of them, so the demo shows both halves of the system: these appear in the
            // HUD's own Craft window wherever the player is standing, and everything else
            // below needs the right station in the village.
            new Recipe
            {
                Name = "CraftLeather", Product = "Leather", Amount = 1, Gold = 0,
                Materials = new[] { "DeerHide", "Stone" }, Counts = new[] { 2, 1 },
                Anywhere = true,
            },
            new Recipe
            {
                Name = "CraftRangerBracers", Product = "RangerBracers", Amount = 1, Gold = 30,
                Materials = new[] { "Leather", "WolfPelt" }, Counts = new[] { 2, 1 },
                Anywhere = true,
            },

            // ---- the cookfire -----------------------------------------------
            new Recipe
            {
                Name = "CraftStew", Product = "Stew", Amount = 1, Gold = 5,
                Materials = new[] { "Venison", "Browncap" }, Counts = new[] { 2, 1 },
            },

            // ---- the alchemist's table --------------------------------------
            new Recipe
            {
                Name = "CraftHealingPotion", Product = "MinorHealingPotion", Amount = 1, Gold = 10,
                Materials = new[] { "Browncap" }, Counts = new[] { 3 },
            },
            new Recipe
            {
                // Bought ale, spiced at the table - the one recipe whose first ingredient
                // comes off a merchant rather than out of the ground.
                Name = "CraftSpicedWine", Product = "SpicedWine", Amount = 1, Gold = 8,
                Materials = new[] { "Ale", "Browncap" }, Counts = new[] { 1, 2 },
            },

            // Twenty arrows from one length of ash. The bow needs them to fire at all, so
            // this is the third way to get them after the pedlar's board and the hundred a
            // ranger starts with - and the one that works out on the headland with no coin.
            new Recipe
            {
                Name = "CraftArrows", Product = "Arrow", Amount = 20, Gold = 0,
                Materials = new[] { "Timber" }, Counts = new[] { 1 },
            },

            // Timber, stone for the ring and a hide to carry it in. The one recipe whose
            // product is a **building**: it makes the campfire kit the player sets down
            // (see DemoBuildingBuilder), so the gathering loop now ends in something
            // placed in the world rather than only in something worn or drunk.
            new Recipe
            {
                Name = "CraftCampfireKit", Product = "CampfireKit", Amount = 1, Gold = 15,
                Materials = new[] { "Timber", "Stone", "Leather" }, Counts = new[] { 4, 2, 1 },
            },

            // ---- the forge ---------------------------------------------------
            new Recipe
            {
                Name = "CraftRoundShield", Product = "PaintedRoundShield", Amount = 1, Gold = 40,
                Materials = new[] { "Timber", "Stone" }, Counts = new[] { 4, 2 },
            },
            new Recipe
            {
                Name = "CraftShortsword", Product = "IronShortsword", Amount = 1, Gold = 60,
                Materials = new[] { "Stone", "Timber", "WolfFang" }, Counts = new[] { 3, 2, 1 },
            },
            new Recipe
            {
                Name = "CraftLongsword", Product = "IronLongsword", Amount = 1, Gold = 110,
                Materials = new[] { "Stone", "Timber", "Leather" }, Counts = new[] { 5, 2, 1 },
            },
            new Recipe
            {
                Name = "CraftKnightHelm", Product = "KnightHelm", Amount = 1, Gold = 90,
                Materials = new[] { "Stone", "Leather" }, Counts = new[] { 6, 2 },
            },

            // ---- the fletcher's bench ----------------------------------------
            new Recipe
            {
                Name = "CraftHuntingBow", Product = "HuntingBow", Amount = 1, Gold = 50,
                Materials = new[] { "Timber", "Leather" }, Counts = new[] { 3, 2 },
            },
            new Recipe
            {
                Name = "CraftYewLongbow", Product = "YewLongbow", Amount = 1, Gold = 130,
                Materials = new[] { "Timber", "Leather", "WolfFang" }, Counts = new[] { 5, 3, 1 },
            },
            new Recipe
            {
                Name = "CraftRangerBoots", Product = "RangerBoots", Amount = 1, Gold = 35,
                Materials = new[] { "Leather" }, Counts = new[] { 3 },
            },
            new Recipe
            {
                Name = "CraftRangerHood", Product = "RangerHood", Amount = 1, Gold = 35,
                Materials = new[] { "Leather", "WolfPelt" }, Counts = new[] { 2, 1 },
            },
        };

        // ------------------------------------------------------------------

        [MenuItem("Open MMORPG/Demo/Build Progression (attributes and crafting)", priority = 150)]
        public static void Build()
        {
            DemoItemBuilder.EnsureFolder(AttributeDir);
            DemoItemBuilder.EnsureFolder(FormulaDir);
            List<BaseItem> materials = WriteCraftedMaterials();

            var attributes = new Dictionary<string, MultiplayerARPG.Attribute>();
            int icons = 0;
            foreach (AttributeSpec spec in Attributes)
            {
                attributes[spec.Name] = WriteAttribute(spec);
                if (new SerializedObject(attributes[spec.Name]).FindProperty("icon").objectReferenceValue != null)
                    ++icons;
            }

            int grown = ApplyGrowth(attributes);
            var formulas = new List<ItemCraftFormula>();
            int skipped = 0;
            foreach (Recipe recipe in Recipes)
            {
                ItemCraftFormula formula = WriteRecipe(recipe);
                if (formula == null)
                    ++skipped;
                else
                    formulas.Add(formula);
            }

            Register(attributes, formulas, materials);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[{nameof(DemoProgressionBuilder)}] {attributes.Count} attributes written " +
                      $"({icons} with icons) and " +
                      $"applied to {grown} class growth rows; {formulas.Count} craft formulas written" +
                      $"{(skipped > 0 ? $" ({skipped} skipped - missing items)" : "")}. " +
                      "Both registered in the GameDatabase. Run Build HUD afterwards so the " +
                      "attribute rows stop being hidden.");
        }

        /// <summary>
        /// Writes the crafted materials, which are plain `JunkItem`s - the same shape as the
        /// gathered ones, so they stack, sell and sit in the pack identically.
        /// </summary>
        private static List<BaseItem> WriteCraftedMaterials()
        {
            var written = new List<BaseItem>();
            foreach (CraftedMaterial spec in CraftedMaterials)
            {
                string path = $"{ItemDir}/{spec.Name}.asset";
                var item = AssetDatabase.LoadAssetAtPath<JunkItem>(path);
                if (item == null)
                {
                    item = ScriptableObject.CreateInstance<JunkItem>();
                    AssetDatabase.CreateAsset(item, path);
                }

                var serialized = new SerializedObject(item);
                serialized.FindProperty("id").stringValue = spec.Name;
                serialized.FindProperty("defaultTitle").stringValue = spec.Title;
                serialized.FindProperty("defaultDescription").stringValue = spec.Description;
                serialized.FindProperty("sellPrice").intValue = spec.Price;
                serialized.FindProperty("weight").floatValue = spec.Weight;
                // Materials come in by the armful - see DemoHarvestBuilder for the same note.
                serialized.FindProperty("maxStack").intValue = 999;

                Sprite icon = AdoptIcon(ItemIconDir, spec.Name);
                if (icon != null)
                    serialized.FindProperty("icon").objectReferenceValue = icon;

                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(item);
                written.Add(item);
            }
            return written;
        }

        private static MultiplayerARPG.Attribute WriteAttribute(AttributeSpec spec)
        {
            string path = $"{AttributeDir}/{spec.Name}.asset";
            var asset = AssetDatabase.LoadAssetAtPath<MultiplayerARPG.Attribute>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<MultiplayerARPG.Attribute>();
                AssetDatabase.CreateAsset(asset, path);
            }

            var serialized = new SerializedObject(asset);
            serialized.FindProperty("defaultTitle").stringValue = spec.Title;
            serialized.FindProperty("defaultDescription").stringValue = spec.Description;
            // Zero means "no ceiling" here; the demo never reaches one anyway, and a wrong
            // ceiling silently stops a class growing halfway up the level range.
            serialized.FindProperty("maxAmount").intValue = 0;

            SerializedProperty stats = serialized.FindProperty("statsIncreaseEachLevel");
            Stat(stats, "hp", spec.Hp);
            Stat(stats, "hpRecovery", spec.HpRecovery);
            Stat(stats, "mp", spec.Mp);
            Stat(stats, "mpRecovery", spec.MpRecovery);
            Stat(stats, "stamina", spec.Stamina);
            Stat(stats, "staminaRecovery", spec.StaminaRecovery);
            Stat(stats, "accuracy", spec.Accuracy);
            Stat(stats, "evasion", spec.Evasion);
            Stat(stats, "criRate", spec.CriRate);
            Stat(stats, "atkSpeed", spec.AtkSpeed);
            Stat(stats, "weightLimit", spec.WeightLimit);

            SerializedProperty damages = serialized.FindProperty("increaseDamages");
            if (spec.DamageMax > 0f)
            {
                damages.arraySize = 1;
                SerializedProperty entry = damages.GetArrayElementAtIndex(0);
                // Left null on purpose - see the note on AttributeSpec.
                entry.FindPropertyRelative("damageElement").objectReferenceValue = null;
                SerializedProperty amount = entry.FindPropertyRelative("amount.baseAmount");
                amount.FindPropertyRelative("min").floatValue = spec.DamageMin;
                amount.FindPropertyRelative("max").floatValue = spec.DamageMax;
            }
            else
            {
                damages.arraySize = 0;
            }

            Sprite icon = AdoptIcon(AttributeIconDir, spec.Name);
            if (icon != null)
                serialized.FindProperty("icon").objectReferenceValue = icon;

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            return asset;
        }

        /// <summary>
        /// Takes the hand-drawn icon for an attribute, if someone has drawn one.
        ///
        /// **Adopted, never drawn.** This builder writes data from its tables on every run,
        /// which is safe for numbers and wrong for art - so the icon is the one field here
        /// that comes from the project rather than from the table, and a missing file leaves
        /// whatever is already assigned alone rather than clearing it. Same rule as
        /// `DemoSkillBuilder.AdoptIcon`.
        ///
        /// **The import settings are fixed on the way past**, because that is the trap this
        /// has already been caught by once: a PNG dropped into the project imports as a plain
        /// texture, `LoadAssetAtPath&lt;Sprite&gt;` then returns **null**, and the result is a
        /// blank square in the UI with no error anywhere to say why.
        /// </summary>
        private static Sprite AdoptIcon(string folder, string name)
        {
            string path = $"{folder}/{name}.png";
            if (!System.IO.File.Exists(path))
                return null;

            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null &&
                (importer.textureType != TextureImporterType.Sprite ||
                 importer.spriteImportMode != SpriteImportMode.Single ||
                 !importer.alphaIsTransparency))
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.SaveAndReimport();
                Debug.Log($"[{nameof(DemoProgressionBuilder)}] Reimported \"{name}.png\" as a sprite; " +
                          "it had come in as a plain texture, which loads as null.");
            }

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null)
                Debug.LogWarning($"[{nameof(DemoProgressionBuilder)}] \"{path}\" exists but will not load " +
                                 "as a sprite.");
            return sprite;
        }

        private static void Stat(SerializedProperty stats, string field, float value)
        {
            SerializedProperty property = stats.FindPropertyRelative(field);
            if (property == null)
            {
                Debug.LogWarning($"[{nameof(DemoProgressionBuilder)}] CharacterStats has no \"{field}\".");
                return;
            }
            property.floatValue = value;
        }

        /// <summary>
        /// Writes each class's attribute growth. The whole array is rebuilt rather than appended
        /// to, so removing a row from <see cref="Growths"/> actually removes it.
        /// </summary>
        private static int ApplyGrowth(Dictionary<string, MultiplayerARPG.Attribute> attributes)
        {
            var byCharacter = new Dictionary<string, List<Growth>>();
            foreach (Growth growth in Growths)
            {
                if (!byCharacter.ContainsKey(growth.Character))
                    byCharacter[growth.Character] = new List<Growth>();
                byCharacter[growth.Character].Add(growth);
            }

            int rows = 0;
            foreach (KeyValuePair<string, List<Growth>> pair in byCharacter)
            {
                string path = $"{CharacterDir}/{pair.Key}.asset";
                var character = AssetDatabase.LoadAssetAtPath<PlayerCharacter>(path);
                if (character == null)
                {
                    Debug.LogWarning($"[{nameof(DemoProgressionBuilder)}] No player character at {path}.");
                    continue;
                }

                var serialized = new SerializedObject(character);
                SerializedProperty array = serialized.FindProperty("attributes");
                array.arraySize = pair.Value.Count;
                for (int i = 0; i < pair.Value.Count; ++i)
                {
                    Growth growth = pair.Value[i];
                    SerializedProperty entry = array.GetArrayElementAtIndex(i);
                    entry.FindPropertyRelative("attribute").objectReferenceValue =
                        attributes.TryGetValue(growth.Attribute, out MultiplayerARPG.Attribute a) ? a : null;
                    entry.FindPropertyRelative("amount.baseAmount").floatValue = growth.Base;
                    entry.FindPropertyRelative("amount.amountIncreaseEachLevel").floatValue = growth.PerLevel;
                    ++rows;
                }
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(character);
            }
            return rows;
        }

        private static ItemCraftFormula WriteRecipe(Recipe recipe)
        {
            BaseItem product = FindItem(recipe.Product);
            if (product == null)
            {
                Debug.LogWarning($"[{nameof(DemoProgressionBuilder)}] No item named \"{recipe.Product}\"; " +
                                 $"skipping the recipe for it.");
                return null;
            }

            string path = $"{FormulaDir}/{recipe.Name}.asset";
            var asset = AssetDatabase.LoadAssetAtPath<ItemCraftFormula>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<ItemCraftFormula>();
                AssetDatabase.CreateAsset(asset, path);
            }

            var serialized = new SerializedObject(asset);
            serialized.FindProperty("defaultTitle").stringValue = product.Title;
            // Source 0 is "no source needed" - see GameInstance.AddItemCraftFormulas - which is
            // what makes a recipe appear in the HUD's own Craft window rather than only at a
            // station. Written from the table either way, so turning a recipe into a station
            // one is a single edit here rather than a flag to go and clear somewhere else.
            serialized.FindProperty("canBeCraftedWithoutSource").boolValue = recipe.Anywhere;
            serialized.FindProperty("craftDuration").floatValue = 0f;

            SerializedProperty craft = serialized.FindProperty("itemCraft");
            craft.FindPropertyRelative("craftingItem").objectReferenceValue = product;
            craft.FindPropertyRelative("amount").intValue = recipe.Amount;
            craft.FindPropertyRelative("requireGold").intValue = recipe.Gold;

            SerializedProperty requires = craft.FindPropertyRelative("requireItems");
            var wanted = new List<KeyValuePair<BaseItem, int>>();
            for (int i = 0; i < recipe.Materials.Length; ++i)
            {
                BaseItem material = FindItem(recipe.Materials[i]);
                if (material == null)
                {
                    Debug.LogWarning($"[{nameof(DemoProgressionBuilder)}] \"{recipe.Name}\" wants " +
                                     $"\"{recipe.Materials[i]}\", which does not exist.");
                    continue;
                }
                wanted.Add(new KeyValuePair<BaseItem, int>(material, recipe.Counts[i]));
            }
            requires.arraySize = wanted.Count;
            for (int i = 0; i < wanted.Count; ++i)
            {
                SerializedProperty entry = requires.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("item").objectReferenceValue = wanted[i].Key;
                entry.FindPropertyRelative("amount").intValue = wanted[i].Value;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            return asset;
        }

        private static BaseItem FindItem(string name)
        {
            foreach (string guid in AssetDatabase.FindAssets($"t:BaseItem {name}", new[] { "Assets/OpenMMORPG/Demo" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(path) != name)
                    continue;
                return AssetDatabase.LoadAssetAtPath<BaseItem>(path);
            }
            return null;
        }

        /// <summary>
        /// Puts both sets into the database the demo actually loads.
        ///
        /// Being under a `Resources` folder is not enough on its own: the demo uses an explicit
        /// <see cref="GameDatabase"/> asset with a list per data type, and anything missing from
        /// those lists does not exist as far as the game is concerned however correctly it is
        /// authored.
        /// </summary>
        private static void Register(Dictionary<string, MultiplayerARPG.Attribute> attributes,
                                     List<ItemCraftFormula> formulas,
                                     List<BaseItem> materials)
        {
            var database = AssetDatabase.LoadAssetAtPath<GameDatabase>(DatabasePath);
            if (database == null)
            {
                Debug.LogError($"[{nameof(DemoProgressionBuilder)}] No GameDatabase at {DatabasePath}; " +
                               "nothing will be loaded at runtime.");
                return;
            }

            var serialized = new SerializedObject(database);
            SerializedProperty attributeList = serialized.FindProperty("attributes");
            attributeList.arraySize = attributes.Count;
            int index = 0;
            foreach (AttributeSpec spec in Attributes)
            {
                if (!attributes.TryGetValue(spec.Name, out MultiplayerARPG.Attribute asset))
                    continue;
                attributeList.GetArrayElementAtIndex(index++).objectReferenceValue = asset;
            }
            attributeList.arraySize = index;

            SerializedProperty formulaList = serialized.FindProperty("itemCraftFormulas");
            formulaList.arraySize = formulas.Count;
            for (int i = 0; i < formulas.Count; ++i)
                formulaList.GetArrayElementAtIndex(i).objectReferenceValue = formulas[i];

            // Appended, never rewritten: the items list is 46 assets long and belongs to the
            // item and harvest builders. Anything already there is left exactly where it is.
            SerializedProperty items = serialized.FindProperty("items");
            foreach (BaseItem material in materials)
            {
                bool present = false;
                for (int i = 0; i < items.arraySize && !present; ++i)
                    present = items.GetArrayElementAtIndex(i).objectReferenceValue == material;
                if (present)
                    continue;
                items.arraySize++;
                items.GetArrayElementAtIndex(items.arraySize - 1).objectReferenceValue = material;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(database);
        }
    }
}
