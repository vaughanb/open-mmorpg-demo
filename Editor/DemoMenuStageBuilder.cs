using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// The menu scene: a lit stone terrace with the painted valley behind it.
    ///
    /// `01Home` shipped as a flat orange camera clear colour, one directional light, an
    /// **invisible** `Plane` (a MeshFilter and a MeshCollider, no renderer) and a character
    /// standing in the void. Every home screen - server list, login, register, character list,
    /// character create - shares that camera, so one backdrop and one stage fix all of them.
    ///
    /// Built rather than hand-placed, like the rest of the demo, so it can be re-run after the
    /// art changes. Nothing here is guessed: every prop is instantiated, its renderer bounds
    /// measured, and then offset so its **footprint centre** lands on the mark and its
    /// **bottom** sits on the floor. Quaternius pivots are not consistent - some models are
    /// centred, some sit on a corner - and placing by transform position alone leaves things
    /// sunk or floating.
    ///
    /// The composition follows the backdrop painting: a valley at sunset, seen from above. So
    /// the stage is the **top of a road** - flat where the character stands, then tipping over a
    /// brow and running down the hillside towards the painting, with grass verges, trees standing
    /// on the slope and lit braziers stepping down beside the cobbles.
    ///
    /// **Everything is seated on <see cref="GroundY"/>, not on y=0.** That is the lesson of the
    /// two versions before this one, which both put trees out where there was no floor and left
    /// them hanging in the air. Give a prop a depth and the hill decides its height.
    ///
    /// **The camera is two framings, not one.** See <see cref="MenuEye"/> and
    /// <see cref="PortraitEye"/>: the scene is saved wide, and the character screens borrow the
    /// camera and give it back. Trying to serve both from one lens is what made every earlier
    /// version of this set a compromise.
    /// </summary>
    public static class DemoMenuStageBuilder
    {
        private const string ScenePath = "Assets/OpenMMORPG/Demo/Scenes/01Home.unity";
        private const string BackdropTexture = "Assets/OpenMMORPG/Demo/Textures/OpenMMORPG-menu-background.jpg";

        /// <summary>
        /// How tall the painted backdrop stands, in metres. **The width is not a constant** - it
        /// comes from the image's own aspect, so a recrop cannot stretch the painting.
        ///
        /// **Sized by the widest screen it has to cover, and nothing else.** At the backdrop's
        /// depth the frame is 18.1 to each side on 16:9 and 23.8 on 21:9, so the quad's half-width
        /// must be at least 23.8 or an ultrawide screen shows the void past the painting's edge.
        /// 17.6 tall gives 48.2 wide on this 2.74:1 panorama: 24.1 each side, which clears 23.8
        /// with a little to spare.
        ///
        /// The cost is that a 16:9 player sees the middle **75%** of the painting. That is not
        /// waste that can be tuned away - the ceiling while still covering 21:9 is `(16/9)/(21/9)`,
        /// or 76.2%, and this is within a point of it. Showing the whole width would mean either
        /// dropping ultrawide support or living with empty edges there.
        ///
        /// Vertically 17.6 is generous: the quad only has to span y -2.1 (where the road's lip
        /// starts hiding it) to 9.3 (the close framing's frame top), which is 11.4. The extra is
        /// what the width demanded, and it buys room to sit the horizon where it looks right.
        /// </summary>
        private const float BackdropHeight = 17.6f;

        /// <summary>
        /// Where its centre sits.
        ///
        /// The y has a window rather than a value: the quad's top must reach 9.3 and its bottom
        /// must fall below -2.1, which with a half-height of 8.8 leaves 0.5 to 6.7. Low in that
        /// window keeps the painting's horizon down near the road's lip, where it reads as
        /// distance rather than as a poster hung behind the set.
        /// </summary>
        private static readonly Vector3 BackdropCentre = new Vector3(0f, 1f, 14f);
        private const string MaterialDir = "Assets/OpenMMORPG/Demo/Materials";
        private const string BackdropMaterial = MaterialDir + "/MenuBackdrop.mat";
        private const string FlamePrefab = "Assets/OpenMMORPG/Demo/Prefabs/Effects/TorchFlame.prefab";
        private const string QuaterniusDir = "Assets/Plugins/Quaternius";

        /// <summary>The root this tool owns. Deleted and rebuilt whole, so nothing accumulates.</summary>
        private const string StageRoot = "MenuStage";

        /// <summary>
        /// Where the previewed character stands. The two character screens instantiate into
        /// `CharacterModelContainer`, which sits at the origin, so the terrace has to bring its
        /// surface up to exactly y=0 rather than the character being moved onto it.
        /// </summary>
        private const float FloorY = 0f;

        private struct Prop
        {
            public string Model;
            /// <summary>Footprint centre. The prop is offset so its bounds land here.</summary>
            public float X, Z;
            public float Yaw;
            public float Scale;
            /// <summary>Lifts a prop off the floor; used for the things that sit on the rim.</summary>
            public float Lift;
        }

        /// <summary>
        /// What stands on the hill.
        ///
        /// The character stands at the origin on the flat top, facing the camera, so the road is
        /// left clear and everything is arranged to frame it: braziers just behind the shoulders
        /// where their light rims the character, the working props on the verges where the UI
        /// panels do not cover them, and the trees out beyond those.
        ///
        /// **`Z` is a depth, not a position - the height comes from <see cref="GroundY"/>.** So a
        /// prop moved down the road is seated on the slope without anyone working out where the
        /// slope is, and nothing can end up hanging in the air, which is what the two versions of
        /// this scene before it managed twice over.
        /// </summary>
        private static readonly Prop[] Props =
        {
            // Braziers flanking the character, close enough that their light reaches them.
            new Prop { Model = "Firepit", X = -2.9f, Z = 0.55f, Yaw = 20f, Scale = 1f },
            new Prop { Model = "Firepit", X = 2.9f, Z = 0.55f, Yaw = -20f, Scale = 1f },

            // More of them down the road, alight, getting smaller and lower as the hill falls
            // away. This is the thing that says "down" - a straight line of equal objects
            // dropping and converging reads as a descent far more plainly than the grade itself,
            // which is only 8 degrees and barely a sixth of the frame.
            new Prop { Model = "Firepit", X = -3.7f, Z = 4.4f, Yaw = 0f, Scale = 0.95f },
            new Prop { Model = "Firepit", X = 3.7f, Z = 5.6f, Yaw = 0f, Scale = 0.95f },
            new Prop { Model = "Firepit", X = -3.8f, Z = 8.2f, Yaw = 0f, Scale = 0.9f },
            new Prop { Model = "Firepit", X = 3.8f, Z = 10.8f, Yaw = 0f, Scale = 0.85f },

            // A soldier's corner on the left, a traveller's on the right, both off the road.
            new Prop { Model = "WeaponStand", X = -4.6f, Z = 1.35f, Yaw = 62f, Scale = 1f },
            new Prop { Model = "Barrel", X = 4.4f, Z = 1.5f, Yaw = 0f, Scale = 1f },
            new Prop { Model = "Crate_Wooden", X = 5.0f, Z = 0.8f, Yaw = -28f, Scale = 0.9f },
            new Prop { Model = "Chest_Wood", X = -5.0f, Z = 0.55f, Yaw = 74f, Scale = 0.8f },

            // A cart pulled onto the verge, pointing down the road - a second line for the eye
            // to follow, and a reason for the road to exist.
            new Prop { Model = "Prop_Wagon", X = 6.4f, Z = 3.6f, Yaw = -8f, Scale = 1f },

            // Trees on the hillside, standing on the ground they are actually on. They shrink and
            // sink on their own as the slope takes them down, which is the whole point of seating
            // by `GroundY` - no lift, no fudging, nothing to check.
            new Prop { Model = "Pine_2", X = -9.2f, Z = 4.5f, Yaw = 30f, Scale = 1.1f },
            new Prop { Model = "Pine_4", X = 9.8f, Z = 5.4f, Yaw = -40f, Scale = 1f },
            new Prop { Model = "Pine_3", X = -13.5f, Z = 7.4f, Yaw = 90f, Scale = 1.05f },
            new Prop { Model = "Pine_5", X = 14.6f, Z = 7.4f, Yaw = -70f, Scale = 0.95f },
            new Prop { Model = "Pine_1", X = -7.6f, Z = 8.6f, Yaw = 140f, Scale = 0.8f },
            new Prop { Model = "CommonTree_2", X = 7.2f, Z = 9.1f, Yaw = 20f, Scale = 0.75f },

            // Scrub along the lip, so the ground does not simply stop in a straight line where
            // the painting takes over.
            // **Nothing here is `Bush_Common`.** Despite the name it is a solid red autumn shrub,
            // and a row of them put the only pure red in the frame across the middle of a dusk
            // painting. Rendering the pack's greenery as a row of swatches settled it in one shot
            // and turned up two more to avoid: `Plant_7` and `Plant_7_Big` are enormous purple
            // leaves, over 6m across even cut down to knee height.
            // **Depths chosen from each model's measured footprint, not by eye.** The ferns are
            // over 4m deep even at half scale, so they sit well back; the low, narrow things go
            // right up to the lip, which is where the cover is actually wanted. The check in
            // `BuildProps` is what keeps this honest when a number here changes.
            new Prop { Model = "Fern_1", X = -4.9f, Z = 8.9f, Yaw = 0f, Scale = 0.5f },
            new Prop { Model = "Fern_1", X = 2.6f, Z = 8.4f, Yaw = 120f, Scale = 0.44f },
            new Prop { Model = "Fern_1", X = 9.6f, Z = 8.0f, Yaw = 210f, Scale = 0.52f },
            new Prop { Model = "Fern_1", X = -15.0f, Z = 7.7f, Yaw = 300f, Scale = 0.55f },
            new Prop { Model = "Bush_Common_Flowers", X = -1.9f, Z = 10.1f, Yaw = 40f, Scale = 0.9f },
            new Prop { Model = "Bush_Common_Flowers", X = -8.6f, Z = 10.1f, Yaw = 75f, Scale = 1.05f },
            new Prop { Model = "Bush_Common_Flowers", X = 13.2f, Z = 10.1f, Yaw = 160f, Scale = 1.1f },
            new Prop { Model = "Grass_Wispy_Tall", X = 6.6f, Z = 10.3f, Yaw = 20f, Scale = 1.1f },
            new Prop { Model = "Grass_Wispy_Tall", X = -12.4f, Z = 10.3f, Yaw = 250f, Scale = 1f },
            new Prop { Model = "Grass_Wispy_Tall", X = 0.9f, Z = 10.35f, Yaw = 90f, Scale = 0.95f },
            new Prop { Model = "Rock_Medium_2", X = 4.6f, Z = 10.4f, Yaw = 200f, Scale = 0.5f, Lift = -0.35f },
            new Prop { Model = "Rock_Medium_1", X = -11.4f, Z = 10.3f, Yaw = 25f, Scale = 0.55f, Lift = -0.4f },
            new Prop { Model = "Rock_Medium_3", X = 17.0f, Z = 10.3f, Yaw = 120f, Scale = 0.45f, Lift = -0.3f },

            // Tufts along the road's edges, breaking the line where cobbles meet grass. Small
            // enough that sitting flat on an 8-degree slope does not show.
            new Prop { Model = "Grass_Common_Tall", X = -3.3f, Z = 2.4f, Yaw = 0f, Scale = 0.55f },
            new Prop { Model = "Grass_Common_Tall", X = 3.35f, Z = 3.3f, Yaw = 140f, Scale = 0.5f },
            new Prop { Model = "Grass_Common_Short", X = -3.25f, Z = 6.3f, Yaw = 60f, Scale = 0.5f },
            new Prop { Model = "Grass_Common_Short", X = 3.3f, Z = 7.6f, Yaw = 20f, Scale = 0.45f },
            new Prop { Model = "Clover_1", X = 3.4f, Z = 0.2f, Yaw = 0f, Scale = 0.6f },
            new Prop { Model = "Clover_1", X = -3.4f, Z = -1.4f, Yaw = 200f, Scale = 0.55f },
            new Prop { Model = "Pebble_Round_2", X = -3.2f, Z = -3.2f, Yaw = 30f, Scale = 1f },
            new Prop { Model = "Pebble_Square_3", X = 3.25f, Z = -2.1f, Yaw = 200f, Scale = 1f },
            new Prop { Model = "Pebble_Square_1", X = -3.3f, Z = 4.9f, Yaw = 90f, Scale = 1f },
        };

        /// <summary>
        /// The children of the stage that a plain build **keeps if they are already
        /// there**, and only creates when they are missing.
        ///
        /// `Terrain` is on the list because the ground is no longer the generator's to
        /// own: the flat quads this used to lay were replaced by a sculpted Unity Terrain,
        /// with a cut road channel and banks either side, which no amount of tuning here
        /// would reproduce. `Road` and `Dressing` are on it because their contents get
        /// nudged by hand once the stage looks nearly right, and a regenerate placing them
        /// from the tables again throws that away.
        ///
        /// This is <see cref="DemoSceneBuilder.FrozenAreaRootNames"/> applied to the menu
        /// scene, and it follows the same rule: a generator fills gaps, it does not paint
        /// over what it finds. Editing the tables in this file therefore needs
        /// **Regenerate Menu Stage**, which says in its name that it destroys hand edits.
        /// </summary>
        private static readonly string[] KeptStageChildren = { "Terrain", "Road", "Dressing" };

        [MenuItem("Open MMORPG/Demo/Build Menu Stage")]
        public static void Build()
        {
            Run(true);
        }

        /// <summary>
        /// Throws the whole stage away and lays it out from the tables again - including
        /// the ground, so **a sculpted terrain is replaced by the flat quads**. The way
        /// back after editing the prop tables, and nothing else.
        /// </summary>
        [MenuItem("Open MMORPG/Demo/Regenerate Menu Stage (destroys hand edits)", priority = 141)]
        public static void Regenerate()
        {
            if (!EditorUtility.DisplayDialog(
                    "Regenerate the menu stage?",
                    "This destroys everything under MenuStage and builds it from the tables again: " +
                    "the Terrain, the Road and the Dressing, with every adjustment made to them by " +
                    "hand. The flat ground quads come back in place of the terrain.\n\n" +
                    "Build Menu Stage keeps all three.",
                    "Regenerate", "Cancel"))
                return;
            Run(false);
        }

        private static void Run(bool keepAuthored)
        {
            bool opened = false;
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            if (!scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
                opened = true;
            }

            // Lifted out to the scene root before the stage is destroyed and put back
            // afterwards, which keeps the objects themselves - their transforms, their
            // components and anything hung off them - rather than rebuilding lookalikes.
            List<Transform> kept = keepAuthored ? Detach(scene) : new List<Transform>();

            Clear(scene);
            var root = new GameObject(StageRoot);
            EditorSceneManager.MoveGameObjectToScene(root, scene);

            var standing = new HashSet<string>();
            foreach (Transform t in kept)
            {
                // worldPositionStays, or everything kept lands at the stage root's origin.
                t.SetParent(root.transform, true);
                standing.Add(t.name);
            }

            // The terrain is the ground when there is one, and it brings its own collider,
            // its own grass and its own shape. Laying the quads underneath it would put a
            // second floor half a metre out of step with the first.
            bool terrain = standing.Contains("Terrain");
            _ground = root.GetComponentInChildren<Terrain>(true);
            if (!terrain)
                BuildGround(root.transform);
            if (!standing.Contains("Road"))
                BuildRoad(root.transform);
            int placed = standing.Contains("Dressing") ? -1 : BuildProps(root.transform);

            bool backdrop = BuildBackdrop(root.transform);
            BuildLighting(root.transform, scene);
            bool music = BuildMusic(root.transform);
            FrameCamera(scene);
            RemoveDeadObjects(scene);
            // Order matters: both touch CanvasHome.prefab, and each loads its own copy of
            // the prefab contents - so the second to run must be the second to load.
            BuildTitle();
            BuildPreviewControls();
            StyleMenuPanels();
            int seated = SeatPanels();
            if (seated > 0)
                Debug.Log($"[{nameof(DemoMenuStageBuilder)}] Seated {seated} panels in the gap between " +
                          "the title band and the button below them.");

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            if (opened)
                EditorSceneManager.CloseScene(scene, true);
            AssetDatabase.SaveAssets();

            string ground = terrain ? "the sculpted terrain" : "flat ground quads";
            string props = placed < 0 ? "the dressing as it stands" : $"{placed} props";
            string held = standing.Count == 0
                ? "Nothing was kept - this was a full regenerate."
                : $"Kept as they stand: {string.Join(", ", new List<string>(standing).ToArray())}. " +
                  "Run Regenerate Menu Stage to build those from the tables again.";
            Debug.Log($"[{nameof(DemoMenuStageBuilder)}] Menu stage built on {ground}, with {props}, " +
                      $"{(music ? "music" : "NO MUSIC")}, {(backdrop ? "backdrop" : "NO BACKDROP")}, " +
                      $"lighting and title. {held}");
        }

        /// <summary>
        /// Lays the cobbles again and touches nothing else.
        ///
        /// `Road` is a kept child, so a plain build leaves it exactly as it stands - which
        /// is what protects a hand-nudged stage and also means the road cannot be retuned
        /// without a way in. This is that way in: it is the only generated part of the
        /// stage anyone iterates on once the terrain is sculpted, and it is cheap to
        /// redo, whereas the dressing is where the hand edits live.
        /// </summary>
        [MenuItem("Open MMORPG/Demo/Rebuild Menu Road", priority = 142)]
        public static void RebuildRoad()
        {
            bool opened = false;
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            if (!scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
                opened = true;
            }

            GameObject stage = null;
            foreach (GameObject go in scene.GetRootGameObjects())
            {
                if (go.name == StageRoot)
                    stage = go;
            }
            if (stage == null)
            {
                Debug.LogError($"[{nameof(DemoMenuStageBuilder)}] No \"{StageRoot}\" in {ScenePath}; " +
                               "run Build Menu Stage first.");
                return;
            }

            _ground = stage.GetComponentInChildren<Terrain>(true);
            Transform old = stage.transform.Find("Road");
            if (old != null)
                Object.DestroyImmediate(old.gameObject);
            BuildRoad(stage.transform);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            if (opened)
                EditorSceneManager.CloseScene(scene, true);
            Debug.Log($"[{nameof(DemoMenuStageBuilder)}] Relaid the road" +
                      $"{(_ground == null ? " on the analytic slope" : " down the terrain")}, " +
                      $"to z={RoadEndZ}. The dressing and the terrain were not touched.");
        }

        /// <summary>
        /// Moves the kept children out of the stage and into the scene root, so that
        /// destroying the stage leaves them alive. They are put back by the caller.
        /// </summary>
        private static List<Transform> Detach(Scene scene)
        {
            var kept = new List<Transform>();
            foreach (GameObject go in scene.GetRootGameObjects())
            {
                if (go.name != StageRoot)
                    continue;
                // Snapshot first: reparenting inside a foreach over the live child list
                // skips every other one.
                var children = new List<Transform>();
                foreach (Transform child in go.transform)
                    children.Add(child);
                foreach (Transform child in children)
                {
                    if (System.Array.IndexOf(KeptStageChildren, child.name) < 0)
                        continue;
                    child.SetParent(null, true);
                    kept.Add(child);
                }
            }
            return kept;
        }



        /// <summary>
        /// The menu theme, looping under every home screen.
        ///
        /// It lives on the stage root rather than on the camera or the canvas because the
        /// stage is the one thing here this tool owns and rebuilds whole; the camera and
        /// CanvasHome are shared with the kit. All five home screens - server list, login,
        /// register, character list, character create - are the same scene, so one source
        /// plays across the lot without restarting as the player moves between them, which
        /// is the whole point of putting it in the scene rather than on a screen.
        ///
        /// It follows the BGM setting through <see cref="MultiplayerARPG.Demo.DemoMusicPlayer"/>;
        /// the source is left silent in the scene file so that a build with the music turned
        /// down does not open at full volume for a frame.
        /// </summary>
        private static bool BuildMusic(Transform parent)
        {
            AudioClip[] tracks = DemoAudioWiring.MusicClips(DemoAudioWiring.MenuMusic);
            if (tracks.Length == 0)
                return false;
            var go = new GameObject("Music");
            go.transform.SetParent(parent, false);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.volume = 0f;
            var music = go.AddComponent<MultiplayerARPG.Demo.DemoMusicPlayer>();
            music.tracks = tracks;
            music.mode = MultiplayerARPG.Demo.DemoMusicPlayer.PlayMode.Continuous;
            music.volume = DemoAudioWiring.MenuMusicVolume;
            return true;
        }


        // ------------------------------------------------------------------
        // The title
        // ------------------------------------------------------------------

        private const string GlobalCanvasPath = "Assets/OpenMMORPG/Demo/Prefabs/UI/Global/CanvasGlobal.prefab";
        private const string TextureDir = "Assets/OpenMMORPG/Demo/Textures";

        /// <summary>The artwork: the ring, the wordmark and the strapline, on transparency.</summary>
        private const string TitleSpritePath = TextureDir + "/OpenMMORPG-title.png";

        /// <summary>The scrim generated behind it. Not hand art - see <see cref="EnsureBandSprite"/>.</summary>
        private const string BandSpritePath = TextureDir + "/MenuTitleBand.png";

        /// <summary>
        /// Left over from the TMP wordmark the artwork replaced, and deleted on sight so a
        /// rebuild does not leave an unreferenced material behind in the demo's own folder.
        /// </summary>
        private const string DeadTitleMaterial = MaterialDir + "/MenuTitleText.mat";

        /// <summary>
        /// The title block's slice of the canvas, in its 800x600 reference.
        ///
        /// **The vertical budget is exactly 600 at every aspect ratio**, because the scaler
        /// matches on height (`m_MatchWidthOrHeight: 1`); a wider screen only ever buys more
        /// width. So these can be fixed numbers rather than fractions. The centred login window
        /// reaches y +125, which is 175 down from the top edge, and the band bottoms out at 162 -
        /// the clearance holds on any monitor.
        ///
        /// <see cref="InkHeight"/> is the height of the **inked part** of the artwork, not of the
        /// image: the PNG is 1664x608 with the logo occupying the middle 885x509 of it, so sizing
        /// the `Image` to these numbers directly would draw it at two thirds the intended size
        /// and leave the strapline too small to read. The margins are measured instead - see
        /// <see cref="MeasureInk"/> - and the `Image` scaled up and re-centred around them.
        /// </summary>
        private const float BandTop = -4f;
        private const float BandPadding = 13f;
        private const float InkHeight = 132f;
        private const float BandHeight = InkHeight + (BandPadding * 2f);

        /// <summary>
        /// How much wider than the artwork the scrim reaches, before its sides fade out.
        ///
        /// The scrim is **not** stretched across the screen. Full width meant its left and right
        /// ends were cut off by the screen edges, which is the one place a soft scrim shows a
        /// hard edge - and on a wide monitor it read as a header bar rather than as something
        /// sitting behind the logo. A fixed width, centred, keeps it the same shape on every
        /// display.
        ///
        /// Multiplied by the **inked** width rather than set in pixels, so it follows the
        /// artwork. <see cref="HorizontalFade"/> spends 30% of that width on each side's fade,
        /// leaving a solid middle of 40% - so 3.1 puts a core about a quarter wider than the
        /// logo behind it, and all the taper outside.
        /// </summary>
        private const float BandSpread = 3.1f;

        /// <summary>
        /// The screens the title belongs on: everything before the player reaches their
        /// characters.
        ///
        /// The two it is missing from - `UICharacterList` and `UICharacterCreate` - are the ones
        /// with their own furniture in the corners: a Back button top-left on both, and on create
        /// a column of panels down each side. They are also the screens where the character, not
        /// the game's name, is the thing to look at.
        /// </summary>
        private static readonly string[] TitleScreens =
        {
            "UIServerList", "UILogin", "UIRegister", "UIChannelList",
        };

        private static void BuildTitle()
        {
            // An earlier build put the title on CanvasGlobal, which showed it on every home
            // screen. Clear that out before writing the new ones.
            //
            // `Logo` goes with it, and for the same reason: it was the mark and the version
            // string in the bottom-left corner, drawn over every home screen from a canvas no
            // screen's own layout can see - which is how it ended up underneath the create
            // screen's Back button. It is gone rather than moved; a version readout that is
            // wanted back belongs on a screen that owns its own corners, and is one line of
            // `Application.version`.
            GameObject global = PrefabUtility.LoadPrefabContents(GlobalCanvasPath);
            try
            {
                bool stripped = false;
                foreach (string name in new[] { "Title", "Logo" })
                {
                    Transform stale = global.transform.Find(name);
                    if (stale == null)
                        continue;
                    Object.DestroyImmediate(stale.gameObject);
                    stripped = true;
                }
                if (stripped)
                    PrefabUtility.SaveAsPrefabAsset(global, GlobalCanvasPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(global);
            }

            AssetDatabase.DeleteAsset(DeadTitleMaterial);

            // Both of these reimport textures, which is not something to start with a prefab's
            // contents open - and if the artwork is missing there is nothing to do anyway, so
            // settle it before the prefab is touched at all rather than opening it, stripping
            // the old title and bailing out with the menu worse off than it started.
            Sprite logo = EnsureTitleSprite();
            if (logo == null)
            {
                Debug.LogError($"[{nameof(DemoMenuStageBuilder)}] No title artwork at \"{TitleSpritePath}\"; title left as it was.");
                return;
            }
            Rect ink = MeasureInk(TitleSpritePath);
            Sprite band = EnsureBandSprite();

            GameObject canvas = PrefabUtility.LoadPrefabContents(HomeCanvasPath);
            try
            {
                // Strip every screen first, then add to the listed ones. That way the list is the
                // whole truth: moving a screen out of it actually removes the title, rather than
                // leaving one behind from an earlier run.
                foreach (Transform screen in canvas.transform)
                {
                    Transform existing = screen.Find("Title");
                    if (existing != null)
                        Object.DestroyImmediate(existing.gameObject);
                }

                int added = 0;
                foreach (string screenName in TitleScreens)
                {
                    Transform screen = canvas.transform.Find(screenName);
                    if (screen == null)
                    {
                        Debug.LogWarning($"[{nameof(DemoMenuStageBuilder)}] No \"{screenName}\" in the home canvas to hang the title on.");
                        continue;
                    }
                    AddTitle(screen, logo, band, ink);
                    ++added;
                }
                PrefabUtility.SaveAsPrefabAsset(canvas, HomeCanvasPath);
                Debug.Log($"[{nameof(DemoMenuStageBuilder)}] Title placed on {added} screens: {string.Join(", ", TitleScreens)}.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(canvas);
            }
        }

        /// <summary>
        /// One copy of the title, inside one screen: the artwork on a soft scrim.
        ///
        /// A copy per screen rather than a single shared one, because each of these screens is a
        /// full-screen container the kit switches with `SetActive` - so a child simply inherits
        /// the right visibility, with no script, no `OnEnable`/`OnDisable` wiring and no reference
        /// reaching out of the prefab. The duplication is only in the scene graph; the builder
        /// above is still the one place the title is described.
        ///
        /// First child, so that if a window ever grew tall enough to reach the title, the window
        /// would draw over it rather than the other way round.
        ///
        /// **The scrim is the parent and the artwork its child**, which is what keeps the two
        /// together: the logo stays centred in the band whatever either is sized to, and moving
        /// the block is one number. Both are centred on the top edge, and the band's width comes
        /// from the artwork's, so the pair holds together at any aspect ratio.
        ///
        /// Neither graphic is a raycast target. A full-width band that ate clicks would swallow
        /// anything the kit ever put under it, and there is nothing here to click.
        /// </summary>
        private static void AddTitle(Transform screen, Sprite logo, Sprite band, Rect ink)
        {
            var title = new GameObject("Title", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            title.transform.SetParent(screen, false);
            title.transform.SetSiblingIndex(0);

            // Scale the artwork up until its *inked* part is InkHeight tall, then slide the
            // image so that part - rather than the file's own middle - sits in the band's middle.
            float height = InkHeight / ink.height;
            float width = height * (logo.rect.width / logo.rect.height);

            var rect = title.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, BandTop);
            rect.sizeDelta = new Vector2(width * ink.width * BandSpread, BandHeight);

            var frame = title.GetComponent<UnityEngine.UI.Image>();
            frame.sprite = band;
            frame.type = UnityEngine.UI.Image.Type.Simple;
            // Near-black rather than a colour: the backdrop painting is a warm sunset and the
            // logo is cold teal, so anything tinted lands on one side or the other.
            //
            // **Alpha here is not the fraction of light it takes away.** The project renders in
            // linear colour space, so the blend happens in linear light and the result is
            // re-encoded to sRGB on the way out: alpha `a` of black leaves `1-a` of the light,
            // which displays at `(1-a)^(1/2.2)`. The curve is steep at the top and almost flat
            // lower down, which is why the first two attempts at this vanished:
            //
            //   a = 0.50 -> 73% as bright   (reads as no band at all)
            //   a = 0.62 -> 63%             (still barely there)
            //   a = 0.72 -> 56%
            //   a = 0.86 -> 41%             (what this was, and too heavy)
            //
            // 0.72 keeps the logo legible against a bright sunset while letting the painting
            // show through the band instead of blacking it out.
            frame.color = new Color(0.03f, 0.04f, 0.06f, 0.72f);
            frame.raycastTarget = false;

            // The two lavender hairlines off the mock-up, top and bottom. Children of the
            // band, so they inherit its width and its position and cannot drift from it.
            Sprite rule = EnsureRuleSprite();
            if (rule != null)
            {
                AddRule(title.transform, rule, "RuleTop", 1f);
                AddRule(title.transform, rule, "RuleBottom", 0f);
            }

            var mark = new GameObject("Logo", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            mark.transform.SetParent(title.transform, false);

            var markRect = mark.GetComponent<RectTransform>();
            markRect.anchorMin = new Vector2(0.5f, 0.5f);
            markRect.anchorMax = new Vector2(0.5f, 0.5f);
            markRect.pivot = new Vector2(0.5f, 0.5f);
            markRect.anchoredPosition = new Vector2(
                -(ink.center.x - 0.5f) * width,
                -(ink.center.y - 0.5f) * height);
            markRect.sizeDelta = new Vector2(width, height);

            var image = mark.GetComponent<UnityEngine.UI.Image>();
            image.sprite = logo;
            image.preserveAspect = true;
            image.raycastTarget = false;
        }

        /// <summary>
        /// The title artwork, as a <see cref="Sprite"/> the UI can actually use.
        ///
        /// A PNG dropped into the project is **not** a sprite by default, and this one arrived as
        /// texture type Sprite with import mode *Multiple* and an empty sheet - which looks right
        /// in the inspector and loads as `null`, because Multiple publishes only the sub-sprites
        /// the sprite editor was used to slice, and none were. So the importer is corrected here
        /// rather than left to whoever next opens the project.
        ///
        /// **CompressedHQ**, not the default: BC7 rather than DXT5. The logo is one large soft
        /// glow, which is the exact case DXT's three-colour-per-block palette bands badly, and at
        /// 1664x608 the difference is about a megabyte.
        /// </summary>
        private static Sprite EnsureTitleSprite()
        {
            if (!File.Exists(TitleSpritePath))
                return null;

            var importer = AssetImporter.GetAtPath(TitleSpritePath) as TextureImporter;
            if (importer != null &&
                (importer.textureType != TextureImporterType.Sprite ||
                 importer.spriteImportMode != SpriteImportMode.Single ||
                 !importer.alphaIsTransparency ||
                 importer.textureCompression != TextureImporterCompression.CompressedHQ))
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(TitleSpritePath);
        }

        /// <summary>
        /// One axis of the scrim's alpha: 0 at either edge, 1 across the middle, smoothstepped in
        /// and out over <paramref name="fade"/> of the whole span.
        ///
        /// A `fade` of zero means **no ramp on this axis** - square edges, full alpha to the
        /// last row. It has to be spelt out, because `Mathf.InverseLerp(0, 0, x)` returns 0
        /// rather than 1, so falling through would make the whole texture transparent.
        /// </summary>
        private static float Falloff(int index, int span, float fade)
        {
            if (fade <= 0f)
                return 1f;
            float toEdge = Mathf.Min(index, span - 1 - index) / (float)(span - 1);
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, fade, toEdge));
        }

        /// <summary>Byte-for-byte, so a generated file is only rewritten when it really differs.</summary>
        private static bool Same(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
                return false;
            for (int i = 0; i < left.Length; ++i)
                if (left[i] != right[i])
                    return false;
            return true;
        }

        /// <summary>
        /// Where the artwork's visible pixels are, as a fraction of the image, measured bottom-up
        /// the way texture coordinates run.
        ///
        /// Measured rather than trusted, because a logo exported from a paint package carries
        /// whatever canvas it was drawn on: this one is 53% ink across and 84% down, so an
        /// `Image` sized to the file draws the logo at half the width it was asked for, off
        /// centre, with a strapline nobody can read.
        ///
        /// **Decoded from the file's own bytes** rather than read off the imported texture, which
        /// would need `isReadable` turned on - and that is a runtime cost (a permanent second
        /// copy of the texture in system memory) paid for an editor-time measurement. `LoadImage`
        /// gives a readable texture that belongs to nobody and is thrown away on the next line.
        /// </summary>
        private static Rect MeasureInk(string path)
        {
            var whole = new Rect(0f, 0f, 1f, 1f);
            if (!File.Exists(path))
                return whole;

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!texture.LoadImage(File.ReadAllBytes(path)))
                    return whole;

                Color32[] pixels = texture.GetPixels32();
                int width = texture.width;
                int height = texture.height;
                int left = width, right = -1, bottom = height, top = -1;
                for (int y = 0; y < height; ++y)
                {
                    for (int x = 0; x < width; ++x)
                    {
                        // A threshold, not "any alpha at all": the glow around the ring trails
                        // off into single-digit alpha that reaches further than anything the eye
                        // registers, and measuring to it would undo most of the point.
                        if (pixels[(y * width) + x].a <= 8)
                            continue;
                        if (x < left) left = x;
                        if (x > right) right = x;
                        if (y < bottom) bottom = y;
                        if (y > top) top = y;
                    }
                }
                if (right < left || top < bottom)
                    return whole;
                return Rect.MinMaxRect(left / (float)width, bottom / (float)height,
                                       (right + 1f) / width, (top + 1f) / height);
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        /// <summary>
        /// The scrim behind the artwork: white, with its alpha falling away top and bottom.
        ///
        /// Generated rather than drawn, because it is a gradient and nothing else - and because a
        /// checked-in PNG is one more thing that can go missing. The `Image` tints it, so the
        /// colour lives in <see cref="AddTitle"/> where it can be read next to the rest of the
        /// palette.
        ///
        /// **The fade is the whole point.** A flat rectangle over a painted backdrop reads as a
        /// letterbox bar - a hard edge the painting does not have anywhere else, and the screen
        /// edge would cut the left and right ends square. So it fades on all four sides and the
        /// scrim has no edge anywhere.
        ///
        /// **The vertical ramp is zero: the band is square top and bottom.** It used to fade
        /// over a tenth of its height at each end, which is right for a scrim with nothing
        /// drawn on its edge - but the two lavender rules are drawn exactly there, and a rule
        /// pinned to the rect's edge sat out where the scrim had already faded to nothing,
        /// leaving a band of empty sky between the dark and the line. The rules are the edge
        /// treatment now, so the scrim can stop dead against them, which is also how the
        /// mock-up reads: a hard step at the top, not a gradient.
        ///
        /// **Three tenths** of the width still, because sideways there is nothing to protect
        /// and the scrim should be well gone before it reaches anything else on the screen -
        /// and the rules taper on the same curve, so band and lines run out together.
        ///
        /// Uncompressed, for the same reason BC7 was chosen for the logo only more so: a block
        /// compressor on a pure ramp is all banding, and 128KB is not worth saving.
        /// </summary>
        private const float VerticalFade = 0f;
        private const float HorizontalFade = 0.3f;

        private static Sprite EnsureBandSprite()
        {
            DemoItemBuilder.EnsureFolder(TextureDir);
            const int width = 256;
            const int height = 128;

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; ++y)
            {
                float down = Falloff(y, height, VerticalFade);
                for (int x = 0; x < width; ++x)
                {
                    // The two ramps multiplied, which rounds the corners off for free: a corner
                    // is the only place both are short of 1, so it arrives at the lower value
                    // of the pair without any second shape having to be drawn.
                    byte alpha = (byte)Mathf.RoundToInt(down * Falloff(x, width, HorizontalFade) * 255f);
                    pixels[(y * width) + x] = new Color32(255, 255, 255, alpha);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            byte[] png = texture.EncodeToPNG();
            Object.DestroyImmediate(texture);

            // Written only when it would actually change. Regenerating unconditionally means an
            // asset reimport on every build of the menu; early-returning on "the file exists"
            // means editing the ramp above silently does nothing, which is how the first version
            // of this shipped the wrong gradient.
            if (!File.Exists(BandSpritePath) || !Same(File.ReadAllBytes(BandSpritePath), png))
            {
                File.WriteAllBytes(BandSpritePath, png);
                AssetDatabase.ImportAsset(BandSpritePath, ImportAssetOptions.ForceUpdate);
            }
            else
            {
                var current = AssetDatabase.LoadAssetAtPath<Sprite>(BandSpritePath);
                if (current != null)
                    return current;
            }

            var importer = (TextureImporter)AssetImporter.GetAtPath(BandSpritePath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(BandSpritePath);
        }



        /// <summary>The hairline generated for the band's two edges. See <see cref="EnsureRuleSprite"/>.</summary>
        private const string RuleSpritePath = TextureDir + "/MenuTitleRule.png";

        /// <summary>
        /// How tall the edge rules are, in the canvas's 600-unit reference height.
        ///
        /// A hairline, and it has to be specified in reference units rather than pixels
        /// because the scaler matches on height: 1.5 here is about 2.7 real pixels at
        /// 1080p and scales with the window, so it stays a hairline on a 4K monitor
        /// instead of vanishing.
        /// </summary>
        private const float RuleHeight = 1.5f;

        /// <summary>
        /// The colour of those rules, measured off the mock-up rather than picked.
        ///
        /// In the reference the line reads as rgb(127,113,165) where it crosses the band's
        /// own rgb(66,62,84), which is a light lavender laid over it at about half alpha.
        /// It is the one warm-cold-neutral break in the title block: the scrim is
        /// near-black so it takes no side between the sunset behind and the teal logo in
        /// front, and these two lines are what stop that reading as a plain grey bar.
        ///
        /// **Carried above the measured half-alpha, to 0.7.** The mock-up's band is darker
        /// than this one, so a line matched to it exactly reads well along the top - where
        /// the sky behind is mid-tone - and all but vanishes along the bottom, which
        /// crosses the bright part of the sunset. The higher alpha is what makes one line
        /// look like the other across the whole width.
        /// </summary>
        private static readonly Color RuleColour = new Color(0.70f, 0.60f, 0.92f, 0.7f);

        /// <summary>
        /// A one-dimensional version of the band: the same horizontal fade, no vertical
        /// one, so the rules taper off at the ends exactly where the scrim behind them
        /// does and neither finishes in a visible stop.
        ///
        /// Its own sprite rather than the band's, because the band's vertical ramp would
        /// be squeezed into a 1.5-unit-tall rect and dim the line unpredictably - it
        /// would be at the mercy of a constant that exists to soften a 158-unit scrim.
        /// </summary>
        private static Sprite EnsureRuleSprite()
        {
            DemoItemBuilder.EnsureFolder(TextureDir);
            const int width = 256;
            const int height = 4;

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var pixels = new Color32[width * height];
            for (int x = 0; x < width; ++x)
            {
                byte alpha = (byte)Mathf.RoundToInt(Falloff(x, width, HorizontalFade) * 255f);
                for (int y = 0; y < height; ++y)
                    pixels[(y * width) + x] = new Color32(255, 255, 255, alpha);
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            byte[] png = texture.EncodeToPNG();
            Object.DestroyImmediate(texture);

            // Written only when it would actually change - see EnsureBandSprite for why
            // neither "always" nor "only if missing" is right.
            if (!File.Exists(RuleSpritePath) || !Same(File.ReadAllBytes(RuleSpritePath), png))
            {
                File.WriteAllBytes(RuleSpritePath, png);
                AssetDatabase.ImportAsset(RuleSpritePath, ImportAssetOptions.ForceUpdate);
            }
            else
            {
                var current = AssetDatabase.LoadAssetAtPath<Sprite>(RuleSpritePath);
                if (current != null)
                    return current;
            }

            var importer = (TextureImporter)AssetImporter.GetAtPath(RuleSpritePath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(RuleSpritePath);
        }

        /// <summary>
        /// One of the band's two edge rules, stretched across its full width and pinned to
        /// the given edge. `edge` is 1 for the top and 0 for the bottom - the same number
        /// serves as the anchor and the pivot, so the line sits just inside the band
        /// rather than straddling its boundary.
        /// </summary>
        private static void AddRule(Transform band, Sprite sprite, string name, float edge)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Image));
            go.transform.SetParent(band, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, edge);
            rect.anchorMax = new Vector2(1f, edge);
            rect.pivot = new Vector2(0.5f, edge);
            rect.anchoredPosition = Vector2.zero;
            // Zero width delta against stretched anchors means "exactly the parent's width".
            rect.sizeDelta = new Vector2(0f, RuleHeight);

            var image = go.GetComponent<UnityEngine.UI.Image>();
            image.sprite = sprite;
            image.type = UnityEngine.UI.Image.Type.Simple;
            image.color = RuleColour;
            image.raycastTarget = false;
        }

        // ------------------------------------------------------------------
        // Panel styling
        // ------------------------------------------------------------------

        private const string HomeCanvasPath = "Assets/OpenMMORPG/Demo/Prefabs/UI/Home/CanvasHome.prefab";

        /// <summary>
        /// The palette lives in <see cref="DemoPalette"/>, because the HUD builder paints with the
        /// same colours and a value that lives in only one of them drifts the first time either is
        /// retuned. Aliased here so the rest of this file reads unchanged.
        /// </summary>
        private static readonly Color PlaceholderMagenta = DemoPalette.PlaceholderMagenta;
        private static readonly Color HeaderColour = DemoPalette.Header;
        private static readonly Color PanelColour = DemoPalette.Panel;
        private static readonly Color FieldColour = DemoPalette.Field;
        private static readonly Color KitGreen = DemoPalette.KitGreen;
        private static readonly Color KitDeepGreen = DemoPalette.KitDeepGreen;
        private static readonly Color ActionTeal = DemoPalette.ActionTeal;
        private static readonly Color CommitTeal = DemoPalette.CommitTeal;

        /// <summary>
        /// Repaints the home menu's panels to suit the scene behind them.
        ///
        /// The template shipped **twelve** window headers in `RGBA(0.71, 0, 1)` - a placeholder
        /// magenta - and every panel body in clinical white. Against a sunset painting that reads
        /// as an unfinished screen more than anything else on it.
        ///
        /// Deliberately a **repaint, not a redesign**: only the fill colours move, and only
        /// towards warm neutrals that keep every existing text colour legible. The panels stay
        /// light because their labels are near-black - flipping them to a dark fantasy panel
        /// would mean restyling every label in five screens to keep contrast, which is a much
        /// larger change than was asked for and much easier to get subtly wrong.
        ///
        /// The buttons' **greens** move to teal - see <see cref="ActionTeal"/> for why - but the
        /// **red** Delete button does not. Red is the one colour here that genuinely carries
        /// meaning rather than style, and it means the same thing in every game ever made. The
        /// greens did not: this pass only ever touches `CanvasHome`, so the confirm-and-cancel
        /// semantics that green really does carry live on the in-game canvases, which it never
        /// sees. On these six screens green was on Login, Connect and Register - primary actions,
        /// not confirmations.
        /// </summary>
        /// <summary>
        /// Centres each title screen's panel in the space between the band above it and the
        /// button below it.
        ///
        /// **The home canvas is 600 units tall on every screen there is** - the scaler matches
        /// height against an 800x600 reference - so this is not a guess that happens to work at
        /// one resolution. The band takes the top 162 of those units and the Exit/Disconnect
        /// button the bottom 70, which leaves 368 for a panel that is 250 or 284 tall. Centred in
        /// what is left, a panel clears the band by 42 units at worst.
        ///
        /// It was not centred there before, it was centred on the *whole screen*, and 300 is far
        /// enough up that a 284-tall panel reached four units **inside** the band - they touched.
        /// That went unnoticed while the panels were 250 tall, with 13 units to spare, and showed
        /// up the moment <see cref="DemoUiSkin"/> stretched two of them to fit their own row
        /// spacing. Measuring the gap rather than nudging the panel down by a number means the
        /// next panel to change height seats itself correctly without anyone noticing it had to.
        ///
        /// Run **after** `Skin UI` if that has resized a window, since it is the window's height
        /// this is centring.
        /// </summary>
        private static int SeatPanels()
        {
            GameObject canvas = PrefabUtility.LoadPrefabContents(HomeCanvasPath);
            try
            {
                var scaler = canvas.GetComponent<UnityEngine.UI.CanvasScaler>();
                float height = scaler == null ? 600f : scaler.referenceResolution.y;
                // The band hangs from the top of the screen: BandTop is negative, hence the minus.
                float ceiling = height - (-BandTop + BandHeight);

                int seated = 0;
                foreach (string screenName in TitleScreens)
                {
                    Transform screen = canvas.transform.Find(screenName);
                    if (screen == null)
                        continue;

                    // The floor is whatever is pinned to the bottom of the screen - Exit,
                    // Disconnect, Logout. Read rather than assumed: they are 40 tall at y=30
                    // today and the panel should not have to be re-centred by hand if that moves.
                    float floor = 0f;
                    RectTransform panel = null;
                    foreach (Transform child in screen)
                    {
                        var rect = child as RectTransform;
                        if (rect == null)
                            continue;
                        if (child.name.StartsWith("Window"))
                            panel = rect;
                        else if (rect.anchorMin.y == 0f && rect.anchorMax.y == 0f)
                            floor = Mathf.Max(floor, rect.anchoredPosition.y + ((1f - rect.pivot.y) * rect.rect.height));
                    }
                    if (panel == null)
                        continue;

                    float span = ceiling - floor;
                    if (panel.rect.height > span)
                    {
                        Debug.LogWarning($"[{nameof(DemoMenuStageBuilder)}] \"{screenName}\" panel is " +
                                         $"{panel.rect.height:0} tall but only {span:0} units are free " +
                                         "between the title band and the button under it; centring it anyway.");
                    }

                    float centre = floor + (span * 0.5f);
                    float wanted = centre - (panel.rect.height * 0.5f) + (panel.pivot.y * panel.rect.height)
                                 - (panel.anchorMin.y * height);
                    if (Mathf.Abs(wanted - panel.anchoredPosition.y) < 0.5f)
                        continue;
                    panel.anchoredPosition = new Vector2(panel.anchoredPosition.x, wanted);
                    ++seated;
                }
                if (seated > 0)
                    PrefabUtility.SaveAsPrefabAsset(canvas, HomeCanvasPath);
                return seated;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(canvas);
            }
        }

        private static void StyleMenuPanels()
        {
            GameObject canvas = PrefabUtility.LoadPrefabContents(HomeCanvasPath);
            try
            {
                int headers = 0, panels = 0, fields = 0, buttons = 0;
                foreach (UnityEngine.UI.Image image in canvas.GetComponentsInChildren<UnityEngine.UI.Image>(true))
                {
                    // Headers: matched by **where they sit** - a "Title" under a "Window..."
                    // parent - with the placeholder colour only as a fallback for a fresh template.
                    //
                    // Colour matching alone works exactly once. After the first repaint there is no
                    // magenta left to find, so every later change to the bronze silently does
                    // nothing; this was written to change the colour and changed nothing at all
                    // until the match moved off colour.
                    //
                    // Name alone is not enough either: `BuildTitle` hangs the **logo** on an object
                    // also called "Title". That one is a direct child of the screen rather than of
                    // a window, which is what separates the twelve headers from the four logos.
                    Transform holder = image.transform.parent;
                    bool isHeader = image.name == "Title" && holder != null && holder.name.StartsWith("Window");
                    if (isHeader || Approximately(image.color, PlaceholderMagenta)
                        || DemoPalette.SameAny(image.color, DemoPalette.KnownHeaders))
                    {
                        image.color = HeaderColour;
                        ++headers;
                        continue;
                    }
                    // Panel bodies and the plain buttons that share their white.
                    if ((Approximately(image.color, Color.white) &&
                        (image.name.StartsWith("Window") || image.name.StartsWith("Button")))
                        || DemoPalette.SameAny(image.color, DemoPalette.KnownPanels))
                    {
                        image.color = PanelColour;
                        ++panels;
                        continue;
                    }
                    // Input fields, a shade down from the panel so they still read as wells.
                    if (Approximately(image.color, new Color(0.878f, 0.878f, 0.878f))
                        || DemoPalette.SameAny(image.color, DemoPalette.KnownFields))
                    {
                        image.color = FieldColour;
                        ++fields;
                        continue;
                    }
                    if (Approximately(image.color, KitGreen))
                    {
                        image.color = ActionTeal;
                        ++buttons;
                        continue;
                    }
                    if (Approximately(image.color, KitDeepGreen))
                    {
                        image.color = CommitTeal;
                        ++buttons;
                    }
                }
                PrefabUtility.SaveAsPrefabAsset(canvas, HomeCanvasPath);
                Debug.Log($"[{nameof(DemoMenuStageBuilder)}] Repainted the menu: {headers} placeholder-magenta " +
                          $"headers, {panels} panels, {fields} input fields, {buttons} action buttons " +
                          "moved from the kit's green onto the logo's teal. The red Delete button is untouched.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(canvas);
            }
        }

        private static bool Approximately(Color a, Color b)
        {
            return Mathf.Abs(a.r - b.r) < 0.02f && Mathf.Abs(a.g - b.g) < 0.02f
                && Mathf.Abs(a.b - b.b) < 0.02f && Mathf.Abs(a.a - b.a) < 0.02f;
        }


        // ------------------------------------------------------------------
        // Turning and zooming the previewed character
        // ------------------------------------------------------------------

        /// <summary>
        /// The two character screens, and whether the player may turn and zoom on each.
        ///
        /// **Both get the component; only creation gets the mouse.** It is the component that asks
        /// the rig for the close framing, so leaving it off the selection screen would hand that
        /// screen the wide menu lens. On selection the character is a row you are flipping
        /// through, and it should read the same every time you land on it - square-on, at the
        /// resting distance. On creation it is the thing you are making, and turning it to see the
        /// back of a helmet is the point.
        /// </summary>
        private static readonly KeyValuePair<string, bool>[] PreviewScreens =
        {
            new KeyValuePair<string, bool>("UICharacterList", false),
            new KeyValuePair<string, bool>("UICharacterCreate", true),
        };

        /// <summary>
        /// Gives the character screens a transparent full-screen pad that catches drags and
        /// scrolls, carrying <see cref="DemoCharacterPreviewControl"/>.
        ///
        /// **First child on purpose.** uGUI sends a pointer event to the topmost graphic under
        /// the cursor and later siblings draw on top, so putting the pad first means every panel
        /// on these screens - the character list, the body-part columns, the buttons - keeps its
        /// own drags, and the pad only hears the ones that land on the character. That is what
        /// removes any need to test "is the pointer over UI", which is the usual way this goes
        /// wrong.
        ///
        /// The Image is fully transparent but **must still be a raycast target** where the pad is
        /// meant to hear anything: alpha does not affect hit testing, an Image with no sprite still
        /// rasterises a quad for the raycaster, and without a Graphic there is nothing for uGUI to
        /// hit at all. On the screen that takes no input it is switched off instead, so the pad
        /// does not sit in front of that screen swallowing pointer events for nothing.
        /// </summary>
        private static void BuildPreviewControls()
        {
            GameObject canvas = PrefabUtility.LoadPrefabContents(HomeCanvasPath);
            try
            {
                int added = 0;
                foreach (Transform screen in canvas.transform)
                {
                    Transform stale = screen.Find("PreviewControl");
                    if (stale != null)
                        Object.DestroyImmediate(stale.gameObject);
                }
                foreach (KeyValuePair<string, bool> entry in PreviewScreens)
                {
                    string screenName = entry.Key;
                    bool interactive = entry.Value;
                    Transform screen = canvas.transform.Find(screenName);
                    if (screen == null)
                    {
                        Debug.LogWarning($"[{nameof(DemoMenuStageBuilder)}] No \"{screenName}\" to give preview controls to.");
                        continue;
                    }
                    var pad = new GameObject("PreviewControl",
                        typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(DemoCharacterPreviewControl));
                    pad.transform.SetParent(screen, false);
                    pad.transform.SetSiblingIndex(0);
                    // No framings written here: this asks the rig on the camera for one rather
                    // than applying it, and a prefab cannot hold a reference to a scene object
                    // anyway, so it finds the rig through the camera at runtime.

                    var rect = pad.GetComponent<RectTransform>();
                    rect.anchorMin = Vector2.zero;
                    rect.anchorMax = Vector2.one;
                    rect.pivot = new Vector2(0.5f, 0.5f);
                    rect.offsetMin = Vector2.zero;
                    rect.offsetMax = Vector2.zero;

                    pad.GetComponent<DemoCharacterPreviewControl>().SetPlayerControl(interactive);

                    var image = pad.GetComponent<UnityEngine.UI.Image>();
                    image.color = new Color(0f, 0f, 0f, 0f);
                    image.raycastTarget = interactive;
                    ++added;
                }
                PrefabUtility.SaveAsPrefabAsset(canvas, HomeCanvasPath);
                var report = new System.Text.StringBuilder();
                foreach (KeyValuePair<string, bool> entry in PreviewScreens)
                    report.Append(entry.Key).Append(entry.Value ? " (turn and zoom)" : " (locked to default)").Append(" ");
                Debug.Log($"[{nameof(DemoMenuStageBuilder)}] Preview pads on {added} screens: {report}");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(canvas);
            }
        }

        /// <summary>Wipes the previous stage so a rebuild replaces rather than stacks.</summary>
        private static void Clear(Scene scene)
        {
            foreach (GameObject go in scene.GetRootGameObjects())
            {
                if (go.name == StageRoot)
                    Object.DestroyImmediate(go);
            }
        }

        // ------------------------------------------------------------------
        // The hill
        // ------------------------------------------------------------------

        private const string GrassTexture = "Assets/OpenMMORPG/Demo/Textures/Island_Grass.png";
        private const string GroundMaterial = MaterialDir + "/MenuGround.mat";

        /// <summary>Where the flat top of the hill ends and the road starts down.</summary>
        private const float BrowZ = 1.5f;

        /// <summary>
        /// The road's fall, in metres of drop per metre of depth. 0.14 is a shade under 8 degrees.
        ///
        /// **It has to stay under `eyeY / (BrowZ - eyeZ)`** - 1.55/4.9, or 0.316 - or the slope
        /// falls away faster than the sightline that grazes the brow and the whole descent is
        /// hidden behind its own crest. That is the ceiling; the floor is that a gentler road
        /// shows *more* of itself, because it stays nearer the line of sight. At 8 degrees the
        /// descent is about 15% of the frame's height from the menu camera. At 14 it drops to 4%,
        /// which looks like a step rather than a road.
        /// </summary>
        private const float Grade = 0.14f;

        /// <summary>2m rows, measured down the slope rather than across the map.</summary>
        private const int SlopeRows = 5;
        private const int FlatRows = 4;

        /// <summary>
        /// Wide enough that the grass runs past the frame at the lip, which is the furthest the
        /// eye can see it: 15m from the menu camera a 55-degree frame is 13.9m to each side at
        /// 16:9 and 18.2m at 21:9.
        /// </summary>
        private const float GroundHalfWidth = 22f;

        private static float SlopeAngle => Mathf.Atan(Grade) * Mathf.Rad2Deg;

        /// <summary>Depth gained and height lost by one 2m row of road on the slope.</summary>
        private static float RowStep => 2f * Mathf.Cos(SlopeAngle * Mathf.Deg2Rad);
        private static float RowDrop => 2f * Mathf.Sin(SlopeAngle * Mathf.Deg2Rad);

        /// <summary>Where the ground runs out. Past this there is only the painting.</summary>
        private static float LipZ => BrowZ + (SlopeRows * RowStep);

        /// <summary>How much ground a prop must keep behind it. Enough that a shadow has somewhere to fall.</summary>
        private const float LipMargin = 0.3f;

        /// <summary>
        /// The height of the ground at a given depth: flat to the brow, then falling away.
        ///
        /// Everything the builder places is seated on this rather than on y=0, which is what lets
        /// props stand on the slope without being positioned by hand - and what stops the pines
        /// floating, which is how this scene spent its first two versions.
        /// </summary>
        private static float GroundY(float z)
        {
            return z <= BrowZ ? 0f : -(z - BrowZ) * Grade;
        }

        /// <summary>
        /// The grass either side of the road: two quads, one flat and one tipped down the slope.
        ///
        /// Quads rather than tiles because this is 44m across and nothing but a texture - a
        /// Village floor tile is 2m, and paving the verges would be 200 draw calls to say
        /// "grass". The road on top is tiles, because there it is the cobbles that do the work.
        ///
        /// A Unity Quad faces its local -Z and stands upright, so **`Euler(90, 0, 0)` lays it
        /// down facing the sky**, and its local +Y - the axis `localScale.y` stretches - then
        /// runs along world +Z. Adding the slope angle to that 90 tips it downhill, which is why
        /// the sloped piece is placed by its centre and its own length rather than by its corners.
        /// </summary>
        private static void BuildGround(Transform parent)
        {
            Material material = GroundMaterialAsset();
            if (material == null)
                return;

            float nearZ = -6f;
            Lay(parent, "GroundFlat", material,
                new Vector3(0f, -0.01f, (nearZ + BrowZ) * 0.5f),
                Quaternion.Euler(90f, 0f, 0f),
                new Vector3(GroundHalfWidth * 2f, BrowZ - nearZ, 1f));

            float run = LipZ - BrowZ;
            float fall = -GroundY(LipZ);
            Lay(parent, "GroundSlope", material,
                new Vector3(0f, (-fall * 0.5f) - 0.01f, (BrowZ + LipZ) * 0.5f),
                Quaternion.Euler(90f + SlopeAngle, 0f, 0f),
                new Vector3(GroundHalfWidth * 2f, Mathf.Sqrt((run * run) + (fall * fall)), 1f));
        }

        private static void Lay(Transform parent, string name, Material material,
                                Vector3 position, Quaternion rotation, Vector3 scale)
        {
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            quad.transform.SetParent(parent, false);
            quad.transform.position = position;
            quad.transform.rotation = rotation;
            quad.transform.localScale = scale;
            quad.GetComponent<MeshRenderer>().sharedMaterial = material;
            Object.DestroyImmediate(quad.GetComponent<Collider>());
        }

        /// <summary>
        /// Lit grass, tiled about every three metres.
        ///
        /// `Island_Grass` is one of the demo map's own terrain layers, so it is already in the
        /// project, already tileable and already the right grass for this world - no new art, and
        /// nothing to keep in step if the island's palette changes.
        /// </summary>
        private static Material GroundMaterialAsset()
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture>(GrassTexture);
            if (texture == null)
            {
                Debug.LogError($"[{nameof(DemoMenuStageBuilder)}] No grass texture at \"{GrassTexture}\".");
                return null;
            }
            DemoItemBuilder.EnsureFolder(MaterialDir);
            var material = AssetDatabase.LoadAssetAtPath<Material>(GroundMaterial);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, GroundMaterial);
            }
            material.SetTexture("_BaseMap", texture);
            // About 1.5m a repeat. At 3m the texture is soft enough that the verges read as a
            // flat green shelf; finer than this and it starts to shimmer at the lip.
            material.SetTextureScale("_BaseMap", new Vector2(GroundHalfWidth * 2f / 1.5f, 5.2f));
            // **Knocked back, not used straight.** `Island_Grass` is authored for a midday island
            // and comes out a saturated green next to a painted dusk; the tint desaturates it and
            // warms it into the same light as the backdrop. Cheaper and more reversible than
            // repainting the texture, and the island keeps its own greens.
            material.SetColor("_BaseColor", new Color(0.66f, 0.67f, 0.55f));
            material.SetFloat("_Smoothness", 0f);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// The cobbled road: three tiles across, flat over the top of the hill and then tipped
        /// down the slope row by row.
        ///
        /// Placed by transform rather than by <see cref="Place"/>, because a floor tile's pivot
        /// *is* its centre and bounds-seating a pitched tile would lift it by the height its own
        /// tilted bounding box gained. The rows on the slope step along the road surface - one
        /// row is `RowStep` of depth and `RowDrop` of height - so the cobbles meet edge to edge
        /// instead of overlapping or leaving a gap at the crease.
        /// </summary>
        /// <summary>The terrain the stage stands on, when it has one. Null means the old flat quads.</summary>
        private static Terrain _ground;

        /// <summary>How far down the hill the cobbles run.</summary>
        private const float RoadEndZ = 13.2f;

        /// <summary>
        /// How far past the brow the fall takes to reach its full angle. The rounding of
        /// the hill's shoulder, in metres.
        /// </summary>
        private const float RoadShoulder = 5f;

        /// <summary>
        /// How far the road has dropped `run` metres past the brow.
        ///
        /// The fall is not one angle. It ramps from level to <see cref="Grade"/> across
        /// <see cref="RoadShoulder"/> and holds there, which is this integrated:
        /// the slope at any point is `Grade * smoothstep(run / RoadShoulder)`, and the
        /// height is its integral, so the two cannot disagree the way a hand-written
        /// curve and a hand-written pitch would.
        ///
        /// **A constant angle was the thing that read as "two levels of cobble".** A flat
        /// apron meeting a ramp is two planes with a crease between them, and the eye
        /// finds that crease however good the texture is. Rounding the shoulder gives a
        /// road whose slope is different in every course, which is what a road going over
        /// a hill actually looks like.
        ///
        /// **It deliberately does not chase the terrain down.** Dropping the cobbles onto
        /// the sculpted channel was tried and is worse: the ground falls 2.5m by the far
        /// end, so a road pinned to it averages 13 degrees and peaks near 24, and at that
        /// angle the courses face the camera instead of receding from it and the middle of
        /// the road washes out into a pale slab. The cobbles are a causeway over the
        /// channel, not a lining for it - which is also what keeps the crown at y=0 where
        /// the character stands.
        /// </summary>
        private static float RoadFall(float run)
        {
            if (run <= 0f)
                return 0f;
            if (run >= RoadShoulder)
                return Grade * (run - (RoadShoulder * 0.5f));
            // Integral of Grade * smoothstep(u) du over [0, run], u = run / RoadShoulder.
            float u = run / RoadShoulder;
            return Grade * RoadShoulder * ((u * u * u) - (u * u * u * u * 0.5f));
        }

        /// <summary>The height of the cobbles at a given depth. Level across all three lanes.</summary>
        private static float RoadY(float z)
        {
            return -RoadFall(z - BrowZ);
        }

        /// <summary>The road's angle at a given depth, in degrees, nose-down the hill.</summary>
        private static float RoadPitch(float z)
        {
            float run = z - BrowZ;
            if (run <= 0f)
                return 0f;
            float slope = run >= RoadShoulder
                ? Grade
                : Grade * Mathf.SmoothStep(0f, 1f, run / RoadShoulder);
            return Mathf.Atan(slope) * Mathf.Rad2Deg;
        }

        private static void BuildRoad(Transform parent)
        {
            var road = new GameObject("Road");
            road.transform.SetParent(parent, false);

            for (int row = 0; row < FlatRows; ++row)
            {
                float z = BrowZ - 1f - (row * 2f);
                for (int lane = -1; lane <= 1; ++lane)
                    Pave(road.transform, lane * 2f, RoadY(z), z, 0f);
            }

            // Stepped in world depth rather than along the slope: the fall is no longer one
            // angle, so there is no single hypotenuse to march down. The step is the tile's
            // own 2m - closer would overlap them and z-fight.
            for (float z = BrowZ + 1f; z <= RoadEndZ; z += 2f)
            {
                for (int lane = -1; lane <= 1; ++lane)
                    Pave(road.transform, lane * 2f, RoadY(z), z, RoadPitch(z));
            }
        }

        private static void Pave(Transform parent, float x, float y, float z, float pitch)
        {
            string path = PathOf("Floor_UnevenBrick");
            if (path == null)
                return;
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source, parent);
            instance.name = "Cobbles";
            // Compose, never assign - see Place for what the Village pack does to a tile that has
            // its rotation written outright.
            instance.transform.rotation = Quaternion.Euler(pitch, 0f, 0f) * instance.transform.rotation;
            instance.transform.position = new Vector3(x, y, z);
        }

        private static int BuildProps(Transform parent)
        {
            var dressing = new GameObject("Dressing");
            dressing.transform.SetParent(parent, false);
            int placed = 0;
            foreach (Prop prop in Props)
            {
                GameObject instance = Place(dressing.transform, prop.Model, prop.X, prop.Z, prop.Yaw, prop.Scale, prop.Lift);
                if (instance == null)
                    continue;
                ++placed;
                if (prop.Model == "Firepit")
                    Ignite(instance);
                Sway(instance, prop.Model);
            }
            return placed;
        }

        /// <summary>
        /// A brazier is only furniture until it is lit. Reuses the demo's own generated flame
        /// rather than a new effect, and hangs a warm point light on it - that light is what
        /// actually does the work, because it is the only thing lighting the character's front
        /// from below and it is what makes the stone read as stone.
        /// </summary>
        /// <summary>
        /// Which way the wind blows across the menu, in degrees. 75 puts it mostly left to right
        /// across the frame, where a lean is visible, rather than towards or away from the camera
        /// where it would read as the plants growing and shrinking.
        /// </summary>
        private const float WindYaw = 75f;

        /// <summary>
        /// Gives a plant a wind sway sized to what it is, and leaves everything else alone.
        ///
        /// **Matched on the model's name, not listed per prop.** The table is already long, this
        /// is a property of the *kind* of thing rather than of the individual, and a plant added
        /// later gets wind without anybody remembering to ask for it. A rock or a crate matches
        /// nothing and stays still.
        ///
        /// Smaller things move further and faster. That is not decoration: a fern and a pine in
        /// the same breeze genuinely behave differently, and giving them the same figures is the
        /// quickest way to make a scene look like it is all one object.
        ///
        /// **Degrees are a misleading unit here, and reading them as if they were not is how both
        /// tiers came out wrong.** What the eye follows is the arc the top of the plant travels,
        /// which is the lean times the height - so the trees' original 1.7 degree peak swung a
        /// 9.7m pine's crown through **29cm** while the grass, at twice the angle, moved its tips
        /// 4cm. The trees were the loudest thing in the scene despite having the smallest number
        /// against them. The figures below are picked so the crown travel lands near 11cm and the
        /// tip travel near 4cm, and are worth re-deriving rather than nudged if the planting or
        /// the scales change.
        /// </summary>
        private static void Sway(GameObject instance, string model)
        {
            float degrees, period, swell, swellPeriod;
            if (model.StartsWith("Pine") || model.StartsWith("CommonTree")
                || model.StartsWith("TwistedTree") || model.StartsWith("DeadTree"))
            {
                // 0.65 degrees of peak lean: 11cm of crown travel on a 9.7m pine, down from 29.
                // Slowed as well, because a tree's mass should read in how long it takes to come
                // back, not just how far it goes.
                degrees = 0.45f; period = 6.5f; swell = 0.2f; swellPeriod = 15f;
            }
            else if (model.StartsWith("Fern") || model.StartsWith("Bush") || model.StartsWith("Plant"))
            {
                degrees = 2.2f; period = 3.4f; swell = 1.0f; swellPeriod = 9f;
            }
            else if (model.StartsWith("Grass") || model.StartsWith("Clover") || model.StartsWith("Flower"))
            {
                // Toned down from 4.0/2.2 with a 1.5 swell - a 5.5 degree peak on a 2.2 second
                // cycle, which read as fidgeting rather than as wind. **The rate was the bigger
                // culprit than the lean**: these are wispy blades and a fast cycle makes them
                // shimmer, so the period went up by half again and the amplitude down by a third.
                // Still the liveliest tier, because it should be, just not frantic.
                degrees = 2.6f; period = 3.2f; swell = 0.9f; swellPeriod = 8.5f;
            }
            else
            {
                return;
            }
            instance.AddComponent<DemoWindSway>().Configure(WindYaw, degrees, period, swell, swellPeriod);
        }

        private static void Ignite(GameObject brazier)
        {
            // Measured off the brazier rather than assumed from y=0: they stand down the slope
            // now, so "one metre up" is one metre above wherever this one happens to sit.
            Bounds bowl = default;
            bool any = false;
            foreach (Renderer renderer in brazier.GetComponentsInChildren<Renderer>(true))
            {
                if (!any) { bowl = renderer.bounds; any = true; }
                else bowl.Encapsulate(renderer.bounds);
            }
            // 0.97 of the brazier's own height, which is where the bowl's rim is - the same
            // place the old fixed 1.05 landed on a full-size one, but right on a scaled one too.
            float top = any ? bowl.min.y + (bowl.size.y * 0.97f) : FloorY + 1.05f;
            var flamePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(FlamePrefab);
            if (flamePrefab != null)
            {
                var flame = (GameObject)PrefabUtility.InstantiatePrefab(flamePrefab, brazier.transform);
                flame.transform.position = new Vector3(brazier.transform.position.x, top, brazier.transform.position.z);
                flame.transform.localScale = Vector3.one * 1.35f;
            }
            var lightObject = new GameObject("BrazierLight");
            lightObject.transform.SetParent(brazier.transform, false);
            lightObject.transform.position = new Vector3(brazier.transform.position.x, top + 0.25f, brazier.transform.position.z);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.62f, 0.28f);
            light.intensity = 4.5f;
            light.range = 8f;
            light.shadows = LightShadows.None;
        }

        /// <summary>
        /// The painted valley, on a quad far enough back to sit behind everything and large
        /// enough to overfill the frame.
        ///
        /// Unlit on purpose: it is a painting, already lit by its own sun, and running it through
        /// the scene's lights would darken it and drag the sunset towards whatever colour the key
        /// light happens to be. Deliberately oversized - 48x27 at 14m - so it still covers the
        /// frame on an ultrawide monitor, where a quad sized for 16:9 would show its own edges.
        /// </summary>
        private static bool BuildBackdrop(Transform parent)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture>(BackdropTexture);
            if (texture == null)
            {
                // Worth shouting about. Without this the menu is a dark void behind the road, and
                // the stage builds perfectly happily around the hole - which is exactly what
                // happened when the painting was renamed and this kept looking for the old path.
                Debug.LogError($"[{nameof(DemoMenuStageBuilder)}] No backdrop image at \"{BackdropTexture}\" - " +
                               "the menu will have no sky. Point BackdropTexture at the painting.");
                return false;
            }
            var importer = AssetImporter.GetAtPath(BackdropTexture) as TextureImporter;
            if (importer != null && importer.maxTextureSize < 2048)
            {
                importer.maxTextureSize = 2048;
                importer.SaveAndReimport();
            }

            DemoItemBuilder.EnsureFolder(MaterialDir);
            var material = AssetDatabase.LoadAssetAtPath<Material>(BackdropMaterial);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(material, BackdropMaterial);
            }
            material.SetTexture("_BaseMap", texture);
            EditorUtility.SetDirty(material);

            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Backdrop";
            quad.transform.SetParent(parent, false);
            quad.transform.position = BackdropCentre;
            // **No rotation.** Unity's built-in Quad already faces its local -Z, which is towards
            // a camera sitting at negative Z - which ours does. Turning it 180 to "face the
            // camera" points the normal away instead and backface culling eats it: the first build
            // of this rendered a black screen for exactly that reason.
            quad.transform.rotation = Quaternion.identity;
            // **Sized from the image, not from numbers typed here.** The first version was a
            // hand-worked crop of a 16:9 painting that had its own three adventurers in one corner
            // and had to be framed around them. The painting it shows now is a clean 2.74:1
            // panorama with nobody in it, so the whole thing is wanted and the only job left is
            // not to distort it.
            float aspect = texture.width / (float)texture.height;
            quad.transform.localScale = new Vector3(BackdropHeight * aspect, BackdropHeight, 1f);
            quad.GetComponent<MeshRenderer>().sharedMaterial = material;
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            return true;
        }

        /// <summary>
        /// Three lights, doing three jobs.
        ///
        /// The backdrop's sun is low and behind the stage to the right, so the key is a warm
        /// **back** light from that direction - it rims the character's shoulders and separates
        /// them from a busy painting, which is the whole problem with standing a character in
        /// front of artwork. The fill is cool and from the front, standing in for skylight, and
        /// it is the only thing keeping a backlit face from going to silhouette. The braziers
        /// supply the warm bounce from below.
        /// </summary>
        private static void BuildLighting(Transform parent, Scene scene)
        {
            Light existing = null;
            foreach (GameObject go in scene.GetRootGameObjects())
            {
                Light found = go.GetComponent<Light>();
                if (found != null && found.type == LightType.Directional)
                {
                    existing = found;
                    break;
                }
            }
            if (existing != null)
            {
                existing.transform.rotation = Quaternion.Euler(22f, 202f, 0f);
                existing.color = new Color(1f, 0.83f, 0.62f);
                existing.intensity = 1.75f;
                existing.shadows = LightShadows.Soft;
                EditorUtility.SetDirty(existing);
            }

            var fillObject = new GameObject("FillLight");
            fillObject.transform.SetParent(parent, false);
            fillObject.transform.rotation = Quaternion.Euler(16f, 18f, 0f);
            var fill = fillObject.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.color = new Color(0.72f, 0.80f, 0.98f);
            // Carrying more than a fill normally would. With the key behind the stage, this is
            // the only light on every surface the camera can actually see - at 0.85 the brick
            // terrace came out near black and the character was a silhouette.
            fill.intensity = 1.5f;
            fill.shadows = LightShadows.None;

            // Ambient to match the painting's dusk rather than Unity's default grey.
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.50f, 0.56f, 0.72f);
            RenderSettings.ambientEquatorColor = new Color(0.44f, 0.40f, 0.42f);
            RenderSettings.ambientGroundColor = new Color(0.26f, 0.22f, 0.20f);
        }

        /// <summary>
        /// Frames the terrace, the character and enough of the painting to read.
        ///
        /// Pulled back and levelled off from the shipped (0,2,-2.5) at 20 degrees down, which
        /// framed a character in a void and pointed most of the frame at the floor. The numbers
        /// are chosen against the frustum rather than by eye: at this height and pitch the bottom
        /// of a 60-degree frame lands **on** the terrace rather than past its front edge, so
        /// there is no gap under the stage, and the top clears the character's head by about a
        /// metre and looks into the painting's sky.
        /// </summary>
        /// <summary>
        /// The wide framing, which is the one the scene is saved in.
        ///
        /// Up at 3.4m and pitched 14 degrees down, because that is what it takes to see a road
        /// rather than a horizon: from the old portrait lens at 1.55m and 6 degrees, the ten
        /// metres of descent occupied about 9% of the frame's height. From here it is 15%, the
        /// cobbles read as a road going somewhere, and the trees down the hillside have room to
        /// step down. Nothing on these screens has to flatter a character, so nothing here is a
        /// compromise between the two jobs any more.
        /// </summary>
        private static readonly Vector3 MenuEye = new Vector3(0f, 3.4f, -5.6f);
        private const float MenuPitch = 14f;
        private const float MenuFieldOfView = 55f;

        /// <summary>
        /// The close framing, asked for by <see cref="DemoCharacterPreviewControl"/> while a
        /// character screen is up and given back when it closes. Eye height and pitch are a
        /// standing figure's, and the character stands at the origin on the flat top of the hill.
        /// <see cref="DemoMenuCamera"/> travels between the two rather than cutting.
        /// </summary>
        private static readonly Vector3 PortraitEye = new Vector3(0f, 1.55f, -3.4f);
        private const float PortraitPitch = 6f;
        private const float PortraitFieldOfView = 60f;

        private static void FrameCamera(Scene scene)
        {
            foreach (GameObject go in scene.GetRootGameObjects())
            {
                Camera camera = go.GetComponent<Camera>();
                if (camera == null)
                    continue;
                camera.transform.position = MenuEye;
                camera.transform.rotation = Quaternion.Euler(MenuPitch, 0f, 0f);
                camera.fieldOfView = MenuFieldOfView;

                // The rig that travels between the two framings at runtime. It goes on the camera
                // rather than on a screen, so it is still running to carry the camera back after
                // the screen that asked for the close framing has gone. Reused if it is already
                // there: `Clear` only removes the stage root, so this survives a rebuild.
                var rig = camera.GetComponent<DemoMenuCamera>();
                if (rig == null)
                    rig = camera.gameObject.AddComponent<DemoMenuCamera>();
                rig.Configure(MenuEye, MenuPitch, MenuFieldOfView,
                              PortraitEye, PortraitPitch, PortraitFieldOfView);
                EditorUtility.SetDirty(rig);
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 80f;
                // The backdrop fills the frame, so the clear colour is only ever seen if
                // something goes wrong - keep it dark rather than the shipped orange.
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.05f, 0.06f, 0.09f);
                EditorUtility.SetDirty(camera);
                break;
            }
        }

        /// <summary>
        /// The scene carried a root `Canvas` holding nothing but a **missing script** - no
        /// children, no Canvas component, nothing referencing it. Left alone it is a broken
        /// script warning on every load of the menu.
        ///
        /// It also carried the kit template's `Plane`: a MeshFilter and a MeshCollider with
        /// **no renderer**, an invisible floor at y=0 left from before this scene had any
        /// ground of its own. Harmless to look at and confusing to find, and now that the
        /// ground is a terrain with its own collider it is one more thing under the stage
        /// claiming to be the floor. Removed only when it is that exact shape - collider,
        /// no renderer, no children - so a plane anyone adds on purpose is left alone.
        /// </summary>
        private static void RemoveDeadObjects(Scene scene)
        {
            foreach (GameObject go in scene.GetRootGameObjects())
            {
                if (go.name == "Plane" && go.transform.childCount == 0 &&
                    go.GetComponent<MeshRenderer>() == null && go.GetComponent<MeshCollider>() != null)
                {
                    Debug.Log($"[{nameof(DemoMenuStageBuilder)}] Removed the template \"Plane\" - " +
                              "an invisible collider-only floor left over from the kit scene.");
                    Object.DestroyImmediate(go);
                    continue;
                }
                if (go.name != "Canvas" || go.transform.childCount > 0)
                    continue;
                if (go.GetComponent<Canvas>() != null)
                    continue;
                Component[] components = go.GetComponents<Component>();
                bool onlyBroken = true;
                foreach (Component c in components)
                {
                    if (c is Transform || c == null)
                        continue;
                    onlyBroken = false;
                }
                if (onlyBroken)
                {
                    Debug.Log($"[{nameof(DemoMenuStageBuilder)}] Removed the empty \"Canvas\" object, which held only a missing script.");
                    Object.DestroyImmediate(go);
                }
            }
        }

        // ------------------------------------------------------------------

        private static readonly Dictionary<string, string> _modelPaths = new Dictionary<string, string>();

        private static string PathOf(string model)
        {
            if (_modelPaths.TryGetValue(model, out string cached))
                return cached;
            foreach (string guid in AssetDatabase.FindAssets(model, new[] { QuaterniusDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(path) != model)
                    continue;
                if (!path.EndsWith(".fbx") && !path.EndsWith(".prefab"))
                    continue;
                _modelPaths[model] = path;
                return path;
            }
            _modelPaths[model] = null;
            return null;
        }

        /// <summary>
        /// Instantiates a model and seats it: footprint centre on (x, z), bottom on the floor.
        ///
        /// Measured, not assumed. These packs do not share a pivot convention - a floor tile is
        /// centred, a banner hangs off its post, a rock is modelled wherever it was sculpted - so
        /// the only reliable way to line things up is to instantiate, read the renderer bounds,
        /// and move by the difference.
        /// </summary>
        private static GameObject Place(Transform parent, string model, float x, float z, float yaw, float scale, float lift)
        {
            string path = PathOf(model);
            if (path == null)
            {
                Debug.LogWarning($"[{nameof(DemoMenuStageBuilder)}] No Quaternius model named \"{model}\".");
                return null;
            }
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source, parent);
            instance.name = model;
            // **Multiply the prefab's own scale and compose with its own rotation - never
            // replace them.** The Village pack's floor tiles import as a 0.02-unit mesh on a
            // root scaled by **100**, the metre-to-centimetre conversion sitting in the node
            // instead of the file header (see [[blender-to-unity-rig-traps]] #2 for the same
            // trap from the other side). Assigning `localScale = one` turned every 2m tile into
            // a 2cm speck, and assigning the rotation outright threw away the import's axis
            // correction so the survivors stood on edge. Props authored at scale 1 with no
            // rotation - which is most of them - are unaffected either way, which is exactly
            // why this went unnoticed until the floor was the only thing missing.
            instance.transform.localScale = instance.transform.localScale * scale;
            instance.transform.rotation = Quaternion.Euler(0f, yaw, 0f) * instance.transform.rotation;
            instance.transform.position = new Vector3(x, GroundY(z), z);

            Bounds bounds = default;
            bool any = false;
            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                if (!any) { bounds = renderer.bounds; any = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            if (any)
            {
                // **Kept on the ground, not merely seated on it.** Seating puts a prop's *centre*
                // at z, so anything wide near the lip can be perfectly seated and still have its
                // back half over the end of the world - which is how a row of ferns came to hang
                // over the painting, each one reaching three metres past ground that stops at
                // 11.4. The half-depth is the **rotated** bounds, so a yawed fern is 3.3m deep
                // where the same fern square-on is 2.1m; that is why hand-picked depths kept
                // nearly working and then not quite.
                float halfDepth = bounds.max.z - bounds.center.z;
                float seatZ = Mathf.Min(z, LipZ - halfDepth - LipMargin);
                if (seatZ < z - 0.001f)
                {
                    Debug.Log($"[{nameof(DemoMenuStageBuilder)}] \"{model}\" pulled in from z {z} to " +
                              $"{seatZ:0.00}: it is {halfDepth:0.00}m deep from its centre and the ground " +
                              $"ends at {LipZ:0.00}. Set it there in the table to keep the two in step.");
                }
                // Props are left upright rather than tipped to match the slope: at 8 degrees a 1m
                // barrel's base is 7cm out of true, which nothing in the frame is close enough to
                // show, and pitching them would mean seating each one along its own tilted box.
                var offset = new Vector3(x - bounds.center.x, GroundY(seatZ) + lift - bounds.min.y, seatZ - bounds.center.z);
                instance.transform.position += offset;
            }
            return instance;
        }

        private static GameObject Place(Transform parent, string model, float x, float z, float yaw, float scale)
        {
            return Place(parent, model, x, z, yaw, scale, 0f);
        }
    }
}
