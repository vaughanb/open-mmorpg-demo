using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// Creates the demo's NPCs, their dialogs, and the quest that sends the player at
    /// the bandit camp.
    ///
    /// The NPCs are placed in the map scene as prefab instances under one root, the
    /// way the kit's own demos did it, so that where each one stands and which way it
    /// looks can be adjusted by hand in the editor. Running this again only adds an NPC
    /// that is missing and refreshes titles and dialogs; it never moves one that is
    /// already there. The scene builder keeps that root through a rebuild for the same
    /// reason. The NpcDatabase is left empty so nothing spawns twice.
    ///
    /// Each placed NPC is posed with the idle clip through a PlayableGraph so the editor
    /// shows it the way the game will: these bodies are modelled facing -Z and the
    /// library is corrected to +Z by a clip import setting that only the animation
    /// runtime applies, so the bind pose in the editor shows every NPC with its back to
    /// whatever it is looking at. A gizmo arrow says the same thing without the pose.
    ///
    /// The banker is the reason the storage system is reachable at all: player storage
    /// only opens through a dialog of type PlayerStorage, and its size comes from
    /// GameInstance rather than from the NPC.
    /// </summary>
    public static class DemoNpcBuilder
    {
        private const string GameDataDir = "Assets/OpenMMORPG/Demo/GameData";
        private const string ResourcesDir = GameDataDir + "/Resources";
        private const string DialogDir = ResourcesDir + "/NpcDialogs";
        private const string QuestDir = ResourcesDir + "/Quests";
        private const string ItemDir = ResourcesDir + "/Items";
        private const string EntityDir = "Assets/OpenMMORPG/Demo/Prefabs/GamePlay/CharacterEntities";

        /// <summary>
        /// Where each NPC stands, relative to the village centre.
        ///
        /// The banker stands at the middle of the bank, which is the one point in that
        /// building the house's own rotation cannot move, so it stays right however the
        /// village is laid out. He is read from DemoSceneBuilder's layout rather than
        /// copied, so the two cannot drift apart and leave him standing in the street.
        /// </summary>
        public static Vector3 BankerLocalPosition
        {
            get { return DemoSceneBuilder.HouseLayout[DemoSceneBuilder.BankHouseIndex]; }
        }

        /// <summary>
        /// Half a turn, because a character driven by the kit faces along its own +Z while
        /// the same body in its bind pose faces -Z. See the placement code for how that was
        /// measured and why the editor cannot be asked.
        /// </summary>
        private const float FacingCorrection = 180f;

        public static readonly Vector3 ElderLocalPosition = new Vector3(-2.5f, 0f, 2f);

        /// <summary>
        /// The pedlar stands behind his counter, a step back from the stall and a little
        /// off its middle, facing out over it the way the stall faces - which is toward
        /// the green, where his customers are. He used to stand in front of it with his
        /// back to his own goods. Given in the stall's space, so he follows the stall.
        /// </summary>
        public static Vector3 MerchantLocalPosition
        {
            get
            {
                Quaternion yaw = Quaternion.Euler(0f, DemoSceneBuilder.StallYaw, 0f);
                return DemoSceneBuilder.StallLayout + yaw * new Vector3(0.6f, 0f, -1.0f);
            }
        }

        /// <summary>Facing over the counter: the stall's own yaw, since its counter is on its +Z.</summary>
        public static float MerchantYaw
        {
            get { return DemoSceneBuilder.StallYaw; }
        }

        /// <summary>
        /// The tower guard stands on the watchtower deck, a little off centre so the
        /// player has room to come up the ladder, looking out over the parapet toward
        /// the headland the bandits hold. Given in the tower's own space, like the
        /// innkeeper in the alehouse's, so he follows the tower.
        /// </summary>
        public static Vector3 TowerGuardLocalPosition
        {
            get
            {
                Vector3 tower = DemoSceneBuilder.WatchtowerLayout;
                Quaternion yaw = Quaternion.Euler(0f, DemoSceneBuilder.HouseYaw(tower), 0f);
                return tower + yaw * new Vector3(0.6f, DemoSceneBuilder.WatchtowerDeckHeight, 1.0f);
            }
        }

        /// <summary>
        /// The round the other guard walks: the lane in on the west, the north side of
        /// the green, the foot of the tower on the east and the south side, and round
        /// again. Each point is snapped to the navmesh when placed, so a point that
        /// lands in a stall or a wall is nudged out rather than walked into. Edit them on
        /// the guard's DemoPatrol component in the scene; this is only where he starts.
        /// </summary>
        public static readonly Vector3[] PatrolLocalRoute =
        {
            new Vector3(-10.5f, 0f, 0.5f), new Vector3(0f, 0f, 4f), new Vector3(10.5f, 0f, -3f), new Vector3(0f, 0f, -4f),
        };

        /// <summary>
        /// The innkeeper stands inside the alehouse, by the casks on its east wall and
        /// clear of the door's swing, facing the door. Given in the house's own space and
        /// turned out through the house's yaw, so she follows the building wherever the
        /// village layout puts it - the same reason the banker is read from the layout.
        /// </summary>
        public static Vector3 InnkeeperLocalPosition
        {
            get
            {
                Vector3 house = DemoSceneBuilder.HouseLayout[DemoSceneBuilder.AlehouseIndex];
                Quaternion yaw = Quaternion.Euler(0f, DemoSceneBuilder.HouseYaw(house), 0f);
                return house + yaw * new Vector3(1.7f, 0f, -0.9f);
            }
        }

        [MenuItem("Open MMORPG/Demo/Build NPCs And Quests")]
        public static void BuildAll()
        {
            DemoItemBuilder.EnsureFolder(DialogDir);
            DemoItemBuilder.EnsureFolder(QuestDir);

            Quest quest = BuildQuest();
            NpcDialog banker = BuildBankerDialogs();
            NpcDialog merchant = BuildMerchantDialog();
            NpcDialog innkeeper = BuildInnkeeperDialog(quest);
            NpcDialog elder = BuildElderDialogs(quest);
            NpcDialog towerGuard = SimpleDialog("TowerGuard", "Watchtower Guard",
                "Nothing on the headland road since first light. Rowan wants it kept that way, so if you go out there, do not lead them back.");
            NpcDialog patrolGuard = SimpleDialog("PatrolGuard", "Town Guard",
                "Keep to the green after dark and you will come to no harm. It is the road past the tower I would not walk alone.");

            PlaceInScene(banker, merchant, innkeeper, elder, towerGuard, patrolGuard);
            ClearNpcDatabase();
            ConfigureGameInstance();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[{nameof(DemoNpcBuilder)}] Built demo NPCs, dialogs and quest.");
        }

        // ---- quest -----------------------------------------------------------

        private static Quest BuildQuest()
        {
            var quest = Create<Quest>($"{QuestDir}/ThinTheCamp.asset");
            var serialized = new SerializedObject(quest);
            serialized.FindProperty("id").stringValue = "ThinTheCamp";
            serialized.FindProperty("defaultTitle").stringValue = "Thin the Camp";
            serialized.FindProperty("defaultDescription").stringValue =
                "Bandits have taken the headland southeast of here. Cut their numbers down and bring me proof.";
            serialized.FindProperty("rewardExp").intValue = 400;
            serialized.FindProperty("rewardGold").intValue = 250;
            serialized.FindProperty("canAbandon").boolValue = true;
            serialized.FindProperty("autoTrackQuest").boolValue = true;

            // Tasks live under randomTasks; the kit migrates its old flat list into the
            // first entry, so a quest with one fixed set of objectives uses index 0.
            SerializedProperty randomTasks = serialized.FindProperty("randomTasks");
            randomTasks.arraySize = 1;
            SerializedProperty tasks = randomTasks.GetArrayElementAtIndex(0).FindPropertyRelative("tasks");
            tasks.arraySize = 1;

            MonsterCharacter bandit = AssetDatabase.LoadAssetAtPath<MonsterCharacter>($"{ResourcesDir}/MonsterCharacters/BaseEnemy.asset");
            SerializedProperty kill = tasks.GetArrayElementAtIndex(0);
            kill.FindPropertyRelative("taskType").enumValueIndex = (int)QuestTaskType.KillMonster;
            kill.FindPropertyRelative("monsterCharacterAmount.monster").objectReferenceValue = bandit;
            kill.FindPropertyRelative("monsterCharacterAmount.amount").intValue = 8;

            SerializedProperty rewards = serialized.FindProperty("rewardItems");
            rewards.arraySize = 1;
            rewards.GetArrayElementAtIndex(0).FindPropertyRelative("item").objectReferenceValue = LoadItem("PaintedRoundShield");
            rewards.GetArrayElementAtIndex(0).FindPropertyRelative("amount").intValue = 1;

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(quest);
            return quest;
        }

        // ---- dialogs ---------------------------------------------------------

        /// <summary>
        /// The banker. A greeting with a menu leading into the storage dialog, so
        /// opening the strongbox reads as a conversation rather than a UI popping up
        /// the moment the player walks near.
        /// </summary>
        private static NpcDialog BuildBankerDialogs()
        {
            var storage = Create<NpcDialog>($"{DialogDir}/BankStorage.asset");
            var storageSerialized = new SerializedObject(storage);
            storageSerialized.FindProperty("title").stringValue = "Strongbox";
            storageSerialized.FindProperty("description").stringValue = "Take what you need. It will keep.";
            storageSerialized.FindProperty("type").enumValueIndex = (int)NpcDialogType.PlayerStorage;
            storageSerialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(storage);

            var farewell = Create<NpcDialog>($"{DialogDir}/BankFarewell.asset");
            var farewellSerialized = new SerializedObject(farewell);
            farewellSerialized.FindProperty("title").stringValue = "Fenwick";
            farewellSerialized.FindProperty("description").stringValue = "The door is always open. Mind the step.";
            farewellSerialized.FindProperty("type").enumValueIndex = (int)NpcDialogType.Normal;
            SetMenus(farewellSerialized, new[] { new MenuSpec { Title = "Goodbye", Close = true } });
            farewellSerialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(farewell);

            var greeting = Create<NpcDialog>($"{DialogDir}/BankerGreeting.asset");
            var serialized = new SerializedObject(greeting);
            serialized.FindProperty("title").stringValue = "Fenwick the Keeper";
            serialized.FindProperty("description").stringValue =
                "Everything the village cannot carry, it leaves with me. Shall I bring out your strongbox?";
            serialized.FindProperty("type").enumValueIndex = (int)NpcDialogType.Normal;
            SetMenus(serialized, new[]
            {
                new MenuSpec { Title = "Open my strongbox", Dialog = storage },
                new MenuSpec { Title = "Not today", Dialog = farewell },
            });
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(greeting);
            return greeting;
        }

        private static NpcDialog BuildMerchantDialog()
        {
            var shop = Create<NpcDialog>($"{DialogDir}/MerchantShop.asset");
            var shopSerialized = new SerializedObject(shop);
            shopSerialized.FindProperty("title").stringValue = "Goods";
            shopSerialized.FindProperty("description").stringValue = "Fair prices, mostly.";
            shopSerialized.FindProperty("type").enumValueIndex = (int)NpcDialogType.Shop;
            // The whistle that calls the horse is sold here, so a player who rode the
            // one on the green and lost it out in the hills has a way to another.
            SetSellItems(shopSerialized, new[]
            {
                "MinorHealingPotion", "IronShortsword", "PeasantTunic",
                "PeasantTrousers", "PeasantShoes", "PeasantSleeves", "HorseWhistle",
            });
            shopSerialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(shop);

            var greeting = Create<NpcDialog>($"{DialogDir}/MerchantGreeting.asset");
            var serialized = new SerializedObject(greeting);
            serialized.FindProperty("title").stringValue = "Marek the Pedlar";
            serialized.FindProperty("description").stringValue =
                "Come off the boat, have you? You will want boots before you want a sword.";
            serialized.FindProperty("type").enumValueIndex = (int)NpcDialogType.Normal;
            SetMenus(serialized, new[]
            {
                new MenuSpec { Title = "Show me your wares", Dialog = shop },
                new MenuSpec { Title = "Maybe later", Close = true },
            });
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(greeting);
            return greeting;
        }

        /// <summary>
        /// The innkeeper. Her shop sells the alehouse's food and drink, which is every
        /// provision the item builder makes, so adding a dish there puts it on her board.
        /// </summary>
        private static NpcDialog BuildInnkeeperDialog(Quest quest)
        {
            var board = Create<NpcDialog>($"{DialogDir}/InnkeeperBoard.asset");
            var boardSerialized = new SerializedObject(board);
            boardSerialized.FindProperty("title").stringValue = "Board and Drink";
            boardSerialized.FindProperty("description").stringValue = "The stew is today's. The ale is whenever.";
            boardSerialized.FindProperty("type").enumValueIndex = (int)NpcDialogType.Shop;
            SetSellItems(boardSerialized, DemoItemBuilder.ProvisionNames());
            boardSerialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(board);

            // The rumour points at the quest without giving it: it is Rowan's to hand
            // out, and an alehouse is where a stranger hears who to ask.
            var rumour = Create<NpcDialog>($"{DialogDir}/InnkeeperRumour.asset");
            var rumourSerialized = new SerializedObject(rumour);
            rumourSerialized.FindProperty("title").stringValue = "Hilde the Innkeeper";
            rumourSerialized.FindProperty("description").stringValue =
                "Bandits on the headland southeast, past the hills. They had my second-best cask off the cart last week, " +
                "and nobody here can lift a blade. Elder Rowan is out on the green asking after anyone who can. " +
                "See him, then come back and I will feed you properly.";
            rumourSerialized.FindProperty("type").enumValueIndex = (int)NpcDialogType.Normal;
            SetMenus(rumourSerialized, new[]
            {
                new MenuSpec { Title = "I will find him", Close = true },
                new MenuSpec { Title = "First, what have you got?", Dialog = board },
            });
            rumourSerialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(rumour);

            // What she says once the camp is thinned. The kit cannot change an NPC's
            // opening line by quest state, only which choices it offers, so this hangs
            // off a menu that appears only when the quest is complete - and the rumour
            // menus go away at the same time, so she does not send you after bandits
            // you have already dealt with.
            var farewell = Create<NpcDialog>($"{DialogDir}/InnkeeperFarewell.asset");
            var farewellSerialized = new SerializedObject(farewell);
            farewellSerialized.FindProperty("title").stringValue = "Hilde the Innkeeper";
            farewellSerialized.FindProperty("description").stringValue =
                "So I heard - Rowan came in shouting it before you had crossed the green. Sit down. The stew is on " +
                "the house tonight, and the cask we are drinking from is the one you brought back. Whenever the " +
                "road tires you, there is a bench here with your name on it. The one that does not wobble.";
            farewellSerialized.FindProperty("type").enumValueIndex = (int)NpcDialogType.Normal;
            SetMenus(farewellSerialized, new[]
            {
                new MenuSpec { Title = "To your health, Hilde", Close = true },
                new MenuSpec { Title = "I will take you up on the stew", Dialog = board },
            });
            farewellSerialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(farewell);

            var greeting = Create<NpcDialog>($"{DialogDir}/InnkeeperGreeting.asset");
            var serialized = new SerializedObject(greeting);
            serialized.FindProperty("title").stringValue = "Hilde the Innkeeper";
            serialized.FindProperty("description").stringValue =
                "Sit anywhere that is not the bench by the door, that one wobbles. You have the look of someone " +
                "the elder wants a word with - but eat first. Hungry, or only thirsty?";
            serialized.FindProperty("type").enumValueIndex = (int)NpcDialogType.Normal;
            SetMenus(serialized, new[]
            {
                new MenuSpec { Title = "What have you got?", Dialog = board },
                new MenuSpec { Title = "What does the elder want?", Dialog = rumour,
                    IfQuest = quest, QuestState = NpcDialogConditionType.QuestNotStarted },
                new MenuSpec { Title = "About those bandits...", Dialog = rumour,
                    IfQuest = quest, QuestState = NpcDialogConditionType.QuestOngoing },
                new MenuSpec { Title = "The headland is clear", Dialog = farewell,
                    IfQuest = quest, QuestState = NpcDialogConditionType.QuestCompleted },
                new MenuSpec { Title = "Just passing through", Close = true },
            });
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(greeting);
            return greeting;
        }

        private static NpcDialog BuildElderDialogs(Quest quest)
        {
            var accepted = SimpleDialog("QuestAccepted", "Elder Rowan",
                "Good. Keep to the ridge and they will not see you coming.");
            var declined = SimpleDialog("QuestDeclined", "Elder Rowan",
                "Then we wait, and we bar the doors at dusk.");
            var completed = SimpleDialog("QuestCompleted", "Elder Rowan",
                "You have done what we could not. Take this — it hung in my father's hall.");

            var offer = Create<NpcDialog>($"{DialogDir}/ElderQuest.asset");
            var serialized = new SerializedObject(offer);
            serialized.FindProperty("title").stringValue = "Elder Rowan";
            serialized.FindProperty("description").stringValue =
                "You carry a blade, and we have a problem that wants one.";
            serialized.FindProperty("type").enumValueIndex = (int)NpcDialogType.Quest;
            serialized.FindProperty("quest").objectReferenceValue = quest;
            serialized.FindProperty("questAcceptedDialog").objectReferenceValue = accepted;
            serialized.FindProperty("questDeclinedDialog").objectReferenceValue = declined;
            serialized.FindProperty("questCompletedDialog").objectReferenceValue = completed;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(offer);
            return offer;
        }

        private static NpcDialog SimpleDialog(string assetName, string title, string description)
        {
            var dialog = Create<NpcDialog>($"{DialogDir}/{assetName}.asset");
            var serialized = new SerializedObject(dialog);
            serialized.FindProperty("title").stringValue = title;
            serialized.FindProperty("description").stringValue = description;
            serialized.FindProperty("type").enumValueIndex = (int)NpcDialogType.Normal;
            SetMenus(serialized, new[] { new MenuSpec { Title = "Farewell", Close = true } });
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(dialog);
            return dialog;
        }

        private struct MenuSpec
        {
            public string Title;
            public NpcDialog Dialog;
            public bool Close;
            /// <summary>
            /// Shown only while this quest is in the given state. The kit hides a menu
            /// whose conditions fail, which is how one NPC says different things before
            /// and after a quest: the opening line is fixed, the choices under it are not.
            /// </summary>
            public Quest IfQuest;
            public NpcDialogConditionType QuestState;
        }

        private static void SetMenus(SerializedObject serialized, MenuSpec[] specs)
        {
            SerializedProperty menus = serialized.FindProperty("menus");
            menus.arraySize = specs.Length;
            for (int i = 0; i < specs.Length; ++i)
            {
                SerializedProperty menu = menus.GetArrayElementAtIndex(i);
                menu.FindPropertyRelative("title").stringValue = specs[i].Title;
                menu.FindPropertyRelative("isCloseMenu").boolValue = specs[i].Close;
                menu.FindPropertyRelative("dialog").objectReferenceValue = specs[i].Dialog;
                SerializedProperty conditions = menu.FindPropertyRelative("showConditions");
                conditions.arraySize = specs[i].IfQuest == null ? 0 : 1;
                if (specs[i].IfQuest != null)
                {
                    SerializedProperty condition = conditions.GetArrayElementAtIndex(0);
                    // intValue, not enumValueIndex: the enum has gaps (the custom kinds sit
                    // at 253 and 254), so index and value are not the same thing.
                    condition.FindPropertyRelative("conditionType").intValue = (int)specs[i].QuestState;
                    condition.FindPropertyRelative("quest").objectReferenceValue = specs[i].IfQuest;
                }
            }
        }

        private static void SetSellItems(SerializedObject serialized, string[] itemNames)
        {
            SerializedProperty items = serialized.FindProperty("sellItems");
            items.arraySize = itemNames.Length;
            for (int i = 0; i < itemNames.Length; ++i)
            {
                SerializedProperty entry = items.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("item").objectReferenceValue = LoadItem(itemNames[i]);
                entry.FindPropertyRelative("level").intValue = 1;
                entry.FindPropertyRelative("amount").intValue = 0;
                // Zero means the shop charges the item's own price.
                entry.FindPropertyRelative("sellPrice").intValue = 0;
            }
        }

        // ---- placement -------------------------------------------------------

        /// <summary>One NPC's place in the village: where it stands, which way it looks, what it is and what it says.</summary>
        private struct Stand
        {
            public string Name;
            public string Title;
            public Vector3 Local;
            public float Yaw;
            public NpcDialog Dialog;
            public NpcEntity Body;
            /// <summary>Village-local points to walk between, for a guard on his rounds.</summary>
            public Vector3[] Route;
        }

        private static void PlaceInScene(NpcDialog banker, NpcDialog merchant, NpcDialog innkeeper, NpcDialog elder,
                                         NpcDialog towerGuard, NpcDialog patrolGuard)
        {
            // One body per trade rather than four copies of the same villager: the keeper
            // of the strongbox in a warden's tabard, the elder in noble dress, the pedlar
            // as an ordinary villager and the innkeeper as the other one. The guards share
            // a body, but the one on his rounds is the prefab that can walk.
            NpcEntity keeperEntity = NpcBody("DemoKeeper");
            NpcEntity villagerEntity = NpcBody("DemoVillager");
            NpcEntity innkeeperEntity = NpcBody("DemoInnkeeper");
            NpcEntity elderEntity = NpcBody("DemoElder");
            NpcEntity guardEntity = NpcBody("DemoGuard");
            NpcEntity patrolEntity = NpcBody("DemoPatrolGuard");
            if (keeperEntity == null || villagerEntity == null || innkeeperEntity == null || elderEntity == null ||
                guardEntity == null || patrolEntity == null)
            {
                Debug.LogError($"[{nameof(DemoNpcBuilder)}] Missing an NPC entity prefab.");
                return;
            }

            Vector2 village = DemoIslandBuilder.VillageCentre;
            var origin = new Vector3(village.x, DemoIslandBuilder.VillageHeight, village.y);

            // A house's door is on its own local -Z, so HouseYaw turns a building's door
            // toward the green — and a person given the same yaw looks the same way. The
            // banker gets his for free by standing in the bank: facing the green from in
            // there means facing his own doorway.
            //
            // The half turn is the part that is not guessable. These bodies are modelled
            // facing -Z, so it reads as though the yaw needs no correction, and in the
            // editor it measurably does not: sample a clip onto one with
            // AnimationClip.SampleAnimation and it still faces -Z. Under the kit's own
            // playable graph it faces +Z — measured at runtime from the hands, left at -X
            // and right at +X — so every NPC placed on the untouched yaw stood with its
            // back to whatever it was meant to be looking at. The bind pose is not evidence
            // about the runtime here, and neither is an editor-sampled one.
            // The townsfolk face the green. The tower guard faces the other way, out over
            // the parapet, which is the tower's own yaw with no half turn: HouseYaw points
            // a building's -Z at the green, and a kit-driven character looks along +Z.
            var placements = new[]
            {
                new Stand { Name = "Fenwick", Title = "Fenwick the Keeper", Local = BankerLocalPosition, Yaw = Facing(BankerLocalPosition), Dialog = banker, Body = keeperEntity },
                new Stand { Name = "Marek", Title = "Marek the Pedlar", Local = MerchantLocalPosition, Yaw = MerchantYaw, Dialog = merchant, Body = villagerEntity },
                new Stand { Name = "Hilde", Title = "Hilde the Innkeeper", Local = InnkeeperLocalPosition, Yaw = Facing(InnkeeperLocalPosition), Dialog = innkeeper, Body = innkeeperEntity },
                new Stand { Name = "Rowan", Title = "Elder Rowan", Local = ElderLocalPosition, Yaw = Facing(ElderLocalPosition), Dialog = elder, Body = elderEntity },
                new Stand { Name = "TowerGuard", Title = "Watchtower Guard", Local = TowerGuardLocalPosition, Yaw = DemoSceneBuilder.HouseYaw(DemoSceneBuilder.WatchtowerLayout), Dialog = towerGuard, Body = guardEntity },
                new Stand { Name = "PatrolGuard", Title = "Town Guard", Local = PatrolLocalRoute[0], Yaw = Facing(PatrolLocalRoute[0]), Dialog = patrolGuard, Body = patrolEntity, Route = PatrolLocalRoute },
            };

            // The map may already be the open scene; if not it is opened beside whatever
            // is, and closed again after.
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
                if (candidate.name == DemoSceneBuilder.NpcRootName)
                    root = candidate;
            }
            if (root == null)
            {
                root = new GameObject(DemoSceneBuilder.NpcRootName);
                SceneManager.MoveGameObjectToScene(root, scene);
            }

            int placed = 0;
            foreach (Stand placement in placements)
            {
                Transform existing = root.transform.Find(placement.Name);
                GameObject npc;
                if (existing != null)
                {
                    // Already stood where somebody put them. Only what they say is refreshed.
                    npc = existing.gameObject;
                }
                else
                {
                    npc = (GameObject)PrefabUtility.InstantiatePrefab(placement.Body.gameObject, scene);
                    npc.name = placement.Name;
                    npc.transform.SetParent(root.transform, true);
                    npc.transform.position = origin + placement.Local;
                    npc.transform.rotation = Quaternion.Euler(0f, placement.Yaw, 0f);
                    if (placement.Route != null)
                    {
                        var patrol = npc.GetComponent<MultiplayerARPG.Demo.DemoPatrol>();
                        if (patrol == null)
                            Debug.LogError($"[{nameof(DemoNpcBuilder)}] {placement.Name} has a route but no DemoPatrol component.");
                        else
                            patrol.waypoints = System.Array.ConvertAll(placement.Route, point => OnTheNavMesh(origin + point));
                    }
                    ++placed;
                }

                var entity = npc.GetComponent<NpcEntity>();
                var serialized = new SerializedObject(entity);
                serialized.FindProperty("entityTitle").stringValue = placement.Title;
                serialized.FindProperty("startDialog").objectReferenceValue = placement.Dialog;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Pose(npc);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            if (!wasOpen)
                EditorSceneManager.CloseScene(scene, true);
            Debug.Log($"[{nameof(DemoNpcBuilder)}] {placed} NPC(s) placed, {placements.Length - placed} kept where they stood.");
        }

        /// <summary>A townsman's yaw: looking at the green from where he stands.</summary>
        private static float Facing(Vector3 local)
        {
            return DemoSceneBuilder.HouseYaw(local) + FacingCorrection;
        }

        /// <summary>
        /// The nearest walkable point to one, so a route runs on the navmesh the guard
        /// walks rather than through the stall beside it. Left as given, with a warning,
        /// if there is no navmesh within reach - which means the scene has not been
        /// built yet.
        /// </summary>
        private static Vector3 OnTheNavMesh(Vector3 point)
        {
            if (NavMesh.SamplePosition(point, out NavMeshHit hit, 3f, NavMesh.AllAreas))
                return hit.position;
            Debug.LogWarning($"[{nameof(DemoNpcBuilder)}] No navmesh within 3m of {point:F1}; has the scene been built?");
            return point;
        }

        /// <summary>
        /// Poses an NPC the way the game will show it.
        ///
        /// The clip goes through a PlayableGraph rather than <c>SampleAnimation</c>
        /// because only the animation runtime applies the import setting that turns the
        /// library's clips round to +Z; sampled directly, every clip shows the body
        /// backwards. The bones it writes are ordinary transform values on the instance,
        /// and the Animator overwrites them the moment the game starts.
        /// </summary>
        private static void Pose(GameObject npc)
        {
            Animator animator = npc.GetComponentInChildren<Animator>(true);
            AnimationClip idle = DemoAnimationSet.State("Idle_Loop").clip;
            if (animator == null || idle == null)
                return;
            PlayableGraph graph = PlayableGraph.Create("NpcPose");
            AnimationPlayableOutput output = AnimationPlayableOutput.Create(graph, "pose", animator);
            output.SetSourcePlayable(AnimationClipPlayable.Create(graph, idle));
            graph.Evaluate(0.01f);
            graph.Destroy();
        }

        /// <summary>
        /// The NPCs used to be spawned from here per map. Now that they stand in the
        /// scene, an entry left behind would put a second copy of each on top of the first.
        /// </summary>
        private static void ClearNpcDatabase()
        {
            var database = AssetDatabase.LoadAssetAtPath<NpcDatabase>($"{GameDataDir}/NpcDatabase.asset");
            if (database == null)
                return;
            var serialized = new SerializedObject(database);
            serialized.FindProperty("maps").arraySize = 0;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(database);
        }

        private static NpcEntity NpcBody(string prefabName)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{EntityDir}/{prefabName}.prefab");
            if (prefab == null)
            {
                Debug.LogError($"[{nameof(DemoNpcBuilder)}] Missing \"{prefabName}\". Run Build Character Entities first.");
                return null;
            }
            return prefab.GetComponent<NpcEntity>();
        }

        /// <summary>
        /// Storage has no size by default, and the kit's conversation range is a metre,
        /// which is close enough that a player walks past an NPC without the prompt
        /// appearing.
        /// </summary>
        private static void ConfigureGameInstance()
        {
            const string scenePath = "Assets/OpenMMORPG/Demo/Scenes/00Init.unity";
            UnityEngine.SceneManagement.Scene scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                scenePath, UnityEditor.SceneManagement.OpenSceneMode.Additive);

            GameInstance instance = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                GameInstance found = root.GetComponentInChildren<GameInstance>(true);
                if (found != null)
                    instance = found;
            }
            if (instance == null)
            {
                Debug.LogError($"[{nameof(DemoNpcBuilder)}] No GameInstance in {scenePath}.");
                UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
                return;
            }

            var serialized = new SerializedObject(instance);
            serialized.FindProperty("playerStorage.slotLimit").intValue = 40;
            serialized.FindProperty("playerStorage.weightLimit").intValue = 0;
            serialized.FindProperty("conversationDistance").floatValue = 3.5f;
            serialized.FindProperty("equipmentModelBonesSetupManager").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<BaseEquipmentModelBonesSetupManager>(DemoItemBuilder.BonesSetupPath);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(instance);

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
            UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
        }

        private static BaseItem LoadItem(string name)
        {
            var item = AssetDatabase.LoadAssetAtPath<BaseItem>($"{ItemDir}/{name}.asset");
            if (item == null)
                Debug.LogError($"[{nameof(DemoNpcBuilder)}] Missing item \"{name}\". Run Build Items first.");
            return item;
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

        /// <summary>Dialog assets, for registering in the game database.</summary>
        public static List<Object> AllDialogs()
        {
            var found = new List<Object>();
            foreach (string guid in AssetDatabase.FindAssets("t:BaseNpcDialog", new[] { DialogDir }))
                found.Add(AssetDatabase.LoadAssetAtPath<Object>(AssetDatabase.GUIDToAssetPath(guid)));
            return found;
        }

        public static List<Object> AllQuests()
        {
            var found = new List<Object>();
            foreach (string guid in AssetDatabase.FindAssets("t:Quest", new[] { QuestDir }))
                found.Add(AssetDatabase.LoadAssetAtPath<Object>(AssetDatabase.GUIDToAssetPath(guid)));
            return found;
        }
    }
}
