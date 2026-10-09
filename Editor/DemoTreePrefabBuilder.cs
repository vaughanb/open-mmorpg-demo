using UnityEditor;
using UnityEngine;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// Makes terrain-ready copies of the Quaternius tree and bush prefabs.
    ///
    /// Unity only instances terrain trees when the prefab carries its MeshFilter and
    /// MeshRenderer on its own root. The pack puts them on a child, one level down, so
    /// the terrain refuses to instance them — "couldn't be instanced because the prefab
    /// contains no valid mesh renderer" — and falls back to drawing every tree
    /// individually. Flattening the hierarchy is all that is needed; the child sits at
    /// identity, so nothing has to be baked into the mesh to move it.
    ///
    /// Trees also get a trunk collider. Terrain trees take their collision from the
    /// prototype prefab, and a capsule round the trunk is far cheaper than a mesh
    /// collider on a tree with foliage. Bushes get none, so the player can push through
    /// undergrowth rather than being fenced in by it.
    ///
    /// Trees and bushes get a LODGroup as well - see <see cref="AddLodGroup"/>.
    /// </summary>
    public static class DemoTreePrefabBuilder
    {
        private const string SourceDir = "Assets/Plugins/Quaternius/Nature/Prefabs";
        public const string OutputDir = "Assets/OpenMMORPG/Demo/Prefabs/Terrain";

        public static readonly string[] Trees =
        {
            "CommonTree_1", "CommonTree_2", "CommonTree_3", "CommonTree_4", "CommonTree_5",
            "Pine_1", "Pine_2", "Pine_3", "Pine_4", "Pine_5",
            "TwistedTree_1", "TwistedTree_2", "TwistedTree_3", "TwistedTree_4", "TwistedTree_5",
            "DeadTree_1", "DeadTree_2", "DeadTree_3", "DeadTree_4", "DeadTree_5",
        };

        public static readonly string[] Bushes =
        {
            "Bush_Common_Flowers", "Plant_1", "Plant_1_Big", "Plant_7", "Plant_7_Big",
        };

        /// <summary>
        /// Undergrowth drawn as terrain detail instances. Instanced details are subject
        /// to the same root-renderer rule as trees, so these need flattening too — with
        /// no collider, since grass should not stop anyone.
        /// </summary>
        public static readonly string[] Details =
        {
            "Grass_Common_Short", "Grass_Common_Tall", "Grass_Wispy_Short", "Grass_Wispy_Tall",
            "Clover_1", "Clover_2", "Fern_1", "Flower_3_Group", "Flower_4_Group",
            "Mushroom_Common",
        };

        [MenuItem("Open MMORPG/Demo/Build Terrain Tree Prefabs")]
        public static void BuildAll()
        {
            DemoIslandBuilder.EnsureFolder(OutputDir);
            foreach (string name in Trees)
                Build(name, true, true);
            foreach (string name in Bushes)
                Build(name, false, true);
            foreach (string name in Details)
                Build(name, false, false);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            // The tree materials switch to the wind shader here too, so the one step leaves the
            // forest ready to blow. Idempotent, and a no-op for any material not collected yet.
            DemoWindMaterials.Apply();
            Debug.Log($"[{nameof(DemoTreePrefabBuilder)}] Built {Trees.Length + Bushes.Length + Details.Length} terrain prefabs in {OutputDir}.");
        }

        /// <summary>The terrain-ready copy of a source prefab, or null if it was not built.</summary>
        public static GameObject Load(string name)
        {
            return AssetDatabase.LoadAssetAtPath<GameObject>($"{OutputDir}/{name}.prefab");
        }

        /// <param name="solid">Gets a trunk collider.</param>
        /// <param name="tree">Drawn by the terrain as a tree rather than as detail.</param>
        private static void Build(string name, bool solid, bool tree)
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>($"{SourceDir}/{name}.prefab");
            if (source == null)
            {
                Debug.LogWarning($"[{nameof(DemoTreePrefabBuilder)}] No source prefab \"{name}\".");
                return;
            }
            MeshFilter sourceFilter = source.GetComponentInChildren<MeshFilter>(true);
            MeshRenderer sourceRenderer = source.GetComponentInChildren<MeshRenderer>(true);
            if (sourceFilter == null || sourceRenderer == null)
            {
                Debug.LogWarning($"[{nameof(DemoTreePrefabBuilder)}] \"{name}\" has no mesh to flatten.");
                return;
            }

            var flattened = new GameObject(name);
            flattened.AddComponent<MeshFilter>().sharedMesh = sourceFilter.sharedMesh;
            MeshRenderer renderer = flattened.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = sourceRenderer.sharedMaterials;
            // Bushes and details are the small plants the wind treats softly and measures from the
            // ground; the pack's own materials stay on the trees. See DemoWindMaterials.
            if (!solid)
                renderer.sharedMaterials = DemoWindMaterials.ForSmallPlant(renderer.sharedMaterials);
            if (tree)
                AddLodGroup(flattened, renderer);

            if (solid)
            {
                Bounds bounds = sourceFilter.sharedMesh.bounds;
                var trunk = flattened.AddComponent<CapsuleCollider>();
                trunk.height = bounds.size.y;
                // A fraction of the canopy's spread: the trunk is a fraction of the tree.
                trunk.radius = Mathf.Max(0.22f, Mathf.Min(bounds.size.x, bounds.size.z) * 0.09f);
                trunk.center = new Vector3(0f, bounds.size.y * 0.5f, 0f);
            }

            PrefabUtility.SaveAsPrefabAsset(flattened, $"{OutputDir}/{name}.prefab");
            Object.DestroyImmediate(flattened);
        }

        /// <summary>
        /// Gives a terrain tree one level of detail - its own renderer, never culled.
        ///
        /// The terrain checks a tree without a LODGroup for the built-in pipeline's Soft
        /// Occlusion shaders, which it makes billboards with, and these are URP Lit, so the
        /// island warned about each of them whenever it loaded fresh: "The tree Plant_1 must
        /// use the Nature/Soft Occlusion shader", ten lines every Build Map Server. It was
        /// only noise - the island never billboards (see DemoSceneBuilder.BuildTerrain) - but
        /// a tree with a LODGroup is drawn from its levels and not checked at all. A threshold
        /// of zero means the group never culls by size, so the terrain's tree distance still
        /// decides how far the undergrowth carries, as it did before.
        /// </summary>
        private static void AddLodGroup(GameObject tree, Renderer renderer)
        {
            LODGroup group = tree.AddComponent<LODGroup>();
            group.SetLODs(new[] { new LOD(0f, new Renderer[] { renderer }) });
            group.RecalculateBounds();
        }
    }
}
