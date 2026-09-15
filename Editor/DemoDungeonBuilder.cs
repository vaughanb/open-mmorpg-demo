using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// Lays out the crypt under the hills: the demo's dungeon, a map of its own that the
    /// island's crypt door warps into.
    ///
    /// The dungeon is cut from the same Medieval Village modules as the houses. The pack
    /// has no dungeon set, but its uneven-brick wall is a 2m by 3m slab of rubble
    /// masonry that reads as a vault wall the moment it is lit by a torch instead of the
    /// sun, and its brick floor tiles both ways up: laid at the ceiling height and turned
    /// over, a floor is a ceiling. So the whole place is a grid of 2m cells, each either
    /// open or rock, and everything else follows from the grid: a wall wherever an open
    /// cell meets rock, a quoin wherever two walls turn a corner into the room, a slab
    /// over every cell at that cell's ceiling height.
    ///
    /// The layout is a loop. Players come down a stair into an antechamber and can see
    /// the sanctum through an iron gate at its far end, but the gate is locked, so the way
    /// round is through the cultists' barracks and their scriptorium and down into the
    /// sanctum from behind, where the Hierophant waits at the altar. The stair is the way
    /// back out.
    /// </summary>
    public static class DemoDungeonBuilder
    {
        public const string ScenePath = "Assets/OpenMMORPG/Demo/Scenes/DemoDungeon.unity";
        public const string MapInfoPath = "Assets/OpenMMORPG/Demo/GameData/Resources/MapInfos/CultistCrypt.asset";
        public const string GatePrefabPath = "Assets/OpenMMORPG/Demo/Prefabs/GamePlay/DungeonGate.prefab";
        private const string PortalTemplatePath = "Assets/OpenMMORPG/Demo/Prefabs/GamePlay/WarpPortalEntity.prefab";
        private const string DarknessMaterialPath = "Assets/OpenMMORPG/Demo/Materials/Darkness.mat";
        private const string SkyMaterialPath = "Assets/OpenMMORPG/Demo/Materials/CryptSky.mat";
        private const string EntityDir = "Assets/OpenMMORPG/Demo/Prefabs/GamePlay/CharacterEntities";

        /// <summary>The map's id, which is what a saved character records as where it is.</summary>
        public const string MapId = "CultistCrypt";
        public const string MapTitle = "Cultist Crypt";

        /// <summary>
        /// Where a character coming down from the island arrives: on the landing at the
        /// top of the stair, a pace in from the door, looking down into the crypt.
        /// </summary>
        public static readonly Vector3 ArrivalPosition = new Vector3(2f, Storey + 0.05f, -7.6f);
        public const float ArrivalYaw = 0f;

        /// <summary>
        /// The way out: the gate stands in the landing's doorway, its trigger reaching a
        /// hand's width into the landing, so that walking into the dark beyond the door
        /// is what takes you up. It is placed clear of the arrival point, or a character
        /// coming down would be sent straight back up.
        /// </summary>
        public static readonly Vector3 ExitGatePosition = new Vector3(2f, Storey, -9.05f);
        public const float ExitGateYaw = 0f;

        /// <summary>The gate's trigger: the width of a doorway, and no deeper than its jambs.</summary>
        public static readonly Vector3 GateTriggerSize = new Vector3(1.6f, 2.6f, 0.9f);

        private const float Cell = DemoVillageBuilder.Cell;
        private const float Storey = DemoVillageBuilder.WallHeight;

        /// <summary>
        /// How far the thin side of a wall module stands inside the line it is placed on.
        /// The module straddles its line, 0.31 out and 0.09 in; the 0.09 is the face the
        /// room sees, and what anything stood against a wall is measured to.
        /// </summary>
        private const float WallInset = 0.09f;

        /// <summary>
        /// The layer the ceiling slabs are on. They keep their colliders, so the camera's
        /// wall-hit spring stops at them instead of rising through the roof, but on the
        /// built-in Ignore Raycast layer, which the spawn areas' ground ray and the
        /// navmesh bake are told to skip - the kit finds a monster's ground with a ray
        /// from far above, and a ceiling that stops it stands the monster on the roof.
        /// </summary>
        private static int CeilingLayer { get { return LayerMask.NameToLayer("Ignore Raycast"); } }

        // ---- layout ----------------------------------------------------------

        private enum Side { North, East, South, West }

        private struct Room
        {
            public string Name;
            /// <summary>Cell range, inclusive. A cell (x, z) is centred on (2x, 2z).</summary>
            public int X0, Z0, X1, Z1;
            public float Floor;
            public float Ceiling;
            public string Tile;

            public float MinX { get { return X0 * Cell - Cell * 0.5f; } }
            public float MaxX { get { return X1 * Cell + Cell * 0.5f; } }
            public float MinZ { get { return Z0 * Cell - Cell * 0.5f; } }
            public float MaxZ { get { return Z1 * Cell + Cell * 0.5f; } }
            public Vector3 Centre { get { return new Vector3((MinX + MaxX) * 0.5f, Floor, (MinZ + MaxZ) * 0.5f); } }
        }

        private static Room R(string name, int x0, int z0, int x1, int z1, float floor = 0f, float ceiling = Storey, string tile = "Floor_UnevenBrick")
        {
            return new Room { Name = name, X0 = x0, Z0 = z0, X1 = x1, Z1 = z1, Floor = floor, Ceiling = ceiling, Tile = tile };
        }

        /// <summary>
        /// The rooms, in cells. Rooms wear the uneven brick underfoot and the passages the
        /// dressed brick, so the change of floor says a passage has begun. The stairwell,
        /// the landing and the sanctum are two storeys tall: the stair needs the headroom
        /// and the sanctum is meant to feel like the one room that was built rather than dug.
        /// </summary>
        private static readonly Room[] Rooms =
        {
            // Two cells wide, though one stair would do: the third-person camera needs
            // the room. In a passage one cell wide it is pushed in against the
            // character's back, and the way in is the first thing a player sees.
            R("Landing", 1, -4, 2, -4, Storey, Storey * 2f, "Floor_Brick"),
            R("Stairwell", 1, -3, 2, -1, 0f, Storey * 2f, "Floor_Brick"),
            R("Antechamber", 0, 0, 3, 2),
            R("GatePassage", 4, 0, 5, 1, 0f, Storey, "Floor_Brick"),
            R("NorthPassage", 1, 3, 2, 4, 0f, Storey, "Floor_Brick"),
            R("Barracks", 0, 5, 4, 7),
            R("EastPassage", 5, 5, 6, 6, 0f, Storey, "Floor_Brick"),
            R("Scriptorium", 7, 4, 9, 7),
            R("SouthPassage", 8, 2, 9, 3, 0f, Storey, "Floor_Brick"),
            R("Sanctum", 6, -4, 11, 1, 0f, Storey * 2f, "Floor_Brick"),
            R("Ossuary", 12, -2, 13, -1),
        };

        private static readonly Dictionary<Vector2Int, int> Cells = new Dictionary<Vector2Int, int>();

        private static void MapCells()
        {
            Cells.Clear();
            for (int i = 0; i < Rooms.Length; ++i)
            {
                Room room = Rooms[i];
                for (int x = room.X0; x <= room.X1; ++x)
                    for (int z = room.Z0; z <= room.Z1; ++z)
                        Cells[new Vector2Int(x, z)] = i;
            }
        }

        private static Room RoomNamed(string name)
        {
            foreach (Room room in Rooms)
                if (room.Name == name)
                    return room;
            throw new System.ArgumentException($"No room named {name}.");
        }

        private static Vector2Int Step(Side side)
        {
            switch (side)
            {
                case Side.North: return new Vector2Int(0, 1);
                case Side.East: return new Vector2Int(1, 0);
                case Side.South: return new Vector2Int(0, -1);
                default: return new Vector2Int(-1, 0);
            }
        }

        /// <summary>
        /// A wall module's yaw so that its thick side faces the given way. At yaw 0 the
        /// module's bulk is on its +Z, which is how <see cref="DemoVillageBuilder"/>
        /// places a north wall.
        /// </summary>
        private static float WallYaw(Side outward)
        {
            switch (outward)
            {
                case Side.North: return 0f;
                case Side.East: return 90f;
                case Side.South: return 180f;
                default: return 270f;
            }
        }

        /// <summary>A prop's yaw to stand with its back to a wall and its front to the room.</summary>
        private static float FacingYaw(Side wall)
        {
            switch (wall)
            {
                case Side.South: return 0f;
                case Side.West: return 90f;
                case Side.North: return 180f;
                default: return 270f;
            }
        }

        // ---- build -----------------------------------------------------------

        [MenuItem("Open MMORPG/Demo/Build Dungeon Scene")]
        public static void Build()
        {
            DemoItemBuilder.EnsureFolder("Assets/OpenMMORPG/Demo/Scenes");
            DemoItemBuilder.EnsureFolder("Assets/OpenMMORPG/Demo/Materials");

            Scene scene;
            if (System.IO.File.Exists(ScenePath))
            {
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                foreach (GameObject root in scene.GetRootGameObjects())
                    Object.DestroyImmediate(root);
            }
            else
            {
                scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, ScenePath);
            }

            MapCells();
            BuildLighting();

            var dungeon = new GameObject("Dungeon");
            SceneManager.MoveGameObjectToScene(dungeon, scene);
            Transform t = dungeon.transform;

            BuildShell(Child(t, "Shell"));
            BuildStair(Child(t, "Stair"));
            BuildGate(Child(t, "Gate"));
            Furnish(Child(t, "Props"));
            BuildSpawners(scene);
            BuildAmbience(scene);

            DemoSceneBuilder.MarkStatic(dungeon);
            BakeNavMesh(scene);

            BuildMapInfo();
            BuildGatePrefab();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log($"[{nameof(DemoDungeonBuilder)}] Built {ScenePath}. Run Wire Game Database to open the way in.");
        }

        private static Transform Child(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        /// <summary>
        /// The crypt's ambience bed: one 2D loop from the CryptAmbience clip family under
        /// an `Ambience` root, driven by <see cref="MultiplayerARPG.Demo.DemoAmbientLoop"/>
        /// so the ambient volume setting applies. Not built when no clip is provided.
        /// </summary>
        private static void BuildAmbience(Scene scene)
        {
            AudioClip[] clips = DemoAudioWiring.Clips(DemoAudioWiring.CryptAmbience);
            if (clips.Length == 0)
                return;
            var root = new GameObject("Ambience");
            SceneManager.MoveGameObjectToScene(root, scene);
            var go = new GameObject("Crypt");
            go.transform.SetParent(root.transform, false);
            var source = go.AddComponent<AudioSource>();
            source.clip = clips[0];
            source.loop = true;
            source.playOnAwake = true;
            source.spatialBlend = 0f;
            source.volume = 0.6f;
            var loop = go.AddComponent<MultiplayerARPG.Demo.DemoAmbientLoop>();
            loop.baseVolume = 0.6f;
            loop.fadeWithHeight = false;
        }

        /// <summary>
        /// Replaces the crypt's ambience in the saved dungeon scene and nothing else, so a
        /// new clip does not cost a full dungeon rebuild.
        /// </summary>
        [MenuItem("Open MMORPG/Demo/Rebuild Crypt Ambience")]
        public static void RebuildAmbience()
        {
            if (!System.IO.File.Exists(ScenePath))
            {
                Debug.LogError($"[{nameof(DemoDungeonBuilder)}] No dungeon scene at {ScenePath}; build it first.");
                return;
            }
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == "Ambience")
                    Object.DestroyImmediate(root);
            }
            BuildAmbience(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[{nameof(DemoDungeonBuilder)}] Rebuilt the crypt's ambience in {ScenePath}.");
        }

        /// <summary>
        /// No sun, no sky, and the fog is the dark. The ambient is a flat dim grey-violet
        /// rather than the island's trilight, because underground there is no sky to be
        /// lit from and no ground bounce: what is not lit by a torch is lit by nothing
        /// much. The skybox is a black material rather than none at all, because with
        /// none the camera clears to its own background colour, which is the island's
        /// blue, and the dark corners of the crypt would open onto daylight.
        /// </summary>
        private static void BuildLighting()
        {
            RenderSettings.sun = null;
            RenderSettings.skybox = BuildSky();
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.11f, 0.10f, 0.13f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 0.04f;
            RenderSettings.fogColor = new Color(0.012f, 0.010f, 0.016f);
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = null;
        }

        private static Material BuildSky()
        {
            var sky = AssetDatabase.LoadAssetAtPath<Material>(SkyMaterialPath);
            if (sky == null)
            {
                sky = new Material(Shader.Find("Skybox/Cubemap"));
                AssetDatabase.CreateAsset(sky, SkyMaterialPath);
            }
            sky.SetColor("_Tint", Color.black);
            sky.SetFloat("_Exposure", 0f);
            EditorUtility.SetDirty(sky);
            return sky;
        }

        /// <summary>
        /// Black with no lighting at all, for the void behind a doorway that leads out of
        /// the map. A lit black surface still catches the torchlight and reads as a wall;
        /// an unlit one reads as nothing there.
        /// </summary>
        internal static Material Darkness()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(DarknessMaterialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                material.SetColor("_BaseColor", Color.black);
                AssetDatabase.CreateAsset(material, DarknessMaterialPath);
            }
            return material;
        }

        // ---- shell ------------------------------------------------------------

        /// <summary>
        /// Floors, ceilings, walls and quoins, all read off the cell grid.
        ///
        /// A wall goes on every edge where an open cell meets rock, thick side into the
        /// rock, and stacked in storeys up to the cell's ceiling. Where two open cells
        /// meet, a wall goes only across the part of the edge that one has and the other
        /// does not: the riser under a higher floor, or the band above a lower ceiling —
        /// which is how the sanctum keeps its full height over the passages that open
        /// into it. Ceiling slabs get no collider, because the kit's spawn areas find
        /// the ground by a ray cast from far above, and a ceiling that stops the ray
        /// would stand every cultist on the roof.
        /// </summary>
        private static void BuildShell(Transform parent)
        {
            Transform floors = Child(parent, "Floors");
            Transform ceilings = Child(parent, "Ceilings");
            Transform walls = Child(parent, "Walls");
            Transform quoins = Child(parent, "Quoins");

            foreach (KeyValuePair<Vector2Int, int> entry in Cells)
            {
                Vector2Int cell = entry.Key;
                Room room = Rooms[entry.Value];
                var centre = new Vector3(cell.x * Cell, 0f, cell.y * Cell);

                DemoVillageBuilder.Place(room.Tile, floors, centre + Vector3.up * room.Floor, 0f);

                GameObject slab = DemoVillageBuilder.Place("Floor_Brick", ceilings, centre + Vector3.up * room.Ceiling, 0f);
                if (slab != null)
                {
                    slab.transform.localRotation = Quaternion.Euler(180f, 0f, 0f);
                    foreach (Transform part in slab.GetComponentsInChildren<Transform>(true))
                        part.gameObject.layer = CeilingLayer;
                }

                foreach (Side side in new[] { Side.North, Side.East, Side.South, Side.West })
                {
                    Vector2Int step = Step(side);
                    Vector3 edge = centre + new Vector3(step.x, 0f, step.y) * (Cell * 0.5f);
                    float yaw = WallYaw(side);
                    int neighbourIndex;
                    if (!Cells.TryGetValue(cell + step, out neighbourIndex))
                    {
                        // The landing's own south wall is the way out, and its west cell
                        // takes the door; the arrival point and the gate sit in that cell.
                        bool doorway = room.Name == "Landing" && side == Side.South && cell.x == room.X0;
                        WallRun(walls, edge, yaw, room.Floor, room.Ceiling, doorway);
                        continue;
                    }
                    Room neighbour = Rooms[neighbourIndex];
                    if (neighbour.Floor > room.Floor)
                        WallRun(walls, edge, yaw, room.Floor, neighbour.Floor, false);
                    if (neighbour.Ceiling < room.Ceiling)
                        WallRun(walls, edge, yaw, Mathf.Max(neighbour.Ceiling, room.Floor), room.Ceiling, false);
                }

                // A convex corner: this cell and both its neighbours along the diagonal are
                // open, and the cell across the diagonal is rock. The two walls on that
                // rock stop short of each other by the width of a wall's thin side and
                // leave a notch in the corner; a quoin fills it, and since a quoin is
                // heavier than the wall it reads as a pier the vault was built on.
                //
                // Only this cell sees the corner — from either neighbour the diagonal
                // cell is rock, so it is not counted twice. The quoin's masonry lies to
                // its own -X/-Z, so it is turned to put that toward the notch.
                foreach (int dx in new[] { -1, 1 })
                {
                    foreach (int dz in new[] { -1, 1 })
                    {
                        if (!Cells.ContainsKey(cell + new Vector2Int(dx, 0)) ||
                            !Cells.ContainsKey(cell + new Vector2Int(0, dz)) ||
                            Cells.ContainsKey(cell + new Vector2Int(dx, dz)))
                            continue;
                        float quoinYaw = dx < 0 ? (dz < 0 ? 180f : 270f) : (dz < 0 ? 90f : 0f);
                        Vector3 corner = centre + new Vector3(dx, 0f, dz) * (Cell * 0.5f);
                        for (float y = room.Floor; y < room.Ceiling - 0.01f; y += Storey)
                            DemoVillageBuilder.Place("Corner_Exterior_Brick", quoins, corner + Vector3.up * y, quoinYaw);
                    }
                }
            }
        }

        /// <summary>A wall from one height to another, in storeys, with a doorway if asked.</summary>
        private static void WallRun(Transform parent, Vector3 edge, float yaw, float from, float to, bool doorway)
        {
            for (float y = from; y < to - 0.01f; y += Storey)
            {
                bool door = doorway && y < from + 0.01f;
                GameObject wall = DemoVillageBuilder.Place(door ? "Wall_UnevenBrick_Door_Round" : "Wall_UnevenBrick_Straight",
                    parent, edge + Vector3.up * y, yaw);
                if (wall == null)
                    continue;
                float height = Mathf.Min(Storey, to - y);
                if (height < Storey - 0.01f)
                    wall.transform.localScale = new Vector3(1f, height / Storey, 1f);
                if (door)
                    DemoVillageBuilder.Place("DoorFrame_Round_Brick", parent, edge + Vector3.up * y, yaw);
            }
        }

        // ---- the stair and the gate ---------------------------------------------

        /// <summary>
        /// The stair down from the landing. The pack's long interior stair climbs one
        /// storey over 5.8m, which from the landing's edge reaches the antechamber's
        /// door line with its bottom step just inside the stairwell — measured off the
        /// model: it climbs along its own +Z from an origin 0.36 short of the first step.
        /// It is 1.76 wide, so one goes in each cell of the well, each stretched the
        /// last eighth to fill its cell, and the two read as one broad stair.
        /// </summary>
        private static void BuildStair(Transform parent)
        {
            Room stairwell = RoomNamed("Stairwell");
            const float bottomOverhang = 0.36f;
            const float stairWidth = 1.76f;
            // Turned to climb toward -Z, the landing's side, with its foot at the well's
            // north end, on the antechamber's line.
            float footZ = stairwell.MaxZ;
            for (int x = stairwell.X0; x <= stairwell.X1; ++x)
            {
                GameObject stair = DemoVillageBuilder.Place("Stair_Interior_SolidExtended", parent, new Vector3(x * Cell, 0f, footZ - bottomOverhang), 180f);
                if (stair != null)
                    stair.transform.localScale = new Vector3(Cell / stairWidth, 1f, 1f);
            }
        }

        /// <summary>
        /// The dark beyond the way out: an unlit black box filling the rock behind the
        /// landing's door, so the doorway shows nothing rather than the far side of a
        /// wall module. The gate entity itself is not placed here — the kit spawns it
        /// from the warp portal database when the map loads — but its box is drawn in
        /// the editor so the doorway and the trigger can be seen to agree.
        /// </summary>
        private static void BuildGate(Transform parent)
        {
            Room landing = RoomNamed("Landing");
            GameObject dark = GameObject.CreatePrimitive(PrimitiveType.Cube);
            dark.name = "Darkness";
            dark.transform.SetParent(parent, false);
            Object.DestroyImmediate(dark.GetComponent<Collider>());
            dark.transform.localPosition = new Vector3(landing.MinX + Cell * 0.5f, landing.Floor + Storey * 0.5f, landing.MinZ - 0.8f);
            dark.transform.localScale = new Vector3(Cell - 0.1f, Storey - 0.05f, 1.4f);
            dark.GetComponent<MeshRenderer>().sharedMaterial = Darkness();

            // An empty marks where the gate will stand, so the doorway and the trigger
            // can be compared in the editor without running the map.
            var marker = new GameObject("ExitGate (spawned at runtime)");
            marker.transform.SetParent(parent, false);
            marker.transform.localPosition = ExitGatePosition;
            marker.transform.localRotation = Quaternion.Euler(0f, ExitGateYaw, 0f);
        }

        // ---- furnishing ---------------------------------------------------------

        private const string TorchModel = "Torch_Metal";
        private const float TorchHeight = 1.45f;
        private static readonly Vector3 TorchFlame = new Vector3(0f, 0.36f, 0.25f);

        /// <summary>
        /// What is in each room. Everything is stood against a wall by measurement,
        /// the way the houses are furnished, so a change to the modules cannot leave
        /// a bookcase standing in the rock.
        /// </summary>
        private static void Furnish(Transform parent)
        {
            Room landing = RoomNamed("Landing");
            Torch(parent, landing, Side.West, -8f);
            Torch(parent, landing, Side.East, -8f);
            // The cultists' colours beside the way out, on the cell the door is not in.
            Against(Prop("Banner_Vertical_1", parent, 0f), landing, Side.South, landing.MaxX - Cell * 0.5f, 0.4f);

            Room hall = RoomNamed("Antechamber");
            Torch(parent, hall, Side.North, 0f);
            Torch(parent, hall, Side.South, 6f);
            Against(Prop("Vase_Rubble_Large", parent, 20f, false), hall, Side.West, 3.5f);
            Against(Prop("Skull", parent, 40f), hall, Side.West, 2.9f);
            Against(Prop("Chain_Coil", parent, 0f, false), hall, Side.East, 4f);
            Against(Prop("Cage_Small", parent, 15f), hall, Side.South, 6.2f);
            Against(Prop("Banner_Vertical_1", parent, 0f), hall, Side.North, 6f, 0.35f);
            Against(Prop("Barrel_Dark", parent, 0f), hall, Side.North, -0.4f);

            // The gate the antechamber's east passage is barred by: two iron fence
            // modules across its mouth, on the antechamber's side of the line so the
            // sanctum shows through them from the hall.
            Room gatePassage = RoomNamed("GatePassage");
            Transform bars = Child(parent, "IronGate");
            foreach (float z in new[] { gatePassage.MinZ + Cell * 0.5f, gatePassage.MinZ + Cell * 1.5f })
                DemoVillageBuilder.Place("Prop_MetalFence_Simple", bars, new Vector3(gatePassage.MinX, 0f, z), 90f);
            Torch(parent, gatePassage, Side.North, gatePassage.Centre.x + 0.9f);

            Room northPassage = RoomNamed("NorthPassage");
            Torch(parent, northPassage, Side.West, northPassage.Centre.z);

            Room barracks = RoomNamed("Barracks");
            Torch(parent, barracks, Side.South, -0.5f);
            Torch(parent, barracks, Side.North, 6.5f);
            Against(Prop("Bed_Bunk", parent, 90f), barracks, Side.West, 11f);
            Against(Prop("Bed_Bunk", parent, 90f), barracks, Side.West, 13.6f);
            Against(Prop("Cage_Large", parent, 180f), barracks, Side.North, 3.2f);
            Against(Prop("Cage_Large", parent, 160f), barracks, Side.North, 4.4f);
            Against(Prop("Crate_Metal", parent, 15f), barracks, Side.East, 14.2f);
            Against(Prop("Barrel_Dark", parent, 0f), barracks, Side.East, 13.4f);
            Against(Prop("Barrel_Dark", parent, 0f), barracks, Side.South, 7.6f);
            Against(Prop("Barrel_Dark", parent, 0f), barracks, Side.South, 8.35f);
            GameObject table = Prop("Table_Large", parent, 0f);
            table.transform.localPosition = new Vector3(barracks.Centre.x + 1.5f, 0f, barracks.Centre.z + 0.6f);
            On(Prop("Mug", parent, 0f, false), table, new Vector3(0.5f, 0f, 0.1f));
            On(Prop("Candle_2", parent, 0f, false), table, new Vector3(-0.3f, 0f, -0.2f));
            On(Prop("Skull_Top", parent, 250f, false), table, new Vector3(-0.9f, 0f, 0.15f));
            GameObject stool = Prop("Stool", parent, 0f);
            stool.transform.localPosition = new Vector3(barracks.Centre.x + 0.9f, 0f, barracks.Centre.z - 0.4f);
            Against(Prop("Chest_Wood", parent, 180f), barracks, Side.North, 7.4f);

            Room eastPassage = RoomNamed("EastPassage");
            Torch(parent, eastPassage, Side.South, eastPassage.Centre.x);

            Room scriptorium = RoomNamed("Scriptorium");
            Torch(parent, scriptorium, Side.West, 8.6f);
            Torch(parent, scriptorium, Side.East, 10.5f);
            Against(Prop("Bookcase_1", parent, 180f), scriptorium, Side.North, 14.9f);
            Against(Prop("Bookcase_2", parent, 180f), scriptorium, Side.North, 16.8f);
            Against(Prop("Shelf_Small_Bottles", parent, 180f), scriptorium, Side.North, 18.25f, 1.2f);
            Against(Prop("Bookcase_1", parent, 270f), scriptorium, Side.East, 12.6f);
            GameObject desk = Prop("Desk", parent, 200f);
            desk.transform.localPosition = new Vector3(scriptorium.Centre.x + 0.2f, 0f, scriptorium.Centre.z - 0.3f);
            On(Prop("Book_Open", parent, 20f, false), desk, new Vector3(0.2f, 0f, 0f));
            On(Prop("CandleStick_Triple", parent, 0f, false), desk, new Vector3(-0.55f, 0f, 0.1f));
            On(Prop("Page_Stack_Small", parent, 30f, false), desk, new Vector3(0.65f, 0f, 0.15f));
            GameObject chair = Prop("Chair_1", parent, 20f);
            chair.transform.localPosition = desk.transform.localPosition + new Vector3(-0.15f, 0f, 0.75f);
            Against(Prop("BookStand", parent, 0f), scriptorium, Side.South, 13.8f);
            Against(Prop("Scroll_1", parent, 70f, false), scriptorium, Side.West, 7.6f);
            Against(Prop("Book_Stack_2", parent, 15f, false), scriptorium, Side.West, 8.2f);

            Room southPassage = RoomNamed("SouthPassage");
            Torch(parent, southPassage, Side.East, southPassage.Centre.z);

            Room sanctum = RoomNamed("Sanctum");
            FurnishSanctum(parent, sanctum);

            Room ossuary = RoomNamed("Ossuary");
            Torch(parent, ossuary, Side.North, 26f);
            Against(Prop("Chest_Wood", parent, 90f), ossuary, Side.East, -3f);
            Against(Prop("Skull", parent, 30f), ossuary, Side.South, 24.4f);
            Against(Prop("Skull_Top", parent, 300f), ossuary, Side.South, 24.9f);
            Against(Prop("Skull", parent, 120f), ossuary, Side.South, 25.5f);
            Against(Prop("Skull_Top", parent, 200f), ossuary, Side.East, -1.6f);
            Against(Prop("Skull", parent, 80f), ossuary, Side.North, 24.6f);
            Against(Prop("Vase_Rubble_Medium", parent, 0f, false), ossuary, Side.North, 25.8f);
            Against(Prop("Chain_Coil", parent, 50f, false), ossuary, Side.South, 26.3f);
        }

        /// <summary>
        /// The sanctum: a two-storey hall on four piers, a fire in the middle under a
        /// chandelier, and the altar against the south wall with the Hierophant's
        /// treasure behind it. The banners are the cultists' colours and the runes on
        /// the floor round the fire are the thing they came down here to do.
        /// </summary>
        private static void FurnishSanctum(Transform parent, Room sanctum)
        {
            Vector3 centre = sanctum.Centre;
            Transform piers = Child(parent, "Piers");
            foreach (float sx in new[] { -3f, 3f })
            {
                foreach (float sz in new[] { -3f, 3f })
                {
                    for (float y = 0f; y < sanctum.Ceiling - 0.01f; y += Storey)
                    {
                        // The quoin's bulk lies off its origin; centre it on the pier's spot.
                        GameObject pier = DemoVillageBuilder.Place("Corner_Exterior_Brick", piers, Vector3.zero, 0f);
                        if (pier == null)
                            continue;
                        Bounds bounds = DemoVillageBuilder.LocalBounds(piers, pier.transform);
                        pier.transform.localPosition = new Vector3(centre.x + sx - bounds.center.x, y, centre.z + sz - bounds.center.z);
                    }
                }
            }

            GameObject firepit = Prop("Firepit", parent, 0f);
            firepit.transform.localPosition = centre;
            DemoFlameBuilder.Light(DemoFlameBuilder.CampfireFlamePath, firepit.transform, new Vector3(0f, 0.85f, 0f), DemoTorch.Schedule.Always);
            GameObject runes = Prop("Runes", parent, 0f, false);
            runes.transform.localPosition = centre + new Vector3(0f, 0.015f, 0f);
            runes.transform.localScale = Vector3.one * 2.2f;

            GameObject chandelier = Prop("Chandelier", parent, 0f, false);
            chandelier.transform.localPosition = centre + new Vector3(0f, sanctum.Ceiling - 0.05f, 0f);

            // The west wall is open where the gate passage comes in and the east wall
            // where the ossuary does, so what hangs on those walls keeps to their closed
            // halves: the south end of the west wall, and both ends of the east.
            Torch(parent, sanctum, Side.West, centre.z - 0.5f);
            Torch(parent, sanctum, Side.East, centre.z + 3.5f);
            Torch(parent, sanctum, Side.East, centre.z - 4.6f);
            Torch(parent, sanctum, Side.North, centre.x - 4f);
            Torch(parent, sanctum, Side.North, centre.x + 4f);
            foreach (float along in new[] { centre.x - 4.5f, centre.x - 1.5f, centre.x + 1.5f, centre.x + 4.5f })
                Against(Prop("Banner_Vertical_1", parent, 0f), sanctum, Side.South, along, 0.4f);
            Against(Prop("Banner_Vertical_1", parent, 0f), sanctum, Side.East, centre.z - 2.8f, 0.4f);
            Against(Prop("Banner_Vertical_1", parent, 0f), sanctum, Side.West, centre.z - 1.9f, 0.4f);

            // The altar: the pack's long table dressed for the rite, and the chest of
            // everything the cult has taken behind it, in the corner the piers frame.
            GameObject altar = Prop("Table_Large", parent, 0f);
            Against(altar, sanctum, Side.South, centre.x, 0f, 0.9f);
            On(Prop("Chalice_Golden", parent, 0f, false), altar, new Vector3(-0.3f, 0f, 0f));
            On(Prop("Skull", parent, 190f, false), altar, new Vector3(0.45f, 0f, -0.05f));
            On(Prop("Book_Open", parent, 175f, false), altar, new Vector3(0.05f, 0f, 0.15f));
            On(Prop("Candle_1", parent, 0f, false), altar, new Vector3(-1.0f, 0f, 0.1f));
            On(Prop("Candle_3", parent, 0f, false), altar, new Vector3(1.05f, 0f, -0.1f));
            Against(Prop("CandleStick_Stand", parent, 0f), sanctum, Side.South, centre.x - 2.4f);
            Against(Prop("CandleStick_Stand", parent, 0f), sanctum, Side.South, centre.x + 2.4f);
            Against(Prop("Chest_Legendary", parent, 0f), sanctum, Side.South, centre.x, 0f, 0.05f);
            Against(Prop("Cauldron", parent, 0f), sanctum, Side.East, centre.z + 1.6f);
            Against(Prop("Cage_Large", parent, 250f), sanctum, Side.North, centre.x + 5.2f);
            Against(Prop("Bench_MetalSides", parent, 90f), sanctum, Side.West, centre.z - 4.3f);
        }

        private static GameObject Prop(string name, Transform parent, float yaw, bool solid = true)
        {
            GameObject instance = DemoSceneBuilder.Prop(name, parent, Vector3.zero, yaw, solid);
            if (instance == null)
                throw new System.InvalidOperationException($"No prop named {name}.");
            return instance;
        }

        /// <summary>
        /// Stands a prop against a wall of a room: <paramref name="along"/> is the
        /// world coordinate along the wall, <paramref name="height"/> how far up it is
        /// hung, <paramref name="gap"/> how far off the wall face it stands. Measured
        /// off the prop's own extent in the room's space, so the back of whatever it is
        /// touches the wall's inner face exactly.
        /// </summary>
        private static void Against(GameObject instance, Room room, Side wall, float along, float height = 0f, float gap = 0f)
        {
            Transform space = instance.transform.parent;
            Bounds bounds = DemoVillageBuilder.LocalBounds(space, instance.transform);
            Vector3 move;
            switch (wall)
            {
                case Side.North:
                    move = new Vector3(along - bounds.center.x, 0f, room.MaxZ - WallInset - gap - bounds.max.z);
                    break;
                case Side.South:
                    move = new Vector3(along - bounds.center.x, 0f, room.MinZ + WallInset + gap - bounds.min.z);
                    break;
                case Side.East:
                    move = new Vector3(room.MaxX - WallInset - gap - bounds.max.x, 0f, along - bounds.center.z);
                    break;
                default:
                    move = new Vector3(room.MinX + WallInset + gap - bounds.min.x, 0f, along - bounds.center.z);
                    break;
            }
            move.y = room.Floor + 0.01f + height - bounds.min.y;
            // Flat things laid on the floor share its plane and z-fight with it.
            if (bounds.size.y < 0.02f)
                move.y += 0.005f;
            instance.transform.localPosition += move;
        }

        /// <summary>Rests a small thing on top of a bigger one, at an offset across its top.</summary>
        private static void On(GameObject small, GameObject big, Vector3 offset)
        {
            Transform space = small.transform.parent;
            Bounds top = DemoVillageBuilder.LocalBounds(space, big.transform);
            Bounds bounds = DemoVillageBuilder.LocalBounds(space, small.transform);
            Vector3 target = big.transform.localPosition + big.transform.localRotation * offset;
            small.transform.localPosition += new Vector3(target.x - bounds.center.x, top.max.y - bounds.min.y, target.z - bounds.center.z);
        }

        /// <summary>
        /// A torch on a wall, lit and kept lit: there is no day down here for it to go
        /// out in. Brighter and further-reaching than the village's, because it is the
        /// only light there is.
        /// </summary>
        private static void Torch(Transform parent, Room room, Side wall, float along)
        {
            GameObject torch = Prop(TorchModel, parent, FacingYaw(wall), false);
            torch.name = $"Torch_{room.Name}_{wall}";
            Against(torch, room, wall, along, TorchHeight);
            DemoTorch flame = DemoFlameBuilder.Light(DemoFlameBuilder.TorchFlamePath, torch.transform, TorchFlame, DemoTorch.Schedule.Always);
            if (flame == null)
                return;
            flame.intensity = 2.4f;
            if (flame.lamp != null)
                flame.lamp.range = 11f;
            flame.Settle();
        }

        // ---- who lives here -----------------------------------------------------

        /// <summary>
        /// The cultists, and their Hierophant at the altar. Levels run above the island's:
        /// this is where a character who has cleared the hills comes next. The sanctum's
        /// guard stands in the hall's north half, so a player coming down the south passage
        /// meets them before the altar and the Hierophant is a second fight, not a pile-on.
        /// </summary>
        private static void BuildSpawners(Scene scene)
        {
            var root = new GameObject("Spawners");
            SceneManager.MoveGameObjectToScene(root, scene);

            MonsterCharacterEntity cultistMale = LoadEntity($"{EntityDir}/DemoCultistMale.prefab");
            MonsterCharacterEntity cultistFemale = LoadEntity($"{EntityDir}/DemoCultistFemale.prefab");
            MonsterCharacterEntity hierophant = LoadEntity($"{EntityDir}/DemoHierophant.prefab");
            if (cultistMale == null || cultistFemale == null || hierophant == null)
                return;

            Room barracks = RoomNamed("Barracks");
            Room scriptorium = RoomNamed("Scriptorium");
            Room sanctum = RoomNamed("Sanctum");
            Spawner(root.transform, "Spawn_Barracks", barracks.Centre + new Vector3(-0.6f, 0f, -1.6f), 1.6f, cultistMale, 7, 8, 2, 40f);
            Spawner(root.transform, "Spawn_Scriptorium", scriptorium.Centre + new Vector3(-0.5f, 0f, 1.5f), 1.8f, cultistFemale, 8, 9, 2, 40f);
            Spawner(root.transform, "Spawn_Sanctum", sanctum.Centre + new Vector3(0f, 0f, 3.5f), 3f, cultistMale, 9, 9, 3, 60f);
            Spawner(root.transform, "Spawn_Hierophant", sanctum.Centre + new Vector3(0f, 0f, -3.6f), 0.5f, hierophant, 10, 10, 1, 120f);
        }

        private static MonsterCharacterEntity LoadEntity(string path)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                Debug.LogError($"[{nameof(DemoDungeonBuilder)}] Missing \"{path}\". Run Open MMORPG > Demo > Build Character Entities first.");
                return null;
            }
            return prefab.GetComponent<MonsterCharacterEntity>();
        }

        private static void Spawner(Transform parent, string name, Vector3 centre, float radius, MonsterCharacterEntity prefab, int minLevel, int maxLevel, int amount, float respawnSeconds)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = centre;
            var area = go.AddComponent<MonsterSpawnArea>();
            var serialized = new SerializedObject(area);
            serialized.FindProperty("prefab").objectReferenceValue = prefab;
            serialized.FindProperty("randomRadius").floatValue = radius;
            serialized.FindProperty("minLevel").intValue = minLevel;
            serialized.FindProperty("maxLevel").intValue = maxLevel;
            serialized.FindProperty("minAmount").intValue = amount;
            serialized.FindProperty("maxAmount").intValue = amount;
            serialized.FindProperty("destroyRespawnDelay").floatValue = respawnSeconds;
            // Everything but the ceilings, or the ground ray stops at the roof.
            serialized.FindProperty("groundLayerMask").intValue = ~(1 << CeilingLayer);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void BakeNavMesh(Scene scene)
        {
            var go = new GameObject("Navigation");
            SceneManager.MoveGameObjectToScene(go, scene);
            NavMeshSurface surface = go.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            // Not the ceilings: their upper faces would bake into islands of walkable
            // surface on top of the rooms, which the monsters' wander could pick and
            // never reach.
            surface.layerMask = ~(1 << CeilingLayer);
            surface.BuildNavMesh();
        }

        // ---- the map, and the way in ---------------------------------------------

        /// <summary>
        /// The map info the kit knows the dungeon by. Its start position is the landing,
        /// though nothing starts here: a character that dies in the crypt respawns
        /// wherever its respawn map says, which the warp does not change, so it wakes up
        /// on the village green and walks back. That is the dungeon's cost.
        /// </summary>
        private static void BuildMapInfo()
        {
            DemoItemBuilder.EnsureFolder("Assets/OpenMMORPG/Demo/GameData/Resources/MapInfos");
            var map = AssetDatabase.LoadAssetAtPath<MapInfo>(MapInfoPath);
            if (map == null)
            {
                map = ScriptableObject.CreateInstance<MapInfo>();
                AssetDatabase.CreateAsset(map, MapInfoPath);
            }
            var serialized = new SerializedObject(map);
            serialized.FindProperty("id").stringValue = MapId;
            serialized.FindProperty("defaultTitle").stringValue = MapTitle;
            serialized.FindProperty("scene.sceneAsset").objectReferenceValue = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            serialized.FindProperty("scene.sceneName").stringValue = System.IO.Path.GetFileNameWithoutExtension(ScenePath);
            serialized.FindProperty("startPosition").vector3Value = ArrivalPosition;
            serialized.FindProperty("startRotation").vector3Value = new Vector3(0f, ArrivalYaw, 0f);
            serialized.FindProperty("deadY").floatValue = -30f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(map);
        }

        /// <summary>
        /// The gate: the kit's warp portal with its glow taken off and its trigger cut
        /// down to a doorway. The kit's portal is a swirl of particles standing in the
        /// open, which is the right thing for a portal and the wrong thing for a door —
        /// the crypt's own archway and the dark inside it are the signal here. What is
        /// left is the entity and the trigger, and a prefab of its own so it has a
        /// network id of its own.
        ///
        /// The entity itself is swapped for <see cref="DemoDungeonGate"/>, which sends
        /// each character through once: a character's capsule and its hit boxes are all
        /// tagged as the player, and every one of them entering the trigger is a
        /// request to warp. Two scene changes started in the same frame hung the editor.
        /// </summary>
        private static void BuildGatePrefab()
        {
            GameObject template = AssetDatabase.LoadAssetAtPath<GameObject>(PortalTemplatePath);
            if (template == null)
            {
                Debug.LogError($"[{nameof(DemoDungeonBuilder)}] No warp portal prefab at \"{PortalTemplatePath}\".");
                return;
            }
            GameObject gate = (GameObject)PrefabUtility.InstantiatePrefab(template);
            PrefabUtility.UnpackPrefabInstance(gate, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            gate.name = System.IO.Path.GetFileNameWithoutExtension(GatePrefabPath);
            var doomed = new List<GameObject>();
            foreach (Transform child in gate.transform)
                doomed.Add(child.gameObject);
            foreach (GameObject child in doomed)
                Object.DestroyImmediate(child);
            var portal = gate.GetComponent<WarpPortalEntity>();
            if (portal != null && !(portal is DemoDungeonGate))
                Object.DestroyImmediate(portal);
            if (gate.GetComponent<DemoDungeonGate>() == null)
            {
                var demoGate = gate.AddComponent<DemoDungeonGate>();
                var serialized = new SerializedObject(demoGate);
                serialized.FindProperty("warpImmediatelyWhenEnter").boolValue = true;
                serialized.FindProperty("warpSignals").arraySize = 0;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            var box = gate.GetComponent<BoxCollider>();
            if (box == null)
                box = gate.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = GateTriggerSize;
            box.center = new Vector3(0f, GateTriggerSize.y * 0.5f, 0f);
            PrefabUtility.SaveAsPrefabAsset(gate, GatePrefabPath);
            Object.DestroyImmediate(gate);
            DemoEntityBuilder.GiveOwnNetworkId(GatePrefabPath);
            AssetDatabase.ImportAsset(GatePrefabPath, ImportAssetOptions.ForceUpdate);
        }
    }
}
