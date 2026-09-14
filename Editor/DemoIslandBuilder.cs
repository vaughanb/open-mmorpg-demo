using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// Generates the demo island as a Unity Terrain.
    ///
    /// The shape comes from <see cref="HeightAt"/>: a radial falloff drops the ground
    /// under sea level at the edges so the coast is a real boundary rather than an
    /// invisible wall, and layered noise raises hills inland so the village, the shore
    /// and the bandit camp cannot all be seen from one spot.
    ///
    /// Terrain earns its place here over a generated mesh for three reasons. Ground
    /// types blend into one another through the splatmap instead of meeting at a hard
    /// triangle edge, which is what made rock read as grey patches pasted onto the
    /// grass. Grass and undergrowth become detail instances with their own density map,
    /// so the ground can be far thicker than several thousand scattered GameObjects
    /// could afford. And trees become terrain instances rather than transforms, which
    /// keeps them out of the scene file entirely.
    /// </summary>
    public static class DemoIslandBuilder
    {
        public const float Size = 260f;
        public const float ShoreRadius = 110f;

        /// <summary>Height of the low ground the island mostly consists of.</summary>
        public const float BaseLandHeight = 3.5f;
        public const float PeakHeight = 26f;
        public const float SeabedDepth = -12f;

        /// <summary>Sea level. The island is built around this, and the water plane sits on it.</summary>
        public const float WaterLevel = 0f;

        /// <summary>
        /// Heightmap detail. 513 across 260m is roughly half a metre per sample, finer
        /// than the metre-and-a-third quads the previous generated mesh used.
        /// </summary>
        private const int HeightmapResolution = 513;

        private const int AlphamapResolution = 512;
        private const int DetailResolution = 512;
        /// <summary>
        /// How many detail cells share one patch, and so one combined mesh.
        ///
        /// The terrain welds every plant in a patch into a single mesh, and that mesh is
        /// what the detail system allocates. Sixteen puts too much in one of them once
        /// the ground is thickly covered; eight quarters the size of each at the cost of
        /// four times as many.
        /// </summary>
        private const int DetailPerPatch = 8;

        private const float SandMaxHeight = 1.5f;
        private const float RockMinSlope = 46f;

        private const string TerrainDataPath = "Assets/OpenMMORPG/Demo/Terrain/IslandTerrain.asset";
        private const string LayerDir = "Assets/OpenMMORPG/Demo/Terrain";
        private const string TextureDir = "Assets/OpenMMORPG/Demo/Textures";
        private const string NoiseSourcePath = "Assets/Plugins/Quaternius/Village/Textures/T_Noise_Terrain.png";
        private const string NaturePrefabDir = "Assets/Plugins/Quaternius/Nature/Prefabs";

        /// <summary>
        /// Flat ground the layout depends on: the village needs somewhere buildings can
        /// sit level, and the bandit camp needs a clearing. Carving these into the
        /// height function keeps them flat no matter how the noise is retuned.
        /// </summary>
        public static readonly Vector2 VillageCentre = new Vector2(-34f, 22f);
        public const float VillageRadius = 30f;

        /// <summary>
        /// Radius of the level pad under each house, in metres. Flat within 55% of it, as
        /// <see cref="Flatten"/> works, so 9m gives just under 5m of true flat — enough for
        /// the 4.24m half-diagonal of the largest house, which is three 2m cells square.
        /// </summary>
        public const float HousePadRadius = 9f;
        public const float VillageHeight = 5.5f;

        public static readonly Vector2 CampCentre = new Vector2(46f, -40f);
        public const float CampRadius = 20f;
        public const float CampHeight = 7.5f;

        /// <summary>
        /// How far the bare, trodden ground reaches around each settlement. Ground people
        /// walk over daily is not pasture, so this area is earth rather than grass and
        /// nothing is scattered on it.
        /// </summary>
        public const float VillageGroundRadius = 21f;
        public const float CampGroundRadius = 13f;

        private const int NoiseSeed = 20260910;

        /// <summary>Where the terrain's corner sits, since a Terrain is anchored at its corner.</summary>
        public static Vector3 TerrainOrigin
        {
            get { return new Vector3(-Size * 0.5f, SeabedDepth, -Size * 0.5f); }
        }

        public static float TerrainHeight
        {
            get { return PeakHeight - SeabedDepth; }
        }

        // ---- shape -----------------------------------------------------------

        public static float HeightAt(float x, float z)
        {
            Vector2 point = new Vector2(x, z);
            float distance = point.magnitude;
            float shore = ShoreAt(Mathf.Atan2(z, x));

            // The coast mask only cuts the island off at its edge. Scaling the whole
            // height by distance instead would make the island one big cone with
            // almost no flat ground on it.
            float coast = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(shore * 0.80f, shore, distance));
            float offshore = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(shore, shore * 1.30f, distance));
            // Hills are held inland so the land reaches the coast already low. Without
            // this a hill that runs to the shore is cut off as a sheer cliff.
            float inland = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(shore * 0.50f, shore * 0.84f, distance));

            float offset = NoiseSeed % 1000;
            float hills =
                Perlin(x * 0.0075f + offset, z * 0.0075f + offset) * 1.0f +
                Perlin(x * 0.019f + offset, z * 0.019f + offset) * 0.5f +
                Perlin(x * 0.052f + offset, z * 0.052f + offset) * 0.22f;
            hills /= 1.72f;
            // Summed Perlin bunches around the middle of its range, so the raw value
            // would only ever reach a third of PeakHeight. Stretch it back out to 0-1
            // first, then bias low so the island is mostly walkable ground with a few
            // real hills rather than uniformly lumpy everywhere.
            hills = Mathf.Clamp01(Mathf.InverseLerp(0.33f, 0.67f, hills));
            hills = Mathf.Pow(hills, 1.5f);

            // Most of the island is a low plateau the player can move around freely on,
            // with hills rising out of it where the noise peaks.
            float height = coast * (BaseLandHeight + hills * inland * (PeakHeight - BaseLandHeight));
            height = Mathf.Lerp(height, SeabedDepth, offshore);

            height = Flatten(height, point, VillageCentre, VillageRadius, VillageHeight);
            // And a level pad under each house. The village disc alone does not do it: only
            // its inner 55% is truly flat, and the ring of houses stands at 14m of a 30m
            // disc with their far corners past 19m — out in the blend, where the ground is
            // already climbing. Measured under the bank, it climbed 0.89m above the floor
            // and came up through the floorboards.
            foreach (Vector3 house in DemoSceneBuilder.HouseLayout)
            {
                height = Flatten(height, point,
                    VillageCentre + new Vector2(house.x, house.z), HousePadRadius, VillageHeight);
            }
            height = Flatten(height, point, CampCentre, CampRadius, CampHeight);
            return height;
        }

        /// <summary>
        /// Distance from the centre to the coast at a given bearing. Sampling the noise
        /// on a circle rather than by angle keeps it continuous where the bearing wraps,
        /// which a plain angle lookup would seam.
        /// </summary>
        public static float ShoreAt(float angle)
        {
            float warp =
                Perlin(Mathf.Cos(angle) * 1.7f, Mathf.Sin(angle) * 1.7f) * 1.0f +
                Perlin(Mathf.Cos(angle) * 4.3f + 40f, Mathf.Sin(angle) * 4.3f + 40f) * 0.4f;
            warp /= 1.4f;
            return ShoreRadius * Mathf.Lerp(0.62f, 1.05f, warp);
        }

        private static float Perlin(float x, float z)
        {
            return Mathf.PerlinNoise(x + 1000f, z + 1000f);
        }

        /// <summary>Eases the ground towards a level plateau, so the edges are walkable ramps.</summary>
        private static float Flatten(float height, Vector2 point, Vector2 centre, float radius, float level)
        {
            float blend = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(radius * 0.55f, radius, Vector2.Distance(point, centre)));
            return Mathf.Lerp(height, level, blend);
        }

        /// <summary>
        /// Slope of the ground around a point, measured across several metres rather than
        /// from one heightmap sample to the next, so the rock layer lands in one piece on
        /// what is genuinely steep instead of speckling a hillside.
        /// </summary>
        /// <summary>
        /// Which way is uphill, flattened onto the ground and normalised.
        ///
        /// Measured over the same baseline as the slope, so the two agree: read off a
        /// tighter one, the per-triangle noise in the heightfield swings the direction
        /// about and a row of rocks meant to follow a contour wanders across it.
        /// </summary>
        public static Vector2 SlopeDirection(float x, float z)
        {
            const float step = 2.5f;
            float dx = HeightAt(x + step, z) - HeightAt(x - step, z);
            float dz = HeightAt(x, z + step) - HeightAt(x, z - step);
            var uphill = new Vector2(dx, dz);
            return uphill.sqrMagnitude < 1e-6f ? Vector2.right : uphill.normalized;
        }

        /// <summary>
        /// Which way the ground faces, over the same baseline as the slope.
        /// </summary>
        public static Vector3 SurfaceNormal(float x, float z)
        {
            const float step = 2.5f;
            float dx = HeightAt(x + step, z) - HeightAt(x - step, z);
            float dz = HeightAt(x, z + step) - HeightAt(x, z - step);
            return new Vector3(-dx, 2f * step, -dz).normalized;
        }

        public static float SmoothSlopeAt(float x, float z)
        {
            const float step = 2.5f;
            float dx = HeightAt(x + step, z) - HeightAt(x - step, z);
            float dz = HeightAt(x, z + step) - HeightAt(x, z - step);
            var normal = new Vector3(-dx, 2f * step, -dz).normalized;
            return Vector3.Angle(normal, Vector3.up);
        }

        /// <summary>
        /// How settled a point is, from nothing out in the fields to fully trodden in the
        /// middle of a village. The edge is pushed in and out by noise, because ground
        /// worn bare by use does not have a boundary that is a perfect circle.
        /// </summary>
        public static float SettlementWeight(float x, float z)
        {
            return Mathf.Max(
                SettlementWeightOne(x, z, VillageCentre, VillageGroundRadius),
                SettlementWeightOne(x, z, CampCentre, CampGroundRadius));
        }

        private static float SettlementWeightOne(float x, float z, Vector2 centre, float radius)
        {
            float wobble = Mathf.Lerp(0.82f, 1.16f, Perlin(x * 0.045f, z * 0.045f));
            float edge = radius * wobble;
            float distance = Vector2.Distance(new Vector2(x, z), centre);
            return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(edge * 0.75f, edge, distance));
        }

        /// <summary>Whether a point is settled enough that nothing should be scattered on it.</summary>
        public static bool InsideSettlement(float x, float z)
        {
            return SettlementWeight(x, z) > 0.5f;
        }

        /// <summary>
        /// How much room the village and the camp are given, measured from their middles.
        ///
        /// The village's own buildings reach about eighteen and a half metres out — the
        /// far corner of the largest house — and its fences ring it at sixteen. The camp
        /// is a fence ring at eleven with its gear inside that. These leave a few paces
        /// beyond each.
        /// </summary>
        public const float VillageClearance = 22f;
        public const float CampClearance = 16f;

        /// <summary>
        /// Whether a point is on ground the settlements occupy.
        ///
        /// A hard circle, unlike <see cref="InsideSettlement"/>. That one is deliberately
        /// soft and wobbly because it decides where the ground is painted as bare earth,
        /// and a settlement whose edge is a drawn circle looks drawn — but the same wobble
        /// means it reports points well inside the camp as being outside it. Its edge for
        /// the camp falls as low as eight metres, which put a boulder inside the bandits'
        /// fence ring. Anything that is placed as an object, rather than painted, belongs
        /// behind this instead.
        /// </summary>
        public static bool OnSettledGround(float x, float z)
        {
            var point = new Vector2(x, z);
            return Vector2.Distance(point, VillageCentre) < VillageClearance ||
                   Vector2.Distance(point, CampCentre) < CampClearance;
        }

        /// <summary>
        /// How much of each ground type covers a point, in layer order: sand, grass,
        /// rock, earth. Unlike a per-triangle choice these overlap, which is the whole
        /// reason for using a terrain — the coast fades into the grass and the crags
        /// fade out of it instead of meeting at a hard edge.
        /// </summary>
        private static void GroundWeights(float x, float z, float[] weights)
        {
            float height = HeightAt(x, z);
            float slope = SmoothSlopeAt(x, z);

            float rock = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(RockMinSlope - 9f, RockMinSlope + 4f, slope));
            float sand = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(SandMaxHeight - 0.6f, SandMaxHeight + 1.6f, height));
            float earth = SettlementWeight(x, z);

            // Precedence, softened: rock wins over everything because nobody treads a
            // crag and nothing grows on one; then the coast; then trodden ground.
            weights[2] = rock;
            weights[0] = sand * (1f - rock);
            weights[3] = earth * (1f - rock) * (1f - sand);
            weights[1] = (1f - rock) * (1f - sand) * (1f - earth);
        }

        // ---- build -----------------------------------------------------------

        [MenuItem("Open MMORPG/Demo/Build Island Terrain")]
        public static void Build()
        {
            EnsureFolder(LayerDir);
            EnsureFolder(TextureDir);

            TerrainData data = LoadOrCreate();
            data.heightmapResolution = HeightmapResolution;
            data.alphamapResolution = AlphamapResolution;
            data.SetDetailResolution(DetailResolution, DetailPerPatch);
            data.size = new Vector3(Size, TerrainHeight, Size);

            WriteHeights(data);
            data.terrainLayers = BuildLayers();
            WriteSplatmap(data);
            // Trees first: the mushrooms are painted around their trunks, so the detail
            // pass needs to know where the woods ended up.
            List<Vector2> trunks = WriteTrees(data);
            WriteDetails(data, trunks);

            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            Debug.Log($"[{nameof(DemoIslandBuilder)}] Built {TerrainDataPath}: {HeightmapResolution}x{HeightmapResolution} heightmap, " +
                      $"{data.terrainLayers.Length} layers, {data.detailPrototypes.Length} detail types, {data.treeInstanceCount} trees.");
        }

        private static TerrainData LoadOrCreate()
        {
            var existing = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainDataPath);
            if (existing != null)
                return existing;
            var created = new TerrainData { name = "IslandTerrain" };
            AssetDatabase.CreateAsset(created, TerrainDataPath);
            return created;
        }

        public static TerrainData LoadTerrainData()
        {
            return AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainDataPath);
        }

        /// <summary>
        /// The material the terrain draws with. Taken from the render pipeline rather
        /// than hardcoded, so the demo keeps working if the project's pipeline changes.
        /// </summary>
        public static Material TerrainMaterial()
        {
            const string path = LayerDir + "/IslandTerrain.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
                return existing;

            Material source = null;
            UnityEngine.Rendering.RenderPipelineAsset pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            if (pipeline != null)
                source = pipeline.defaultTerrainMaterial;
            if (source == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Terrain/Lit");
                if (shader == null)
                {
                    Debug.LogError($"[{nameof(DemoIslandBuilder)}] No terrain shader available for this pipeline.");
                    return null;
                }
                source = new Material(shader);
            }

            EnsureFolder(LayerDir);
            var material = new Material(source);
            material.name = "IslandTerrain";
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void WriteHeights(TerrainData data)
        {
            int side = HeightmapResolution;
            var heights = new float[side, side];
            float step = Size / (side - 1);
            for (int z = 0; z < side; ++z)
            {
                for (int x = 0; x < side; ++x)
                {
                    float wx = TerrainOrigin.x + x * step;
                    float wz = TerrainOrigin.z + z * step;
                    // Terrain stores height as a fraction of its own vertical size, from
                    // its base, so the seabed is 0 and the highest peak is 1.
                    heights[z, x] = Mathf.Clamp01((HeightAt(wx, wz) - SeabedDepth) / TerrainHeight);
                }
            }
            data.SetHeights(0, 0, heights);
        }

        private static void WriteSplatmap(TerrainData data)
        {
            int side = AlphamapResolution;
            var map = new float[side, side, 4];
            var weights = new float[4];
            float step = Size / side;
            for (int z = 0; z < side; ++z)
            {
                for (int x = 0; x < side; ++x)
                {
                    float wx = TerrainOrigin.x + (x + 0.5f) * step;
                    float wz = TerrainOrigin.z + (z + 0.5f) * step;
                    GroundWeights(wx, wz, weights);

                    float total = weights[0] + weights[1] + weights[2] + weights[3];
                    if (total <= 0.0001f)
                    {
                        map[z, x, 1] = 1f;
                        continue;
                    }
                    for (int layer = 0; layer < 4; ++layer)
                        map[z, x, layer] = weights[layer] / total;
                }
            }
            data.SetAlphamaps(0, 0, map);
        }

        // ---- layers ----------------------------------------------------------

        private struct LayerSpec
        {
            public string Name;
            public Color Tint;
            public float TileSize;
        }

        private static readonly LayerSpec[] Layers =
        {
            new LayerSpec { Name = "Island_Sand", Tint = new Color(0.82f, 0.75f, 0.57f), TileSize = 14f },
            new LayerSpec { Name = "Island_Grass", Tint = new Color(0.37f, 0.52f, 0.26f), TileSize = 11f },
            new LayerSpec { Name = "Island_Rock", Tint = new Color(0.45f, 0.44f, 0.41f), TileSize = 9f },
            new LayerSpec { Name = "Island_Earth", Tint = new Color(0.55f, 0.45f, 0.31f), TileSize = 8f },
        };

        private static TerrainLayer[] BuildLayers()
        {
            var built = new TerrainLayer[Layers.Length];
            for (int i = 0; i < Layers.Length; ++i)
            {
                LayerSpec spec = Layers[i];
                string path = $"{LayerDir}/{spec.Name}.terrainlayer";
                var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
                if (layer == null)
                {
                    layer = new TerrainLayer();
                    AssetDatabase.CreateAsset(layer, path);
                }
                layer.diffuseTexture = BuildGroundTexture(spec.Name, spec.Tint);
                layer.tileSize = new Vector2(spec.TileSize, spec.TileSize);
                layer.tileOffset = Vector2.zero;
                layer.specular = Color.black;
                layer.metallic = 0f;
                layer.smoothness = 0f;
                EditorUtility.SetDirty(layer);
                built[i] = layer;
            }
            return built;
        }

        /// <summary>
        /// Bakes a ground texture: the village kit's terrain noise, tinted.
        ///
        /// Terrain layers take a texture rather than a colour, and the raw noise is
        /// mid-grey, so tinting has to happen in the pixels rather than through a
        /// material tint. Its range is lifted towards white first so the result reads as
        /// the intended colour shaded a little, not as a colour halved in brightness.
        /// </summary>
        private static Texture2D BuildGroundTexture(string name, Color tint)
        {
            string path = $"{TextureDir}/{name}.png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null)
                return existing;

            var importer = AssetImporter.GetAtPath(NoiseSourcePath) as TextureImporter;
            if (importer == null)
            {
                Debug.LogError($"[{nameof(DemoIslandBuilder)}] No terrain noise at \"{NoiseSourcePath}\".");
                return null;
            }

            bool wasReadable = importer.isReadable;
            TextureImporterCompression wasCompression = importer.textureCompression;
            importer.isReadable = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();

            try
            {
                var source = AssetDatabase.LoadAssetAtPath<Texture2D>(NoiseSourcePath);
                Color[] pixels = source.GetPixels();
                for (int i = 0; i < pixels.Length; ++i)
                {
                    float shade = Mathf.Lerp(0.74f, 1f, pixels[i].r);
                    pixels[i] = new Color(tint.r * shade, tint.g * shade, tint.b * shade, 1f);
                }
                var output = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
                output.SetPixels(pixels);
                output.Apply();
                System.IO.File.WriteAllBytes(path, output.EncodeToPNG());
                Object.DestroyImmediate(output);
            }
            finally
            {
                importer.isReadable = wasReadable;
                importer.textureCompression = wasCompression;
                importer.SaveAndReimport();
            }

            AssetDatabase.ImportAsset(path);
            var written = AssetImporter.GetAtPath(path) as TextureImporter;
            written.wrapMode = TextureWrapMode.Repeat;
            written.maxTextureSize = 512;
            written.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ---- undergrowth -----------------------------------------------------

        /// <summary>
        /// Ground cover written into the detail map as <em>coverage</em>, the fraction of
        /// each cell the plant should occupy.
        ///
        /// Unity 6 scatters details in CoverageMode, where the map holds 0-255 coverage
        /// rather than a count of instances. Writing counts into it — two or three per
        /// cell — reads as roughly one percent coverage, and the terrain comes up bare
        /// with no warning of any kind.
        /// </summary>
        private struct DetailSpec
        {
            public string Prefab;

            /// <summary>How much of the ground this layer carpets, before patchiness.</summary>
            public float Coverage;

            public float MinHeight;
            public float MaxHeight;
            public float MaxSlope;

            /// <summary>
            /// How tall the plant should stand, in metres, at its smallest and largest.
            ///
            /// Given as a real size rather than a multiple of the model, because the pack
            /// authors these at wildly different scales — a fern is a nine metre cluster
            /// and a clover a metre-tall leaf — so the same multiplier means something
            /// different for every one of them. The builder divides by each model's own
            /// measured height, so what is written here is what shows up beside a person.
            /// </summary>
            public float MinTall;
            public float MaxTall;

            /// <summary>Only grows within this of a tree trunk; zero grows anywhere.</summary>
            public float NearTrees;
        }

        /// <summary>
        /// The most vertices the undergrowth may put into any one patch's combined mesh.
        ///
        /// This, not the number of plants, is what decides whether the detail system
        /// survives: for every patch the terrain welds its plants into a single mesh, and
        /// it is that allocation which falls over. The pack's plants are not the handful
        /// of triangles a grass billboard usually is — they run from 153 vertices for a
        /// tuft of grass to 1344 for a clump of flowers — so ground cover that would be
        /// nothing at all with billboards runs into hundreds of thousands of vertices per
        /// patch here. Counting plants misses this entirely: the same number of them
        /// costs nine times as much if they happen to be flowers.
        /// </summary>
        private const int PatchVertexBudget = 90000;

        // Coverage is what fraction of the ground a layer carpets, before the patchiness
        // noise thins it. The grasses overlap deliberately: no single one of them covers
        // ground on its own, and a field is short grass with taller grass standing
        // through it, not one tuft repeated.
        //
        // The slope limits are what decides whether a hillside is a field or a bare
        // green texture. Grass that gives up at thirty degrees leaves every slope on the
        // island shaved, which is the single thing that most gives away a terrain as
        // painted rather than grown; the short grasses now carry on to forty, where the
        // rock layer is taking over anyway.
        private static readonly DetailSpec[] Details =
        {
            new DetailSpec { Prefab = "Grass_Common_Short", Coverage = 0.80f, MinHeight = 1.6f, MaxHeight = 22f, MaxSlope = 40f, MinTall = 0.35f, MaxTall = 0.60f },
            new DetailSpec { Prefab = "Grass_Common_Tall", Coverage = 0.52f, MinHeight = 2f, MaxHeight = 20f, MaxSlope = 35f, MinTall = 0.60f, MaxTall = 1.00f },
            new DetailSpec { Prefab = "Grass_Wispy_Short", Coverage = 0.52f, MinHeight = 1.6f, MaxHeight = 22f, MaxSlope = 40f, MinTall = 0.35f, MaxTall = 0.60f },
            new DetailSpec { Prefab = "Grass_Wispy_Tall", Coverage = 0.40f, MinHeight = 2f, MaxHeight = 20f, MaxSlope = 35f, MinTall = 0.60f, MaxTall = 1.00f },
            new DetailSpec { Prefab = "Clover_1", Coverage = 0.34f, MinHeight = 1.6f, MaxHeight = 18f, MaxSlope = 33f, MinTall = 0.20f, MaxTall = 0.35f },
            new DetailSpec { Prefab = "Clover_2", Coverage = 0.34f, MinHeight = 1.6f, MaxHeight = 18f, MaxSlope = 33f, MinTall = 0.20f, MaxTall = 0.35f },
            new DetailSpec { Prefab = "Fern_1", Coverage = 0.13f, MinHeight = 2f, MaxHeight = 18f, MaxSlope = 26f, MinTall = 0.40f, MaxTall = 0.65f },
            new DetailSpec { Prefab = "Flower_3_Group", Coverage = 0.09f, MinHeight = 2f, MaxHeight = 16f, MaxSlope = 22f, MinTall = 0.40f, MaxTall = 0.65f },
            new DetailSpec { Prefab = "Flower_4_Group", Coverage = 0.09f, MinHeight = 2f, MaxHeight = 16f, MaxSlope = 22f, MinTall = 0.40f, MaxTall = 0.65f },
            // Mushrooms grow in the leaf litter under the trees, not out in the open
            // field, which is where they read as woodland rather than as scatter.
            new DetailSpec { Prefab = "Mushroom_Common", Coverage = 0.05f, MinHeight = 2f, MaxHeight = 20f, MaxSlope = 26f, MinTall = 0.15f, MaxTall = 0.25f, NearTrees = 3.2f },
        };

        private static void WriteDetails(TerrainData data, List<Vector2> trunks)
        {
            var prototypes = new List<DetailPrototype>();
            var used = new List<DetailSpec>();
            var shares = new List<float>();
            foreach (DetailSpec spec in Details)
            {
                // Flattened copies again: an instanced detail is dropped just like an
                // instanced tree when its renderer is not on the prefab root, and it
                // fails silently — a full density map draws no grass at all.
                GameObject prefab = DemoTreePrefabBuilder.Load(spec.Prefab);
                if (prefab == null)
                {
                    Debug.LogWarning($"[{nameof(DemoIslandBuilder)}] No terrain detail prefab \"{spec.Prefab}\". Run Build Terrain Tree Prefabs first.");
                    continue;
                }
                // The size the plant is asked to be, as a multiple of the model it is.
                Bounds model = prefab.GetComponent<MeshFilter>().sharedMesh.bounds;
                float small = spec.MinTall / Mathf.Max(model.size.y, 0.01f);
                float large = spec.MaxTall / Mathf.Max(model.size.y, 0.01f);
                // And how much ground one of them then covers, as a share of a detail
                // cell. This is the number the terrain divides coverage by to decide how
                // many to plant, so it has to describe the plant that is actually drawn:
                // left at a guess while the plants are shrunk, the terrain compensates by
                // planting orders of magnitude more of them.
                float cell = Size / DetailResolution;
                float middle = (small + large) * 0.5f;
                float footprint = model.size.x * middle * model.size.z * middle;
                float share = Mathf.Clamp(footprint / (cell * cell), 0.05f, 2f);
                shares.Add(share);

                prototypes.Add(new DetailPrototype
                {
                    prototype = prefab,
                    usePrototypeMesh = true,
                    renderMode = DetailRenderMode.VertexLit,
                    // Not instanced. URP draws nothing at all for instanced mesh details
                    // here — no error, just bare ground under a full density map. The
                    // batched path renders them correctly.
                    useInstancing = false,
                    minWidth = small,
                    maxWidth = large,
                    minHeight = small,
                    maxHeight = large,
                    noiseSpread = 12f,
                    healthyColor = Color.white,
                    dryColor = Color.white,
                    // How much of a cell one plant is taken to cover. The terrain divides
                    // the coverage in the map by this to decide how many to place, so
                    // leaving it at 1 puts down roughly one tuft per cell however thick
                    // the map says the grass is.
                    targetCoverage = share,
                });
                used.Add(spec);
            }
            data.detailPrototypes = prototypes.ToArray();
            // Set explicitly rather than relying on the project default, so the numbers
            // written below always mean the same thing.
            data.SetDetailScatterMode(DetailScatterMode.CoverageMode);
            // Coverage and the legacy distribution are two different readings of the same
            // map, and the project ships with the legacy one switched on.
            if (QualitySettings.useLegacyDetailDistribution)
            {
                QualitySettings.useLegacyDetailDistribution = false;
                Debug.Log($"[{nameof(DemoIslandBuilder)}] Turned off legacy detail distribution so coverage is read as coverage.");
            }

            int side = DetailResolution;
            float step = Size / side;
            // Where the woods are, as a coarse grid of cells that hold a trunk. Testing
            // every cell against every tree is a quarter of a million cells times seven
            // hundred trees; bucketing the trunks first turns that into a handful of
            // lookups per cell.
            float bucketSize = 8f;
            var woods = new Dictionary<long, List<Vector2>>();
            foreach (Vector2 trunk in trunks)
            {
                long key = Bucket(trunk.x, trunk.y, bucketSize);
                if (!woods.TryGetValue(key, out List<Vector2> bucket))
                    woods[key] = bucket = new List<Vector2>();
                bucket.Add(trunk);
            }

            // Worked out in full before a byte of it is written, because the way to find
            // out that this is too much for the detail system is otherwise to build it,
            // open the scene and watch the editor die inside the combined-mesh job.
            var wanted = new float[used.Count][];
            int patches = side / DetailPerPatch;
            var load = new double[patches * patches];
            double plants = 0d;

            for (int layer = 0; layer < used.Count; ++layer)
            {
                DetailSpec spec = used[layer];
                var map = new float[side * side];
                wanted[layer] = map;
                int vertices = prototypes[layer].prototype.GetComponent<MeshFilter>().sharedMesh.vertexCount;
                for (int z = 0; z < side; ++z)
                {
                    for (int x = 0; x < side; ++x)
                    {
                        float coverage = Coverage(spec, layer, step, woods, bucketSize, x, z);
                        if (coverage <= 0f)
                            continue;
                        map[z * side + x] = coverage;
                        double count = coverage / shares[layer];
                        plants += count;
                        load[(z / DetailPerPatch) * patches + x / DetailPerPatch] += count * vertices;
                    }
                }
            }

            double heaviest = 0d;
            foreach (double patch in load)
                heaviest = System.Math.Max(heaviest, patch);
            float fit = heaviest > PatchVertexBudget ? (float)(PatchVertexBudget / heaviest) : 1f;
            Debug.Log($"[{nameof(DemoIslandBuilder)}] Undergrowth: {plants:F0} plants, heaviest patch " +
                      $"{heaviest:F0} vertices against a budget of {PatchVertexBudget}" +
                      (fit < 1f ? $" — thinned to {fit:P0}, {plants * fit:F0} plants." : " — within budget."));

            for (int layer = 0; layer < used.Count; ++layer)
            {
                float[] map = wanted[layer];
                var density = new int[side, side];
                for (int z = 0; z < side; ++z)
                    for (int x = 0; x < side; ++x)
                        density[z, x] = Mathf.Clamp(Mathf.RoundToInt(map[z * side + x] * fit * 255f), 0, 255);
                data.SetDetailLayer(0, 0, layer, density);
            }
        }

        /// <summary>
        /// How thickly one layer covers one detail cell, before the budget is applied.
        /// </summary>
        private static float Coverage(DetailSpec spec, int layer, float step,
            Dictionary<long, List<Vector2>> woods, float bucketSize, int x, int z)
        {
            float wx = TerrainOrigin.x + (x + 0.5f) * step;
            float wz = TerrainOrigin.z + (z + 0.5f) * step;
            float height = HeightAt(wx, wz);
            if (height < spec.MinHeight || height > spec.MaxHeight)
                return 0f;
            if (SmoothSlopeAt(wx, wz) > spec.MaxSlope)
                return 0f;
            // Nothing grows where the village walks.
            float open = 1f - SettlementWeight(wx, wz);
            if (open < 0.35f)
                return 0f;
            if (spec.NearTrees > 0f && !UnderTrees(woods, bucketSize, wx, wz, spec.NearTrees))
                return 0f;
            // Patchiness, so the field has thick and thin parts rather than one even
            // carpet. The thin parts still keep most of their cover: taking them most of
            // the way to nothing is what leaves bald ground showing between the tufts.
            float patch = Perlin(wx * 0.035f + layer * 37f, wz * 0.035f + layer * 37f);
            return spec.Coverage * open * Mathf.SmoothStep(0.55f, 1f, patch);
        }

        private static long Bucket(float x, float z, float size)
        {
            return (long)Mathf.FloorToInt(x / size) * 100000L + Mathf.FloorToInt(z / size);
        }

        /// <summary>Whether a point is close enough to a trunk to be under a canopy.</summary>
        private static bool UnderTrees(Dictionary<long, List<Vector2>> woods, float bucketSize, float x, float z, float reach)
        {
            float squared = reach * reach;
            int cx = Mathf.FloorToInt(x / bucketSize);
            int cz = Mathf.FloorToInt(z / bucketSize);
            for (int ox = -1; ox <= 1; ++ox)
            {
                for (int oz = -1; oz <= 1; ++oz)
                {
                    if (!woods.TryGetValue((long)(cx + ox) * 100000L + (cz + oz), out List<Vector2> bucket))
                        continue;
                    foreach (Vector2 trunk in bucket)
                    {
                        if ((trunk.x - x) * (trunk.x - x) + (trunk.y - z) * (trunk.y - z) < squared)
                            return true;
                    }
                }
            }
            return false;
        }

        // ---- trees -----------------------------------------------------------

        private struct TreeSpec
        {
            public string[] Prefabs;
            public int Count;
            public float MinHeight;
            public float MaxHeight;
            public float MaxSlope;
            public float MinScale;
            public float MaxScale;
            public int Clusters;
            public float ClusterRadius;
            /// <summary>A real tree rather than undergrowth, so things grow under it.</summary>
            public bool Canopy;
            /// <summary>Keep this far from the village; zero grows right up to it.</summary>
            public float FromVillage;
            /// <summary>Keep this far from others of the same band; zero lets them touch.</summary>
            public float Spacing;
        }

        private static readonly TreeSpec[] Trees =
        {
            new TreeSpec { Count = 275, MinHeight = 2.5f, MaxHeight = 20f, MaxSlope = 22f, MinScale = 0.85f, MaxScale = 1.35f, Canopy = true,
                Clusters = 14, ClusterRadius = 30f,
                Prefabs = new[] { "CommonTree_1", "CommonTree_2", "CommonTree_3", "CommonTree_4", "CommonTree_5", "Pine_1", "Pine_2", "Pine_3", "Pine_4", "Pine_5" } },
            // Quaternius' twisted tree carries a red autumn leaf texture. A whole forest of
            // it reads as one solid red mass — and so, it turns out, does a cluster of
            // four: keeping the count down is not enough on its own if the few that are
            // placed are placed on top of each other. Scattered singly and held apart,
            // the same eight trees read as accents among the green instead of as two red
            // blobs. Their habitat holds this many at this spacing with room to spare.
            new TreeSpec { Count = 8, MinHeight = 4f, MaxHeight = 20f, MaxSlope = 26f, MinScale = 0.7f, MaxScale = 0.95f, Canopy = true,
                Clusters = 0, Spacing = 26f,
                Prefabs = new[] { "TwistedTree_1", "TwistedTree_2", "TwistedTree_3", "TwistedTree_4", "TwistedTree_5" } },
            // Dead trees are not a wood. They are single snags left standing where the
            // rest of the stand has gone, so this band is scattered rather than clustered
            // — clustering is what put a blighted grove on the skyline above the village
            // — and each is kept clear of the others, which random placement alone does
            // not do: uniform scatter still deals the occasional touching pair, and a
            // pair of dead trees reads as the start of a copse.
            //
            // Its height range is wide on purpose. Restricted to the tops it had barely a
            // thousand square metres of the island to stand on, which physically cannot
            // hold twenty trees a spacing apart — so they piled into the little ground
            // that qualified, and no amount of randomness would have separated them. A
            // narrow habitat concentrates a band however it is scattered. They are still
            // held well back from the village, which is what keeps them off its skyline.
            new TreeSpec { Count = 16, MinHeight = 5f, MaxHeight = 24f, MaxSlope = 30f, MinScale = 0.8f, MaxScale = 1.2f, Canopy = true,
                Clusters = 0, FromVillage = 55f, Spacing = 14f,
                Prefabs = new[] { "DeadTree_1", "DeadTree_2", "DeadTree_3", "DeadTree_4", "DeadTree_5" } },
            new TreeSpec { Count = 320, MinHeight = 2f, MaxHeight = 20f, MaxSlope = 28f, MinScale = 0.7f, MaxScale = 1.4f,
                Clusters = 30, ClusterRadius = 24f,
                // Bush_Common is not in this list, though the pack ships it: it is painted
                // with Leaves_TwistedTree, the red autumn sheet, so scattering it as
                // ordinary undergrowth put hundreds of scarlet bushes through the woods
                // and swamped the handful of red trees that are meant to be the accent.
                Prefabs = new[] { "Bush_Common_Flowers", "Plant_1", "Plant_1_Big", "Plant_7", "Plant_7_Big" } },
        };

        /// <summary>One tree the island wants, wherever it ended up.</summary>
        public struct TreePlacement
        {
            public string Prefab;
            public Vector2 Spot;
            public float Scale;
            public float Yaw;
            /// <summary>A real tree rather than undergrowth.</summary>
            public bool Canopy;
        }

        /// <summary>
        /// Works out where every tree on the island goes.
        ///
        /// Split out from writing them because the two kinds now go to different places:
        /// undergrowth is written into the terrain, while the trees themselves are stood
        /// up as harvestable entities by the scene. Both builders call this, and it is
        /// seeded, so both get the same forest without either having to store it.
        /// </summary>
        public static List<TreePlacement> PlaceTrees()
        {
            Random.State previous = Random.state;
            Random.InitState(NoiseSeed);
            var placements = new List<TreePlacement>();
            float half = Size * 0.5f - 4f;

            foreach (TreeSpec spec in Trees)
            {
                Vector2[] centres = PickClusters(spec, half);
                var standing = new List<Vector2>();
                int placed = 0;
                // A band that has to keep its distance from itself rejects most of what it
                // is offered once the first few are down, so it needs far more tries than
                // one that may put its trees anywhere.
                int tries = spec.Count * (spec.Spacing > 0f ? 500 : 60);
                for (int attempt = 0; attempt < tries && placed < spec.Count; ++attempt)
                {
                    float x, z;
                    if (centres.Length > 0)
                    {
                        Vector2 centre = centres[Random.Range(0, centres.Length)];
                        float angle = Random.Range(0f, Mathf.PI * 2f);
                        // Square-rooting the radius spreads points evenly over the disc
                        // rather than bunching them at the middle of every stand.
                        float distance = Mathf.Sqrt(Random.value) * spec.ClusterRadius;
                        x = centre.x + Mathf.Cos(angle) * distance;
                        z = centre.y + Mathf.Sin(angle) * distance;
                        if (Mathf.Abs(x) > half || Mathf.Abs(z) > half)
                            continue;
                    }
                    else
                    {
                        x = Random.Range(-half, half);
                        z = Random.Range(-half, half);
                    }

                    if (!Suits(spec, x, z))
                        continue;
                    if (spec.Spacing > 0f && !ClearOfOthers(standing, x, z, spec.Spacing))
                        continue;

                    placements.Add(new TreePlacement
                    {
                        Prefab = spec.Prefabs[Random.Range(0, spec.Prefabs.Length)],
                        Spot = new Vector2(x, z),
                        Scale = Random.Range(spec.MinScale, spec.MaxScale),
                        Yaw = Random.Range(0f, 360f),
                        Canopy = spec.Canopy,
                    });
                    if (spec.Spacing > 0f)
                        standing.Add(new Vector2(x, z));
                    ++placed;
                }
                if (placed < spec.Count)
                    Debug.LogWarning($"[{nameof(DemoIslandBuilder)}] Only placed {placed} of {spec.Count} for one tree band.");
            }

            Random.state = previous;
            return placements;
        }

        /// <summary>
        /// Writes the undergrowth into the terrain, and reports where the trees are.
        ///
        /// Only the undergrowth goes in here now. The trees themselves are harvestable,
        /// and a terrain tree cannot be: it is a record in the TerrainData with no
        /// GameObject to hang an entity on. The scene stands those up instead. Their
        /// positions still come back from here because the mushrooms are painted around
        /// them, and a mushroom belongs under a tree whoever is drawing it.
        /// </summary>
        private static List<Vector2> WriteTrees(TerrainData data)
        {
            List<TreePlacement> placements = PlaceTrees();

            var prototypes = new List<TreePrototype>();
            var prototypeIndex = new Dictionary<string, int>();
            var instances = new List<TreeInstance>();
            var trunks = new List<Vector2>();

            foreach (TreePlacement placement in placements)
            {
                if (placement.Canopy)
                {
                    trunks.Add(placement.Spot);
                    continue;
                }

                if (!prototypeIndex.TryGetValue(placement.Prefab, out int index))
                {
                    // The flattened copies, not the pack's own prefabs: the terrain only
                    // instances a tree whose renderer is on its root.
                    GameObject prefab = DemoTreePrefabBuilder.Load(placement.Prefab);
                    if (prefab == null)
                    {
                        Debug.LogWarning($"[{nameof(DemoIslandBuilder)}] No terrain detail prefab \"{placement.Prefab}\". Run Build Terrain Tree Prefabs first.");
                        continue;
                    }
                    index = prototypes.Count;
                    prototypeIndex[placement.Prefab] = index;
                    prototypes.Add(new TreePrototype { prefab = prefab, bendFactor = 0f });
                }

                instances.Add(new TreeInstance
                {
                    // Tree positions are a fraction of the terrain, not world units.
                    position = new Vector3((placement.Spot.x - TerrainOrigin.x) / Size, 0f, (placement.Spot.y - TerrainOrigin.z) / Size),
                    prototypeIndex = index,
                    widthScale = placement.Scale,
                    heightScale = placement.Scale,
                    rotation = placement.Yaw * Mathf.Deg2Rad,
                    color = Color.white,
                    lightmapColor = Color.white,
                });
            }

            data.treePrototypes = prototypes.ToArray();
            data.SetTreeInstances(instances.ToArray(), true);
            return trunks;
        }

        private static Vector2[] PickClusters(TreeSpec spec, float half)
        {
            if (spec.Clusters <= 0)
                return new Vector2[0];
            var centres = new List<Vector2>();
            for (int attempt = 0; attempt < spec.Clusters * 200 && centres.Count < spec.Clusters; ++attempt)
            {
                float x = Random.Range(-half, half);
                float z = Random.Range(-half, half);
                float height = HeightAt(x, z);
                if (height < spec.MinHeight || height > spec.MaxHeight)
                    continue;
                if (InsideSettlement(x, z))
                    continue;
                // Measured from the centre of the stand, less its own radius, so no part
                // of it creeps back inside the distance the band is meant to keep.
                if (spec.FromVillage > 0f &&
                    Vector2.Distance(new Vector2(x, z), VillageCentre) < spec.FromVillage + spec.ClusterRadius)
                    continue;
                centres.Add(new Vector2(x, z));
            }
            return centres.ToArray();
        }

        /// <summary>Whether a spot is far enough from the trees of its band already placed.</summary>
        private static bool ClearOfOthers(List<Vector2> standing, float x, float z, float spacing)
        {
            float squared = spacing * spacing;
            foreach (Vector2 other in standing)
            {
                if ((other.x - x) * (other.x - x) + (other.y - z) * (other.y - z) < squared)
                    return false;
            }
            return true;
        }

        private static bool Suits(TreeSpec spec, float x, float z)
        {
            float height = HeightAt(x, z);
            if (height < spec.MinHeight || height > spec.MaxHeight)
                return false;
            if (SmoothSlopeAt(x, z) > spec.MaxSlope)
                return false;
            if (spec.FromVillage > 0f && Vector2.Distance(new Vector2(x, z), VillageCentre) < spec.FromVillage)
                return false;
            // Trees stand up as harvestable entities now, so they are objects rather than
            // paint and have to keep off the settlements properly.
            return !InsideSettlement(x, z) && !OnSettledGround(x, z);
        }

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            int split = path.LastIndexOf('/');
            EnsureFolder(path.Substring(0, split));
            AssetDatabase.CreateFolder(path.Substring(0, split), path.Substring(split + 1));
        }
    }
}
