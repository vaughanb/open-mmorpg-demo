using MultiplayerARPG.GameData.Model.Playables;
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

        private const string PlayerTemplate = EntityDir + "/BaseCharacter.prefab";
        private const string EnemyTemplate = EntityDir + "/BaseEnemy.prefab";

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
            // The guards, sword and shield in hand: one stands the watchtower deck, the
            // other walks the green and needs to be able to move.
            string[] guardArms = { "IronLongsword", "PaintedRoundShield" };
            BuildNpc($"{ModelDir}/GuardModel_Male.prefab", $"{EntityDir}/DemoGuard.prefab", guardArms, false);
            BuildNpc($"{ModelDir}/GuardModel_Male.prefab", $"{EntityDir}/DemoPatrolGuard.prefab", guardArms, true);
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
                CharacterModelManager manager = entity.GetComponent<CharacterModelManager>();
                if (manager == null)
                    manager = entity.AddComponent<CharacterModelManager>();
                manager.MainTpsModel = model3d;
            }

            if (armedWith != null)
            {
                foreach (string item in armedWith)
                    Arm(modelInstance, item);
            }

            if (patrols)
            {
                // The kit's navmesh mover, the same one its monsters walk on, driven by
                // a demo component that hands it one point of a loop at a time. The
                // base entity animates its model from the movement state on its own.
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
            Vector3 scale = model.FindPropertyRelative("localScale").vector3Value;
            held.transform.localScale = scale == Vector3.zero ? Vector3.one : scale;
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
            if (template == null || model == null)
            {
                Debug.LogError($"[{nameof(DemoEntityBuilder)}] Missing template \"{templatePath}\" or model \"{modelPath}\".");
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
        /// </summary>
        internal static void GiveOwnNetworkId(string prefabPath)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var identity = prefab != null ? prefab.GetComponent<LiteNetLibManager.LiteNetLibIdentity>() : null;
            if (identity == null)
                return;
            string guid = AssetDatabase.AssetPathToGUID(prefabPath);
            if (identity.AssetId == guid)
                return;
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
