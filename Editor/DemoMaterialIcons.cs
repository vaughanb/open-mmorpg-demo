using UnityEditor;
using UnityEngine;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// Stand-in icons for the iron materials, until painted ones arrive.
    ///
    /// The island's material icons (Stone, Timber, Leather...) are painted, and a render of a
    /// model does not sit well beside them - but an item with no icon is a blank square in the
    /// pack, which is worse. So these draw one only where there is no PNG, and a painted icon
    /// dropped in at the same path replaces it for good: nothing here overwrites a file (see
    /// [[demo-builders-must-not-eat-hand-edits]]). Delete the PNG to have it drawn again.
    ///
    /// The ore is a veined rock, the same as the nodes it comes from. The ingot has no model in
    /// any pack, so it is a bar built here: a flat-topped, sloped-sided block, the shape of an
    /// ingot cast in an open mould.
    /// </summary>
    internal static class DemoMaterialIcons
    {
        private const string IconDir = "Assets/OpenMMORPG/Demo/Textures/Icons/Items";
        private const string NatureDir = "Assets/Plugins/Quaternius/Nature/Prefabs";

        internal static void EnsureStandIn(string iconName)
        {
            string path = $"{IconDir}/{iconName}.png";
            if (System.IO.File.Exists(path))
                return;

            GameObject subject = null;
            if (iconName == "IronOre")
                subject = OreChunk();
            else if (iconName == "IronIngot")
                subject = Ingot();
            if (subject == null)
                return;

            try
            {
                Texture2D icon = DemoHomesteadBuilder.RenderIcon(subject);
                if (icon == null)
                    return;
                DemoItemBuilder.EnsureFolder(IconDir);
                System.IO.File.WriteAllBytes(path, icon.EncodeToPNG());
                Object.DestroyImmediate(icon);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
            finally
            {
                Object.DestroyImmediate(subject);
            }
        }

        /// <summary>
        /// A weapon's icon drawn from its prefab, laid on the diagonal the Equipment Icon
        /// Generator's weapon icons use (handle low-left, head up-right), if there is none at
        /// the path. For a tool the generator has not been run on yet.
        /// </summary>
        internal static void EnsureWeaponIcon(string iconPath, string prefabPath)
        {
            if (System.IO.File.Exists(iconPath))
                return;
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
                return;
            var root = new GameObject("WeaponIcon");
            try
            {
                var model = new GameObject("Model");
                model.transform.SetParent(root.transform, false);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, model.transform);
                PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                // Turned to face the icon camera, which looks in from the front right, then
                // leant over onto the diagonal.
                instance.transform.localRotation = Quaternion.Euler(0f, 35f, 0f) * Quaternion.Euler(0f, 0f, 40f);
                Texture2D icon = DemoHomesteadBuilder.RenderIcon(root, 0.72f);
                if (icon == null)
                    return;
                System.IO.File.WriteAllBytes(iconPath, icon.EncodeToPNG());
                Object.DestroyImmediate(icon);
                AssetDatabase.ImportAsset(iconPath, ImportAssetOptions.ForceUpdate);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>A piece of vein rock, small enough to be something carried.</summary>
        private static GameObject OreChunk()
        {
            GameObject rockPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{NatureDir}/Rock_Medium_2.prefab");
            if (rockPrefab == null)
                return null;
            var root = new GameObject("OreIcon");
            var model = new GameObject("Model");
            model.transform.SetParent(root.transform, false);
            var rock = (GameObject)PrefabUtility.InstantiatePrefab(rockPrefab, model.transform);
            PrefabUtility.UnpackPrefabInstance(rock, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            DemoHarvestBuilder.Veined(rock, "Rock_Medium_2");
            return root;
        }

        /// <summary>A cast bar: the top narrower than the base, the ends sloped as well as the sides.</summary>
        private static GameObject Ingot()
        {
            const float length = 1.0f, width = 0.5f, height = 0.4f, taper = 0.1f;
            float bx = length * 0.5f, bz = width * 0.5f;
            float tx = bx - taper, tz = bz - taper;
            var corners = new[]
            {
                new Vector3(-bx, 0f, -bz), new Vector3(bx, 0f, -bz), new Vector3(bx, 0f, bz), new Vector3(-bx, 0f, bz),
                new Vector3(-tx, height, -tz), new Vector3(tx, height, -tz), new Vector3(tx, height, tz), new Vector3(-tx, height, tz),
            };
            // Each face its own four vertices, so the edges stay hard under the light.
            int[][] faces =
            {
                new[] { 4, 7, 6, 5 }, // top
                new[] { 0, 1, 2, 3 }, // bottom
                new[] { 0, 4, 5, 1 }, // front
                new[] { 2, 6, 7, 3 }, // back
                new[] { 1, 5, 6, 2 }, // right
                new[] { 3, 7, 4, 0 }, // left
            };
            var vertices = new System.Collections.Generic.List<Vector3>();
            var triangles = new System.Collections.Generic.List<int>();
            foreach (int[] face in faces)
            {
                int start = vertices.Count;
                foreach (int corner in face)
                    vertices.Add(corners[corner]);
                triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
            }
            var mesh = new Mesh { name = "IngotIcon" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var root = new GameObject("IngotIcon");
            var bar = new GameObject("Model");
            bar.transform.SetParent(root.transform, false);
            bar.AddComponent<MeshFilter>().sharedMesh = mesh;
            // Never saved: it exists for the one render.
            var metal = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "IngotIcon" };
            metal.SetColor("_BaseColor", new Color(0.55f, 0.55f, 0.57f));
            metal.SetFloat("_Metallic", 0.9f);
            metal.SetFloat("_Smoothness", 0.7f);
            bar.AddComponent<MeshRenderer>().sharedMaterial = metal;
            // Turned and tipped towards the camera, so it shows its sloped end and its top
            // together; square on, it reads as a flat slab.
            bar.transform.localRotation = Quaternion.Euler(0f, 60f, 0f);
            return root;
        }
    }
}
