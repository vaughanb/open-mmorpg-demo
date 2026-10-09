using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// A house a player builds for themselves, one piece at a time: foundation, walls, a
    /// doorway and its door, a roof, and a strongbox and a carpenter's bench to put in it.
    ///
    /// The campfire (DemoBuildingBuilder) showed that a player can put a building down. It
    /// never touched the part of the kit that makes building a *system*: pieces that snap to
    /// each other through sockets (`BuildingArea`), who may build on whose foundation, a
    /// rule for what may go where (`BaseBuildConditionForBuildingArea`), doors that lock with
    /// a code (`DoorEntity`), a locked chest, a workbench the player owns, and a foundation
    /// whose walls come down with it. This is all of those, in the smallest set that makes a
    /// house.
    ///
    /// **The pieces are the village's own modules** - the Quaternius walls, floors, doors and
    /// roofs the houses on the green are built from - so a player's house reads as belonging
    /// to the island. They sit on the same 2m module and 3m storey. A foundation is two
    /// modules square (4m), because that is the smallest square the pack has a roof for.
    ///
    /// **How the pieces fit** (all measured in the piece's own frame):
    ///
    /// - A foundation's pivot is the ground at its middle. Its floor is <see cref="FloorTop"/>
    ///   above that, on a brick footing that runs <see cref="FootingDepth"/> into the ground
    ///   to hide uneven terrain. It offers sockets for four more foundations beside it, two
    ///   walls along each edge, a roof and furniture on the floor.
    /// - A wall's pivot is the bottom of its edge line with +Z outward, exactly as the village
    ///   places its walls (<see cref="DemoVillageBuilder.BuildHouse"/>). A doorway wall offers
    ///   a socket for a door.
    /// - A roof's pivot is the middle of the wall tops.
    ///
    /// **Every piece is three kinds of child**, because the kit uses colliders for three
    /// different things and they want different shapes:
    ///
    /// - `Hit` - one trigger box with a `BuildingMaterial`. It is what a click or an attack
    ///   lands on, and while the piece is still a ghost it is the volume the kit tests for
    ///   overlaps. Deliberately a little smaller than the piece, so neighbours that touch do
    ///   not count as overlapping.
    /// - `Solid` - box colliders for walking into. **Boxes, never the models' mesh
    ///   colliders**: the kit turns every collider on a ghost into a trigger, and Unity does
    ///   not support triggers on concave meshes. The models' colliders are stripped.
    /// - sockets - trigger boxes with a `BuildingArea`, which the aim ray finds and the new
    ///   piece snaps to.
    ///
    /// The ghost is drawn by <see cref="BuildingGhost"/> rather than the kit's own
    /// `BuildingMaterial` colouring, which cannot handle a piece made of several models.
    /// </summary>
    public static class DemoHomesteadBuilder
    {
        private const string GameDataDir = "Assets/OpenMMORPG/Demo/GameData";
        private const string ItemDir = GameDataDir + "/Resources/Items";
        private const string FormulaDir = GameDataDir + "/Resources/ItemCraftFormulas";
        private const string PrefabDir = "Assets/OpenMMORPG/Demo/Prefabs/GamePlay/Buildings/Homestead";
        private const string MaterialDir = "Assets/OpenMMORPG/Demo/Materials";
        private const string IconDir = "Assets/OpenMMORPG/Demo/Textures/Icons/Items";

        public const string FoundationType = "Foundation";
        public const string WallType = "Wall";
        public const string DoorType = "Door";
        public const string RoofType = "Roof";
        public const string FurnitureType = "Furniture";

        /// <summary>
        /// How high the floor stands above the ground at the foundation's pivot.
        ///
        /// Low enough to step onto: the player's `CharacterController` climbs 0.3m, and a
        /// foundation any taller would need steps, which is a piece this set does not have.
        /// The price is that a foundation needs nearly level ground - the kit refuses a ghost
        /// whose footprint cuts into the terrain, and the footprint is tested from just under
        /// the floor - which is why the homestead plot is levelled (DemoIslandBuilder).
        /// </summary>
        public const float FloorTop = 0.25f;

        /// <summary>How far the brick footing runs below the pivot, to hide ground that falls away.</summary>
        private const float FootingDepth = 0.75f;

        /// <summary>Half a foundation's width: two 2m modules either side of the middle.</summary>
        private const float Half = DemoVillageBuilder.Cell;

        /// <summary>The wall module's real height; a roof sits at <see cref="DemoVillageBuilder.WallHeight"/> and overlaps it.</summary>
        private const float WallModuleHeight = 3.12f;

        /// <summary>Four sides, each turned so the module's +Z faces outward, as the village places them.</summary>
        private static readonly float[] SideYaws = { 0f, 90f, 180f, 270f };
        private static readonly string[] SideNames = { "North", "East", "South", "West" };

        private struct Kit
        {
            public string Name;
            public string Title;
            public string Description;
            public int Price;
            public string Piece;
            /// <summary>What the carpenter's bench asks for it. Empty for a kit the bench does not make.</summary>
            public string[] Materials;
            public int[] Counts;
        }

        /// <summary>
        /// The kits, in the order the carpenter lays them out.
        ///
        /// A house of one foundation takes a foundation, eight walls (one a doorway), a door and
        /// a roof - 24 timber and 13 stone from the bench, or some 260 gold from Oswin.
        /// That is an afternoon's gathering rather than an evening's, which is the right size
        /// for a demo: long enough to see the gathering loop pay off, short enough to finish.
        ///
        /// **Wood and stone only** (user, 2026-09-25): the door and the strongbox used to want
        /// a leather each for hinges and straps, which sent a builder off hunting deer for two
        /// pieces out of eleven. The whole house now comes from chopping trees and breaking
        /// rocks, the two things a player does on the way to the plot anyway.
        /// </summary>
        private static readonly Kit[] Kits =
        {
            new Kit
            {
                Name = "FoundationKit", Title = "Foundation Kit", Piece = "DemoHomesteadFoundation", Price = 40,
                Description = "Boards and a brick footing. Set it on level ground; walls, a roof and furniture all go on top, and more foundations beside it.",
                Materials = new[] { "Stone", "Timber" }, Counts = new[] { 4, 1 },
            },
            new Kit
            {
                Name = "WallKit", Title = "Wall Kit", Piece = "DemoHomesteadWall", Price = 20,
                Description = "A plastered wall section. Aim it at the edge of one of your foundations.",
                Materials = new[] { "Timber", "Stone" }, Counts = new[] { 2, 1 },
            },
            new Kit
            {
                Name = "WindowWallKit", Title = "Window Wall Kit", Piece = "DemoHomesteadWindowWall", Price = 25,
                Description = "A wall section with a glazed window. Goes wherever a wall goes.",
                Materials = new[] { "Timber", "Stone" }, Counts = new[] { 2, 1 },
            },
            new Kit
            {
                Name = "DoorwayKit", Title = "Doorway Kit", Piece = "DemoHomesteadDoorway", Price = 20,
                Description = "A wall section with an arched opening. Hang a door in it.",
                Materials = new[] { "Timber", "Stone" }, Counts = new[] { 2, 1 },
            },
            new Kit
            {
                Name = "DoorKit", Title = "Door Kit", Piece = "DemoHomesteadDoor", Price = 25,
                Description = "A door in its frame, for a doorway. Hold the use key on it to set a code and lock it.",
                Materials = new[] { "Timber" }, Counts = new[] { 3 },
            },
            new Kit
            {
                Name = "RoofKit", Title = "Roof Kit", Piece = "DemoHomesteadRoof", Price = 40,
                Description = "A tiled roof for one foundation. It needs a wall standing on every side before it will go up.",
                Materials = new[] { "Timber", "Stone" }, Counts = new[] { 4, 1 },
            },
            new Kit
            {
                Name = "StrongboxKit", Title = "Strongbox Kit", Piece = "DemoHomesteadStrongbox", Price = 45,
                Description = "A chest that locks. Set it on your floor, give it a code, and only those who know it can open it.",
                Materials = new[] { "Timber", "Stone" }, Counts = new[] { 3, 2 },
            },
            new Kit
            {
                Name = "CarpentersBenchKit", Title = "Carpenter's Bench Kit", Piece = "DemoHomesteadBench", Price = 60,
                Description = "Set it down outside the village and it will make every other piece of a house from timber and stone.",
                // Not made at a bench: it is the bench. See WriteBenchFormula.
                Materials = new string[0], Counts = new int[0],
            },
        };

        /// <summary>The bench kit's own recipe, made anywhere from the HUD's Craft window.</summary>
        private static readonly string[] BenchKitMaterials = { "Timber", "Stone" };
        private static readonly int[] BenchKitCounts = { 5, 2 };

        /// <summary>The kits by item name, for the carpenter's shop.</summary>
        public static string[] KitNames()
        {
            var names = new string[Kits.Length];
            for (int i = 0; i < Kits.Length; ++i)
                names[i] = Kits[i].Name;
            return names;
        }

        // ---- menu ----------------------------------------------------------------

        [MenuItem("Open MMORPG/Demo/Build Homestead Pieces", priority = 155)]
        public static void Build()
        {
            DemoItemBuilder.EnsureFolder(PrefabDir);
            DemoItemBuilder.EnsureFolder(MaterialDir);
            DemoItemBuilder.EnsureFolder(FormulaDir);

            Material canBuild = GhostMaterial("BuildGhost_CanBuild", new Color(0.35f, 1f, 0.45f, 0.35f));
            Material cannotBuild = GhostMaterial("BuildGhost_CannotBuild", new Color(1f, 0.3f, 0.25f, 0.35f));

            var pieces = new Dictionary<string, BuildingEntity>
            {
                { "DemoHomesteadFoundation", BuildFoundation(canBuild, cannotBuild) },
                { "DemoHomesteadWall", BuildWall("DemoHomesteadWall", "Wall", "Wall_Plaster_Straight", null, false, canBuild, cannotBuild) },
                { "DemoHomesteadWindowWall", BuildWall("DemoHomesteadWindowWall", "Window Wall", "Wall_Plaster_Window_Wide_Round", "Window_Wide_Round1", false, canBuild, cannotBuild) },
                { "DemoHomesteadDoorway", BuildWall("DemoHomesteadDoorway", "Doorway", "Wall_Plaster_Door_Round", null, true, canBuild, cannotBuild) },
                { "DemoHomesteadDoor", BuildDoor(canBuild, cannotBuild) },
                { "DemoHomesteadRoof", BuildRoof(canBuild, cannotBuild) },
                { "DemoHomesteadStrongbox", BuildStrongbox(canBuild, cannotBuild) },
                { "DemoHomesteadBench", BuildBench(canBuild, cannotBuild) },
            };

            var kits = new Dictionary<string, BuildingItem>();
            foreach (Kit kit in Kits)
            {
                if (!pieces.TryGetValue(kit.Piece, out BuildingEntity piece) || piece == null)
                {
                    Debug.LogError($"[{nameof(DemoHomesteadBuilder)}] {kit.Piece} did not build; no {kit.Name}.");
                    continue;
                }
                kits[kit.Name] = BuildKitItem(kit, piece);
            }

            WriteBenchRecipes(pieces["DemoHomesteadBench"] as WorkbenchEntity, kits);
            WriteBenchFormula(kits);
            DemoControllerBuilder.ConfigureBuildPlacementInPlace();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[{nameof(DemoHomesteadBuilder)}] Built {pieces.Count} homestead pieces and {kits.Count} kits. " +
                      "Next: Wire Game Database (registers the kits), Build NPCs And Quests (the carpenter), " +
                      "Level Homestead Plot, then Collect Demo Art.");
        }

        // ---- foundation ---------------------------------------------------------

        private static BuildingEntity BuildFoundation(Material canBuild, Material cannotBuild)
        {
            var root = new GameObject("DemoHomesteadFoundation");
            try
            {
                var model = Child(root.transform, "Model");

                // Four boards, their tops at the floor line. The module is 2cm thick about
                // its own origin, so it goes a centimetre down to put its top there.
                for (int x = -1; x <= 1; x += 2)
                    for (int z = -1; z <= 1; z += 2)
                        Village("Floor_WoodDark", model, new Vector3(x, FloorTop - 0.01f, z), 0f);

                // The footing: the village's brick wall module, squashed to a metre, run
                // round the edge the way a house's walls are, with a quoin at the end of
                // each run. The same arrangement as a wall piece, so a wall stands flush on it.
                float footing = FloorTop + FootingDepth;
                for (int side = 0; side < 4; ++side)
                {
                    for (int u = -1; u <= 1; u += 2)
                    {
                        Quaternion turn = Quaternion.Euler(0f, SideYaws[side], 0f);
                        Vector3 at = turn * new Vector3(u, 0f, Half) + Vector3.down * FootingDepth;
                        GameObject course = Village("Wall_UnevenBrick_Straight", model, at, SideYaws[side]);
                        if (course != null)
                            course.transform.localScale = new Vector3(1f, footing / WallModuleHeight, 1f);
                        GameObject quoin = Village("Corner_Exterior_Brick", model, at + turn * Vector3.right, SideYaws[side] + 180f);
                        if (quoin != null)
                            quoin.transform.localScale = new Vector3(1f, footing / 3.01f, 1f);
                    }
                }

                var solid = Child(root.transform, "Solid");
                Box(solid.gameObject, new Vector3(0f, (FloorTop - FootingDepth) * 0.5f, 0f),
                    new Vector3(Half * 2f, FloorTop + FootingDepth, Half * 2f), false);

                // Only the top of the slab. The kit refuses a ghost whose hit volume cuts
                // into anything solid, terrain included, so the deeper this reaches the
                // flatter the ground has to be.
                Hit(root.transform, new Vector3(0f, FloorTop - 0.1f, 0f), new Vector3(Half * 2f, 0.2f, Half * 2f), false, Vector3.zero);

                var entity = root.AddComponent<BuildingEntity>();
                Configure(entity, new EntitySpec
                {
                    Title = "Foundation", Types = new[] { FoundationType },
                    AnySurface = true, LevelOnly = true, BuildDistance = 7f, MaxHp = 600, BuildLimit = 16,
                    // Not with its neighbour: a foundation built off another's socket is that
                    // one's child to the kit, and a whole row coming down because its first
                    // board was lifted would be a surprise nobody asked for.
                    ChainDestroy = false,
                    Drops = new[] { "Stone", "2" },
                });

                var sockets = Child(root.transform, "Sockets");
                for (int side = 0; side < 4; ++side)
                {
                    // Turned with the grid rather than the side, so a neighbour laid off any
                    // edge lines up with this one's boards.
                    Vector3 at = Quaternion.Euler(0f, SideYaws[side], 0f) * new Vector3(0f, 0f, Half * 2f);
                    Socket(sockets, $"Foundation_{SideNames[side]}", FoundationType, entity, at, 0f,
                        new Vector3(0f, 0.1f, 0f), new Vector3(Half * 2f - 0.2f, 0.8f, Half * 2f - 0.2f), true, false);
                }

                var walls = new BuildingArea[4][];
                for (int side = 0; side < 4; ++side)
                {
                    walls[side] = new BuildingArea[2];
                    for (int i = 0; i < 2; ++i)
                    {
                        int u = i == 0 ? -1 : 1;
                        Vector3 at = Quaternion.Euler(0f, SideYaws[side], 0f) * new Vector3(u, 0f, Half) + Vector3.up * FloorTop;
                        // A pad along the edge of the boards, half on and half off: aim a wall
                        // at the edge of the floor. See Socket for why every socket is flat.
                        walls[side][i] = Socket(sockets, $"Wall_{SideNames[side]}_{i}", WallType, entity, at, SideYaws[side],
                            new Vector3(0f, 0.05f, 0.1f), new Vector3(1.8f, 0.1f, 0.6f), true, false);
                    }
                }

                // A ceiling across the wall tops. Seen from the ground its underside is what
                // the aim ray meets, and that works as well as a top face (see Socket).
                BuildingArea roof = Socket(sockets, "Roof", RoofType, entity,
                    new Vector3(0f, FloorTop + DemoVillageBuilder.WallHeight, 0f), 0f,
                    new Vector3(0f, 0.05f, 0f), new Vector3(Half * 2f - 0.2f, 0.1f, Half * 2f - 0.2f), true, true);
                var condition = roof.gameObject.AddComponent<RequireBuildingsAround>();
                condition.requiredType = WallType;
                condition.sides = new RequireBuildingsAround.Side[4];
                for (int side = 0; side < 4; ++side)
                    condition.sides[side] = new RequireBuildingsAround.Side { areas = walls[side] };
                roof.buildConditions = new BaseBuildConditionForBuildingArea[] { condition };

                // Furniture goes anywhere on the boards, where the player aims, not snapped.
                // Without this a chest aimed at the floor lands on the terrain under it: the
                // aim ray skips anything that belongs to a building unless it is a socket,
                // and carries on down to the ground.
                Socket(sockets, "Floor", FurnitureType, entity, new Vector3(0f, FloorTop, 0f), 0f,
                    new Vector3(0f, -0.05f, 0f), new Vector3(Half * 2f - 0.1f, 0.1f, Half * 2f - 0.1f), false, false);

                AddGhost(root, canBuild, cannotBuild);
                return Save(root, "DemoHomesteadFoundation");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // ---- walls --------------------------------------------------------------

        /// <param name="window">A glazing insert for a windowed module, or null.</param>
        /// <param name="doorway">Whether it is the doorway: open in the middle, and carries a door socket.</param>
        private static BuildingEntity BuildWall(string name, string title, string module, string window, bool doorway,
                                                Material canBuild, Material cannotBuild)
        {
            var root = new GameObject(name);
            try
            {
                var model = Child(root.transform, "Model");
                Village(module, model, Vector3.zero, 0f);
                if (window != null)
                    Village(window, model, Vector3.zero, 0f);
                // The quoin at the wall's +X end. Going round a foundation, every side's +X
                // end is the next corner clockwise, so each corner gets exactly one - from
                // the wall that ends there - and the notch where two runs meet is covered
                // whichever of them was built first. Midway along a side it stands as a
                // pilaster between the two sections.
                Village("Corner_Exterior_Brick", model, Vector3.right, 180f);

                var solid = Child(root.transform, "Solid");
                // The module runs from 0.09 inside the line to 0.31 outside it.
                const float depth = 0.4f, middle = 0.11f;
                if (!doorway)
                {
                    Box(solid.gameObject, new Vector3(0f, WallModuleHeight * 0.5f, middle),
                        new Vector3(2f, WallModuleHeight, depth), false);
                }
                else
                {
                    // Two jambs and the head over the arch. The opening is 1.4m, the door
                    // frame's own width, and 2.56m to the top of the arch.
                    const float opening = 0.7f, head = 2.56f;
                    float jamb = 1f - opening;
                    for (int s = -1; s <= 1; s += 2)
                        Box(solid.gameObject, new Vector3(s * (opening + jamb * 0.5f), WallModuleHeight * 0.5f, middle),
                            new Vector3(jamb, WallModuleHeight, depth), false);
                    Box(solid.gameObject, new Vector3(0f, (head + WallModuleHeight) * 0.5f, middle),
                        new Vector3(opening * 2f, WallModuleHeight - head, depth), false);
                }

                // The hit volume stops 15cm short of each end, so a wall round the corner -
                // whose own end sits in the same square - is not taken for an overlap.
                bool carve = !doorway;
                Hit(root.transform, new Vector3(0f, 1.5f, middle), new Vector3(1.7f, 2.9f, 0.3f), carve, new Vector3(2f, 3f, depth));

                var entity = root.AddComponent<BuildingEntity>();
                Configure(entity, new EntitySpec
                {
                    Title = title, Types = new[] { WallType },
                    BuildDistance = 7f, MaxHp = 400, BuildLimit = 48, ChainDestroy = true,
                    Drops = new[] { "Timber", "1" },
                });

                if (doorway)
                {
                    // Navigation round an opening: a carving obstacle for each jamb, and a
                    // link across the threshold, because the agent's half-metre radius erodes
                    // a metre out of any gap and would close a 1.1m doorway in the navmesh -
                    // the trap the village's doorways hit (DemoVillageBuilder.HangDoor).
                    for (int s = -1; s <= 1; s += 2)
                    {
                        var jamb = Child(root.transform, s < 0 ? "JambLeft" : "JambRight");
                        var obstacle = jamb.gameObject.AddComponent<NavMeshObstacle>();
                        obstacle.shape = NavMeshObstacleShape.Box;
                        obstacle.center = new Vector3(s * 0.85f, 1.5f, middle);
                        obstacle.size = new Vector3(0.3f, 3f, depth);
                        obstacle.carving = true;
                        obstacle.carveOnlyStationary = true;
                        obstacle.enabled = false;
                    }
                    var threshold = Child(root.transform, "Threshold");
                    var link = threshold.gameObject.AddComponent<NavMeshLink>();
                    link.startPoint = new Vector3(0f, 0f, 1.1f);
                    link.endPoint = new Vector3(0f, 0f, -1.1f);
                    link.width = 0.9f;
                    link.bidirectional = true;
                    link.enabled = false;

                    var sockets = Child(root.transform, "Sockets");
                    // The threshold: aim a door at the doorway's floor.
                    Socket(sockets, "Door", DoorType, entity, Vector3.zero, 0f,
                        new Vector3(0f, 0.05f, 0.05f), new Vector3(1.2f, 0.1f, 0.5f), true, false);
                }

                AddGhost(root, canBuild, cannotBuild);
                return Save(root, name);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // ---- door ---------------------------------------------------------------

        private static BuildingEntity BuildDoor(Material canBuild, Material cannotBuild)
        {
            var root = new GameObject("DemoHomesteadDoor");
            try
            {
                var model = Child(root.transform, "Model");
                Village("DoorFrame_Round_WoodDark", model, Vector3.zero, 0f);

                // Hung the way the village hangs its doors: the leaf's pivot is on its hinge
                // with the leaf along +X, so the hinge is set back by the leaf's middle and
                // the leaf fills the opening.
                var hinge = Child(root.transform, "Hinge");
                GameObject leaf = Village("Door_1_Round", hinge, Vector3.zero, 0f);
                float offset = leaf != null ? LeafCentreOffset(leaf) : 0.51f;
                hinge.localPosition = new Vector3(-offset, 0f, 0f);
                if (leaf != null)
                    Box(leaf, new Vector3(offset, 1.19f, 0f), new Vector3(1.12f, 2.32f, 0.1f), false);

                // The frame's posts narrow the doorway's 1.4m opening to the leaf's 1.12m.
                var solid = Child(root.transform, "Solid");
                for (int s = -1; s <= 1; s += 2)
                    Box(solid.gameObject, new Vector3(s * 0.63f, 1.28f, 0f), new Vector3(0.14f, 2.56f, 0.5f), false);

                Hit(root.transform, new Vector3(0f, 1.2f, 0f), new Vector3(1.1f, 2.3f, 0.3f), false, Vector3.zero);

                var entity = root.AddComponent<DoorEntity>();
                Configure(entity, new EntitySpec
                {
                    Title = "Door", Types = new[] { DoorType },
                    BuildDistance = 7f, MaxHp = 300, BuildLimit = 8, ChainDestroy = true, Clickable = true,
                    ActivateDistance = 3f, Drops = new[] { "Timber", "1" },
                });
                var serialized = new SerializedObject(entity);
                serialized.FindProperty("lockable").boolValue = true;
                serialized.FindProperty("passwordLength").intValue = 4;

                var swing = root.AddComponent<MultiplayerARPG.BuildingDoorLeaf>();
                DemoAudioWiring.WireBuildingDoor(swing);
                swing.pivot = hinge;
                // The wall's local -Z faces into the house and a positive turn carries the
                // leaf that way, so it opens inward, as the village's doors do.
                swing.openAngle = 100f;
                Listen(serialized, "onOpen", swing, nameof(MultiplayerARPG.BuildingDoorLeaf.Open));
                Listen(serialized, "onClose", swing, nameof(MultiplayerARPG.BuildingDoorLeaf.Close));
                Listen(serialized, "onInitialOpen", swing, nameof(MultiplayerARPG.BuildingDoorLeaf.OpenAtOnce));
                Listen(serialized, "onInitialClose", swing, nameof(MultiplayerARPG.BuildingDoorLeaf.CloseAtOnce));
                serialized.ApplyModifiedPropertiesWithoutUndo();

                AddGhost(root, canBuild, cannotBuild);
                return Save(root, "DemoHomesteadDoor");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// How far the leaf's middle sits from its hinge, along the leaf, in its own root's
        /// frame - measured, because the leaf is not modelled at exactly half its width from
        /// its pivot. Same measurement as the village's doors.
        /// </summary>
        private static float LeafCentreOffset(GameObject leaf)
        {
            float min = float.MaxValue, max = float.MinValue;
            foreach (MeshFilter filter in leaf.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null)
                    continue;
                Bounds bounds = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; ++i)
                {
                    var corner = new Vector3(
                        (i & 1) == 0 ? bounds.min.x : bounds.max.x,
                        (i & 2) == 0 ? bounds.min.y : bounds.max.y,
                        (i & 4) == 0 ? bounds.min.z : bounds.max.z);
                    float x = leaf.transform.InverseTransformPoint(filter.transform.TransformPoint(corner)).x;
                    min = Mathf.Min(min, x);
                    max = Mathf.Max(max, x);
                }
            }
            return min > max ? 0f : (min + max) * 0.5f;
        }

        // ---- roof ---------------------------------------------------------------

        private static BuildingEntity BuildRoof(Material canBuild, Material cannotBuild)
        {
            var root = new GameObject("DemoHomesteadRoof");
            try
            {
                // The village's roof for a two-by-two house, and the gable ends that close
                // the triangles over the walls the ridge runs to.
                var model = Child(root.transform, "Model");
                Village("Roof_RoundTiles_4x4", model, Vector3.zero, 0f);
                Village("Roof_Front_Brick4", model, new Vector3(0f, 0f, -Half), 180f);
                Village("Roof_Front_Brick4", model, new Vector3(0f, 0f, Half), 0f);

                // Nothing to walk into up there. The hit volume starts clear of the wall tops,
                // which stand 12cm into the roof's space, or every roof would overlap the
                // walls it is meant to sit on.
                Hit(root.transform, new Vector3(0f, 1.6f, 0f), new Vector3(Half * 2f + 0.4f, 2.8f, Half * 2f + 0.4f), false, Vector3.zero);

                var entity = root.AddComponent<BuildingEntity>();
                Configure(entity, new EntitySpec
                {
                    Title = "Roof", Types = new[] { RoofType },
                    // Measured from the builder's feet to the roof's middle, three metres up.
                    BuildDistance = 9f, MaxHp = 400, BuildLimit = 16, ChainDestroy = true,
                    Drops = new[] { "Timber", "2" },
                });

                AddGhost(root, canBuild, cannotBuild);
                return Save(root, "DemoHomesteadRoof");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // ---- furniture ----------------------------------------------------------

        private static BuildingEntity BuildStrongbox(Material canBuild, Material cannotBuild)
        {
            var root = new GameObject("DemoHomesteadStrongbox");
            try
            {
                var model = Child(root.transform, "Model");
                Prop("Chest_Wood", model);
                Bounds size = MeasureLocal(root.transform, model);
                FurnitureColliders(root.transform, size);

                var entity = root.AddComponent<StorageEntity>();
                Configure(entity, new EntitySpec
                {
                    Title = "Strongbox", Types = new[] { FurnitureType },
                    AnySurface = true, LevelOnly = true, BuildDistance = 6f, MaxHp = 300, BuildLimit = 2,
                    ChainDestroy = true, Clickable = true, ActivateDistance = 2.5f,
                    Drops = new[] { "Timber", "1" },
                });
                var serialized = new SerializedObject(entity);
                serialized.FindProperty("storage.slotLimit").intValue = 16;
                serialized.FindProperty("storage.weightLimit").intValue = 0;
                serialized.FindProperty("lockable").boolValue = true;
                // Anyone may open it who knows the code - the lock is the control, which is
                // the thing a locked chest is here to show. Creator-only would make the
                // code pointless.
                serialized.FindProperty("canUseByEveryone").boolValue = true;
                serialized.FindProperty("passwordLength").intValue = 4;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                AddGhost(root, canBuild, cannotBuild);
                return Save(root, "DemoHomesteadStrongbox");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static BuildingEntity BuildBench(Material canBuild, Material cannotBuild)
        {
            var root = new GameObject("DemoHomesteadBench");
            try
            {
                var model = Child(root.transform, "Model");
                // The vice is modelled at bench-top height on the bench's own origin.
                Prop("Workbench", model);
                Prop("Workbench_Vice", model);
                Bounds size = MeasureLocal(root.transform, model);
                FurnitureColliders(root.transform, size);

                var entity = root.AddComponent<WorkbenchEntity>();
                Configure(entity, new EntitySpec
                {
                    Title = "Carpenter's Bench", Types = new[] { FurnitureType },
                    AnySurface = true, LevelOnly = true, BuildDistance = 6f, MaxHp = 300, BuildLimit = 1,
                    ChainDestroy = true, Clickable = true, ActivateDistance = 3f,
                    Drops = new[] { "Timber", "2" },
                });

                AddGhost(root, canBuild, cannotBuild);
                return Save(root, "DemoHomesteadBench");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>A solid box round the furniture, a trigger hit volume a touch inside it, and a carve for the navmesh.</summary>
        private static void FurnitureColliders(Transform root, Bounds size)
        {
            var solid = Child(root, "Solid");
            Box(solid.gameObject, size.center, size.size, false);
            Vector3 inner = new Vector3(size.size.x - 0.06f, size.size.y - 0.04f, size.size.z - 0.06f);
            Hit(root, size.center + Vector3.up * 0.02f, inner, true, size.size);
        }

        // ---- entity settings ----------------------------------------------------

        private struct EntitySpec
        {
            public string Title;
            public string[] Types;
            /// <summary>Whether it can stand on open ground rather than only in a socket.</summary>
            public bool AnySurface;
            /// <summary>Whether open ground has to be within ten degrees of level.</summary>
            public bool LevelOnly;
            public float BuildDistance;
            public int MaxHp;
            public int BuildLimit;
            /// <summary>Whether it comes down with the piece it was built on.</summary>
            public bool ChainDestroy;
            /// <summary>Whether a click targets it (a door, a chest) rather than walking onto it (a floor, a wall).</summary>
            public bool Clickable;
            public float ActivateDistance;
            /// <summary>What it leaves when taken down: item name, amount.</summary>
            public string[] Drops;
        }

        /// <summary>
        /// The settings every homestead piece shares, and the ones each sets for itself.
        ///
        /// **Not attackable.** Only the kit's wolves and bandits could reach one, and a PvE
        /// island has no business knocking down someone's house while they are offline. It
        /// makes the repair settings below unreachable in play - a building that cannot be
        /// hurt never needs mending - but they are written anyway, so the fields are there
        /// to see and the day a server turns damage on they work.
        ///
        /// **No lifetime; a build limit instead.** A lifetime on a storage building eats its
        /// contents when it runs out (the campfire's lesson), and on a wall it would take a
        /// player's house down while they were away. The limits keep a shared server from
        /// filling up: sixteen foundations a player is a large house, not a town.
        /// </summary>
        private static void Configure(BuildingEntity entity, EntitySpec spec)
        {
            var serialized = new SerializedObject(entity);
            SerializedProperty title = serialized.FindProperty("entityTitle");
            if (title != null)
                title.stringValue = spec.Title;
            serialized.FindProperty("canBeAttacked").boolValue = false;
            serialized.FindProperty("canBuildOnAnySurface").boolValue = spec.AnySurface;
            serialized.FindProperty("limitSurfaceHitNormalAngle").boolValue = spec.LevelOnly;
            serialized.FindProperty("limitSurfaceHitNormalAngleMin").floatValue = 80f;
            serialized.FindProperty("limitSurfaceHitNormalAngleMax").floatValue = 100f;
            SerializedProperty types = serialized.FindProperty("buildingTypes");
            types.arraySize = spec.Types.Length;
            for (int i = 0; i < spec.Types.Length; ++i)
                types.GetArrayElementAtIndex(i).stringValue = spec.Types[i];
            serialized.FindProperty("buildDistance").floatValue = spec.BuildDistance;
            serialized.FindProperty("destroyWhenParentDestroyed").boolValue = spec.ChainDestroy;
            serialized.FindProperty("notBeingSelectedOnClick").boolValue = !spec.Clickable;
            serialized.FindProperty("maxHp").intValue = spec.MaxHp;
            serialized.FindProperty("lifeTime").floatValue = 0f;
            serialized.FindProperty("buildLimit").intValue = spec.BuildLimit;
            serialized.FindProperty("activatableDistance").floatValue = spec.ActivateDistance;

            SerializedProperty drops = serialized.FindProperty("droppingItems");
            drops.arraySize = 0;
            if (spec.Drops != null)
            {
                for (int i = 0; i + 1 < spec.Drops.Length; i += 2)
                {
                    BaseItem item = LoadItem(spec.Drops[i]);
                    if (item == null)
                        continue;
                    drops.arraySize++;
                    SerializedProperty entry = drops.GetArrayElementAtIndex(drops.arraySize - 1);
                    entry.FindPropertyRelative("item").objectReferenceValue = item;
                    entry.FindPropertyRelative("amount").intValue = int.Parse(spec.Drops[i + 1]);
                }
            }

            WriteRepairs(serialized);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            // The kit's flag above does nothing on its own - see BuildingChainDestroy.
            var chain = entity.gameObject.AddComponent<MultiplayerARPG.BuildingChainDestroy>();
            chain.comesDownWithParent = spec.ChainDestroy;
        }

        /// <summary>
        /// Mending: from the building's menu for a gold a point, or by striking it bare-handed.
        ///
        /// **Both entries are needed for the menu's Repair to appear.** `CanRepairByMenu`
        /// asks for the menu entry *and* a non-empty `CacheRepairs` - and `CacheRepairs` is
        /// built from every entry *except* the menu one. So a building with only a menu
        /// entry never offers the button. The bare-handed entry fills the cache; a null
        /// weapon is the kit's `DefaultWeaponItem`, i.e. fists.
        /// </summary>
        private static void WriteRepairs(SerializedObject serialized)
        {
            SerializedProperty repairs = serialized.FindProperty("repairs");
            repairs.arraySize = 2;
            for (int i = 0; i < 2; ++i)
            {
                SerializedProperty entry = repairs.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("canRepairFromMenu").boolValue = i == 0;
                entry.FindPropertyRelative("weaponItem").objectReferenceValue = null;
                entry.FindPropertyRelative("maxRecoveryHp").intValue = 50;
                entry.FindPropertyRelative("requireGold").intValue = i == 0 ? 1 : 0;
                SerializedProperty items = entry.FindPropertyRelative("requireItems");
                items.arraySize = 0;
                BaseItem timber = i == 1 ? LoadItem("Timber") : null;
                if (timber != null)
                {
                    // A length of timber for every ten points struck back in is not
                    // expressible - the cost is per point - so it is one per point, capped by
                    // the fifty a blow can mend.
                    items.arraySize = 1;
                    items.GetArrayElementAtIndex(0).FindPropertyRelative("item").objectReferenceValue = timber;
                    items.GetArrayElementAtIndex(0).FindPropertyRelative("amount").intValue = 1;
                }
                entry.FindPropertyRelative("requireCurrencies").arraySize = 0;
            }
        }

        /// <summary>
        /// Points one of the entity's events at a no-argument method, as a persistent
        /// listener the prefab carries (the pattern DemoBuildingBuilder.WireFlame explains).
        /// </summary>
        private static void Listen(SerializedObject serialized, string eventName, Object target, string method)
        {
            SerializedProperty calls = serialized.FindProperty(eventName + ".m_PersistentCalls.m_Calls");
            if (calls == null)
            {
                Debug.LogWarning($"[{nameof(DemoHomesteadBuilder)}] No event \"{eventName}\".");
                return;
            }
            calls.arraySize = 1;
            SerializedProperty call = calls.GetArrayElementAtIndex(0);
            call.FindPropertyRelative("m_Target").objectReferenceValue = target;
            call.FindPropertyRelative("m_MethodName").stringValue = method;
            call.FindPropertyRelative("m_Mode").enumValueIndex = (int)PersistentListenerMode.Void;
            call.FindPropertyRelative("m_CallState").enumValueIndex = (int)UnityEventCallState.RuntimeOnly;
            SerializedProperty typeName = call.FindPropertyRelative("m_TargetAssemblyTypeName");
            if (typeName != null)
                typeName.stringValue = target.GetType().AssemblyQualifiedName;
        }

        // ---- kits and recipes ---------------------------------------------------

        private static BuildingItem BuildKitItem(Kit kit, BuildingEntity piece)
        {
            string path = $"{ItemDir}/{kit.Name}.asset";
            var item = AssetDatabase.LoadAssetAtPath<BuildingItem>(path);
            if (item == null)
            {
                item = ScriptableObject.CreateInstance<BuildingItem>();
                AssetDatabase.CreateAsset(item, path);
            }
            var serialized = new SerializedObject(item);
            serialized.FindProperty("id").stringValue = kit.Name;
            serialized.FindProperty("defaultTitle").stringValue = kit.Title;
            serialized.FindProperty("defaultDescription").stringValue = kit.Description;
            serialized.FindProperty("sellPrice").intValue = kit.Price;
            serialized.FindProperty("weight").floatValue = 2f;
            serialized.FindProperty("maxStack").intValue = 10;
            serialized.FindProperty("buildingEntity").objectReferenceValue = piece;
            EnsureIcon(kit.Name, piece.gameObject);
            DemoItemBuilder.AdoptItemIcon(serialized, kit.Name);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(item);
            return item;
        }

        /// <summary>
        /// The bench's recipes, embedded on it the way the village stations carry theirs
        /// (DemoCraftStationBuilder explains why a `WorkbenchEntity` rather than the queued
        /// kind). Written from the kit table, which is their one source.
        /// </summary>
        private static void WriteBenchRecipes(WorkbenchEntity bench, Dictionary<string, BuildingItem> kits)
        {
            if (bench == null)
                return;
            var serialized = new SerializedObject(bench);
            SerializedProperty crafts = serialized.FindProperty("itemCrafts");
            crafts.arraySize = 0;
            foreach (Kit kit in Kits)
            {
                if (kit.Materials.Length == 0 || !kits.TryGetValue(kit.Name, out BuildingItem product))
                    continue;
                crafts.arraySize++;
                SerializedProperty craft = crafts.GetArrayElementAtIndex(crafts.arraySize - 1);
                craft.FindPropertyRelative("craftingItem").objectReferenceValue = product;
                craft.FindPropertyRelative("amount").intValue = 1;
                craft.FindPropertyRelative("requireGold").intValue = 0;
                WriteMaterials(craft.FindPropertyRelative("requireItems"), kit.Materials, kit.Counts);
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(bench);
            EditorUtility.SetDirty(bench.gameObject);
        }

        /// <summary>
        /// The one recipe this builder owns. Public because DemoProgressionBuilder, which
        /// rewrites the database's recipe list, keeps it by this path - and only it.
        /// </summary>
        public const string BenchFormulaPath = FormulaDir + "/CraftCarpentersBenchKit.asset";

        /// <summary>
        /// The bench kit is made anywhere, from the HUD's own Craft window. That is what
        /// makes the house reachable without a coin: gather, make a bench in the field, set
        /// it down, and it makes the rest.
        /// </summary>
        private static void WriteBenchFormula(Dictionary<string, BuildingItem> kits)
        {
            if (!kits.TryGetValue("CarpentersBenchKit", out BuildingItem product))
                return;
            string path = BenchFormulaPath;
            var formula = AssetDatabase.LoadAssetAtPath<ItemCraftFormula>(path);
            if (formula == null)
            {
                formula = ScriptableObject.CreateInstance<ItemCraftFormula>();
                AssetDatabase.CreateAsset(formula, path);
            }
            var serialized = new SerializedObject(formula);
            serialized.FindProperty("defaultTitle").stringValue = product.Title;
            serialized.FindProperty("canBeCraftedWithoutSource").boolValue = true;
            serialized.FindProperty("craftDuration").floatValue = 0f;
            SerializedProperty craft = serialized.FindProperty("itemCraft");
            craft.FindPropertyRelative("craftingItem").objectReferenceValue = product;
            craft.FindPropertyRelative("amount").intValue = 1;
            craft.FindPropertyRelative("requireGold").intValue = 0;
            WriteMaterials(craft.FindPropertyRelative("requireItems"), BenchKitMaterials, BenchKitCounts);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(formula);
            RegisterFormula(formula);
        }

        /// <summary>
        /// Lists the recipe in the game database, which is what makes it exist at runtime.
        /// Appended if missing; DemoProgressionBuilder owns the rest of that list and keeps
        /// this one when it rewrites it.
        /// </summary>
        private static void RegisterFormula(ItemCraftFormula formula)
        {
            var database = AssetDatabase.LoadAssetAtPath<GameDatabase>($"{GameDataDir}/GameDatabase.asset");
            if (database == null)
            {
                Debug.LogError($"[{nameof(DemoHomesteadBuilder)}] No GameDatabase; the bench kit's recipe is not registered.");
                return;
            }
            var serialized = new SerializedObject(database);
            SerializedProperty list = serialized.FindProperty("itemCraftFormulas");
            for (int i = 0; i < list.arraySize; ++i)
            {
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == formula)
                    return;
            }
            list.arraySize++;
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = formula;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(database);
        }

        private static void WriteMaterials(SerializedProperty requires, string[] names, int[] counts)
        {
            requires.arraySize = 0;
            for (int i = 0; i < names.Length; ++i)
            {
                BaseItem material = LoadItem(names[i]);
                if (material == null)
                    continue;
                requires.arraySize++;
                SerializedProperty entry = requires.GetArrayElementAtIndex(requires.arraySize - 1);
                entry.FindPropertyRelative("item").objectReferenceValue = material;
                entry.FindPropertyRelative("amount").intValue = counts[i];
            }
        }

        // ---- the plot -----------------------------------------------------------

        /// <summary>
        /// Cuts the homestead terrace into the shore and settles the map round it.
        ///
        /// Writes the terrain (only the plot's patch, see DemoIslandBuilder.RelevelRegion),
        /// moves any scene object standing on the patch up or down with the ground under
        /// it, relays the `Harvestables` root (destroyed and rebuilt from the island's rules,
        /// as `Rebuild Harvestable Nodes` does) and rebakes the navmesh, which reads the
        /// terrain. Objects under `Authored` are reported rather than moved - nothing
        /// generated touches those.
        ///
        /// After it: `Build Map Server`, because DemoMap has changed.
        /// </summary>
        [MenuItem("Open MMORPG/Demo/Level Homestead Plot (writes terrain and DemoMap)", priority = 156)]
        public static void LevelPlot()
        {
            // Opening the map replaces whatever is open, and OpenScene does not ask: unsaved
            // edits to DemoMap - work under Authored included - would simply be gone.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            Scene scene = EditorSceneManager.OpenScene(DemoSceneBuilder.ScenePath, OpenSceneMode.Single);
            Terrain terrain = Terrain.activeTerrain;
            if (terrain == null)
            {
                Debug.LogError($"[{nameof(DemoHomesteadBuilder)}] No terrain in {DemoSceneBuilder.ScenePath}.");
                return;
            }

            Vector2 centre = DemoIslandBuilder.HomesteadCentre;
            float radius = DemoIslandBuilder.HomesteadRadius;

            // Where everything on the patch stands relative to the ground, before it moves.
            var standing = new List<KeyValuePair<Transform, float>>();
            var authored = new List<string>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.GetComponent<Terrain>() != null)
                    continue;
                bool isAuthored = root.name == DemoSceneBuilder.AuthoredRootName;
                bool characters = System.Array.IndexOf(DemoSceneBuilder.MovableRootNames, root.name) >= 0;
                foreach (Transform child in root.transform)
                    Collect(child, centre, radius, terrain, standing, authored, isAuthored, characters);
            }

            DemoIslandBuilder.RelevelRegion(centre, radius);
            terrain.Flush();

            int moved = 0;
            foreach (KeyValuePair<Transform, float> entry in standing)
            {
                Vector3 position = entry.Key.position;
                float ground = terrain.SampleHeight(position) + terrain.transform.position.y;
                float wanted = ground + entry.Value;
                if (Mathf.Abs(wanted - position.y) < 0.005f)
                    continue;
                Undo.RecordObject(entry.Key, "Reseat on levelled ground");
                entry.Key.position = new Vector3(position.x, wanted, position.z);
                ++moved;
            }
            foreach (string name in authored)
                Debug.LogWarning($"[{nameof(DemoHomesteadBuilder)}] {name} (under {DemoSceneBuilder.AuthoredRootName}) " +
                                 "stands on the plot, whose ground has moved. Not touched - check it by hand.");

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[{nameof(DemoHomesteadBuilder)}] Levelled the homestead plot; {moved} object(s) reseated. " +
                      "Relaying the harvestable nodes and rebaking the navmesh.");

            // The wood players chop is not terrain trees but spawned nodes on spots baked
            // into the Harvestables root, and those spots were rolled before the plot was
            // settled ground - so trees would keep growing out of people's floors. The
            // spots come from the island's own rules, which now keep off the plot, so
            // relaying the root takes them off it. It replaces that root and nothing else.
            DemoSceneBuilder.RebuildHarvestableNodes();
            DemoSceneBuilder.RebakeNavMesh();
        }

        /// <summary>
        /// Top-level objects of each root that stand on the patch, with their height above
        /// the ground, so they can be put back at the same height over the new ground. Only
        /// the first level: a house's furniture moves with its house.
        /// </summary>
        /// <param name="characters">
        /// Whether these are people and animals (the NPC, mount and wildlife roots), which
        /// stand on the ground and never in it. Their height over the old ground is kept only
        /// when it is above it. The carpenter is why: he is placed at the *levelled* height
        /// (DemoNpcBuilder reads the island's height function, which already has the plot in
        /// it), so on a plot not yet levelled he stands below the old slope - and keeping
        /// that offset sank him 0.58m into the new ground (found 2026-09-25). Rocks keep a
        /// negative offset on purpose: they are meant to sit half buried.
        /// </param>
        private static void Collect(Transform node, Vector2 centre, float radius, Terrain terrain,
                                    List<KeyValuePair<Transform, float>> standing, List<string> authored,
                                    bool isAuthored, bool characters)
        {
            Vector3 position = node.position;
            if (Vector2.Distance(new Vector2(position.x, position.z), centre) > radius)
            {
                // A grouping object (the Harvestables root's areas, say) can sit far off
                // while its children stand here.
                foreach (Transform child in node)
                    Collect(child, centre, radius, terrain, standing, authored, isAuthored, characters);
                return;
            }
            if (isAuthored)
            {
                authored.Add(node.name);
                return;
            }
            float ground = terrain.SampleHeight(position) + terrain.transform.position.y;
            float offset = position.y - ground;
            if (characters)
                offset = Mathf.Max(0f, offset);
            standing.Add(new KeyValuePair<Transform, float>(node, offset));
        }

        // ---- icons --------------------------------------------------------------

        /// <summary>
        /// Draws a kit's icon from its piece, if the kit has none yet.
        ///
        /// A stand-in, like the skill icons the skill builder draws: a PNG already at the
        /// path is kept, so a hand-drawn icon dropped in later wins and a rebuild never paints
        /// over it (see [[demo-builders-must-not-eat-hand-edits]]). Delete the file to have it
        /// drawn again.
        /// </summary>
        private static void EnsureIcon(string kitName, GameObject piece)
        {
            string path = $"{IconDir}/{kitName}.png";
            if (System.IO.File.Exists(path))
                return;
            DemoItemBuilder.EnsureFolder(IconDir);
            Texture2D icon = RenderIcon(piece);
            if (icon == null)
                return;
            System.IO.File.WriteAllBytes(path, icon.EncodeToPNG());
            Object.DestroyImmediate(icon);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }

        private const int IconSize = 256;

        /// <summary>
        /// A three-quarter view of the piece, on a transparent ground.
        ///
        /// The transparency is recovered by rendering twice, over black and over white: a
        /// pixel the model covers comes out the same both times, an empty one differs by the
        /// full range, and anything between - an antialiased edge, the window glass - by how
        /// much of the background shows through. That is exact where a colour key is not.
        /// </summary>
        /// <param name="closeness">
        /// Under 1 brings the camera in. The framing fits the piece's bounding sphere, which
        /// for a long thin thing on a diagonal - a tool - leaves it small in the frame.
        /// </param>
        internal static Texture2D RenderIcon(GameObject piece, float closeness = 1f)
        {
            var preview = new PreviewRenderUtility();
            try
            {
                // Every child that draws something - `Model`, and the door's `Hinge`, which
                // carries its leaf - copied without the piece's scripts, which have no
                // business running in a preview.
                var instance = new GameObject("Icon");
                foreach (Transform child in piece.transform)
                {
                    if (child.GetComponentInChildren<Renderer>(true) == null)
                        continue;
                    GameObject copy = Object.Instantiate(child.gameObject, instance.transform);
                    copy.transform.localPosition = child.localPosition;
                    copy.transform.localRotation = child.localRotation;
                    copy.transform.localScale = child.localScale;
                }
                preview.AddSingleGO(instance);

                Bounds bounds = new Bounds();
                bool any = false;
                foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>())
                {
                    if (!any) { bounds = renderer.bounds; any = true; }
                    else bounds.Encapsulate(renderer.bounds);
                }
                if (!any)
                    return null;

                Camera camera = preview.camera;
                camera.fieldOfView = 30f;
                camera.nearClipPlane = 0.05f;
                camera.farClipPlane = 100f;
                // From in front and to the right, a little above: the modules' +Z face is the
                // outside of the house, the side a player sees walking up to it.
                Quaternion view = Quaternion.Euler(24f, 215f, 0f);
                float distance = bounds.extents.magnitude / Mathf.Sin(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * 0.92f * closeness;
                camera.transform.rotation = view;
                camera.transform.position = bounds.center - view * Vector3.forward * distance;

                preview.lights[0].intensity = 1.3f;
                preview.lights[0].transform.rotation = Quaternion.Euler(40f, 160f, 0f);
                preview.lights[1].intensity = 0.6f;
                preview.lights[1].transform.rotation = Quaternion.Euler(20f, 40f, 0f);
                preview.ambientColor = new Color(0.45f, 0.45f, 0.5f);

                var rect = new Rect(0, 0, IconSize, IconSize);
                Color[] black = Shoot(preview, rect, Color.black);
                Color[] white = Shoot(preview, rect, Color.white);

                var icon = new Texture2D(IconSize, IconSize, TextureFormat.RGBA32, false);
                var pixels = new Color[black.Length];
                for (int i = 0; i < pixels.Length; ++i)
                {
                    float through = ((white[i].r - black[i].r) + (white[i].g - black[i].g) + (white[i].b - black[i].b)) / 3f;
                    float alpha = Mathf.Clamp01(1f - through);
                    pixels[i] = alpha <= 0.001f
                        ? new Color(0f, 0f, 0f, 0f)
                        : new Color(Mathf.Clamp01(black[i].r / alpha), Mathf.Clamp01(black[i].g / alpha), Mathf.Clamp01(black[i].b / alpha), alpha);
                }
                icon.SetPixels(pixels);
                icon.Apply();
                return icon;
            }
            finally
            {
                preview.Cleanup();
            }
        }

        private static Color[] Shoot(PreviewRenderUtility preview, Rect rect, Color background)
        {
            preview.BeginStaticPreview(rect);
            preview.camera.clearFlags = CameraClearFlags.SolidColor;
            preview.camera.backgroundColor = background;
            preview.Render(true);
            Texture2D shot = preview.EndStaticPreview();
            Color[] pixels = shot.GetPixels();
            Object.DestroyImmediate(shot);
            return pixels;
        }

        // ---- construction helpers -----------------------------------------------

        private static Transform Child(Transform parent, string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child.transform;
        }

        /// <summary>
        /// A village module, unpacked and with its colliders taken off. Unpacked so the
        /// piece holds plain meshes, as the collector expects to find and redirect them;
        /// stripped because the piece's own boxes do the colliding (see the class notes).
        /// </summary>
        private static GameObject Village(string name, Transform parent, Vector3 position, float yaw)
        {
            GameObject instance = DemoVillageBuilder.Place(name, parent, position, yaw);
            return Prepare(instance);
        }

        private static GameObject Prop(string name, Transform parent)
        {
            GameObject instance = DemoSceneBuilder.Prop(name, parent, Vector3.zero, 0f, false);
            return Prepare(instance);
        }

        private static GameObject Prepare(GameObject instance)
        {
            if (instance == null)
                return null;
            if (PrefabUtility.IsPartOfPrefabInstance(instance))
                PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true))
                Object.DestroyImmediate(collider);
            foreach (Transform node in instance.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(node.gameObject, (StaticEditorFlags)0);
            return instance;
        }

        private static BoxCollider Box(GameObject host, Vector3 centre, Vector3 size, bool trigger)
        {
            var box = host.AddComponent<BoxCollider>();
            box.center = centre;
            box.size = size;
            box.isTrigger = trigger;
            return box;
        }

        /// <summary>The piece's hit volume: a trigger box carrying the kit's `BuildingMaterial`, and optionally a navmesh carve.</summary>
        private static void Hit(Transform root, Vector3 centre, Vector3 size, bool carve, Vector3 carveSize)
        {
            var hit = Child(root, "Hit");
            Box(hit.gameObject, centre, size, true);
            // No renderer here, so the kit's own ghost colouring finds none and leaves the
            // drawing to BuildingGhost.
            hit.gameObject.AddComponent<BuildingMaterial>();
            if (!carve)
                return;
            var obstacle = hit.gameObject.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.center = centre;
            obstacle.size = carveSize;
            obstacle.carving = true;
            obstacle.carveOnlyStationary = true;
            // Off in the prefab; BuildingGhost switches it on for a placed piece only.
            obstacle.enabled = false;
        }

        /// <summary>
        /// A place another piece snaps to: a trigger box with a `BuildingArea`.
        ///
        /// **Sockets are flat pads, never tall boxes** (found in the harness, 2026-09-25). The
        /// kit's aim (`DefaultBuildAimController.LoopSetBuildingArea`) takes the point where
        /// the camera ray met a socket and casts *straight down from 50m above it*, accepting
        /// the socket only if that vertical ray hits the same collider. A ray that met a tall
        /// box on one of its **sides** met it on the box's boundary, so the vertical ray
        /// grazes the face and misses, and the socket is silently skipped - the ghost falls
        /// through to whatever is behind it. The first wall sockets were 3m tall boxes where
        /// the wall would stand and worked or not depending on the angle. A thin pad is met on
        /// its top or its underside, whose points are well inside its footprint, so the
        /// vertical ray always finds it. The same goes for anything else built on this kit.
        /// </summary>
        private static BuildingArea Socket(Transform parent, string name, string type, BuildingEntity entity,
                                           Vector3 position, float yaw, Vector3 boxCentre, Vector3 boxSize,
                                           bool snap, bool rotate)
        {
            var socket = Child(parent, name);
            socket.localPosition = position;
            socket.localRotation = Quaternion.Euler(0f, yaw, 0f);
            Box(socket.gameObject, boxCentre, boxSize, true);
            var area = socket.gameObject.AddComponent<BuildingArea>();
            area.entity = entity;
            area.buildingType = type;
            area.snapBuildingObject = snap;
            area.allowRotateInSocket = rotate;
            return area;
        }

        private static void AddGhost(GameObject root, Material canBuild, Material cannotBuild)
        {
            var ghost = root.AddComponent<MultiplayerARPG.BuildingGhost>();
            ghost.canBuildMaterial = canBuild;
            ghost.cannotBuildMaterial = cannotBuild;
        }

        /// <summary>Renderer bounds of the model, in the piece's own frame.</summary>
        private static Bounds MeasureLocal(Transform frame, Transform model)
        {
            var bounds = new Bounds();
            bool any = false;
            foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null)
                    continue;
                Bounds own = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; ++i)
                {
                    var corner = new Vector3(
                        (i & 1) == 0 ? own.min.x : own.max.x,
                        (i & 2) == 0 ? own.min.y : own.max.y,
                        (i & 4) == 0 ? own.min.z : own.max.z);
                    Vector3 point = frame.InverseTransformPoint(filter.transform.TransformPoint(corner));
                    if (!any) { bounds = new Bounds(point, Vector3.zero); any = true; }
                    else bounds.Encapsulate(point);
                }
            }
            return any ? bounds : new Bounds(Vector3.up * 0.5f, Vector3.one);
        }

        private static BuildingEntity Save(GameObject root, string name)
        {
            string path = $"{PrefabDir}/{name}.prefab";
            PrefabUtility.SaveAsPrefabAsset(root, path);
            DemoEntityBuilder.GiveOwnNetworkId(path);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var saved = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            return saved != null ? saved.GetComponent<BuildingEntity>() : null;
        }

        /// <summary>
        /// A see-through, unlit colour for the ghost, built once and kept. URP's Unlit shader
        /// switched to transparent the long way round, because the keywords and blend state
        /// its inspector sets are not implied by the `_Surface` property on its own.
        /// </summary>
        private static Material GhostMaterial(string name, Color colour)
        {
            string path = $"{MaterialDir}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null)
                {
                    Debug.LogError($"[{nameof(DemoHomesteadBuilder)}] No URP Unlit shader for the build ghost.");
                    return null;
                }
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", colour);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static BaseItem LoadItem(string name)
        {
            var item = AssetDatabase.LoadAssetAtPath<BaseItem>($"{ItemDir}/{name}.asset");
            if (item == null)
                Debug.LogError($"[{nameof(DemoHomesteadBuilder)}] No item \"{name}\". Run Build Items and Build Harvestables first.");
            return item;
        }
    }
}
