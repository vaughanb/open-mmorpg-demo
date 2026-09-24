using MultiplayerARPG.GameData.Model.Playables;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// Turns the built character models into playable entities.
    ///
    /// The kit's existing demo entities already carry the full component set —
    /// movement, attack, skill, recovery, networking — wired and tuned, and only
    /// their placeholder capsule needs replacing. So each entity here is cloned
    /// from one of those rather than assembled from scratch, which keeps the
    /// wiring in step with whatever the kit does to its own prefabs.
    ///
    /// The model is left as a child rather than merged into the entity root, so a
    /// character can switch between the male and female model without the entity
    /// being rebuilt. CharacterModelManager.MainTpsModel points at it.
    /// </summary>
    public static class DemoEntityBuilder
    {
        private const string EntityDir = "Assets/OpenMMORPG/Demo/Prefabs/GamePlay/CharacterEntities";
        private const string ModelDir = "Assets/OpenMMORPG/Demo/Prefabs/GamePlay/CharacterModels";
        private const string MonsterDir = "Assets/OpenMMORPG/Demo/GameData/Resources/MonsterCharacters";

        /// <summary>
        /// The two prefabs every entity here is cloned from. **They are tooling, not
        /// content**: nothing in the game references them, nothing spawns them, and they
        /// appear in no database - so an asset sweep that goes by "is anything pointing at
        /// this" will take them, and on 2026-09-23 something had already taken
        /// `BaseCharacter.prefab`. Both are tracked in the kit repo, so the way back is
        /// `git checkout -- Demo/Prefabs/GamePlay/CharacterEntities/BaseCharacter.prefab*`
        /// from `Assets/OpenMMORPG`.
        ///
        /// They are allowed to be old. `Build` strips the arrangement they carry - the
        /// placeholder capsule, the model component on the root - and the later steps of
        /// the pipeline put back what they never had, so a template from months ago still
        /// produces a current entity. See <see cref="Build"/> for what is added on top.
        /// </summary>
        private const string PlayerTemplate = EntityDir + "/BaseCharacter.prefab";
        private const string EnemyTemplate = EntityDir + "/BaseEnemy.prefab";

        /// <summary>
        /// Whether a prefab in the entity folder is one of the two templates.
        ///
        /// **A template is an input and must never be written to.** It lives in the same
        /// folder as the entities, and `BaseCharacter.prefab` carries a real
        /// `PlayerCharacterEntity`, so a sweep that says "every player prefab in this
        /// folder" picks it up and edits it - which is how it came to be carrying a
        /// `DemoSkinTone` it has no use for and showing as modified in the kit repo.
        /// Every folder sweep here should skip these two.
        /// </summary>
        public static bool IsTemplate(string assetPath)
        {
            return assetPath == PlayerTemplate || assetPath == EnemyTemplate;
        }

        /// <summary>
        /// How long a dead monster lies there, and how long its loot lasts. **One number for both**
        /// - `DemoNpcBuilder` writes it to `GameInstance.monsterCorpseAppearDuration` as well.
        ///
        /// The kit ships them wildly apart: the body is destroyed after `destroyDelay` of **2
        /// seconds** while the loot container it spawns lives for `monsterCorpseAppearDuration`,
        /// **60**. So the corpse blinked out two seconds after the kill and left a sack glittering
        /// on the grass for a minute - which is what it looks like, and is also what put a
        /// destroyed entity under the player's cursor long enough to trip the interface-null bug
        /// in `DemoPlayerController`.
        ///
        /// **They cannot simply both be 60**, because the kit couples the body to the respawn:
        /// `RespawnRoutine(DestroyDelay + DestroyRespawnDelay)`. A minute-long body means a
        /// minute-long wait for the monster to come back, on an island small enough to clear.
        /// 30 seconds is long enough to finish a fight and walk over to loot, and puts the
        /// monster back 35 seconds after it died rather than 7.
        /// </summary>
        internal const float CorpseLifetime = 30f;

        /// <summary>Added to <see cref="CorpseLifetime"/> to give the respawn delay.</summary>
        internal const float RespawnAfterBody = 5f;

        // The Quaternius characters stand about 1.75m, so the placeholder capsule's
        // 0.5m radius is far too wide for them - they would not fit between the
        // village buildings.
        private const float CharacterHeight = 1.8f;
        private const float CharacterRadius = 0.3f;

        /// <summary>The bodies a player can be, each its own entity. Also the title shown on the create screen.</summary>
        public static readonly string[] PlayerBodies = { "Male", "Female" };

        public static string PlayerEntityPath(string gender)
        {
            return $"{EntityDir}/DemoPlayerCharacter{gender}.prefab";
        }

        [MenuItem("Open MMORPG/Demo/Build Character Entities")]
        public static void BuildAll()
        {
            // One entity per body: the create screen offers a body per player entity in
            // the database, and each carries the classes it may be. The hairstyles and
            // beards are chosen within a body; see DemoBodyPartBuilder.
            foreach (string gender in PlayerBodies)
                Build(PlayerTemplate, $"{ModelDir}/PlayerCharacterModel_{gender}.prefab", PlayerEntityPath(gender), null, gender);
            // Each enemy family points at its own MonsterCharacter, because that asset is
            // where the loot table lives — sharing one would mean every enemy dropped every
            // class's armour no matter what it was wearing.
            Build(EnemyTemplate, $"{ModelDir}/BanditModel_Male.prefab", $"{EntityDir}/DemoBanditMale.prefab", "BaseEnemy");
            Build(EnemyTemplate, $"{ModelDir}/BanditModel_Female.prefab", $"{EntityDir}/DemoBanditFemale.prefab", "BaseEnemy");
            Build(EnemyTemplate, $"{ModelDir}/MarauderModel_Male.prefab", $"{EntityDir}/DemoMarauderMale.prefab", "Marauder");
            Build(EnemyTemplate, $"{ModelDir}/MarauderModel_Female.prefab", $"{EntityDir}/DemoMarauderFemale.prefab", "Marauder");
            Build(EnemyTemplate, $"{ModelDir}/CultistModel_Male.prefab", $"{EntityDir}/DemoCultistMale.prefab", "Cultist");
            Build(EnemyTemplate, $"{ModelDir}/CultistModel_Female.prefab", $"{EntityDir}/DemoCultistFemale.prefab", "Cultist");
            // The Hierophant, who holds the crypt's sanctum: a cultist a head taller than
            // the rest, with the stats and the loot table of a boss. See DemoDungeonBuilder.
            Build(EnemyTemplate, $"{ModelDir}/CultistModel_Male.prefab", $"{EntityDir}/DemoHierophant.prefab", "Hierophant", null, 1.12f);
            BuildNpc($"{ModelDir}/VillagerModel_Male.prefab", $"{EntityDir}/DemoVillager.prefab");
            BuildNpc($"{ModelDir}/ElderModel_Male.prefab", $"{EntityDir}/DemoElder.prefab");
            BuildNpc($"{ModelDir}/KeeperModel_Male.prefab", $"{EntityDir}/DemoKeeper.prefab");
            // The alehouse keeper: the other villager body, so she is not Marek's twin.
            BuildNpc($"{ModelDir}/VillagerModel_Female.prefab", $"{EntityDir}/DemoInnkeeper.prefab");
            // The smith, who repairs, refines and breaks down gear at the anvil in House_2.
            // Empty-handed: `armedWith` takes *items*, and the pack's hammers are props with
            // no item behind them, while putting a sword in his hand would read as a guard
            // standing at an anvil. The anvil he is standing at does the work instead.
            BuildNpc($"{ModelDir}/SmithModel_Male.prefab", $"{EntityDir}/DemoSmith.prefab");
            // The guards, sword and shield in hand: one stands the watchtower deck, the
            // other walks the green and needs to be able to move.
            string[] guardArms = { "IronLongsword", "PaintedRoundShield" };
            BuildNpc($"{ModelDir}/GuardModel_Male.prefab", $"{EntityDir}/DemoGuard.prefab", guardArms, false);
            BuildNpc($"{ModelDir}/GuardModel_Male.prefab", $"{EntityDir}/DemoPatrolGuard.prefab", guardArms, true);
            EnsureEntitySetting();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        /// <summary>
        /// Builds the townsfolk entity. There is no NPC prefab in the kit's demo to
        /// clone, but an NPC needs far less than a fighter does — it never moves or
        /// takes damage, so it is a model, a collider to click, and the anchors its
        /// nameplate and quest marker hang from.
        /// </summary>
        private static void BuildNpc(string modelPath, string outputPath, string[] armedWith = null, bool patrols = false)
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null)
            {
                Debug.LogError($"[{nameof(DemoEntityBuilder)}] Missing model \"{modelPath}\".");
                return;
            }

            var entity = new GameObject(System.IO.Path.GetFileNameWithoutExtension(outputPath));
            GameObject modelInstance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            modelInstance.transform.SetParent(entity.transform, false);
            modelInstance.name = "Model";

            var transforms = new GameObject("Transforms");
            transforms.transform.SetParent(entity.transform, false);
            Transform characterUi = MakeAnchor(transforms.transform, "UIElementContainer", CharacterHeight + 0.25f);
            Transform miniMapUi = MakeAnchor(transforms.transform, "MiniMapContainer", 0f);
            Transform questIndicator = MakeAnchor(transforms.transform, "QuestIndicatorContainer", CharacterHeight + 0.55f);
            // The kit's quest indicator is a world-space canvas laid out in pixels - its
            // "!" is a 160 by 40 text - and NpcEntity instantiates it into this container
            // with no scaling of its own, so the container has to carry the pixel-to-metre
            // scale the kit's world-space UI assumes. At scale one the exclamation mark was
            // a hundred and sixty metres wide, and read as a yellow beam over the elder; at
            // a hundredth it was a hand's width, lost against the plaster from across the
            // green. This makes the mark about half a metre tall.
            questIndicator.localScale = Vector3.one * 0.045f;

            NpcEntity npc = entity.AddComponent<NpcEntity>();
            var serialized = new SerializedObject(npc);
            serialized.FindProperty("characterUiTransform").objectReferenceValue = characterUi;
            serialized.FindProperty("miniMapUiTransform").objectReferenceValue = miniMapUi;
            serialized.FindProperty("questIndicatorContainer").objectReferenceValue = questIndicator;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var capsule = entity.AddComponent<CapsuleCollider>();
            capsule.height = CharacterHeight;
            capsule.radius = CharacterRadius;
            capsule.center = new Vector3(0f, CharacterHeight * 0.5f, 0f);

            var model3d = modelInstance.GetComponent<PlayableCharacterModel>();
            if (model3d != null)
            {
                WireModelRenderer(modelInstance, model3d);

                CharacterModelManager manager = entity.GetComponent<CharacterModelManager>();
                if (manager == null)
                    manager = entity.AddComponent<CharacterModelManager>();
                manager.MainTpsModel = model3d;

                // And the entity's OWN model field, which is what actually animates it.
                //
                // `BaseGameEntity.EntityUpdate` drives the model from the movement state,
                // but only `if (Model != null)` - and for an NPC `Model` is the serialized
                // field below, filled by `InitialRequiredComponents` with
                // `GetComponent<GameEntityModel>()`. That is GetComponent, not
                // GetComponentInChildren, and the model sits on the child "Model" object,
                // so it finds nothing and every NPC stands frozen: the idles never play and
                // a patrolling guard slides along the path without moving his legs.
                //
                // Characters do not hit this because `BaseCharacterEntity` overrides `Model`
                // to return `ModelManager.ActiveTpsModel` and ignores the field entirely.
                // Setting the manager above is therefore not enough on its own.
                serialized.FindProperty("model").objectReferenceValue = model3d;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            if (armedWith != null)
            {
                foreach (string item in armedWith)
                    Arm(modelInstance, item);
            }

            if (patrols)
            {
                // The kit's navmesh mover, the same one its monsters walk on, driven by
                // a demo component that hands it one point of a loop at a time. The base
                // entity animates the model from the movement state — but only once the
                // entity's `model` field is set, which is done above.
                entity.AddComponent<NavMeshEntityMovement>();
                NavMeshAgent agent = entity.GetComponent<NavMeshAgent>();
                agent.height = CharacterHeight;
                agent.radius = CharacterRadius;
                agent.stoppingDistance = 0.3f;
                entity.AddComponent<MultiplayerARPG.Demo.DemoPatrol>();
            }
            DemoAudioWiring.WireCharacter(entity, modelPath.Contains("Female"));

            PrefabUtility.SaveAsPrefabAsset(entity, outputPath);
            GiveOwnNetworkId(outputPath);
            // Forced, because the editor's loaded copy of a prefab is not refreshed by
            // writing a new file over it. Where a mesh or material this references was
            // itself regenerated earlier in the same run, that loaded copy can be holding
            // a reference the rebuild has since repaired on disk - and every later step
            // reads the loaded copy, so the stale one propagates into everything built
            // from it, silently and with nothing missing in the inspector to show for it.
            AssetDatabase.ImportAsset(outputPath, ImportAssetOptions.ForceUpdate);
            Object.DestroyImmediate(entity);
            Debug.Log($"[{nameof(DemoEntityBuilder)}] Built {outputPath}.");
        }

        /// <summary>
        /// Puts a piece of equipment in an NPC's hand.
        ///
        /// NPCs have no equipment of their own, so the item asset's model entry - which
        /// prefab, which hand, and the grip offsets the kit applies when a player equips
        /// it - is read and applied by hand. The guard then holds the sword exactly as a
        /// player holding the same sword does, and a change to the item's grip reaches
        /// both the next time they are built.
        /// </summary>
        /// <summary>
        /// How far an NPC lowers a weapon from the grip a player wields it with, in degrees
        /// about the weapon's own Z - the blade's flat normal, so the blade drops within
        /// the plane of its edges and the flat keeps facing the leg.
        ///
        /// **A grip is judged against a stance, and the guards never take that stance.**
        /// The sword's grip was captured with the body in `Sword_Idle`, arm up and wrist
        /// set, which is how a player holds it. An NPC has no equipped weapon for the kit
        /// to pick a weapon set with, so the guards play the *unarmed* `Idle_Loop` and
        /// `Walk_Loop` - relaxed arms - with that same grip in the hand. Measured on the
        /// guard's rig: in the idle the blade pointed 15 degrees **above** horizontal,
        /// straight out in front, and mid-stride it swung up to 48 above. That was the
        /// sword that read wrong side-on.
        ///
        /// Minus 48 is measured, not picked. Sampling both clips and scanning the drop:
        /// the idle rests at 31-35 degrees below horizontal, the walk swings 0-50, and the
        /// tip clears the ground by 11cm at the bottom of the back-swing (33cm at rest). A
        /// steeper drop reads more relaxed and puts the tip into the ground mid-stride -
        /// at 40 degrees it clears by 4cm, past that it goes through. The one cost is the
        /// handle, which now crosses the fist at 48 degrees to the line it was captured
        /// on; at a guard's distance that is a wrist, and a sword pointing at the player
        /// is not.
        ///
        /// Relative to the item's grip rather than an absolute rotation, so re-tuning the
        /// player's grip on the animation bench carries the guards with it. It is a
        /// rotation about the weapon's own axis, so if a re-tune flips the blade over,
        /// flip the sign here too.
        /// </summary>
        private static readonly Dictionary<string, float> CarryTilt = new Dictionary<string, float>
        {
            { "IronLongsword", -48f },
        };

        private const string UseSkillScriptPath = "Assets/OpenMMORPG/Demo/Scripts/DemoUseSkillComponent.cs";

        /// <summary>
        /// The chance a hit taken while casting breaks the cast. The kit's rule is certainty;
        /// at about one in three, a Meteor (1.4s, roughly one bite long) mostly gets through a
        /// wolf, and an Arcane Bolt (0.6s) nearly always. See
        /// <see cref="MultiplayerARPG.Demo.DemoUseSkillComponent"/>.
        /// </summary>
        private const float CastInterruptChance = 0.35f;

        /// <summary>
        /// Swaps the entity's skill component for the demo's, which lets a hit only sometimes
        /// break a cast.
        ///
        /// The script is swapped on the component the template already carries, rather than the
        /// component being replaced: it keeps its file id and its place in the component list,
        /// so the network behaviour order and anything pointing at it are unchanged. The kit
        /// finds it by `GetOrAddComponent&lt;ICharacterUseSkillComponent, ...&gt;`, which a subclass
        /// satisfies, so it never adds a second.
        /// </summary>
        private static void FocusCasting(GameObject entity)
        {
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(UseSkillScriptPath);
            var component = entity.GetComponent<DefaultCharacterUseSkillComponent>();
            if (script == null || component == null)
            {
                Debug.LogError($"[{nameof(DemoEntityBuilder)}] Could not give {entity.name} the demo's skill " +
                               $"component: {(script == null ? UseSkillScriptPath + " is missing" : "it has no skill component")}.");
                return;
            }
            if (!(component is MultiplayerARPG.Demo.DemoUseSkillComponent))
            {
                var serialized = new SerializedObject(component);
                serialized.FindProperty("m_Script").objectReferenceValue = script;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            var focus = entity.GetComponent<MultiplayerARPG.Demo.DemoUseSkillComponent>();
            if (focus == null)
            {
                Debug.LogError($"[{nameof(DemoEntityBuilder)}] Swapping {entity.name}'s skill component script did not take.");
                return;
            }
            focus.interruptChance = CastInterruptChance;
        }

        /// <summary>The player skill that dashes, and the empty child its handler moves.</summary>
        private const string DashSkill = "Charge";
        private const string DashAnchorName = "DashHop";

        /// <summary>
        /// Gives a player the handler that makes Charge land its hit.
        ///
        /// **Charge never dealt its arrival damage until 2026-09-23.** The skill is set up for
        /// it - a 2.5m post-dash lookup and 10-15 damage - but `SimpleDashAttackSkill` only
        /// gets told the dash has ended through a `DashAttackHandler` on the character,
        /// configured for that one skill: `sourceType` Skill, `sourceDataId` the skill's id,
        /// and a transform to animate. The kit's entity setting does add a handler to every
        /// player, but a blank one, which switches itself off in `Start` - so the dash closed
        /// the distance and the damage never came. Measured live: 9.1m closed to 1.3m, target
        /// untouched.
        ///
        /// Configured here, on the prefab, it is the one the kit's `GetOrAddComponent` finds,
        /// so no blank handler is added beside it.
        ///
        /// **Its transform is an empty child and its curve is flat.** During a dash the kit
        /// sets that transform's local height straight from `jumpCurve` - not added to the
        /// resting height, set to it - and the default curve rises to a metre, which is a leap
        /// attack. Charge is a run. Pointed at the model, even a flat curve would pin its height
        /// to zero, and `DemoSurfaceSwimmer` moves the model's height too; an empty child moves
        /// nothing anyone can see. Point it at the model and give it a curve if a skill should
        /// ever jump.
        /// </summary>
        private static void AddChargeHandler(GameObject entity)
        {
            BaseSkill skill = DemoSkillBuilder.Asset(DashSkill);
            if (skill == null)
            {
                Debug.LogError($"[{nameof(DemoEntityBuilder)}] No \"{DashSkill}\" skill; run Build Skills first, " +
                               "or Charge will close the distance and never hit.");
                return;
            }
            Transform anchor = entity.transform.Find(DashAnchorName);
            if (anchor == null)
            {
                anchor = new GameObject(DashAnchorName).transform;
                anchor.SetParent(entity.transform, false);
            }
            var handler = entity.GetComponent<DashAttackHandler>();
            if (handler == null)
                handler = entity.AddComponent<DashAttackHandler>();
            handler.sourceType = ApplyMovementForceSourceType.Skill;
            handler.sourceDataId = skill.DataId;
            handler.jumpAnimTransform = anchor;
            handler.jumpCurve = AnimationCurve.Constant(0f, 1f, 0f);
        }

        private const string EntitySettingPath = "Assets/OpenMMORPG/Demo/GameData/DemoEntitySetting.asset";
        private const string GameInstancePath = "Assets/OpenMMORPG/Demo/Prefabs/GameInstance.prefab";

        /// <summary>
        /// Points GameInstance at the demo's entity setting, which stops monsters being given a
        /// blank dash handler - see `DemoEntitySetting` for the knockback crash that caused.
        /// Written to the GameInstance **prefab**, as every GameInstance setting is.
        /// </summary>
        private static void EnsureEntitySetting()
        {
            var setting = AssetDatabase.LoadAssetAtPath<MultiplayerARPG.Demo.DemoEntitySetting>(EntitySettingPath);
            if (setting == null)
            {
                setting = ScriptableObject.CreateInstance<MultiplayerARPG.Demo.DemoEntitySetting>();
                AssetDatabase.CreateAsset(setting, EntitySettingPath);
            }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GameInstancePath);
            GameInstance instance = prefab != null ? prefab.GetComponent<GameInstance>() : null;
            if (instance == null)
            {
                Debug.LogError($"[{nameof(DemoEntityBuilder)}] No GameInstance at {GameInstancePath}.");
                return;
            }
            var serialized = new SerializedObject(instance);
            SerializedProperty field = serialized.FindProperty("entitySetting");
            if (field.objectReferenceValue == setting)
                return;
            field.objectReferenceValue = setting;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(prefab);
        }

        private static void Arm(GameObject modelInstance, string itemName)
        {
            var item = AssetDatabase.LoadAssetAtPath<BaseItem>($"Assets/OpenMMORPG/Demo/GameData/Resources/Items/{itemName}.asset");
            if (item == null)
            {
                Debug.LogError($"[{nameof(DemoEntityBuilder)}] No item \"{itemName}\" to arm an NPC with. Run Build Items first.");
                return;
            }
            SerializedProperty models = new SerializedObject(item).FindProperty("equipmentModels");
            if (models == null || models.arraySize == 0)
            {
                Debug.LogError($"[{nameof(DemoEntityBuilder)}] \"{itemName}\" has no equipment model to hold.");
                return;
            }
            SerializedProperty model = models.GetArrayElementAtIndex(0);
            string socket = model.FindPropertyRelative("equipSocket").stringValue;
            var prefab = model.FindPropertyRelative("meshPrefab").objectReferenceValue as GameObject;
            Transform container = Socket(modelInstance, socket);
            if (prefab == null || container == null)
            {
                Debug.LogError($"[{nameof(DemoEntityBuilder)}] Cannot hold \"{itemName}\": no prefab, or no \"{socket}\" socket on {modelInstance.name}.");
                return;
            }
            var held = (GameObject)PrefabUtility.InstantiatePrefab(prefab, container);
            held.transform.localPosition = model.FindPropertyRelative("localPosition").vector3Value;
            held.transform.localEulerAngles = model.FindPropertyRelative("localEulerAngles").vector3Value;
            float tilt;
            if (CarryTilt.TryGetValue(itemName, out tilt))
                held.transform.localRotation *= Quaternion.Euler(0f, 0f, tilt);
            Vector3 scale = model.FindPropertyRelative("localScale").vector3Value;
            held.transform.localScale = scale == Vector3.zero ? Vector3.one : scale;
        }

        /// <summary>
        /// Gives an animal's model its animator and its renderer, and stops the animator
        /// being culled.
        ///
        /// **Without this a wolf is invisible while it is still biting you.** A
        /// `SkinnedMeshRenderer` is culled against bounds derived from its bones, and those
        /// bounds only move when something drives the skeleton. With `animator` unset the
        /// model's playable graph never takes the animator over, so the bones stay where the
        /// bind pose left them, the bounds stay a box round the origin, and Unity frustum
        /// culls a wolf that is standing in front of you. The entity is perfectly alive
        /// underneath: it closes, it attacks, the hit effects and the damage arrive, and
        /// there is nothing on screen to click on, because the collider that would be hit
        /// rides the same unmoved skeleton.
        ///
        /// `DemoCharacterBuilder` has always done this for the people. The animals go
        /// through this builder instead and were never given the same treatment, so the
        /// wolf, the deer and the dog have been shipping with `animator` and
        /// `skinnedMeshRenderer` null.
        ///
        /// `AlwaysAnimate` for the same reason it is set on the characters: the server
        /// drives combat from animation timing, so an attack animated while culled would
        /// never land its hit.
        /// </summary>
        private static void WireModelRenderer(GameObject modelInstance, PlayableCharacterModel model)
        {
            var animator = modelInstance.GetComponent<Animator>();
            if (animator != null)
            {
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                model.animator = animator;
            }
            else
            {
                Debug.LogWarning($"[{nameof(DemoEntityBuilder)}] \"{modelInstance.name}\" has no Animator; " +
                                 "its model cannot be driven and it will be culled where it stands.");
            }

            // The first skinned renderer under the model. Animals are a single mesh, so
            // there is nothing to choose between - unlike a character, where the bone map is
            // read off a renderer that has to carry the whole skeleton.
            var renderer = modelInstance.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (renderer != null)
                model.skinnedMeshRenderer = renderer;
            else
                Debug.LogWarning($"[{nameof(DemoEntityBuilder)}] \"{modelInstance.name}\" has no " +
                                 "SkinnedMeshRenderer.");
        }

        /// <summary>The transform a character model equips a socket's items under.</summary>
        private static Transform Socket(GameObject modelInstance, string socket)
        {
            var model = modelInstance.GetComponent<PlayableCharacterModel>();
            if (model == null)
                return null;
            SerializedProperty containers = new SerializedObject(model).FindProperty("equipmentContainers");
            for (int i = 0; i < containers.arraySize; ++i)
            {
                SerializedProperty container = containers.GetArrayElementAtIndex(i);
                if (container.FindPropertyRelative("equipSocket").stringValue == socket)
                    return container.FindPropertyRelative("transform").objectReferenceValue as Transform;
            }
            return null;
        }

        private static Transform MakeAnchor(Transform parent, string name, float height)
        {
            var anchor = new GameObject(name);
            anchor.transform.SetParent(parent, false);
            anchor.transform.localPosition = new Vector3(0f, height, 0f);
            return anchor.transform;
        }

        /// <summary>
        /// Returns the MonsterCharacter of that name, making an empty one if it is not there
        /// yet. Only the asset's existence matters here — DemoDatabaseWiring fills in its
        /// name, its stats and what it drops.
        /// </summary>
        private static MonsterCharacter MonsterData(string name)
        {
            string path = $"{MonsterDir}/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<MonsterCharacter>(path);
            if (existing != null)
                return existing;
            var created = ScriptableObject.CreateInstance<MonsterCharacter>();
            AssetDatabase.CreateAsset(created, path);
            Debug.Log($"[{nameof(DemoEntityBuilder)}] Created monster data \"{name}\".");
            return created;
        }

        /// <summary>
        /// Players swim on the surface and only there. The kit's movement can dive
        /// (SwimUp/SwimDown), but with autoSwimToSurface on it always heads for the
        /// surface and a Down input is ignored, and the demo binds no dive keys anyway.
        /// The sea itself is the trigger volume DemoSceneBuilder puts under the water
        /// plane. Also given to the horse, so a mount ridden into the sea floats rather
        /// than sinks. The kit holds the capsule 0.75 of its height under the surface,
        /// which is right for treading water and wrong for the demo's flat swim clips, so
        /// <see cref="DemoSurfaceSwimmer"/> lifts the model to <paramref name="depthBelowSurface"/>
        /// while swimming: near the surface for a body lying flat, deeper for the horse.
        /// </summary>
        internal static void SwimOnSurface(GameObject entity, float depthBelowSurface = 0.15f)
        {
            var movement = entity.GetComponent<CharacterControllerEntityMovement>();
            if (movement == null)
            {
                Debug.LogError($"[{nameof(DemoEntityBuilder)}] {entity.name} has no CharacterControllerEntityMovement to set swimming on.");
                return;
            }
            var serialized = new SerializedObject(movement);
            serialized.FindProperty("autoSwimToSurface").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var swimmer = entity.GetComponent<MultiplayerARPG.Demo.DemoSurfaceSwimmer>();
            if (swimmer == null)
                swimmer = entity.AddComponent<MultiplayerARPG.Demo.DemoSurfaceSwimmer>();
            swimmer.depthBelowSurface = depthBelowSurface;
        }

        private static void Build(string templatePath, string modelPath, string outputPath, string monsterData = null, string title = null, float scale = 1f)
        {
            GameObject template = AssetDatabase.LoadAssetAtPath<GameObject>(templatePath);
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (template == null)
            {
                // Said separately from the model, and said fully, because this one is
                // recoverable and reads like a dead end otherwise: it fired twice into a
                // busy log and the two player entities were quietly not rebuilt.
                Debug.LogError($"[{nameof(DemoEntityBuilder)}] No template at \"{templatePath}\". " +
                               "It is tracked in the kit repo and nothing references it, so a cleanup " +
                               "pass can delete it without anything noticing. Restore it with " +
                               "`git checkout -- Demo/Prefabs/GamePlay/CharacterEntities/` from " +
                               $"Assets/OpenMMORPG. Nothing was written for {outputPath}.");
                return;
            }
            if (model == null)
            {
                Debug.LogError($"[{nameof(DemoEntityBuilder)}] No model at \"{modelPath}\". " +
                               $"Nothing was written for {outputPath}.");
                return;
            }

            GameObject entity = (GameObject)PrefabUtility.InstantiatePrefab(template);
            PrefabUtility.UnpackPrefabInstance(entity, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            entity.name = System.IO.Path.GetFileNameWithoutExtension(outputPath);

            // Drop the placeholder capsule and the model component that drove it. The
            // real model is a child, so the root keeps neither.
            Transform capsule = entity.transform.Find("CapsuleModel");
            if (capsule != null)
                Object.DestroyImmediate(capsule.gameObject);
            PlayableCharacterModel rootModel = entity.GetComponent<PlayableCharacterModel>();
            if (rootModel != null)
                Object.DestroyImmediate(rootModel);
            Animator rootAnimator = entity.GetComponent<Animator>();
            if (rootAnimator != null)
                Object.DestroyImmediate(rootAnimator);

            GameObject modelInstance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            modelInstance.transform.SetParent(entity.transform, false);
            modelInstance.name = "Model";
            // A boss is told apart at a glance by its size; the scale goes on the model
            // rather than the entity, so the capsule and the anchors stay where the kit
            // expects them.
            modelInstance.transform.localScale = Vector3.one * scale;

            CharacterModelManager manager = entity.GetComponent<CharacterModelManager>();
            manager.MainTpsModel = modelInstance.GetComponent<PlayableCharacterModel>();

            // Players can climb: the watchtower's ladder is there to show that they can.
            // The kit's template entity does not carry the ladder component - climbing is
            // opt-in - and the entity finds it with GetComponent, so being present is all
            // it takes; the network identity gathers its behaviours when it starts.
            if (monsterData == null && entity.GetComponent<CharacterLadderComponent>() == null)
                entity.AddComponent<CharacterLadderComponent>();

            if (monsterData == null)
                SwimOnSurface(entity);

            if (monsterData == null)
                AddChargeHandler(entity);

            // Players and monsters alike, so a hit breaks the Hierophant's casts by the same
            // odds as the mage's.
            FocusCasting(entity);

            if (monsterData == null)
            {
                // What a player can choose about this body, read off the model just built.
                DemoBodyPartBuilder.AddComponents(entity, modelInstance);
                if (!string.IsNullOrEmpty(title))
                {
                    // The name the create screen lists the body under; the template's is
                    // "Base Character", which tells two bodies apart not at all.
                    var serialized = new SerializedObject(entity.GetComponent<BaseGameEntity>());
                    serialized.FindProperty("entityTitle").stringValue = title;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            if (monsterData != null)
            {
                var monster = entity.GetComponent<MonsterCharacterEntity>();
                if (monster == null)
                {
                    Debug.LogError($"[{nameof(DemoEntityBuilder)}] {outputPath} is not a monster, so it has no character data to set.");
                }
                else
                {
                    var serialized = new SerializedObject(monster);
                    serialized.FindProperty("characterDatabase").objectReferenceValue = MonsterData(monsterData);
                    // Clear the template's own title, or it shadows the one on the data
                    // asset: `BaseGameEntity.Title` prefers `entityTitle` when it is set,
                    // and the kit's template ships it as the literal word "Enemy". Every
                    // bandit, marauder and cultist in the demo wore that one nameplate
                    // until 2026-09-15 — the three families were indistinguishable at a
                    // glance, and the deer would have been labelled an enemy too.
                    serialized.FindProperty("entityTitle").stringValue = string.Empty;
                    // The body lies there as long as its loot does - see CorpseLifetime.
                    serialized.FindProperty("destroyDelay").floatValue = CorpseLifetime;
                    serialized.FindProperty("destroyRespawnDelay").floatValue = RespawnAfterBody;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            FitColliders(entity);
            PlaceAnchors(entity);
            DemoAudioWiring.WireCharacter(entity, modelPath.Contains("Female"));

            PrefabUtility.SaveAsPrefabAsset(entity, outputPath);
            GiveOwnNetworkId(outputPath);
            // Forced, because the editor's loaded copy of a prefab is not refreshed by
            // writing a new file over it. Where a mesh or material this references was
            // itself regenerated earlier in the same run, that loaded copy can be holding
            // a reference the rebuild has since repaired on disk - and every later step
            // reads the loaded copy, so the stale one propagates into everything built
            // from it, silently and with nothing missing in the inspector to show for it.
            AssetDatabase.ImportAsset(outputPath, ImportAssetOptions.ForceUpdate);
            Object.DestroyImmediate(entity);
            Debug.Log($"[{nameof(DemoEntityBuilder)}] Built {outputPath}.");
        }

        /// <summary>
        /// Gives a built entity a network asset id of its own.
        ///
        /// Every networked prefab is told apart by the hash of its LiteNetLibIdentity's
        /// asset id, which the identity fills in from the prefab's GUID the first time it
        /// is validated - and never again while it holds a value. An entity cloned from a
        /// template therefore keeps the template's id: the prefab is new, the GUID is new,
        /// and the id inside it is still BaseCharacter's or BaseEnemy's. Six enemies and
        /// two player bodies then all answer to two ids, so the prefab registry keeps one
        /// of each and the rest can neither be spawned nor chosen, with nothing to say so
        /// beyond the wrong monster turning up. Set from the saved prefab's GUID, the way
        /// the identity itself would for a prefab made by hand.
        ///
        /// **Written and saved even when the loaded copy already holds the right id.** The
        /// identity fills an empty id in `OnValidate` as the new prefab loads, so the loaded
        /// copy usually has it already - but it marks itself dirty through
        /// `EditorApplication.delayCall`, which does not run while the editor sits idle
        /// behind a script or the MCP bridge. An early return on a matching id then left the
        /// file on disk with `assetId:` empty. Found 2026-09-23 on all seven NPCs, whose
        /// template carries no id to overwrite, after a rebuild; the monsters were spared
        /// only because they start from the template's id and so always differ.
        /// </summary>
        internal static void GiveOwnNetworkId(string prefabPath)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var identity = prefab != null ? prefab.GetComponent<LiteNetLibManager.LiteNetLibIdentity>() : null;
            if (identity == null)
                return;
            string guid = AssetDatabase.AssetPathToGUID(prefabPath);
            var serialized = new SerializedObject(identity);
            serialized.FindProperty("assetId").stringValue = guid;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(prefab);
            AssetDatabase.SaveAssets();
        }

        private static void FitColliders(GameObject entity)
        {
            CharacterController controller = entity.GetComponent<CharacterController>();
            if (controller != null)
            {
                controller.height = CharacterHeight;
                controller.radius = CharacterRadius;
                controller.center = new Vector3(0f, CharacterHeight * 0.5f, 0f);
            }

            CapsuleCollider capsule = entity.GetComponent<CapsuleCollider>();
            if (capsule != null)
            {
                capsule.height = CharacterHeight;
                capsule.radius = CharacterRadius;
                capsule.center = new Vector3(0f, CharacterHeight * 0.5f, 0f);
            }

            NavMeshAgent agent = entity.GetComponent<NavMeshAgent>();
            if (agent != null)
            {
                agent.height = CharacterHeight;
                agent.radius = CharacterRadius;
            }
        }

        /// <summary>
        /// Damage numbers read from the chest, nameplates and health bars sit above
        /// the head. Both were pinned to the capsule and need moving onto the taller
        /// character.
        /// </summary>
        private static void PlaceAnchors(GameObject entity)
        {
            Transform transforms = entity.transform.Find("Transforms");
            if (transforms == null)
                return;
            Place(transforms, "DamageTransform", CharacterHeight * 0.6f);
            Place(transforms, "UIElementContainer", CharacterHeight + 0.25f);
            Place(transforms, "ChatBubbleTransform", CharacterHeight + 0.4f);
            Place(transforms, "MiniMapContainer", 0f);
        }

        private static void Place(Transform parent, string name, float height)
        {
            Transform target = parent.Find(name);
            if (target != null)
                target.localPosition = new Vector3(0f, height, 0f);
        }
    }
}
