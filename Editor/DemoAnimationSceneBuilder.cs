using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// Builds the workbench the demo's character animation is edited on: the two player
    /// bodies standing on a measured floor, each with a live Animator and a weapon in each
    /// hand, and nothing else in the scene to get in the way.
    ///
    /// It exists because neither of the places a character normally appears will do. The
    /// demo map runs the whole game around it, and in a prefab stage there is no floor, no
    /// second body to compare against and no Animator the Animation window will talk to.
    /// This scene is only the parts needed to judge a pose: a body, a clip, a grip and a
    /// shadow.
    ///
    /// **The Animator is the whole point of the build.** The model prefabs ship with
    /// `m_Controller` empty, because at runtime <see cref="GameData.Model.Playables.PlayableCharacterModel"/>
    /// drives the Animator through a PlayableGraph it builds itself and never uses a
    /// controller at all. That is right for the game and useless in the editor: with no
    /// controller the Animation window has no clip list, offers only to create one, and
    /// drops it wherever it likes. So the build writes <see cref="ControllerPath"/> — every
    /// clip this rig can play, one state each — and assigns it to the two scene instances
    /// as a prefab override. The prefabs themselves are left alone, so nothing about the
    /// game changes.
    ///
    /// **Rebuilding never removes a state.** A clip authored here through the Animation
    /// window's "Create New Clip" is added to that controller, and wiping the controller on
    /// the next build would unhook it — the .anim would survive on disk with nothing
    /// pointing at it, which reads exactly like a lost animation. The controller is
    /// therefore topped up, not rewritten.
    ///
    /// The scene itself is disposable and is rebuilt from scratch every time, so anything
    /// worth keeping has to be somewhere else by the time you rerun this. For a tuned grip
    /// that somewhere is the weapon's game data: `Open MMORPG > Demo > Save Weapon Grips
    /// From Scene`, or the button on the component.
    ///
    /// Not in `Assets/OpenMMORPG`, because it is a bench and not part of the demo — see
    /// `Verify Demo Is Self-Contained`. It may reference the demo; the demo must not
    /// reference it.
    /// </summary>
    public static class DemoAnimationSceneBuilder
    {
        /// <summary>
        /// The name `DemoWeaponGripCapture` tells people to open, so it stays put.
        /// </summary>
        public const string ScenePath = "Assets/Scenes/AnimationEditing.unity";
        public const string ControllerPath = "Assets/Scenes/AnimationEditing.controller";
        private const string FloorMaterialPath = "Assets/Scenes/AnimationEditingFloor.mat";
        private const string FloorTexturePath = "Assets/Scenes/AnimationEditingFloor.png";

        private const string ModelDir = "Assets/OpenMMORPG/Demo/Prefabs/GamePlay/CharacterModels";
        private const string ItemDir = "Assets/OpenMMORPG/Demo/GameData/Resources/Items";
        private const string SkillDir = "Assets/OpenMMORPG/Demo/GameData/Resources/Skills";

        /// <summary>Half the gap between the two bodies. Wide enough not to overlap arms in a T-pose.</summary>
        private const float Spread = 0.9f;

        /// <summary>
        /// One square of the floor check, in metres. Half a stride, so a foot that slides
        /// during a loop crosses a boundary and the slide becomes something you can see
        /// rather than something you have to feel.
        /// </summary>
        private const float FloorSquare = 0.5f;

        /// <summary>
        /// What each body starts wearing and holding. Both hands are wired on both bodies
        /// whether or not the item starts equipped, so swapping to another weapon is one
        /// field and never "which component do I add".
        ///
        /// The two are dressed differently on purpose. A clip has to read on a bare figure —
        /// that is the only way to see where a limb really is — and it also has to read
        /// through a jerkin and a pair of boots, which move with the body and can hide a
        /// beat entirely. Each garment is its own preview, so any piece comes off with one
        /// tick.
        /// </summary>
        private static readonly Loadout[] Loadouts =
        {
            new Loadout("Male", "PlayerCharacterModel_Male", -Spread,
                        rightHand: "IronLongsword", leftHand: "PaintedRoundShield",
                        skill: "Cleave",
                        // The guard the sword is drawn for, not a generic stance: this is
                        // the pose a grip is judged against, and the bind pose's open palm
                        // is 7.5cm from where the hand actually closes.
                        idle: "Sword_Idle",
                        armour: new[] { "PeasantTunic", "PeasantSleeves", "PeasantTrousers", "PeasantShoes" },
                        leftHandEquipped: true),
            new Loadout("Female", "PlayerCharacterModel_Female", Spread,
                        rightHand: "IronShortsword", leftHand: "YewLongbow",
                        // the bow skill, because it is the one that borrows the weapon's own
                        // attack instead of carrying a clip, and that is worth seeing work
                        skill: "AimedShot",
                        // The bow has no rest pose in either library, so the plain idle
                        // stands in - the same stand-in `BuildRanged` uses in the game.
                        idle: "Idle_Loop",
                        // no hood: it hides the hair, and the head is where a look-at or a
                        // turn is judged from
                        armour: new[] { "RangerJerkin", "RangerBracers", "RangerBreeches", "RangerBoots" },
                        rightHandEquipped: false, leftHandEquipped: true),
        };

        private struct Loadout
        {
            public readonly string Name;
            public readonly string Model;
            public readonly float X;
            public readonly string RightHand;
            public readonly string LeftHand;
            public readonly string Skill;
            /// <summary>The clip the body is left standing in. See <see cref="PoseIdle"/> for why it is not the bind pose.</summary>
            public readonly string Idle;
            public readonly string[] Armour;
            public readonly bool RightHandEquipped;
            public readonly bool LeftHandEquipped;

            public Loadout(string name, string model, float x, string rightHand, string leftHand,
                           string skill, string idle, string[] armour,
                           bool rightHandEquipped = true, bool leftHandEquipped = false)
            {
                Name = name;
                Model = model;
                X = x;
                RightHand = rightHand;
                LeftHand = leftHand;
                Skill = skill;
                Idle = idle;
                Armour = armour;
                RightHandEquipped = rightHandEquipped;
                LeftHandEquipped = leftHandEquipped;
            }
        }

        // ---- build -----------------------------------------------------------

        [MenuItem("Open MMORPG/Demo/Regenerate Animation Editing Scene (destroys hand edits)", priority = 121)]
        public static void Build()
        {
            DemoItemBuilder.EnsureFolder("Assets/Scenes");

            AnimatorController controller = BuildController();

            Scene scene;
            if (System.IO.File.Exists(ScenePath))
            {
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    // A bench is still somewhere you leave things; see
                    // DemoSceneBuilder.AuthoredRootName.
                    if (root.name == DemoSceneBuilder.AuthoredRootName)
                        continue;
                    Object.DestroyImmediate(root);
                }
            }
            else
            {
                scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, ScenePath);
            }

            BuildLighting(scene);
            BuildFloor(scene);
            BuildCamera(scene);

            int built = 0;
            foreach (Loadout loadout in Loadouts)
            {
                if (BuildCharacter(scene, loadout, controller))
                    ++built;
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            FrameSceneView();

            Debug.Log($"[{nameof(DemoAnimationSceneBuilder)}] Built {ScenePath} with {built} character(s) and " +
                      $"{controller.layers[0].stateMachine.states.Length} clip(s) in {ControllerPath}.\n" +
                      "    Clips: select a body and open the Animation window to pick one off its Animator.\n" +
                      "    Weapons: change the item on either DemoEquipPreview to hang it on that hand, then " +
                      "drag it in the scene view to tune the grip; "+
                      "Open MMORPG > Demo > Save Weapon Grips From Scene writes every tuned grip back " +
                      "into the item game data.\n" +
                      "    Skills: pick one on DemoSkillPreview and press Play to watch it run with its " +
                      "sound, its effects and its trigger frames.\n" +
                      "    Stay out of play mode - the bench holds bare models with no entity behind them.\n" +
                      "    The scene is rebuilt from scratch each run, so save a grip before rerunning this.");
        }

        // ---- animator --------------------------------------------------------

        /// <summary>
        /// Writes one state per clip the player rig can play, so the Animation window has
        /// something to list.
        ///
        /// Both sources go in, and for different reasons. The demo's own clips under
        /// `Demo/Animations` are the editable ones — they are loose .anim assets and this
        /// bench is where they get worked on. The Quaternius library's are read-only inside
        /// its FBX and can only be scrubbed, but scrubbing them is most of the job: a new
        /// clip has to hand over to the library idle and the library jog without a jolt, and
        /// that is only checkable with both in the same dropdown.
        ///
        /// Clips already in the controller are left exactly as they are, states and all —
        /// see the class comment for why that matters.
        /// </summary>
        private static AnimatorController BuildController()
        {
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
                controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

            AnimatorStateMachine machine = controller.layers[0].stateMachine;

            var already = new HashSet<string>();
            foreach (ChildAnimatorState child in machine.states)
                already.Add(child.state.name);

            int added = 0;
            int placed = machine.states.Length;
            foreach (AnimationClip clip in Clips())
            {
                if (!already.Add(clip.name))
                    continue;
                // laid out in columns rather than a line, so the graph stays roughly square
                // and a state can still be found by eye at 124 of them
                var position = new Vector3(280f * (placed / 24), 56f * (placed % 24), 0f);
                AnimatorState state = machine.AddState(clip.name, position);
                state.motion = clip;
                ++placed;
                ++added;
            }

            // A T-pose for a default would be honest but useless: every grip judgement
            // starts from how the hand sits at rest, so the bench opens on the idle the
            // characters really stand in.
            foreach (ChildAnimatorState child in machine.states)
            {
                if (child.state.name == "Idle_Loop")
                {
                    machine.defaultState = child.state;
                    break;
                }
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            if (added > 0)
                Debug.Log($"[{nameof(DemoAnimationSceneBuilder)}] Added {added} clip(s) to {ControllerPath}.");
            return controller;
        }

        /// <summary>
        /// Every clip this rig can play, demo-authored first so the editable ones are at the
        /// top of the Animation window's list.
        ///
        /// Only humanoid clips: `Demo/Animations` also holds the kit's `BaseCharacter-*`
        /// placeholders, which animate a capsule by object path and would silently do
        /// nothing here, and the item-drop clips, which belong to a different object
        /// altogether.
        /// </summary>
        private static IEnumerable<AnimationClip> Clips()
        {
            var seen = new HashSet<AnimationClip>();
            var ordered = new List<AnimationClip>();

            foreach (string guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { "Assets/OpenMMORPG/Demo/Animations" }))
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(guid));
                if (clip != null && clip.isHumanMotion && seen.Add(clip))
                    ordered.Add(clip);
            }

            if (AssetDatabase.LoadAssetAtPath<Object>(DemoAnimationSet.LibraryPath) != null)
            {
                foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(DemoAnimationSet.LibraryPath))
                {
                    var clip = asset as AnimationClip;
                    if (clip == null || clip.name.StartsWith("__") || !seen.Add(clip))
                        continue;
                    ordered.Add(clip);
                }
            }
            else
            {
                Debug.LogWarning($"[{nameof(DemoAnimationSceneBuilder)}] {DemoAnimationSet.LibraryPath} is not in " +
                                 "this project, so only the demo's own clips are on the bench.");
            }

            return ordered;
        }

        // ---- characters ------------------------------------------------------

        private static bool BuildCharacter(Scene scene, Loadout loadout, AnimatorController controller)
        {
            string path = $"{ModelDir}/{loadout.Model}.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                Debug.LogWarning($"[{nameof(DemoAnimationSceneBuilder)}] Missing {path}. " +
                                 "Run Build Character Models first.");
                return false;
            }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            SceneManager.MoveGameObjectToScene(go, scene);
            go.name = loadout.Name;
            go.transform.position = new Vector3(loadout.X, 0f, 0f);

            var animator = go.GetComponent<Animator>();
            if (animator != null)
            {
                animator.runtimeAnimatorController = controller;
                // Root motion would walk the body off its mark every time a clip with a
                // root curve is scrubbed, and the two would stop lining up.
                animator.applyRootMotion = false;
                // These bodies are cut from a T-pose and carry a hand-widened bind bounds
                // (see DemoCharacterBuilder.WidenBounds); culling on top of that is one more
                // way for a limb to stop moving for reasons that have nothing to do with the
                // clip being looked at.
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }
            else
            {
                Debug.LogWarning($"[{nameof(DemoAnimationSceneBuilder)}] {loadout.Model} has no Animator, " +
                                 "so there is nothing for the Animation window to drive.");
            }

            QuietenModel(go);
            // Clothes first, so they read as the character's own and the two weapon previews
            // stay at the bottom of the inspector where the grip work happens.
            if (loadout.Armour != null)
            {
                foreach (string garment in loadout.Armour)
                    AddPreview(go, garment, true);
            }
            AddPreview(go, loadout.RightHand, loadout.RightHandEquipped);
            AddPreview(go, loadout.LeftHand, loadout.LeftHandEquipped);
            AddSkillPreview(go, loadout.Skill);
            // Last, so the garments and the props are already hung off the bones the pose
            // is about to move; both follow, because both are parented into the skeleton.
            PoseIdle(go, loadout.Idle);
            return true;
        }

        /// <summary>
        /// Leaves the body standing in its idle rather than in the bind pose.
        ///
        /// The bind pose is not neutral on these characters, it is **backwards**: every
        /// Quaternius body carries `Armature (270, 180, 0)`, and that 180 on Y is a real
        /// yaw pointing it at -Z against Unity's humanoid convention. Measured on the male
        /// model, the bind pose reads 180.0 degrees off `transform.forward` while every
        /// clip in the project reads within about 25 of it. Nothing in the game ever shows
        /// that pose, because a character always has a clip playing - but the bench had
        /// nothing playing, so it showed the one pose that faces the wrong way and read as
        /// a bug in the skills.
        ///
        /// **It has to be a PlayableGraph.** `AnimationMode` and `clip.SampleAnimation`
        /// bypass the animation runtime, and the runtime is what applies
        /// `keepOriginalOrientation` / `orientationOffsetY` - the very settings the whole
        /// Quaternius library is corrected with. Sampled that way the library clips come
        /// out at their raw authored angle, which is backwards, so the obvious fix would
        /// have swapped one wrong-facing bench for another.
        ///
        /// The pose is baked into the scene as transform overrides on the prefab instance,
        /// which is why the modifications are recorded explicitly: a bone moved by script
        /// on a prefab instance is not otherwise guaranteed to survive the save.
        /// </summary>
        private static void PoseIdle(GameObject character, string clipName)
        {
            if (string.IsNullOrEmpty(clipName))
                return;
            AnimationClip clip = DemoAnimationSet.Clip(clipName);
            if (clip == null)
                return;
            var animator = character.GetComponent<Animator>();
            if (animator == null || animator.avatar == null || !animator.isHuman)
                return;

            // Root motion is off on this Animator, but the graph is a different path into
            // it and a clip with a root curve would otherwise walk the body off its mark.
            Vector3 position = character.transform.position;
            Quaternion rotation = character.transform.rotation;

            PlayableGraph graph = PlayableGraph.Create("DemoAnimationSceneBuilder.PoseIdle");
            try
            {
                AnimationPlayableOutput output = AnimationPlayableOutput.Create(graph, "out", animator);
                output.SetSourcePlayable(AnimationClipPlayable.Create(graph, clip));
                graph.Evaluate(0.01f);
            }
            finally
            {
                graph.Destroy();
            }

            character.transform.position = position;
            character.transform.rotation = rotation;

            foreach (Transform bone in character.GetComponentsInChildren<Transform>(true))
                PrefabUtility.RecordPrefabInstancePropertyModifications(bone);
        }

        /// <summary>
        /// Hangs one item on the body. The socket is not named here — the item carries it,
        /// and <see cref="MultiplayerARPG.Demo.DemoEquipPreview"/> reads it — so a bench
        /// slot is "a weapon", not "a hand", and swapping a sword for a bow moves it to the
        /// other hand by itself.
        /// </summary>
        private static void AddPreview(GameObject character, string itemName, bool equipped)
        {
            var preview = character.AddComponent<DemoEquipPreview>();
            preview.equipped = equipped;
            if (string.IsNullOrEmpty(itemName))
                return;

            var item = AssetDatabase.LoadAssetAtPath<BaseEquipmentItem>($"{ItemDir}/{itemName}.asset");
            if (item == null)
            {
                Debug.LogWarning($"[{nameof(DemoAnimationSceneBuilder)}] No item named \"{itemName}\" in {ItemDir}. " +
                                 "Run Build Items, or pick a weapon on the component by hand.");
                return;
            }
            preview.item = item;
            preview.Refresh();
        }

        /// <summary>
        /// Gives the body a skill to play. Which weapon it plays it with is not set here:
        /// <see cref="MultiplayerARPG.Demo.DemoSkillPreview"/> reads that off the equip
        /// previews above, the same way the game reads it off what is equipped.
        /// </summary>
        private static void AddSkillPreview(GameObject character, string skillName)
        {
            var preview = character.AddComponent<DemoSkillPreview>();
            if (string.IsNullOrEmpty(skillName))
                return;

            var skill = AssetDatabase.LoadAssetAtPath<BaseSkill>($"{SkillDir}/{skillName}.asset");
            if (skill == null)
            {
                Debug.LogWarning($"[{nameof(DemoAnimationSceneBuilder)}] No skill named \"{skillName}\" in " +
                                 $"{SkillDir}. Run Build Skills, or pick one on the component by hand.");
                return;
            }
            preview.skill = skill;
        }

        /// <summary>
        /// Switches the model component off.
        ///
        /// It does nothing in the editor either way — it is not `ExecuteAlways`, and the
        /// bench never touches its playable graph — but it throws in `Start` when there is no
        /// entity behind it, and on this bench there never is. Left on, one stray press of
        /// Play fills the console with null references and leaves the bodies frozen, which
        /// looks like the bench is broken rather than like play mode is the wrong tool. The
        /// component stays on the object because both previews read their data off it; a
        /// disabled component still answers `GetComponentInChildren`.
        /// </summary>
        private static void QuietenModel(GameObject character)
        {
            foreach (var model in character.GetComponentsInChildren<BaseCharacterModel>(true))
                model.enabled = false;
        }

        // ---- stage -----------------------------------------------------------

        /// <summary>
        /// A key light high enough to read the face and bright enough to throw a hard
        /// shadow, over flat neutral ambient. Not the demo's lighting, deliberately: the
        /// island's sun is coloured and its sky is tinted, and on a bench that turns every
        /// silhouette judgement into a guess about the sky.
        ///
        /// The shadow is doing real work — it is the only view of the feet that does not
        /// need the camera on the floor, so it is what a foot-slide or a sunken heel shows
        /// up in first.
        /// </summary>
        private static void BuildLighting(Scene scene)
        {
            var go = new GameObject("Key Light");
            SceneManager.MoveGameObjectToScene(go, scene);
            // 150 rather than -30. This was the other way round while the bench left its
            // bodies in the bind pose, which really does face -Z - the Quaternius library
            // is a half turn off Unity's convention and `Armature` carries the 180. But
            // the bind pose is the only pose that does: a humanoid avatar normalises it
            // away, and every clip in the project drives the body to `transform.forward`,
            // which is +Z. Now that the bench stands its bodies in an idle (see PoseIdle),
            // a key aimed at -Z lights the backs of their heads.
            go.transform.SetPositionAndRotation(new Vector3(0f, 3f, 0f), Quaternion.Euler(48f, 150f, 0f));

            Light light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.25f;
            light.color = Color.white;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.65f;

            RenderSettings.sun = light;
            RenderSettings.skybox = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.42f, 0.44f, 0.48f);
            RenderSettings.fog = false;
        }

        /// <summary>
        /// A half-metre check for the bodies to stand on. Plain ground would hide exactly the
        /// fault this bench is for — a looping clip whose feet creep — because a slide is
        /// only visible against something that does not move.
        /// </summary>
        private static void BuildFloor(Scene scene)
        {
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            SceneManager.MoveGameObjectToScene(floor, scene);
            floor.name = "Floor";
            // the default plane is 10m; 20m is more than any clip travels while it is scrubbed
            floor.transform.localScale = new Vector3(2f, 1f, 2f);
            Object.DestroyImmediate(floor.GetComponent<MeshCollider>());
            floor.GetComponent<MeshRenderer>().sharedMaterial = BuildFloorMaterial();
        }

        private static Material BuildFloorMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(FloorMaterialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, FloorMaterialPath);
            }
            material.mainTexture = BuildFloorTexture();
            // one texture square per FloorSquare across a 20m plane
            float tiles = 20f / (FloorSquare * 2f);
            material.mainTextureScale = new Vector2(tiles, tiles);
            material.SetFloat("_Smoothness", 0f);
            material.SetColor("_BaseColor", Color.white);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// Two greys, point filtered. Low contrast on purpose — a black-and-white check
        /// fights the character for attention and bounces hard in the ambient.
        /// </summary>
        private static Texture2D BuildFloorTexture()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(FloorTexturePath);
            if (existing != null)
                return existing;

            var texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
            var light = new Color(0.44f, 0.45f, 0.47f);
            var dark = new Color(0.33f, 0.34f, 0.36f);
            texture.SetPixels(new[] { light, dark, dark, light });
            texture.Apply();
            System.IO.File.WriteAllBytes(FloorTexturePath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(FloorTexturePath, ImportAssetOptions.ForceUpdate);

            var importer = (TextureImporter)AssetImporter.GetAtPath(FloorTexturePath);
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(FloorTexturePath);
        }

        /// <summary>
        /// Chest height, three metres back, taking in both bodies. A near plane of 3cm so
        /// the same camera can be pushed into a fist to look at a grip without the hand
        /// clipping away.
        /// </summary>
        private static void BuildCamera(Scene scene)
        {
            var go = new GameObject("Main Camera");
            SceneManager.MoveGameObjectToScene(go, scene);
            go.tag = "MainCamera";
            // In front of the bodies, which means +Z looking back: an animated body faces
            // `transform.forward`. Only the bind pose faces the other way, and the bench
            // no longer leaves anything in it - see PoseIdle and BuildLighting.
            go.transform.SetPositionAndRotation(new Vector3(0f, 1.15f, 3.1f), Quaternion.Euler(4f, 180f, 0f));

            Camera camera = go.AddComponent<Camera>();
            camera.fieldOfView = 50f;
            camera.nearClipPlane = 0.03f;
            camera.farClipPlane = 60f;
            camera.clearFlags = CameraClearFlags.Skybox;
        }

        /// <summary>
        /// Points the scene view at the two bodies, because the scene view is where the work
        /// happens — the camera above is only there so the scene has one.
        /// </summary>
        private static void FrameSceneView()
        {
            SceneView view = SceneView.lastActiveSceneView;
            if (view == null)
                return;
            // Yawed to match the camera: the scene view opens on the bodies' faces, not
            // the backs of their heads. See BuildCamera.
            view.LookAt(new Vector3(0f, 1.0f, 0f), Quaternion.Euler(8f, 180f, 0f), 3.4f);
        }
    }
}
