using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// The minimap: the island seen from above, and the three references that carry it.
    ///
    /// The demo shipped every part of a working minimap except the parts that make it show
    /// anything (found 2026-09-16 when the player reported it dead). The HUD has a `Map` panel
    /// with a `MapRenderTexture` RawImage, `MinimapCamera.prefab` is a top-down orthographic
    /// camera carrying `FollowCameraControls` and the kit's `MinimapRenderer`, the controllers
    /// all point at it, and twenty entity prefabs carry wired `MiniMapContainer` anchors. What
    /// was missing:
    ///
    /// - **The camera's `targetTexture` and the RawImage's `texture` both pointed at a deleted
    ///   RenderTexture.** A perfectly good `MinimapRenderTexture.asset` was sitting in
    ///   `Demo/Textures` under a different GUID - the asset had been recreated at some point and
    ///   neither reference followed it. A dead reference reads as null and draws nothing.
    /// - **`BaseMap.asset` had no minimap sprite and zeroed bounds**, so even with the texture
    ///   restored the camera would have filmed an empty layer.
    ///
    /// How the kit's minimap actually works, because it is not obvious: `MinimapRenderer` (on the
    /// camera) spawns a **SpriteRenderer on the MiniMap layer** holding the map image, parks it
    /// at `MinimapPosition + up * spriteOffsets3D` (100m *below* the world, out of everyone's
    /// way) and scales it to `MinimapBoundsWidth/Length`. The minimap camera culls to that one
    /// layer, so it films the image and the markers and nothing else, into the RenderTexture the
    /// RawImage displays. Markers are what `MiniMapContainer` anchors are for.
    ///
    /// The sprite is **rendered from the island itself** rather than drawn, so it cannot drift
    /// from the terrain: any rebuild of the island can re-run this and the map matches again.
    /// </summary>
    public static class DemoMinimapBuilder
    {
        private const string ScenePath = "Assets/OpenMMORPG/Demo/Scenes/DemoMap.unity";
        private const string TextureDir = "Assets/OpenMMORPG/Demo/Textures";
        private const string SpritePath = TextureDir + "/MinimapIsland.png";
        private const string RenderTexturePath = TextureDir + "/MinimapRenderTexture.asset";
        private const string MapInfoPath = "Assets/OpenMMORPG/Demo/GameData/Resources/MapInfos/BaseMap.asset";
        private const string CameraPath = "Assets/OpenMMORPG/Demo/Prefabs/GamePlay/MinimapCamera.prefab";
        private const string CanvasPath = "Assets/OpenMMORPG/Demo/Prefabs/UI/CanvasGameplay.prefab";

        /// <summary>Pixels along each edge of the generated map image.</summary>
        private const int Resolution = 1024;

        /// <summary>
        /// What the map image is allowed to contain. Scenery only: the terrain, the sea, the
        /// buildings and the harvestables. Characters, monsters, NPCs, drops and damage volumes
        /// are all excluded, because the image is baked once and they move - a bandit painted
        /// into the map would stand there for ever.
        /// </summary>
        private static int SceneryMask()
        {
            return (1 << 0)                        // Default: terrain, props, the sea
                 | (1 << 4)                        // Water
                 | (1 << 6)                        // MapTile
                 | (1 << 13)                       // Building
                 | (1 << 14);                      // Harvestable
        }

        [MenuItem("Open MMORPG/Demo/Build Minimap")]
        public static void Build()
        {
            Sprite sprite = RenderIsland();
            if (sprite == null)
                return;
            WriteMapInfo(sprite);
            WireRenderTexture();
            AssetDatabase.SaveAssets();
            Debug.Log($"[{nameof(DemoMinimapBuilder)}] Minimap built: {SpritePath} at {Resolution}px, " +
                      "map bounds written, and the camera and HUD pointed at " +
                      Path.GetFileNameWithoutExtension(RenderTexturePath) + ".");
        }

        /// <summary>
        /// Photographs the island from straight above, at exactly the terrain's extent.
        ///
        /// Orthographic and square to the terrain, so a pixel of the image is a fixed number of
        /// metres and the markers line up with the world without a fudge factor.
        /// </summary>
        private static Sprite RenderIsland()
        {
            bool opened = false;
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            if (!scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
                opened = true;
            }

            Terrain terrain = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                terrain = root.GetComponentInChildren<Terrain>(true);
                if (terrain != null)
                    break;
            }
            if (terrain == null)
            {
                Debug.LogError($"[{nameof(DemoMinimapBuilder)}] No terrain in \"{ScenePath}\" to photograph.");
                if (opened)
                    EditorSceneManager.CloseScene(scene, true);
                return null;
            }

            Vector3 size = terrain.terrainData.size;
            Vector3 origin = terrain.transform.position;
            Vector3 centre = new Vector3(origin.x + size.x * 0.5f, origin.y, origin.z + size.z * 0.5f);
            float extent = Mathf.Max(size.x, size.z);

            var cameraObject = new GameObject("__MinimapCapture");
            EditorSceneManager.MoveGameObjectToScene(cameraObject, scene);
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = extent * 0.5f;
            camera.transform.position = centre + Vector3.up * (size.y + 200f);
            camera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            camera.nearClipPlane = 1f;
            camera.farClipPlane = size.y + 400f;
            camera.cullingMask = SceneryMask();
            camera.clearFlags = CameraClearFlags.SolidColor;
            // The sea reaches past the terrain, so anything outside it should read as open water
            // rather than as a hole.
            camera.backgroundColor = new Color(0.09f, 0.20f, 0.30f, 1f);

            var target = new RenderTexture(Resolution, Resolution, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 4,
            };
            camera.targetTexture = target;
            camera.Render();

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            var texture = new Texture2D(Resolution, Resolution, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, Resolution, Resolution), 0, 0);
            texture.Apply();
            RenderTexture.active = previous;
            camera.targetTexture = null;

            TintTheSea(texture, terrain, origin, size);

            DemoItemBuilder.EnsureFolder(TextureDir);
            File.WriteAllBytes(SpritePath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(cameraObject);
            if (opened)
                EditorSceneManager.CloseScene(scene, true);

            AssetDatabase.ImportAsset(SpritePath, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(SpritePath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            // 100 is the default and the scaling in `MinimapRenderer` divides it straight back
            // out (`bounds * pixelsPerUnit / textureSize`), so it only has to agree with itself.
            importer.spritePixelsPerUnit = 100f;
            importer.maxTextureSize = Resolution;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);
        }

        /// <summary>
        /// Paints the water, because photographing it does not.
        ///
        /// The terrain is a single 260m square and the island only occupies the middle of it -
        /// everything beyond the shore is **seabed**, textured as sand, and the sea surface above
        /// it is transparent. Straight out of the camera the island therefore sits in a wide tan
        /// field that reads as more beach, which is worse than useless on a map: the one thing a
        /// minimap has to show is where the land stops.
        ///
        /// Rather than fight the water shader from directly overhead, the sea is tinted
        /// afterwards from the terrain itself: each pixel maps back to a world position, the
        /// terrain's height there decides whether it is under water, and how far under sets how
        /// strongly it goes to blue. Shallows keep a hint of the sand so the beach still reads as
        /// a beach, and deep water goes solid. It is sampled from the same heightmap the island
        /// is built from, so the coastline in the image is the coastline in the world by
        /// construction.
        ///
        /// Image orientation: the capture camera is rotated (90,0,0), which puts world +X to the
        /// right and world +Z up, and `ReadPixels` starts at the bottom left - so pixel (x,y) maps
        /// to world (originX + x/res * sizeX, originZ + y/res * sizeZ) with no flip.
        /// </summary>
        private static void TintTheSea(Texture2D texture, Terrain terrain, Vector3 origin, Vector3 size)
        {
            var shallow = new Color(0.20f, 0.42f, 0.48f);
            var deep = new Color(0.06f, 0.16f, 0.28f);
            const float FullDepth = 9f;

            Color[] pixels = texture.GetPixels();
            for (int y = 0; y < Resolution; ++y)
            {
                float worldZ = origin.z + (y + 0.5f) / Resolution * size.z;
                for (int x = 0; x < Resolution; ++x)
                {
                    float worldX = origin.x + (x + 0.5f) / Resolution * size.x;
                    float height = terrain.SampleHeight(new Vector3(worldX, 0f, worldZ)) + origin.y;
                    if (height >= DemoIslandBuilder.WaterLevel)
                        continue;
                    float depth = Mathf.Clamp01((DemoIslandBuilder.WaterLevel - height) / FullDepth);
                    Color water = Color.Lerp(shallow, deep, depth);
                    int i = y * Resolution + x;
                    pixels[i] = Color.Lerp(pixels[i], water, Mathf.Lerp(0.55f, 0.97f, depth));
                }
            }
            texture.SetPixels(pixels);
            texture.Apply();
        }

        /// <summary>
        /// The bounds the sprite is stretched to, taken from the terrain rather than typed in.
        /// </summary>
        private static void WriteMapInfo(Sprite sprite)
        {
            var map = AssetDatabase.LoadAssetAtPath<ScriptableObject>(MapInfoPath);
            if (map == null)
            {
                Debug.LogError($"[{nameof(DemoMinimapBuilder)}] Missing \"{MapInfoPath}\".");
                return;
            }
            var serialized = new SerializedObject(map);
            float extent = DemoIslandBuilder.Size;
            Set(serialized, "minimapSprite", sprite);
            SetFloat(serialized, "minimapBoundsWidth", extent);
            SetFloat(serialized, "minimapBoundsLength", extent);
            // The island straddles the origin, so the image belongs there too. The renderer
            // drops it 100m under the world itself; this is only the horizontal placement.
            SerializedProperty position = serialized.FindProperty("minimapPosition");
            if (position != null)
                position.vector3Value = Vector3.zero;
            // Unused by the 3D renderer the demo uses, written so the asset is not half filled in.
            SetFloat(serialized, "minimapOrthographicSize", extent * 0.5f);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(map);
        }

        /// <summary>
        /// Re-points the camera and the HUD image at the RenderTexture that exists, replacing the
        /// dead references to the one that does not.
        /// </summary>
        private static void WireRenderTexture()
        {
            var target = AssetDatabase.LoadAssetAtPath<RenderTexture>(RenderTexturePath);
            if (target == null)
            {
                Debug.LogError($"[{nameof(DemoMinimapBuilder)}] Missing \"{RenderTexturePath}\".");
                return;
            }

            GameObject camera = PrefabUtility.LoadPrefabContents(CameraPath);
            try
            {
                foreach (Camera found in camera.GetComponentsInChildren<Camera>(true))
                    found.targetTexture = target;
                PrefabUtility.SaveAsPrefabAsset(camera, CameraPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(camera);
            }

            GameObject canvas = PrefabUtility.LoadPrefabContents(CanvasPath);
            try
            {
                bool wrote = false;
                foreach (RawImage image in canvas.GetComponentsInChildren<RawImage>(true))
                {
                    if (image.name != "MapRenderTexture")
                        continue;
                    image.texture = target;
                    EditorUtility.SetDirty(image);
                    wrote = true;
                }
                if (wrote)
                    PrefabUtility.SaveAsPrefabAsset(canvas, CanvasPath);
                else
                    Debug.LogWarning($"[{nameof(DemoMinimapBuilder)}] No \"MapRenderTexture\" RawImage in the gameplay canvas.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(canvas);
            }
        }

        private static void Set(SerializedObject serialized, string path, Object value)
        {
            SerializedProperty property = serialized.FindProperty(path);
            if (property != null)
                property.objectReferenceValue = value;
        }

        private static void SetFloat(SerializedObject serialized, string path, float value)
        {
            SerializedProperty property = serialized.FindProperty(path);
            if (property != null)
                property.floatValue = value;
        }
    }
}
