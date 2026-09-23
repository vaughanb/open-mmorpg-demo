using MultiplayerARPG.GameData.Model.Playables;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// Builds the demo's two animals: the deer the player can hunt out on the island, and
    /// the collie that walks the village.
    ///
    /// Both meshes are generated models retopologised in Blender and rigged by retargeting
    /// the CC0 Quaternius *Ultimate Animated Animals* skeletons onto them — the deer takes
    /// that pack's Deer rig, the collie its Husky — so the models are the project's own and
    /// every clip is Quaternius's. CC0 throughout, as the demo requires. The clip names
    /// below are that library's.
    ///
    /// The two are wired very differently, because they are different things:
    ///
    /// * The deer is a <see cref="MonsterCharacterEntity"/> cloned from the same tuned
    ///   `BaseEnemy` template the bandits use, so it inherits the kit's movement, AI and
    ///   damage handling for free. What makes it game rather than an enemy is its
    ///   <see cref="MonsterCharacteristic.NoHarm"/> characteristic: it wanders, it can be
    ///   shot and killed for its meat and hide, and it never fights back.
    /// * The collie is an <see cref="NpcEntity"/> carrying <see cref="DemoPatrol"/>, the
    ///   same pairing as the village's walking guard. It is not attackable and has no
    ///   dialog — a dog has nothing to say — so nothing prompts the player to talk to it.
    ///
    /// Sizes are measured, not guessed: the deer stands 0.90m at the shoulder (a
    /// white-tailed doe), the collie 0.53m (the breed standard is 48-56cm) and the wolf
    /// 0.89m at the withers, 1.47m nose to tail.
    ///
    /// The wolf is the odd one out here and belongs in this file anyway. It is an enemy
    /// rather than wildlife - the only thing on the island that fights and is not a person
    /// - but it is built from an animal FBX, runs animal clips and needs the animal effect
    /// sockets, so it shares the deer's code and not the bandits'. Its model is the user's
    /// own, retopologised and rigged onto the collie's skeleton so that the dog's twelve
    /// clips drive it unchanged; see WolfClips.
    /// </summary>
    public static class DemoWildlifeBuilder
    {
        private const string EntityDir = "Assets/OpenMMORPG/Demo/Prefabs/GamePlay/CharacterEntities";
        private const string ModelDir = "Assets/OpenMMORPG/Demo/Prefabs/GamePlay/CharacterModels";
        private const string MonsterDir = "Assets/OpenMMORPG/Demo/GameData/Resources/MonsterCharacters";
        private const string MaterialDir = "Assets/OpenMMORPG/Demo/Materials";
        private const string MeshDir = "Assets/OpenMMORPG/Demo/Meshes";
        private const string TextureDir = "Assets/OpenMMORPG/Demo/Textures";
        private const string EnemyTemplate = EntityDir + "/BaseEnemy.prefab";

        private const string DeerFbx = MeshDir + "/Deer.fbx";
        private const string CollieFbx = MeshDir + "/Collie.fbx";
        private const string WolfFbx = MeshDir + "/Wolf.fbx";

        /// <summary>
        /// Standing height and a capsule radius for each animal, in metres, from the meshes
        /// themselves. A quadruped is longer than it is wide, and a capsule cannot say so;
        /// the radius follows the body's half-width plus a little, so the animal does not
        /// snag on doorways it visibly fits through.
        /// </summary>
        private const float DeerHeight = 1.33f;
        private const float DeerRadius = 0.30f;
        private const float CollieHeight = 0.79f;
        private const float CollieRadius = 0.22f;
        // 1.00m to the ear tips, 0.89m at the withers, and 1.47m nose to tail - a large
        // grey wolf, and half again the collie it is built on.
        private const float WolfHeight = 1.0f;
        private const float WolfRadius = 0.28f;

        /// <summary>
        /// The clips the Quaternius animal rigs ship, and whether each one loops. The two
        /// packs name a few of theirs differently — the deer has `Idle_Headlow` and two
        /// attacks, the dog `Idle_2_HeadLow` and one — so each gets its own table rather
        /// than a shared one with holes in it.
        /// </summary>
        private static readonly Dictionary<string, bool> DeerClips = new Dictionary<string, bool>
        {
            { "Idle", true }, { "Idle_2", true }, { "Idle_Headlow", true },
            { "Walk", true }, { "Gallop", true }, { "Eating", true },
            { "Gallop_Jump", false }, { "Jump_toIdle", false },
            { "Attack_Headbutt", false }, { "Attack_Kick", false },
            { "Death", false }, { "Idle_HitReact_Left", false }, { "Idle_HitReact_Right", false },
        };

        private static readonly Dictionary<string, bool> CollieClips = new Dictionary<string, bool>
        {
            { "Idle", true }, { "Idle_2", true }, { "Idle_2_HeadLow", true },
            { "Walk", true }, { "Gallop", true }, { "Eating", true },
            { "Gallop_Jump", false }, { "Jump_ToIdle", false },
            { "Attack", false },
            { "Death", false }, { "Idle_HitReact_Left", false }, { "Idle_HitReact_Right", false },
        };

        /// <summary>
        /// The wolf was rigged onto the collie's own skeleton - same 65 bones, same names,
        /// same root - so the dog's twelve takes transferred to it directly and it ships
        /// exactly the same clip list. It shares the table rather than repeating it, so the
        /// two cannot drift apart.
        /// </summary>
        private static readonly Dictionary<string, bool> WolfClips = CollieClips;

        [MenuItem("Open MMORPG/Demo/Build Wildlife")]
        public static void BuildAll()
        {
            ConfigureImport(DeerFbx, DeerClips);
            ConfigureImport(CollieFbx, CollieClips);
            ConfigureImport(WolfFbx, WolfClips);

            Material deerMat = BuildMaterial("Deer");
            Material collieMat = BuildMaterial("Collie");
            Material wolfMat = BuildMaterial("Wolf");

            BuildModel(DeerFbx, deerMat, $"{ModelDir}/DeerModel.prefab", DeerClips.ContainsKey("Idle_Headlow") ? "Idle_Headlow" : "Idle_2", DeerHeight,
                       new[] { "Attack_Headbutt", "Attack_Kick" });
            BuildModel(CollieFbx, collieMat, $"{ModelDir}/CollieModel.prefab", "Idle_2_HeadLow", CollieHeight,
                       new[] { "Attack" });
            BuildModel(WolfFbx, wolfMat, $"{ModelDir}/WolfModel.prefab", "Idle_2_HeadLow", WolfHeight,
                       new[] { "Attack" });

            BuildDeer();
            BuildDog();
            BuildWolf();
            BuildWolfPup();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            PlaceInScene();
        }

        /// <summary>
        /// Sets the FBX up the way the horse's is: a Generic rig with an avatar built from
        /// the model itself, its takes cut into named clips, and no materials — the demo
        /// keeps its own, pointed at the collected textures.
        ///
        /// Generic rather than Humanoid matters. Unity will happily accept a quadruped as
        /// Humanoid and then map its four legs onto a two-legged skeleton, which produces
        /// a rig that imports without complaint and animates into a knot.
        /// </summary>
        private static void ConfigureImport(string path, Dictionary<string, bool> clips)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
            {
                Debug.LogError($"[{nameof(DemoWildlifeBuilder)}] \"{path}\" is not a model.");
                return;
            }

            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importAnimation = true;
            importer.resampleCurves = true;
            importer.animationCompression = ModelImporterAnimationCompression.Off;

            // Match each take to the clip table. The exporter writes takes as
            // "<Armature>|<Armature>|<Clip>" or "<Armature>|<Clip>", so the clip is
            // whatever follows the last bar.
            var defaults = new List<ModelImporterClipAnimation>();
            foreach (ModelImporterClipAnimation take in importer.defaultClipAnimations)
            {
                string bare = take.takeName;
                int bar = bare.LastIndexOf('|');
                if (bar >= 0)
                    bare = bare.Substring(bar + 1);
                if (!clips.TryGetValue(bare, out bool loops))
                {
                    Debug.LogWarning($"[{nameof(DemoWildlifeBuilder)}] \"{path}\" has an unexpected take \"{take.takeName}\".");
                    continue;
                }
                take.name = bare;
                take.loopTime = loops;
                defaults.Add(take);
            }

            foreach (string wanted in clips.Keys)
            {
                if (!defaults.Exists(c => c.name == wanted))
                    Debug.LogWarning($"[{nameof(DemoWildlifeBuilder)}] \"{path}\" is missing clip \"{wanted}\".");
            }

            importer.clipAnimations = defaults.ToArray();
            importer.SaveAndReimport();
            Debug.Log($"[{nameof(DemoWildlifeBuilder)}] Imported \"{path}\" as Generic with {defaults.Count} clips.");
        }

        /// <summary>
        /// The animal's material: base colour and the normal map baked from the original
        /// high-poly mesh, on the same URP Lit the horse uses. Smoothness is low — fur is
        /// not wet.
        /// </summary>
        private static Material BuildMaterial(string name)
        {
            string path = $"{MaterialDir}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture>($"{TextureDir}/{name}_BaseColor.png"));
            Texture normal = AssetDatabase.LoadAssetAtPath<Texture>($"{TextureDir}/{name}_Normal.png");
            if (normal != null)
            {
                material.SetTexture("_BumpMap", normal);
                material.EnableKeyword("_NORMALMAP");
            }
            material.SetFloat("_Smoothness", 0.2f);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// The character model prefab: the rigged mesh plus the kit's playable model, told
        /// which clip to play for each state.
        ///
        /// Only a handful of the kit's animation slots mean anything to an animal. It never
        /// crouches, crawls, climbs or swims, and it carries no weapon, so
        /// <see cref="PlayableCharacterModel.weaponAnimations"/> is deliberately left empty
        /// — an animal with an empty weapon set cannot have an equipped-item pose override
        /// its idle.
        ///
        /// The two gaits map onto the kit's own distinction rather than being picked by
        /// hand: <see cref="MonsterActivityComponent"/> sets `IsWalking` while a monster
        /// wanders and clears it when it is roused, so `walkStates` gets the walk and
        /// `moveStates` the gallop, and the animal breaks into a run exactly when the kit
        /// thinks it should.
        /// </summary>
        /// <summary>
        /// The two sockets an animal needs: the ground it stands on, and a point in the
        /// middle of its body for a hit to flash at. Named to match the characters' own
        /// (`Floor`, `Body`) so one effect prefab works on either.
        /// </summary>
        private static EffectContainer[] AnimalEffectContainers(GameObject root, float bodyHeight)
        {
            var containers = new List<EffectContainer>();
            foreach (var socket in new[] { ("Floor", 0f), ("Body", bodyHeight * 0.6f) })
            {
                var go = new GameObject("FX_" + socket.Item1);
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = new Vector3(0f, socket.Item2, 0f);
                // Square to the animal rather than to any bone: these rigs run along
                // their own length, and an effect born with that rotation fires sideways.
                go.transform.localRotation = Quaternion.identity;
                containers.Add(new EffectContainer { effectSocket = socket.Item1, transform = go.transform });
            }
            return containers.ToArray();
        }

        private static void BuildModel(string fbxPath, Material material, string outputPath, string grazeClip,
                                       float bodyHeight, string[] attackClips)
        {
            GameObject fbx = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (fbx == null)
            {
                Debug.LogError($"[{nameof(DemoWildlifeBuilder)}] Missing \"{fbxPath}\".");
                return;
            }

            var root = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
            PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            root.name = System.IO.Path.GetFileNameWithoutExtension(outputPath);

            foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                renderer.sharedMaterial = material;

            var model = root.GetComponent<PlayableCharacterModel>();
            if (model == null)
                model = root.AddComponent<PlayableCharacterModel>();
            model.defaultAnimations = AnimalAnimations(fbxPath, grazeClip, attackClips);
            model.weaponAnimations = new WeaponAnimations[0];
            // Somewhere for a hit to land. Without these the deer and the collie had no
            // effect containers at all, and `GameEntityModel.InstantiateEffect` drops an
            // effect whose socket it cannot find without saying so - so an arrow into a
            // deer, which is the one thing the island's hunting is for, showed nothing.
            //
            // Hung off the root at body height rather than off a spine bone: this is a
            // different rig from the characters' and shares none of their bone names, and
            // a flash on a struck animal does not need to follow a lean.
            model.EffectContainers = AnimalEffectContainers(root, bodyHeight);

            PrefabUtility.SaveAsPrefabAsset(root, outputPath);
            AssetDatabase.ImportAsset(outputPath, ImportAssetOptions.ForceUpdate);
            Object.DestroyImmediate(root);
            Debug.Log($"[{nameof(DemoWildlifeBuilder)}] Built {outputPath}.");
        }

        /// <summary>
        /// An animal's animation set.
        ///
        /// <paramref name="attackClips"/> is not optional, however harmless an unarmed animal
        /// looks. `ActionAnimation` is a **class**, and `PlayableCharacterModel.GetActionAnimation`
        /// returns it unset when the attack list is empty - so `PlayActionAnimation` dereferences
        /// null and the whole `AttackRoutine` dies with it, meaning the animal never deals its
        /// damage either. It went unnoticed until the wolf, because the wolf is the first animal
        /// here that actually attacks: the deer is `NoHarm` and the collie is an NpcEntity, and
        /// neither ever walks this path. Found 2026-09-16 as an NRE per wolf swing.
        ///
        /// These rigs have no hands, so everything goes in the right-hand list, which is what
        /// an unarmed attack reads.
        /// </summary>
        private static DefaultAnimations AnimalAnimations(string fbxPath, string grazeClip, string[] attackClips)
        {
            AnimState Idle(string clip) => new AnimState { clip = Clip(fbxPath, clip) };
            ActionState Act(string clip) => new ActionState { clip = Clip(fbxPath, clip) };
            // 0.45 is judged, not measured: these are short lunges whose contact is somewhere
            // around the middle, and unlike the character clips there is no weapon arc to track.
            ActionAnimation Atk(string clip) => new ActionAnimation
            {
                state = Act(clip),
                triggerDurationRates = new[] { 0.45f },
                durationType = AnimationDurationType.ByClipLength,
                audioClips = new AudioClip[0],
            };

            var attacks = new List<ActionAnimation>();
            foreach (string clipName in attackClips)
            {
                if (Clip(fbxPath, clipName) != null)
                    attacks.Add(Atk(clipName));
            }

            AnimationClip walk = Clip(fbxPath, "Walk");
            AnimationClip gallop = Clip(fbxPath, "Gallop");

            return new DefaultAnimations
            {
                idleState = Idle("Idle"),
                // Wandering (the kit flags it `IsWalking`) walks; roused, it runs.
                walkStates = Same(walk),
                moveStates = Same(gallop),
                sprintStates = Same(gallop),

                // Nothing here can crouch, crawl, climb or swim, but the kit reads these
                // slots unconditionally, so they hold the idle rather than a null clip.
                crouchIdleState = Idle("Idle"),
                crouchMoveStates = Same(walk),
                crawlIdleState = Idle("Idle"),
                crawlMoveStates = Same(walk),
                swimIdleState = Idle("Idle"),
                swimMoveStates = Same(walk),

                jumpState = Idle("Gallop_Jump"),
                fallState = Idle("Gallop_Jump"),
                landedState = Idle(grazeClip),

                hurtState = Act("Idle_HitReact_Left"),
                deadState = Idle("Death"),

                rightHandAttackAnimations = attacks.ToArray(),
            };
        }

        private static MoveStates Same(AnimationClip clip)
        {
            AnimState state = new AnimState { clip = clip };
            return new MoveStates
            {
                forwardState = state,
                backwardState = state,
                leftState = state,
                rightState = state,
                upState = state,
                downState = state,
                forwardLeftState = state,
                forwardRightState = state,
                backwardLeftState = state,
                backwardRightState = state,
            };
        }

        /// <summary>Pulls one clip out of the model, by the name its take was given.</summary>
        private static AnimationClip Clip(string fbxPath, string name)
        {
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
            {
                var clip = asset as AnimationClip;
                if (clip != null && clip.name == name)
                    return clip;
            }
            Debug.LogError($"[{nameof(DemoWildlifeBuilder)}] \"{fbxPath}\" has no clip \"{name}\".");
            return null;
        }

        /// <summary>
        /// The deer: a monster in the kit's sense, so that it can be targeted, damaged and
        /// looted, but a <see cref="MonsterCharacteristic.NoHarm"/> one, so it never turns
        /// on the player. Cloned from the bandits' template, which already carries the
        /// movement, AI and combat components tuned for this demo.
        /// </summary>
        private static void BuildDeer()
        {
            GameObject entity = CloneEnemy($"{ModelDir}/DeerModel.prefab", $"{EntityDir}/DemoDeer.prefab", DeerHeight, DeerRadius);
            if (entity == null)
                return;

            // Its own MonsterCharacter, or it keeps the template's and the island fills up
            // with deer that have a bandit's health, a bandit's loot and a bandit's temper.
            var monster = entity.GetComponent<MonsterCharacterEntity>();
            var serialized = new SerializedObject(monster);
            SerializedProperty data = serialized.FindProperty("characterDatabase");
            if (data != null)
            {
                data.objectReferenceValue = MonsterData("Deer");
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            else
            {
                Debug.LogError($"[{nameof(DemoWildlifeBuilder)}] No \"characterDatabase\" on the monster entity — the deer would keep the template's data.");
            }
            // And drop the template's "Enemy" nameplate, which a deer is not. See the note
            // in DemoEntityBuilder: an empty entityTitle falls through to the data asset's.
            SerializedProperty title = serialized.FindProperty("entityTitle");
            if (title != null)
            {
                title.stringValue = string.Empty;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            // Bolts when shot. The kit has no flee behaviour of any kind — a monster either
            // fights or ignores you — so without this a hunted deer stands and takes it.
            if (entity.GetComponent<DemoFlee>() == null)
                entity.AddComponent<DemoFlee>();

            Save(entity, $"{EntityDir}/DemoDeer.prefab");
        }

        /// <summary>
        /// The wolf: the island's starter enemy, and the only thing on it that fights and
        /// is not a person.
        ///
        /// Built the deer's way - the tuned enemy template on an animal model - but with
        /// the two things that make the deer game taken off it. No
        /// <see cref="MonsterCharacteristic.NoHarm"/>, so it fights, and no
        /// <see cref="DemoFlee"/>, so it does not break off once it is hurt. What it is
        /// worth, what it drops and how hard it hits are in the Wolf spec in
        /// DemoDatabaseWiring, with the reasoning for pitching it under a bandit.
        /// </summary>
        private static void BuildWolf()
        {
            GameObject entity = CloneEnemy($"{ModelDir}/WolfModel.prefab", $"{EntityDir}/DemoWolf.prefab", WolfHeight, WolfRadius);
            if (entity == null)
                return;

            // Its own MonsterCharacter, exactly as the deer needs one: left on the
            // template's the island fills up with wolves carrying a bandit's health, a
            // bandit's loot and a bandit's level band.
            var monster = entity.GetComponent<MonsterCharacterEntity>();
            var serialized = new SerializedObject(monster);
            SerializedProperty data = serialized.FindProperty("characterDatabase");
            if (data != null)
            {
                data.objectReferenceValue = MonsterData("Wolf");
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            else
            {
                Debug.LogError($"[{nameof(DemoWildlifeBuilder)}] No \"characterDatabase\" on the monster entity — the wolf would keep the template's data.");
            }
            // And the template's "Enemy" nameplate, which would read over every wolf on the
            // island. Empty falls through to the data asset's own title.
            SerializedProperty title = serialized.FindProperty("entityTitle");
            if (title != null)
            {
                title.stringValue = string.Empty;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            Save(entity, $"{EntityDir}/DemoWolf.prefab");
        }

        /// <summary>
        /// The wolf pup: the same animal, smaller, and the player's.
        ///
        /// It is built here rather than with the pet item because everything it needs is
        /// here - the wolf's mesh, its material, its twelve clips and the collider numbers
        /// measured off it. What makes it a *pet* is nothing on the prefab at all: the kit
        /// summons it through `CharacterSummon` and the summoner it is given is what makes
        /// it an ally. To the prefab it is an ordinary monster entity, which is why it can
        /// be a clone of the wolf with a different data asset and a smaller scale.
        ///
        /// Scaled by the **model**, not the entity: the capsule and the nameplate anchors
        /// come off the entity, and shrinking the root would take the collider and the
        /// name with it. Two thirds reads as young rather than as a wolf seen from far off.
        /// </summary>
        private static void BuildWolfPup()
        {
            const float PupScale = 0.66f;
            GameObject entity = CloneEnemy($"{ModelDir}/WolfModel.prefab", $"{EntityDir}/DemoWolfPup.prefab",
                                           WolfHeight * PupScale, WolfRadius * PupScale);
            if (entity == null)
                return;

            Transform model = entity.transform.Find("Model");
            if (model != null)
                model.localScale = Vector3.one * PupScale;

            var monster = entity.GetComponent<MonsterCharacterEntity>();
            var serialized = new SerializedObject(monster);
            SerializedProperty data = serialized.FindProperty("characterDatabase");
            if (data != null)
            {
                data.objectReferenceValue = MonsterData("WolfPup");
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            else
            {
                Debug.LogError($"[{nameof(DemoWildlifeBuilder)}] No \"characterDatabase\" on the pup — it would keep the template's data.");
            }
            SerializedProperty title = serialized.FindProperty("entityTitle");
            if (title != null)
            {
                title.stringValue = string.Empty;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            Save(entity, $"{EntityDir}/DemoWolfPup.prefab");
        }

        /// <summary>
        /// The village dog: the guard's pairing of <see cref="NpcEntity"/> and
        /// <see cref="DemoPatrol"/>, on four legs. No dialog and no quest marker, so the
        /// player is never prompted to talk to it.
        /// </summary>
        private static void BuildDog()
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelDir}/CollieModel.prefab");
            if (model == null)
            {
                Debug.LogError($"[{nameof(DemoWildlifeBuilder)}] Missing the collie model.");
                return;
            }

            var entity = new GameObject("DemoVillageDog");
            var modelInstance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            modelInstance.transform.SetParent(entity.transform, false);
            modelInstance.name = "Model";

            var transforms = new GameObject("Transforms");
            transforms.transform.SetParent(entity.transform, false);
            Anchor(transforms.transform, "UIElementContainer", CollieHeight + 0.2f);
            Anchor(transforms.transform, "MiniMapContainer", 0f);

            NpcEntity npc = entity.AddComponent<NpcEntity>();
            var serialized = new SerializedObject(npc);
            serialized.FindProperty("characterUiTransform").objectReferenceValue = transforms.transform.Find("UIElementContainer");
            serialized.FindProperty("miniMapUiTransform").objectReferenceValue = transforms.transform.Find("MiniMapContainer");

            var capsule = entity.AddComponent<CapsuleCollider>();
            capsule.height = CollieHeight;
            capsule.radius = CollieRadius;
            capsule.center = new Vector3(0f, CollieHeight * 0.5f, 0f);

            var model3d = modelInstance.GetComponent<PlayableCharacterModel>();
            CharacterModelManager manager = entity.AddComponent<CharacterModelManager>();
            manager.MainTpsModel = model3d;
            // The entity's own model field, which is what actually animates it — see the
            // long note in DemoEntityBuilder. Without it the dog slides round its route
            // with its legs still.
            serialized.FindProperty("model").objectReferenceValue = model3d;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            entity.AddComponent<NavMeshEntityMovement>();
            NavMeshAgent agent = entity.GetComponent<NavMeshAgent>();
            agent.height = CollieHeight;
            agent.radius = CollieRadius;
            agent.stoppingDistance = 0.3f;

            DemoPatrol patrol = entity.AddComponent<DemoPatrol>();
            // Measured off the collie's own `Walk` clip, not chosen: 0.28 m/s, rounded.
            // It reads as slow for a dog because the clip is slow - the library drew a
            // dawdle, and any faster is the animal skating. It was 1.6 until 2026-09-22,
            // which is five times the clip's pace, and before that it was not walking at
            // all: its `moveStates` clip is literally `Gallop`. See DemoPatrol.
            patrol.walkSpeed = 0.3f;
            patrol.pause = 4f;
            patrol.holdForPlayersWithin = 0f;   // it has nothing to say, so it never stops to talk

            Save(entity, $"{EntityDir}/DemoVillageDog.prefab");
        }

        /// <summary>
        /// Clones the tuned enemy template onto an animal model and refits everything the
        /// template sized for a person: the capsule, the agent, and the anchors the
        /// nameplate and damage numbers hang from.
        /// </summary>
        private static GameObject CloneEnemy(string modelPath, string outputPath, float height, float radius)
        {
            GameObject template = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyTemplate);
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (template == null || model == null)
            {
                Debug.LogError($"[{nameof(DemoWildlifeBuilder)}] Missing \"{EnemyTemplate}\" or \"{modelPath}\".");
                return null;
            }

            var entity = (GameObject)PrefabUtility.InstantiatePrefab(template);
            PrefabUtility.UnpackPrefabInstance(entity, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            entity.name = System.IO.Path.GetFileNameWithoutExtension(outputPath);

            Transform capsuleModel = entity.transform.Find("CapsuleModel");
            if (capsuleModel != null)
                Object.DestroyImmediate(capsuleModel.gameObject);
            PlayableCharacterModel rootModel = entity.GetComponent<PlayableCharacterModel>();
            if (rootModel != null)
                Object.DestroyImmediate(rootModel);
            Animator rootAnimator = entity.GetComponent<Animator>();
            if (rootAnimator != null)
                Object.DestroyImmediate(rootAnimator);

            var modelInstance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            modelInstance.transform.SetParent(entity.transform, false);
            modelInstance.name = "Model";
            entity.GetComponent<CharacterModelManager>().MainTpsModel = modelInstance.GetComponent<PlayableCharacterModel>();

            CharacterController controller = entity.GetComponent<CharacterController>();
            if (controller != null)
            {
                controller.height = height;
                controller.radius = radius;
                controller.center = new Vector3(0f, height * 0.5f, 0f);
            }
            CapsuleCollider capsule = entity.GetComponent<CapsuleCollider>();
            if (capsule != null)
            {
                capsule.height = height;
                capsule.radius = radius;
                capsule.center = new Vector3(0f, height * 0.5f, 0f);
            }
            NavMeshAgent agent = entity.GetComponent<NavMeshAgent>();
            if (agent != null)
            {
                agent.height = height;
                agent.radius = radius;
            }

            Transform transforms = entity.transform.Find("Transforms");
            if (transforms != null)
            {
                Place(transforms, "DamageTransform", height * 0.6f);
                Place(transforms, "UIElementContainer", height + 0.2f);
                Place(transforms, "ChatBubbleTransform", height + 0.35f);
                Place(transforms, "MiniMapContainer", 0f);
            }
            return entity;
        }

        private static void Save(GameObject entity, string outputPath)
        {
            // Hooves and paws, before the prefab is written. DemoAudioWiring knows these two
            // by name and gives them an animal's step rather than a person's bootfall; it
            // does the same on its own menu run, so a later `Wire Audio` agrees with this.
            DemoAudioWiring.WireAnimalEntity(entity, System.IO.Path.GetFileNameWithoutExtension(outputPath));
            PrefabUtility.SaveAsPrefabAsset(entity, outputPath);
            GiveOwnNetworkId(outputPath);
            AssetDatabase.ImportAsset(outputPath, ImportAssetOptions.ForceUpdate);
            Object.DestroyImmediate(entity);
            Debug.Log($"[{nameof(DemoWildlifeBuilder)}] Built {outputPath}.");
        }

        /// <summary>
        /// Every spawnable entity needs its own asset id, or the pair of them share one and
        /// the server spawns whichever it looked up first.
        /// </summary>
        private static void GiveOwnNetworkId(string prefabPath)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var identity = prefab.GetComponent<LiteNetLibManager.LiteNetLibIdentity>();
            if (identity == null)
                return;
            string guid = AssetDatabase.AssetPathToGUID(prefabPath);
            var serialized = new SerializedObject(identity);
            serialized.FindProperty("assetId").stringValue = guid;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(prefab);
            AssetDatabase.SaveAssets();
        }

        private static MonsterCharacter MonsterData(string name)
        {
            string path = $"{MonsterDir}/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<MonsterCharacter>(path);
            if (existing != null)
                return existing;
            var created = ScriptableObject.CreateInstance<MonsterCharacter>();
            AssetDatabase.CreateAsset(created, path);
            Debug.Log($"[{nameof(DemoWildlifeBuilder)}] Created monster data \"{name}\".");
            return created;
        }

        private static Transform Anchor(Transform parent, string name, float height)
        {
            var anchor = new GameObject(name);
            anchor.transform.SetParent(parent, false);
            anchor.transform.localPosition = new Vector3(0f, height, 0f);
            return anchor.transform;
        }

        private static void Place(Transform parent, string name, float height)
        {
            Transform target = parent.Find(name);
            if (target != null)
                target.localPosition = new Vector3(0f, height, 0f);
        }

        // ------------------------------------------------------------------
        // Placing them in the map
        // ------------------------------------------------------------------

        /// <summary>The root a rebuild keeps, alongside `Npcs` and `Mounts`.</summary>
        public const string WildlifeRoot = "Wildlife";

        /// <summary>
        /// The dog's round, as village-local positions. A loop of the green: the well, the
        /// tavern door, the smithy, and back along the fence.
        /// </summary>
        private static readonly Vector3[] DogRoute =
        {
            new Vector3(-2.5f, 0f, 6.0f),
            new Vector3(6.0f, 0f, 4.0f),
            new Vector3(7.5f, 0f, -5.0f),
            new Vector3(-4.0f, 0f, -6.5f),
        };

        [MenuItem("Open MMORPG/Demo/Place Wildlife")]
        public static void PlaceInScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.name.Equals("DemoMap"))
            {
                Debug.LogWarning($"[{nameof(DemoWildlifeBuilder)}] Open DemoMap before placing wildlife.");
                return;
            }

            GameObject root = GameObject.Find(WildlifeRoot);
            if (root == null)
                root = new GameObject(WildlifeRoot);

            PlaceDog(root.transform);
            PlaceDeer(root.transform);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static void PlaceDog(Transform root)
        {
            if (root.Find("VillageDog") != null)
                return;
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{EntityDir}/DemoVillageDog.prefab");
            if (prefab == null)
                return;

            Transform village = GameObject.Find("Village")?.transform;
            Vector3 origin = village != null ? village.position : Vector3.zero;

            var dog = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            dog.name = "VillageDog";
            dog.transform.SetParent(root, false);
            dog.transform.position = Ground(origin + DogRoute[0]);

            var patrol = dog.GetComponent<DemoPatrol>();
            var route = new Vector3[DogRoute.Length];
            for (int i = 0; i < DogRoute.Length; ++i)
                route[i] = Ground(origin + DogRoute[i]);
            patrol.waypoints = route;
            Debug.Log($"[{nameof(DemoWildlifeBuilder)}] Placed the village dog on a {route.Length}-point round.");
        }

        /// <summary>
        /// How many grounds the deer are spread over, and so — with
        /// <see cref="DeerPerGround"/> — how many the island carries. Four threes reads as
        /// a scattered population on a island this size without turning it into a farm.
        /// </summary>
        private const int DeerGrounds = 4;

        /// <summary>
        /// Where people are. Deer are not placed within <see cref="ClearOfSettlements"/> of
        /// any of them, so the player walks out into the country to hunt rather than
        /// shooting one over the village fence.
        /// </summary>
        private static readonly Vector3[] Settlements =
        {
            new Vector3(-34f, 0f, 22f),   // the village
            new Vector3(46f, 0f, -40f),   // the bandit camp
            new Vector3(6f, 0f, -39f),    // the crypt door
        };

        private const float ClearOfSettlements = 38f;
        /// <summary>Kept apart from each other too, so they read as a scattered population.</summary>
        private const float ClearOfEachOther = 22f;
        /// <summary>Comfortably above the waterline, which sits at zero.</summary>
        private const float DryLand = 2f;

        /// <summary>How many deer each ground stands, and how far they range from its centre.</summary>
        private const int DeerPerGround = 3;
        private const float DeerGroundRadius = 26f;

        /// <summary>
        /// Deer come from spawn areas, the same component the bandit camp uses, scattered
        /// over the island so the player meets them out in open country.
        ///
        /// Spawn areas rather than deer standing in the scene, for two reasons. The kit
        /// respawns what an area spawned, so a hunted island does not stay empty — which is
        /// the whole point of game. And a scene entity needs a network scene id that is only
        /// handed out when the object is created through the editor's own path: eight deer
        /// placed by script got `objectId = 0` and seven of the eight never spawned at all,
        /// which is how this was found.
        ///
        /// The grounds are searched for rather than written down. The island is a terrain
        /// with a coastline, three settlements and a good deal of cliff, and a hardcoded
        /// list drifts out of date the moment any of that is regenerated — the first pass at
        /// this put two deer in the village's back garden and two more inside the bandit
        /// camp.
        /// </summary>
        private static void PlaceDeer(Transform root)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{EntityDir}/DemoDeer.prefab");
            var monster = prefab != null ? prefab.GetComponent<BaseMonsterCharacterEntity>() : null;
            if (monster == null)
            {
                Debug.LogError($"[{nameof(DemoWildlifeBuilder)}] No deer entity to spawn.");
                return;
            }

            List<Vector3> grounds = FindPasture();
            int placed = 0, kept = 0;
            for (int i = 0; i < DeerGrounds; ++i)
            {
                string name = $"DeerGround_{i + 1:00}";
                if (root.Find(name) != null)
                {
                    kept++;
                    continue;
                }
                if (i >= grounds.Count)
                    break;

                var area = new GameObject(name);
                area.transform.SetParent(root, false);
                area.transform.position = grounds[i];
                var spawner = area.AddComponent<MonsterSpawnArea>();
                var serialized = new SerializedObject(spawner);
                serialized.FindProperty("prefab").objectReferenceValue = monster;
                serialized.FindProperty("randomRadius").floatValue = DeerGroundRadius;
                serialized.FindProperty("minAmount").intValue = DeerPerGround;
                serialized.FindProperty("maxAmount").intValue = DeerPerGround;
                // Low level and a narrow band: a deer is not a fight, and one that scaled
                // with the island's danger would take a quiver to bring down out on the
                // hills and one arrow near the village.
                serialized.FindProperty("minLevel").intValue = 1;
                serialized.FindProperty("maxLevel").intValue = 2;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                placed++;
            }
            Debug.Log($"[{nameof(DemoWildlifeBuilder)}] Placed {placed} deer grounds ({kept} already stood), {DeerPerGround} deer each, from {grounds.Count} candidate spots.");
        }

        /// <summary>
        /// Open country: dry, walkable, away from people and from the other deer. Returned
        /// loneliest first, so taking the first few spreads them right across the island.
        /// </summary>
        private static List<Vector3> FindPasture()
        {
            Terrain terrain = Terrain.activeTerrain;
            var scored = new List<KeyValuePair<float, Vector3>>();
            if (terrain == null)
                return new List<Vector3>();

            Vector3 origin = terrain.transform.position;
            Vector3 size = terrain.terrainData.size;
            for (float x = origin.x + 10f; x < origin.x + size.x - 10f; x += 6f)
            {
                for (float z = origin.z + 10f; z < origin.z + size.z - 10f; z += 6f)
                {
                    float height = terrain.SampleHeight(new Vector3(x, 0f, z)) + origin.y;
                    if (height < DryLand)
                        continue;

                    var at = new Vector3(x, height, z);
                    float nearest = float.MaxValue;
                    foreach (Vector3 settlement in Settlements)
                        nearest = Mathf.Min(nearest, Vector2.Distance(new Vector2(x, z), new Vector2(settlement.x, settlement.z)));
                    if (nearest < ClearOfSettlements)
                        continue;

                    // On the navmesh where it actually is, not snapped to it from a
                    // distance: a cliff face samples to the clifftop several metres away,
                    // and a deer put there stands in the rock.
                    if (!NavMesh.SamplePosition(at + Vector3.up * 2f, out NavMeshHit hit, 4f, NavMesh.AllAreas))
                        continue;
                    if (Vector2.Distance(new Vector2(hit.position.x, hit.position.z), new Vector2(x, z)) > 2f)
                        continue;

                    scored.Add(new KeyValuePair<float, Vector3>(nearest, hit.position));
                }
            }

            var chosen = new List<Vector3>();
            if (scored.Count == 0)
                return chosen;

            // Start from the spot furthest from anybody, then repeatedly take whichever
            // candidate is furthest from every deer already placed.
            //
            // Simply taking the loneliest spots in order does NOT work, and the first
            // attempt did exactly that: all three settlements sit in the south and west,
            // so "far from people" means "north-east" and six of the eight deer bunched
            // into that one corner. Spreading them has to be measured against the deer,
            // not against the villages.
            scored.Sort((a, b) => b.Key.CompareTo(a.Key));
            chosen.Add(scored[0].Value);
            while (chosen.Count < DeerGrounds)
            {
                float bestGap = -1f;
                Vector3 best = Vector3.zero;
                foreach (KeyValuePair<float, Vector3> candidate in scored)
                {
                    float gap = float.MaxValue;
                    foreach (Vector3 already in chosen)
                        gap = Mathf.Min(gap, Vector3.Distance(already, candidate.Value));
                    if (gap > bestGap)
                    {
                        bestGap = gap;
                        best = candidate.Value;
                    }
                }
                if (bestGap < ClearOfEachOther)
                    break;
                chosen.Add(best);
            }
            return chosen;
        }

        /// <summary>
        /// Drops a point onto the terrain. Raycasting from well overhead rather than using
        /// the terrain height directly, so anything standing on a rock or a path sits on
        /// what is actually there.
        /// </summary>
        private static Vector3 Ground(Vector3 at)
        {
            if (Physics.Raycast(new Vector3(at.x, 500f, at.z), Vector3.down, out RaycastHit hit, 1000f))
                return hit.point;
            Terrain terrain = Terrain.activeTerrain;
            if (terrain != null)
                return new Vector3(at.x, terrain.SampleHeight(at) + terrain.transform.position.y, at.z);
            return at;
        }
    }
}
