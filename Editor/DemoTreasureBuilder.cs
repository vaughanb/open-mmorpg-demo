using System.Collections.Generic;
using MultiplayerARPG.Demo;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// The treasure chests: the loot tables, the two chest entity prefabs, and the swap
    /// that turns every chest *prop* standing in a scene into one of those entities.
    ///
    /// **The chests were scenery.** The bank vault, the bandit camp and the crypt were
    /// furnished with `Chest_Wood` and `Chest_Legendary` straight out of the prop library:
    /// a mesh and a collider, with nothing to click. Each is now a
    /// <see cref="TreasureChestEntity"/> - the kit's loot container, standing still - on the
    /// same spot, under the same parent, with the same name, so the interior audit and the
    /// furniture rules that know a chest by its name still find one there.
    ///
    /// **Why a swap rather than a change to the placers.** The village and the camp are
    /// settled areas: their generators no longer run over a root that is standing, and
    /// the scene is their source of truth (see `DemoSceneBuilder.FrozenAreaRootNames`).
    /// Teaching `Prop()` to place an entity would reach neither of them without a full
    /// regenerate, and would also reach the homestead's strongbox, which borrows the same
    /// model and must stay a plain mesh. So the placers are left alone, and the swap is run
    /// over whatever they put down: by this menu item today, and by every regenerate at its
    /// end, so a rebuilt village gets its chests back as entities without a second step.
    ///
    /// **Each chest carries a scene object id of its own.** A scene object is spawned to
    /// clients by the hash of its `sceneObjectId`, which is saved on the prefab; two
    /// instances of one prefab therefore share an id until something gives them their
    /// own, and the kit's own generator only fills an *empty* one in. The id here is the
    /// chest's path in the hierarchy, so it is stable across runs and distinct across a
    /// room with three chests in it. The same trap is written up in DemoShrineBuilder.
    ///
    /// **After this, build the map server.** A scene object that the project knows and the
    /// build does not is an "Unable to spawn object" per chest.
    /// </summary>
    public static class DemoTreasureBuilder
    {
        private const string PrefabDir = "Assets/OpenMMORPG/Demo/Prefabs/GamePlay/Treasure";
        private const string ResourcesDir = "Assets/OpenMMORPG/Demo/GameData/Resources";
        private const string ItemDir = ResourcesDir + "/Items";
        private const string TableDir = ResourcesDir + "/ItemDropTables";
        /// <summary>
        /// Where the chest models are looked for, in order: the copy collected into the demo's
        /// own art first, the library second. The island's chests reference the collected
        /// copy and the crypt's the library, and a swap has to recognise both.
        /// </summary>
        private static readonly string[] PropDirs =
        {
            "Assets/OpenMMORPG/Demo/Art/Props/Models",
            "Assets/Plugins/Quaternius/Props/Models",
        };
        private const string ModelName = "Model";

        /// <summary>The scenes a swap is run over. The menu stage's chest is set dressing and stays one.</summary>
        private static readonly string[] ScenePaths = { DemoSceneBuilder.ScenePath, DemoDungeonBuilder.ScenePath };

        private struct Loot
        {
            public string Item;
            public int Min;
            public int Max;
            public float Chance;
        }

        private struct Kind
        {
            public string Model;
            public string Prefab;
            public string Title;
            public string Table;
            public int MinItems;
            public int MaxItems;
            public int MinGold;
            public int MaxGold;
            public float RefillSeconds;
            public Loot[] Loot;
        }

        /// <summary>
        /// The wooden chest: provisions and materials, the odd scroll, and a gem once in a
        /// while. Two or three kinds a fill, back in five minutes - enough to be worth
        /// looking in on a way past, not enough to farm. The legendary chest in the bank
        /// vault and the crypt's sanctum holds the things a player would otherwise buy or
        /// win: the Sealed Cache, the socket gems, the tome and the passage stone. Three
        /// or four kinds, and a quarter of an hour to come back. Both hold coins as well, as a
        /// stack of the gold item: a handful in the wooden chest, a bandit's week in the
        /// legendary one, taken like any other item (the kit credits the purse on pickup).
        /// </summary>
        private static readonly Kind[] Kinds =
        {
            new Kind
            {
                Model = "Chest_Wood", Prefab = "DemoTreasureChest", Title = "Treasure Chest",
                Table = "ChestLoot", MinItems = 2, MaxItems = 3, MinGold = 6, MaxGold = 14, RefillSeconds = 300f,
                Loot = new[]
                {
                    new Loot { Item = "MinorHealingPotion", Min = 1, Max = 2, Chance = 0.50f },
                    new Loot { Item = "Bread", Min = 1, Max = 2, Chance = 0.45f },
                    new Loot { Item = "Arrow", Min = 10, Max = 20, Chance = 0.40f },
                    new Loot { Item = "Cheese", Min = 1, Max = 1, Chance = 0.35f },
                    new Loot { Item = "MinorManaPotion", Min = 1, Max = 2, Chance = 0.35f },
                    new Loot { Item = "Ale", Min = 1, Max = 2, Chance = 0.30f },
                    new Loot { Item = "Timber", Min = 2, Max = 4, Chance = 0.30f },
                    new Loot { Item = "Stone", Min = 2, Max = 4, Chance = 0.30f },
                    new Loot { Item = "Stew", Min = 1, Max = 1, Chance = 0.25f },
                    new Loot { Item = "Leather", Min = 1, Max = 2, Chance = 0.25f },
                    new Loot { Item = "IronOre", Min = 1, Max = 3, Chance = 0.20f },
                    new Loot { Item = "ScrollOfReturn", Min = 1, Max = 1, Chance = 0.10f },
                    new Loot { Item = "GemGarnet", Min = 1, Max = 1, Chance = 0.05f },
                },
            },
            new Kind
            {
                Model = "Chest_Legendary", Prefab = "DemoLegendaryChest", Title = "Legendary Chest",
                Table = "LegendaryChestLoot", MinItems = 3, MaxItems = 4, MinGold = 40, MaxGold = 90, RefillSeconds = 900f,
                Loot = new[]
                {
                    new Loot { Item = "SealedCache", Min = 1, Max = 1, Chance = 0.60f },
                    new Loot { Item = "MinorHealingPotion", Min = 2, Max = 3, Chance = 0.50f },
                    new Loot { Item = "IronIngot", Min = 1, Max = 2, Chance = 0.40f },
                    new Loot { Item = "SpicedWine", Min = 1, Max = 2, Chance = 0.40f },
                    new Loot { Item = "GemGarnet", Min = 1, Max = 1, Chance = 0.35f },
                    new Loot { Item = "GemSapphire", Min = 1, Max = 1, Chance = 0.35f },
                    new Loot { Item = "GemCitrine", Min = 1, Max = 1, Chance = 0.30f },
                    new Loot { Item = "ScrollOfMending", Min = 1, Max = 1, Chance = 0.30f },
                    new Loot { Item = "ScrollOfReturn", Min = 1, Max = 1, Chance = 0.30f },
                    new Loot { Item = "TomeOfInsight", Min = 1, Max = 1, Chance = 0.25f },
                    new Loot { Item = "PassageStone", Min = 1, Max = 1, Chance = 0.15f },
                },
            },
        };

        [MenuItem("Open MMORPG/Demo/Build Treasure Chests", priority = 159)]
        public static void Build()
        {
            DemoItemBuilder.EnsureFolder(PrefabDir);
            DemoItemBuilder.EnsureFolder(TableDir);

            foreach (Kind kind in Kinds)
            {
                ItemDropTable table = BuildTable(kind);
                BuildPrefab(kind, table);
            }
            AssetDatabase.SaveAssets();

            int swapped = 0;
            foreach (string scenePath in ScenePaths)
                swapped += ConvertSceneFile(scenePath);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[{nameof(DemoTreasureBuilder)}] Built the chest prefabs and loot tables; " +
                      $"{swapped} chest prop(s) replaced with entities. Every chest is a scene object the " +
                      "map server has to know, so run Build Map Server.");
        }

        // ---- the loot tables ------------------------------------------------------------

        /// <summary>
        /// A kit `ItemDropTable`, one row per entry with its own chance. Every row is an
        /// item the database already holds: nothing registers an item by being in a drop
        /// table, so a row naming an item outside the Items folder would be a null at
        /// runtime (the same rule as the Sealed Cache's table in DemoSundriesBuilder).
        /// </summary>
        private static ItemDropTable BuildTable(Kind kind)
        {
            var table = Create<ItemDropTable>($"{TableDir}/{kind.Table}.asset");
            var serialized = new SerializedObject(table);
            SerializedProperty rows = serialized.FindProperty("randomItems");
            rows.arraySize = kind.Loot.Length;
            int missing = 0;
            for (int i = 0; i < kind.Loot.Length; ++i)
            {
                Loot loot = kind.Loot[i];
                var item = AssetDatabase.LoadAssetAtPath<BaseItem>($"{ItemDir}/{loot.Item}.asset");
                if (item == null)
                {
                    Debug.LogWarning($"[{nameof(DemoTreasureBuilder)}] No item \"{loot.Item}\" for the {kind.Table} table.");
                    ++missing;
                }
                SerializedProperty row = rows.GetArrayElementAtIndex(i);
                row.FindPropertyRelative("item").objectReferenceValue = item;
                // Level 1 flat: a minLevel of 0 tells the kit to use maxLevel rather than randomise.
                row.FindPropertyRelative("minLevel").intValue = 0;
                row.FindPropertyRelative("maxLevel").intValue = 1;
                row.FindPropertyRelative("minAmount").intValue = loot.Min;
                row.FindPropertyRelative("maxAmount").intValue = loot.Max;
                row.FindPropertyRelative("dropRate").floatValue = loot.Chance;
            }
            serialized.FindProperty("randomCurrencies").arraySize = 0;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(table);
            if (missing > 0)
                Debug.LogWarning($"[{nameof(DemoTreasureBuilder)}] {missing} empty row(s) in the {kind.Table} table.");
            return table;
        }

        // ---- the prefabs ------------------------------------------------------------------

        /// <summary>
        /// One chest: the entity on the root with a trigger box for the kit to find it by,
        /// the library model under it posed shut with an Animator for the lid, and a
        /// solid box on a child for walking into.
        ///
        /// **Both the kit's lookups want the collider on the entity's own object.** A click
        /// asks the collider it hit for an `ITargetableEntity` with `GetComponent`, and the
        /// server's distance check overlaps the entity's layer and asks each collider the
        /// same way; neither looks up the hierarchy. The trigger on the root is for them,
        /// and it is padded so a click from outside meets it before the solid box. The
        /// solid box is on a child on the default layer because the ItemDrop layer the
        /// entity takes at runtime is a layer for things that are picked up, and whether it
        /// stops a player is a question of the collision matrix this never has to ask.
        ///
        /// **Shut by the pack's own clip**, as DemoSceneBuilder.ShutLid does for the props:
        /// the rig is posed wide open at rest and the lid bone's axes are nothing to guess
        /// a hinge from. The bone transforms the sample writes are saved into the prefab,
        /// and the collider is measured off the *skinned* result, because the shared mesh
        /// bounds describe the open pose and would stand a box up behind the chest.
        /// </summary>
        private static GameObject BuildPrefab(Kind kind, ItemDropTable table)
        {
            string modelPath = ModelPath(kind.Model);
            var modelPrefab = modelPath != null ? AssetDatabase.LoadAssetAtPath<GameObject>(modelPath) : null;
            if (modelPrefab == null)
            {
                Debug.LogError($"[{nameof(DemoTreasureBuilder)}] No chest model named \"{kind.Model}.fbx\" under " +
                               string.Join(" or ", PropDirs) + ".");
                return null;
            }
            AnimationClip open = Clip(modelPath, "Open");
            AnimationClip close = Clip(modelPath, "Close");
            AnimationClip opened = Clip(modelPath, "Opened");
            AnimationClip closed = Clip(modelPath, "Closed");
            if (open == null || close == null || opened == null || closed == null)
                Debug.LogWarning($"[{nameof(DemoTreasureBuilder)}] {kind.Model} is missing a lid clip; the lid will not move.");

            string path = $"{PrefabDir}/{kind.Prefab}.prefab";
            var root = new GameObject(kind.Prefab);
            try
            {
                root.layer = LayerMask.NameToLayer("ItemDrop");

                var model = (GameObject)PrefabUtility.InstantiatePrefab(modelPrefab, root.transform);
                model.name = ModelName;
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
                model.transform.localScale = Vector3.one;
                // Always: a chest at the edge of the view that stopped animating when its
                // renderer was culled would be found half-open on the way back.
                var animator = model.AddComponent<Animator>();
                animator.runtimeAnimatorController = null;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                if (closed != null)
                    closed.SampleAnimation(model, 0f);

                Bounds box = SkinnedBounds(root.transform, model);
                var collision = new GameObject("Collision");
                collision.transform.SetParent(root.transform, false);
                var solid = collision.AddComponent<BoxCollider>();
                solid.center = box.center;
                solid.size = box.size;

                const float padding = 0.06f;
                var trigger = root.AddComponent<BoxCollider>();
                trigger.isTrigger = true;
                trigger.center = box.center;
                trigger.size = box.size + Vector3.one * padding * 2f;

                var chest = root.AddComponent<TreasureChestEntity>();
                var serialized = new SerializedObject(chest);
                serialized.FindProperty("entityTitle").stringValue = kind.Title;
                serialized.FindProperty("lootTable").objectReferenceValue = table;
                serialized.FindProperty("minItems").intValue = kind.MinItems;
                serialized.FindProperty("maxItems").intValue = kind.MaxItems;
                serialized.FindProperty("minGold").intValue = kind.MinGold;
                serialized.FindProperty("maxGold").intValue = kind.MaxGold;
                serialized.FindProperty("refillSeconds").floatValue = kind.RefillSeconds;
                serialized.FindProperty("lid").objectReferenceValue = animator;
                serialized.FindProperty("openClip").objectReferenceValue = open;
                serialized.FindProperty("closeClip").objectReferenceValue = close;
                serialized.FindProperty("openedPose").objectReferenceValue = opened;
                serialized.FindProperty("closedPose").objectReferenceValue = closed;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                DemoAudioWiring.WireChest(chest);

                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
            DemoEntityBuilder.GiveOwnNetworkId(path);
            // Forced: writing a new file over a prefab does not refresh the editor's loaded
            // copy, and the swap below instantiates that copy.
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        /// <summary>The first of <see cref="PropDirs"/> that holds the model, or null.</summary>
        private static string ModelPath(string model)
        {
            foreach (string dir in PropDirs)
            {
                string path = $"{dir}/{model}.fbx";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
                    return path;
            }
            return null;
        }

        /// <summary>
        /// The clip of that name in the model. The pack names them `Chest_Armature|Open` on
        /// one chest and `Chest_Armature|Chest_Open` on the other, so only what follows
        /// the bar, less any `Chest_`, is compared. Unity keeps a `__preview__` copy of
        /// every clip beside the real one.
        /// </summary>
        private static AnimationClip Clip(string modelPath, string name)
        {
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(modelPath))
            {
                var clip = asset as AnimationClip;
                if (clip == null || clip.name.StartsWith("__preview__"))
                    continue;
                string clipName = clip.name;
                int bar = clipName.LastIndexOf('|');
                if (bar >= 0)
                    clipName = clipName.Substring(bar + 1);
                if (clipName.StartsWith("Chest_"))
                    clipName = clipName.Substring("Chest_".Length);
                if (clipName == name)
                    return clip;
            }
            return null;
        }

        /// <summary>The model's extent in the root's space as its skin currently stands, not as it was bound.</summary>
        private static Bounds SkinnedBounds(Transform space, GameObject model)
        {
            var bounds = new Bounds();
            bool any = false;
            foreach (SkinnedMeshRenderer skinned in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var baked = new Mesh();
                skinned.BakeMesh(baked, true);
                Vector3[] vertices = baked.vertices;
                for (int i = 0; i < vertices.Length; ++i)
                {
                    // BakeMesh writes vertices in the renderer's own space with its scale
                    // already applied, so only its rotation and position are left to undo.
                    Vector3 world = skinned.transform.position + skinned.transform.rotation * vertices[i];
                    Vector3 point = space.InverseTransformPoint(world);
                    if (any)
                        bounds.Encapsulate(point);
                    else
                    {
                        bounds = new Bounds(point, Vector3.zero);
                        any = true;
                    }
                }
                Object.DestroyImmediate(baked);
            }
            return bounds;
        }

        // ---- the swap -----------------------------------------------------------------------

        /// <summary>Opens a scene (additively, if it is not the open one), swaps its chests, saves it.</summary>
        private static int ConvertSceneFile(string scenePath)
        {
            Scene scene = default;
            bool wasOpen = false;
            for (int i = 0; i < SceneManager.sceneCount; ++i)
            {
                Scene loaded = SceneManager.GetSceneAt(i);
                if (loaded.isLoaded && loaded.path == scenePath)
                {
                    scene = loaded;
                    wasOpen = true;
                }
            }
            if (!wasOpen)
            {
                if (!System.IO.File.Exists(scenePath))
                {
                    Debug.LogWarning($"[{nameof(DemoTreasureBuilder)}] No scene at \"{scenePath}\"; nothing to convert there.");
                    return 0;
                }
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            }

            int swapped = ConvertScene(scene);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            if (!wasOpen)
                EditorSceneManager.CloseScene(scene, true);
            return swapped;
        }

        /// <summary>
        /// Replaces every chest prop in the scene with the matching chest entity, on the
        /// same spot under the same parent with the same name, and gives every chest
        /// entity in the scene - new or already there - its own scene object id.
        ///
        /// Called at the end of each scene regenerate as well as from the menu, and
        /// idempotent either way: a chest that is already an entity is left where it
        /// stands, hand-nudged or not. Returns how many props were swapped.
        /// </summary>
        public static int ConvertScene(Scene scene)
        {
            var prefabs = new Dictionary<string, GameObject>();
            foreach (Kind kind in Kinds)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/{kind.Prefab}.prefab");
                if (prefab == null)
                {
                    Debug.LogWarning($"[{nameof(DemoTreasureBuilder)}] No chest prefab at \"{PrefabDir}/{kind.Prefab}.prefab\". " +
                                     "Run Open MMORPG > Demo > Build Treasure Chests; the chests in " +
                                     $"{scene.name} are still props.");
                    return 0;
                }
                prefabs[$"{kind.Model}.fbx"] = prefab;
            }

            // Found first, swapped after: a swap destroys objects the walk would visit.
            var props = new List<GameObject>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform node in root.GetComponentsInChildren<Transform>(true))
                {
                    GameObject go = node.gameObject;
                    if (go.GetComponentInParent<TreasureChestEntity>(true) != null)
                        continue; // The model inside an entity is an instance of the same FBX.
                    if (PrefabUtility.GetNearestPrefabInstanceRoot(go) != go)
                        continue;
                    // By file name: the same model is referenced from the collected copy in
                    // one scene and from the library in another.
                    string source = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
                    if (!string.IsNullOrEmpty(source) && prefabs.ContainsKey(System.IO.Path.GetFileName(source)))
                        props.Add(go);
                }
            }

            foreach (GameObject prop in props)
            {
                GameObject prefab = prefabs[System.IO.Path.GetFileName(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(prop))];
                Transform parent = prop.transform.parent;
                int order = prop.transform.GetSiblingIndex();
                var entity = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                entity.name = prop.name;
                entity.transform.SetParent(parent, false);
                entity.transform.localPosition = prop.transform.localPosition;
                entity.transform.localRotation = prop.transform.localRotation;
                entity.transform.localScale = prop.transform.localScale;
                entity.transform.SetSiblingIndex(order);
                // A regenerate marks whole areas static; a thing with a lid that moves and
                // an identity that spawns is not.
                foreach (Transform node in entity.GetComponentsInChildren<Transform>(true))
                    GameObjectUtility.SetStaticEditorFlags(node.gameObject, (StaticEditorFlags)0);
                Object.DestroyImmediate(prop);
            }

            GiveSceneObjectIds(scene);
            if (props.Count > 0)
                EditorSceneManager.MarkSceneDirty(scene);
            return props.Count;
        }

        /// <summary>
        /// Every chest's scene object id, from its path in the hierarchy. Named rather than
        /// numbered so it is stable across runs; suffixed only where two chests share a
        /// path, which the dungeon's props root can do. Any clash with something that is
        /// not a chest is an error, because the symptom at runtime is every scene object
        /// after the pair failing to appear.
        /// </summary>
        private static void GiveSceneObjectIds(Scene scene)
        {
            var taken = new HashSet<string>();
            var chests = new List<TreasureChestEntity>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (var identity in root.GetComponentsInChildren<LiteNetLibManager.LiteNetLibIdentity>(true))
                {
                    if (identity.GetComponent<TreasureChestEntity>() != null)
                        chests.Add(identity.GetComponent<TreasureChestEntity>());
                    else if (!string.IsNullOrEmpty(identity.SceneObjectId))
                        taken.Add(identity.SceneObjectId);
                }
            }
            // Hierarchy order, so the suffix a duplicate path gets is the same every run.
            chests.Sort((a, b) => string.CompareOrdinal(HierarchyPath(a.transform), HierarchyPath(b.transform)));
            foreach (TreasureChestEntity chest in chests)
            {
                string id = "Chest@" + HierarchyPath(chest.transform);
                string candidate = id;
                for (int n = 2; taken.Contains(candidate); ++n)
                    candidate = $"{id}#{n}";
                taken.Add(candidate);

                var identity = chest.GetComponent<LiteNetLibManager.LiteNetLibIdentity>();
                if (identity.SceneObjectId == candidate)
                    continue;
                var serialized = new SerializedObject(identity);
                serialized.FindProperty("sceneObjectId").stringValue = candidate;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(chest.gameObject);
            }
        }

        private static string HierarchyPath(Transform t)
        {
            string path = t.name;
            for (Transform up = t.parent; up != null; up = up.parent)
                path = up.name + "/" + path;
            return path;
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
