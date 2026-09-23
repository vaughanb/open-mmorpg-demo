using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// The island's resurrection shrines: a carved stele over a basin on a stepped
    /// plinth, which a player activates to bind their spirit to it and wakes beside after
    /// dying.
    ///
    /// The binding is entirely the kit's. A dialog of type `SaveRespawnPoint` writes
    /// `RespawnMapName` and `RespawnPosition` onto the character, and the kit's own death
    /// handling reads them back, so a shrine is an NPC with one dialog and no code of its
    /// own. Before this the demo set neither: every character kept the respawn point it
    /// was given at creation, so dying at the bottom of the crypt put you back on the
    /// village green and cost the whole island on foot.
    ///
    /// **A shrine is an NPC, not a person.** `NpcEntity` derives from `BaseGameEntity`,
    /// not from the character entity, so it needs no model, no skeleton and no animator -
    /// `EntityUpdate` drives a model only `if (Model != null)`, and null is a fine answer
    /// for a pile of stonework. It gets the same nameplate and the same activate prompt
    /// Fenwick does.
    ///
    /// **The shrine is a modelled asset**, not an assembly. It began as an arch, a rune
    /// ring and two braziers put together out of the village pack, because no shrine mesh
    /// existed; `Shrine_Wayside.fbx` replaced all of that. What is left here is the part
    /// that was never about the stonework: where it stands, what it says, and the scene
    /// object id it answers to.
    ///
    /// Every offset below is **measured off the mesh** rather than guessed - the collider
    /// boxes from its own vertical profile, the fire anchors from where its metallic map
    /// says the brass is. See `Shrine_Wayside.fbx` at 2.60m tall on a 2.40 x 2.45m
    /// footprint, pivot on the ground at the centre of the plinth, face on local +Z.
    /// </summary>
    public static class DemoShrineBuilder
    {
        private const string GameDataDir = "Assets/OpenMMORPG/Demo/GameData";
        private const string DialogDir = GameDataDir + "/Resources/NpcDialogs";
        private const string MapInfoDir = GameDataDir + "/Resources/MapInfos";
        private const string PrefabDir = "Assets/OpenMMORPG/Demo/Prefabs/GamePlay/Shrines";
        private const string PrefabPath = PrefabDir + "/DemoShrine.prefab";

        /// <summary>
        /// The shrine itself: a carved stele over a basin on a stepped plinth, with brass
        /// caps at the front corners. Authored art rather than library parts, and it
        /// carries its own material, so nothing here repaints it.
        /// </summary>
        private const string ModelPath = "Assets/OpenMMORPG/Demo/Art/Shrine/Models/Shrine_Wayside.fbx";

        // ---- the shape of a shrine -------------------------------------------
        //
        // Read off the mesh, not chosen: 2.40 x 2.45m on the ground, 2.60m to the crown of
        // the stele, pivot on the ground at the centre of the plinth, face on local +Z.
        // The profile it was measured from, in metres above the ground:
        //
        //     0.00 - 0.20   bottom step      2.40 x 2.45
        //     0.20 - 0.40   second step      2.06 x 2.17
        //     0.40 - 0.85   plinth and basin 1.88 x 2.01
        //     0.85 - 2.60   the stele          to 1.42 x 0.48, narrowing with height

        /// <summary>How tall it stands, and how wide it lies. Measured off the mesh.</summary>
        private const float ShrineHeight = 2.60f;

        /// <summary>
        /// The brass caps at the front corners, and the top of them.
        ///
        /// Found rather than eyeballed: the vertices whose UV lands on a metallic texel,
        /// clustered. There are four brass pieces on the shrine - two bracing the stele's
        /// shoulders at 2.06m, and these two, which are the flat tops of the pedestals
        /// flanking the way in. **The model has sockets for its own fires**, which is why
        /// nothing is stood beside it any more.
        /// </summary>
        private static readonly Vector3[] FireLocal =
        {
            new Vector3(-0.833f, 0.80f, 0.785f), new Vector3(0.833f, 0.80f, 0.785f),
        };

        /// <summary>
        /// The **torch** flame, not the campfire one. A campfire is bedded 0.17m deep and
        /// throws particles up to 0.46m across, which is wider than the 0.20m cap it would
        /// be standing on; the torch recipe is the one built for a flame on a bracket.
        /// </summary>
        private const string FirePath = DemoFlameBuilder.TorchFlamePath;

        /// <summary>
        /// What a player can walk into. Four boxes off the profile above: the two steps,
        /// the plinth the basin is sunk into, and the stele.
        ///
        /// Boxes rather than the mesh itself. A 5,000-triangle concave collider would carve
        /// the navmesh with every crack and moulding in it, and none of that detail is
        /// anything a character can act on - the shape a player meets is a stepped block
        /// with a slab on the back of it.
        ///
        /// The stele's box takes its width at the shoulders and keeps it to the crown,
        /// which is up to 0.3m proud of the stone at the very top. That is 2.4m in the air
        /// on a thing nobody can climb.
        /// </summary>
        private static readonly Vector3[] ColliderCentre =
        {
            new Vector3(0f, 0.100f, 0f), new Vector3(0f, 0.300f, -0.05f),
            new Vector3(0f, 0.625f, -0.075f), new Vector3(0f, 1.725f, -0.84f),
        };
        private static readonly Vector3[] ColliderSize =
        {
            new Vector3(2.40f, 0.20f, 2.45f), new Vector3(2.06f, 0.20f, 2.17f),
            new Vector3(1.88f, 0.45f, 2.01f), new Vector3(1.42f, 1.75f, 0.48f),
        };

        /// <summary>
        /// Where the nameplate hangs: a little clear of the crown of the stele, so the name
        /// reads against the sky rather than against the carving it is sitting on.
        /// </summary>
        private const float NameplateHeight = ShrineHeight + 0.30f;

        /// <summary>
        /// Where a bound spirit wakes: on the grass in front of the steps, not at the
        /// shrine's own origin, which is the middle of the plinth and therefore inside the
        /// stonework. The plinth's front edge is at 1.23m, so this clears it by a stride.
        /// </summary>
        private static readonly Vector3 BindOffset = new Vector3(0f, 0f, 2.00f);

        // ---- where they stand ------------------------------------------------

        /// <summary>
        /// The shrine just outside the village: twenty-one metres north of the green, out
        /// through the gap in the fence past the north houses, on the open grass.
        ///
        /// Outside rather than on the green, which is where it began. A shrine is a thing
        /// you walk out to, and the green is already the market, the smithy, the fire, the
        /// benches and three of the four quest givers; one more structure on it was the
        /// green getting fuller rather than the island getting somewhere.
        ///
        /// **Far enough out to be outside, level enough to stand on. There is exactly one
        /// such place.** The island's terrain is flattened under each settlement and the
        /// village's pad runs out at about seventeen metres, so the ring divides into three:
        /// inside sixteen the ground is dead flat and every bearing is under somebody's
        /// eaves; the fence stands at sixteen; and past seventeen the hill falls away - by
        /// three metres across the shrine's own width to the south-west, five to the
        /// south, six to the south-east. Swept at every bearing from 18 to 26m, this is the
        /// only footprint that comes back both level (0.46m) and clear (3.7m), and it
        /// happens to sit in one of the five gaps in the fence, which is the way out of town
        /// on that side. Moving it wants the sweep run again rather than a nudge - the
        /// neighbouring bearings are a roof or a hillside.
        ///
        /// **Terrain trees are invisible to the obvious version of that sweep.** They live in
        /// `TerrainData.treeInstances`, not as renderers in the scene, so a search that walks
        /// `GetComponentsInChildren&lt;Renderer&gt;` reports open ground under a wood. All 320
        /// of the island's trees are in that array and not one of them is in the scene graph.
        /// </summary>
        private static readonly Vector3 VillageLocal = new Vector3(0f, 0f, 21f);

        // A second shrine at the crypt would be the one that pays for itself - dying to the
        // Hierophant currently costs the whole island on foot - and there is nowhere to put
        // it. Swept the ground within 24m of the crypt door for a footprint that is level
        // to within 0.6m and 2m clear of everything (including the terrain trees, which are
        // in TerrainData and not in the scene): **no site passes**. The crypt stands on a
        // small rocky rise ringed with boulders, its own terrain pad runs out about six
        // metres past the door, and the ground falls a metre or more in every direction
        // after that. Making that shrine happen needs a decision rather than a nudge:
        // shrink it to something that fits the step outside the door, flatten a pad for it
        // in DemoIslandBuilder the way the settlements get one, or accept it standing well
        // down the approach. Left out rather than built somewhere it floats.

        /// <summary>
        /// One shrine: where it stands, which way it faces, what it is called, and the
        /// scene object id it answers to.
        /// </summary>
        private struct Site
        {
            public string Name;
            public string Title;
            public string Greeting;
            public string Bound;
            public string Declined;
            public Vector3 World;
            public float Yaw;
        }

        [MenuItem("Open MMORPG/Demo/Build Shrines", priority = 152)]
        public static void Build()
        {
            DemoItemBuilder.EnsureFolder(PrefabDir);
            DemoItemBuilder.EnsureFolder(DialogDir);

            GameObject prefab = BuildPrefab();
            if (prefab == null)
                return;

            PlaceInScene(prefab, Sites());

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[{nameof(DemoShrineBuilder)}] Built the shrine, its dialogs and both sites. " +
                      "There is stone in the scene that was not in the navmesh, so run " +
                      "Rebake Island Navmesh, and Build Map Server after that.");
        }

        /// <summary>
        /// The sites, positioned off the village layout rather than in world coordinates,
        /// so that moving the village takes its shrine along - the same reason the banker
        /// is read out of the house layout instead of being written down beside it.
        /// </summary>
        private static Site[] Sites()
        {
            Vector2 village = DemoIslandBuilder.VillageCentre;
            var villageWorld = new Vector3(village.x + VillageLocal.x, DemoIslandBuilder.VillageHeight,
                                           village.y + VillageLocal.z);
            // Facing the village: the shrine's own +Z turned back at the green, so anyone
            // walking out of town meets its face and not its back.
            float villageYaw = Mathf.Atan2(-VillageLocal.x, -VillageLocal.z) * Mathf.Rad2Deg;

            return new[]
            {
                new Site
                {
                    Name = "VillageShrine",
                    Title = "Wayside Shrine",
                    Greeting = "Old stone, older runes. They warm under your hand.\n\nBind your spirit to this place?",
                    Bound = "The ring dims, and something of you stays behind in it. However the island finishes with you, you will wake here.",
                    Declined = "The light goes out of the runes. The stone will keep.",
                    World = villageWorld,
                    Yaw = villageYaw,
                },
            };
        }

        // ---- the prefab ------------------------------------------------------

        /// <summary>
        /// Puts the shrine together and saves it as one prefab, rebuilt from scratch each
        /// run. Every site is an instance of it, so its shape is edited here rather than
        /// once per site out in the scene.
        /// </summary>
        private static GameObject BuildPrefab()
        {
            var shrine = new GameObject("DemoShrine");
            var structure = new GameObject("Structure");
            structure.transform.SetParent(shrine.transform, false);

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                Debug.LogError($"[{nameof(DemoShrineBuilder)}] No shrine model at \"{ModelPath}\".");
                Object.DestroyImmediate(shrine);
                return null;
            }
            // Straight in at the origin. The model is authored standing on its own pivot
            // with its face on +Z and its metre scale baked into the vertices, so there is
            // no offset, no turn and no scale to apply here - and the seating pass below
            // depends on that, because it works from the piece's renderer bounds.
            var stone = (GameObject)PrefabUtility.InstantiatePrefab(model, structure.transform);
            stone.name = "Shrine";
            stone.transform.localPosition = Vector3.zero;
            stone.transform.localRotation = Quaternion.identity;

            foreach (Vector3 at in FireLocal)
            {
                // Never out. A resurrection shrine that goes dark at dawn reads as a ruin,
                // and the village's own fire is on the same schedule for the same reason.
                //
                // Under the model rather than under `Structure`, so the fires ride with the
                // stone when it settles onto the hill instead of staying where the flat
                // prefab put them.
                MultiplayerARPG.Demo.DemoTorch fire = DemoFlameBuilder.Light(
                    FirePath, stone.transform, at, MultiplayerARPG.Demo.DemoTorch.Schedule.Always);
                if (fire == null)
                    continue;

                // A votive flame, not a street torch. The recipe is set for a torch on a
                // wall bracket with nothing within arm's reach of it; here the lamp sits
                // 0.8m from two square metres of pale stone, and **a fire that never goes
                // out is lit at noon as well** - at full strength the pair of them turned
                // the whole shrine amber in broad daylight. Cut to what a bowl of fire
                // actually throws, which is still a warm pool after dark.
                fire.intensity = 0.55f;
                if (fire.lamp != null)
                    fire.lamp.range = 4.5f;
                // Again: Light already settled it, at the strength it is being taken off.
                fire.Settle();
            }

            // The anchors the nameplate and the quest marker hang from. The marker is never
            // used - a shrine hands out no quests - but NpcEntity instantiates into whatever
            // container it is given, and the kit's marker is a world-space canvas laid out in
            // pixels, so the container has to carry the pixel-to-metre scale. See
            // DemoEntityBuilder for what that number is and why it is not one.
            var transforms = new GameObject("Transforms");
            transforms.transform.SetParent(shrine.transform, false);
            Transform characterUi = Anchor(transforms.transform, "UIElementContainer", NameplateHeight);
            Transform miniMapUi = Anchor(transforms.transform, "MiniMapContainer", 0f);
            Transform questIndicator = Anchor(transforms.transform, "QuestIndicatorContainer", NameplateHeight + 0.3f);
            questIndicator.localScale = Vector3.one * 0.045f;

            NpcEntity npc = shrine.AddComponent<NpcEntity>();
            var serialized = new SerializedObject(npc);
            serialized.FindProperty("characterUiTransform").objectReferenceValue = characterUi;
            serialized.FindProperty("miniMapUiTransform").objectReferenceValue = miniMapUi;
            serialized.FindProperty("questIndicatorContainer").objectReferenceValue = questIndicator;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            AddColliders(shrine);

            PrefabUtility.SaveAsPrefabAsset(shrine, PrefabPath);
            DemoEntityBuilder.GiveOwnNetworkId(PrefabPath);
            // Forced: writing a new file over a prefab does not refresh the editor's loaded
            // copy, and everything below reads that copy.
            AssetDatabase.ImportAsset(PrefabPath, ImportAssetOptions.ForceUpdate);
            Object.DestroyImmediate(shrine);
            return AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        }

        /// <summary>
        /// The boxes from `ColliderCentre` / `ColliderSize`, which is where the shape of
        /// them is written down and measured.
        ///
        /// These carve the navmesh too, which is why the shrines get a root of their own
        /// rather than standing under `Npcs`. That root is switched off for the bake
        /// because the people under it move and would each cut a hole where they stand. A
        /// shrine does not move: it is masonry, and masonry belongs in the bake.
        /// </summary>
        private static void AddColliders(GameObject shrine)
        {
            for (int i = 0; i < ColliderCentre.Length; ++i)
                Box(shrine, ColliderCentre[i], ColliderSize[i]);
        }

        // ---- the dialog ------------------------------------------------------

        /// <summary>
        /// One shrine's dialog: the offer, and the two things it says afterwards.
        ///
        /// The offer *is* the start dialog rather than sitting behind a greeting, the way
        /// the banker's strongbox does. A shrine is a thing, not a person, and there is
        /// nothing to ask it but the one question. The kit draws the Confirm and Cancel
        /// buttons itself for this dialog type, so the offer carries no menus of its own -
        /// only the two dialogs the answers lead to.
        ///
        /// **Each shrine needs its own dialog asset**, because the bind point is a field on
        /// the dialog and not a property of the NPC showing it: `saveRespawnMap` and
        /// `saveRespawnPosition` are what get written onto the character. Two shrines
        /// sharing one dialog would both bind you to the same stone.
        /// </summary>
        private static NpcDialog BuildDialog(Site site, BaseMapInfo map, Vector3 bindPosition)
        {
            var bound = Create<NpcDialog>($"{DialogDir}/{site.Name}Bound.asset");
            var boundSerialized = new SerializedObject(bound);
            boundSerialized.FindProperty("title").stringValue = site.Title;
            boundSerialized.FindProperty("description").stringValue = site.Bound;
            boundSerialized.FindProperty("type").enumValueIndex = (int)NpcDialogType.Normal;
            SetCloseMenu(boundSerialized, "Step back");
            boundSerialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(bound);

            var declined = Create<NpcDialog>($"{DialogDir}/{site.Name}Declined.asset");
            var declinedSerialized = new SerializedObject(declined);
            declinedSerialized.FindProperty("title").stringValue = site.Title;
            declinedSerialized.FindProperty("description").stringValue = site.Declined;
            declinedSerialized.FindProperty("type").enumValueIndex = (int)NpcDialogType.Normal;
            SetCloseMenu(declinedSerialized, "Step back");
            declinedSerialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(declined);

            var offer = Create<NpcDialog>($"{DialogDir}/{site.Name}.asset");
            var serialized = new SerializedObject(offer);
            serialized.FindProperty("title").stringValue = site.Title;
            serialized.FindProperty("description").stringValue = site.Greeting;
            serialized.FindProperty("type").enumValueIndex = (int)NpcDialogType.SaveRespawnPoint;
            serialized.FindProperty("saveRespawnMap").objectReferenceValue = map;
            serialized.FindProperty("saveRespawnPosition").vector3Value = bindPosition;
            serialized.FindProperty("saveRespawnConfirmDialog").objectReferenceValue = bound;
            serialized.FindProperty("saveRespawnCancelDialog").objectReferenceValue = declined;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(offer);
            return offer;
        }

        private static void SetCloseMenu(SerializedObject serialized, string title)
        {
            SerializedProperty menus = serialized.FindProperty("menus");
            menus.arraySize = 1;
            SerializedProperty menu = menus.GetArrayElementAtIndex(0);
            menu.FindPropertyRelative("title").stringValue = title;
            menu.FindPropertyRelative("isCloseMenu").boolValue = true;
            menu.FindPropertyRelative("dialog").objectReferenceValue = null;
            menu.FindPropertyRelative("showConditions").arraySize = 0;
        }

        // ---- placement -------------------------------------------------------

        /// <summary>
        /// Stands both shrines in the map scene, under a root of their own.
        ///
        /// Run again, it leaves a shrine that is already there exactly where it stands and
        /// only refreshes what it says - the same contract the NPCs have, so a shrine can
        /// be nudged by hand in the editor and keep the nudge.
        /// </summary>
        private static void PlaceInScene(GameObject prefab, Site[] sites)
        {
            var map = AssetDatabase.LoadAssetAtPath<BaseMapInfo>($"{MapInfoDir}/BaseMap.asset");
            if (map == null)
            {
                Debug.LogError($"[{nameof(DemoShrineBuilder)}] No BaseMap.asset. A SaveRespawnPoint dialog " +
                               "with no map is refused at runtime by ValidateDialog, silently.");
                return;
            }

            Scene scene = default;
            bool wasOpen = false;
            for (int i = 0; i < SceneManager.sceneCount; ++i)
            {
                Scene loaded = SceneManager.GetSceneAt(i);
                if (loaded.isLoaded && loaded.path == DemoSceneBuilder.ScenePath)
                {
                    scene = loaded;
                    wasOpen = true;
                }
            }
            if (!wasOpen)
                scene = EditorSceneManager.OpenScene(DemoSceneBuilder.ScenePath, OpenSceneMode.Additive);

            GameObject root = null;
            foreach (GameObject candidate in scene.GetRootGameObjects())
            {
                if (candidate.name == DemoSceneBuilder.ShrineRootName)
                    root = candidate;
            }
            if (root == null)
            {
                root = new GameObject(DemoSceneBuilder.ShrineRootName);
                SceneManager.MoveGameObjectToScene(root, scene);
            }

            int placed = 0;
            foreach (Site site in sites)
            {
                Transform existing = root.transform.Find(site.Name);
                GameObject shrine;
                if (existing != null)
                {
                    shrine = existing.gameObject;
                }
                else
                {
                    shrine = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                    shrine.name = site.Name;
                    shrine.transform.SetParent(root.transform, true);
                    shrine.transform.rotation = Quaternion.Euler(0f, site.Yaw, 0f);
                    shrine.transform.position = new Vector3(
                        site.World.x, DemoIslandBuilder.HeightAt(site.World.x, site.World.z), site.World.z);
                    ++placed;
                }

                GiveOwnSceneObjectId(shrine, site.Name);
                SeatPieces(shrine);
                AlignColliders(shrine);
                ClearGroundCover(scene, shrine);

                Vector3 bind = shrine.transform.TransformPoint(BindOffset);
                bind.y = DemoIslandBuilder.HeightAt(bind.x, bind.z);
                NpcDialog dialog = BuildDialog(site, map, bind);

                var entity = shrine.GetComponent<NpcEntity>();
                var serialized = new SerializedObject(entity);
                serialized.FindProperty("entityTitle").stringValue = site.Title;
                serialized.FindProperty("startDialog").objectReferenceValue = dialog;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            if (!wasOpen)
                EditorSceneManager.CloseScene(scene, true);
            Debug.Log($"[{nameof(DemoShrineBuilder)}] {placed} shrine(s) placed, " +
                      $"{sites.Length - placed} kept where they stood.");
        }

        /// <summary>
        /// Gives a placed shrine a scene object id of its own.
        ///
        /// This is the one thing two instances of one entity prefab in one map cannot do
        /// for themselves. A scene object is spawned to clients by the hash of its
        /// `sceneObjectId`, that id is serialized **on the prefab**, and the identity's own
        /// `OnValidate` only fills one in when it is *empty* - it does not notice a
        /// duplicate. So a second instance silently inherits the first one's id, the server
        /// sends two spawns for one id, and one of the two shrines never appears. Worse,
        /// `ReadSpawnGameState` logs the failure and keeps reading a stream it is no longer
        /// positioned in, so the symptom is every scene object after it going missing too -
        /// which is how this previously showed up as "none of the NPCs loaded".
        ///
        /// Named rather than numbered, so it is stable across rebuilds: the kit's own
        /// generator would hand out `DemoShrine_1` and `DemoShrine_2` by iteration order,
        /// and the order two shrines are visited in is not something to hang a network id
        /// on.
        /// </summary>
        private static void GiveOwnSceneObjectId(GameObject shrine, string id)
        {
            var identity = shrine.GetComponent<LiteNetLibManager.LiteNetLibIdentity>();
            if (identity == null)
                return;
            if (identity.SceneObjectId == id)
                return;
            var serialized = new SerializedObject(identity);
            serialized.FindProperty("sceneObjectId").stringValue = id;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(shrine);
        }

        /// <summary>
        /// Settles the shrine onto the ground under itself.
        ///
        /// The shrine is one flat prefab and the island is not flat: the best site outside
        /// the village still falls 0.35m across the shrine's own 2.45m footprint, which is
        /// enough to leave a corner of the bottom step hanging in the air.
        ///
        /// **It beds into the lowest ground beneath it** rather than standing on the
        /// highest, so it sinks at the high side instead of standing on one corner at the
        /// low side. Buried reads as settled; floating reads as broken. At the village site
        /// the hill rises to the north and the shrine faces south, so what this buys is the
        /// steps meeting the grass exactly where a player walks up to them and the back of
        /// the stele cut into the bank - which is what a wayside shrine on a slope looks
        /// like.
        ///
        /// It stays plumb. Tilting is for a surface laid along the ground - the paving this
        /// shrine used to stand on was turned to the slope under its own corners, because a
        /// flagstone floor bedded in is simply gone and one laid on the high point hangs
        /// forty centimetres clear at the far corner. A monument has neither problem: it is
        /// upright, and upright is the one thing about it that is not negotiable.
        ///
        /// Idempotent: it works from where the ground is rather than from where the piece
        /// was, so running the builder again over a seated shrine moves nothing.
        /// </summary>
        private static void SeatPieces(GameObject shrine)
        {
            Transform structure = shrine.transform.Find("Structure");
            if (structure == null)
                return;
            foreach (Transform piece in structure)
            {
                if (!WorldBounds(piece, out Bounds bounds))
                    continue;

                float lowest = float.MaxValue;
                foreach (float x in new[] { bounds.min.x, bounds.center.x, bounds.max.x })
                    foreach (float z in new[] { bounds.min.z, bounds.center.z, bounds.max.z })
                        lowest = Mathf.Min(lowest, DemoIslandBuilder.HeightAt(x, z));
                piece.position -= new Vector3(0f, bounds.min.y - lowest, 0f);
            }
        }

        /// <summary>
        /// Brings the collider boxes down with the stone after it has settled.
        ///
        /// **The boxes cannot simply be put on the model and settle with it**, which is the
        /// obvious fix and the wrong one. The kit's `NearbyEntityDetector` resolves what a
        /// player is standing next to with `other.GetComponent&lt;IActivatableEntity&gt;()` on
        /// the collider it overlapped - `GetComponent`, not `GetComponentInParent`. A box
        /// one level down from the `NpcEntity` is a box belonging to nobody, and the shrine
        /// becomes a thing you can walk into and cannot talk to.
        ///
        /// So the boxes stay on the entity and are moved to meet the stone instead. Written
        /// absolutely, from `ColliderCentre` plus the offset the stone actually took, so a
        /// second run re-aligns them rather than pushing them down twice.
        /// </summary>
        private static void AlignColliders(GameObject shrine)
        {
            Transform stone = shrine.transform.Find("Structure/Shrine");
            if (stone == null)
                return;
            float settled = shrine.transform.InverseTransformPoint(stone.position).y;
            BoxCollider[] boxes = shrine.GetComponents<BoxCollider>();
            for (int i = 0; i < boxes.Length && i < ColliderCentre.Length; ++i)
                boxes[i].center = ColliderCentre[i] + new Vector3(0f, settled, 0f);
            EditorUtility.SetDirty(shrine);
        }

        /// <summary>
        /// Clears the painted ground cover from under the shrine's paving.
        ///
        /// The island's grass, clover, ferns, flowers and mushrooms are **terrain detail**,
        /// not scene objects: ten layers painted into `TerrainData`, drawn wherever the
        /// terrain says to draw them and entirely unaware that anything has been built on
        /// top. Laying flagstones does not move them, so the first shrine had grass growing
        /// up through its floor and a fern standing in the middle of the rune ring.
        ///
        /// Cleared to the shrine's own footprint plus a hand's width, so the grass comes
        /// back right at the edge of the bottom step rather than leaving a bald ring around
        /// it. Taken from the renderer bounds rather than from `ColliderSize`, because the
        /// mouldings overhang the boxes by a few centimetres and grass through a moulding
        /// is the thing this exists to stop.
        ///
        /// **`Build Island Terrain` repaints these layers**, which puts the grass back
        /// through the floor. Run this again after it - the same dependency the craft
        /// stations have on a scene regenerate.
        /// </summary>
        private static void ClearGroundCover(Scene scene, GameObject shrine)
        {
            Transform stone = shrine.transform.Find("Structure/Shrine");
            if (stone == null || !WorldBounds(stone, out Bounds bounds))
                return;

            Terrain terrain = null;
            foreach (GameObject candidate in scene.GetRootGameObjects())
            {
                terrain = candidate.GetComponentInChildren<Terrain>(true);
                if (terrain != null)
                    break;
            }
            if (terrain == null)
                return;

            TerrainData data = terrain.terrainData;
            if (data.detailPrototypes.Length == 0)
                return;

            const float Margin = 0.15f;
            float metresPerCell = data.size.x / data.detailResolution;
            Vector3 origin = terrain.transform.position;
            int minX = Mathf.FloorToInt((bounds.min.x - Margin - origin.x) / metresPerCell);
            int minZ = Mathf.FloorToInt((bounds.min.z - Margin - origin.z) / metresPerCell);
            int maxX = Mathf.CeilToInt((bounds.max.x + Margin - origin.x) / metresPerCell);
            int maxZ = Mathf.CeilToInt((bounds.max.z + Margin - origin.z) / metresPerCell);
            minX = Mathf.Clamp(minX, 0, data.detailResolution - 1);
            minZ = Mathf.Clamp(minZ, 0, data.detailResolution - 1);
            int width = Mathf.Clamp(maxX - minX + 1, 1, data.detailResolution - minX);
            int height = Mathf.Clamp(maxZ - minZ + 1, 1, data.detailResolution - minZ);

            // The detail map is indexed [z, x], which is the opposite way round from the
            // rect that selects it - a transposed patch clears a strip beside the shrine
            // rather than under it, and looks like nothing happened at all.
            var empty = new int[height, width];
            for (int layer = 0; layer < data.detailPrototypes.Length; ++layer)
                data.SetDetailLayer(minX, minZ, layer, empty);
            EditorUtility.SetDirty(data);
        }

        /// <summary>
        /// What a piece actually occupies, in world space.
        ///
        /// Particles are left out: a flame's renderer bounds are the volume its particles
        /// are allowed to reach, which on a brazier reaches well below the basket, so
        /// including one would have the builder seat the fire on the ground and leave the
        /// brazier under it in a hole.
        /// </summary>
        private static bool WorldBounds(Transform piece, out Bounds bounds)
        {
            bounds = new Bounds();
            bool any = false;
            foreach (Renderer renderer in piece.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is ParticleSystemRenderer)
                    continue;
                if (!any)
                {
                    bounds = renderer.bounds;
                    any = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }
            return any;
        }

        // ---- pieces ----------------------------------------------------------

        private static void Box(GameObject on, Vector3 centre, Vector3 size)
        {
            var box = on.AddComponent<BoxCollider>();
            box.center = centre;
            box.size = size;
        }

        private static Transform Anchor(Transform parent, string name, float height)
        {
            var anchor = new GameObject(name);
            anchor.transform.SetParent(parent, false);
            anchor.transform.localPosition = new Vector3(0f, height, 0f);
            return anchor.transform;
        }

        private static T Create<T>(string path) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null)
                return existing;
            var created = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(created, path);
            return created;
        }
    }
}
