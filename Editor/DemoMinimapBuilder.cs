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
            BuildQuestMarkers();
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

        // ---- NPC quest markers ------------------------------------------------

        private const string NpcMarkerPath = "Assets/OpenMMORPG/Demo/Prefabs/GamePlay/RelatesObjects/Npc/NpcMiniMapCanvas.prefab";
        private const string MarkerDir = TextureDir + "/MinimapMarkers";
        private const int MarkerResolution = 128;
        /// <summary>
        /// Across, on the ground. The minimap films 40m at 256px, so this is about 38px of
        /// render texture - big enough that the glyph inside reads, small enough that two
        /// quest givers a few metres apart do not merge.
        /// </summary>
        private const float MarkerMetres = 6f;
        private static readonly Color Gold = new Color(1f, 0.80f, 0.16f);
        private static readonly Color Silver = new Color(0.80f, 0.81f, 0.84f);

        /// <summary>
        /// The NPC markers on the minimap: gold "!" for a quest on offer, silver "?" for one under
        /// way, gold "?" for one ready to hand in - the convention MMO players already read - and
        /// the hand-in pinned to the minimap's edge when the NPC is out of frame
        /// (<see cref="DemoMinimapQuestPin"/>).
        ///
        /// What the template had (found 2026-09-24, when a player could not find where to hand
        /// in the venison): all three states drew as the same plain disc, because
        ///
        /// - **the glyph never rendered.** Each state is a legacy `Text` of font size 10 in a
        ///   20-unit box, on a canvas whose world scale works out at 1.5m per unit - a 15m
        ///   character, rasterised at 200 dynamic pixels per unit. Nothing of it reached the
        ///   screen; only the 3.75m disc behind it did.
        /// - **the colours would not have told them apart anyway.** On offer and ready to hand
        ///   in were the same gold disc with a gold glyph, and under way a white glyph on a
        ///   white disc.
        ///
        /// So the glyph is baked into the marker image instead of set in type, and the kit's
        /// `NpcQuestIndicator` switches between three images rather than three texts. The images
        /// are drawn here from distance fields rather than rendered from a font, so they need no
        /// font asset and stay crisp at any size. The states keep their objects, so the
        /// indicator's references still hold.
        /// </summary>
        [MenuItem("Open MMORPG/Demo/Build Minimap Quest Markers")]
        public static void BuildQuestMarkers()
        {
            DemoItemBuilder.EnsureFolder(MarkerDir);
            Sprite offer = MarkerSprite("QuestOffer", Gold, false);
            Sprite underWay = MarkerSprite("QuestUnderWay", Silver, true);
            Sprite handIn = MarkerSprite("QuestHandIn", Gold, true);

            GameObject root = PrefabUtility.LoadPrefabContents(NpcMarkerPath);
            try
            {
                var indicator = root.GetComponent<NpcQuestIndicator>();
                if (indicator == null)
                {
                    Debug.LogError($"[{nameof(DemoMinimapBuilder)}] No NpcQuestIndicator on \"{NpcMarkerPath}\".");
                    return;
                }
                // FixScale holds the canvas at one world scale whatever the NPC's own scale is -
                // but not the one it names: it divides its `scale` by the canvas's own lossy scale
                // at Awake rather than by its parent's, so the world scale comes out as `scale`
                // over the prefab's local scale (1 / 0.5 = 2, measured in play). A size in metres
                // converts through that.
                var fixScale = root.GetComponent<UtilsComponents.FixScale>();
                float local = Mathf.Max(0.0001f, root.transform.localScale.x);
                float canvasScale = fixScale != null && fixScale.scale.x > 0f ? fixScale.scale.x / local : local;
                float units = MarkerMetres / canvasScale;
                Restyle(indicator.haveNewQuestsIndicator, offer, units);
                Restyle(indicator.haveInProgressQuestsIndicator, underWay, units);
                Restyle(indicator.haveTasksDoneQuestsIndicator, handIn, units);

                var pin = root.GetComponent<DemoMinimapQuestPin>();
                if (pin == null)
                    pin = root.AddComponent<DemoMinimapQuestPin>();
                pin.indicator = indicator;
                pin.edgeInset = MarkerMetres * 0.5f + 0.5f;

                PrefabUtility.SaveAsPrefabAsset(root, NpcMarkerPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            Debug.Log($"[{nameof(DemoMinimapBuilder)}] NPC quest markers drawn into {MarkerDir} and put on the minimap canvas.");
        }

        /// <summary>One state of the marker: its old text removed, its disc given the new image.</summary>
        private static void Restyle(GameObject state, Sprite sprite, float units)
        {
            if (state == null)
                return;
            var text = state.GetComponent<Text>();
            if (text != null)
                Object.DestroyImmediate(text, true);
            // The text's renderer, now drawing nothing. The disc child has its own.
            var renderer = state.GetComponent<CanvasRenderer>();
            if (renderer != null && state.GetComponent<Graphic>() == null)
                Object.DestroyImmediate(renderer, true);
            state.transform.localScale = Vector3.one;

            Image disc = null;
            foreach (Image image in state.GetComponentsInChildren<Image>(true))
            {
                disc = image;
                break;
            }
            if (disc == null)
            {
                var child = new GameObject("BG", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                child.layer = state.layer;
                child.transform.SetParent(state.transform, false);
                disc = child.GetComponent<Image>();
            }
            disc.sprite = sprite;
            disc.color = Color.white;
            disc.preserveAspect = true;
            disc.raycastTarget = false;
            disc.transform.localScale = Vector3.one;
            disc.rectTransform.anchorMin = disc.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            disc.rectTransform.anchoredPosition = Vector2.zero;
            disc.rectTransform.sizeDelta = new Vector2(units, units);
        }

        /// <summary>
        /// Draws one marker: a dark face with a coloured rim, and a "!" or "?" in the same colour.
        /// Shapes are distance fields in a 128-unit frame, so each edge gets one pixel of
        /// antialiasing whatever the resolution.
        /// </summary>
        private static Sprite MarkerSprite(string name, Color glyph, bool question)
        {
            string path = $"{MarkerDir}/{name}.png";
            int n = MarkerResolution;
            float k = n / 128f;
            var face = new Color(0.09f, 0.07f, 0.05f, 0.92f);
            var centre = new Vector2(n * 0.5f, n * 0.5f);
            float outer = n * 0.5f - 1.5f * k;
            float rim = 7f * k;

            var pixels = new Color[n * n];
            for (int y = 0; y < n; ++y)
            {
                for (int x = 0; x < n; ++x)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float d = Vector2.Distance(p, centre);
                    float cover = Mathf.Clamp01(outer - d + 0.5f);
                    if (cover <= 0f)
                    {
                        pixels[y * n + x] = Color.clear;
                        continue;
                    }
                    float onRim = Mathf.Clamp01(d - (outer - rim) + 0.5f);
                    Vector2 q = p / k;
                    float onGlyph = Mathf.Clamp01(0.5f - (question ? QuestionMark(q) : ExclamationMark(q)) * k);
                    Color colour = Color.Lerp(face, glyph, Mathf.Max(onRim, onGlyph));
                    colour.a *= cover;
                    pixels[y * n + x] = colour;
                }
            }

            var texture = new Texture2D(n, n, TextureFormat.RGBA32, false);
            texture.SetPixels(pixels);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            // Shown at a quarter of its size or less, so it wants mips or its edges crawl as the
            // player moves.
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = n;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        /// <summary>Signed distance to a "!", in the 128-unit frame, y up.</summary>
        private static float ExclamationMark(Vector2 p)
        {
            float stem = Segment(p, new Vector2(64f, 100f), new Vector2(64f, 58f)) - 9.5f;
            float dot = Vector2.Distance(p, new Vector2(64f, 33f)) - 10f;
            return Mathf.Min(stem, dot);
        }

        /// <summary>
        /// Signed distance to a "?", in the 128-unit frame, y up: a hook (an arc from the bottom,
        /// round the right, over the top and a little way down the left), a short stem under it,
        /// and the dot.
        /// </summary>
        private static float QuestionMark(Vector2 p)
        {
            var hookCentre = new Vector2(64f, 82f);
            const float HookRadius = 17f;
            const float Stroke = 8.5f;
            float hook = Arc(p, hookCentre, HookRadius, -90f, 160f) - Stroke;
            float stem = Segment(p, new Vector2(64f, 65f), new Vector2(64f, 56f)) - Stroke;
            float dot = Vector2.Distance(p, new Vector2(64f, 33f)) - 10f;
            return Mathf.Min(hook, Mathf.Min(stem, dot));
        }

        private static float Segment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(p, a + ab * t);
        }

        /// <summary>Distance to a circular arc running counter-clockwise from one angle to the other.</summary>
        private static float Arc(Vector2 p, Vector2 centre, float radius, float fromDegrees, float toDegrees)
        {
            Vector2 v = p - centre;
            float angle = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;
            if (angle >= fromDegrees && angle <= toDegrees)
                return Mathf.Abs(v.magnitude - radius);
            float from = fromDegrees * Mathf.Deg2Rad;
            float to = toDegrees * Mathf.Deg2Rad;
            Vector2 start = centre + radius * new Vector2(Mathf.Cos(from), Mathf.Sin(from));
            Vector2 end = centre + radius * new Vector2(Mathf.Cos(to), Mathf.Sin(to));
            return Mathf.Min(Vector2.Distance(p, start), Vector2.Distance(p, end));
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
