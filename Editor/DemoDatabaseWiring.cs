using UnityEditor;
using UnityEngine;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// Points the demo's game data at the rebuilt island and characters.
    ///
    /// The entity prefabs already carry their own character data — that link is
    /// inherited from the prefabs they were cloned from — so what is left is
    /// registering the new entities in the database and moving the spawn point onto
    /// the island, which the old data still placed out at (-50, 10, -200).
    /// </summary>
    public static class DemoDatabaseWiring
    {
        private const string GameDataDir = "Assets/OpenMMORPG/Demo/GameData";
        private const string EntityDir = "Assets/OpenMMORPG/Demo/Prefabs/GamePlay/CharacterEntities";

        [MenuItem("Open MMORPG/Demo/Wire Game Database")]
        public static void Wire()
        {
            var database = AssetDatabase.LoadAssetAtPath<ScriptableObject>($"{GameDataDir}/GameDatabase.asset");
            var map = AssetDatabase.LoadAssetAtPath<ScriptableObject>($"{GameDataDir}/Resources/MapInfos/BaseMap.asset");
            if (database == null || map == null)
            {
                Debug.LogError($"[{nameof(DemoDatabaseWiring)}] Demo game data is missing under {GameDataDir}.");
                return;
            }

            WriteMap(map);
            WriteClasses(map);
            WriteMonsters();
            WriteDatabase(database);

            AssetDatabase.SaveAssets();
            Debug.Log($"[{nameof(DemoDatabaseWiring)}] Wired the demo database.");
        }

        /// <summary>
        /// Where players arrive, relative to the middle of the green, and which way they
        /// face when they do. Kept clear of the firepit — see <see cref="WriteMap"/>.
        /// `DemoSceneBuilder` checks the result against the built scene and complains if
        /// anything has moved into it.
        /// </summary>
        private static readonly Vector2 ArrivalOffset = new Vector2(-5f, 0f);
        private const float ArrivalYaw = 270f;

        private static void WriteMap(ScriptableObject map)
        {
            var serialized = new SerializedObject(map);
            // The map's id is the key the whole game looks it up by, and the name a saved
            // character carries around as the place it logged out.
            serialized.FindProperty("id").stringValue = "VerdantIsle";
            serialized.FindProperty("defaultTitle").stringValue = "Verdant Isle";
            // Players arrive on the village green, a few paces off the middle of it and
            // turned to look back across it.
            //
            // Not *on* the middle: that is where the scene builder stands the firepit, and
            // the two had independently picked the same spot. A character that materialises
            // inside a mesh collider is thrown out of it by the physics, which in game reads
            // as dying the instant you arrive — and then again on every attempt to get up,
            // because the respawn point is the arrival point. It looks for all the world
            // like a broken respawn, and the respawn code is fine.
            Vector2 arrival = DemoIslandBuilder.VillageCentre + ArrivalOffset;
            serialized.FindProperty("startPosition").vector3Value =
                new Vector3(arrival.x, DemoIslandBuilder.HeightAt(arrival.x, arrival.y) + 0.5f, arrival.y);
            serialized.FindProperty("startRotation").vector3Value = new Vector3(0f, ArrivalYaw, 0f);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(map);
        }

        private struct ClassSpec
        {
            public string Name;
            public string Title;
            public string Description;
            public string Weapon;
            public float Hp;
            public float HpPerLevel;
            public float Mp;
            public float MpPerLevel;
            public float MoveSpeed;
            public float AtkSpeed;
        }

        /// <summary>
        /// The three things a new character can be.
        ///
        /// They are told apart by what they fight with rather than by skills, because the
        /// demo has no skills yet: the warrior swings a sword, the ranger looses arrows and
        /// the mage throws bolts, and those are three different weapon types with three
        /// different ranges. The health and mana pools lean the same way, so the mage cannot
        /// simply stand in the open and out-trade a warrior.
        ///
        /// All three start in the same peasant clothes. The starting kit is deliberately
        /// poor so that the armour taken off an enemy is worth putting on.
        /// </summary>
        private static readonly ClassSpec[] Classes =
        {
            new ClassSpec { Name = "Warrior", Title = "Warrior",
                Description = "Fights up close and can afford to be hit while doing it.",
                Weapon = "IronShortsword",
                Hp = 150f, HpPerLevel = 30f, Mp = 30f, MpPerLevel = 5f, MoveSpeed = 4.0f, AtkSpeed = 1f },
            new ClassSpec { Name = "Ranger", Title = "Ranger",
                Description = "Keeps her distance and makes the ground between you count.",
                Weapon = "HuntingBow",
                Hp = 115f, HpPerLevel = 22f, Mp = 55f, MpPerLevel = 9f, MoveSpeed = 4.5f, AtkSpeed = 1.15f },
            new ClassSpec { Name = "Mage", Title = "Mage",
                Description = "Hits hardest at range and worst of the three in reach of a blade.",
                Weapon = "ApprenticeStaff",
                Hp = 95f, HpPerLevel = 18f, Mp = 120f, MpPerLevel = 24f, MoveSpeed = 4.0f, AtkSpeed = 0.9f },
        };

        /// <summary>Peasant clothes, worn by every class at level one.</summary>
        private static readonly string[] StartingClothes =
        {
            "PeasantTunic", "PeasantSleeves", "PeasantTrousers", "PeasantShoes",
        };

        private static void WriteClasses(ScriptableObject map)
        {
            DemoItemBuilder.EnsureFolder($"{GameDataDir}/Resources/PlayerCharacters");
            foreach (ClassSpec spec in Classes)
            {
                string path = $"{GameDataDir}/Resources/PlayerCharacters/{spec.Name}.asset";
                var asset = AssetDatabase.LoadAssetAtPath<PlayerCharacter>(path);
                if (asset == null)
                {
                    asset = ScriptableObject.CreateInstance<PlayerCharacter>();
                    AssetDatabase.CreateAsset(asset, path);
                }
                var serialized = new SerializedObject(asset);
                // The id is hashed into the data id every saved character carries. Two
                // classes sharing one, or leaving one empty, and the game cannot tell which
                // of them a character was.
                serialized.FindProperty("id").stringValue = spec.Name;
                serialized.FindProperty("defaultTitle").stringValue = spec.Title;
                SerializedProperty description = serialized.FindProperty("defaultDescription");
                if (description != null)
                    description.stringValue = spec.Description;

                Stat(serialized, "stats.baseStats.hp", spec.Hp);
                Stat(serialized, "stats.statsIncreaseEachLevel.hp", spec.HpPerLevel);
                Stat(serialized, "stats.baseStats.hpRecovery", 2f);
                Stat(serialized, "stats.statsIncreaseEachLevel.hpRecovery", 0.4f);
                Stat(serialized, "stats.baseStats.mp", spec.Mp);
                Stat(serialized, "stats.statsIncreaseEachLevel.mp", spec.MpPerLevel);
                Stat(serialized, "stats.baseStats.mpRecovery", 2f);
                Stat(serialized, "stats.baseStats.stamina", 100f);
                Stat(serialized, "stats.statsIncreaseEachLevel.stamina", 6f);
                Stat(serialized, "stats.baseStats.staminaRecovery", 12f);
                Stat(serialized, "stats.baseStats.moveSpeed", spec.MoveSpeed);
                Stat(serialized, "stats.baseStats.atkSpeed", spec.AtkSpeed);
                Stat(serialized, "stats.baseStats.weightLimit", 140f);
                Stat(serialized, "stats.statsIncreaseEachLevel.weightLimit", 6f);
                Stat(serialized, "stats.baseStats.criRate", 0.05f);

                serialized.FindProperty("startMap").objectReferenceValue = map;
                serialized.FindProperty("rightHandEquipItem").objectReferenceValue = Item(spec.Weapon);

                var clothes = new Object[StartingClothes.Length];
                for (int i = 0; i < StartingClothes.Length; ++i)
                    clothes[i] = Item(StartingClothes[i]);
                SetList(serialized, "armorItems", clothes);

                SerializedProperty startItems = serialized.FindProperty("startItems");
                startItems.arraySize = 1;
                SerializedProperty first = startItems.GetArrayElementAtIndex(0);
                first.FindPropertyRelative("item").objectReferenceValue = Item("MinorHealingPotion");
                first.FindPropertyRelative("amount").intValue = 5;

                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(asset);
            }
        }

        private struct MonsterSpec
        {
            public string Name;
            public string Title;
            public string Weapon;
            public float Hp;
            public float HpPerLevel;
            public float MoveSpeed;
            public float AtkSpeed;
            public float VisualRange;
            public string[] Loot;
        }

        /// <summary>
        /// The three things on the island that fight back, and what comes off each.
        ///
        /// Each family wears one of the outfits and drops the pieces of it, so what a player
        /// takes off a body is what they watched it wearing — and because the three outfits
        /// are the three classes' armour, killing the right enemy is how a character of that
        /// class gears up. A warrior wants the marauders in plate, a ranger wants the
        /// bandits, a mage wants the cultists.
        /// </summary>
        private static readonly MonsterSpec[] Monsters =
        {
            new MonsterSpec { Name = "BaseEnemy", Title = "Bandit", Weapon = "BanditAxe",
                Hp = 55f, HpPerLevel = 18f, MoveSpeed = 3.6f, AtkSpeed = 0.85f, VisualRange = 14f,
                Loot = new[] { "RangerBoots", "RangerBracers", "RangerPauldron", "RangerHood", "RangerBreeches", "RangerJerkin", "HuntingBow" } },
            new MonsterSpec { Name = "Marauder", Title = "Marauder", Weapon = "IronLongsword",
                Hp = 95f, HpPerLevel = 26f, MoveSpeed = 3.2f, AtkSpeed = 0.7f, VisualRange = 15f,
                Loot = new[] { "KnightSabatons", "KnightGauntlets", "KnightPauldrons", "KnightHelm", "KnightGreaves", "KnightCuirass", "IronLongsword" } },
            new MonsterSpec { Name = "Cultist", Title = "Cultist", Weapon = "ApprenticeStaff",
                Hp = 45f, HpPerLevel = 14f, MoveSpeed = 3.4f, AtkSpeed = 1.0f, VisualRange = 16f,
                Loot = new[] { "WizardShoes", "WizardSleeves", "WizardTrousers", "WizardRobe", "ElderStaff" } },
        };

        private static void WriteMonsters()
        {
            DemoItemBuilder.EnsureFolder($"{GameDataDir}/Resources/MonsterCharacters");
            foreach (MonsterSpec spec in Monsters)
            {
                string path = $"{GameDataDir}/Resources/MonsterCharacters/{spec.Name}.asset";
                var asset = AssetDatabase.LoadAssetAtPath<MonsterCharacter>(path);
                if (asset == null)
                {
                    asset = ScriptableObject.CreateInstance<MonsterCharacter>();
                    AssetDatabase.CreateAsset(asset, path);
                }
                var serialized = new SerializedObject(asset);
                serialized.FindProperty("id").stringValue = spec.Title;
                serialized.FindProperty("defaultTitle").stringValue = spec.Title;
                Stat(serialized, "stats.baseStats.hp", spec.Hp);
                Stat(serialized, "stats.statsIncreaseEachLevel.hp", spec.HpPerLevel);
                Stat(serialized, "stats.baseStats.moveSpeed", spec.MoveSpeed);
                Stat(serialized, "stats.baseStats.atkSpeed", spec.AtkSpeed);
                Stat(serialized, "stats.baseStats.hpRecovery", 1f);
                Stat(serialized, "visualRange", spec.VisualRange);
                SerializedProperty weapon = serialized.FindProperty("rightHandEquipItem");
                if (weapon != null)
                    weapon.objectReferenceValue = Item(spec.Weapon);
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(asset);

                WriteDrops(asset, spec.Loot);
            }
        }

        /// <summary>Sets one stat, and says so if the kit has renamed the field.</summary>
        private static void Stat(SerializedObject on, string path, float value)
        {
            SerializedProperty property = on.FindProperty(path);
            if (property == null)
            {
                Debug.LogWarning($"[{nameof(DemoDatabaseWiring)}] No stat \"{path}\" on {on.targetObject.name}.");
                return;
            }
            if (property.propertyType == SerializedPropertyType.Integer)
                property.intValue = Mathf.RoundToInt(value);
            else
                property.floatValue = value;
        }

        /// <summary>
        /// What an enemy leaves behind: its own gear.
        ///
        /// Each family wears one of the three outfits and drops the pieces of it, so the
        /// armour a player picks up is the armour they just fought — and since those three
        /// outfits are the three classes' armour, killing the right enemy is how a character
        /// gears into its class. The list is ordered cheapest first and the rates fall along
        /// it, which makes a full set something to work towards rather than something the
        /// first kill hands over. The insignia is the common drop that gives the merchant a
        /// reason to exist, and a potion often enough that the next fight is not gated on
        /// walking home.
        /// </summary>
        private static void WriteDrops(ScriptableObject monsterData, string[] loot)
        {
            var serialized = new SerializedObject(monsterData);
            SerializedProperty list = serialized.FindProperty("itemDropManager.randomItems");
            if (list == null)
            {
                Debug.LogError($"[{nameof(DemoDatabaseWiring)}] No drop list on {monsterData.name}.");
                return;
            }

            list.arraySize = 2 + loot.Length;
            WriteDrop(list.GetArrayElementAtIndex(0), "BanditInsignia", 0.55f, 2);
            WriteDrop(list.GetArrayElementAtIndex(1), "MinorHealingPotion", 0.30f, 2);
            for (int i = 0; i < loot.Length; ++i)
                WriteDrop(list.GetArrayElementAtIndex(2 + i), loot[i], Mathf.Max(0.025f, 0.10f - i * 0.012f), 1);

            // At most three of those at once, so a kill is a handful rather than a haul.
            serialized.FindProperty("itemDropManager.minDropItems").intValue = 1;
            serialized.FindProperty("itemDropManager.maxDropItems").intValue = 3;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(monsterData);
        }

        private static void WriteDrop(SerializedProperty entry, string item, float rate, int most)
        {
            entry.FindPropertyRelative("item").objectReferenceValue = Item(item);
            entry.FindPropertyRelative("minAmount").intValue = 1;
            entry.FindPropertyRelative("maxAmount").intValue = most;
            entry.FindPropertyRelative("minLevel").intValue = 0;
            entry.FindPropertyRelative("maxLevel").intValue = 1;
            entry.FindPropertyRelative("dropRate").floatValue = rate;
        }

        private static Object Item(string name)
        {
            var item = AssetDatabase.LoadAssetAtPath<Object>($"{GameDataDir}/Resources/Items/{name}.asset");
            if (item == null)
                Debug.LogError($"[{nameof(DemoDatabaseWiring)}] Missing item \"{name}\". Run Build Items first.");
            return item;
        }

        /// <summary>
        /// Gives the demo a clock, and points the game at it.
        ///
        /// Without one the kit makes an unconfigured updater at runtime, which runs a day
        /// in fifteen minutes — too slow to notice in a demo somebody looks at for five.
        /// Eight minutes is short enough that a player who stops to fight the bandits sees
        /// the light change while they do it.
        /// </summary>
        private static void WriteClock()
        {
            const string path = "Assets/OpenMMORPG/Demo/GameData/DayNightClock.asset";
            var clock = AssetDatabase.LoadAssetAtPath<DefaultDayNightTimeUpdater>(path);
            if (clock == null)
            {
                clock = ScriptableObject.CreateInstance<DefaultDayNightTimeUpdater>();
                AssetDatabase.CreateAsset(clock, path);
            }
            var settings = new SerializedObject(clock);
            settings.FindProperty("secondsToOneDay").floatValue = 8f * 60f;
            settings.FindProperty("startHourOfDay").intValue = 9;
            settings.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(clock);

            GameObject instance = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/OpenMMORPG/Demo/Prefabs/GameInstance.prefab");
            if (instance == null)
            {
                Debug.LogError($"[{nameof(DemoDatabaseWiring)}] No GameInstance prefab to give the clock to.");
                return;
            }
            var game = instance.GetComponent<GameInstance>();
            var serialized = new SerializedObject(game);
            serialized.FindProperty("dayNightTimeUpdater").objectReferenceValue = clock;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(instance);
            AssetDatabase.SaveAssets();
        }

        private static void WriteDatabase(ScriptableObject database)
        {
            var serialized = new SerializedObject(database);

            // One entity per body a player can be; the create screen lists these.
            var bodies = new Object[DemoEntityBuilder.PlayerBodies.Length];
            for (int i = 0; i < bodies.Length; ++i)
                bodies[i] = Component<BasePlayerCharacterEntity>(DemoEntityBuilder.PlayerEntityPath(DemoEntityBuilder.PlayerBodies[i]));
            SetList(serialized, "playerCharacterEntities", bodies);
            SetList(serialized, "monsterCharacterEntities", new[]
            {
                Component<BaseMonsterCharacterEntity>($"{EntityDir}/DemoBanditMale.prefab"),
                Component<BaseMonsterCharacterEntity>($"{EntityDir}/DemoBanditFemale.prefab"),
                Component<BaseMonsterCharacterEntity>($"{EntityDir}/DemoMarauderMale.prefab"),
                Component<BaseMonsterCharacterEntity>($"{EntityDir}/DemoMarauderFemale.prefab"),
                Component<BaseMonsterCharacterEntity>($"{EntityDir}/DemoCultistMale.prefab"),
                Component<BaseMonsterCharacterEntity>($"{EntityDir}/DemoCultistFemale.prefab"),
            });

            // The classes a character can be, and the enemy families it can fight. These
            // two lists were never written before, because with one of each the kit could
            // not tell the difference. With three, anything left out of them simply does
            // not exist: an unlisted class cannot be chosen and an unlisted monster has no
            // data id, so its drops resolve to nothing.
            SetList(serialized, "playerCharacters", ClassAssets());
            SetList(serialized, "monsterCharacters", MonsterAssets());
            WriteClassesOntoEntity();

            // The database is what makes an asset exist to the game: anything left out
            // of these lists has no data id at runtime, so an item cannot be spawned and
            // a quest cannot be assigned even though the asset is sitting right there.
            SetList(serialized, "items", DemoItemBuilder.AllItems().ToArray());
            SetList(serialized, "weaponTypes", DemoItemBuilder.AllOfType("WeaponTypes", "t:WeaponType").ToArray());
            SetList(serialized, "armorTypes", DemoItemBuilder.AllOfType("ArmorTypes", "t:ArmorType").ToArray());
            // NPC dialogs are not listed here; the kit collects those from the
            // NpcDatabase when it walks each map's NPCs.
            SetList(serialized, "quests", DemoNpcBuilder.AllQuests().ToArray());
            // The harvestable definitions. Their entity prefabs are not listed here — the
            // spawn areas in the map register those themselves when the scene loads.
            SetList(serialized, "harvestables", DemoHarvestBuilder.AllHarvestables().ToArray());
            WriteClock();

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(database);
        }

        /// <summary>
        /// Tells each player entity which classes are allowed to wear it.
        ///
        /// The create screen pairs a body with a class, and it only offers the classes the
        /// chosen body lists. The demo has two bodies and three classes, and any class may
        /// be either body, so all three belong on both — left as the template shipped, with
        /// the single class it was cloned from, the screen offers exactly one thing to be
        /// however many classes the database holds. The entity is a prefab rather than
        /// data, which is why this reaches out of the database to set it.
        /// </summary>
        private static void WriteClassesOntoEntity()
        {
            foreach (string gender in DemoEntityBuilder.PlayerBodies)
                WriteClassesOntoEntity(DemoEntityBuilder.PlayerEntityPath(gender));
        }

        private static void WriteClassesOntoEntity(string path)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                Debug.LogError($"[{nameof(DemoDatabaseWiring)}] Missing \"{path}\".");
                return;
            }
            var entity = prefab.GetComponent<BasePlayerCharacterEntity>();
            if (entity == null)
            {
                Debug.LogError($"[{nameof(DemoDatabaseWiring)}] \"{path}\" is not a player character.");
                return;
            }
            var serialized = new SerializedObject(entity);
            SetList(serialized, "characterDatabases", ClassAssets());
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(prefab);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }

        private static Object[] ClassAssets()
        {
            var found = new Object[Classes.Length];
            for (int i = 0; i < Classes.Length; ++i)
                found[i] = AssetDatabase.LoadAssetAtPath<PlayerCharacter>(
                    $"{GameDataDir}/Resources/PlayerCharacters/{Classes[i].Name}.asset");
            return found;
        }

        private static Object[] MonsterAssets()
        {
            var found = new Object[Monsters.Length];
            for (int i = 0; i < Monsters.Length; ++i)
                found[i] = AssetDatabase.LoadAssetAtPath<MonsterCharacter>(
                    $"{GameDataDir}/Resources/MonsterCharacters/{Monsters[i].Name}.asset");
            return found;
        }

        private static Object Component<T>(string prefabPath) where T : Component
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogError($"[{nameof(DemoDatabaseWiring)}] Missing prefab \"{prefabPath}\".");
                return null;
            }
            T component = prefab.GetComponent<T>();
            if (component == null)
                Debug.LogError($"[{nameof(DemoDatabaseWiring)}] \"{prefabPath}\" has no {typeof(T).Name}.");
            return component;
        }

        private static void SetList(SerializedObject serialized, string field, Object[] values)
        {
            SerializedProperty list = serialized.FindProperty(field);
            if (list == null)
            {
                Debug.LogError($"[{nameof(DemoDatabaseWiring)}] GameDatabase has no field \"{field}\".");
                return;
            }
            list.ClearArray();
            for (int i = 0; i < values.Length; ++i)
            {
                if (values[i] == null)
                    continue;
                list.InsertArrayElementAtIndex(list.arraySize);
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = values[i];
            }
        }
    }
}
