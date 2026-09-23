using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// Lays out the demo island scene.
    ///
    /// Everything here is generated rather than hand placed so the map can be retuned
    /// — a different island shape, denser woods, another spawn camp — by changing a
    /// number and running it again, instead of dragging several thousand props about.
    /// Scatter is seeded, so a rebuild reproduces the same island.
    ///
    /// The layout gives the player somewhere to start, something to do and somewhere
    /// to do it: they spawn in the village on the west plateau, and the bandits hold a
    /// camp southeast across the hills.
    /// </summary>
    public static class DemoSceneBuilder
    {
        public const string ScenePath = "Assets/OpenMMORPG/Demo/Scenes/DemoMap.unity";

        /// <summary>
        /// The entity roots a regenerate keeps. The NPCs, the horse and the animals are
        /// scene objects placed by DemoNpcBuilder, DemoMountBuilder and DemoWildlifeBuilder
        /// and then moved about by hand in the editor, which is the point of having them in
        /// the scene at all - so they are not the generator's to wipe.
        /// </summary>
        public const string NpcRootName = "Npcs";
        public const string MountRootName = "Mounts";
        public const string WildlifeRootName = "Wildlife";

        /// <summary>
        /// Where hand-placed work lives. Nothing under a root of this name is ever built,
        /// moved or destroyed by a generator, in any demo scene - park a prop here and a
        /// regenerate leaves it exactly where you put it. That is the whole contract: the
        /// generators own the scene apart from this one root, and you own this one.
        ///
        /// Unlike the entity roots it stays switched **on** for the navmesh bake, so a rock
        /// dropped in blocks the path and a plank laid down carries one, which is what you
        /// would expect of scenery. Hand-placed *characters* belong under
        /// <see cref="NpcRootName"/>, which is kept too and does come out of the bake.
        /// </summary>
        public const string AuthoredRootName = "Authored";

        /// <summary>
        /// The resurrection shrines, written by DemoShrineBuilder.
        ///
        /// Kept through a regenerate like the entity roots - a shrine can be nudged by hand
        /// and keeps the nudge - but **not** taken out of the navmesh bake, because a shrine
        /// is masonry and does not move. That is the distinction <see cref="MovableRootNames"/>
        /// exists to draw: an NPC standing on the ground would carve a hole where it stands,
        /// and an arch standing on the ground is supposed to.
        /// </summary>
        public const string ShrineRootName = "Shrines";

        /// <summary>
        /// The safe areas, written by DemoSundriesBuilder.
        ///
        /// Kept through a regenerate, and deliberately **not** in
        /// <see cref="MovableRootNames"/>: a safe area is a trigger with no renderer, and
        /// Unity's navmesh builder skips trigger colliders, so there is nothing here for a
        /// bake to carve. Switching it off would be work that changes nothing.
        /// </summary>
        public const string SafeAreaRootName = "SafeAreas";

        /// <summary>
        /// The roots holding things that stand on the ground and move, and so have to be
        /// switched off around a navmesh bake or each one carves a hole where it stands.
        /// A subset of <see cref="KeptRootNames"/>: surviving the wipe and surviving the
        /// bake are two different questions, and the Authored root answers them differently.
        /// </summary>
        public static readonly string[] MovableRootNames = { NpcRootName, MountRootName, WildlifeRootName };

        /// <summary>
        /// The settled areas, which a regenerate no longer builds over. Their generators
        /// are finished: the village layout and its measured interiors, the bandit camp,
        /// the crypt's surface entrance and the cliffs have not changed in rule for a long
        /// time, and what happens to them now is hand-tuning in the editor. So the scene is
        /// their source of truth and the code below is the record of how they were made -
        /// still runnable, and run when the root is missing, but no longer run *over* a
        /// root that is already there.
        ///
        /// `Regenerate Settled Areas` is the way back: it destroys these four and builds
        /// them again from the rules. That is also the item to run after changing one of
        /// their generators, because a full regenerate will not do it any more.
        ///
        /// One thing that does not follow the freeze: the scatter keeps off the houses by
        /// asking <see cref="InsideBuilding"/>, which reads the constant `HouseLayout`
        /// rather than the scene. Move a house far by hand and a later regenerate can
        /// scatter rocks through where it now stands - move the layout constant with it,
        /// or keep the move small.
        /// </summary>
        public const string VillageRootName = "Village";
        public const string CampRootName = "BanditCamp";
        public const string CryptRootName = "Crypt";
        public const string CliffsRootName = "Cliffs";
        public static readonly string[] FrozenAreaRootNames =
            { VillageRootName, CampRootName, CryptRootName, CliffsRootName };

        /// <summary>Every root a regenerate leaves alone.</summary>
        public static readonly string[] KeptRootNames =
            { NpcRootName, MountRootName, WildlifeRootName, AuthoredRootName, ShrineRootName, SafeAreaRootName };
        private const string NatureDir = "Assets/Plugins/Quaternius/Nature/Prefabs";
        private const string PropDir = "Assets/Plugins/Quaternius/Props/Models";
        private const string EntityDir = "Assets/OpenMMORPG/Demo/Prefabs/GamePlay/CharacterEntities";

        /// <summary>
        /// Which house is the bank. Its index into the village layout has to match where
        /// DemoNpcBuilder stands the banker, or he ends up outside in the street.
        /// </summary>
        public const int BankHouseIndex = 0;

        /// <summary>Houses ring a green, so the village has a centre to stand in.</summary>
        public static readonly Vector3[] HouseLayout =
        {
            new Vector3(-11f, 0f, -9f), new Vector3(0f, 0f, -13f), new Vector3(12f, 0f, -8f),
            new Vector3(-12f, 0f, 7f), new Vector3(1f, 0f, 12f), new Vector3(13f, 0f, 6f),
        };

        /// <summary>
        /// Turns a house so its door opens onto the green.
        ///
        /// DemoVillageBuilder puts the doorway on the house's local -Z, under the gable,
        /// so that face is the one to aim inward — pointing +Z at the centre instead
        /// leaves every door facing out into the fields.
        /// </summary>
        public static float HouseYaw(Vector3 localPosition)
        {
            return Mathf.Atan2(localPosition.x, localPosition.z) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// Which houses are built three cells square rather than two. The bank is one so
        /// it reads as the building worth walking to, and the alehouse is one because a
        /// long table with a bench down each side is 2.9 by 2.8 metres, which in a small
        /// house is the whole floor; the rest alternate so the green is not ringed by
        /// six identical boxes.
        /// </summary>
        public static bool IsBigHouse(int index)
        {
            return index == BankHouseIndex || index == AlehouseIndex || index % 3 == 1;
        }

        /// <summary>Which house is the alehouse: the one with the tankards on its sign.</summary>
        public const int AlehouseIndex = 2;

        /// <summary>Which house is the smith's: the anvil on its sign, and the yard out front.</summary>
        public const int SmithHouseIndex = 1;

        /// <summary>
        /// The pedlar's stall on the green: where it stands and which way it is turned.
        /// Its counter and goods are on its own +Z, which this yaw points at the fire, so
        /// customers come to it from the green. DemoNpcBuilder stands the pedlar off these
        /// so he is behind his own counter however the market is moved.
        /// </summary>
        public static readonly Vector3 StallLayout = new Vector3(6f, 0f, 5f);
        public const float StallYaw = 215f;

        /// <summary>
        /// Where the watchtower stands: in the east gap of the ring, between the alehouse
        /// and the house beyond it, which is the side the bandits' camp is on. It takes the
        /// fence's place in that gap. Its ladder faces the green, so it is seen from
        /// anywhere on it.
        ///
        /// It stands a pace further out than the houses do. The alehouse is a big house,
        /// and a big house's roof reaches a metre and a half past its walls: at the houses'
        /// own radius the tower's wall stood 0.6m inside those eaves. Out here it clears
        /// them by as much, and the ground under its far corners has only begun to climb -
        /// 0.11m, less than the height of the walls' plinth.
        /// </summary>
        public static readonly Vector3 WatchtowerLayout = new Vector3(15f, 0f, -0.5f);

        /// <summary>The tower's footprint, in grid cells a side, and how many storeys of wall it is.</summary>
        public const int WatchtowerCells = 2;
        public const int WatchtowerStoreys = 2;

        /// <summary>The top of the tower's deck boards, above the village ground: where a guard stands.</summary>
        public static float WatchtowerDeckHeight
        {
            get { return WatchtowerStoreys * DemoVillageBuilder.WallHeight + 0.01f; }
        }

        private const int ScatterSeed = 8712;
        private const int CliffSeed = 4471;
        private const int GreenSeed = 5290;
        private const int HarvestSeed = 6613;

        /// <summary>
        /// Under this, a thing on the ground is litter and gets no collision: you step
        /// over a stone this size rather than walking into it. The audit reads the same
        /// number, so the two cannot drift into disagreeing about what counts.
        /// </summary>
        public const float LitterHeight = 0.35f;

        [MenuItem("Open MMORPG/Demo/Regenerate Island Scene (destroys hand edits)")]
        public static void Build()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var hidden = new System.Collections.Generic.List<GameObject>();
            var standing = new System.Collections.Generic.HashSet<string>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (System.Array.IndexOf(KeptRootNames, root.name) >= 0)
                {
                    // Kept either way. Only the roots that move come out of the bake below;
                    // anything under Authored is scenery and stays in it.
                    if (System.Array.IndexOf(MovableRootNames, root.name) >= 0 && root.activeSelf)
                        hidden.Add(root);
                    continue;
                }
                // A settled area that is already standing is left exactly as it is, and its
                // builder is skipped below. See FrozenAreaRootNames.
                if (System.Array.IndexOf(FrozenAreaRootNames, root.name) >= 0)
                {
                    standing.Add(root.name);
                    continue;
                }
                Object.DestroyImmediate(root);
            }

            BuildLighting(scene);
            // The fire the torches and campfires burn. Rebuilt with the scene so the
            // recipe and the scene never drift apart; it has its own menu item too.
            DemoFlameBuilder.Build();
            GameObject terrain = BuildTerrain(scene);
            BuildSea(scene);
            BuildSettledAreas(scene, standing);
            System.Collections.Generic.List<Vector3> boulders = BuildNature(scene);
            BuildSpawners(scene);
            BuildHarvestNodes(scene, boulders);

            // Bake last: the navmesh has to see the finished ground and every building.
            NavMeshSurface surface = terrain.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            // The sea's swimming volume is a trigger on the Water layer; a bake ignores
            // triggers, but the layer is masked out as well so it can never read as a
            // floor at sea level.
            surface.layerMask &= ~(1 << PhysicLayers.Water);
            // And the sea floor is taken out of the walkable ground, so that nothing
            // which walks wanders, chases or bolts into the water.
            EnsureSeaCarve(terrain);
            // A bench seat is walkable ground to a bake unless it is told otherwise.
            MarkFurnitureUnwalkable(scene);
            // The NPCs and the horse stand on the green with a capsule each, which the
            // bake would read as a post and cut a hole round. They are not part of the
            // ground, and they move.
            foreach (GameObject root in hidden)
                root.SetActive(false);
            BakeWithDoorsOpen(surface);
            foreach (GameObject root in hidden)
                root.SetActive(true);

            CheckArrivalIsClear();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[{nameof(DemoSceneBuilder)}] Built {ScenePath}.");
        }

        /// <summary>
        /// Builds each settled area that is not already standing, and says which ones it
        /// left alone. Shared by the full regenerate and by `Regenerate Settled Areas`,
        /// which passes an empty set so that all four are built again.
        /// </summary>
        private static void BuildSettledAreas(Scene scene, System.Collections.Generic.HashSet<string> standing)
        {
            if (!standing.Contains(VillageRootName))
                BuildVillage(scene);
            if (!standing.Contains(CampRootName))
                BuildCamp(scene);
            if (!standing.Contains(CryptRootName))
                BuildCrypt(scene);
            if (!standing.Contains(CliffsRootName))
                BuildCliffs(scene);

            if (standing.Count == 0)
                return;
            Debug.Log($"[{nameof(DemoSceneBuilder)}] Kept {standing.Count} settled area(s) as they stand: " +
                      $"{string.Join(", ", standing)}. Their generators are finished and the scene is the " +
                      "source of truth for them - run Regenerate Settled Areas to build them from the rules again.");
        }

        /// <summary>
        /// Throws away the settled areas and builds all four from the rules, on the scene
        /// that is already there. The way back from the freeze described on
        /// <see cref="FrozenAreaRootNames"/>.
        ///
        /// Rebakes the navmesh afterwards: moving a wall the player walks past without
        /// rebaking leaves them walking through it, or into thin air where it used to be.
        /// </summary>
        [MenuItem("Open MMORPG/Demo/Regenerate Settled Areas (village, camp, crypt, cliffs)", priority = 112)]
        public static void RegenerateSettledAreas()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            int removed = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (System.Array.IndexOf(FrozenAreaRootNames, root.name) < 0)
                    continue;
                Object.DestroyImmediate(root);
                ++removed;
            }

            // The torches these areas place are instances of the flame prefabs, so the
            // prefabs have to exist before the areas are built - the same order a full
            // regenerate uses.
            DemoFlameBuilder.Build();
            BuildSettledAreas(scene, new System.Collections.Generic.HashSet<string>());

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[{nameof(DemoSceneBuilder)}] Rebuilt the settled areas in {ScenePath} " +
                      $"({removed} root(s) replaced). Rebaking the navmesh over them now.");
            RebakeNavMesh();
        }

        private static GameObject Root(Scene scene, string name)
        {
            var go = new GameObject(name);
            EditorSceneManager.MoveGameObjectToScene(go, scene);
            return go;
        }

        private const string DaySkyDir = "Assets/Plugins/Blue Sky Skybox Pack/blue sky no sun";
        private const string DaySkyPrefix = "jettelly_no_sun_";
        private const string NightSkyDir = "Assets/Plugins/Blue Sky Skybox Pack/night moon";
        private const string NightSkyPrefix = "jettelly_moon_";
        private const string SkyMaterialPath = "Assets/OpenMMORPG/Demo/Materials/IslandSky.mat";
        private const string ClockPath = "Assets/OpenMMORPG/Demo/GameData/DayNightClock.asset";

        /// <summary>
        /// Builds the sky material from the Jettelly cube faces.
        ///
        /// The set without a sun, deliberately. The sunshine set has its sun painted near
        /// the top of the dome, and the demo's own light comes in at forty-eight degrees
        /// because that is the angle that puts long shadows down the island rather than
        /// short ones underneath everything. Using a sky with a sun in it would mean
        /// either two suns disagreeing, or re-aiming the light to match the painting and
        /// losing the shadows. A stylised sky without a visible sun reads perfectly well.
        /// </summary>
        private static Material BuildSky()
        {
            string[][] faces =
            {
                new[] { "FRONT", "_FrontTex", "_NightFrontTex" }, new[] { "BACK", "_BackTex", "_NightBackTex" },
                new[] { "LEFT", "_LeftTex", "_NightLeftTex" }, new[] { "RIGHT", "_RightTex", "_NightRightTex" },
                new[] { "UP", "_UpTex", "_NightUpTex" }, new[] { "DOWN", "_DownTex", "_NightDownTex" },
            };

            Shader shader = Shader.Find("Demo/DayNightSkybox");
            if (shader == null)
            {
                Debug.LogError($"[{nameof(DemoSceneBuilder)}] No Demo/DayNightSkybox shader.");
                return null;
            }

            Material material = AssetDatabase.LoadAssetAtPath<Material>(SkyMaterialPath);
            if (material == null)
            {
                material = new Material(shader);
                DemoIslandBuilder.EnsureFolder(System.IO.Path.GetDirectoryName(SkyMaterialPath).Replace('\\', '/'));
                AssetDatabase.CreateAsset(material, SkyMaterialPath);
            }
            material.shader = shader;

            foreach (string[] face in faces)
            {
                material.SetTexture(face[1], SkyFace($"{DaySkyDir}/{DaySkyPrefix}{face[0]}.png"));
                material.SetTexture(face[2], SkyFace($"{NightSkyDir}/{NightSkyPrefix}{face[0]}.png"));
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// Loads one face of a sky, importing it the way a sky needs.
        ///
        /// Clamped, or the filtering at the edge of a face wraps round to the opposite
        /// edge and every join in the cube shows as a seam.
        /// </summary>
        private static Texture2D SkyFace(string path)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null)
            {
                Debug.LogError($"[{nameof(DemoSceneBuilder)}] Missing sky face \"{path}\".");
                return null;
            }
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null && (importer.wrapMode != TextureWrapMode.Clamp || importer.mipmapEnabled))
            {
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
                texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            return texture;
        }

        /// <summary>A gradient with the same colour at both midnights, so the day loops.</summary>
        private static Gradient DayGradient(Color night, Color dawn, Color noon, Color dusk)
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(night, 0f),
                    new GradientColorKey(dawn, 0.27f),
                    new GradientColorKey(noon, 0.5f),
                    new GradientColorKey(dusk, 0.79f),
                    new GradientColorKey(night, 1f),
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return gradient;
        }

        /// <summary>
        /// The colour of the sky just above the horizon, read off the sky itself.
        ///
        /// Used for the haze, which exists to hide the edge of the sea plane: pick that
        /// colour by eye and the plane's end shows as a band of slightly wrong blue
        /// against the sky. Taken from the sky, the two meet invisibly.
        /// </summary>
        private static Color SkyHorizon(string dir, string prefix)
        {
            string path = $"{dir}/{prefix}FRONT.png";
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                return new Color(0.68f, 0.78f, 0.86f);
            bool wasReadable = importer.isReadable;
            if (!wasReadable)
            {
                importer.isReadable = true;
                importer.SaveAndReimport();
            }

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            Color total = Color.black;
            int taken = 0;
            // A band across the middle of the face, which is the horizon: the faces are
            // painted with the sky above and the haze below, and the join is the middle.
            for (int x = 0; x < texture.width; x += 16)
            {
                for (int y = texture.height / 2 - 24; y < texture.height / 2 + 24; y += 8)
                {
                    total += texture.GetPixel(x, y);
                    ++taken;
                }
            }

            if (!wasReadable)
            {
                importer.isReadable = false;
                importer.SaveAndReimport();
            }
            if (taken == 0)
                return new Color(0.68f, 0.78f, 0.86f);
            Color mean = total / taken;
            mean.a = 1f;
            return mean;
        }

        private static void BuildLighting(Scene scene)
        {
            GameObject go = Root(scene, "Directional Light");
            Light light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.Soft;
            // Shadows that block all the light turn the floor of a wood black, and the
            // undergrowth that was scattered across it stops reading at all — a canopy
            // in this art style is meant to dapple the ground, not switch it off.
            light.shadowStrength = 0.72f;
            RenderSettings.sun = light;

            Material sky = BuildSky();
            if (sky != null)
                RenderSettings.skybox = sky;

            // Haze hides the seam where the sea plane ends rather than pushing the plane
            // out far enough to reach the horizon.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 180f;
            RenderSettings.fogEndDistance = 620f;

            BuildSkyCycle(scene, light, sky);
        }

        /// <summary>
        /// Hangs the day on the kit's clock.
        ///
        /// The colours are gradients across the whole twenty-four hours rather than a
        /// day setting and a night setting, because the thing worth seeing in a cycle is
        /// the half hour either side of dawn — and both ends of a gradient have to be the
        /// same colour or midnight has a seam in it.
        ///
        /// The ambient is still Trilight, with the horizon term carrying most of the
        /// weight: it is what fills the shade under a canopy, and letting the bright cyan
        /// sky light the world instead tints everything with it.
        /// </summary>
        private static void BuildSkyCycle(Scene scene, Light sun, Material sky)
        {
            GameObject go = Root(scene, "SkyCycle");
            var cycle = go.AddComponent<MultiplayerARPG.Demo.DemoSkyCycle>();
            cycle.sun = sun;
            cycle.sky = sky;
            cycle.sunrise = 6f;
            cycle.sunset = 19f;
            cycle.twilight = 1.5f;
            cycle.noonIntensity = 1.15f;
            cycle.nightIntensity = 0.10f;
            cycle.previewHour = 12f;

            cycle.sunColour = DayGradient(
                new Color(0.42f, 0.50f, 0.72f),   // midnight, what little the moon gives
                new Color(1.00f, 0.72f, 0.50f),   // dawn, low and warm
                new Color(1.00f, 0.96f, 0.88f),   // noon
                new Color(1.00f, 0.63f, 0.42f));  // dusk
            cycle.skyAmbient = DayGradient(
                new Color(0.10f, 0.13f, 0.24f),
                new Color(0.44f, 0.48f, 0.62f),
                new Color(0.58f, 0.68f, 0.80f),
                new Color(0.40f, 0.40f, 0.56f));
            cycle.horizonAmbient = DayGradient(
                new Color(0.09f, 0.12f, 0.18f),
                new Color(0.34f, 0.38f, 0.38f),
                new Color(0.44f, 0.52f, 0.42f),
                new Color(0.32f, 0.31f, 0.34f));
            cycle.groundAmbient = DayGradient(
                new Color(0.05f, 0.06f, 0.09f),
                new Color(0.22f, 0.21f, 0.19f),
                new Color(0.32f, 0.31f, 0.26f),
                new Color(0.20f, 0.18f, 0.18f));
            // The haze has to be the colour of the sky behind it at every hour, or the
            // sea's far edge shows against it as a band. Both ends are read off the skies
            // themselves rather than picked: a night fog chosen by eye came out a grey
            // blue against a deep blue night sky, and the water lay across the horizon in
            // a paler stripe than anything around it.
            Color dayHorizon = SkyHorizon(DaySkyDir, DaySkyPrefix);
            Color nightHorizon = SkyHorizon(NightSkyDir, NightSkyPrefix);
            cycle.fog = DayGradient(
                nightHorizon,
                Color.Lerp(nightHorizon, dayHorizon, 0.55f),
                dayHorizon,
                Color.Lerp(nightHorizon, dayHorizon, 0.45f));

            // Light the scene as it will look at noon, so the editor shows the island
            // rather than whatever hour the component happened to be left at.
            cycle.Apply(cycle.previewHour);
        }

        /// <summary>
        /// Puts the island in the scene.
        ///
        /// A Terrain added from a script has no material, and under URP a terrain with no
        /// material draws nothing at all — the island simply vanishes while everything
        /// standing on it stays where it was. The material comes from the render pipeline
        /// itself.
        ///
        /// The draw distances are set rather than left at Unity's defaults: trees carry to
        /// 340 and never billboard, because a billboarded Quaternius tree is a flat card
        /// that pops as you walk toward it, and the undergrowth reaches 90.
        /// </summary>
        private static GameObject BuildTerrain(Scene scene)
        {
            GameObject go = Root(scene, "Island");
            TerrainData data = DemoIslandBuilder.LoadTerrainData();
            if (data == null)
            {
                Debug.LogError($"[{nameof(DemoSceneBuilder)}] No island terrain. Run Build Island Terrain first.");
                return go;
            }

            go.transform.position = DemoIslandBuilder.TerrainOrigin;
            Terrain terrain = go.AddComponent<Terrain>();
            terrain.terrainData = data;
            terrain.materialTemplate = DemoIslandBuilder.TerrainMaterial();
            terrain.treeDistance = 340f;
            // Equal to the tree distance, which means never: past this a tree becomes a
            // flat card facing the camera, and these trees do not survive that.
            terrain.treeBillboardDistance = 340f;
            terrain.detailObjectDistance = 90f;
            terrain.heightmapPixelError = 3f;
            terrain.basemapDistance = 220f;

            go.AddComponent<TerrainCollider>().terrainData = data;
            MarkStatic(go);
            return go;
        }

        private const string SeaMeshPath = "Assets/OpenMMORPG/Demo/Meshes/SeaSurface.asset";
        private const string SeaMaterialPath = "Assets/OpenMMORPG/Demo/Materials/Island_Sea.mat";

        /// <summary>
        /// Half-width of the sea. This has to reach past where the fog finishes, not
        /// merely a long way: the sea fades toward the fog colour with distance, so an
        /// edge that stops short of that is still part sea blue when it ends, and against
        /// a bright sky the join reads as a hard line drawn across the water. Beyond the
        /// fog's end the surface is exactly the colour of the horizon behind it and there
        /// is nothing to see.
        /// </summary>
        private const float SeaRadius = 750f;

        /// <summary>
        /// Quads across the sea grid. 225 vertices a side is 50,625 in all, which stays
        /// under the 65,536 that lets Unity keep a 16-bit index buffer — worth having for
        /// a surface that is, after all, flat.
        /// </summary>
        private const int SeaGrid = 224;

        /// <summary>
        /// How much of the grid spacing stays linear rather than bending into the cubic.
        /// Lower crowds more of the mesh into the middle.
        /// </summary>
        private const float SeaGridLinear = 0.30f;

        /// <summary>
        /// Where the nth grid line sits along an axis.
        ///
        /// Spacing a grid evenly over fifteen hundred metres gives a choice between too
        /// coarse at the shore to carry a wave and absurdly dense out at the fog line.
        /// Bending the spacing through a cubic puts the vertices where the player is:
        /// about two metres apart over the island's shallows, fourteen at the far edge,
        /// where the shader has faded the displacement out anyway.
        /// </summary>
        private static float SeaGridPosition(int index)
        {
            float u = index * 2f / SeaGrid - 1f;
            return SeaRadius * (SeaGridLinear * u + (1f - SeaGridLinear) * u * u * u);
        }

        /// <summary>
        /// Fills a mesh with the graded sea grid. The mesh is rewritten in place rather
        /// than replaced so its GUID survives a rebuild.
        /// </summary>
        private static void FillSeaMesh(Mesh mesh)
        {
            const int side = SeaGrid + 1;
            var vertices = new Vector3[side * side];
            var normals = new Vector3[side * side];
            for (int z = 0; z < side; z++)
            {
                float pz = SeaGridPosition(z);
                for (int x = 0; x < side; x++)
                {
                    int i = z * side + x;
                    vertices[i] = new Vector3(SeaGridPosition(x), 0f, pz);
                    normals[i] = Vector3.up;
                }
            }

            var triangles = new int[SeaGrid * SeaGrid * 6];
            int t = 0;
            for (int z = 0; z < SeaGrid; z++)
            {
                for (int x = 0; x < SeaGrid; x++)
                {
                    int i = z * side + x;
                    triangles[t++] = i;
                    triangles[t++] = i + side;
                    triangles[t++] = i + 1;
                    triangles[t++] = i + 1;
                    triangles[t++] = i + side;
                    triangles[t++] = i + side + 1;
                }
            }

            mesh.Clear();
            mesh.name = "SeaSurface";
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.triangles = triangles;
            // Set by hand because the shader lifts the crests after the bounds are read,
            // and water culled at the top of the screen because its bounds say it is flat
            // is a hard thing to go looking for.
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(SeaRadius * 2f, 4f, SeaRadius * 2f));
        }

        /// <summary>
        /// The sea material.
        ///
        /// A material already on the water shader is left exactly as it is, so tuning the
        /// sea in the inspector survives a scene rebuild. One that is missing, or still on
        /// the lit shader the sea used before, is replaced with the shader's own defaults,
        /// which are where the demo's look is authored.
        /// </summary>
        private static Material SeaMaterial()
        {
            Shader shader = Shader.Find("Demo/StylizedWater");
            if (shader == null)
            {
                Debug.LogError($"[{nameof(DemoSceneBuilder)}] Demo/StylizedWater is missing; the sea will be untextured.");
                return AssetDatabase.LoadAssetAtPath<Material>(SeaMaterialPath);
            }

            Material water = AssetDatabase.LoadAssetAtPath<Material>(SeaMaterialPath);
            if (water != null && water.shader == shader)
                return water;

            AssetDatabase.DeleteAsset(SeaMaterialPath);
            water = new Material(shader);
            // Creating a material from a shader takes the default property values but not
            // the keywords those values stand for, so the two [Toggle] properties read as
            // on while the branches they gate are compiled out. Both need saying twice.
            water.SetFloat("_Shore", 1f);
            water.EnableKeyword("_SHORE_ON");
            water.SetFloat("_Refraction", 1f);
            water.EnableKeyword("_REFRACTION_ON");
            AssetDatabase.CreateAsset(water, SeaMaterialPath);
            return water;
        }

        private static void BuildSea(Scene scene)
        {
            GameObject go = Root(scene, "Sea");
            var surface = new GameObject("Surface");
            surface.transform.SetParent(go.transform, false);
            surface.transform.localPosition = new Vector3(0f, DemoIslandBuilder.WaterLevel, 0f);

            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(SeaMeshPath);
            if (mesh == null)
            {
                // The folder has to be made first. CreateAsset into one that is not there
                // throws rather than returning, which takes the whole scene build down with
                // it — the village, the props and the spawners never run, and what is left
                // is an island with nothing on it. Unity removes a folder once the last
                // asset in it goes, so this cannot be assumed to survive between runs.
                DemoItemBuilder.EnsureFolder(System.IO.Path.GetDirectoryName(SeaMeshPath).Replace('\\', '/'));
                mesh = new Mesh();
                AssetDatabase.CreateAsset(mesh, SeaMeshPath);
            }
            FillSeaMesh(mesh);
            EditorUtility.SetDirty(mesh);

            surface.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = surface.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = SeaMaterial();
            // The sea is transparent and its crests only exist in the vertex shader, so a
            // shadow from it would be a hard-edged slab cast by the flat mesh underneath.
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            BuildSeaVolume(go);
            BuildAmbience(go);
        }

        /// <summary>
        /// The island's two ambience beds and its music, under the sea root so `Rebuild Sea` renews
        /// them: the nature loop everywhere, and the waves loud on the beach, half-heard in
        /// the village and a murmur in the middle of the island. Each is a 2D loop driven by
        /// <see cref="MultiplayerARPG.Demo.DemoAmbientLoop"/>, which follows the ambient
        /// volume setting. A bed whose clip is not provided yet is simply not built;
        /// DemoAudioWiring lists what is missing.
        ///
        /// The waves fade by **distance from the waterline**, traced off the terrain at
        /// runtime, with the height rule kept for the hills. Height on its own did not
        /// do it: the island is a plateau at 3.5-7.5 m with the beach at 1.5 m, so a fade
        /// keyed to height left the village at seven tenths of the beach's volume and the
        /// sea sounded the same everywhere. The numbers here are set against the island's
        /// layout - village centre 43 m from the coast, camp 26 m, crypt 48 m, the middle
        /// 76 m - so the village hears roughly four tenths, the camp two thirds, and the
        /// interior the floor.
        /// </summary>
        private static void BuildAmbience(GameObject sea)
        {
            var root = new GameObject("Ambience");
            root.transform.SetParent(sea.transform, false);
            AmbientBed(root, "Nature", DemoAudioWiring.Clips(DemoAudioWiring.AmbientNature), 0.5f, false);
            MultiplayerARPG.Demo.DemoAmbientLoop shore = AmbientBed(root, "Shore", DemoAudioWiring.Clips(DemoAudioWiring.OceanWaves), 0.8f, true);
            if (shore != null)
            {
                shore.fadeWithShoreDistance = true;
                shore.fullWithinShoreDistance = ShoreFullWithin;
                shore.quietBeyondShoreDistance = ShoreQuietBeyond;
                shore.shoreQuietVolume = ShoreQuietVolume;
                shore.fullBelowHeight = ShoreFullBelowHeight;
                shore.quietAboveHeight = ShoreQuietAboveHeight;
                shore.quietVolume = ShoreHeightQuietVolume;
            }
            BuildMusic(root);
        }

        /// <summary>Metres inland from the waterline over which the waves are at full volume: the beach.</summary>
        private const float ShoreFullWithin = 8f;
        /// <summary>Metres inland by which the waves are down to <see cref="ShoreQuietVolume"/>.</summary>
        private const float ShoreQuietBeyond = 60f;
        /// <summary>The waves' volume deep inland; not silence, the island is only 110 m across.</summary>
        private const float ShoreQuietVolume = 0.1f;
        /// <summary>
        /// The height rule's thresholds, set above the plateau (village 5.5 m, camp 7.5 m)
        /// so it only bites on the hills and the crypt's rise, and above the beach-cliff
        /// tops rather than on them.
        /// </summary>
        private const float ShoreFullBelowHeight = 8f;
        private const float ShoreQuietAboveHeight = 24f;
        private const float ShoreHeightQuietVolume = 0.4f;

        /// <summary>
        /// The island's music, on the schedule world music runs on in an MMO.
        ///
        /// It sits with the ambience because it is renewed the same way and belongs to the
        /// same layer of the mix, but it is not an ambience bed - it follows the **BGM**
        /// setting, not the ambient one, and it is silent most of the time.
        ///
        /// Three things make it world music rather than a soundtrack:
        ///
        /// **It greets you.** A piece plays <see cref="LoginDelay"/> seconds after the
        /// character is in the world, which is what every MMO does on zoning in: the world
        /// is drawn, the ambience is up, and then the zone announces itself. The delay is
        /// short but not zero - starting on the same frame the player appears puts the music
        /// under the tail of the loading screen, where it is heard as part of the interface
        /// rather than as part of the island. The clock starts from the **listener**
        /// appearing, not from the scene loading, and on a map scene the listener arrives
        /// with the player - so this is timed from logging in, not from the level streaming.
        ///
        /// **Then it leaves you alone.** <see cref="GapMin"/> to <see cref="GapMax"/>
        /// seconds of silence between plays, so the ambience beds carry the island most of
        /// the time and the music is an event when it returns. A single piece looping over
        /// an island this size would be wallpaper inside an hour.
        ///
        /// **And it does not follow a running order.** With more than one island track,
        /// <see cref="MultiplayerARPG.Demo.DemoMusicPlayer"/> picks at random and never
        /// repeats the piece it just played.
        /// </summary>
        private static void BuildMusic(GameObject parent)
        {
            AudioClip[] tracks = DemoAudioWiring.MusicClips(DemoAudioWiring.IslandMusic);
            if (tracks.Length == 0)
                return;
            var go = new GameObject("Music");
            go.transform.SetParent(parent.transform, false);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.volume = 0f;
            var music = go.AddComponent<MultiplayerARPG.Demo.DemoMusicPlayer>();
            music.tracks = tracks;
            music.mode = MultiplayerARPG.Demo.DemoMusicPlayer.PlayMode.Occasional;
            music.volume = DemoAudioWiring.IslandMusicVolume;
            music.firstGapMin = LoginDelay;
            music.firstGapMax = LoginDelay;
            music.gapMin = GapMin;
            music.gapMax = GapMax;
        }

        /// <summary>
        /// How long after the character reaches the island its music starts.
        ///
        /// Fixed rather than a range: this one is a cue, not a shuffle, and the same beat
        /// every login is what makes it read as the island greeting you. Eight seconds plus
        /// the player's own fade in is long enough for the loading screen to be gone and the
        /// ambience to have established the place first.
        /// </summary>
        private const float LoginDelay = 8f;

        /// <summary>
        /// The silence between plays afterwards - a few minutes, the range MMO zone music
        /// sits in. Long enough that the island is mostly its own ambience, short enough
        /// that a session hears the piece more than once.
        /// </summary>
        private const float GapMin = 120f;
        private const float GapMax = 300f;

        private static MultiplayerARPG.Demo.DemoAmbientLoop AmbientBed(GameObject parent, string name, AudioClip[] clips, float volume, bool fadeWithHeight)
        {
            if (clips.Length == 0)
                return null;
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            var source = go.AddComponent<AudioSource>();
            source.clip = clips[0];
            source.loop = true;
            source.playOnAwake = true;
            source.spatialBlend = 0f;
            source.volume = volume;
            var loop = go.AddComponent<MultiplayerARPG.Demo.DemoAmbientLoop>();
            loop.baseVolume = volume;
            loop.fadeWithHeight = fadeWithHeight;
            loop.seaLevel = DemoIslandBuilder.WaterLevel;
            return loop;
        }

        /// <summary>
        /// How far below sea level the water volume reaches. Deeper than the seabed, so a
        /// character can never fall out of the bottom of it.
        /// </summary>
        private const float SeaVolumeDepth = 30f;

        /// <summary>
        /// The water the kit can swim in. To the kit, water is a trigger collider on the
        /// built-in Water layer: a character's movement keeps the last one it entered and
        /// counts itself under water once it sits low enough against that collider's top,
        /// so the top of this box has to be sea level exactly. The box covers the whole sea
        /// mesh and reaches below the seabed. Walking in from the beach, the character
        /// wades until the bottom drops away and then swims; the movement's
        /// autoSwimToSurface (set on the player entities and the horse) keeps it on the
        /// surface and ignores any attempt to dive, which is all the demo wants of the
        /// sea. Raycasts that find ground ignore triggers, so spawners, warps and the
        /// camera's wall spring (which masks Water out anyway) see nothing new.
        /// </summary>
        private static void BuildSeaVolume(GameObject sea)
        {
            var volume = new GameObject("Volume");
            volume.transform.SetParent(sea.transform, false);
            volume.layer = PhysicLayers.Water;
            var box = volume.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = new Vector3(0f, DemoIslandBuilder.WaterLevel - SeaVolumeDepth * 0.5f, 0f);
            box.size = new Vector3(SeaRadius * 2f, SeaVolumeDepth, SeaRadius * 2f);
        }

        /// <summary>
        /// Replaces the sea in whatever scene is open, and nothing else.
        ///
        /// Building the island scene throws the map away and lays it out again, which is
        /// a great deal to go through to look at the water. Retuning the sea is the one
        /// thing anyone does repeatedly, so it gets its own way in.
        /// </summary>
        [MenuItem("Open MMORPG/Demo/Rebuild Sea")]
        public static void RebuildSea()
        {
            Scene scene = SceneManager.GetActiveScene();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == "Sea")
                    Object.DestroyImmediate(root);
            }

            BuildSea(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"[{nameof(DemoSceneBuilder)}] Rebuilt the sea in {scene.name}.");
        }

        /// <summary>
        /// Bakes the island's navmesh on the scene as it stands, without rebuilding it.
        ///
        /// `Regenerate Island Scene` bakes as its last step, but it also **rewrites
        /// `DemoMap.unity` from nothing**, which is far too much to run when all that is
        /// wrong is the navmesh. This is the same bake, on the scene that is already
        /// there.
        ///
        /// Worth knowing what a missing bake looks like, because it does not announce
        /// itself: the surface component is still on the terrain, the scene still opens,
        /// and the only sign is `Failed to create agent because there is no valid
        /// NavMesh` in the map server's warning log - one line per entity that tried to
        /// exist, so a quiet map produces only a handful and it reads as noise. Nothing
        /// bakes at runtime; if no `NavMeshData` asset sits beside the scene, there is no
        /// navmesh at all and nothing that walks can walk.
        /// </summary>
        /// <summary>
        /// The furniture a character should walk **around**, not over.
        ///
        /// Prefixes rather than a full list of names, because the scatter numbers its
        /// copies - `Bench`, `Bench (1)`, `Bench (2)` - and because the library names a
        /// family consistently: everything beginning `Barrel` is a barrel.
        ///
        /// What is deliberately **not** here is anything structural. `Collision` (65 of
        /// them in the village alone) is the houses' own collision, `Balcony`, `Floor`,
        /// `Stair` and `DoorFrame` are the buildings themselves - carve any of those and
        /// the interiors stop being reachable. The rule is furniture and containers: the
        /// things standing *on* a floor.
        /// </summary>
        private static readonly string[] UnwalkableFurniture =
        {
            "Bench", "Stool", "Chair_", "Table_", "Workbench", "WeaponStand", "Hammer_Rack",
            "Anvil_Log", "Bellows", "Whetstone", "Barrel", "Crate_", "FarmCrate_", "Chest_",
            "Bag", "Wagon", "Stall_", "Cabinet", "Dresser_", "Nightstand_", "Bookcase",
            "Bed_", "Shelf_", "Desk", "Altar", "Bucket_",
        };

        /// <summary>
        /// Marks the furniture non-walkable before a bake, so that a bench is an obstacle
        /// rather than a raised pavement.
        ///
        /// **A low prop with a flat top is walkable ground to a bake.** The surface
        /// collects every collider (`CollectObjects.All`, `PhysicsColliders`), the agent's
        /// climb is 0.75m and a bench seat is 0.53m up, so the voxelizer put walkable
        /// polygons across the seats and the agents took the shortcut over them: measured
        /// on 2026-09-22, three of the village's six benches and stools had navmesh laid
        /// on top of them, which is exactly the ones the guard and the dog were seen
        /// strolling across.
        ///
        /// Marking the geometry `Not Walkable` fixes both halves at once. No walkable
        /// polygon is generated on the seat - and the terrain voxels *underneath* the
        /// bench then fail the agent-height test, because the clearance from the ground to
        /// the underside of the seat is nothing like two metres, so the bench's footprint
        /// drops out of the navmesh as well and the path goes round. One flag, not a
        /// carving obstacle and not a hand-placed block-out.
        ///
        /// Players are unaffected: a player moves on a CharacterController and never
        /// consults the navmesh. This only reaches what walks on agents - the patrolling
        /// guard, the dog and the monsters.
        ///
        /// **Outdoors only.** Interior furniture is skipped, and the first cut of this did
        /// not skip it: carving the bank's desk left Fenwick standing on the one square of
        /// floor no path could reach, and a room is small enough that a bed, a dresser and
        /// a chair between them can shut it. Nothing in the demo patrols indoors, so there
        /// is nothing to gain there and a pet following its owner through a door to lose.
        /// </summary>
        private const string InteriorGroupName = "Interior";

        private static int MarkFurnitureUnwalkable(Scene scene)
        {
            int marked = 0;
            // Clear first, so that narrowing the rule takes effect on a rerun. A modifier
            // left behind by an earlier, wider version of this is invisible in the scene
            // and carves the ground anyway; only furniture is touched, so a modifier put
            // anywhere by hand is left alone.
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (System.Array.IndexOf(FrozenAreaRootNames, root.name) < 0)
                    continue;
                foreach (NavMeshModifier stale in root.GetComponentsInChildren<NavMeshModifier>(true))
                {
                    bool furniture = false;
                    for (int i = 0; i < UnwalkableFurniture.Length && !furniture; ++i)
                        furniture = stale.name.StartsWith(UnwalkableFurniture[i]);
                    if (furniture)
                        Object.DestroyImmediate(stale, true);
                }
            }

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (System.Array.IndexOf(FrozenAreaRootNames, root.name) < 0)
                    continue;
                foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
                {
                    if (collider.isTrigger)
                        continue;
                    bool indoors = false;
                    for (Transform up = collider.transform; up != null && !indoors; up = up.parent)
                        indoors = up.name == InteriorGroupName;
                    if (indoors)
                        continue;
                    // The named piece, which is not always the object the collider is on -
                    // a library prop keeps its mesh on a child.
                    Transform piece = collider.transform;
                    string name = piece.name;
                    bool match = false;
                    while (piece != null && piece != root.transform)
                    {
                        name = piece.name;
                        for (int i = 0; i < UnwalkableFurniture.Length && !match; ++i)
                            match = name.StartsWith(UnwalkableFurniture[i]);
                        if (match)
                            break;
                        piece = piece.parent;
                    }
                    if (!match)
                        continue;
                    var modifier = piece.GetComponent<NavMeshModifier>();
                    if (modifier == null)
                        modifier = piece.gameObject.AddComponent<NavMeshModifier>();
                    modifier.overrideArea = true;
                    // 1 is the kit-independent index of Unity's built-in "Not Walkable"
                    // area, which every project has and none can remove.
                    modifier.area = 1;
                    modifier.applyToChildren = true;
                    ++marked;
                }
            }
            if (marked > 0)
                Debug.Log($"[{nameof(DemoSceneBuilder)}] {marked} piece(s) of furniture marked non-walkable.");
            return marked;
        }

        [MenuItem("Open MMORPG/Demo/Rebake Island Navmesh", priority = 110)]
        public static void RebakeNavMesh()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            NavMeshSurface surface = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                surface = root.GetComponentInChildren<NavMeshSurface>(true);
                if (surface != null)
                    break;
            }
            if (surface == null)
            {
                Debug.LogError($"[{nameof(DemoSceneBuilder)}] No {nameof(NavMeshSurface)} in {ScenePath}. " +
                               "Only Regenerate Island Scene creates one, and that rewrites the scene.");
                return;
            }

            // The same settings the build uses. Set again rather than trusted, because a
            // surface that has been round a scene save can have been edited by hand.
            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask &= ~(1 << PhysicLayers.Water);
            EnsureSeaCarve(surface.gameObject);
            MarkFurnitureUnwalkable(scene);

            // The NPCs, the horse and the wildlife stand on the ground with a capsule
            // each; left switched on, the bake reads every one as a post and cuts a hole
            // around it. They are not scenery and they move.
            //
            // Exactly the three roots the build takes out, by name - NOT "every root that
            // contains an entity", which was the first cut of this and is too greedy: the
            // harvestables and the item drops are entities too and they sit under roots
            // that also carry the scenery, so that rule would have baked the village
            // without its buildings.
            var hidden = new System.Collections.Generic.List<GameObject>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (System.Array.IndexOf(MovableRootNames, root.name) < 0 || !root.activeSelf)
                    continue;
                root.SetActive(false);
                hidden.Add(root);
            }

            try
            {
                BakeWithDoorsOpen(surface);
            }
            finally
            {
                foreach (GameObject root in hidden)
                    root.SetActive(true);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log($"[{nameof(DemoSceneBuilder)}] Rebaked the navmesh in {ScenePath} " +
                      $"with {hidden.Count} entity root(s) taken out of it. " +
                      "The map server reads this from a NavMeshData asset beside the scene, so a " +
                      "server running from builds/ needs Build Map Server before it sees this.");
        }


        /// <summary>The name of the volume that keeps the navmesh out of the sea.</summary>
        public const string SeaCarveName = "NavmeshSeaCarve";

        /// <summary>
        /// How far above sea level the walkable ground stops, in metres.
        ///
        /// Not zero, because zero is the waterline itself and the point is that the
        /// animals stay out of the water rather than stand in the edge of it. The beach
        /// falls about a quarter of a metre every metre, so this holds them back roughly
        /// a stride from the surf - enough to read as keeping to the sand, little enough
        /// that the beach is still theirs to walk on.
        /// </summary>
        private const float NavmeshWaterline = 0.25f;

        /// <summary>Wider than the 260m terrain, so the carve runs past its edges.</summary>
        private const float SeaCarveWidth = 320f;

        /// <summary>
        /// Cuts the sea floor out of the navmesh, so that nothing which walks can walk
        /// into the water.
        ///
        /// **The island does not stop at the waterline.** The terrain carries on down to
        /// the seabed at <see cref="DemoIslandBuilder.SeabedDepth"/>, and a bake reads
        /// that the way it reads any other ground: a gentle sand slope, well within the
        /// agent slope limit, therefore walkable. So the navmesh has always run out under
        /// the sea to about 125m from the middle, and every monster on it treated the
        /// seabed as somewhere it could go - wandering out, chasing a swimming player
        /// out, and in the deer case bolting out when shot.
        ///
        /// A box marked "not walkable" over everything below the waterline is the whole
        /// fix, and it is better than the alternatives precisely because it changes
        /// nothing else. The spawn areas keep their positions and their radii; the water
        /// keeps its shape; no monster needs a new component or a rule about swimming.
        /// The sea simply stops being floor, and every behaviour that asks the navmesh
        /// where it may go gets the right answer for free:
        ///
        /// * wandering and chasing are `NavMeshAgent` paths, which cannot enter a hole;
        /// * <see cref="MultiplayerARPG.Demo.DemoFlee"/> already samples the navmesh and
        ///   swings its escape line round until it finds somewhere valid - its own notes
        ///   list the sea as a case it handles, and it failed only because the sea was
        ///   walkable, so the first sample succeeded and the deer ran into the water;
        /// * spawning re-grounds itself. `MonsterSpawnArea` calls `FindGroundedPosition`
        ///   on the entity *after* instantiating it, and the navmesh mover override of
        ///   that widens its search until it finds mesh. So the third-odd of deer ground
        ///   that lies under water goes on producing deer, and they now arrive on the
        ///   nearest sand instead of standing in the shallows.
        ///
        /// **The volume must not be on the Water layer.** `NavMeshSurface` skips any
        /// modifier whose layer is masked out, and this surface masks Water out
        /// deliberately (see the bake settings). A carve sitting on Water would be
        /// dropped silently - no warning, no error, just a bake that still has a seabed.
        ///
        /// Idempotent, and reused rather than replaced, so that a nudge in the inspector
        /// survives a rebake the way the rest of the authored scene does.
        /// </summary>
        private static void EnsureSeaCarve(GameObject surfaceOwner)
        {
            Transform existing = surfaceOwner.transform.Find(SeaCarveName);
            GameObject go = existing != null ? existing.gameObject : new GameObject(SeaCarveName);
            if (existing == null)
                go.transform.SetParent(surfaceOwner.transform, false);

            // Identity, so that `center` below is read straight off the world. The
            // surface multiplies the volume by its transform, so a scaled or rotated
            // parent would otherwise skew the box.
            go.transform.position = Vector3.zero;
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            go.layer = 0;

            NavMeshModifierVolume volume = go.GetComponent<NavMeshModifierVolume>();
            if (volume == null)
                volume = go.AddComponent<NavMeshModifierVolume>();

            // Down past the seabed rather than to it, so there is no chance of the box
            // ending inside the ground it is meant to be cutting.
            float floor = DemoIslandBuilder.SeabedDepth - 8f;
            float ceiling = DemoIslandBuilder.WaterLevel + NavmeshWaterline;
            volume.center = new Vector3(0f, (floor + ceiling) * 0.5f, 0f);
            volume.size = new Vector3(SeaCarveWidth, ceiling - floor, SeaCarveWidth);

            // 1 is "not walkable" - the one area value that cuts a hole rather than
            // costing more to cross. Looked up rather than written, in case the project
            // area list has been reordered, with the documented value as the fallback.
            int notWalkable = NavMesh.GetAreaFromName("Not Walkable");
            volume.area = notWalkable >= 0 ? notWalkable : 1;
        }

        /// <summary>
        /// Bakes the navmesh with the doors taken out of it.
        ///
        /// The doors are solid and they start closed, so baking with them in place walls
        /// every interior off into an island of its own: the room has navmesh on its
        /// floor, but no way of reaching it. A player driving with the keyboard never
        /// notices, because they open the door and walk in, but a click inside a house
        /// sends them as far as the doorstep and stops. The doors are put back
        /// afterwards, so the scene that is saved still has solid doors.
        /// </summary>
        private static void BakeWithDoorsOpen(NavMeshSurface surface)
        {
            var doors = new System.Collections.Generic.List<Collider>();
            foreach (MultiplayerARPG.Demo.DemoDoor door in
                Object.FindObjectsByType<MultiplayerARPG.Demo.DemoDoor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (door.pivot == null)
                    continue;
                foreach (Collider collider in door.pivot.GetComponentsInChildren<Collider>())
                {
                    if (!collider.enabled)
                        continue;
                    collider.enabled = false;
                    doors.Add(collider);
                }
            }

            surface.BuildNavMesh();
            PersistNavMesh(surface, ScenePath);

            foreach (Collider collider in doors)
                collider.enabled = true;
            Debug.Log($"[{nameof(DemoSceneBuilder)}] Baked the navmesh through {doors.Count} door(s).");
        }

        /// <summary>
        /// Writes the baked navmesh out as an asset, without which there is no navmesh at
        /// all once the scene is loaded anywhere else.
        ///
        /// `NavMeshSurface.BuildNavMesh()` is the **runtime** API: it builds the data into
        /// memory and hands it to the component, and that is all. The editor's own Bake
        /// button does a second thing - it saves the result as an asset beside the scene -
        /// and a script that only calls `BuildNavMesh` gets the first half. The scene then
        /// looks right in the editor, because the in-memory data is still attached, and
        /// has no navmesh anywhere else: saving the scene does not persist it, so the map
        /// server loads a surface whose data is null and every agent fails with `Failed to
        /// create agent because there is no valid NavMesh`.
        ///
        /// The tell is a surface whose `navMeshData` is **not null but has an empty asset
        /// path**. Null would have been easier to spot.
        /// </summary>
        public static void PersistNavMesh(NavMeshSurface surface, string scenePath)
        {
            if (surface.navMeshData == null)
            {
                Debug.LogError($"[{nameof(DemoSceneBuilder)}] The bake produced no data for " +
                               $"\"{surface.gameObject.name}\". Nothing will be able to walk.");
                return;
            }
            // Already an asset from a previous bake: the data object is reused, so writing
            // it again would throw. Saving is enough.
            if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(surface.navMeshData)))
            {
                EditorUtility.SetDirty(surface.navMeshData);
                AssetDatabase.SaveAssets();
                return;
            }

            string folder = scenePath.Substring(0, scenePath.Length - ".unity".Length);
            DemoItemBuilder.EnsureFolder(folder);
            string path = $"{folder}/NavMesh-{surface.gameObject.name}.asset";
            AssetDatabase.CreateAsset(surface.navMeshData, path);
            AssetDatabase.SaveAssets();
            Debug.Log($"[{nameof(DemoSceneBuilder)}] Wrote the baked navmesh to {path}.");
        }

        private static void BuildVillage(Scene scene)
        {
            GameObject root = Root(scene, "Village");
            Vector2 centre = DemoIslandBuilder.VillageCentre;
            root.transform.position = new Vector3(centre.x, DemoIslandBuilder.VillageHeight, centre.y);

            var houses = new Transform[HouseLayout.Length];
            for (int i = 0; i < HouseLayout.Length; ++i)
            {
                bool isBank = i == BankHouseIndex;
                var house = new GameObject(isBank ? "House_Bank" : $"House_{i + 1}");
                houses[i] = house.transform;
                house.transform.SetParent(root.transform, false);
                house.transform.localPosition = HouseLayout[i];
                house.transform.localRotation = Quaternion.Euler(0f, HouseYaw(HouseLayout[i]), 0f);
                // The bank is the largest house on the green and the only brick one, so
                // it reads as the building worth walking to.
                bool big = IsBigHouse(i);
                DemoVillageBuilder.BuildHouse(house.transform, big ? 3 : 2, big ? 3 : 2, isBank || i % 2 == 0);
                Furnish(house.transform, i, big);
                HangSign(house.transform, i, big);
                MountTorches(house.transform, big);
                MarkStatic(house);
            }

            BuildWatchtower(root.transform);

            var props = new GameObject("Props");
            props.transform.SetParent(root.transform, false);

            // A market on the green. The produce sits with the stall it is sold from,
            // squared to it and butted against its ends, so the stall reads as a stall
            // with stock and not as a stall with crates dropped near it.
            GameObject stall = GroundProp("Stall_Vegetables_Full", props.transform, StallLayout.x, StallLayout.z, StallYaw);
            Beside(stall, props.transform, new Vector3(-1.35f, 0f, 0f), "FarmCrate_Apple");
            Beside(stall, props.transform, new Vector3(2.55f, 0f, 0f), "FarmCrate_Leek");
            Beside(stall, props.transform, new Vector3(-1.35f, 0f, 0.85f), "Barrel_Apples");
            GroundProp("Stall_Potions", props.transform, 9.5f, 1.5f, 250f);

            // Two benches round the fire, across from each other, each turned to run
            // along the circle so whoever sits faces the flames. Three metres puts the
            // near edge clear of the tripod and the far one clear of the guard's round.
            foreach (float angle in new[] { 45f, 225f })
            {
                float radians = angle * Mathf.Deg2Rad;
                GroundProp("Bench", props.transform, Mathf.Cos(radians) * 3f, Mathf.Sin(radians) * 3f, -angle - 90f);
            }

            // Stores stand where they were delivered: casks against the alehouse wall
            // beside its door, and the smith's crate and quenching barrel between his
            // door and his bench. Each is flush to the wall and squared to it. They used
            // to stand in a loose group in the open by the west houses, at odd angles,
            // which read as things dropped rather than things put down.
            Outside(props.transform, houses[AlehouseIndex], -1.7f, "Barrel", 0f);
            // The second cask sits a hand further along than its width asks: it is turned a
            // little, and the audit boxes a turned barrel wider than a barrel is.
            Outside(props.transform, houses[AlehouseIndex], -2.55f, "Barrel_Dark", 25f);
            Outside(props.transform, houses[SmithHouseIndex], 1.5f, "Crate_Wooden", 0f);
            Outside(props.transform, houses[SmithHouseIndex], 2.35f, "Barrel", 0f);
            // The cage wagon is parked at the west edge of the green, by the lane out
            // between the bank and the house beyond it. It used to stand at (7, -3.5),
            // which was the middle of the green's southeast side - and once the alehouse
            // grew to a big house, that was squarely in front of its door.
            GroundProp("Wagon", props.transform, -9.5f, -1.5f, 100f);
            GameObject firepit = GroundProp("Firepit", props.transform, 0f, 0f, 0f);
            GroundProp("CampfireTripod", props.transform, 0f, 0f, 20f);
            // Lit with the torches at dusk and out with them after dawn: it is the
            // middle of the green, and the thing a player arriving at night walks toward.
            //
            // **DemoCraftStationBuilder puts this one on `Always`**, because it is also the
            // demo's cooking station and a cold fire pit that offers you a stew at noon
            // reads as a bug. The night schedule here is what the fire is when nothing is
            // cooked on it, and it is what a regenerate restores - which is one of the
            // reasons Build Craft Stations has to be run after one.
            Kindle(firepit, BrazierFlame, DemoFlameBuilder.CampfireFlamePath, MultiplayerARPG.Demo.DemoTorch.Schedule.Night);

            // The smith's yard, out in front of the smith's own house rather than off on
            // its own: it used to stand on the far side of the green, which put it inside
            // the bank's footprint.
            GroundProp("Anvil_Log", props.transform, -3f, -8.6f, 60f);
            GroundProp("Workbench", props.transform, -4.6f, -9.4f, 30f);

            RingTheGreen(props.transform);
            MarkStatic(props);
            StrewTheGreen(root.transform);
        }

        // ---- fire ------------------------------------------------------------

        /// <summary>
        /// The wall torch from the props pack. Its bracket is at its origin, and the
        /// head reaches out and up along its own +Z, so it is turned to face into
        /// whatever it is fixed to the outside of.
        /// </summary>
        private const string TorchModel = "Torch_Metal";

        /// <summary>
        /// How high a torch is fixed: the lowest point of its bracket, off the floor.
        /// This puts the flame a little over two metres up, above everyone's head and
        /// under every ceiling and eave.
        /// </summary>
        private const float TorchHeight = 1.45f;

        /// <summary>Where the flame sits on the torch: in the cup at the top of the head, measured off the model.</summary>
        private static readonly Vector3 TorchFlame = new Vector3(0f, 0.36f, 0.25f);

        /// <summary>
        /// Where a fire burns in the thing the pack calls a firepit, which is a raised
        /// iron brazier: down in the basket, so the flames rise past the rim rather than
        /// hovering above it.
        /// </summary>
        private static readonly Vector3 BrazierFlame = new Vector3(0f, 0.85f, 0f);

        /// <summary>Puts a flame on something, at a point in its own space.</summary>
        private static MultiplayerARPG.Demo.DemoTorch Kindle(GameObject holder, Vector3 at, string flame, MultiplayerARPG.Demo.DemoTorch.Schedule schedule)
        {
            if (holder == null)
                return null;
            return DemoFlameBuilder.Light(flame, holder.transform, at, schedule);
        }

        /// <summary>
        /// The torches inside a house, as furniture, so they are placed and audited the
        /// way everything else on a wall is.
        ///
        /// They go where no layout puts anything: on the door wall, clear of the leaf's
        /// sweep, and for the small houses - which have no room on the door wall for
        /// two - one on the back wall instead, above whatever stands against it. Yaw 0
        /// reaches the head into the room from the south wall, 180 from the north.
        ///
        /// The sweep is the constraint that sets the numbers. The leaf is 1.12 long on a
        /// hinge half a metre east of the door's middle, and a torch is fixed at the
        /// height the leaf's top passes through, so anything nearer the hinge than the
        /// leaf's length is struck when the door opens: at 1.5 the east torch was, by
        /// the audit's own measure, 0.88 of 1.12 in.
        /// </summary>
        private static Furniture[] TorchesFor(bool big)
        {
            if (big)
            {
                return new[]
                {
                    Against(TorchModel, Side.South, -2.0f, 0f, TorchHeight, "Torch_W"),
                    Against(TorchModel, Side.South, 2.0f, 0f, TorchHeight, "Torch_E"),
                };
            }
            return new[]
            {
                Against(TorchModel, Side.South, -0.5f, 0f, TorchHeight, "Torch_S"),
                Against(TorchModel, Side.North, 1.2f, 180f, TorchHeight, "Torch_N"),
            };
        }

        /// <summary>
        /// Fixes torches to the outside of a house, flanking its door.
        ///
        /// Every house faces the green, so lighting the door walls lights the green's
        /// edge all the way round. A big house takes one either side of its door,
        /// outside the sign's post and inside the corner quoin. A small house has no
        /// wall to spare on the door side of its door - the frame, the sign and the
        /// quoin between them use it all - so it takes one on the far side of the door
        /// and one round the corner on the west wall, whose front cell is the plain one.
        /// </summary>
        private static void MountTorches(Transform house, bool big)
        {
            var torches = new GameObject("Torches");
            torches.transform.SetParent(house, false);

            if (big)
            {
                Mount(torches.transform, house, Side.South, -2.2f);
                Mount(torches.transform, house, Side.South, 2.2f);
            }
            else
            {
                Mount(torches.transform, house, Side.South, -1.35f);
                Mount(torches.transform, house, Side.West, -1.0f);
            }
        }

        /// <summary>
        /// Fixes one torch to the outer face of a wall, measured the way the furniture
        /// is: the model is turned to reach away from the wall, then moved so its
        /// bracket touches the face and its lowest point is at torch height. No
        /// collision - it is above head height, and a bracket that stops a player
        /// walking along a wall is worse than one they can put their head through.
        /// </summary>
        private static void Mount(Transform parent, Transform house, Side wall, float along)
        {
            // Turned so the head reaches out from the wall: -Z from the south face, -X
            // from the west.
            float yaw = wall == Side.South ? 180f : 270f;
            GameObject torch = Prop(TorchModel, parent, Vector3.zero, yaw, false);
            if (torch == null)
                return;

            float face = OuterFace(house, wall);
            Bounds bounds = DemoVillageBuilder.LocalBounds(house, torch.transform);
            // The bracket goes a few millimetres into the wall so no daylight shows
            // between the two.
            const float embed = 0.005f;
            Vector3 move = wall == Side.South
                ? new Vector3(along - bounds.center.x, TorchHeight - bounds.min.y, face + embed - bounds.max.z)
                : new Vector3(face + embed - bounds.max.x, TorchHeight - bounds.min.y, along - bounds.center.z);
            torch.transform.localPosition += move;
            Kindle(torch, TorchFlame, DemoFlameBuilder.TorchFlamePath, MultiplayerARPG.Demo.DemoTorch.Schedule.Night);
        }

        /// <summary>
        /// The outer face of one of a house's walls, in the house's space. Measured off
        /// the wall modules, as the interior is, because a module is not flush with the
        /// grid line it stands on: it straddles it, most of its depth outward.
        /// </summary>
        private static float OuterFace(Transform house, Side wall)
        {
            bool low = wall == Side.South || wall == Side.West;
            float face = low ? float.MaxValue : float.MinValue;
            foreach (Transform child in house)
            {
                if (!child.name.StartsWith("Wall_"))
                    continue;
                Bounds local = DemoVillageBuilder.LocalBounds(house, child);
                // A run is long on one axis and thin on the other; the thin one is the
                // axis it walls off.
                bool wallsZ = local.size.x > local.size.z;
                switch (wall)
                {
                    case Side.South: if (wallsZ && local.center.z < 0f) face = Mathf.Min(face, local.min.z); break;
                    case Side.North: if (wallsZ && local.center.z > 0f) face = Mathf.Max(face, local.max.z); break;
                    case Side.West: if (!wallsZ && local.center.x < 0f) face = Mathf.Min(face, local.min.x); break;
                    case Side.East: if (!wallsZ && local.center.x > 0f) face = Mathf.Max(face, local.max.x); break;
                }
            }
            return face;
        }

        /// <summary>
        /// How far a house's outer wall face stands beyond its footprint line, in metres.
        ///
        /// The wall modules are 0.41 deep and hang outward off the line they are placed
        /// on. Measured on both a plastered and a brick house, the outer face lands at
        /// exactly -half - 0.314 on the local z of the south wall.
        /// </summary>
        private const float WallFace = 0.314f;

        /// <summary>
        /// Hangs a shop sign over the door of the houses that are a trade: the bank, the
        /// smith and the alehouse.
        ///
        /// The signs are modelled hanging from their own origin, which is the bracket
        /// they swing on, so the whole sign is below the point it is placed at. Stood on
        /// the ground - which is where the smith's sign used to be - it is buried and
        /// only the bracket shows.
        ///
        /// The model is built to PROJECT: the bracket's post is at its origin and the arm
        /// reaches away along local -X, with the board's faces on local +/-Z. So at yaw 0
        /// it lies flat on the wall, arm and all, and the board faces the same way the
        /// wall does - which is the one orientation a hanging sign is never built in,
        /// because nobody walking down the street can read it. A quarter turn to 270 swings
        /// the arm out over the lane and turns the board side-on, where it belongs. That
        /// also moves where the sign is fixed: the origin now wants to be ON the wall face
        /// rather than clear of it, with the whole 1.2m of arm projecting from there.
        ///
        /// Which model matters, too. The six sign FBXs are the same mesh with the same UV0,
        /// but each carries its own emblem in UV2 - an anvil, two tankards, crossed swords,
        /// crossed axes, potion bottles, a bunch of vegetables - so the trade is chosen by
        /// choosing the file. See the "Prop Sign (Emblem)" shader for how that is drawn.
        /// </summary>
        private static void HangSign(Transform house, int houseIndex, bool big)
        {
            string sign;
            switch (houseIndex)
            {
                // No emblem in the pack is a bank - all six are trades - so the bank's board
                // is repainted below with one drawn for it. Which of the six carries it does
                // not matter to the eye, but it matters to the arithmetic: the swap works by
                // mapping this model's UV2 cell onto a texture of its own, so it has to be
                // the model BankEmblemCell was measured from.
                case BankHouseIndex: sign = "Sign_Blacksmith"; break;
                case 1: sign = "Sign_Blacksmith"; break;   // the anvil and hammer
                case 2: sign = "Sign_Pub"; break;          // two tankards
                default: return;
            }

            // Beside the doorway on the south wall, hung high enough to walk under and
            // low enough to stay under the eaves. Which side depends on where the door
            // is: it sits in the middle of a three-cell wall but off to one side of a
            // two-cell one, so a fixed offset hangs the sign over the door of the small
            // houses and half through its frame.
            int cells = big ? 3 : 2;
            float half = cells * DemoVillageBuilder.Cell * 0.5f;
            float door = -half + DemoVillageBuilder.Cell * (cells / 2 + 0.5f);
            float across = door > 0f ? door - 1.7f : door + 1.7f;
            // A centimetre into the wall, so no hairline of daylight shows behind the post.
            //
            // Higher than it hung flat, because it no longer hangs over nothing: projecting,
            // the board is out above ground people walk on, and its collider with it. At the
            // old 2.4 the board's underside sat at 1.80m - head height, and low enough for
            // the navmesh bake to see it. 2.7 puts the underside at 2.10m, clear of a 2m
            // agent, and still leaves 0.84m under the eaves.
            GameObject placed = Prop(sign, house, new Vector3(across, 2.7f, -half - WallFace + 0.01f), 270f);
            if (placed == null)
                return;
            placed.name = sign;
            if (houseIndex != BankHouseIndex)
                return;
            // Built on demand rather than as a pipeline step of its own: it is one material
            // and it would be an eighth menu item nobody would remember to run in order.
            if (AssetDatabase.LoadAssetAtPath<Material>(BankSignMaterial) == null)
                BuildBankSign();
            RepaintBoard(placed, BankSignMaterial);
        }

        /// <summary>Where the bank's own board material lives. Built by <see cref="BuildBankSign"/>.</summary>
        private const string BankSignMaterial = "Assets/OpenMMORPG/Demo/Materials/T_WoodenSign_Bank.mat";

        /// <summary>
        /// The slice of UV2 that Sign_Blacksmith's front panel occupies, as (u, v, width,
        /// height). Measured off the mesh, in the emblem sheet's coordinates.
        ///
        /// Every sign's panel takes one cell out of the pack's emblem block this way, which
        /// is what makes a new emblem cheap: point the material at a texture of its own and
        /// scale this cell up to cover it, and the panel reads the new art in full. The cell
        /// is 460 x 318 texels of a 2048 sheet, so the art is drawn to that 1.448 aspect.
        /// </summary>
        private static readonly Rect BankEmblemCell = new Rect(0.00032f, 0.00658f, 0.22475f, 0.15519f);

        /// <summary>
        /// Swaps a sign's board for another material, leaving the frame, arm and chains be.
        ///
        /// The board is its own material slot, so only that slot moves. It is found by the
        /// material's name rather than by slot index: the index is the FBX's own submesh
        /// order, which is not ours to rely on.
        /// </summary>
        private static void RepaintBoard(GameObject sign, string materialPath)
        {
            var board = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (board == null)
            {
                Debug.LogWarning($"[{nameof(DemoSceneBuilder)}] \"{materialPath}\" is missing, so " +
                                 $"{sign.name} keeps the emblem its model came with. Run " +
                                 "\"Quaternius/Build Props Materials\".");
                return;
            }
            foreach (Renderer renderer in sign.GetComponentsInChildren<Renderer>())
            {
                Material[] slots = renderer.sharedMaterials;
                bool any = false;
                for (int i = 0; i < slots.Length; ++i)
                {
                    if (slots[i] == null || slots[i].name != "T_WoodenSign")
                        continue;
                    slots[i] = board;
                    any = true;
                }
                if (any)
                    renderer.sharedMaterials = slots;
            }
        }

        /// <summary>
        /// Builds the bank's board material: the sign shader, but reading a coins emblem
        /// drawn for the demo instead of the pack's block of trade symbols.
        ///
        /// None of the pack's six emblems is a bank - they are an anvil, tankards, swords,
        /// axes, bottles and vegetables - so this one is ours: five struck coins, the same
        /// diamond-faced coins the vault is full of.
        ///
        /// The trick is the tiling. The panel's UV2 does not span 0..1; it spans one small
        /// cell of the pack's sheet. Scaling that cell up to the whole 0..1 range makes the
        /// same untouched mesh read a texture of its own, so no model, mesh or UV had to be
        /// edited to give the bank an emblem - and nothing in the pack was modified either.
        /// </summary>
        [MenuItem("Open MMORPG/Demo/Build Bank Sign")]
        public static void BuildBankSign()
        {
            const string coinsPath = "Assets/OpenMMORPG/Demo/Textures/T_Emblem_Coins.png";
            // Clamped, because a stencil that wraps paints a sliver of the next coin along
            // the board's edge, and uncompressed, because DXT's 4x4 blocks fray a hard black
            // and white edge into grey - which is the one thing this shader's cutoff sees.
            var coinsImporter = AssetImporter.GetAtPath(coinsPath) as TextureImporter;
            if (coinsImporter != null &&
                (coinsImporter.wrapMode != TextureWrapMode.Clamp ||
                 coinsImporter.textureCompression != TextureImporterCompression.Uncompressed))
            {
                coinsImporter.wrapMode = TextureWrapMode.Clamp;
                coinsImporter.textureCompression = TextureImporterCompression.Uncompressed;
                coinsImporter.SaveAndReimport();
            }

            var source = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Plugins/Quaternius/Props/Materials/T_WoodenSign.mat");
            var coins = AssetDatabase.LoadAssetAtPath<Texture2D>(coinsPath);
            if (source == null || coins == null)
            {
                Debug.LogError($"[{nameof(DemoSceneBuilder)}] Need both T_WoodenSign and " +
                               "T_Emblem_Coins to build the bank's sign.");
                return;
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(BankSignMaterial);
            if (material == null)
            {
                material = new Material(source);
                AssetDatabase.CreateAsset(material, BankSignMaterial);
            }
            material.shader = source.shader;
            material.CopyPropertiesFromMaterial(source);
            material.SetTexture("_EmblemMap", coins);
            material.SetTextureScale("_EmblemMap", new Vector2(1f / BankEmblemCell.width, 1f / BankEmblemCell.height));
            material.SetTextureOffset("_EmblemMap", new Vector2(
                -BankEmblemCell.x / BankEmblemCell.width, -BankEmblemCell.y / BankEmblemCell.height));
            // The whole texture is emblem now, so the shader's out-of-block test has nothing
            // left to reject. It still runs - it is what keeps the board from wrapping the
            // art across its edges.
            material.SetVector("_EmblemRect", new Vector4(0f, 0f, 1f, 1f));
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            Debug.Log($"[{nameof(DemoSceneBuilder)}] Built the bank's coins sign.");
        }

        /// <summary>
        /// Scatters loose stones over the village's bare earth.
        ///
        /// The green is trodden earth with the grass kept off it, which leaves a wide
        /// flat patch of one colour in the middle of the village. Stones trodden into it
        /// break that up the way they do on any worn path — it is the detail the pack's
        /// own screenshots use to keep bare ground from reading as a texture. They are
        /// the pebbles already scattered on the shore, so this costs nothing new, and
        /// they get no collision because nobody should trip on a stone.
        /// </summary>
        private static void StrewTheGreen(Transform root)
        {
            string[] stones = { "Pebble_Round_1", "Pebble_Round_3", "Pebble_Square_2", "Pebble_Square_5", "Pebble_Square_6" };
            var ground = new GameObject("Ground");
            ground.transform.SetParent(root, false);

            Random.State previous = Random.state;
            Random.InitState(GreenSeed);
            int placed = 0;
            for (int attempt = 0; attempt < 900 && placed < 40; ++attempt)
            {
                float angle = Random.Range(0f, Mathf.PI * 2f);
                float distance = Mathf.Sqrt(Random.value) * 15f;
                var spot = new Vector2(Mathf.Cos(angle) * distance, Mathf.Sin(angle) * distance);
                // Only on the earth itself, and not underfoot in a doorway or a stall.
                Vector2 world = DemoIslandBuilder.VillageCentre + spot;
                if (DemoIslandBuilder.SettlementWeight(world.x, world.y) < 0.75f)
                    continue;
                if (InsideBuilding(world.x, world.y))
                    continue;

                GameObject stone = InstantiateNature(stones[Random.Range(0, stones.Length)], ground.transform);
                if (stone == null)
                    break;
                stone.transform.localScale = Vector3.one * Random.Range(0.5f, 1.1f);
                stone.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                Bounds bounds = WorldExtent(stone.transform);
                float height = DemoIslandBuilder.HeightAt(world.x, world.y);
                stone.transform.position = new Vector3(world.x, height, world.y)
                    + new Vector3(0f, -bounds.size.y * Random.Range(0.25f, 0.55f), 0f);
                ++placed;
            }
            Random.state = previous;
            MarkStatic(ground);
        }

        /// <summary>
        /// Fences and banners round the edge of the green.
        ///
        /// Both are put in the gaps between the houses rather than on a circle of their
        /// own, which is what a village boundary would actually look like — and it also
        /// keeps them off the buildings. The radius matters: the green is flat out to
        /// about sixteen metres and then climbs away steeply, so a ring any wider than
        /// that is driven into the hillside. The old one sat at nineteen and was buried
        /// up to its top rail on the high side.
        /// </summary>
        private static void RingTheGreen(Transform parent)
        {
            var angles = new System.Collections.Generic.List<float>();
            foreach (Vector3 house in HouseLayout)
            {
                float angle = Mathf.Atan2(house.z, house.x) * Mathf.Rad2Deg;
                angles.Add(angle < 0f ? angle + 360f : angle);
            }
            angles.Sort();

            float towerBearing = Mathf.Atan2(WatchtowerLayout.z, WatchtowerLayout.x) * Mathf.Rad2Deg;
            for (int i = 0; i < angles.Count; ++i)
            {
                float from = angles[i];
                float to = angles[(i + 1) % angles.Count] + (i == angles.Count - 1 ? 360f : 0f);
                float gap = (from + to) * 0.5f;
                // The watchtower stands in its gap and closes it better than a fence would.
                if (Mathf.Abs(Mathf.DeltaAngle(gap, towerBearing)) < 30f)
                    continue;
                float radians = gap * Mathf.Deg2Rad;
                var direction = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));

                // A fence panel is long on its own X, so to lie across the gap rather
                // than point out of it the panel is turned a quarter past the radius.
                // Three panels butted end to end, because a single one adrift in a
                // fourteen-metre gap reads as a leftover rather than as a fence.
                var tangent = new Vector2(-direction.y, direction.x);
                for (int panel = -1; panel <= 1; ++panel)
                {
                    Vector2 spot = direction * 16f + tangent * (panel * 2.04f);
                    GroundProp("Prop_WoodenFence_Single", parent, spot.x, spot.y, -(gap + 90f), true);
                }
                if (i % 2 == 0)
                {
                    // Standing banners, not the cloth ones: those hang from their origin
                    // to be fixed on a wall, so stood on the ground they are underground.
                    GroundProp(i % 4 == 0 ? "Banner_Vertical_1" : "Banner_Vertical_2", parent,
                        direction.x * 12.5f, direction.y * 12.5f, -(gap + 90f));
                }
            }
        }

        // ---- watchtower ---------------------------------------------------------

        /// <summary>The wall module from the village pack: how far its outer face stands proud of the grid line it is placed on.</summary>
        private const float WallProud = 0.31f;

        /// <summary>The wall module's actual height; a storey is placed every <see cref="DemoVillageBuilder.WallHeight"/>, so the top strip overlaps the next.</summary>
        private const float WallModuleHeight = 3.12f;

        /// <summary>Headroom under the roof on the lookout deck, from the deck to the eave.</summary>
        private const float LookoutHeadroom = 2.6f;

        /// <summary>The ladder model from the props pack: its height and half its thickness.</summary>
        private const float LadderHeight = 2.92f;
        private const float LadderHalfDepth = 0.05f;

        /// <summary>
        /// A watchtower on the edge of the green, with a ladder up the outside to an open
        /// deck under a roof. It is there to show that climbing works: the kit has a ladder
        /// system, and nothing else in the demo uses it.
        ///
        /// It is built from the same modules as the houses - two storeys of stone wall on
        /// a two-cell square, quoined at the corners - with the deck on top of the walls,
        /// railed on three sides, and the roof of a small house carried on posts above it.
        /// Two ladder sections stand end to end up the face that looks onto the green,
        /// stretched a little so the top one reaches above the deck for a handhold.
        ///
        /// The climbing itself is the kit's <see cref="Ladder"/>: a line from a bottom
        /// anchor to a top one that the character is held against, an exit point at each
        /// end they are moved to when they leave, and a trigger at each end that lets
        /// them on. Walking into a trigger toward the ladder gets on; climbing past either
        /// end gets off. The kit's <c>Forward</c> for a ladder is world +Z turned by
        /// <c>yAngleOffsets</c> - it ignores the ladder's own rotation - so the offset is
        /// the tower's yaw plus a half turn, which puts the climber on the outside of the
        /// wall facing it.
        /// </summary>
        private static void BuildWatchtower(Transform parent)
        {
            var tower = new GameObject("Watchtower");
            tower.transform.SetParent(parent, false);
            tower.transform.localPosition = WatchtowerLayout;
            float yaw = HouseYaw(WatchtowerLayout);
            tower.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            Transform t = tower.transform;

            float half = WatchtowerCells * DemoVillageBuilder.Cell * 0.5f;
            float deck = WatchtowerStoreys * DemoVillageBuilder.WallHeight;
            // The deck boards lie on the last storey's wall tops, which stand a hand's
            // width above the boards' surface as a kerb round the edge.
            float kerb = (WatchtowerStoreys - 1) * DemoVillageBuilder.WallHeight + WallModuleHeight;
            float deckSurface = WatchtowerDeckHeight;

            // Walls. One arrow slit per face on the top storey, on alternate cells so no
            // two meet at a corner; the ground storey is blind, as a tower's would be.
            for (int storey = 0; storey < WatchtowerStoreys; ++storey)
            {
                float y = storey * DemoVillageBuilder.WallHeight;
                bool top = storey == WatchtowerStoreys - 1;
                for (int i = 0; i < WatchtowerCells; ++i)
                {
                    float p = -half + DemoVillageBuilder.Cell * (i + 0.5f);
                    TowerWall(top && i == 0, t, new Vector3(p, y, -half), 180f);
                    TowerWall(top && i == 0, t, new Vector3(p, y, half), 0f);
                    TowerWall(top && i == 1, t, new Vector3(half, y, p), 90f);
                    TowerWall(top && i == 1, t, new Vector3(-half, y, p), 270f);
                }
                // Same quoin, same turns, as DemoVillageBuilder.BuildHouse.
                const string corner = "Corner_Exterior_Brick";
                DemoVillageBuilder.Place(corner, t, new Vector3(-half, y, -half), 0f);
                DemoVillageBuilder.Place(corner, t, new Vector3(-half, y, half), 90f);
                DemoVillageBuilder.Place(corner, t, new Vector3(half, y, half), 180f);
                DemoVillageBuilder.Place(corner, t, new Vector3(half, y, -half), 270f);
            }

            // The deck.
            for (int x = 0; x < WatchtowerCells; ++x)
            {
                for (int z = 0; z < WatchtowerCells; ++z)
                {
                    DemoVillageBuilder.Place("Floor_WoodDark", t,
                        new Vector3(-half + DemoVillageBuilder.Cell * (x + 0.5f), deck, -half + DemoVillageBuilder.Cell * (z + 0.5f)), 0f);
                }
            }

            // Railings stand on the wall tops. The rail is modelled a cell out along the
            // module's -Z, so each is placed a cell in from the edge it guards and turned
            // to put that side outward. The ladder side has a metre's gap in the middle
            // for the ladder, each half's rail shortened to leave it.
            const string rail = "Balcony_Cross_Straight";
            for (int i = 0; i < WatchtowerCells; ++i)
            {
                float p = -half + DemoVillageBuilder.Cell * (i + 0.5f);
                DemoVillageBuilder.Place(rail, t, new Vector3(p, kerb, half - 1f), 180f);
                DemoVillageBuilder.Place(rail, t, new Vector3(half - 1f, kerb, p), 270f);
                DemoVillageBuilder.Place(rail, t, new Vector3(-half + 1f, kerb, p), 90f);
            }
            const float gap = 1f;
            float railLength = half - gap * 0.5f;
            foreach (float side in new[] { -1f, 1f })
            {
                GameObject piece = DemoVillageBuilder.Place(rail, t, new Vector3(side * (gap * 0.5f + railLength * 0.5f), kerb, -half + 1f), 0f);
                if (piece != null)
                    piece.transform.localScale = new Vector3(railLength / DemoVillageBuilder.Cell, 1f, 1f);
            }

            // Posts and roof. The post module is a storey tall and stood on the boards a
            // little in from each corner; it is shortened to the headroom and run a
            // hand's width up into the roof so the join is inside it. The roof is the
            // small house's, whose eaves hang half a metre below its origin.
            float postHeight = LookoutHeadroom + 0.52f + 0.1f;
            float inset = half - 0.25f;
            foreach (float sx in new[] { -1f, 1f })
            {
                foreach (float sz in new[] { -1f, 1f })
                {
                    GameObject post = DemoVillageBuilder.Place("Corner_Exterior_Wood", t, new Vector3(sx * inset, deckSurface, sz * inset), 0f);
                    if (post != null)
                        post.transform.localScale = new Vector3(1f, postHeight / DemoVillageBuilder.WallHeight, 1f);
                }
            }
            DemoVillageBuilder.Place("Roof_RoundTiles_4x4", t, new Vector3(0f, deckSurface + LookoutHeadroom + 0.52f, 0f), 0f);

            // The ladder, up the face that looks onto the green. Two sections end to end,
            // stretched so the upper one stands proud of the deck by a rung or two.
            float ladderZ = -(half + WallProud + LadderHalfDepth + 0.02f);
            float ladderTop = kerb + 0.3f;
            float stretch = ladderTop / (2f * LadderHeight);
            for (int section = 0; section < 2; ++section)
            {
                GameObject ladder = Prop("Ladder", t, new Vector3(0f, section * LadderHeight * stretch, ladderZ), 0f);
                if (ladder != null)
                    ladder.transform.localScale = new Vector3(1f, stretch, 1f);
            }

            // The climb. The line the character is held to runs just in front of the
            // rungs, so that a capsule pressed against it clears the ladder's own
            // collision instead of being pushed out of it every frame. Its bottom is a
            // little off the ground and its top a little above the kerb: the character
            // has to be carried past an end to leave, and the ground would stop them
            // going below a bottom set on it, as the kerb would stop them stepping onto
            // the deck from a top set level with it.
            var rig = new GameObject("LadderRig");
            rig.transform.SetParent(t, false);
            var climb = rig.AddComponent<Ladder>();
            float lineZ = ladderZ - LadderHalfDepth - 0.03f;
            climb.bottomTransform = Anchor(rig.transform, "Bottom", new Vector3(0f, 0.12f, lineZ));
            climb.topTransform = Anchor(rig.transform, "Top", new Vector3(0f, kerb + 0.1f, lineZ));
            // Getting off at the bottom is a step back onto the ground where they hang,
            // which is where the step-off clip plays; at the top it is a pull up onto the
            // boards, well in from the edge.
            climb.bottomExitTransform = Anchor(rig.transform, "BottomExit", new Vector3(0f, 0f, lineZ - 0.3f));
            climb.topExitTransform = Anchor(rig.transform, "TopExit", new Vector3(0f, deckSurface, -half + 0.8f));
            climb.yAngleOffsets = yaw + 180f;
            rig.AddComponent<MultiplayerARPG.Demo.DemoLadderExit>();

            // Where you can get on: a body's worth of ground at the foot, and the strip of
            // deck inside the gap in the rail. Neither reaches the ladder itself, so a
            // climber is not standing in one.
            Entrance(rig.transform, climb, LadderEntranceType.Bottom,
                new Vector3(0f, 0.9f, lineZ - 0.7f), new Vector3(1.6f, 1.8f, 1.4f));
            Entrance(rig.transform, climb, LadderEntranceType.Top,
                new Vector3(0f, deckSurface + 0.9f, -half + 0.7f), new Vector3(1.2f, 1.8f, 1.4f));

            MarkStatic(tower);
        }

        /// <summary>A tower wall: blind, or cut with an arrow slit and glazed.</summary>
        private static void TowerWall(bool slit, Transform parent, Vector3 position, float yaw)
        {
            DemoVillageBuilder.Place(slit ? "Wall_UnevenBrick_Window_Thin_Round" : "Wall_UnevenBrick_Straight", parent, position, yaw);
            if (slit)
                DemoVillageBuilder.Place("Window_Thin_Round1", parent, position, yaw);
        }

        private static Transform Anchor(Transform parent, string name, Vector3 localPosition)
        {
            var anchor = new GameObject(name);
            anchor.transform.SetParent(parent, false);
            anchor.transform.localPosition = localPosition;
            return anchor.transform;
        }

        private static void Entrance(Transform parent, Ladder ladder, LadderEntranceType type, Vector3 centre, Vector3 size)
        {
            var entrance = new GameObject($"{type}Entrance");
            entrance.transform.SetParent(parent, false);
            entrance.transform.localPosition = centre;
            var box = entrance.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = size;
            var component = entrance.AddComponent<LadderEntrance>();
            component.ladder = ladder;
            component.type = type;
        }

        /// <summary>Which wall a piece of furniture stands against, if any.</summary>
        private enum Side { Free, North, South, East, West }

        /// <summary>
        /// One piece of furniture.
        ///
        /// A piece says which wall it belongs against and where along that wall it sits;
        /// how far out from the wall it ends up is measured from the piece at build
        /// time, not authored. Writing both coordinates by hand is what put the last set
        /// of furniture three quarters of a metre into the room: the models have wildly
        /// different pivots - some centred, some at a corner, some below the floor - so
        /// the same coordinate means a different thing for every one of them, and the
        /// figure that looks right for a small house is wrong for a big one.
        /// </summary>
        private struct Furniture
        {
            public string Prefab;
            public string Id;
            public Side Wall;
            public float Along;
            public float Depth;
            public float Yaw;
            public float Height;
            public string On;
        }

        /// <summary>A piece standing against a wall, optionally hung at a height on it.</summary>
        private static Furniture Against(string prefab, Side wall, float along, float yaw, float height = float.NaN, string id = null)
        {
            return new Furniture { Prefab = prefab, Id = id ?? prefab, Wall = wall, Along = along, Yaw = yaw, Height = height };
        }

        /// <summary>A piece standing in the room, positioned by the middle of its footprint.</summary>
        private static Furniture Free(string prefab, float x, float z, float yaw, string id = null)
        {
            return new Furniture { Prefab = prefab, Id = id ?? prefab, Wall = Side.Free, Along = x, Depth = z, Yaw = yaw, Height = float.NaN };
        }

        /// <summary>A piece resting on top of another, which must already have been placed.</summary>
        private static Furniture On(string prefab, string support, float x, float z, float yaw, string id = null)
        {
            return new Furniture { Prefab = prefab, Id = id ?? prefab, Wall = Side.Free, Along = x, Depth = z, Yaw = yaw, Height = float.NaN, On = support };
        }

        /// <summary>How much air to leave between a piece and the wall behind it.</summary>
        private const float WallClearance = 0.02f;

        /// <summary>
        /// What is inside each house.
        ///
        /// Every house is furnished, not just the bank - an empty shell reads as
        /// unfinished the moment a player opens a door, and the demo invites them to.
        /// Each is given a purpose so the village looks inhabited by people who do
        /// different things, rather than six copies of the same room.
        ///
        /// Two things constrain every layout. The door is in the middle of the south
        /// wall and opens inward through a quarter circle a leaf's length deep, so the
        /// floor inside that arc has to stay clear or the door swings through the
        /// furniture. And the banker stands at the bank's own origin, so that spot is
        /// his and nothing else's.
        /// </summary>
        private static Furniture[] InteriorFor(int houseIndex)
        {
            switch (houseIndex)
            {
                // The counter is set back from the door rather than just inside it. The
                // navmesh is eroded by the agent's half-metre radius, so furniture casts
                // a shadow that wide around itself: a counter close to the doorway closes
                // the lane through it, and the whole room becomes navmesh nobody can
                // reach. Two metres of clear floor inside the door is what it takes.
                case BankHouseIndex: // The bank: a counter to be served at, the vault behind.
                    return new[]
                    {
                        Against("Chest_Legendary", Side.North, -1.5f, 0f),
                        Against("Chest_Wood", Side.North, 0.15f, 0f, id: "Chest_A"),
                        Against("Chest_Wood", Side.North, 1.7f, 0f, id: "Chest_B"),
                        Against("Bookcase_1", Side.West, -0.3f, 90f),
                        Against("Barrel_Holder", Side.East, -1.4f, -90f),
                        Free("Desk", 0f, -0.65f, 0f),
                        Free("Chair_1", -1.6f, -1.5f, 110f),
                        Free("Rug_Round", 0f, 0.55f, 0f),
                        On("Coin_Pile", "Desk", -0.45f, -0.65f, 0f),
                        On("Coin_Pile_2", "Desk", 0.45f, -0.8f, 40f),
                        On("Book_Stack_2", "Desk", 0.2f, -0.45f, 15f),
                        On("CandleStick", "Desk", -0.72f, -0.45f, 0f),
                        // The two pieces on T_Gold. They go here because this is the one
                        // room in the village whose whole point is treasure, and the one
                        // with a bright enough lamp to show polished metal off - gold is
                        // almost entirely reflected light, so in a dim room it reads grey.
                        // The chalice takes the banker's end of the counter, opposite the
                        // candle; the key lies across the customer's edge between the two
                        // coin piles, turned so its length runs across the counter rather
                        // than into it, because end-on it is only 58mm of silhouette.
                        On("Chalice_Golden", "Desk", 0.72f, -0.45f, -20f),
                        On("Key_Gold", "Desk", -0.08f, -0.87f, 72f),
                    };

                case 1: // The smith's. Forge gear across the back, stock along the sides.
                    return new[]
                    {
                        Free("Anvil_Log", -0.6f, 1.9f, 20f),
                        Free("Whetstone", 0.9f, 0.55f, -25f),
                        Against("Bellows", Side.East, 1.9f, -90f),
                        Against("Workbench", Side.West, 0.4f, 90f),
                        On("Workbench_Vice", "Workbench", -2.4f, 0.9f, 90f),
                        Against("Hammer_Rack", Side.West, -1.4f, 90f, height: 1.15f),
                        Against("WeaponStand", Side.East, -1.6f, -90f),
                        Against("Crate_Metal", Side.East, 0.3f, 8f),
                        Free("Bucket_Metal", -1.5f, -0.1f, 0f),
                    };

                // A big house now. The long table with its two benches is 2.9 by 2.8
                // metres, and in the small house it used to be it filled the floor to
                // within a hand of every wall. Here it takes the west half of the room,
                // leaving a lane from the door up the east side to the barrels, and the
                // east wall gets the things an alehouse keeps: a rack of casks, a shelf
                // of bottles, and a chair by them for whoever is minding it.
                case AlehouseIndex:
                    return new[]
                    {
                        Free("Table_Large", -1.5f, 0.4f, 90f),
                        Free("Bench", -2.45f, 0.4f, 90f, id: "Bench_W"),
                        Free("Bench", -0.55f, 0.4f, -90f, id: "Bench_E"),
                        // At the head of the table, and clear of the leaf's sweep, which
                        // reaches 1.12 from a hinge half a metre east of the door.
                        Free("Stool", -1.5f, -1.5f, 0f),
                        // Down the wall from the corner: the rack is 1.36 long, and at 1.6
                        // its far end ran into the dark barrel standing in the corner.
                        Against("Barrel_Holder", Side.East, 1.3f, -90f),
                        Against("Barrel", Side.North, 1.6f, 0f),
                        Against("Barrel_Dark", Side.North, 2.4f, 0f),
                        Against("Shelf_Small_Bottles", Side.East, -0.3f, -90f, height: 1.4f),
                        Free("Chair_3", 1.6f, 0.6f, -100f),
                        // Stores in the corners by the door, where nothing walks.
                        Free("Crate_Wooden", -2.3f, -2.3f, 10f),
                        Free("Bag", -1.3f, -2.4f, 30f),
                        Free("Pot_1", 2.5f, -2.4f, 0f),
                        On("Mug", "Table_Large", -1.75f, 0.95f, 0f, id: "Mug_A"),
                        On("Mug", "Table_Large", -1.25f, -0.2f, 40f, id: "Mug_B"),
                        On("Mug", "Table_Large", -1.3f, 1.4f, 200f, id: "Mug_C"),
                        On("Chalice", "Table_Large", -1.55f, 0.35f, 0f),
                        On("Bottle_1", "Table_Large", -1.2f, 0.6f, 0f),
                        On("Table_Plate", "Table_Large", -1.7f, -0.4f, 0f),
                    };

                case 3: // Someone's home.
                    return new[]
                    {
                        // Beds go head to the back wall, feet toward the door. Every bed in
                        // the pack has its pillow at local z = -0.75, so at yaw 0 an
                        // Against(North) piece backs its FOOT onto the wall and lies the
                        // sleeper looking out of the house. The half turn is the whole fix -
                        // Against re-measures after the rotation, so they stay flush.
                        Against("Bed_Twin1", Side.North, -0.85f, 180f),
                        // The bedside table follows the pillow to the back wall, and has to
                        // change sides to do it: the bed is 1.88 wide at x -0.85 in a room
                        // only 3.82 across, so it covers the whole west side and the only
                        // floor left beside the head is east of it. The pot was standing
                        // there, so it moves to the far corner by the door - clear of the
                        // leaf's quarter-circle sweep, which is centred on the hinge at
                        // (1.00, -2.00) and a leaf's length deep.
                        Free("Nightstand_Drawer", 0.5f, 1.45f, -90f),
                        On("Candle_1", "Nightstand_Drawer", 0.5f, 1.45f, 0f),
                        Against("Cabinet", Side.East, 0.9f, -90f),
                        Free("Chair_2", 0.6f, 0.2f, 250f),
                        Free("Rug_Round", 0.3f, -0.6f, 0f),
                        Free("Pot_2", -1.5f, -1.4f, 0f),
                    };

                case 4: // A crowded household with children: two beds head to the back wall.
                    return new[]
                    {
                        Against("Bed_Bunk", Side.North, -1.6f, 180f),
                        Against("Bed_Twin2", Side.North, 1.6f, 180f),
                        Against("Dresser_1", Side.West, -1.3f, 90f),
                        Against("Bookcase_2", Side.East, -1.3f, -90f),
                        On("Book_Stack_1", "Dresser_1", -2.68f, -1.3f, 20f),
                        Free("Stool", 0f, -0.9f, 0f),
                        // Rug_Round, not Rug_1: the latter is a 1.47 x 0.36 runner whose
                        // UVs land on a blank part of the cloth sheet and render white.
                        Free("Rug_Round", 0f, -0.3f, 0f),
                    };

                default: // The store room: everything the village keeps dry.
                    return new[]
                    {
                        Against("Crate_Wooden", Side.West, 1.1f, 8f, id: "Crate_Low"),
                        On("Crate_Wooden", "Crate_Low", -1.30f, 1.1f, -20f, id: "Crate_High"),
                        Against("FarmCrate_Apple", Side.North, 0.2f, -15f),
                        Against("FarmCrate_Radish", Side.North, 1.3f, 25f),
                        Against("Barrel_Apples", Side.West, -0.6f, 0f),
                        Free("Bag", -0.55f, -0.75f, 30f),
                        Free("Bag_2", 0.15f, 0.45f, -40f),
                        Against("Shelf_Small_Bottles", Side.East, 0.6f, -90f, height: 1.45f),
                    };
            }
        }

        /// <summary>
        /// Furnishes a house and lights it, so its windows glow from outside after dusk
        /// and the room is readable through the doorway.
        ///
        /// Each piece is placed by measuring it: it is instantiated, turned to face the
        /// way it should, and only then moved, so that its base rests on the floor and
        /// its back touches the wall whatever its model's pivot happens to be. Pieces
        /// that stand on other pieces are measured onto the top of whatever they stand
        /// on, which is why <see cref="InteriorFor"/> lists a support before its load.
        /// </summary>
        private static void Furnish(Transform house, int houseIndex, bool big)
        {
            var interior = new GameObject("Interior");
            interior.transform.SetParent(house, false);

            Bounds room = DemoVillageBuilder.InteriorBounds(house);
            var placed = new System.Collections.Generic.Dictionary<string, Bounds>();

            // The torches are furniture too: placed against their wall by measurement,
            // and checked by the interior audit like anything else hung there.
            var pieces = new System.Collections.Generic.List<Furniture>(InteriorFor(houseIndex));
            pieces.AddRange(TorchesFor(big));
            var torches = new System.Collections.Generic.List<GameObject>();

            foreach (Furniture piece in pieces)
            {
                bool torch = piece.Prefab == TorchModel;
                GameObject instance = Prop(piece.Prefab, interior.transform, Vector3.zero, piece.Yaw, !torch);
                if (instance == null)
                    continue;
                instance.name = piece.Id;
                if (torch)
                    torches.Add(instance);

                Bounds bounds = DemoVillageBuilder.LocalBounds(interior.transform, instance.transform);
                // Along runs with the wall, so which axis it means depends on the wall:
                // across the room for the north and south runs, up it for the east and
                // west ones. The other axis is the measured one.
                Vector3 move;
                switch (piece.Wall)
                {
                    case Side.North:
                        move = new Vector3(piece.Along - bounds.center.x, 0f, room.max.z - WallClearance - bounds.max.z);
                        break;
                    case Side.South:
                        move = new Vector3(piece.Along - bounds.center.x, 0f, room.min.z + WallClearance - bounds.min.z);
                        break;
                    case Side.East:
                        move = new Vector3(room.max.x - WallClearance - bounds.max.x, 0f, piece.Along - bounds.center.z);
                        break;
                    case Side.West:
                        move = new Vector3(room.min.x + WallClearance - bounds.min.x, 0f, piece.Along - bounds.center.z);
                        break;
                    default:
                        move = new Vector3(piece.Along - bounds.center.x, 0f, piece.Depth - bounds.center.z);
                        break;
                }

                float floor = room.min.y;
                if (!string.IsNullOrEmpty(piece.On) && placed.ContainsKey(piece.On))
                    floor = placed[piece.On].max.y;
                else if (!float.IsNaN(piece.Height))
                    floor = room.min.y + piece.Height;
                else if (!string.IsNullOrEmpty(piece.On))
                    Debug.LogWarning("[DemoSceneBuilder] \"" + piece.Id + "\" rests on \"" + piece.On + "\", which has not been placed yet.");
                move.y = floor - bounds.min.y;
                // A rug has no thickness at all, so laid exactly on the boards it shares
                // their plane and the two z-fight - which reads as the rug tearing into
                // white shards rather than as anything recognisable. Lifting flat pieces
                // clear by a few millimetres settles it, and is far too little to see.
                if (bounds.size.y < 0.02f)
                    move.y += 0.005f;

                instance.transform.localPosition += move;
                bounds.center += move;
                placed[piece.Id] = bounds;
            }

            // The torches light the room by night. By day they are out, and the room is
            // lit by the daylight through its windows instead - a fill that keeps the
            // torches' hours in reverse, so as they come up it goes down and the room is
            // never dark. The bank's is the brightest: T_Gold on the coins, the chalice
            // and the key is fully metallic, so it is almost entirely reflected light
            // and goes grey in a dim room.
            var daylight = new GameObject("Daylight");
            daylight.transform.SetParent(interior.transform, false);
            daylight.transform.localPosition = new Vector3(0f, 2.2f, 0f);
            Light light = daylight.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.96f, 0.9f);
            light.range = big ? 9f : 7f;
            var fill = daylight.AddComponent<MultiplayerARPG.Demo.DemoTorch>();
            fill.lamp = light;
            fill.schedule = MultiplayerARPG.Demo.DemoTorch.Schedule.Day;
            fill.intensity = houseIndex == BankHouseIndex ? 2.4f : 1.8f;
            fill.flicker = 0f;
            fill.sway = 0f;
            fill.stagger = 0f;
            fill.Settle();

            // Brighter and further-reaching than the street torches: two on one wall
            // have a whole room to light, where the lamp they replace hung in the middle
            // of it. The reach is the part that matters - a point light's falloff is
            // windowed to its range, so at seven metres the back wall of a big house
            // stood at the very end of it and was nearly dark however bright the torch.
            foreach (GameObject torch in torches)
            {
                MultiplayerARPG.Demo.DemoTorch flame =
                    Kindle(torch, TorchFlame, DemoFlameBuilder.TorchFlamePath, MultiplayerARPG.Demo.DemoTorch.Schedule.Night);
                if (flame == null)
                    continue;
                flame.intensity = houseIndex == BankHouseIndex ? 2.6f : 2.2f;
                flame.lamp.range = big ? 11f : 9f;
            }
        }

        /// <summary>
        /// Places a prop with its base resting on the ground.
        ///
        /// The props are laid out in flat coordinates on a green that is only flat in
        /// the middle, and several of the models do not stand on their own origin — a
        /// wagon sits a little above its, a crate a little below. Both are corrected the
        /// same way as the furniture indoors: measure the model, then move it so its
        /// lowest point meets the ground under it.
        /// </summary>
        private static GameObject GroundProp(string prefabName, Transform parent, float x, float z, float yaw, bool village = false)
        {
            GameObject instance = village
                ? DemoVillageBuilder.Place(prefabName, parent, new Vector3(x, 0f, z), yaw)
                : Prop(prefabName, parent, new Vector3(x, 0f, z), yaw);
            if (instance == null)
                return null;
            if (village)
                AddPropCollider(instance, prefabName);

            Bounds bounds = DemoVillageBuilder.LocalBounds(instance.transform, instance.transform);
            Vector3 world = instance.transform.position;
            float ground = DemoIslandBuilder.HeightAt(world.x, world.z);
            instance.transform.position = new Vector3(world.x, ground - bounds.min.y, world.z);
            return instance;
        }

        /// <summary>
        /// Stands a prop against the outside of a house's front wall - the exterior
        /// version of Against(): a position along the wall is given, and how far out the
        /// prop stands is measured off the prop, so its back touches the wall face and it
        /// is squared to the wall whatever way the house is turned.
        /// </summary>
        private static GameObject Outside(Transform parent, Transform house, float along, string prefabName, float yaw)
        {
            GameObject instance = Prop(prefabName, parent, Vector3.zero, 0f);
            if (instance == null)
                return null;
            instance.transform.rotation = house.rotation * Quaternion.Euler(0f, yaw, 0f);
            float face = OuterFace(house, Side.South);
            Bounds bounds = DemoVillageBuilder.LocalBounds(house, instance.transform);
            // A finger's width off the plaster, so the two do not z-fight where they meet.
            const float gap = 0.03f;
            var move = new Vector3(along - bounds.center.x, 0f, face - gap - bounds.max.z);
            instance.transform.position += house.TransformVector(move);
            Seat(instance);
            return instance;
        }

        /// <summary>
        /// Stands a prop at an offset from another, in that other's own space and turned
        /// the same way - how a crate goes with the stall it belongs to.
        /// </summary>
        private static GameObject Beside(GameObject anchor, Transform parent, Vector3 offset, string prefabName)
        {
            if (anchor == null)
                return null;
            Vector3 world = anchor.transform.TransformPoint(offset);
            Vector3 local = parent.InverseTransformPoint(world);
            return GroundProp(prefabName, parent, local.x, local.z, anchor.transform.eulerAngles.y - parent.eulerAngles.y);
        }

        /// <summary>Rests a placed prop's lowest point on the ground under it.</summary>
        private static void Seat(GameObject instance)
        {
            Bounds bounds = DemoVillageBuilder.LocalBounds(instance.transform, instance.transform);
            Vector3 world = instance.transform.position;
            float ground = DemoIslandBuilder.HeightAt(world.x, world.z);
            instance.transform.position = new Vector3(world.x, ground - bounds.min.y, world.z);
        }

        /// <summary>Places a Fantasy Props model, keeping its collider if the pack ships one.</summary>
        /// <summary>
        /// Shuts anything that ships open on a hinge. The chests do: their rig is posed
        /// wide open, and stood against a wall the lid reaches straight through it.
        ///
        /// The pose comes from the pack's own <c>Closed</c> clip rather than from an angle
        /// picked by eye. There is no closed chest model to swap to, and guessing the hinge
        /// is worse than it sounds — the lid bone reads (302, 180, 180) at rest, so its
        /// local X is not the hinge axis and sweeping it produces nothing that looks shut.
        /// Sampling the clip lands the lid centred over its box to within 13mm.
        ///
        /// Sampling needs an Animator, which these prop prefabs do not carry, so one is
        /// borrowed for the moment it takes. The bone transforms it writes are ordinary
        /// transform values and outlive it.
        /// </summary>
        private static void ShutLid(GameObject instance, string prefabName)
        {
            AnimationClip closed = null;
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath($"{PropDir}/{prefabName}.fbx"))
            {
                var clip = asset as AnimationClip;
                // Unity keeps a "__preview__" copy of every clip beside the real one.
                if (clip == null || clip.name.StartsWith("__preview__") || !clip.name.EndsWith("Closed"))
                    continue;
                closed = clip;
            }
            if (closed == null)
                return;

            bool borrowed = instance.GetComponent<Animator>() == null;
            if (borrowed)
                instance.AddComponent<Animator>();
            closed.SampleAnimation(instance, 0f);
            if (borrowed)
                Object.DestroyImmediate(instance.GetComponent<Animator>());
        }

        /// <param name="solid">Whether it gets collision. Things fixed above head height do not.</param>
        internal static GameObject Prop(string prefabName, Transform parent, Vector3 localPosition, float yaw, bool solid = true)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PropDir}/{prefabName}.fbx");
            if (prefab == null)
            {
                Debug.LogError($"[{nameof(DemoSceneBuilder)}] No prop named \"{prefabName}\".");
                return null;
            }
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            instance.transform.localPosition = localPosition;
            instance.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            if (solid)
                AddPropCollider(instance, prefabName);
            ShutLid(instance, prefabName);
            return instance;
        }

        /// <summary>
        /// Gives a prop something to collide with. The pack ships hand-made collision
        /// meshes for most of them, which are far cheaper and truer than a box round a
        /// wagon or a stall; anything without one falls back to its bounds.
        /// </summary>
        private static void AddPropCollider(GameObject instance, string prefabName)
        {
            GameObject collision = AssetDatabase.LoadAssetAtPath<GameObject>($"{PropDir}/Collisions/Collision_{prefabName}.fbx");
            if (collision != null)
            {
                MeshFilter source = collision.GetComponentInChildren<MeshFilter>();
                if (source != null)
                {
                    var holder = new GameObject("Collision");
                    holder.transform.SetParent(instance.transform, false);
                    holder.AddComponent<MeshCollider>().sharedMesh = source.sharedMesh;
                    return;
                }
            }

            if (instance.GetComponentInChildren<Renderer>() == null)
                return;
            // Measured in the prop's own space, not from Renderer.bounds: that is a box
            // around the world axes, so a prop turned to any angle but a right one gets
            // a collider noticeably bigger than the prop, and the player is stopped by
            // thin air beside it.
            Bounds bounds = DemoVillageBuilder.LocalBounds(instance.transform, instance.transform);
            // Flat things underfoot - rugs, coins, loose pages - should not be walls.
            if (bounds.size.y < 0.25f)
                return;
            var box = instance.AddComponent<BoxCollider>();
            box.center = bounds.center;
            box.size = bounds.size;
        }

        private static void BuildCamp(Scene scene)
        {
            GameObject root = Root(scene, "BanditCamp");
            Vector2 centre = DemoIslandBuilder.CampCentre;
            root.transform.position = new Vector3(centre.x, DemoIslandBuilder.CampHeight, centre.y);

            // No tent module in the pack, so the camp reads through a barricade of
            // fences, stacked crates and a wagon around an open middle.
            for (int i = 0; i < 10; ++i)
            {
                float angle = i / 10f * Mathf.PI * 2f;
                if (i == 3 || i == 4)
                    continue; // Leave a gap so the camp has a way in.
                GroundProp("Prop_WoodenFence_Extension1", root.transform,
                    Mathf.Cos(angle) * 11f, Mathf.Sin(angle) * 11f,
                    -(angle * Mathf.Rad2Deg + 90f), true);
            }
            GroundProp("Wagon", root.transform, -4.9f, 4.9f, 120f);
            GroundProp("Crate_Wooden", root.transform, 3f, -3f, 40f);
            GroundProp("Crate_Metal", root.transform, 4.4f, -1.8f, 8f);
            GroundProp("Barrel_Dark", root.transform, 4.9f, -3.7f, 0f);
            // Loot they have taken, and the gear they took it with.
            GroundProp("Cage_Large", root.transform, -5.5f, -3.5f, 25f);
            GroundProp("WeaponStand", root.transform, 2.5f, 4.5f, 200f);
            GroundProp("Dummy", root.transform, 6f, 2f, 160f);
            GroundProp("Chest_Wood", root.transform, -2.5f, -5f, 70f);
            GroundProp("Skull", root.transform, 1.2f, -6f, 30f);
            GameObject firepit = GroundProp("Firepit", root.transform, 0f, 0f, 0f);
            GroundProp("Cauldron", root.transform, 0f, 0f, 15f);
            GroundProp("Banner_Vertical_1", root.transform, -7f, 2f, 90f);

            // The same fire as the village's, on the same hours: the flame, its light
            // and its smoke all come with the prefab.
            Kindle(firepit, BrazierFlame, DemoFlameBuilder.CampfireFlamePath, MultiplayerARPG.Demo.DemoTorch.Schedule.Night);
            Scatter(root.transform, "Rock_Medium_1", 6, 2.2f, 0.35f);

            MarkStatic(root);
        }

        // ---- the crypt ---------------------------------------------------------

        /// <summary>The crypt front, in cells: three wide so its door is in the middle, and two deep.</summary>
        public const int CryptCellsX = 3;
        public const int CryptCellsZ = 2;

        /// <summary>
        /// Which way the crypt faces: door toward the village, which is the way anyone
        /// walking out to it comes. The same rule as the houses - local -Z is the door.
        /// </summary>
        public static float CryptYaw
        {
            get
            {
                Vector2 away = DemoIslandBuilder.CryptCentre - DemoIslandBuilder.VillageCentre;
                return HouseYaw(new Vector3(away.x, 0f, away.y));
            }
        }

        public static Vector3 CryptOrigin
        {
            get { return new Vector3(DemoIslandBuilder.CryptCentre.x, DemoIslandBuilder.CryptHeight, DemoIslandBuilder.CryptCentre.y); }
        }

        /// <summary>A point in the crypt's own space, on the island.</summary>
        public static Vector3 CryptToWorld(Vector3 local)
        {
            return CryptOrigin + Quaternion.Euler(0f, CryptYaw, 0f) * local;
        }

        /// <summary>
        /// Where the gate down into the dungeon stands: on the door's own line, so that
        /// stepping through the arch is what takes you down. The gate is not placed here;
        /// the kit spawns it from the warp portal database, which DemoDatabaseWiring
        /// writes from these.
        /// </summary>
        public static Vector3 CryptGateWorld
        {
            get { return CryptToWorld(new Vector3(0f, 0f, -CryptCellsZ * DemoVillageBuilder.Cell * 0.5f - 0.1f)); }
        }

        public static float CryptGateYaw { get { return CryptYaw; } }

        /// <summary>
        /// Where a character coming back up arrives: two paces out from the door and
        /// facing away from it, well clear of the gate's trigger - which reaches half a
        /// metre out from the door line - or they would be sent straight back down.
        /// </summary>
        public static Vector3 CryptArrivalWorld
        {
            get { return CryptToWorld(new Vector3(0f, 0.05f, -4.4f)); }
        }

        public static float CryptArrivalYaw { get { return CryptYaw + 180f; } }

        /// <summary>
        /// The way into the dungeon: a squat brick vault set into the hillside, its arch
        /// open on nothing but dark. It is the houses' own masonry - the same walls, the
        /// same quoins, the brick floor slab laid again on top for a roof - because a
        /// crypt the villagers' ancestors built would be. The rocks are the outcrop it
        /// was dug into, the same boulders grown large that the cliffs are made of.
        ///
        /// Inside is a black unlit box. With the sky lighting everything from every side,
        /// a closed room here would be plainly lit through its own doorway, and a lit
        /// room is a room, not a way down. Unlit black is the one thing that reads as
        /// depth. The warp trigger stands in the arch, so a player never gets far enough
        /// in to find the box.
        /// </summary>
        private static void BuildCrypt(Scene scene)
        {
            GameObject root = Root(scene, "Crypt");
            root.transform.position = CryptOrigin;
            root.transform.rotation = Quaternion.Euler(0f, CryptYaw, 0f);
            Transform t = root.transform;
            const float cell = DemoVillageBuilder.Cell;
            const float storey = DemoVillageBuilder.WallHeight;
            float halfX = CryptCellsX * cell * 0.5f;
            float halfZ = CryptCellsZ * cell * 0.5f;

            // Floor and roof, brick slabs both. The walls are 0.12 taller than a storey,
            // so they stand a little proud of the roof slab as a low parapet.
            for (int x = 0; x < CryptCellsX; ++x)
            {
                for (int z = 0; z < CryptCellsZ; ++z)
                {
                    var at = new Vector3(-halfX + cell * (x + 0.5f), 0f, -halfZ + cell * (z + 0.5f));
                    DemoVillageBuilder.Place("Floor_Brick", t, at, 0f);
                    DemoVillageBuilder.Place("Floor_Brick", t, at + Vector3.up * storey, 0f);
                }
            }

            int doorCell = CryptCellsX / 2;
            for (int x = 0; x < CryptCellsX; ++x)
            {
                float px = -halfX + cell * (x + 0.5f);
                bool door = x == doorCell;
                var south = new Vector3(px, 0f, -halfZ);
                DemoVillageBuilder.Place(door ? "Wall_UnevenBrick_Door_Round" : "Wall_UnevenBrick_Straight", t, south, 180f);
                if (door)
                    DemoVillageBuilder.Place("DoorFrame_Round_Brick", t, south, 180f);
                DemoVillageBuilder.Place("Wall_UnevenBrick_Straight", t, new Vector3(px, 0f, halfZ), 0f);
            }
            for (int z = 0; z < CryptCellsZ; ++z)
            {
                float pz = -halfZ + cell * (z + 0.5f);
                DemoVillageBuilder.Place("Wall_UnevenBrick_Straight", t, new Vector3(halfX, 0f, pz), 90f);
                DemoVillageBuilder.Place("Wall_UnevenBrick_Straight", t, new Vector3(-halfX, 0f, pz), 270f);
            }
            // Same quoin, same turns, as DemoVillageBuilder.BuildHouse.
            const string corner = "Corner_Exterior_Brick";
            DemoVillageBuilder.Place(corner, t, new Vector3(-halfX, 0f, -halfZ), 0f);
            DemoVillageBuilder.Place(corner, t, new Vector3(-halfX, 0f, halfZ), 90f);
            DemoVillageBuilder.Place(corner, t, new Vector3(halfX, 0f, halfZ), 180f);
            DemoVillageBuilder.Place(corner, t, new Vector3(halfX, 0f, -halfZ), 270f);

            GameObject dark = GameObject.CreatePrimitive(PrimitiveType.Cube);
            dark.name = "Darkness";
            dark.transform.SetParent(t, false);
            Object.DestroyImmediate(dark.GetComponent<Collider>());
            dark.transform.localPosition = new Vector3(0f, storey * 0.5f, 0.15f);
            dark.transform.localScale = new Vector3(halfX * 2f - 0.3f, storey - 0.05f, halfZ * 2f - 0.1f);
            dark.GetComponent<MeshRenderer>().sharedMaterial = DemoDungeonBuilder.Darkness();

            // Torches either side of the arch, lit by night as the village's are, and
            // mounted the same way: measured to the wall's outer face.
            var torches = new GameObject("Torches");
            torches.transform.SetParent(t, false);
            Mount(torches.transform, t, Side.South, -1.7f);
            Mount(torches.transform, t, Side.South, 1.7f);

            // What lies about a door nobody living uses. The rune ring is where a
            // character coming up arrives, so it is the one thing on the doorstep with
            // no collision.
            var props = new GameObject("Props");
            props.transform.SetParent(t, false);
            GroundProp("Skull", props.transform, -2.8f, -3.3f, 35f);
            GroundProp("Skull_Top", props.transform, 3.1f, -3.4f, 300f);
            GroundProp("Vase_Rubble_Large", props.transform, 2.1f, -2.6f, 80f);
            GroundProp("Chain_Coil", props.transform, -1.5f, -2.6f, 15f);
            GameObject runes = GroundProp("Runes", props.transform, 0f, -4.4f, 0f);
            if (runes != null)
                runes.transform.localScale = Vector3.one * 1.6f;

            // The outcrop the crypt is dug into: boulders grown to the cliffs' size,
            // bedded into the shelf behind and beside it so the front is what shows.
            var rocks = new GameObject("Rocks");
            rocks.transform.SetParent(t, false);
            Outcrop(rocks.transform, "Rock_Medium_1", new Vector3(-2.6f, -0.6f, 2.9f), 1.7f, 20f);
            Outcrop(rocks.transform, "Rock_Medium_2", new Vector3(2.8f, -0.7f, 3.2f), 2.0f, 200f);
            Outcrop(rocks.transform, "Rock_Medium_3", new Vector3(4.1f, -0.5f, 0.6f), 1.4f, 90f);
            Outcrop(rocks.transform, "Rock_Medium_1", new Vector3(-4.2f, -0.5f, 1.3f), 1.5f, 250f);

            MarkStatic(root);
        }

        private static void Outcrop(Transform parent, string prefabName, Vector3 localPosition, float scale, float yaw)
        {
            GameObject rock = InstantiateNature(prefabName, parent);
            if (rock == null)
                return;
            rock.transform.localPosition = localPosition;
            rock.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            rock.transform.localScale = Vector3.one * scale;
            AddCollider(rock);
        }

        // ---- harvestables ----------------------------------------------------

        /// <summary>Where the resource nodes are sown, and how many of each.</summary>
        private struct HarvestPatch
        {
            public string Node;
            public Vector2 Centre;
            public float Radius;
            public int Least;
            public int Most;
            /// <summary>Steepest ground a node of this kind may stand on.</summary>
            public float MaxSlope;
            /// <summary>How far apart two nodes of this kind have to be.</summary>
            public float Spacing;
        }

        private static readonly HarvestPatch[] HarvestPatches =
        {
            // No tree patches here. Every tree on the island is a harvestable now, and
            // they are stood where the island wants its forest rather than in patches of
            // their own — see BuildTreeNodes.
            //
            // No boulder patches either. Every boulder-sized rock on the island is a node,
            // scattered wherever the loose rock falls - see BuildBoulderNodes.
            // Mushrooms under the trees, which is where the terrain paints them too, so
            // the ones you can pick sit among the ones that are only scenery.
            new HarvestPatch { Node = "Mushroom", Centre = new Vector2(-10f, 30f), Radius = 14f, Least = 4, Most = 6, MaxSlope = 24f, Spacing = 3f },
            new HarvestPatch { Node = "Mushroom", Centre = new Vector2(30f, -14f), Radius = 14f, Least = 4, Most = 6, MaxSlope = 24f, Spacing = 3f },
        };

        /// <summary>
        /// Sows the harvestable resources.
        ///
        /// These are spawn areas rather than nodes placed by hand, because a resource the
        /// player takes has to come back: the area holds the count, and puts another node
        /// down after the delay on the entity. It also grounds and spaces them itself, so
        /// the patches below only say roughly where the wood and the stone are.
        /// </summary>
        private static void BuildHarvestNodes(Scene scene, System.Collections.Generic.List<Vector3> boulders)
        {
            GameObject root = Root(scene, "Harvestables");
            BuildTreeNodes(root.transform);
            BuildBoulderNodes(root.transform, boulders);
            foreach (HarvestPatch patch in HarvestPatches)
            {
                string[] models = DemoHarvestBuilder.ModelsFor(patch.Node);
                var kinds = new System.Collections.Generic.List<HarvestableEntity>();
                foreach (string model in models)
                {
                    HarvestableEntity built = DemoHarvestBuilder.Entity(model);
                    if (built != null)
                        kinds.Add(built);
                }
                if (kinds.Count == 0)
                {
                    Debug.LogWarning($"[{nameof(DemoSceneBuilder)}] No harvestable entities for \"{patch.Node}\". Run Build Harvestables first.");
                    continue;
                }

                var area = new GameObject($"{patch.Node}Patch");
                area.transform.SetParent(root.transform, false);
                area.transform.position = new Vector3(
                    patch.Centre.x,
                    DemoIslandBuilder.HeightAt(patch.Centre.x, patch.Centre.y) + 1f,
                    patch.Centre.y);

                var spawner = area.AddComponent<HarvestableSpawnArea>();
                var serialized = new SerializedObject(spawner);
                // A patch is one kind of thing in several shapes, so the models go in the
                // mixture list rather than one being picked as the prefab. Boulders that
                // are all the same rock read as a row of copies.
                serialized.FindProperty("prefab").objectReferenceValue = null;
                SerializedProperty mixture = serialized.FindProperty("spawningPrefabs");
                mixture.arraySize = kinds.Count;
                for (int i = 0; i < kinds.Count; ++i)
                {
                    SerializedProperty entry = mixture.GetArrayElementAtIndex(i);
                    entry.FindPropertyRelative("prefab").objectReferenceValue = kinds[i];
                    entry.FindPropertyRelative("minLevel").intValue = 1;
                    entry.FindPropertyRelative("maxLevel").intValue = 1;
                    // Shared out between the shapes, so the patch holds the number it is
                    // meant to hold however many models it is drawing from.
                    int share = Mathf.Max(1, Mathf.RoundToInt(patch.Least / (float)kinds.Count));
                    int most = Mathf.Max(share, Mathf.RoundToInt(patch.Most / (float)kinds.Count));
                    entry.FindPropertyRelative("minAmount").intValue = share;
                    entry.FindPropertyRelative("maxAmount").intValue = most + 1;
                }
                serialized.FindProperty("randomRadius").floatValue = patch.Radius;
                serialized.FindProperty("minAmount").intValue = patch.Least;
                serialized.FindProperty("maxAmount").intValue = patch.Most;
                serialized.FindProperty("minLevel").intValue = 1;
                serialized.FindProperty("maxLevel").intValue = 1;
                // Pick from the baked spots at random rather than walking them in order,
                // or the same nodes come back in the same sequence every respawn.
                serialized.FindProperty("randomPositionMode").enumValueIndex = (int)GameAreaRandomPositionMode.FullyRandom;
                BakeNodeSpots(serialized, area.transform, patch);
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        /// <summary>
        /// Stands up the island's forest as harvestable nodes.
        ///
        /// The trees are no longer drawn by the terrain — a terrain tree is a record in
        /// the TerrainData with no GameObject, so it can never be harvested — and are
        /// spawned as entities instead. Where they stand is still decided by
        /// <see cref="DemoIslandBuilder.PlaceTrees"/>, so the wood keeps the shape it had:
        /// the same stands, the same sparse red accents, the same scattered dead snags.
        ///
        /// One area per species, rather than one area spawning a mixture. An area picks
        /// its prefab and its position independently, so a single mixed area would put
        /// pines where the twisted trees were meant to be and scatter the dead ones
        /// through the middle of a stand. Giving each species its own area with its own
        /// spots keeps every tree the kind that was placed there.
        /// </summary>
        private static void BuildTreeNodes(Transform root)
        {
            var bySpecies = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<Vector2>>();
            foreach (DemoIslandBuilder.TreePlacement placement in DemoIslandBuilder.PlaceTrees())
            {
                if (!placement.Canopy)
                    continue;
                if (!bySpecies.TryGetValue(placement.Prefab, out System.Collections.Generic.List<Vector2> spots))
                    bySpecies[placement.Prefab] = spots = new System.Collections.Generic.List<Vector2>();
                spots.Add(placement.Spot);
            }

            var trees = new GameObject("Trees");
            trees.transform.SetParent(root, false);
            int total = 0;
            foreach (System.Collections.Generic.KeyValuePair<string, System.Collections.Generic.List<Vector2>> species in bySpecies)
            {
                HarvestableEntity node = DemoHarvestBuilder.Entity(species.Key);
                if (node == null)
                {
                    Debug.LogWarning($"[{nameof(DemoSceneBuilder)}] No harvestable entity for \"{species.Key}\". Run Build Harvestables first.");
                    continue;
                }

                Vector2 middle = Vector2.zero;
                foreach (Vector2 spot in species.Value)
                    middle += spot;
                middle /= species.Value.Count;
                float reach = 0f;
                foreach (Vector2 spot in species.Value)
                    reach = Mathf.Max(reach, Vector2.Distance(spot, middle));

                var area = new GameObject(species.Key);
                area.transform.SetParent(trees.transform, false);
                area.transform.position = new Vector3(middle.x, DemoIslandBuilder.HeightAt(middle.x, middle.y), middle.y);

                var spawner = area.AddComponent<HarvestableSpawnArea>();
                var serialized = new SerializedObject(spawner);
                serialized.FindProperty("prefab").objectReferenceValue = node;
                serialized.FindProperty("randomRadius").floatValue = reach;
                // Every spot is filled, and each is used once: the count matches the spots
                // and they are taken in order, so the forest that was designed is the
                // forest that stands rather than a random draw from it.
                serialized.FindProperty("minAmount").intValue = species.Value.Count;
                serialized.FindProperty("maxAmount").intValue = species.Value.Count;
                serialized.FindProperty("minLevel").intValue = 1;
                serialized.FindProperty("maxLevel").intValue = 1;
                serialized.FindProperty("randomPositionMode").enumValueIndex = (int)GameAreaRandomPositionMode.ByOrder;

                SerializedProperty baked = serialized.FindProperty("randomedPosition3Ds");
                baked.arraySize = species.Value.Count;
                for (int i = 0; i < species.Value.Count; ++i)
                {
                    Vector2 spot = species.Value[i];
                    var world = new Vector3(spot.x, DemoIslandBuilder.HeightAt(spot.x, spot.y), spot.y);
                    baked.GetArrayElementAtIndex(i).vector3Value = area.transform.InverseTransformPoint(world);
                }
                serialized.FindProperty("randomPositionAmount").intValue = species.Value.Count;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                total += species.Value.Count;
            }
            Debug.Log($"[{nameof(DemoSceneBuilder)}] Stood up {total} harvestable trees across {bySpecies.Count} species.");
        }

        /// <summary>
        /// Stands the island's loose boulders up as nodes.
        ///
        /// These come from the scatter rather than from patches of their own, so stone is
        /// found where stone would be — along the rocky ground and under the outcrops —
        /// instead of in two quarries somebody drew on the map. The spots arrive already
        /// grounded and already filtered by the scatter's own height and slope rules.
        ///
        /// The three rock models are shared out between areas the same way the trees are,
        /// one area to a model, so which rock stands where is decided here and not redrawn
        /// every time the area respawns one.
        /// </summary>
        private static void BuildBoulderNodes(Transform root, System.Collections.Generic.List<Vector3> boulders)
        {
            string[] models = DemoHarvestBuilder.ModelsFor("Boulder");
            if (models.Length == 0 || boulders.Count == 0)
                return;

            var stone = new GameObject("Boulders");
            stone.transform.SetParent(root, false);

            // Dealt out in turn rather than randomed, so each model gets its share and no
            // one of them happens to take nearly all of them.
            var byModel = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<Vector3>>();
            for (int i = 0; i < boulders.Count; ++i)
            {
                string model = models[i % models.Length];
                if (!byModel.TryGetValue(model, out System.Collections.Generic.List<Vector3> spots))
                    byModel[model] = spots = new System.Collections.Generic.List<Vector3>();
                spots.Add(boulders[i]);
            }

            int total = 0;
            foreach (System.Collections.Generic.KeyValuePair<string, System.Collections.Generic.List<Vector3>> group in byModel)
            {
                HarvestableEntity node = DemoHarvestBuilder.Entity(group.Key);
                if (node == null)
                {
                    Debug.LogWarning($"[{nameof(DemoSceneBuilder)}] No harvestable entity for \"{group.Key}\". Run Build Harvestables first.");
                    continue;
                }

                Vector3 middle = Vector3.zero;
                foreach (Vector3 spot in group.Value)
                    middle += spot;
                middle /= group.Value.Count;
                float reach = 0f;
                foreach (Vector3 spot in group.Value)
                    reach = Mathf.Max(reach, Vector2.Distance(new Vector2(spot.x, spot.z), new Vector2(middle.x, middle.z)));

                var area = new GameObject(group.Key);
                area.transform.SetParent(stone.transform, false);
                area.transform.position = middle;

                var spawner = area.AddComponent<HarvestableSpawnArea>();
                var serialized = new SerializedObject(spawner);
                serialized.FindProperty("prefab").objectReferenceValue = node;
                serialized.FindProperty("randomRadius").floatValue = reach;
                serialized.FindProperty("minAmount").intValue = group.Value.Count;
                serialized.FindProperty("maxAmount").intValue = group.Value.Count;
                serialized.FindProperty("minLevel").intValue = 1;
                serialized.FindProperty("maxLevel").intValue = 1;
                serialized.FindProperty("randomPositionMode").enumValueIndex = (int)GameAreaRandomPositionMode.ByOrder;

                SerializedProperty baked = serialized.FindProperty("randomedPosition3Ds");
                baked.arraySize = group.Value.Count;
                for (int i = 0; i < group.Value.Count; ++i)
                    baked.GetArrayElementAtIndex(i).vector3Value = area.transform.InverseTransformPoint(group.Value[i]);
                serialized.FindProperty("randomPositionAmount").intValue = group.Value.Count;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                total += group.Value.Count;
            }
            Debug.Log($"[{nameof(DemoSceneBuilder)}] Stood up {total} harvestable boulders across {byModel.Count} kinds.");
        }

        /// <summary>
        /// Works out where in a patch a node may actually stand, and bakes those spots
        /// into the area.
        ///
        /// A spawn area left to itself drops its nodes anywhere inside the circle that a
        /// ray finds ground, and some of these patches run up ground steep enough to
        /// leave a tree standing on air at one side of its trunk. The kit already has the
        /// answer: an area with baked positions uses those instead of randoming its own,
        /// so the slope test can be applied here, once, rather than every respawn.
        ///
        /// The spots are stored in the area's own space, which is what the kit reads them
        /// back as, and they carry their exact ground height — a baked spot is used as
        /// given, with no second ground check.
        /// </summary>
        private static void BakeNodeSpots(SerializedObject serialized, Transform area, HarvestPatch patch)
        {
            Random.State previous = Random.state;
            Random.InitState(HarvestSeed + Mathf.RoundToInt(patch.Centre.x * 31f + patch.Centre.y));

            // Several times the most that will ever stand here at once, so a patch that
            // has just been cleared does not come back in exactly the same places.
            int wanted = patch.Most * 3;
            var spots = new System.Collections.Generic.List<Vector3>();
            for (int attempt = 0; attempt < wanted * 80 && spots.Count < wanted; ++attempt)
            {
                float angle = Random.Range(0f, Mathf.PI * 2f);
                float distance = Mathf.Sqrt(Random.value) * patch.Radius;
                float x = patch.Centre.x + Mathf.Cos(angle) * distance;
                float z = patch.Centre.y + Mathf.Sin(angle) * distance;

                if (DemoIslandBuilder.SmoothSlopeAt(x, z) > patch.MaxSlope)
                    continue;
                if (InsideClearing(x, z) || InsideBuilding(x, z))
                    continue;
                var here = new Vector3(x, DemoIslandBuilder.HeightAt(x, z), z);
                bool crowded = false;
                foreach (Vector3 spot in spots)
                    crowded |= Vector2.Distance(new Vector2(spot.x, spot.z), new Vector2(x, z)) < patch.Spacing;
                if (crowded)
                    continue;
                spots.Add(here);
            }
            Random.state = previous;

            SerializedProperty baked = serialized.FindProperty("randomedPosition3Ds");
            baked.arraySize = spots.Count;
            for (int i = 0; i < spots.Count; ++i)
                baked.GetArrayElementAtIndex(i).vector3Value = area.InverseTransformPoint(spots[i]);
            serialized.FindProperty("randomPositionAmount").intValue = spots.Count;

            if (spots.Count < patch.Most)
                Debug.LogWarning($"[{nameof(DemoSceneBuilder)}] {patch.Node} patch at {patch.Centre} only found " +
                                 $"{spots.Count} spots for up to {patch.Most} nodes.");
        }

        /// <summary>Rings small props around a point, used for the campfire stones.</summary>
        private static void Scatter(Transform parent, string prefabName, int count, float radius, float scale)
        {
            for (int i = 0; i < count; ++i)
            {
                float angle = i / (float)count * Mathf.PI * 2f;
                GameObject rock = InstantiateNature(prefabName, parent);
                if (rock == null)
                    return;
                rock.transform.localPosition = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                rock.transform.localScale = Vector3.one * scale;
            }
        }

        private static GameObject InstantiateNature(string prefabName, Transform parent)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{NatureDir}/{prefabName}.prefab");
            if (prefab == null)
            {
                Debug.LogError($"[{nameof(DemoSceneBuilder)}] No nature prefab named \"{prefabName}\".");
                return null;
            }
            return (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        }

        /// <summary>A band of terrain a group of props is allowed to grow in.</summary>
        private struct Band
        {
            public string Group;
            public string[] Prefabs;
            public int Count;
            public float MinHeight;
            public float MaxHeight;
            public float MaxSlope;
            public float MinScale;
            public float MaxScale;
            public bool Collide;
            /// <summary>Gather into stands instead of spreading evenly, so woods have edges.</summary>
            public int Clusters;
            public float ClusterRadius;
        }

        private static readonly Band[] Bands =
        {
            // Trees, bushes, grass and flowers are not scattered here any more. They live
            // in the terrain as tree instances and detail layers, which draws them far
            // more thickly for far less, and keeps several thousand transforms out of the
            // scene file.
            //
            // What stays are the pieces that need collision the terrain cannot give them:
            // details have no collider at all, and a boulder the player can walk into has
            // to be a real object.
            new Band { Group = "Rocks", Count = 150, MinHeight = 1f, MaxHeight = 24f, MaxSlope = 40f, MinScale = 0.6f, MaxScale = 1.6f, Collide = true,
                Prefabs = new[] { "Rock_Medium_1", "Rock_Medium_2", "Rock_Medium_3", "Pebble_Round_1", "Pebble_Square_2", "Pebble_Round_4" } },
            // Pebbles on the sand. The beach is a narrow strip running down off the
            // plateau, so this tolerates the slope of that ramp rather than flat ground.
            new Band { Group = "Shore", Count = 130, MinHeight = 0.1f, MaxHeight = 1.4f, MaxSlope = 26f, MinScale = 0.5f, MaxScale = 1.2f,
                Prefabs = new[] { "Pebble_Round_2", "Pebble_Round_5", "Pebble_Square_1", "Pebble_Square_4" } },
        };

        // ---- cliffs ----------------------------------------------------------

        /// <summary>How steep ground has to be before rock breaks through it.</summary>
        private const float CliffMinSlope = 31f;

        /// <summary>Outcrops to try for. Not all sites survive the tests.</summary>
        private const int CliffCount = 26;

        /// <summary>The pack has no cliff model, so cliffs are these grown large.</summary>
        private static readonly string[] CliffRocks = { "Rock_Medium_1", "Rock_Medium_2", "Rock_Medium_3" };

        /// <summary>
        /// Breaks rock out of the steep ground as cliffs and outcrops.
        ///
        /// The Nature pack ships no cliff model at all — only three boulders about three
        /// metres across — so a cliff is those same boulders grown four or five times and
        /// set in a row along the slope, each one bedded a third of its height into the
        /// hill and overlapping its neighbours enough that the row reads as one broken
        /// face rather than as a line of separate rocks. This is how the pack's own demo
        /// does it.
        ///
        /// They go along the contour, not up the slope. A row laid across the hill
        /// follows the line a real outcrop weathers into; the same rocks laid up and down
        /// it read as a rockfall.
        /// </summary>
        private static void BuildCliffs(Scene scene)
        {
            GameObject root = Root(scene, "Cliffs");
            Random.State previous = Random.state;
            Random.InitState(CliffSeed);

            float half = DemoIslandBuilder.Size * 0.5f - 8f;
            var sites = new System.Collections.Generic.List<Vector2>();
            int placed = 0;

            for (int attempt = 0; attempt < CliffCount * 400 && placed < CliffCount; ++attempt)
            {
                float x = Random.Range(-half, half);
                float z = Random.Range(-half, half);

                float height = DemoIslandBuilder.HeightAt(x, z);
                if (height < 2.5f || height > 22f)
                    continue;
                if (DemoIslandBuilder.SmoothSlopeAt(x, z) < CliffMinSlope)
                    continue;
                // The whole outcrop has to stand clear, not just the point it is centred
                // on. An outcrop is twenty metres end to end, so a site a step outside
                // the village puts a ten-metre rock in the gateway.
                if (!StandsClear(new Vector2(x, z), 14f))
                    continue;

                // Outcrops want to be apart from one another, or they pile into one
                // quarry-sized heap wherever the island happens to be steepest.
                var here = new Vector2(x, z);
                bool crowded = false;
                // Outcrops keep their distance from one another, but not as far as they
                // once did: giving the settlements a proper berth took a good deal of the
                // island out of the running, and at the old spacing the rock thinned out
                // everywhere else to pay for it.
                foreach (Vector2 site in sites)
                    crowded |= Vector2.Distance(site, here) < 21f;
                if (crowded)
                    continue;
                sites.Add(here);

                RaiseOutcrop(root.transform, here);
                ++placed;
            }

            MarkStatic(root);
            Random.state = previous;
            Debug.Log($"[{nameof(DemoSceneBuilder)}] Raised {placed} outcrops of {root.transform.childCount} rocks.");
        }

        /// <summary>
        /// Whether everything within <paramref name="radius"/> of a point is outside the
        /// village and the camp — tested round the edge of the circle, not just at its
        /// middle, since it is the edge of a big rock that ends up in the street.
        /// </summary>
        private static bool StandsClear(Vector2 spot, float radius)
        {
            if (InsideClearing(spot.x, spot.y))
                return false;
            for (int i = 0; i < 8; ++i)
            {
                float angle = i / 8f * Mathf.PI * 2f;
                if (InsideClearing(spot.x + Mathf.Cos(angle) * radius, spot.y + Mathf.Sin(angle) * radius))
                    return false;
            }
            return true;
        }

        /// <summary>Sets one row of rock into the hillside.</summary>
        private static void RaiseOutcrop(Transform parent, Vector2 site)
        {
            // Along the contour: the direction across the slope, which is the uphill
            // direction turned a quarter.
            Vector2 uphill = DemoIslandBuilder.SlopeDirection(site.x, site.y);
            var along = new Vector2(-uphill.y, uphill.x);

            int rocks = Random.Range(3, 7);
            float centre = (rocks - 1) * 0.5f;
            for (int i = 0; i < rocks; ++i)
            {
                // Big in the middle of the row and smaller at its ends, so an outcrop
                // tapers into the hill instead of stopping dead.
                float fromMiddle = Mathf.Abs(i - centre) / Mathf.Max(centre, 1f);
                float scale = Random.Range(2.6f, 4.6f) * Mathf.Lerp(1f, 0.62f, fromMiddle);
                // Stretched upward. These are boulders, and a boulder scaled evenly is
                // still a boulder however big it gets; drawn out on its vertical axis it
                // starts to read as a block of cliff.
                float rise = Random.Range(1.25f, 1.8f);

                Vector2 spot = site + along * ((i - centre) * Random.Range(2.6f, 3.4f) * scale * 0.32f);
                spot += uphill * Random.Range(-1.6f, 1.6f);
                // Its own footprint plus a few paces, so no part of the rock reaches the
                // green and nobody has to squeeze past it on the way in.
                if (!StandsClear(spot, scale * 1.8f + 4f))
                    continue;

                GameObject rock = InstantiateNature(CliffRocks[Random.Range(0, CliffRocks.Length)], parent);
                if (rock == null)
                    return;
                rock.transform.localScale = new Vector3(scale, scale * rise, scale);

                // Leaned into the slope, but only part of the way. Left upright, a rock
                // with a roughly flat underside touches a steep hillside at its uphill
                // edge and hangs metres clear at the other. Turned the whole way onto the
                // surface normal it lies flush and reads as a low mound rather than as a
                // face. A third of the way over keeps it standing while the sinking below
                // takes care of the daylight underneath.
                Vector3 normal = DemoIslandBuilder.SurfaceNormal(spot.x, spot.y);
                Vector3 lie = Vector3.Slerp(Vector3.up, normal, Random.Range(0.25f, 0.5f));
                rock.transform.rotation =
                    Quaternion.FromToRotation(Vector3.up, lie) *
                    Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

                // Then sunk, so what shows is an outcrop breaking through the hill
                // rather than a boulder resting on it. Measured in world space, after
                // the turn onto the slope: the rock's own local bounds say nothing about
                // where it now reaches once it is lying at an angle.
                rock.transform.position = new Vector3(spot.x, 0f, spot.y);
                Bounds world = WorldExtent(rock.transform);
                float ground = DemoIslandBuilder.HeightAt(spot.x, spot.y);
                float middle = ground - world.size.y * Random.Range(-0.02f, 0.14f);
                rock.transform.position += new Vector3(0f, middle - world.center.y, 0f);
                Settle(rock.transform);

                AddCliffCollider(rock);
            }
        }

        /// <summary>
        /// Lowers a rock until no daylight shows under it.
        ///
        /// Bedding it on the slope gets it most of the way, but these are irregular
        /// lumps, not slabs, and the ground under a fifteen-metre one is not a plane.
        /// Sampling round the footprint and dropping the rock by the worst gap costs
        /// nothing and is the difference between an outcrop and a rock hovering with a
        /// sliver of hillside visible beneath it.
        /// </summary>
        private static void Settle(Transform rock)
        {
            Bounds world = WorldExtent(rock);
            float reach = Mathf.Max(world.size.x, world.size.z) * 0.42f;
            float worst = 0f;
            for (int i = 0; i < 12; ++i)
            {
                float angle = i / 12f * Mathf.PI * 2f;
                float x = rock.position.x + Mathf.Cos(angle) * reach;
                float z = rock.position.z + Mathf.Sin(angle) * reach;
                // How far the rock's lowest point stands above the ground out here.
                worst = Mathf.Max(worst, world.min.y - DemoIslandBuilder.HeightAt(x, z));
            }
            // A hand's width beyond touching, so nothing shows a seam between the two
            // where the ground curves between the points that were sampled.
            rock.position -= new Vector3(0f, Mathf.Max(worst, 0f) + 0.12f, 0f);
        }

        /// <summary>A placed object's reach in world space, turn and scale included.</summary>
        private static Bounds WorldExtent(Transform obj)
        {
            Bounds local = DemoVillageBuilder.LocalBounds(obj, obj);
            var world = new Bounds();
            for (int i = 0; i < 8; ++i)
            {
                var corner = new Vector3(
                    (i & 1) == 0 ? local.min.x : local.max.x,
                    (i & 2) == 0 ? local.min.y : local.max.y,
                    (i & 4) == 0 ? local.min.z : local.max.z);
                Vector3 point = obj.TransformPoint(corner);
                if (i == 0)
                    world = new Bounds(point, Vector3.zero);
                else
                    world.Encapsulate(point);
            }
            return world;
        }

        /// <summary>
        /// Collision for a cliff rock, taken from the rock itself.
        ///
        /// A capsule is right for a boulder the player walks round, but wrong for a
        /// fifteen-metre face they walk along: the capsule bulges out of the rock at the
        /// bottom and stops them short of it. These meshes are only a few hundred
        /// triangles, so the mesh itself is the cheaper and truer collider — and the
        /// navmesh is baked from colliders, so it is what keeps the cliff unclimbable.
        /// </summary>
        private static void AddCliffCollider(GameObject rock)
        {
            MeshFilter mesh = rock.GetComponentInChildren<MeshFilter>();
            if (mesh == null || mesh.sharedMesh == null)
                return;
            // On the object that owns the mesh, not the prop root: the pack keeps its
            // meshes one level down, and a MeshCollider only lines up with the mesh when
            // it shares its transform.
            var collider = mesh.gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh.sharedMesh;
        }

        /// <summary>
        /// Scatters the loose rock, and reports where the boulder-sized pieces went.
        ///
        /// The big ones are not placed here any more. A boulder the player can break has
        /// to be an entity, so this decides where they belong — the same ground, the same
        /// seeded scatter — and hands the spots back for the scene to stand nodes on.
        /// Pebbles stay as scenery: there is nothing to mine out of a stone the size of a
        /// fist, and making every one of them a networked object would be absurd.
        /// </summary>
        private static System.Collections.Generic.List<Vector3> BuildNature(Scene scene)
        {
            var boulders = new System.Collections.Generic.List<Vector3>();
            GameObject root = Root(scene, "Nature");
            Random.State previous = Random.state;
            Random.InitState(ScatterSeed);

            float half = DemoIslandBuilder.Size * 0.5f - 4f;
            foreach (Band band in Bands)
            {
                var group = new GameObject(band.Group);
                group.transform.SetParent(root.transform, false);

                Vector2[] centres = band.Clusters > 0 ? PickClusterCentres(band, half) : null;

                int placed = 0;
                // Rejection sampling: most of the square is sea or the wrong band, so
                // allow generous attempts before giving up on a group.
                for (int attempt = 0; attempt < band.Count * 60 && placed < band.Count; ++attempt)
                {
                    float x, z;
                    if (centres != null && centres.Length > 0)
                    {
                        Vector2 centre = centres[Random.Range(0, centres.Length)];
                        // Square-rooting the radius spreads points evenly over the disc
                        // rather than bunching them at the middle of every stand.
                        float angle = Random.Range(0f, Mathf.PI * 2f);
                        float distance = Mathf.Sqrt(Random.value) * band.ClusterRadius;
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

                    float height = DemoIslandBuilder.HeightAt(x, z);
                    if (height < band.MinHeight || height > band.MaxHeight)
                        continue;
                    if (SlopeAt(x, z) > band.MaxSlope)
                        continue;
                    if (InsideClearing(x, z))
                        continue;

                    string chosen = band.Prefabs[Random.Range(0, band.Prefabs.Length)];
                    if (chosen.StartsWith("Rock_Medium"))
                    {
                        // Boulder sized: hand it to the harvestables rather than standing
                        // a piece of scenery here.
                        boulders.Add(new Vector3(x, height, z));
                        ++placed;
                        continue;
                    }

                    GameObject prop = InstantiateNature(chosen, group.transform);
                    if (prop == null)
                        break;
                    prop.transform.position = new Vector3(x, height, z);
                    prop.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                    prop.transform.localScale = Vector3.one * Random.Range(band.MinScale, band.MaxScale);
                    if (band.Collide)
                        AddCollider(prop);
                    ++placed;
                }
                MarkStatic(group);
                if (placed < band.Count)
                    Debug.LogWarning($"[{nameof(DemoSceneBuilder)}] {band.Group}: only placed {placed} of {band.Count}.");
            }

            Random.state = previous;
            return boulders;
        }

        /// <summary>
        /// Picks where each stand of a clustered band sits. Centres are drawn from
        /// ground the band could actually grow on, so a stand is never centred out at
        /// sea with only its fringe on land.
        /// </summary>
        private static Vector2[] PickClusterCentres(Band band, float half)
        {
            var centres = new System.Collections.Generic.List<Vector2>();
            for (int attempt = 0; attempt < band.Clusters * 200 && centres.Count < band.Clusters; ++attempt)
            {
                float x = Random.Range(-half, half);
                float z = Random.Range(-half, half);
                float height = DemoIslandBuilder.HeightAt(x, z);
                if (height < band.MinHeight || height > band.MaxHeight)
                    continue;
                if (InsideClearing(x, z))
                    continue;
                centres.Add(new Vector2(x, z));
            }
            return centres.ToArray();
        }

        /// <summary>
        /// Trees and rocks need to block movement, but the Quaternius prefabs ship
        /// without colliders. A capsule round the trunk is enough and far cheaper than
        /// a mesh collider on every tree.
        /// </summary>
        private static void AddCollider(GameObject prop)
        {
            if (prop.GetComponentInChildren<Renderer>() == null)
                return;
            // Measured in the prop's own space. Renderer.bounds is a world-axis box, so
            // for a rock turned to a random angle - which every scattered rock is - it
            // reports a size the rock does not have, and the capsule is centred on the
            // pivot rather than on the rock, which leaves boulders whose base can be
            // walked through.
            Bounds bounds = DemoVillageBuilder.LocalBounds(prop.transform, prop.transform);
            // Loose stones are not obstacles. A capsule shorter than it is wide collapses
            // into a sphere of that radius, so collision on an ankle-high pebble is a
            // half-metre ball of invisible air standing in a field - and the band mixes
            // pebbles in among the boulders, so this cannot be decided per band.
            if (bounds.size.y * prop.transform.localScale.y < LitterHeight)
                return;
            var collider = prop.AddComponent<CapsuleCollider>();
            collider.height = bounds.size.y;
            // Wide enough to stop the player at the rock rather than inside it, while
            // leaving the overhanging edges of an irregular boulder walkable.
            collider.radius = Mathf.Max(0.25f, Mathf.Min(bounds.size.x, bounds.size.z) * 0.4f);
            collider.center = bounds.center;
        }

        /// <summary>
        /// Keeps scatter off the village green and the bandit camp. This is the same
        /// test the terrain uses to decide where the ground is bare earth, so the
        /// undergrowth stops exactly where the earth begins instead of thinning out
        /// somewhere near it.
        /// </summary>
        private static bool InsideClearing(float x, float z)
        {
            return DemoIslandBuilder.InsideSettlement(x, z) ||
                   DemoIslandBuilder.OnSettledGround(x, z) ||
                   InsideBuilding(x, z);
        }

        /// <summary>
        /// Whether a point stands on one of the houses.
        ///
        /// The settlement test alone is not enough for these: its edge is deliberately
        /// wobbly, so that the bare earth of the green does not end in a drawn circle,
        /// and the houses sit out near that edge rather than in the middle of it. A
        /// house can therefore fall on the wrong side of the wobble, and then a boulder
        /// is scattered through its wall and stands in the middle of the room.
        /// </summary>
        private static bool InsideBuilding(float x, float z)
        {
            var point = new Vector2(x, z);
            for (int i = 0; i < HouseLayout.Length; ++i)
            {
                Vector2 centre = DemoIslandBuilder.VillageCentre + new Vector2(HouseLayout[i].x, HouseLayout[i].z);
                // Half the footprint's diagonal covers the corners of a house at any
                // angle, and a pace beyond that keeps rocks off the walls as well.
                float half = (IsBigHouse(i) ? 3f : 2f) * DemoVillageBuilder.Cell * 0.5f;
                if (Vector2.Distance(point, centre) < half * Mathf.Sqrt(2f) + 1.5f)
                    return true;
            }
            Vector2 tower = DemoIslandBuilder.VillageCentre + new Vector2(WatchtowerLayout.x, WatchtowerLayout.z);
            float towerHalf = WatchtowerCells * DemoVillageBuilder.Cell * 0.5f;
            // A pace more on the tower than on a house: its ladder and the ground at its
            // foot stand outside the walls.
            return Vector2.Distance(point, tower) < towerHalf * Mathf.Sqrt(2f) + 2.5f;
        }

        private static float SlopeAt(float x, float z)
        {
            return DemoIslandBuilder.SmoothSlopeAt(x, z);
        }

        private static void BuildSpawners(Scene scene)
        {
            GameObject root = Root(scene, "Spawners");

            MonsterCharacterEntity banditMale = LoadEntity($"{EntityDir}/DemoBanditMale.prefab");
            MonsterCharacterEntity banditFemale = LoadEntity($"{EntityDir}/DemoBanditFemale.prefab");
            MonsterCharacterEntity cultistMale = LoadEntity($"{EntityDir}/DemoCultistMale.prefab");
            MonsterCharacterEntity cultistFemale = LoadEntity($"{EntityDir}/DemoCultistFemale.prefab");
            MonsterCharacterEntity marauderMale = LoadEntity($"{EntityDir}/DemoMarauderMale.prefab");
            MonsterCharacterEntity marauderFemale = LoadEntity($"{EntityDir}/DemoMarauderFemale.prefab");
            MonsterCharacterEntity wolf = LoadEntity($"{EntityDir}/DemoWolf.prefab");
            if (banditMale == null || banditFemale == null || cultistMale == null ||
                cultistFemale == null || marauderMale == null || marauderFemale == null ||
                wolf == null)
                return;

            // Weakest nearest the village, toughest at the camp, so difficulty rises as the
            // player works outward from where they spawn.
            //
            // The three families are laid out so that a player can choose their fight rather
            // than take whatever the nearest field offers: each one wears — and drops — the
            // armour of one of the three classes, so where you hunt is how you gear up.
            // Bandits in ranger leathers hold the fields by the village, cultists in robes
            // keep to the hills, and the marauders in plate hold the camp, which is both the
            // hardest fight and the heaviest armour.
            // The outskirts area used to be centred on (-6, -4) with a 42m radius, and the
            // village-peace rule below slid it 72m out from the green - which put it
            // squarely over the crypt's doorstep, so a character coming up out of the
            // crypt was killed on the doorstep by level-two bandits. This centre is
            // where that rule was going to move it anyway, with a radius that stops
            // short of the crypt.
            AddSpawner(root, "Spawn_Outskirts", new Vector2(14f, 0f), 30f, banditMale, 1, 2, 8);
            AddSpawner(root, "Spawn_Woods", new Vector2(26f, 30f), 38f, banditFemale, 2, 4, 7);
            AddSpawner(root, "Spawn_Woods_Cultists", new Vector2(26f, 30f), 34f, cultistMale, 3, 4, 4);
            // The hill areas sit south-west of the crypt rather than on it. A spawn area
            // finds its ground with a ray from above, and the crypt's roof is ground to a
            // ray, so an area that covers the crypt stands cultists on its roof - where
            // they cannot get down, and from where they cannot be reached.
            AddSpawner(root, "Spawn_Hills", new Vector2(-14f, -58f), 24f, cultistFemale, 4, 6, 6);
            AddSpawner(root, "Spawn_Hills_Marauders", new Vector2(-14f, -58f), 24f, marauderMale, 5, 6, 4);
            AddSpawner(root, "Spawn_Camp", DemoIslandBuilder.CampCentre, 16f, marauderFemale, 6, 8, 5);
            AddSpawner(root, "Spawn_Camp_Bandits", DemoIslandBuilder.CampCentre, 16f, banditFemale, 6, 8, 4);

            // Wolves: four small packs ringing the village, and the first fight the island
            // offers anyone.
            //
            // The bands above are laid out by direction, which assumes the player walks the
            // way we expect. They do not - and the one who walked south-east towards the
            // camp met a level five marauder as the first enemy of the game and could not
            // scratch it. A ring fixes that by not caring which way they go: every road out
            // of the green passes a pack within a few paces of leaving.
            //
            // Each is small and tight - a 10m disc holding two to four - so it reads as a
            // pack rather than a field of wolves, and four of them still come to less than
            // one bandit field. 41m out is as close as they are allowed: the village-peace
            // rule below wants a clear 30m beyond an area's own edge, and that rule exists
            // because players used to log in standing among eight bandits. Placed at the
            // limit rather than short of it, so the rule never has to move them and their
            // spacing stays the spacing intended here.
            //
            // North and north-west are left out on purpose: at this distance both run into
            // the shore, and half a pack would spawn in the sea.
            Vector2 green = DemoIslandBuilder.VillageCentre;
            AddSpawner(root, "Spawn_Wolves_East", green + new Vector2(41f, 0f), 10f, wolf, 1, 2, 4);
            AddSpawner(root, "Spawn_Wolves_Southeast", green + new Vector2(29f, -29f), 10f, wolf, 1, 2, 4);
            AddSpawner(root, "Spawn_Wolves_South", green + new Vector2(0f, -41f), 10f, wolf, 1, 2, 4);
            AddSpawner(root, "Spawn_Wolves_West", green + new Vector2(-41f, 0f), 10f, wolf, 1, 2, 3);
        }

        private static MonsterCharacterEntity LoadEntity(string path)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                Debug.LogError($"[{nameof(DemoSceneBuilder)}] Missing \"{path}\". Run Open MMORPG > Demo > Build Character Entities first.");
                return null;
            }
            return prefab.GetComponent<MonsterCharacterEntity>();
        }

        /// <summary>
        /// How much open ground to leave around the village green, in metres. A spawn area
        /// that reaches inside this is pushed out until it does not.
        /// </summary>
        private const float VillagePeace = 30f;

        /// <summary>
        /// The same for the crypt's doorstep, where players arrive coming up out of the
        /// dungeon. Smaller: the crypt is a building and a rune ring, not a village, and
        /// the cultists are meant to be found near it - just not standing on the ring.
        /// Not too small, either: monsters wander a few metres out from where they are
        /// spawned, and at ten a bandit from the outskirts area was on the doorstep
        /// within a minute. A ray from above also reads the crypt's roof as ground, so
        /// an area that reaches the crypt stands monsters on its roof, where they
        /// cannot be fought.
        /// </summary>
        private const float CryptPeace = 16f;

        private static void AddSpawner(GameObject root, string name, Vector2 centre, float radius, MonsterCharacterEntity prefab, short minLevel, short maxLevel, int amount)
        {
            // Nothing may spawn within reach of where players arrive. A monster spawn area
            // is a disc, and the outskirts one was wide enough to cover the village green
            // itself — so a character logged in standing among eight bandits and was killed
            // before it could move. That was survivable only while the monsters were
            // unarmed; the moment each family carried the weapon it drops, it stopped being.
            //
            // The area keeps the size it was given and is slid directly away from the green
            // until its edge clears it, which leaves the intended density and the intended
            // walk from the village rather than quietly shrinking one or the other.
            Vector2 village = DemoIslandBuilder.VillageCentre;
            float distance = Vector2.Distance(centre, village);
            float needed = radius + VillagePeace;
            if (distance < needed)
            {
                Vector2 away = distance > 0.01f ? (centre - village).normalized : Vector2.right;
                Vector2 moved = village + away * needed;
                Debug.Log($"[{nameof(DemoSceneBuilder)}] {name} reached to within {(distance - radius):F0}m of the " +
                          $"village green, so it moved out to {needed:F0}m. Players have to be able to arrive.");
                centre = moved;
            }
            Vector2 crypt = DemoIslandBuilder.CryptCentre;
            distance = Vector2.Distance(centre, crypt);
            needed = radius + CryptPeace;
            if (distance < needed)
            {
                Vector2 away = distance > 0.01f ? (centre - crypt).normalized : Vector2.down;
                Vector2 moved = crypt + away * needed;
                Debug.Log($"[{nameof(DemoSceneBuilder)}] {name} reached to within {(distance - radius):F0}m of the " +
                          $"crypt door, so it moved out to {needed:F0}m. Players come up out of it there.");
                centre = moved;
            }

            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            go.transform.position = new Vector3(centre.x, DemoIslandBuilder.HeightAt(centre.x, centre.y), centre.y);

            var area = go.AddComponent<MonsterSpawnArea>();
            var serialized = new SerializedObject(area);
            serialized.FindProperty("prefab").objectReferenceValue = prefab;
            serialized.FindProperty("randomRadius").floatValue = radius;
            serialized.FindProperty("minLevel").intValue = minLevel;
            serialized.FindProperty("maxLevel").intValue = maxLevel;
            serialized.FindProperty("minAmount").intValue = Mathf.Max(1, amount - 2);
            serialized.FindProperty("maxAmount").intValue = amount;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Says so, loudly, if players would arrive inside something solid.
        ///
        /// The map's start position and the props on the green are decided in two different
        /// files, and they had quietly landed on the same spot: the arrival point sat inside
        /// the firepit. A character that spawns inside a mesh collider is ejected by the
        /// physics and dies on arrival, and since the respawn point is the arrival point it
        /// dies again every time it gets up — which looks like a broken respawn rather than
        /// a misplaced spawn, and sends you looking in entirely the wrong place. Cheap to
        /// check here, where both the scene and the map data are to hand.
        /// </summary>
        private static void CheckArrivalIsClear()
        {
            var map = AssetDatabase.LoadAssetAtPath<BaseMapInfo>(
                "Assets/OpenMMORPG/Demo/GameData/Resources/MapInfos/BaseMap.asset");
            if (map != null)
                CheckClear(new SerializedObject(map).FindProperty("startPosition").vector3Value, "Players arrive");
            // The same for anyone coming back up out of the crypt.
            CheckClear(CryptArrivalWorld, "Players come up out of the crypt");
        }

        private static void CheckClear(Vector3 arrival, string who)
        {
            // A character's own capsule: 0.3m across, 1.8m tall, standing on the spot.
            Collider[] blocking = Physics.OverlapCapsule(
                arrival + Vector3.up * 0.3f, arrival + Vector3.up * 1.5f, 0.3f);
            if (blocking.Length == 0)
                return;
            var names = new System.Collections.Generic.List<string>();
            foreach (Collider collider in blocking)
                names.Add(collider.transform.parent == null
                    ? collider.name
                    : collider.transform.parent.name + "/" + collider.name);
            Debug.LogError($"[{nameof(DemoSceneBuilder)}] {who} at {arrival:F1} inside " +
                           string.Join(", ", names.ToArray()) + ". They will be thrown out of it by the " +
                           "physics and die on arrival, and again on every respawn. Move the arrival " +
                           "point in DemoDatabaseWiring, or move whatever is standing on it.");
        }

        /// <summary>
        /// Marks a building static for batching and occlusion — except for the parts of it
        /// that move.
        ///
        /// Static batching bakes a renderer's vertices into world space once, after which
        /// its transform no longer moves what you see. On a door that is silently fatal: the
        /// collider still swings open, because colliders are not batched, so the doorway
        /// really does open and you walk through — while the leaf stays drawn shut across
        /// the opening you just walked through. It reads as a door that is painted on, and
        /// nothing in the console says otherwise.
        ///
        /// Occlusion culling assumes the same thing, so a mover gets no static flags at all.
        /// </summary>
        internal static void MarkStatic(GameObject root)
        {
            var moving = new System.Collections.Generic.HashSet<Transform>();
            foreach (MultiplayerARPG.Demo.DemoDoor door in root.GetComponentsInChildren<MultiplayerARPG.Demo.DemoDoor>(true))
            {
                if (door.pivot == null)
                    continue;
                foreach (Transform part in door.pivot.GetComponentsInChildren<Transform>(true))
                    moving.Add(part);
            }
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (moving.Contains(child))
                    continue;
                GameObjectUtility.SetStaticEditorFlags(child.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
            }
        }
    }
}
