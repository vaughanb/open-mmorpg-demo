using MultiplayerARPG.GameData.Model.Playables;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// Builds the demo's rideable horse: the animator that picks its gait, the vehicle
    /// entity itself, and the item that summons it.
    ///
    /// There is no vehicle anywhere in the kit's demo to clone — unlike characters, which
    /// <see cref="DemoEntityBuilder"/> copies from a tuned template — so this assembles the
    /// component set from scratch. A vehicle needs less than a character does: it never
    /// attacks, casts or loots. What it does need is a movement component, because
    /// `VehicleEntity` is a `BaseGameEntity` that moves itself while the rider is snapped to
    /// a seat and their own movement is switched off (`BaseGameEntity.EntityUpdate`).
    ///
    /// The horse mesh is a generated model retopologised and rigged in Blender, carrying the
    /// CC0 Quaternius animal skeleton and its thirteen clips retargeted onto it, so the gait
    /// names below are that library's.
    /// </summary>
    public static class DemoMountBuilder
    {
        private const string GameDataDir = "Assets/OpenMMORPG/Demo/GameData";
        private const string ResourcesDir = GameDataDir + "/Resources";
        private const string VehicleDir = "Assets/OpenMMORPG/Demo/Prefabs/GamePlay/Vehicles";
        private const string AnimationDir = "Assets/OpenMMORPG/Demo/Animations";
        private const string ModelPath = "Assets/OpenMMORPG/Demo/Meshes/Horse.fbx";
        private const string MaterialPath = "Assets/OpenMMORPG/Demo/Materials/Horse.mat";

        private const string ModelDir = "Assets/OpenMMORPG/Demo/Prefabs/GamePlay/CharacterModels";
        /// <summary>The body the seat is measured against. The two share a skeleton, so one measurement fits both.</summary>
        private const string RiderModelPath = ModelDir + "/PlayerCharacterModel_Male.prefab";
        private const string RiderModelName = "ModelRiding";
        private const string SitClip = "Sitting_Idle_Loop";

        /// <summary>
        /// How far out from the hip joint counts as pelvis when looking for where a seated
        /// rider makes contact. Wide enough to take in both seat bones, tight enough to
        /// exclude the thighs, which in a seated pose run forward at about this height.
        /// </summary>
        private const float PelvisRadius = 0.22f;

        /// <summary>
        /// How far each thigh is swung out, and dropped, from the chair-sitting pose — the
        /// figures handed to <see cref="DemoRiderLegs"/>.
        ///
        /// Both are needed. A chair sit holds the thighs **horizontal**, at the height of the
        /// horse's spine, so opening them alone splays them across the top of the barrel
        /// instead of down its sides.
        ///
        /// Chosen against a measured figure — the share of the rider's leg vertices that end
        /// up inside the horse's mesh — rather than by eye:
        ///
        /// | abduction | drop | legs inside the horse |
        /// |---|---|---|
        /// | 0 deg | 0 deg | **24.0%** |
        /// | 20 | 0 | 11.7% |
        /// | 25 | 30 | 0.6% |
        /// | **35** | **45** | **0.0%** |
        ///
        /// The barrel is 0.33–0.35m in half-width where the rider sits, which is what the
        /// knee has to clear; at 35 degrees it reaches 0.407m. Wider reads as the splits.
        /// </summary>
        private const float RiderAbductionDegrees = 35f;

        private const float RiderDropDegrees = 45f;

        private const string ControllerPath = AnimationDir + "/Horse.controller";
        private const string HorsePrefabPath = VehicleDir + "/DemoHorse.prefab";
        private const string VehicleTypePath = ResourcesDir + "/VehicleTypes/Horse.asset";
        private const string MountItemPath = ResourcesDir + "/Items/HorseWhistle.asset";

        /// <summary>
        /// How fast the horse travels. The demo's characters run at 4 m/s, and a mount that
        /// is not decisively faster is not worth the inventory slot; a real horse canters at
        /// about this speed, so the gallop clip does not have to be stretched to cover it.
        /// </summary>
        private const float MountSpeed = 8f;

        /// <summary>
        /// Gait thresholds for the blend tree, in metres per second. Walk is a real horse's
        /// walking pace; the gallop threshold sits at the mount's own top speed so the clip
        /// plays at full weight whenever the rider is actually travelling.
        /// </summary>
        private const float WalkSpeed = 1.6f;

        [MenuItem("Open MMORPG/Demo/Build Mounts")]
        public static void BuildAll()
        {
            DemoItemBuilder.EnsureFolder(VehicleDir);
            DemoItemBuilder.EnsureFolder(AnimationDir);
            DemoItemBuilder.EnsureFolder(ResourcesDir + "/VehicleTypes");
            DemoItemBuilder.EnsureFolder(ResourcesDir + "/Items");

            AnimatorController controller = BuildController();
            // The seat is placed against where the rider's weight lands, so the riding pose
            // has to be measured before the horse is built.
            Vector3 riderContact = MeasureRiderContact();
            VehicleEntity horse = BuildHorse(controller, riderContact);
            if (horse == null)
                return;
            BuildMountItem(horse);
            // Every body a player can be needs its own seated copy of itself.
            foreach (string gender in DemoEntityBuilder.PlayerBodies)
                BuildRider(DemoEntityBuilder.PlayerEntityPath(gender), $"{ModelDir}/PlayerCharacterModel_{gender}.prefab");

            PlaceInScene();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[{nameof(DemoMountBuilder)}] Built the horse mount. Re-run " +
                      "\"Wire Game Database\" so the whistle is registered as an item.");
        }

        // ---- placement -------------------------------------------------------

        /// <summary>
        /// Where the horse waits, relative to the village centre: just inside the fence
        /// in the gap north-west of the green, stood along the rails the way a tethered
        /// animal is, with its head toward the green. The user asked for it there; it
        /// first stood by the cage wagon on the west lane.
        /// </summary>
        public static readonly Vector3 HorseLocalPosition = new Vector3(-6.4f, 0f, 12.2f);

        /// <summary>
        /// The fence's own run, measured off the placed panels: they lie on a line
        /// bearing 62 degrees, so the horse does too. A kit-driven horse walks along
        /// its +Z, and this is the yaw of that.
        /// </summary>
        public const float HorseLocalYaw = 62f;

        /// <summary>
        /// Stands the horse in the map scene, the way DemoNpcBuilder stands the NPCs: a
        /// prefab instance under a root the scene rebuild keeps, so it can be moved by
        /// hand afterwards and stays moved. A player walks up and activates it to mount;
        /// nothing else is needed, because the whistle that summons one has no vendor
        /// yet and a horse that is simply there is the better demonstration anyway.
        /// Run again, it only puts the horse back if it is missing.
        /// </summary>
        [MenuItem("Open MMORPG/Demo/Place Mounts")]
        public static void PlaceInScene()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HorsePrefabPath);
            if (prefab == null)
            {
                Debug.LogError($"[{nameof(DemoMountBuilder)}] No horse at {HorsePrefabPath}. Run Build Mounts first.");
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
                if (candidate.name == DemoSceneBuilder.MountRootName)
                    root = candidate;
            }
            if (root == null)
            {
                root = new GameObject(DemoSceneBuilder.MountRootName);
                SceneManager.MoveGameObjectToScene(root, scene);
            }

            if (root.transform.Find("Horse") == null)
            {
                Vector2 village = DemoIslandBuilder.VillageCentre;
                var world = new Vector3(village.x + HorseLocalPosition.x, 0f, village.y + HorseLocalPosition.z);
                world.y = DemoIslandBuilder.HeightAt(world.x, world.z);
                var horse = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                horse.name = "Horse";
                horse.transform.SetParent(root.transform, true);
                horse.transform.position = world;
                horse.transform.rotation = Quaternion.Euler(0f, HorseLocalYaw, 0f);
                Debug.Log($"[{nameof(DemoMountBuilder)}] Placed the horse at {world:F1}.");
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            if (!wasOpen)
                EditorSceneManager.CloseScene(scene, true);
        }

        /// <summary>
        /// A one-dimensional blend on measured speed: standing, walking, galloping.
        ///
        /// Blended rather than switched by state, because the rider accelerates smoothly and
        /// a hard cut between a walk and a gallop reads as a skip. The trot is deliberately
        /// left out — the library has no trot, and blending walk against gallop across that
        /// gap already produces something close enough at the speeds in between.
        /// </summary>
        private static AnimatorController BuildController()
        {
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
                controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

            for (int i = controller.parameters.Length - 1; i >= 0; --i)
                controller.RemoveParameter(i);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);

            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            for (int i = machine.states.Length - 1; i >= 0; --i)
                machine.RemoveState(machine.states[i].state);

            var tree = new BlendTree
            {
                name = "Gait",
                blendType = BlendTreeType.Simple1D,
                blendParameter = "Speed",
                useAutomaticThresholds = false,
            };
            AssetDatabase.AddObjectToAsset(tree, controller);
            tree.AddChild(Clip("Idle"), 0f);
            tree.AddChild(Clip("Walk"), WalkSpeed);
            tree.AddChild(Clip("Gallop"), MountSpeed);

            AnimatorState state = machine.AddState("Gait");
            state.motion = tree;
            machine.defaultState = state;

            EditorUtility.SetDirty(controller);
            return controller;
        }

        /// <summary>Pulls one clip out of the model, by the name the FBX takes were given.</summary>
        private static AnimationClip Clip(string name)
        {
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(ModelPath))
            {
                var clip = asset as AnimationClip;
                if (clip != null && clip.name == name)
                    return clip;
            }
            Debug.LogError($"[{nameof(DemoMountBuilder)}] \"{ModelPath}\" has no clip \"{name}\".");
            return null;
        }

        private static VehicleEntity BuildHorse(AnimatorController controller, Vector3 riderContact)
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (model == null || material == null)
            {
                Debug.LogError($"[{nameof(DemoMountBuilder)}] Missing \"{ModelPath}\" or \"{MaterialPath}\".");
                return null;
            }

            var entity = new GameObject("DemoHorse");
            try
            {
                GameObject modelInstance = (GameObject)PrefabUtility.InstantiatePrefab(model);
                modelInstance.transform.SetParent(entity.transform, false);
                modelInstance.name = "Model";

                // The model imports with no material at all: the FBX is brought in with
                // material import switched off, because the generated one it carries is a
                // Blender Principled node the URP shader cannot read.
                var renderer = modelInstance.GetComponentInChildren<SkinnedMeshRenderer>();
                renderer.sharedMaterial = material;

                Animator animator = modelInstance.GetComponent<Animator>();
                if (animator == null)
                    animator = modelInstance.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                modelInstance.AddComponent<DemoMountAnimator>();

                Vector3 saddle = MeasureSaddle(entity.transform, renderer);
                // The kit snaps the rider's *root* to this transform, but what has to meet
                // the saddle is the rider's seat — and in the sitting pose that is 0.48m up
                // and a third of a metre forward of the root. Subtracting the measured
                // contact puts the weight on the saddle instead of the character's heels.
                Vector3 seatPosition = saddle - riderContact;

                var transforms = new GameObject("Transforms");
                transforms.transform.SetParent(entity.transform, false);
                Transform seat = Anchor(transforms.transform, "Seat", seatPosition);
                // Dismount to the horse's left — the side a rider mounts from — and far
                // enough out that the character controller does not spawn inside the barrel.
                Transform exit = Anchor(transforms.transform, "Exit", new Vector3(-1.1f, 0f, 0f));
                Transform characterUi = Anchor(transforms.transform, "UIElementContainer", new Vector3(0f, 2.4f, 0f));
                Transform miniMapUi = Anchor(transforms.transform, "MiniMapContainer", Vector3.zero);

                // A capsule is a poor fit for something 2.4m long, but CharacterController is
                // what CharacterControllerEntityMovement drives. Sized to the barrel rather
                // than the whole body, so the horse can still be walked between the houses.
                var controllerCollider = entity.AddComponent<CharacterController>();
                controllerCollider.height = 1.6f;
                controllerCollider.radius = 0.5f;
                controllerCollider.center = new Vector3(0f, 0.9f, 0f);
                entity.AddComponent<CharacterControllerEntityMovement>();
                // A horse swims with its legs under and its barrel at the surface.
                DemoEntityBuilder.SwimOnSurface(entity, 0.9f);

                VehicleEntity vehicle = entity.AddComponent<VehicleEntity>();
                vehicle.Seats.Add(new VehicleSeat
                {
                    cameraTarget = VehicleSeatCameraTarget.Vehicle,
                    passengingTransform = seat,
                    exitTransform = exit,
                    canAttack = false,
                    canUseSkill = false,
                    hidePassenger = false,
                });

                var serialized = new SerializedObject(vehicle);
                serialized.FindProperty("vehicleType").objectReferenceValue = VehicleTypeAsset();
                serialized.FindProperty("moveSpeedType").enumValueIndex = (int)VehicleMoveSpeedType.FixedMovedSpeed;
                serialized.FindProperty("moveSpeed").floatValue = MountSpeed;
                serialized.FindProperty("canBeAttacked").boolValue = false;
                SetIfPresent(serialized, "characterUiTransform", characterUi);
                SetIfPresent(serialized, "miniMapUiTransform", miniMapUi);
                serialized.ApplyModifiedPropertiesWithoutUndo();

                DemoAudioWiring.WireHorse(entity);
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(entity, HorsePrefabPath);
                // Forced for the same reason DemoEntityBuilder forces it: the editor's loaded
                // copy of a prefab is not refreshed by writing a new file over it.
                AssetDatabase.ImportAsset(HorsePrefabPath, ImportAssetOptions.ForceUpdate);
                Debug.Log($"[{nameof(DemoMountBuilder)}] Built {HorsePrefabPath}. Saddle {saddle}, " +
                          $"rider contact {riderContact}, seat {seatPosition}.");
                return saved.GetComponent<VehicleEntity>();
            }
            finally
            {
                Object.DestroyImmediate(entity);
            }
        }

        /// <summary>
        /// Finds where a saddle would sit, rather than guessing at it.
        ///
        /// A horse's back is not flat: it falls away behind the withers and rises again at
        /// the croup, and the dip between the two is the only place a rider sits. So the
        /// topline is sampled across that stretch and the lowest point of it wins. Measured
        /// because the mesh is a generated one — nothing about its proportions is known in
        /// advance, and a typed-in height would be wrong the moment the model changed.
        /// </summary>
        private static Vector3 MeasureSaddle(Transform root, SkinnedMeshRenderer renderer)
        {
            Vector3[] vertices = renderer.sharedMesh.vertices;
            // Behind the withers, ahead of the croup. The horse faces +Z, so this is the
            // stretch just behind the shoulder.
            const float from = -0.55f, to = 0.05f;
            // Only the strip of mesh running along the spine, and only what is clearly on
            // the back rather than the belly or a leg. Slicing the band into bins and taking
            // the highest vertex in each does NOT work here: the retopologised mesh carries
            // about 3,500 vertices, so a thin slice can hold nothing but flank geometry and
            // reports a "topline" a quarter of a metre too low.
            const float spineHalfWidth = 0.12f;
            float backHeight = renderer.bounds.size.y * 0.5f;

            float bestY = float.MaxValue;
            float bestZ = 0f;
            int found = 0;
            for (int i = 0; i < vertices.Length; ++i)
            {
                Vector3 p = root.InverseTransformPoint(renderer.transform.TransformPoint(vertices[i]));
                if (p.z < from || p.z >= to)
                    continue;
                if (Mathf.Abs(p.x) > spineHalfWidth || p.y < backHeight)
                    continue;
                ++found;
                if (p.y >= bestY)
                    continue;
                bestY = p.y;
                bestZ = p.z;
            }

            if (found == 0)
            {
                Debug.LogError($"[{nameof(DemoMountBuilder)}] Found no back to seat a rider on.");
                return new Vector3(0f, renderer.bounds.size.y * 0.7f, 0f);
            }
            // Centred on the spine even though the sampled vertex rarely is.
            return new Vector3(0f, bestY, bestZ);
        }

        /// <summary>
        /// Where a seated rider's weight rests, relative to their own root transform.
        ///
        /// Measured off the riding pose rather than assumed, because the clip does not leave
        /// the character sitting where its root is — the pelvis lands ten centimetres off it
        /// in Z and nearly half a metre up, and the seat has to be placed against that.
        ///
        /// The contact point is the underside of the pelvis in the posed *mesh*, not the hip
        /// joint — the joint sits ~8cm inside the body, and seating by it sinks the rider
        /// into the horse.
        ///
        /// **Posed through a PlayableGraph, which is the animation runtime.**
        /// `SampleAnimation` bypasses it and so ignores the orientation correction the whole
        /// library is imported with, producing a pose that is turned 180 degrees. Measuring
        /// there gets the Z offset backwards and seats the rider a third of a metre too far
        /// towards the tail — which is exactly what happened.
        /// </summary>
        private static Vector3 MeasureRiderContact()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RiderModelPath);
            AnimationClip sit = DemoAnimationSet.Clip(SitClip);
            if (prefab == null || sit == null)
            {
                Debug.LogError($"[{nameof(DemoMountBuilder)}] Cannot measure the rider: missing " +
                               $"\"{RiderModelPath}\" or clip \"{SitClip}\".");
                return Vector3.zero;
            }

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            var graph = UnityEngine.Playables.PlayableGraph.Create("DemoMountBuilder.MeasureRider");
            try
            {
                inst.transform.position = Vector3.zero;
                inst.transform.rotation = Quaternion.identity;
                Animator animator = inst.GetComponentInChildren<Animator>();
                animator.applyRootMotion = false;
                var output = UnityEngine.Animations.AnimationPlayableOutput.Create(graph, "out", animator);
                UnityEngine.Playables.PlayableOutputExtensions.SetSourcePlayable(output,
                    UnityEngine.Animations.AnimationClipPlayable.Create(graph, sit));
                graph.Evaluate(0.3f);
                // Posed exactly as the game will pose it, legs included.
                var legs = animator.gameObject.AddComponent<DemoRiderLegs>();
                legs.Configure(RiderAbductionDegrees, RiderDropDegrees);
                legs.Apply();

                Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                float lowest = float.MaxValue;
                float sumX = 0f, sumZ = 0f;
                int counted = 0;
                foreach (SkinnedMeshRenderer smr in inst.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    var baked = new Mesh();
                    smr.BakeMesh(baked, true);
                    Vector3[] vs = baked.vertices;
                    for (int i = 0; i < vs.Length; ++i)
                    {
                        Vector3 w = smr.transform.TransformPoint(vs[i]);
                        if (w.y > hips.position.y)
                            continue;
                        var flat = new Vector2(w.x - hips.position.x, w.z - hips.position.z);
                        if (flat.magnitude > PelvisRadius)
                            continue;
                        ++counted;
                        sumX += w.x;
                        sumZ += w.z;
                        if (w.y < lowest)
                            lowest = w.y;
                    }
                    Object.DestroyImmediate(baked);
                }

                if (counted == 0)
                {
                    Debug.LogError($"[{nameof(DemoMountBuilder)}] Found no pelvis on the rider.");
                    return Vector3.zero;
                }
                return new Vector3(sumX / counted, lowest, sumZ / counted);
            }
            finally
            {
                if (graph.IsValid())
                    graph.Destroy();
                Object.DestroyImmediate(inst);
            }
        }

        /// <summary>
        /// Gives the player a second model that sits, and points the horse's vehicle type at it.
        ///
        /// This is the kit's own mechanism and the only one there is: `VehicleEntity` does
        /// nothing to a rider's pose, and `CharacterModelManager.UpdatePassengingVehicle`
        /// swaps in `modelsForEachSeats[seat]` when the vehicle type matches. Those entries
        /// are **live models in the entity's hierarchy**, not prefabs instantiated on demand,
        /// so the riding body has to sit in the prefab beside the walking one.
        ///
        /// Driving `ExtraMovementState.IsSitting` instead would be far cheaper — the sitting
        /// clips are already in the playable graph — but it is wrong here: sitting makes
        /// `CanTurn()` false, and `ShooterPlayerCharacterController` gates the camera's Y
        /// rotation on exactly that, so the player would be unable to look around while
        /// mounted.
        /// </summary>
        private static void BuildRider(string playerEntityPath, string riderModelPath)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(playerEntityPath) == null)
            {
                Debug.LogError($"[{nameof(DemoMountBuilder)}] Missing \"{playerEntityPath}\"; run Build Character Entities first.");
                return;
            }
            GameObject contents = PrefabUtility.LoadPrefabContents(playerEntityPath);
            try
            {
                var manager = contents.GetComponent<CharacterModelManager>();
                BaseCharacterModel main = manager != null ? manager.MainTpsModel : null;
                if (main == null && manager != null)
                {
                    // A nested prefab's component is referenced through a stripped entry that
                    // records its script; swapping the body's script (EnsureDemoCharacterModel
                    // below, on a body built before it existed) leaves that entry stale and
                    // the reference reads null until this prefab is saved again. Re-point it.
                    Transform model = contents.transform.Find("Model");
                    main = model != null ? model.GetComponent<BaseCharacterModel>() : null;
                    manager.MainTpsModel = main;
                }
                if (main == null)
                {
                    Debug.LogError($"[{nameof(DemoMountBuilder)}] \"{playerEntityPath}\" has no main TPS model.");
                    return;
                }

                // Rebuildable: drop any riding model a previous run left behind.
                Transform existing = contents.transform.Find(RiderModelName);
                if (existing != null)
                    Object.DestroyImmediate(existing.gameObject);

                // The rider is a second copy of the body with its own skeleton, and the kit
                // dresses a seat model through the *main* model's containers - so with the
                // stock model class the rider sat on the horse in its bare default look while
                // its gear went onto the hidden body. DemoCharacterModel claims its own
                // containers when switched to; make sure the body prefab carries it.
                if (!DemoCharacterBuilder.EnsureDemoCharacterModel(riderModelPath))
                {
                    Debug.LogError($"[{nameof(DemoMountBuilder)}] Missing \"{riderModelPath}\"; run Build Character Models first.");
                    return;
                }
                var modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(riderModelPath);
                var riding = (GameObject)PrefabUtility.InstantiatePrefab(modelPrefab, contents.transform);
                riding.name = RiderModelName;
                riding.transform.SetParent(contents.transform, false);
                var ridingModel = riding.GetComponent<DemoCharacterModel>();

                ridingModel.defaultAnimations = RidingAnimations();
                // Opens the legs after the animator has posed them; the clip itself cannot
                // be re-posed, see DemoRiderLegs.
                var legs = riding.GetComponent<DemoRiderLegs>();
                if (legs == null)
                    legs = riding.AddComponent<DemoRiderLegs>();
                legs.Configure(RiderAbductionDegrees, RiderDropDegrees);
                // Deliberately empty. A weapon set would put the character back into its
                // standing guard the moment a sword was equipped, overriding the seated idle;
                // with none, the kit falls back to the default set, which is the sitting one.
                ridingModel.weaponAnimations = new WeaponAnimations[0];

                // Nothing toggles these model objects on its own - SwitchModel only acts on
                // the two lists below. Both bodies are left active in the prefab so each one's
                // Awake runs and builds its playable graph; the main model's switch-in at
                // spawn is what hides the rider.
                var mainSerialized = new SerializedObject(main);
                SetObjectArray(mainSerialized, "activateObjectsWhenSwitchModel", new GameObject[0]);
                SetObjectArray(mainSerialized, "deactivateObjectsWhenSwitchModel", new[] { riding });
                SetVehicleModels(mainSerialized, riding.GetComponent<BaseCharacterModel>());
                mainSerialized.ApplyModifiedPropertiesWithoutUndo();

                var ridingSerialized = new SerializedObject(ridingModel);
                SetObjectArray(ridingSerialized, "activateObjectsWhenSwitchModel", new[] { riding });
                SetObjectArray(ridingSerialized, "deactivateObjectsWhenSwitchModel", new[] { main.gameObject });
                ridingSerialized.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(contents, playerEntityPath);
                AssetDatabase.ImportAsset(playerEntityPath, ImportAssetOptions.ForceUpdate);
                Debug.Log($"[{nameof(DemoMountBuilder)}] Added \"{RiderModelName}\" to {playerEntityPath}; " +
                          "the rider now sits on the horse.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        /// <summary>
        /// The seated set. Every locomotion state is the same sitting loop on purpose: while
        /// mounted the rider's own movement is switched off and the vehicle carries them, but
        /// the model is still handed a movement state, and anything other than the sitting
        /// clip in those slots shows as the rider jogging on the horse's back.
        /// </summary>
        private static DefaultAnimations RidingAnimations()
        {
            // The library's own clip, untouched. Its legs are wrong for a horse, but they
            // are corrected afterwards by DemoRiderLegs rather than in the clip - the clips
            // carry IK goal curves that override any muscle edit.
            AnimState sit = DemoAnimationSet.State(SitClip);
            MoveStates sitMoves = DemoAnimationSet.Moves(SitClip);
            return new DefaultAnimations
            {
                idleState = sit,
                moveStates = sitMoves,
                sprintStates = sitMoves,
                walkStates = sitMoves,

                crouchIdleState = sit,
                crouchMoveStates = sitMoves,
                crawlIdleState = sit,
                crawlMoveStates = sitMoves,

                swimIdleState = sit,
                swimMoveStates = sitMoves,

                jumpState = sit,
                fallState = sit,
                landedState = sit,

                hurtState = DemoAnimationSet.Action("Hit_Chest"),
                deadState = DemoAnimationSet.State("Death01"),

                sittingStartState = DemoAnimationSet.State("Sitting_Enter"),
                sittingLoopState = sit,
                sittingEndState = DemoAnimationSet.State("Sitting_Exit"),
            };
        }

        private static void SetVehicleModels(SerializedObject mainModel, BaseCharacterModel ridingModel)
        {
            SerializedProperty list = mainModel.FindProperty("vehicleModels");
            if (list == null)
            {
                Debug.LogError($"[{nameof(DemoMountBuilder)}] Character model has no \"vehicleModels\".");
                return;
            }
            list.ClearArray();
            list.InsertArrayElementAtIndex(0);
            SerializedProperty entry = list.GetArrayElementAtIndex(0);
            entry.FindPropertyRelative("vehicleType").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<VehicleType>(VehicleTypePath);
            SerializedProperty seats = entry.FindPropertyRelative("modelsForEachSeats");
            seats.ClearArray();
            seats.InsertArrayElementAtIndex(0);
            seats.GetArrayElementAtIndex(0).objectReferenceValue = ridingModel;
        }

        private static void SetObjectArray(SerializedObject serialized, string field, GameObject[] values)
        {
            SerializedProperty list = serialized.FindProperty(field);
            if (list == null)
            {
                Debug.LogError($"[{nameof(DemoMountBuilder)}] Character model has no \"{field}\".");
                return;
            }
            list.ClearArray();
            for (int i = 0; i < values.Length; ++i)
            {
                list.InsertArrayElementAtIndex(i);
                list.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
        }

        private static Transform Anchor(Transform parent, string name, Vector3 localPosition)
        {
            var anchor = new GameObject(name);
            anchor.transform.SetParent(parent, false);
            anchor.transform.localPosition = localPosition;
            return anchor.transform;
        }

        private static void SetIfPresent(SerializedObject serialized, string field, Object value)
        {
            SerializedProperty property = serialized.FindProperty(field);
            if (property != null)
                property.objectReferenceValue = value;
        }

        private static VehicleType VehicleTypeAsset()
        {
            var type = AssetDatabase.LoadAssetAtPath<VehicleType>(VehicleTypePath);
            if (type == null)
            {
                type = ScriptableObject.CreateInstance<VehicleType>();
                AssetDatabase.CreateAsset(type, VehicleTypePath);
            }
            var serialized = new SerializedObject(type);
            SetTitle(serialized, "Horse");
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return type;
        }

        /// <summary>
        /// The item that summons the horse.
        ///
        /// A whistle rather than the horse itself, because `MountItem.UseItem` spawns the
        /// mount and leaves the item in the bag — so it reads as something you keep and
        /// blow, not something you consume. `noMountDuration` keeps the horse out
        /// indefinitely; the rider dismisses it by dismounting.
        /// </summary>
        private static void BuildMountItem(VehicleEntity horse)
        {
            var item = AssetDatabase.LoadAssetAtPath<MountItem>(MountItemPath);
            if (item == null)
            {
                item = ScriptableObject.CreateInstance<MountItem>();
                AssetDatabase.CreateAsset(item, MountItemPath);
            }

            var serialized = new SerializedObject(item);
            SetTitle(serialized, "Horse Whistle");
            SetIfPresent(serialized, "mountEntity", horse);
            SerializedProperty noDuration = serialized.FindProperty("noMountDuration");
            if (noDuration != null)
                noDuration.boolValue = true;
            SerializedProperty sellPrice = serialized.FindProperty("sellPrice");
            if (sellPrice != null)
                sellPrice.intValue = 250;
            SerializedProperty maxStack = serialized.FindProperty("maxStack");
            if (maxStack != null)
                maxStack.intValue = 1;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(item);
            Debug.Log($"[{nameof(DemoMountBuilder)}] Built {MountItemPath}.");
        }

        /// <summary>
        /// Game data titles live behind a language list in this kit, with a plain string
        /// field as the fallback. Both are written so the name shows whatever the language
        /// setting is.
        /// </summary>
        private static void SetTitle(SerializedObject serialized, string title)
        {
            SerializedProperty defaultTitle = serialized.FindProperty("defaultTitle");
            if (defaultTitle != null)
                defaultTitle.stringValue = title;
            // Renamed from "titles" upstream, and still carries that FormerlySerializedAs.
            SerializedProperty titles = serialized.FindProperty("languageSpecificTitles");
            if (titles != null && titles.isArray)
                titles.ClearArray();
        }

        /// <summary>Everything this builder produced, for registering in the game database.</summary>
        public static List<Object> AllVehicleTypes()
        {
            var found = new List<Object>();
            foreach (string guid in AssetDatabase.FindAssets("t:VehicleType", new[] { ResourcesDir + "/VehicleTypes" }))
                found.Add(AssetDatabase.LoadAssetAtPath<Object>(AssetDatabase.GUIDToAssetPath(guid)));
            return found;
        }
    }
}
