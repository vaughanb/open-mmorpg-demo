using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// Builds the demo's harvestable resources: the trees you can fell, the boulders you
    /// can break and the mushrooms you can pick.
    ///
    /// None of these can be the trees already on the island. Those are
    /// <see cref="TreeInstance"/> records inside the TerrainData — plain numbers the
    /// terrain draws itself, with no GameObject, no components and no network identity —
    /// and the mushrooms are terrain detail, which does not even have a collider. There
    /// is nothing there to attach a <see cref="HarvestableEntity"/> to and nothing for a
    /// server to replicate. A resource node has to be a real entity, so these are built
    /// as entities and sown by <see cref="HarvestableSpawnArea"/>, which is also what
    /// gives them respawn after they are taken.
    ///
    /// The scenery stays as it is. Terrain trees are what make a wood affordable, and a
    /// few dozen harvestable nodes standing among them is what a player expects anyway —
    /// not every tree in a forest is a lumber node.
    /// </summary>
    public static class DemoHarvestBuilder
    {
        private const string GameDataDir = "Assets/OpenMMORPG/Demo/GameData";
        private const string ResourcesDir = GameDataDir + "/Resources";
        private const string HarvestableDir = ResourcesDir + "/Harvestables";
        private const string ItemDir = ResourcesDir + "/Items";
        private const string WeaponTypeDir = ResourcesDir + "/WeaponTypes";
        private const string EntityDir = "Assets/OpenMMORPG/Demo/Prefabs/GamePlay/Harvestables";
        private const string NatureDir = "Assets/Plugins/Quaternius/Nature/Prefabs";

        /// <summary>Unity's built-in layer 14, which the kit reserves for harvestables.</summary>
        private const int HarvestableLayer = 14;

        // ---- what the resources are ------------------------------------------

        private struct MaterialSpec
        {
            public string Name;
            public string Title;
            public string Description;
            public int Price;
            public float Weight;
        }

        private static readonly MaterialSpec[] Materials =
        {
            new MaterialSpec { Name = "Timber", Title = "Timber", Price = 4, Weight = 2f,
                Description = "Rough cut lengths, still smelling of sap." },
            new MaterialSpec { Name = "Stone", Title = "Stone", Price = 3, Weight = 3f,
                Description = "Broken from an outcrop, heavy in the hand." },
            new MaterialSpec { Name = "Browncap", Title = "Browncap Mushroom", Price = 6, Weight = 0.2f,
                Description = "Picked from the leaf litter under the pines." },
        };

        /// <summary>
        /// One kind of node: what it is made of, what it takes to work it, and what it
        /// gives up.
        ///
        /// The tools are the demo's existing weapon types rather than new ones. An axe is
        /// already in the game — the bandits carry it — so felling a tree with it needs no
        /// new art at all, and a sword makes a poor job of the same tree rather than being
        /// refused, which teaches the mechanic without blocking a player who has not found
        /// an axe yet. Nothing harvests with a staff.
        /// </summary>
        private struct NodeSpec
        {
            public string Name;
            public string Title;
            /// <summary>Every model this kind of node is built from, one entity each.</summary>
            public string[] Models;
            public float ModelScale;
            public string Yield;
            public int MaxHp;
            public int ExpPerDamage;
            public float Respawn;
            public float DetectionRadius;
            /// <summary>
            /// Collision, as a share of the model's own size. Twenty tree models come in
            /// every shape from a sapling to an eighteen-metre snag, so a figure in metres
            /// would be right for one of them and wrong for the rest.
            /// </summary>
            public float ColliderWidthShare;
            public float ColliderHeightShare;
            /// <summary>Never let collision fall below this, for the very small models.</summary>
            public float MinColliderRadius;
            public float MinColliderHeight;
            /// <summary>
            /// And never above this. The width share is taken off the whole model, which
            /// for a tree means its canopy: the twisted trees spread ten metres, so the
            /// same share that gives a common tree a sensible trunk gives them a barrel
            /// three metres across that the player cannot walk up to.
            /// </summary>
            public float MaxColliderRadius;
            /// <summary>How much bigger or smaller than the model one of these may come out.</summary>
            public float Smallest;
            public float Largest;
            /// <summary>Tool name to how well it works and how much it yields per point of damage.</summary>
            public (string Tool, float Effectiveness, float PerDamage)[] Tools;
        }

        private static readonly NodeSpec[] Nodes =
        {
            // Every tree on the island is one of these. There is no scenery forest behind
            // them any more, so the whole range of the pack's trees is here: a wood of one
            // repeated model reads as a tiling error.
            new NodeSpec
            {
                Name = "Tree", Title = "Tree", ModelScale = 1f,
                Models = new[]
                {
                    "CommonTree_1", "CommonTree_2", "CommonTree_3", "CommonTree_4", "CommonTree_5",
                    "Pine_1", "Pine_2", "Pine_3", "Pine_4", "Pine_5",
                    "TwistedTree_1", "TwistedTree_2", "TwistedTree_3", "TwistedTree_4", "TwistedTree_5",
                    "DeadTree_1", "DeadTree_2", "DeadTree_3", "DeadTree_4", "DeadTree_5",
                },
                Yield = "Timber", MaxHp = 60, ExpPerDamage = 1, Respawn = 45f,
                DetectionRadius = 3f,
                ColliderWidthShare = 0.16f, ColliderHeightShare = 0.55f,
                MinColliderRadius = 0.35f, MinColliderHeight = 2.5f, MaxColliderRadius = 0.85f, Smallest = 0.82f, Largest = 1.3f,
                Tools = new[] { ("Axe", 1f, 0.09f), ("Sword", 0.35f, 0.04f) },
            },
            new NodeSpec
            {
                Name = "Boulder", Title = "Boulder", ModelScale = 0.75f,
                Models = new[] { "Rock_Medium_1", "Rock_Medium_2", "Rock_Medium_3" },
                Yield = "Stone", MaxHp = 80, ExpPerDamage = 1, Respawn = 60f,
                DetectionRadius = 2.5f,
                ColliderWidthShare = 0.42f, ColliderHeightShare = 0.85f,
                MinColliderRadius = 0.8f, MinColliderHeight = 1.2f, MaxColliderRadius = 1.6f, Smallest = 0.7f, Largest = 1.45f,
                Tools = new[] { ("Axe", 0.6f, 0.05f), ("Sword", 0.25f, 0.03f) },
            },
            new NodeSpec
            {
                // Barely any health, so picking it is one swing of whatever is in hand
                // rather than a fight with a mushroom.
                Name = "Mushroom", Title = "Browncap Mushroom", ModelScale = 1.1f,
                Models = new[] { "Mushroom_Laetiporus", "Mushroom_Common" },
                Yield = "Browncap", MaxHp = 1, ExpPerDamage = 3, Respawn = 30f,
                // A hitbox well taller than the mushroom. A swing is aimed at chest
                // height, so collision that stops at the height of the thing itself is
                // passed clean over and the mushroom cannot be picked at all.
                DetectionRadius = 1.2f,
                ColliderWidthShare = 0.5f, ColliderHeightShare = 1f,
                MinColliderRadius = 0.6f, MinColliderHeight = 1.5f, MaxColliderRadius = 0.8f, Smallest = 0.85f, Largest = 1.15f,
                Tools = new[] { ("Axe", 1f, 0.14f), ("Sword", 1f, 0.14f), ("Staff", 1f, 0.14f) },
            },
        };

        [MenuItem("Open MMORPG/Demo/Build Harvestables")]
        public static void BuildAll()
        {
            DemoItemBuilder.EnsureFolder(HarvestableDir);
            DemoItemBuilder.EnsureFolder(EntityDir);

            BuildMaterials();
            AssetDatabase.SaveAssets();

            int entities = 0;
            foreach (NodeSpec spec in Nodes)
            {
                Harvestable harvestable = BuildHarvestable(spec);
                foreach (string model in spec.Models)
                {
                    if (BuildEntity(spec, model, harvestable))
                        ++entities;
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[{nameof(DemoHarvestBuilder)}] Built {Materials.Length} materials, {Nodes.Length} harvestable kinds and {entities} node entities.");
        }

        private static void BuildMaterials()
        {
            foreach (MaterialSpec spec in Materials)
            {
                var item = Create<JunkItem>($"{ItemDir}/{spec.Name}.asset");
                var serialized = new SerializedObject(item);
                serialized.FindProperty("id").stringValue = spec.Name;
                serialized.FindProperty("defaultTitle").stringValue = spec.Title;
                serialized.FindProperty("defaultDescription").stringValue = spec.Description;
                serialized.FindProperty("sellPrice").intValue = spec.Price;
                serialized.FindProperty("weight").floatValue = spec.Weight;
                // Materials come in by the armful, so they have to stack or the first tree
                // fills the pack.
                serialized.FindProperty("maxStack").intValue = 999;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(item);
            }
        }

        private static Harvestable BuildHarvestable(NodeSpec spec)
        {
            var harvestable = Create<Harvestable>($"{HarvestableDir}/{spec.Name}.asset");
            var serialized = new SerializedObject(harvestable);
            serialized.FindProperty("id").stringValue = spec.Name;
            serialized.FindProperty("defaultTitle").stringValue = spec.Title;
            serialized.FindProperty("expPerDamage").intValue = spec.ExpPerDamage;

            BaseItem yield = AssetDatabase.LoadAssetAtPath<BaseItem>($"{ItemDir}/{spec.Yield}.asset");
            if (yield == null)
                Debug.LogError($"[{nameof(DemoHarvestBuilder)}] No item \"{spec.Yield}\" for {spec.Name}.");

            SerializedProperty tools = serialized.FindProperty("harvestEffectivenesses");
            tools.arraySize = spec.Tools.Length;
            for (int i = 0; i < spec.Tools.Length; ++i)
            {
                (string tool, float effectiveness, float perDamage) = spec.Tools[i];
                SerializedProperty entry = tools.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("weaponType").objectReferenceValue =
                    AssetDatabase.LoadAssetAtPath<WeaponType>($"{WeaponTypeDir}/{tool}.asset");
                entry.FindPropertyRelative("damageEffectiveness").floatValue = effectiveness;
                SerializedProperty items = entry.FindPropertyRelative("items");
                items.arraySize = 1;
                SerializedProperty drop = items.GetArrayElementAtIndex(0);
                drop.FindPropertyRelative("item").objectReferenceValue = yield;
                drop.FindPropertyRelative("amountPerDamage").floatValue = perDamage;
                drop.FindPropertyRelative("randomWeight").intValue = 100;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(harvestable);
            return harvestable;
        }

        /// <summary>
        /// Builds the entity a spawn area puts on the ground.
        ///
        /// The model hangs off a child so the entity's own transform stays at the node's
        /// foot, which is where the spawn area grounds it and where its collider is
        /// measured from. The collider is a capsule rather than the model's mesh: it is
        /// what the player's swing has to find, and a trunk-shaped capsule is both
        /// cheaper and easier to hit than the silhouette of a tree with branches.
        /// </summary>
        private static bool BuildEntity(NodeSpec spec, string modelName, Harvestable harvestable)
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>($"{NatureDir}/{modelName}.prefab");
            if (model == null)
            {
                Debug.LogError($"[{nameof(DemoHarvestBuilder)}] No model \"{modelName}\" for {spec.Name}.");
                return false;
            }

            var root = new GameObject($"Harvest_{modelName}");
            root.layer = HarvestableLayer;

            var placed = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
            placed.transform.localPosition = Vector3.zero;
            placed.transform.localScale = Vector3.one * spec.ModelScale;
            foreach (Transform child in placed.GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = HarvestableLayer;
            // The pack ships mesh colliders on some of these models, and they have to go.
            // The kit resolves a hit by asking the collider's own GameObject for its
            // entity — GetComponent, with no walk up the parents — so a swing that landed
            // on the model's collider instead of the root's would find nothing at all,
            // and the node would simply refuse to be harvested.
            foreach (Collider spare in placed.GetComponentsInChildren<Collider>(true))
                Object.DestroyImmediate(spare, true);

            // Measured from the model, so a sapling and an eighteen-metre snag each get
            // collision that fits them.
            Bounds bounds = MeasureModel(model);
            float radius = Mathf.Clamp(
                Mathf.Min(bounds.size.x, bounds.size.z) * spec.ModelScale * spec.ColliderWidthShare,
                spec.MinColliderRadius, spec.MaxColliderRadius);
            float height = Mathf.Max(spec.MinColliderHeight, bounds.size.y * spec.ModelScale * spec.ColliderHeightShare);
            var body = root.AddComponent<CapsuleCollider>();
            body.radius = radius;
            body.height = height;
            body.center = new Vector3(0f, height * 0.5f, 0f);

            // Spawn areas take a node's size from its prefab, so without this every tree
            // of a kind is exactly as tall as every other one of that kind.
            var variety = root.AddComponent<MultiplayerARPG.Demo.DemoNodeVariety>();
            variety.smallest = spec.Smallest;
            variety.largest = spec.Largest;

            var entity = root.AddComponent<HarvestableEntity>();
            var serialized = new SerializedObject(entity);
            serialized.FindProperty("maxHp").intValue = spec.MaxHp;
            serialized.FindProperty("harvestable").objectReferenceValue = harvestable;
            // Straight into the pack. Dropping a bag of timber on the floor for the player
            // to walk over is a second step that teaches nothing.
            serialized.FindProperty("collectType").enumValueIndex = (int)HarvestableCollectType.CollectToInventory;
            serialized.FindProperty("colliderDetectionRadius").floatValue = spec.DetectionRadius;
            serialized.FindProperty("destroyDelay").floatValue = 0f;
            serialized.FindProperty("destroyRespawnDelay").floatValue = spec.Respawn;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, $"{EntityDir}/Harvest_{modelName}.prefab");
            Object.DestroyImmediate(root);
            return true;
        }

        /// <summary>The size of a model, before anything is done to it.</summary>
        private static Bounds MeasureModel(GameObject model)
        {
            var bounds = new Bounds();
            bool any = false;
            foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null)
                    continue;
                Bounds mesh = filter.sharedMesh.bounds;
                if (any)
                    bounds.Encapsulate(mesh);
                else
                {
                    bounds = mesh;
                    any = true;
                }
            }
            return bounds;
        }

        // ---- what the rest of the demo needs from here -----------------------

        /// <summary>The harvestable definitions, for the game database.</summary>
        public static List<Object> AllHarvestables()
        {
            var found = new List<Object>();
            foreach (string guid in AssetDatabase.FindAssets("t:Harvestable", new[] { HarvestableDir }))
                found.Add(AssetDatabase.LoadAssetAtPath<Object>(AssetDatabase.GUIDToAssetPath(guid)));
            return found;
        }

        /// <summary>The node entity built from one model, for the scene's spawn areas.</summary>
        public static HarvestableEntity Entity(string modelName)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{EntityDir}/Harvest_{modelName}.prefab");
            return prefab == null ? null : prefab.GetComponent<HarvestableEntity>();
        }

        /// <summary>Every model a kind of node is built from.</summary>
        public static string[] ModelsFor(string node)
        {
            foreach (NodeSpec spec in Nodes)
            {
                if (spec.Name == node)
                    return spec.Models;
            }
            return new string[0];
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
