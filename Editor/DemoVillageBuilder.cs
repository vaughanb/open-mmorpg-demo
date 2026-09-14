using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// Assembles houses from the Quaternius Medieval Village modules.
    ///
    /// The pack ships parts rather than buildings — walls, corners, roofs — all on a
    /// 2m grid with 3m walls, so a house is a footprint in grid cells with walls run
    /// round the perimeter and a roof of the matching size dropped on top.
    /// </summary>
    public static class DemoVillageBuilder
    {
        private const string PrefabDir = "Assets/Plugins/Quaternius/Village/Prefabs";

        /// <summary>Module footprint. Walls are this wide, floors this square.</summary>
        public const float Cell = 2f;

        /// <summary>Height of a wall module, and so where the roof sits.</summary>
        public const float WallHeight = 3f;

        /// <summary>
        /// The clear space inside a house: floor top, and the inner faces of its walls.
        ///
        /// Measured from the modules that were actually placed rather than worked out
        /// from the grid, because a wall module is not flush with the line it is placed
        /// on — it straddles it, sitting 0.09 proud on the inside — and the floor's
        /// surface is a centimetre above the origin. Furniture positioned on the grid
        /// figure alone stands a hand's width away from every wall and a centimetre
        /// into the floor.
        /// </summary>
        public static Bounds InteriorBounds(Transform house)
        {
            Bounds floor = new Bounds();
            bool anyFloor = false;
            float minX = float.MinValue, maxX = float.MaxValue;
            float minZ = float.MinValue, maxZ = float.MaxValue;

            foreach (Transform child in house)
            {
                Bounds local = LocalBounds(house, child);
                if (child.name.StartsWith("Floor_"))
                {
                    if (anyFloor)
                        floor.Encapsulate(local);
                    else
                    {
                        floor = local;
                        anyFloor = true;
                    }
                    continue;
                }
                if (!child.name.StartsWith("Wall_"))
                    continue;

                // A wall run is long on one axis and thin on the other; the thin axis is
                // the one it walls off, and the face nearer the middle is the inner one.
                if (local.size.x < local.size.z)
                {
                    if (local.center.x < 0f) minX = Mathf.Max(minX, local.max.x);
                    else maxX = Mathf.Min(maxX, local.min.x);
                }
                else
                {
                    if (local.center.z < 0f) minZ = Mathf.Max(minZ, local.max.z);
                    else maxZ = Mathf.Min(maxZ, local.min.z);
                }
            }

            var bounds = new Bounds();
            bounds.SetMinMax(
                new Vector3(minX, anyFloor ? floor.max.y : 0f, minZ),
                new Vector3(maxX, anyFloor ? floor.max.y + WallHeight : WallHeight, maxZ));
            return bounds;
        }

        /// <summary>
        /// An object's extent in another transform's space.
        ///
        /// <see cref="Renderer.bounds"/> cannot be used for this: it is an axis-aligned
        /// box in world space, so for anything turned off the world axes — and every
        /// house on the green is — its size is the inflated box around the rotation, not
        /// the object. Measuring the mesh's own corners through the transform gives the
        /// real extent.
        /// </summary>
        public static Bounds LocalBounds(Transform space, Transform obj)
        {
            var bounds = new Bounds();
            bool any = false;
            // Every renderer, not just the MeshFilter ones: several of the props - the
            // chests among them - are skinned, so they carry their mesh on the renderer
            // and have no filter at all. Looking only for filters measures those as
            // nothing, and a piece measured as nothing is placed by its pivot alone,
            // which puts it through the wall it was meant to stand against.
            foreach (Renderer renderer in obj.GetComponentsInChildren<Renderer>())
            {
                Mesh mesh = null;
                var skinned = renderer as SkinnedMeshRenderer;
                if (skinned != null)
                    mesh = skinned.sharedMesh;
                else
                {
                    MeshFilter filter = renderer.GetComponent<MeshFilter>();
                    if (filter != null)
                        mesh = filter.sharedMesh;
                }
                if (mesh == null)
                    continue;

                Bounds local = mesh.bounds;
                for (int i = 0; i < 8; ++i)
                {
                    var corner = new Vector3(
                        (i & 1) == 0 ? local.min.x : local.max.x,
                        (i & 2) == 0 ? local.min.y : local.max.y,
                        (i & 4) == 0 ? local.min.z : local.max.z);
                    Vector3 point = space.InverseTransformPoint(renderer.transform.TransformPoint(corner));
                    if (any)
                        bounds.Encapsulate(point);
                    else
                    {
                        bounds = new Bounds(point, Vector3.zero);
                        any = true;
                    }
                }
            }
            return bounds;
        }

        public static GameObject Prefab(string name)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/{name}.prefab");
            if (prefab == null)
                Debug.LogError($"[{nameof(DemoVillageBuilder)}] No village prefab named \"{name}\".");
            return prefab;
        }

        public static GameObject Place(string prefabName, Transform parent, Vector3 position, float yaw)
        {
            GameObject prefab = Prefab(prefabName);
            if (prefab == null)
                return null;
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            instance.transform.localPosition = position;
            instance.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            return instance;
        }

        /// <summary>
        /// Hangs a working door in a doorway.
        ///
        /// The door model's pivot is on its hinge with the leaf extending along +X, so
        /// swinging it is a rotation of that transform and nothing else. It is offset
        /// half its own width so the closed leaf fills the opening rather than sitting
        /// against one jamb.
        ///
        /// The trigger that opens it lives on the doorway, which does not move — put on
        /// the leaf, it would swing away with the door and lose track of whoever opened
        /// it.
        /// </summary>
        private static void HangDoor(Transform parent, Vector3 wallPosition, float wallYaw, bool brick)
        {
            GameObject prefab = Prefab("Door_1_Round");
            if (prefab == null)
                return;

            // The leaf is 1.12 wide and the wall's opening is wider than that, so without
            // a frame there is a strip of daylight down one side however well the leaf is
            // centred. The frame closes the opening down to the leaf's own width.
            Place(brick ? "DoorFrame_Round_Brick" : "DoorFrame_Round_WoodDark", parent, wallPosition, wallYaw);

            var doorway = new GameObject("Doorway");
            doorway.transform.SetParent(parent, false);
            doorway.transform.localPosition = wallPosition;
            doorway.transform.localRotation = Quaternion.Euler(0f, wallYaw, 0f);

            // The leaf is modelled offset from its own hinge, and not by exactly half its
            // width, so the shift that centres it in the opening is measured off the
            // model rather than assumed. Getting this from the width instead leaves a
            // finger's width of daylight down one jamb.
            var hinge = new GameObject("Hinge");
            hinge.transform.SetParent(doorway.transform, false);
            hinge.transform.localPosition = new Vector3(-LeafCentreOffset(prefab), 0f, 0f);

            var leaf = (GameObject)PrefabUtility.InstantiatePrefab(prefab, hinge.transform);
            leaf.transform.localPosition = Vector3.zero;
            leaf.transform.localRotation = Quaternion.identity;

            // A doorway is 1.12 wide and the navmesh is baked for an agent half a metre
            // across, which erodes a metre out of any opening — so the doorway closes in
            // the bake and every interior becomes an island of navmesh with no way onto
            // it. A click inside a house then walks the player to the doorstep and
            // stops. A link carries the navmesh across the threshold explicitly, which
            // is what links are for, and it is saved in the scene so it survives being
            // imported elsewhere as a change to the project's agent radius would not.
            var crossing = doorway.AddComponent<NavMeshLink>();
            crossing.startPoint = new Vector3(0f, 0f, 1.1f);
            crossing.endPoint = new Vector3(0f, 0f, -1.1f);
            crossing.width = 0.9f;
            crossing.bidirectional = true;

            var door = doorway.AddComponent<MultiplayerARPG.Demo.DemoDoor>();
            door.pivot = hinge.transform;
            // The wall's local -Z faces into the house, and a positive turn carries the
            // leaf that way, so the door opens inward and away from whoever is arriving.
            door.openAngle = 100f;

            // The handle goes on whatever owns the leaf's collider, which is not the leaf's
            // own root: the pack nests the mesh one level down under a child of the same
            // name, so both are called "Door_1_Round" and it is easy to put this on the
            // wrong one. The controller looks for something to activate with GetComponent
            // on the collider its aim ray passes through - on that object and nowhere else
            // in the hierarchy - so anywhere but the collider's object is invisible to it.
            Collider leafCollider = leaf.GetComponentInChildren<Collider>();
            if (leafCollider == null)
            {
                Debug.LogError($"[{nameof(DemoVillageBuilder)}] The door leaf has no collider, so it " +
                               "cannot be opened or walked into.");
                return;
            }
            var handle = leafCollider.gameObject.AddComponent<MultiplayerARPG.Demo.DemoDoorHandle>();
            handle.door = door;
        }

        /// <summary>
        /// How far the leaf's middle sits from its hinge, along the leaf.
        ///
        /// Measured in the prefab's own root space, not the mesh's: the offset that
        /// puts the hinge at one edge lives on the child transform inside the prefab,
        /// so reading the mesh bounds alone reports zero and centres nothing.
        /// </summary>
        private static float LeafCentreOffset(GameObject prefab)
        {
            float min = float.MaxValue;
            float max = float.MinValue;
            foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true))
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
                    float x = filter.transform.TransformPoint(corner).x;
                    min = Mathf.Min(min, x);
                    max = Mathf.Max(max, x);
                }
            }
            return min > max ? 0f : (min + max) * 0.5f;
        }

        /// <summary>
        /// Places a wall, and glazes it when it is a windowed one.
        ///
        /// The windowed wall modules are cut with an opening and nothing in it, so on
        /// their own you see straight through the house and out the far side. The window
        /// insert is authored against the same module origin, so it drops into the wall's
        /// own transform without an offset.
        /// </summary>
        private static void PlaceWall(bool windowed, string window, string plain, Transform parent, Vector3 position, float yaw)
        {
            Place(windowed ? window : plain, parent, position, yaw);
            if (windowed)
                Place("Window_Wide_Round1", parent, position, yaw);
        }

        /// <summary>
        /// Builds one house, <paramref name="cellsX"/> by <paramref name="cellsZ"/> grid
        /// cells, centred on the parent's origin. The door goes on the south face; the
        /// remaining walls alternate plain and windowed so no elevation is blank.
        /// </summary>
        public static void BuildHouse(Transform parent, int cellsX, int cellsZ, bool brick)
        {
            string plain = brick ? "Wall_UnevenBrick_Straight" : "Wall_Plaster_Straight";
            string window = brick ? "Wall_UnevenBrick_Window_Wide_Round" : "Wall_Plaster_Window_Wide_Round";
            string doorway = brick ? "Wall_UnevenBrick_Door_Round" : "Wall_Plaster_Door_Round";

            float halfX = cellsX * Cell * 0.5f;
            float halfZ = cellsZ * Cell * 0.5f;

            for (int x = 0; x < cellsX; ++x)
            {
                for (int z = 0; z < cellsZ; ++z)
                {
                    Place("Floor_WoodDark", parent,
                        new Vector3(-halfX + Cell * (x + 0.5f), 0f, -halfZ + Cell * (z + 0.5f)), 0f);
                }
            }

            // The door is placed in the middle of the south wall run.
            int doorCell = cellsX / 2;
            for (int x = 0; x < cellsX; ++x)
            {
                float px = -halfX + Cell * (x + 0.5f);
                var wallPosition = new Vector3(px, 0f, -halfZ);
                Place(x == doorCell ? doorway : plain, parent, wallPosition, 180f);
                if (x == doorCell)
                    HangDoor(parent, wallPosition, 180f, brick);
                PlaceWall(x % 2 == 0, window, plain, parent, new Vector3(px, 0f, halfZ), 0f);
            }
            for (int z = 0; z < cellsZ; ++z)
            {
                float pz = -halfZ + Cell * (z + 0.5f);
                PlaceWall(z % 2 == 0, window, plain, parent, new Vector3(halfX, 0f, pz), 90f);
                PlaceWall(z % 2 == 1, window, plain, parent, new Vector3(-halfX, 0f, pz), 270f);
            }

            // Corner quoins cover the notch where two wall runs meet. Each wall sits
            // slightly outside the footprint line it is placed on — the modules are
            // 0.41 deep, offset outward — so the two runs never actually touch at a
            // corner and the join shows through.
            //
            // The quoin's own geometry sits toward its -X/-Z, which is only outward at
            // the south-west corner. Turning it a quarter at a time walks that corner
            // anticlockwise: 0 south-west, 90 north-west, 180 north-east, 270 south-east.
            // Getting that order wrong turns it inward and leaves the gap open.
            //
            // The stone quoin goes on every house, plastered or not. The wooden post that
            // used to go on the plastered ones is **0.21m across against the stone one's
            // 0.53m**, and the notch it has to cover is the walls' own 0.41m depth — so it
            // never stood a chance, and a slot of daylight showed down every plastered
            // corner. Stone quoins on a rendered wall are ordinary building anyway.
            const string corner = "Corner_Exterior_Brick";
            Place(corner, parent, new Vector3(-halfX, 0f, -halfZ), 0f);
            Place(corner, parent, new Vector3(-halfX, 0f, halfZ), 90f);
            Place(corner, parent, new Vector3(halfX, 0f, halfZ), 180f);
            Place(corner, parent, new Vector3(halfX, 0f, -halfZ), 270f);

            // Roof modules come in fixed footprints, so the house is sized to match one.
            string roof = $"Roof_RoundTiles_{cellsX * (int)Cell}x{cellsZ * (int)Cell}";
            if (Prefab(roof) == null)
                roof = "Roof_RoundTiles_4x4";
            Place(roof, parent, new Vector3(0f, WallHeight, 0f), 0f);

            // The ridge runs along Z, leaving a triangular gap over the north and south
            // walls that you can otherwise see straight through into the house.
            string gable = $"Roof_Front_Brick{cellsX * (int)Cell}";
            if (Prefab(gable) != null)
            {
                Place(gable, parent, new Vector3(0f, WallHeight, -halfZ), 180f);
                Place(gable, parent, new Vector3(0f, WallHeight, halfZ), 0f);
            }
        }
    }
}
