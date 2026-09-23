using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// The last of the kit's item types the demo had no example of, and the one piece of
    /// scene furniture one of them needs to work: a safe area over the village.
    ///
    /// Nothing here is a new system. Each item is an asset of a type the kit already
    /// implements and the demo simply never made, and the point of building them is that
    /// a type with no example in the demo is a type a reader has to take on trust. After
    /// this the demo makes **sixteen of the kit's eighteen** concrete item types; the two
    /// left out are `CharacterNameChangeItem` and `SpecificRewardingItem`, which need a
    /// cash shop and a fixed reward table respectively and would be furniture for
    /// features the demo does not have.
    ///
    /// **Its own step, and the last of the data steps**, for the reason every late
    /// builder has: the cache's reward table names potions and gems that `Build Items`
    /// and `Build Supplies` write, and the skill scroll names a skill `Build Skills`
    /// writes. Run it after those and it finds everything; run it before and it logs
    /// what is missing rather than writing a broken reference.
    /// </summary>
    public static class DemoSundriesBuilder
    {
        private const string ResourcesDir = "Assets/OpenMMORPG/Demo/GameData/Resources";
        private const string ItemDir = ResourcesDir + "/Items";
        private const string SkillDir = ResourcesDir + "/Skills";
        private const string MapInfoDir = ResourcesDir + "/MapInfos";
        private const string TableDir = ResourcesDir + "/ItemRandomTables";

        public const string CharmItem = "TownCharm";
        public const string PassageItem = "PassageStone";
        public const string TomeItem = "TomeOfInsight";
        public const string CacheItem = "SealedCache";
        public const string SkillScrollItem = "ScrollOfMending";

        /// <summary>The skill the scroll casts. The mage's heal, in anybody's hands.</summary>
        private const string ScrollSkill = "Mend";

        /// <summary>
        /// The safe area over the village, and how far it reaches.
        ///
        /// A sphere rather than a box because the village pad is round - the island
        /// flattens a disc of ground at <see cref="DemoIslandBuilder.VillageGroundRadius"/>
        /// and a box would put its corners out in the barley. 22m is a little past the
        /// flattened ground, which puts the shrine and the lane-ends inside it and stops
        /// short of the fields.
        ///
        /// Lifted 2m so the sphere's waist, not its rim, is what a character walks
        /// through: a sphere centred on the ground has zero height at its edge, and the
        /// boundary would then depend on how tall you are.
        /// </summary>
        private const float SafeRadius = 22f;
        private const float SafeLift = 2f;
        private const string SafeAreaName = "VillageGreen";

        [MenuItem("Open MMORPG/Demo/Build Sundries (safe area, warps, tomes)", priority = 159)]
        public static void Build()
        {
            DemoItemBuilder.EnsureFolder(TableDir);

            BuildCharm();
            BuildPassageStone();
            BuildTome();
            BuildCache();
            BuildSkillScroll();
            PlaceSafeArea();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[{nameof(DemoSundriesBuilder)}] Built the village safe area and five sundries. " +
                      "Run Build NPCs And Quests for the pedlar's board and the guard's warp, " +
                      "and Wire Game Database to register the items.");
        }

        // ---- the safe area ---------------------------------------------------

        /// <summary>
        /// Puts a `SafeArea` over the village, which is the kit's "town is town" rule:
        /// inside it nothing can be damaged, monsters that wander in turn round and walk
        /// back to where they spawned, duels are refused and nothing can be built.
        ///
        /// Four consequences worth knowing before moving it:
        ///
        /// **Everything inside is invincible, not just players.** `DamageableEntity`
        /// folds `IsInSafeArea` into `IsInvincible`, so a wolf that follows you through
        /// the gate cannot be killed until one of you leaves. That is the intended rule
        /// and it is why the wolves spawn well out of town.
        ///
        /// **A campfire cannot be placed in it.** `PlayerCharacterBuildingComponent`
        /// refuses construction both when the builder is in a safe area and when the
        /// construction box overlaps one, so the building item has to be taken outside
        /// the village to use. The kit says so on screen; it is not a broken item.
        ///
        /// **The shooter controller can still swing in here**, deliberately.
        /// `disableAttackInSafeArea` on the controller would stop the attack button
        /// outright, and harvesting is an attack - a player standing next to a tree at
        /// the edge of town would silently stop being able to chop it. Invincibility
        /// already does the work without taking a verb away.
        ///
        /// **It is a trigger, so the navmesh ignores it.** Unity's bake skips trigger
        /// colliders, which is why this root is kept out of `MovableRootNames` - there is
        /// nothing here to carve a hole in the ground.
        /// </summary>
        private static void PlaceSafeArea()
        {
            Scene scene = default;
            bool wasOpen = false;
            for (int i = 0; i < SceneManager.sceneCount; ++i)
            {
                Scene loaded = SceneManager.GetSceneAt(i);
                if (loaded.isLoaded && loaded.path == DemoSceneBuilder.ScenePath)
                {
                    scene = loaded;
                    wasOpen = true;
                }
            }
            if (!wasOpen)
                scene = EditorSceneManager.OpenScene(DemoSceneBuilder.ScenePath, OpenSceneMode.Additive);

            GameObject root = null;
            foreach (GameObject candidate in scene.GetRootGameObjects())
            {
                if (candidate.name == DemoSceneBuilder.SafeAreaRootName)
                    root = candidate;
            }
            if (root == null)
            {
                root = new GameObject(DemoSceneBuilder.SafeAreaRootName);
                SceneManager.MoveGameObjectToScene(root, scene);
            }

            Transform existing = root.transform.Find(SafeAreaName);
            GameObject area = existing != null ? existing.gameObject : new GameObject(SafeAreaName);
            if (existing == null)
            {
                area.transform.SetParent(root.transform, false);
                Vector2 village = DemoIslandBuilder.VillageCentre;
                area.transform.position = new Vector3(
                    village.x, DemoIslandBuilder.VillageHeight + SafeLift, village.y);
            }

            // Nothing here should be baked into anything - no renderer, no occluder, no
            // navmesh obstacle. Static flags off is the honest way to say that.
            GameObjectUtility.SetStaticEditorFlags(area, 0);

            var sphere = area.GetComponent<SphereCollider>();
            if (sphere == null)
                sphere = area.AddComponent<SphereCollider>();
            sphere.isTrigger = true;
            sphere.radius = SafeRadius;
            // The item picks a random point inside the collider's *bounds*, so an offset
            // centre would move the drop as well as the zone. Keep it on the transform.
            sphere.center = Vector3.zero;

            if (area.GetComponent<SafeArea>() == null)
                area.AddComponent<SafeArea>();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            if (!wasOpen)
                EditorSceneManager.CloseScene(scene, true);
            Debug.Log($"[{nameof(DemoSundriesBuilder)}] Safe area {(existing == null ? "placed" : "refreshed")} " +
                      $"over the village, {SafeRadius}m.");
        }

        // ---- the warps -------------------------------------------------------

        /// <summary>
        /// A charm that always takes you to town, whatever you are bound to.
        ///
        /// It reads nothing off the character: it finds a `SafeArea` in the loaded scene,
        /// picks a random point inside its collider and drops the player on the navmesh
        /// nearest that point. That is the whole difference between it and the Scroll of
        /// Return, and it is a real one the moment a player binds to a shrine that is not
        /// the village one - the scroll follows the binding, the charm does not.
        ///
        /// **It needs a safe area to exist**, or it fails silently and consumes nothing -
        /// which is why the safe area is built in the same step and not left as a thing
        /// to remember.
        /// </summary>
        private static void BuildCharm()
        {
            var charm = Create<WarpToSafeAreaItem>($"{ItemDir}/{CharmItem}.asset");
            var serialized = new SerializedObject(charm);
            serialized.FindProperty("id").stringValue = CharmItem;
            serialized.FindProperty("defaultTitle").stringValue = "Town Charm";
            serialized.FindProperty("defaultDescription").stringValue =
                "A knot of rowan and red thread, the sort every mother on the island ties. It knows the way home even when you do not.";
            serialized.FindProperty("sellPrice").intValue = 45;
            serialized.FindProperty("weight").floatValue = 0.05f;
            serialized.FindProperty("maxStack").intValue = 10;
            serialized.FindProperty("useItemCooldown").floatValue = 120f;
            // How far off the random point the kit will look for standable ground. The
            // village is flat and fully navmeshed, so 5m is generous already.
            serialized.FindProperty("findGroundDistance").floatValue = 5f;
            DemoItemBuilder.AdoptItemIcon(serialized, "TownCharm");
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(charm);
        }

        /// <summary>
        /// A stone that opens the way into the crypt from wherever you are standing.
        ///
        /// The one **cross-map** warp a player carries. `WarpToMapItem` with a map info
        /// set hands the character to the other map server exactly as the crypt door
        /// does, so this is the same journey by a different door - which is the thing
        /// worth showing, because the dungeon warp is otherwise only ever taken by
        /// walking into a portal the player did not configure.
        ///
        /// It lands on the crypt's arrival landing, the same point the door leads to, so
        /// a player who buys one does not skip any of the dungeon - only the walk to it.
        /// </summary>
        private static void BuildPassageStone()
        {
            var stone = Create<WarpToMapItem>($"{ItemDir}/{PassageItem}.asset");
            var serialized = new SerializedObject(stone);
            serialized.FindProperty("id").stringValue = PassageItem;
            serialized.FindProperty("defaultTitle").stringValue = "Passage Stone";
            serialized.FindProperty("defaultDescription").stringValue =
                "Cold basalt with a hole worn through it. Look at the crypt door through the hole and you are already inside.";
            serialized.FindProperty("sellPrice").intValue = 150;
            serialized.FindProperty("weight").floatValue = 0.3f;
            serialized.FindProperty("maxStack").intValue = 5;
            serialized.FindProperty("useItemCooldown").floatValue = 15f;

            var crypt = AssetDatabase.LoadAssetAtPath<BaseMapInfo>(DemoDungeonBuilder.MapInfoPath);
            if (crypt == null)
            {
                Debug.LogError($"[{nameof(DemoSundriesBuilder)}] No {DemoDungeonBuilder.MapInfoPath}; " +
                               "run Build Dungeon first or the stone warps nowhere.");
            }
            serialized.FindProperty("warpToMapInfo").objectReferenceValue = crypt;
            serialized.FindProperty("warpToPosition").vector3Value = DemoDungeonBuilder.ArrivalPosition;
            serialized.FindProperty("warpOverrideRotation").boolValue = true;
            serialized.FindProperty("warpToRotation").vector3Value =
                new Vector3(0f, DemoDungeonBuilder.ArrivalYaw, 0f);

            DemoItemBuilder.AdoptItemIcon(serialized, "PassageStone");
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(stone);
        }

        // ---- the tome --------------------------------------------------------

        /// <summary>
        /// A book that is worth reading twice: a lump of experience now, and a while of
        /// earning it faster afterwards.
        ///
        /// `ExpPotionItem` carries both an `exp` amount and a whole `Buff`, and using only
        /// one of them would leave half the type undemonstrated. The buff raises
        /// `expRate`, which the gameplay rule adds to the global rate rather than
        /// multiplying by it - `exp * multiplier * (ExpRate + caches.ExpRate)` - so 0.5
        /// here is a straight +50% while it lasts, not +50% of a number nobody can see.
        /// </summary>
        private static void BuildTome()
        {
            var tome = Create<ExpPotionItem>($"{ItemDir}/{TomeItem}.asset");
            var serialized = new SerializedObject(tome);
            serialized.FindProperty("id").stringValue = TomeItem;
            serialized.FindProperty("defaultTitle").stringValue = "Tome of Insight";
            serialized.FindProperty("defaultDescription").stringValue =
                "Somebody's field notes, bound in oilcloth. Half of it is wrong and the other half is worth a year of guessing.";
            serialized.FindProperty("sellPrice").intValue = 400;
            serialized.FindProperty("weight").floatValue = 0.6f;
            serialized.FindProperty("maxStack").intValue = 5;
            serialized.FindProperty("useItemCooldown").floatValue = 5f;
            serialized.FindProperty("exp").intValue = 300;
            serialized.FindProperty("buff.duration.baseAmount").floatValue = 600f;
            // `baseStats`, not `baseAmount`: `CharacterStatsIncremental` does not follow
            // the `IncrementalFloat` naming the rest of a Buff uses, and a wrong path here
            // returns a null SerializedProperty and throws rather than warning.
            serialized.FindProperty("buff.increaseStats.baseStats.expRate").floatValue = 0.5f;
            DemoItemBuilder.AdoptItemIcon(serialized, "TomeOfInsight");
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(tome);
        }

        // ---- the cache -------------------------------------------------------

        /// <summary>What can come out of a cache, and how likely each is.</summary>
        private struct Reward
        {
            public string Item;
            public int Min;
            public int Max;
            public int Weight;
        }

        /// <summary>
        /// Three pulls from this table per cache, so the common rows are what a player
        /// usually gets and the gems are the reason to open the next one.
        ///
        /// **Every row is an item the database already holds.** `RandomRewardingItem` does
        /// not override `PrepareRelatesData`, so nothing in this table gets registered by
        /// being in it - an item that exists only here would be a null at runtime. Adding
        /// a row means checking the item is in the Items folder, which is what
        /// `DemoItemBuilder.AllItems` sweeps into the database.
        ///
        /// `noDropWeight` is the chance a pull yields nothing, which is what keeps three
        /// pulls from always being three items.
        /// </summary>
        private static readonly Reward[] Rewards =
        {
            new Reward { Item = "MinorHealingPotion", Min = 2, Max = 4, Weight = 30 },
            new Reward { Item = "MinorManaPotion", Min = 2, Max = 4, Weight = 30 },
            new Reward { Item = "Timber", Min = 3, Max = 6, Weight = 20 },
            new Reward { Item = "Stone", Min = 3, Max = 6, Weight = 20 },
            new Reward { Item = "Leather", Min = 1, Max = 3, Weight = 15 },
            new Reward { Item = "Arrow", Min = 20, Max = 40, Weight = 15 },
            new Reward { Item = "ScrollOfReturn", Min = 1, Max = 1, Weight = 6 },
            new Reward { Item = "GemGarnet", Min = 1, Max = 1, Weight = 4 },
            new Reward { Item = "GemSapphire", Min = 1, Max = 1, Weight = 4 },
            new Reward { Item = "GemCitrine", Min = 1, Max = 1, Weight = 3 },
        };

        private const float NoDropWeight = 12f;
        private const int CachePulls = 3;

        private static void BuildCache()
        {
            var table = Create<ItemRandomByWeightTable>($"{TableDir}/CacheRewards.asset");
            var tableSerialized = new SerializedObject(table);
            SerializedProperty rows = tableSerialized.FindProperty("randomItems");
            rows.arraySize = Rewards.Length;
            int missing = 0;
            for (int i = 0; i < Rewards.Length; ++i)
            {
                Reward reward = Rewards[i];
                var item = AssetDatabase.LoadAssetAtPath<BaseItem>($"{ItemDir}/{reward.Item}.asset");
                if (item == null)
                {
                    Debug.LogWarning($"[{nameof(DemoSundriesBuilder)}] No item \"{reward.Item}\" for the cache table.");
                    ++missing;
                }
                SerializedProperty row = rows.GetArrayElementAtIndex(i);
                row.FindPropertyRelative("item").objectReferenceValue = item;
                // Level 1 flat: the demo's consumables and materials have no levels, and a
                // minLevel of 0 tells the kit to use maxLevel rather than randomise.
                row.FindPropertyRelative("minLevel").intValue = 0;
                row.FindPropertyRelative("maxLevel").intValue = 1;
                row.FindPropertyRelative("minAmount").intValue = reward.Min;
                row.FindPropertyRelative("maxAmount").intValue = reward.Max;
                // A row with weight 0 is skipped entirely, so this is the one field that
                // must never be left at its default.
                row.FindPropertyRelative("randomWeight").intValue = reward.Weight;
            }
            tableSerialized.FindProperty("noDropWeight").floatValue = NoDropWeight;
            tableSerialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(table);
            if (missing > 0)
                Debug.LogWarning($"[{nameof(DemoSundriesBuilder)}] {missing} empty row(s) in the cache table.");

            var cache = Create<RandomRewardingItem>($"{ItemDir}/{CacheItem}.asset");
            var serialized = new SerializedObject(cache);
            serialized.FindProperty("id").stringValue = CacheItem;
            serialized.FindProperty("defaultTitle").stringValue = "Sealed Cache";
            serialized.FindProperty("defaultDescription").stringValue =
                "A strongbox off a wreck, still sealed and still dry. Nobody sells one knowing what is in it.";
            serialized.FindProperty("sellPrice").intValue = 250;
            serialized.FindProperty("weight").floatValue = 1.2f;
            serialized.FindProperty("maxStack").intValue = 10;
            serialized.FindProperty("useItemCooldown").floatValue = 1f;
            serialized.FindProperty("rewardingItemsTable").objectReferenceValue = table;
            serialized.FindProperty("maxDropAmount").intValue = CachePulls;
            DemoItemBuilder.AdoptItemIcon(serialized, "SealedCache");
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(cache);
        }

        // ---- the skill scroll ------------------------------------------------

        /// <summary>
        /// A scroll anybody can cast Mend from, whatever they trained as.
        ///
        /// `SkillItem` is the one usable type whose `UseItem` does nothing - it returns
        /// true and consumes nothing, because a skill item is not used from the inventory
        /// at all. The player puts it on the hotbar and the *skill* path fires it, which
        /// is why the item carries a skill and a level and no effect of its own. Reading
        /// `UseItem` alone would suggest it was unfinished; it is the kit's own TODO on a
        /// method that is never the one called.
        ///
        /// Mend at level 1 rather than a scaling level: a warrior with no healing at all
        /// is the player this is for, and a scroll that heals better than the class whose
        /// skill it is would be a strange thing to sell in the same square.
        /// </summary>
        private static void BuildSkillScroll()
        {
            var scroll = Create<SkillItem>($"{ItemDir}/{SkillScrollItem}.asset");
            var serialized = new SerializedObject(scroll);
            serialized.FindProperty("id").stringValue = SkillScrollItem;
            serialized.FindProperty("defaultTitle").stringValue = "Scroll of Mending";
            serialized.FindProperty("defaultDescription").stringValue =
                "One prayer, copied out fair by somebody who meant it. Say the words and the words do the rest.";
            serialized.FindProperty("sellPrice").intValue = 60;
            serialized.FindProperty("weight").floatValue = 0.1f;
            serialized.FindProperty("maxStack").intValue = 10;
            serialized.FindProperty("useItemCooldown").floatValue = 12f;

            var skill = AssetDatabase.LoadAssetAtPath<BaseSkill>($"{SkillDir}/{ScrollSkill}.asset");
            if (skill == null)
            {
                Debug.LogError($"[{nameof(DemoSundriesBuilder)}] No skill \"{ScrollSkill}\"; " +
                               "run Build Skills first or the scroll casts nothing.");
            }
            serialized.FindProperty("usingSkill").objectReferenceValue = skill;
            serialized.FindProperty("usingSkillLevel").intValue = 1;

            DemoItemBuilder.AdoptItemIcon(serialized, "ScrollOfMending");
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(scroll);
        }

        // ---- for the shops ---------------------------------------------------

        /// <summary>
        /// What the pedlar keeps under the counter. The island has nowhere else to find
        /// any of it yet, which is the same reason the gems and the return scroll are on
        /// his board.
        /// </summary>
        public static string[] SundryNames()
        {
            return new[] { CharmItem, SkillScrollItem, PassageItem, CacheItem, TomeItem };
        }

        /// <summary>Where the island's warp guide sends a player, and what it costs.</summary>
        public static Vector3 GuidedWarpPosition
        {
            get { return DemoSceneBuilder.CryptArrivalWorld; }
        }

        public static float GuidedWarpYaw
        {
            get { return DemoSceneBuilder.CryptArrivalYaw; }
        }

        public const int GuidedWarpToll = 25;

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
