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
            /// <summary>
            /// The icon's file name, for a material whose icon this builder binds. Null for
            /// the first three, whose painted icons were bound by hand and are left alone.
            /// </summary>
            public string Icon;
        }

        private static readonly MaterialSpec[] Materials =
        {
            new MaterialSpec { Name = "Timber", Title = "Timber", Price = 4, Weight = 2f,
                Description = "Rough cut lengths, still smelling of sap." },
            new MaterialSpec { Name = "Stone", Title = "Stone", Price = 3, Weight = 3f,
                Description = "Broken from an outcrop, heavy in the hand." },
            new MaterialSpec { Name = "Browncap", Title = "Browncap Mushroom", Price = 6, Weight = 0.2f,
                Description = "Picked from the leaf litter under the pines." },
            // Iron (2026-09-25): the forge used to make iron swords out of stone. Ore is dug
            // from the veins in the rough ground away from the village and smelted into
            // ingots at the forge (DemoProgressionBuilder), and the iron gear is made from
            // ingots. The ingot is listed here, with the ore, because this is the builder
            // that owns the island's raw materials; nothing harvests one directly.
            new MaterialSpec { Name = "IronOre", Title = "Iron Ore", Price = 8, Weight = 3f, Icon = "IronOre",
                Description = "Rust-streaked rock from a vein in the hills. Smelt it at the forge." },
            new MaterialSpec { Name = "IronIngot", Title = "Iron Ingot", Price = 24, Weight = 2f, Icon = "IronIngot",
                Description = "A bar of smelted iron, ready for the anvil." },
        };

        /// <summary>
        /// One kind of node: what it is made of, what it takes to work it, and what it
        /// gives up.
        ///
        /// **One tool per kind of node** (user, 2026-09-25): trees give only to an axe, rock
        /// and ore only to a pick. Mushrooms are the exception and give to any blade or staff.
        /// Every character starts with a woodcutter's axe and a miner's pick in the pack for
        /// this reason, and Marek sells spares (DemoItemBuilder, DemoDatabaseWiring).
        ///
        /// A wrong tool is not refused, it just gets nothing, which is the kit's own
        /// behaviour for a weapon type missing from the list: the swing lands, plays its
        /// impact and wears the weapon's durability (`OnHarvestableReceivedDamage`), but
        /// `HarvestableEntity.ApplyReceiveDamage` finds no effectiveness for it, so it deals
        /// nothing and yields nothing. Hitting a tree with a sword costs the sword and gives
        /// no timber.
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
            /// <summary>
            /// Tool name to how well it works and how much it yields per point of damage.
            ///
            /// **Shaped by how the kit pays out** (retuned 2026-09-25, when harvesting was found
            /// never to have worked): per hit, rounded down - `(int)(amountPerDamage * damage)`
            /// in `HarvestableEntity.ApplyReceiveDamage` - with nothing carried between swings.
            /// The first tuning read `PerDamage` as a rate over the whole node (0.09 timber a
            /// point on a 60-point tree, "about five logs"), which per ten-point swing is 0.9,
            /// so every swing gave nothing. (No swing did any damage either, until the weapons
            /// got a harvest damage - see DemoItemBuilder.WriteHarvestSwing.)
            ///
            /// So a swing is exactly ten, and ten x Effectiveness x PerDamage is a whole number:
            /// an axe on a tree 10 x 1 x 0.2 = 2 a swing. The node's hit points then set its
            /// total: MaxHp x PerDamage. (Should a node ever take two tools again at different
            /// strengths, give them the same PerDamage and keep the weaker one's ten x
            /// Effectiveness x PerDamage whole - then both take the same total and the weaker
            /// needs more swings, rather than the rounding deciding.)
            /// </summary>
            public (string Tool, float Effectiveness, float PerDamage)[] Tools;
            /// <summary>
            /// Put in front of the model's name in the entity's prefab name. Empty for the
            /// first three kinds, whose prefabs are named after the model alone; a kind that
            /// reuses another's models needs one, or the two would write the same prefab.
            /// </summary>
            public string Prefix;
            /// <summary>Makes the node an ore vein: its rock darkened and seams of ore set into it. See <see cref="Veined"/>.</summary>
            public bool OreVein;
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
                // Eight timber a tree: four swings of an axe at two apiece. See NodeSpec.Tools
                // for why the numbers are these shapes, and NodeSpec for the one-tool rule.
                Yield = "Timber", MaxHp = 40, ExpPerDamage = 1, Respawn = 45f,
                DetectionRadius = 3f,
                ColliderWidthShare = 0.16f, ColliderHeightShare = 0.55f,
                MinColliderRadius = 0.35f, MinColliderHeight = 2.5f, MaxColliderRadius = 0.85f, Smallest = 0.82f, Largest = 1.3f,
                Tools = new[] { ("Axe", 1f, 0.2f) },
            },
            new NodeSpec
            {
                Name = "Boulder", Title = "Boulder", ModelScale = 0.75f,
                Models = new[] { "Rock_Medium_1", "Rock_Medium_2", "Rock_Medium_3" },
                // Ten stone a boulder: five swings of a pick.
                Yield = "Stone", MaxHp = 50, ExpPerDamage = 1, Respawn = 60f,
                DetectionRadius = 2.5f,
                ColliderWidthShare = 0.42f, ColliderHeightShare = 0.85f,
                MinColliderRadius = 0.8f, MinColliderHeight = 1.2f, MaxColliderRadius = 1.6f, Smallest = 0.7f, Largest = 1.45f,
                Tools = new[] { ("Pickaxe", 1f, 0.2f) },
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
                // One mushroom a pick, with any blade, tool or staff in hand - the one node with
                // no tool rule. (Not a bow: a missile is not a hand.)
                Tools = new[] { ("Axe", 1f, 0.1f), ("Pickaxe", 1f, 0.1f), ("Sword", 1f, 0.1f), ("Staff", 1f, 0.1f) },
            },
            new NodeSpec
            {
                // The boulders' own rocks, so a vein sits among the outcrops as one of them,
                // told apart by its colour and the seams in it. Tougher than a boulder and
                // slower to give, and slower back: the ore is the scarce thing and a vein is
                // somewhere to go, not something passed on the way.
                Name = "IronVein", Title = "Iron Vein", ModelScale = 0.75f, Prefix = "IronVein_", OreVein = true,
                Models = new[] { "Rock_Medium_1", "Rock_Medium_2", "Rock_Medium_3" },
                // Six ore a vein, one a swing of a pick: slower going than a boulder, and three
                // ingots' worth - most of a helm, half a longsword.
                Yield = "IronOre", MaxHp = 60, ExpPerDamage = 2, Respawn = 120f,
                DetectionRadius = 2.5f,
                ColliderWidthShare = 0.42f, ColliderHeightShare = 0.85f,
                MinColliderRadius = 0.8f, MinColliderHeight = 1.2f, MaxColliderRadius = 1.6f, Smallest = 0.8f, Largest = 1.25f,
                Tools = new[] { ("Pickaxe", 1f, 0.1f) },
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
                if (!string.IsNullOrEmpty(spec.Icon))
                {
                    DemoMaterialIcons.EnsureStandIn(spec.Icon);
                    DemoItemBuilder.AdoptItemIcon(serialized, spec.Icon);
                }
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

            var root = new GameObject(EntityName(spec, modelName));
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
            if (spec.OreVein)
                Veined(placed, modelName);

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
            var variety = root.AddComponent<MultiplayerARPG.NodeVariety>();
            variety.smallest = spec.Smallest;
            variety.largest = spec.Largest;
            // Boulders and veins lie on the slope they stand on; trees and mushrooms stay upright
            // (NodeVariety sinks every node onto the ground either way).
            variety.tiltToGround = LiesOnSlope(modelName, spec) ? 0.9f : 0f;

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
            AddAimPoint(root, entity, height);
            // The tool's impact sound, by the node's kind (trees chop, rocks and veins ring).
            DemoAudioWiring.WireHarvestNode(root);
            // And its chips, dust and falling leaves (a no-op until Build Harvest Effects has run).
            DemoSkillEffectBuilder.WireHarvestEffectsOn(root);

            PrefabUtility.SaveAsPrefabAsset(root, $"{EntityDir}/{EntityName(spec, modelName)}.prefab");
            Object.DestroyImmediate(root);
            return true;
        }

        /// <summary>Whether a node is a rock or a vein, which lean to lie on a slope where a tree stands up.</summary>
        private static bool LiesOnSlope(string modelName, NodeSpec spec)
        {
            return spec.OreVein || modelName.StartsWith("Rock");
        }

        /// <summary>The height, above a node's foot, that a swing is aimed at.</summary>
        internal const float AimHeight = 1.2f;

        /// <summary>
        /// Gives the node an aim point at trunk height instead of its foot.
        ///
        /// A damageable entity's aim point defaults to its own transform, and a node's
        /// transform is on the ground. A melee swing checks for an obstacle on the line from
        /// the weapon to the aim point, and the terrain is one: close enough to a tree - which
        /// is exactly where the controller walks the character to - that line reaches the
        /// ground at the tree's foot, so the terrain blocks every swing before it can be
        /// reported to the server. In the editor's single-player mode it was masked by the
        /// range (standing a little farther back the line falls short of the ground); in the
        /// MMO flow the character stood 1.4 m from the tree and nothing ever landed (2026-10-03).
        /// </summary>
        internal static void AddAimPoint(GameObject root, HarvestableEntity entity, float colliderHeight)
        {
            Transform aim = root.transform.Find("Aim");
            if (aim == null)
            {
                aim = new GameObject("Aim").transform;
                aim.SetParent(root.transform, false);
            }
            aim.localPosition = new Vector3(0f, Mathf.Min(colliderHeight * 0.5f, AimHeight), 0f);
            var so = new SerializedObject(entity);
            so.FindProperty("opponentAimTransform").objectReferenceValue = aim;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static string EntityName(NodeSpec spec, string modelName)
        {
            return $"Harvest_{spec.Prefix}{modelName}";
        }

        private const string MaterialDir = "Assets/OpenMMORPG/Demo/Materials";

        /// <summary>
        /// Turns one of the boulders' rocks into an ore vein.
        ///
        /// The packs have no ore, so a vein is made from what they do have: the rock itself,
        /// its colour pulled down to a darker, redder stone, with seams of ore laid into its
        /// faces - the pack's flat path pebbles, turned to lie along the rock's surface and
        /// half sunk into it, in a dull iron sheen and a rust. From a distance it reads as a
        /// boulder of a different stone; close to, as rock with metal in it.
        ///
        /// The seams are placed on the rock's own vertices, chosen by a generator seeded from
        /// the model's name, so every build of the same rock puts them in the same places. Only
        /// faces that look up or out are used: a seam on the underside is buried in the ground.
        /// </summary>
        internal static void Veined(GameObject rock, string modelName)
        {
            Material stone = VeinMaterial("IronVein_Rock", null);
            Material iron = VeinMaterial("IronVein_Ore", new Color(0.40f, 0.39f, 0.40f), 0.85f, 0.62f);
            Material rust = VeinMaterial("IronVein_Rust", new Color(0.58f, 0.27f, 0.10f), 0.15f, 0.30f);

            foreach (Renderer renderer in rock.GetComponentsInChildren<Renderer>(true))
            {
                var slots = renderer.sharedMaterials;
                for (int i = 0; i < slots.Length; ++i)
                    slots[i] = stone;
                renderer.sharedMaterials = slots;
            }

            MeshFilter filter = rock.GetComponentInChildren<MeshFilter>();
            GameObject seamModel = AssetDatabase.LoadAssetAtPath<GameObject>($"{NatureDir}/Pebble_Round_1.prefab");
            // Round for the rust as well: the square pebble read as a tile stuck on the rock.
            GameObject chipModel = AssetDatabase.LoadAssetAtPath<GameObject>($"{NatureDir}/Pebble_Round_2.prefab");
            if (filter == null || filter.sharedMesh == null || seamModel == null || chipModel == null)
            {
                Debug.LogWarning($"[{nameof(DemoHarvestBuilder)}] Could not vein {modelName}; it will look like a plain boulder.");
                return;
            }

            Mesh mesh = filter.sharedMesh;
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            float bottom = mesh.bounds.min.y, height = mesh.bounds.size.y;
            // Placed on the rock's faces, not its vertices: on a low-poly rock the vertices
            // are its corners, and a flat seam laid on a corner sticks out into the air on
            // both sides of it. Only faces big enough to hold one, looking up or out, and
            // above the bottom fifth of the rock.
            var candidates = new List<int>();
            for (int t = 0; t < triangles.Length; t += 3)
            {
                Vector3 a = vertices[triangles[t]], b = vertices[triangles[t + 1]], c = vertices[triangles[t + 2]];
                Vector3 cross = Vector3.Cross(b - a, c - a);
                float area = cross.magnitude * 0.5f;
                Vector3 facing = cross.normalized;
                Vector3 middle = (a + b + c) / 3f;
                if (area > 0.12f && facing.y > -0.15f && middle.y > bottom + height * 0.2f)
                    candidates.Add(t);
            }

            var random = new System.Random(StableHash(modelName));
            var used = new List<Vector3>();
            const int Seams = 12;
            for (int attempt = 0; attempt < 200 && used.Count < Seams && candidates.Count > 0; ++attempt)
            {
                int tri = candidates[random.Next(candidates.Count)];
                Vector3 p0 = vertices[triangles[tri]], p1 = vertices[triangles[tri + 1]], p2 = vertices[triangles[tri + 2]];
                // Towards the middle of the face, so the seam does not hang over its edge.
                float u = 0.25f + (float)random.NextDouble() * 0.5f, v = (float)random.NextDouble() * (1f - u) * 0.5f;
                Vector3 point = p0 + (p1 - p0) * u + (p2 - p0) * v;
                point = Vector3.Lerp(point, (p0 + p1 + p2) / 3f, 0.5f);
                Vector3 faceNormal = Vector3.Cross(p1 - p0, p2 - p0).normalized;
                // Spread over the rock rather than clustered on one face.
                bool crowded = false;
                foreach (Vector3 other in used)
                {
                    if ((other - point).sqrMagnitude < 0.45f * 0.45f)
                        crowded = true;
                }
                if (crowded)
                    continue;
                used.Add(point);

                bool isRust = used.Count % 2 == 0;
                GameObject seam = (GameObject)PrefabUtility.InstantiatePrefab(isRust ? chipModel : seamModel, filter.transform);
                // The pebble's thin axis is its Y; laid flat on the face and sunk by half its
                // thickness, so it reads as ore showing through the rock, not a chip stuck on.
                seam.transform.localRotation = Quaternion.FromToRotation(Vector3.up, faceNormal) *
                                               Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f);
                float size = 0.95f + (float)random.NextDouble() * 0.6f;
                seam.transform.localScale = new Vector3(size, size * 0.8f, size);
                seam.transform.localPosition = point - faceNormal * 0.025f * size;
                foreach (Collider spare in seam.GetComponentsInChildren<Collider>(true))
                    Object.DestroyImmediate(spare, true);
                foreach (Transform child in seam.GetComponentsInChildren<Transform>(true))
                    child.gameObject.layer = HarvestableLayer;
                foreach (Renderer renderer in seam.GetComponentsInChildren<Renderer>(true))
                {
                    var slots = renderer.sharedMaterials;
                    for (int i = 0; i < slots.Length; ++i)
                        slots[i] = isRust ? rust : iron;
                    renderer.sharedMaterials = slots;
                }
            }
        }

        /// <summary>
        /// One of the vein's materials, kept as an asset and rewritten each build.
        /// With no colour given it is the pack's rock material, darkened and reddened;
        /// otherwise a plain lit metal of that colour.
        /// </summary>
        internal static Material VeinMaterial(string name, Color? colour, float metallic = 0f, float smoothness = 0f)
        {
            DemoItemBuilder.EnsureFolder(MaterialDir);
            string path = $"{MaterialDir}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (colour == null)
            {
                GameObject rock = AssetDatabase.LoadAssetAtPath<GameObject>($"{NatureDir}/Rock_Medium_1.prefab");
                Material source = rock.GetComponentInChildren<Renderer>().sharedMaterial;
                if (material == null)
                {
                    material = new Material(source) { name = name };
                    AssetDatabase.CreateAsset(material, path);
                }
                else
                {
                    material.CopyPropertiesFromMaterial(source);
                }
                // Warmer and a little darker than the grey-green boulders, and no more: at
                // (0.62, 0.50, 0.44) the vein read as a lump of coal.
                material.SetColor("_BaseColor", new Color(0.92f, 0.72f, 0.60f));
            }
            else
            {
                if (material == null)
                {
                    material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
                    AssetDatabase.CreateAsset(material, path);
                }
                material.SetColor("_BaseColor", colour.Value);
                material.SetFloat("_Metallic", metallic);
                material.SetFloat("_Smoothness", smoothness);
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>A hash that is the same on every run, unlike string.GetHashCode.</summary>
        private static int StableHash(string text)
        {
            unchecked
            {
                int hash = 23;
                foreach (char c in text)
                    hash = hash * 31 + c;
                return hash;
            }
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

        /// <summary>The node entity of one kind built from one model - for kinds that share models, like the iron veins and the boulders.</summary>
        public static HarvestableEntity Entity(string node, string modelName)
        {
            foreach (NodeSpec spec in Nodes)
            {
                if (spec.Name != node)
                    continue;
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{EntityDir}/{EntityName(spec, modelName)}.prefab");
                return prefab == null ? null : prefab.GetComponent<HarvestableEntity>();
            }
            return null;
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
