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
        /// <summary>
        /// The smith stands at the back of his forge, beside the anvil rather than behind
        /// it. Given in the smithy's own space like the innkeeper in the alehouse's, and
        /// measured off the furniture the interior builder placed: the anvil is at
        /// house-local (-0.57, 1.82), the bellows at (2.14, 1.91), the back wall at about
        /// 2.9, and he stands between them.
        ///
        /// **Beside, because the anvil is also the Forge crafting station.** He began
        /// directly behind it, which framed nicely and broke the activate key: the kit
        /// activates the *first* entity in `ActivatableEntityDetector.activatableEntities`
        /// that can be activated and stops there, so with the two of them in a line from
        /// the doorway the anvil answered every time and there was no standing position
        /// that reached the smith. Off to one side each is nearest from somewhere.
        /// Clicking either always worked; it is only the key that had no way through.
        ///
        /// House_2 is the smithy - a furnished one, with a bellows, a whetstone, a hammer
        /// rack and a weapon stand - and it stood empty from the day it was built until
        /// 2026-09-22.
        /// </summary>
        public static Vector3 SmithLocalPosition
        {
            get
            {
                Vector3 house = DemoSceneBuilder.HouseLayout[DemoSceneBuilder.SmithHouseIndex];
                Quaternion yaw = Quaternion.Euler(0f, DemoSceneBuilder.HouseYaw(house), 0f);
                return house + yaw * new Vector3(0.55f, 0f, 2.35f);
            }
        }

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

            // Every quest asset first, in two passes: one of them gates on another having been
            // completed, and that gate is an asset reference, so they all have to exist before
            // any of them is written. Two passes rather than relying on the table's order, which
            // a later edit would quietly break.
            var quests = new Dictionary<string, Quest>();
            foreach (QuestSpec spec in Quests)
                quests[spec.Name] = BuildQuestAsset(spec);
            foreach (QuestSpec spec in Quests)
            {
                if (!string.IsNullOrEmpty(spec.RequireQuest))
                    BuildQuestAsset(spec);
            }

            Quest banditQuest = quests["ThinTheCamp"];
            NpcDialog venisonQuest = BuildQuestDialog(SpecOf("MeatForThePot"), quests["MeatForThePot"]);

            NpcDialog banker = BuildBankerDialogs();
            NpcDialog smith = BuildSmithDialogs();
            NpcDialog merchant = BuildMerchantDialog();
            NpcDialog innkeeper = BuildInnkeeperDialog(banditQuest, venisonQuest);
            NpcDialog elder = BuildQuestDialog(SpecOf("ThinTheCamp"), banditQuest);
            // The two guards now open with a quest of their own rather than a passing remark;
            // each offer line carries the flavour the old one-liner did.
            NpcDialog towerGuard = BuildTowerGuardDialog(
                BuildQuestDialog(SpecOf("WhatTheHillsHide"), quests["WhatTheHillsHide"]),
                quests["WhatTheHillsHide"]);
            NpcDialog patrolGuard = BuildQuestDialog(SpecOf("WolvesAtTheFences"), quests["WolvesAtTheFences"]);

            PlaceInScene(banker, smith, merchant, innkeeper, elder, towerGuard, patrolGuard);
            ClearNpcDatabase();
            ConfigureGameInstance();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[{nameof(DemoNpcBuilder)}] Built demo NPCs, dialogs and quest.");
        }

        // ---- quests ----------------------------------------------------------

        /// <summary>
        /// One quest, and the four things an NPC says about it.
        ///
        /// The kit cannot change an NPC's opening line by quest state, only which menu choices
        /// it offers, so a quest dialog *is* the NPC's line while that quest is live. That is
        /// why each of these carries its own voice rather than sharing a template.
        /// </summary>
        private struct QuestSpec
        {
            public string Name, Title, Description;
            /// <summary>
            /// Prefix for the offer and its three reply assets, giving `<Prefix>Quest`,
            /// `<Prefix>QuestAccepted` and so on. Named per quest rather than per NPC, because
            /// one NPC could hand out two.
            /// </summary>
            public string DialogPrefix;
            public string Speaker, Offer, Accepted, Declined, Completed;
            public int Exp, Gold;
            /// <summary>Hides the quest until the character is ready for it. Zero means no gate.</summary>
            public int RequireLevel;
            /// <summary>Quest that must be finished first, by asset name. Null means no gate.</summary>
            public string RequireQuest;
            public QuestTaskType TaskType;
            /// <summary>A MonsterCharacter asset name for a kill, an item name for a fetch.</summary>
            public string TaskTarget;
            public int TaskAmount;
            public string[] RewardItems;
            public int[] RewardAmounts;
            /// <summary>
            /// Whether it can be taken again once finished. `None` is the kit's default and the
            /// right answer for a story beat - you only clear the camp once.
            /// </summary>
            public QuestRepeatType Repeat;
        }

        /// <summary>
        /// The island's quests, and who hands each one out.
        ///
        /// Spread across four NPCs on purpose (three added 2026-09-16, when the island had grown
        /// wolves, cultists, marauders and a crypt and still had exactly one quest on one elder).
        /// They also ladder: the guard's wolves are a level-one errand a character can do the
        /// moment it leaves the green, Rowan's bandits are the step up, Hilde's venison is the
        /// hunting alternative for anyone who would rather not fight a camp at all, and the tower
        /// guard's crypt is the capstone - gated behind Rowan's, because it asks for the thing at
        /// the bottom of a dungeon.
        ///
        /// Rewards are scaled from Rowan's existing 400/250 for eight bandits: the wolf errand is
        /// worth a fraction of it, the hunt a little more for the walking, and the boss a good
        /// deal more. The boss pays in coin rather than gear, because coin suits all three
        /// classes - and the Hierophant already drops the mage's set himself.
        /// </summary>
        private static readonly QuestSpec[] Quests =
        {
            new QuestSpec
            {
                Name = "WolvesAtTheFences", Title = "Wolves at the Fences",
                Description = "Wolves have come down off the moor and are working the fence line after dark. Thin them out before they learn the way in.",
                DialogPrefix = "Wolves", Speaker = "Town Guard",
                Offer = "You are armed, and I am one man walking a circle. There are wolves on the fence line - four or five, and bold with it. Deal with them and I will see you right out of my own purse.",
                Accepted = "Good. They keep to the open ground around the green, so you will not have far to go. Mind the ones that circle behind you.",
                Declined = "Then walk inside the fence after dark, and do not say I failed to mention it.",
                Completed = "That is the last of them off the line. Here - it is my money, not the village's, so spend it on something useful.",
                Exp = 90, Gold = 40,
                TaskType = QuestTaskType.KillMonster, TaskTarget = "Wolf", TaskAmount = 5,
                RewardItems = new[] { "MinorHealingPotion" }, RewardAmounts = new[] { 3 },
            },
            new QuestSpec
            {
                Name = "ThinTheCamp", Title = "Thin the Camp",
                Description = "Bandits have taken the headland southeast of here. Cut their numbers down and bring me proof.",
                DialogPrefix = "Elder", Speaker = "Elder Rowan",
                Offer = "You carry a blade, and we have a problem that wants one.",
                Accepted = "Good. Keep to the ridge and they will not see you coming.",
                Declined = "Then we wait, and we bar the doors at dusk.",
                Completed = "You have done what we could not. Take this - it hung in my father's hall.",
                Exp = 400, Gold = 250,
                TaskType = QuestTaskType.KillMonster, TaskTarget = "BaseEnemy", TaskAmount = 8,
                RewardItems = new[] { "PaintedRoundShield" }, RewardAmounts = new[] { 1 },
            },
            new QuestSpec
            {
                Name = "MeatForThePot", Title = "Meat for the Pot",
                Description = "Hilde's kitchen is out of venison and the deer keep inland, past the rocks. Bring her four cuts.",
                DialogPrefix = "Venison", Speaker = "Hilde the Innkeeper",
                Offer = "Before you go anywhere else - my pot is empty and the deer keep inland past the rocks. Four cuts of venison and I will pay you properly, in coin and in supper.",
                Accepted = "Four cuts. They bolt the moment they catch sight of you, so shoot straight or walk quietly.",
                Declined = "Then it is turnips again, and everyone will know whose fault that is.",
                Completed = "Look at that. That is a week of proper suppers. Take your coin, and take a bowl with you - you have earned both.",
                Exp = 150, Gold = 120,
                TaskType = QuestTaskType.CollectItem, TaskTarget = "Venison", TaskAmount = 4,
                RewardItems = new[] { "Stew", "Bread" }, RewardAmounts = new[] { 2, 2 },
                // The one standing arrangement on the island: a kitchen always wants more meat,
                // and it gives hunting a permanent buyer rather than a single errand. `AnyTime`
                // rather than `Daily` because a demo has to be showable twice in one sitting -
                // the daily gate is a real calendar day, not a cooldown.
                Repeat = QuestRepeatType.AnyTime,
            },
            new QuestSpec
            {
                Name = "WhatTheHillsHide", Title = "What the Hills Hide",
                Description = "Robed figures have been going down into the crypt under the hills, and the one that leads them has not come up. End it at the source.",
                DialogPrefix = "Crypt", Speaker = "Watchtower Guard",
                Offer = "From up here you see what nobody on the green does. Robed ones, going down into the hills southwest and not coming back up. There is a door under there, and something wearing a crown behind it. I cannot leave this post. You can.",
                Accepted = "Take a light and take your time. Whatever is down there has had a long while to get comfortable.",
                Declined = "No shame in it. I would not go either, and I watch it every night.",
                Completed = "Then it is finished, and I can watch those hills without my neck prickling. This is the watch's purse - all of it. Nobody will miss it.",
                Exp = 900, Gold = 700,
                RequireLevel = 6, RequireQuest = "ThinTheCamp",
                TaskType = QuestTaskType.KillMonster, TaskTarget = "Hierophant", TaskAmount = 1,
                RewardItems = new[] { "MinorHealingPotion" }, RewardAmounts = new[] { 5 },
            },
        };

        /// <summary>Builds a quest asset from its spec. One task each; the demo needs no more.</summary>
        private static Quest BuildQuestAsset(QuestSpec spec)
        {
            var quest = Create<Quest>($"{QuestDir}/{spec.Name}.asset");
            var serialized = new SerializedObject(quest);
            serialized.FindProperty("id").stringValue = spec.Name;
            serialized.FindProperty("defaultTitle").stringValue = spec.Title;
            serialized.FindProperty("defaultDescription").stringValue = spec.Description;
            serialized.FindProperty("rewardExp").intValue = spec.Exp;
            serialized.FindProperty("rewardGold").intValue = spec.Gold;
            serialized.FindProperty("canAbandon").boolValue = true;
            serialized.FindProperty("autoTrackQuest").boolValue = true;
            serialized.FindProperty("repeatType").enumValueIndex = (int)spec.Repeat;

            SerializedProperty requireLevel = serialized.FindProperty("requirement.level");
            if (requireLevel != null)
                requireLevel.intValue = spec.RequireLevel;
            SerializedProperty requireQuests = serialized.FindProperty("requirement.completedQuests");
            if (requireQuests != null)
            {
                bool gated = !string.IsNullOrEmpty(spec.RequireQuest);
                requireQuests.arraySize = gated ? 1 : 0;
                if (gated)
                    requireQuests.GetArrayElementAtIndex(0).objectReferenceValue =
                        AssetDatabase.LoadAssetAtPath<Quest>($"{QuestDir}/{spec.RequireQuest}.asset");
            }

            // Tasks live under randomTasks; the kit migrates its old flat list into the
            // first entry, so a quest with one fixed set of objectives uses index 0.
            SerializedProperty randomTasks = serialized.FindProperty("randomTasks");
            randomTasks.arraySize = 1;
            SerializedProperty tasks = randomTasks.GetArrayElementAtIndex(0).FindPropertyRelative("tasks");
            tasks.arraySize = 1;
            SerializedProperty task = tasks.GetArrayElementAtIndex(0);
            task.FindPropertyRelative("taskType").enumValueIndex = (int)spec.TaskType;
            if (spec.TaskType == QuestTaskType.KillMonster)
            {
                var monster = AssetDatabase.LoadAssetAtPath<MonsterCharacter>(
                    $"{ResourcesDir}/MonsterCharacters/{spec.TaskTarget}.asset");
                if (monster == null)
                    Debug.LogError($"[{nameof(DemoNpcBuilder)}] Quest \"{spec.Name}\" wants monster \"{spec.TaskTarget}\", which is not there.");
                task.FindPropertyRelative("monsterCharacterAmount.monster").objectReferenceValue = monster;
                task.FindPropertyRelative("monsterCharacterAmount.amount").intValue = spec.TaskAmount;
            }
            else
            {
                task.FindPropertyRelative("itemAmount.item").objectReferenceValue = LoadItem(spec.TaskTarget);
                task.FindPropertyRelative("itemAmount.amount").intValue = spec.TaskAmount;
            }

            SerializedProperty rewards = serialized.FindProperty("rewardItems");
            rewards.arraySize = spec.RewardItems == null ? 0 : spec.RewardItems.Length;
            for (int i = 0; i < rewards.arraySize; ++i)
            {
                SerializedProperty reward = rewards.GetArrayElementAtIndex(i);
                reward.FindPropertyRelative("item").objectReferenceValue = LoadItem(spec.RewardItems[i]);
                reward.FindPropertyRelative("amount").intValue = spec.RewardAmounts[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(quest);
            return quest;
        }

        /// <summary>The offer dialog, and the three replies hanging off it.</summary>
        private static NpcDialog BuildQuestDialog(QuestSpec spec, Quest quest)
        {
            NpcDialog accepted = SimpleDialog($"{spec.DialogPrefix}QuestAccepted", spec.Speaker, spec.Accepted);
            NpcDialog declined = SimpleDialog($"{spec.DialogPrefix}QuestDeclined", spec.Speaker, spec.Declined);
            NpcDialog completed = SimpleDialog($"{spec.DialogPrefix}QuestCompleted", spec.Speaker, spec.Completed);

            var offer = Create<NpcDialog>($"{DialogDir}/{spec.DialogPrefix}Quest.asset");
            var serialized = new SerializedObject(offer);
            serialized.FindProperty("title").stringValue = spec.Speaker;
            serialized.FindProperty("description").stringValue = spec.Offer;
            serialized.FindProperty("type").enumValueIndex = (int)NpcDialogType.Quest;
            serialized.FindProperty("quest").objectReferenceValue = quest;
            serialized.FindProperty("questAcceptedDialog").objectReferenceValue = accepted;
            serialized.FindProperty("questDeclinedDialog").objectReferenceValue = declined;
            serialized.FindProperty("questCompletedDialog").objectReferenceValue = completed;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(offer);
            return offer;
        }

        private static QuestSpec SpecOf(string name)
        {
            foreach (QuestSpec spec in Quests)
            {
                if (spec.Name == name)
                    return spec;
            }
            Debug.LogError($"[{nameof(DemoNpcBuilder)}] No quest spec named \"{name}\".");
            return default;
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

            // The guild's chest, beside the player's own. `GuildStorage` is a dialog type
            // like `PlayerStorage` and needs no more setting up than one; the kit refuses
            // it for a character with no guild, so it can sit on the menu unconditionally
            // and explain itself when it is not available. Its size comes from
            // `GameInstance.guildStorage`, set in ConfigureGameInstance.
            var guildStorage = Create<NpcDialog>($"{DialogDir}/GuildStorage.asset");
            var guildSerialized = new SerializedObject(guildStorage);
            guildSerialized.FindProperty("title").stringValue = "Guild Chest";
            guildSerialized.FindProperty("description").stringValue =
                "Your company's, not yours. Everyone with the badge can reach into it, so mind what you leave.";
            guildSerialized.FindProperty("type").enumValueIndex = (int)NpcDialogType.GuildStorage;
            guildSerialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(guildStorage);

            var greeting = Create<NpcDialog>($"{DialogDir}/BankerGreeting.asset");
            var serialized = new SerializedObject(greeting);
            serialized.FindProperty("title").stringValue = "Fenwick the Keeper";
            serialized.FindProperty("description").stringValue =
                "Everything the village cannot carry, it leaves with me. Shall I bring out your strongbox?";
            serialized.FindProperty("type").enumValueIndex = (int)NpcDialogType.Normal;
            SetMenus(serialized, new[]
            {
                new MenuSpec { Title = "Open my strongbox", Dialog = storage },
                new MenuSpec { Title = "My company's chest", Dialog = guildStorage },
                new MenuSpec { Title = "Not today", Dialog = farewell },
            });
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(greeting);
            return greeting;
        }

        /// <summary>
        /// The smith. One greeting and three services, which between them are the three
        /// things the kit can do to a piece of equipment and the demo had never shown:
        /// mend it, improve it, or break it up.
        ///
        /// Each is a dialog of its own type and carries no data at all - the kit renders
        /// the whole window for `RepairItem`, `RefineItem` and `DismantleItem` off the
        /// type alone. What they act on is the item the player drags in, and what they
        /// charge comes from the `ItemRefine` asset on that item (see DemoItemBuilder), so
        /// a smith with no prices of his own can still turn a job down.
        /// </summary>
        private static NpcDialog BuildSmithDialogs()
        {
            var repair = Create<NpcDialog>($"{DialogDir}/SmithRepair.asset");
            var repairSerialized = new SerializedObject(repair);
            repairSerialized.FindProperty("title").stringValue = "Mending";
            repairSerialized.FindProperty("description").stringValue =
                "Put it on the bench. I'll see what's left of it.";
            repairSerialized.FindProperty("type").enumValueIndex = (int)NpcDialogType.RepairItem;
            repairSerialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(repair);

            var refine = Create<NpcDialog>($"{DialogDir}/SmithRefine.asset");
            var refineSerialized = new SerializedObject(refine);
            refineSerialized.FindProperty("title").stringValue = "The Wheel";
            refineSerialized.FindProperty("description").stringValue =
                "Stone and patience. It might come off the wheel better than it went on - or it might not.";
            refineSerialized.FindProperty("type").enumValueIndex = (int)NpcDialogType.RefineItem;
            refineSerialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(refine);

            var dismantle = Create<NpcDialog>($"{DialogDir}/SmithDismantle.asset");
            var dismantleSerialized = new SerializedObject(dismantle);
            dismantleSerialized.FindProperty("title").stringValue = "The Scrap Pile";
            dismantleSerialized.FindProperty("description").stringValue =
                "I'll take it apart and you keep what it was made of. Don't expect the whole of it back.";
            dismantleSerialized.FindProperty("type").enumValueIndex = (int)NpcDialogType.DismantleItem;
            dismantleSerialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(dismantle);

            // A smith who will make the thing for you, as well as mend it. `CraftItem` is
            // the kit's NPC-side crafting - the same `ItemCraftFormula` assets the village's
            // workbenches carry, offered across a counter instead of at a station - and it
            // was the last dialog type the demo had no use for. He takes the forge's list,
            // so a player who has not found House_2 yet can still have a sword made.
            var craft = Create<NpcDialog>($"{DialogDir}/SmithCraft.asset");
            var craftSerialized = new SerializedObject(craft);
            craftSerialized.FindProperty("title").stringValue = "Commission";
            craftSerialized.FindProperty("description").stringValue =
                "Bring me the makings and I'll do the work. It is the same steel either way.";
            craftSerialized.FindProperty("type").enumValueIndex = (int)NpcDialogType.CraftItem;
            SetCraftFormula(craftSerialized, "CraftLongsword");
            craftSerialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(craft);

            var farewell = Create<NpcDialog>($"{DialogDir}/SmithFarewell.asset");
            var farewellSerialized = new SerializedObject(farewell);
            farewellSerialized.FindProperty("title").stringValue = "Bram the Smith";
            farewellSerialized.FindProperty("description").stringValue =
                "Mind the anvil on your way past. It doesn't move.";
            farewellSerialized.FindProperty("type").enumValueIndex = (int)NpcDialogType.Normal;
            SetMenus(farewellSerialized, new[] { new MenuSpec { Title = "Goodbye", Close = true } });
            farewellSerialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(farewell);

            var greeting = Create<NpcDialog>($"{DialogDir}/SmithGreeting.asset");
            var serialized = new SerializedObject(greeting);
            serialized.FindProperty("title").stringValue = "Bram the Smith";
            serialized.FindProperty("description").stringValue =
                "Everything on this island is trying to blunt something of yours. Bring it here before it gives out.";
            serialized.FindProperty("type").enumValueIndex = (int)NpcDialogType.Normal;
            SetMenus(serialized, new[]
            {
                new MenuSpec { Title = "This needs mending", Dialog = repair },
                new MenuSpec { Title = "Can you better it?", Dialog = refine },
                new MenuSpec { Title = "Break this down for me", Dialog = dismantle },
                new MenuSpec { Title = "Make me a longsword", Dialog = craft },
                new MenuSpec { Title = "Just passing", Dialog = farewell },
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
            // The campfire kit is sold as well as craftable, because a building item is the
            // one thing in the demo a player has no way of guessing exists: it does nothing
            // from the inventory, it has to be taken out and placed. On the pedlar's board
            // it is at least something they will read the tooltip of.
            // Arrows are on the board at a gold apiece because a bow without them does not
            // fire - see DemoSuppliesBuilder. The scroll and the gems are here because the
            // island has nowhere else to find them yet.
            // The sundries are on his board for the same reason: a charm, a skill scroll, a
            // passage stone, a cache and a tome, in that order, which is also cheapest to
            // dearest. See DemoSundriesBuilder.SundryNames, which is where that list lives
            // so the two cannot drift.
            var stock = new List<string>
            {
                "MinorHealingPotion", "MinorManaPotion", "IronShortsword", "PeasantTunic",
                "PeasantTrousers", "PeasantShoes", "PeasantSleeves", "HorseWhistle",
                "CampfireKit", "Arrow", "ScrollOfReturn",
                "GemGarnet", "GemSapphire", "GemCitrine",
                // Three hundred gold, which is more than a character has for a while. The
                // pet is the one thing on his board worth saving for.
                "PupCollar",
            };
            stock.AddRange(DemoSundriesBuilder.SundryNames());
            SetSellItems(shopSerialized, stock.ToArray());
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
        private static NpcDialog BuildInnkeeperDialog(Quest quest, NpcDialog venisonQuest)
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
                // Her own errand. No quest condition on it: the kit hides a quest menu whose
                // quest is finished by itself, and leaving it unconditioned means she offers it
                // whatever Rowan's bandits are doing.
                new MenuSpec { Title = "Is the pot empty again?", Dialog = venisonQuest },
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

        /// <summary>
        /// The watchtower guard, who now does two things: gives out the crypt quest, and
        /// once you have taken it, sets you down on the crypt's doorstep for a few coins.
        ///
        /// This is the demo's only `NpcDialogType.Warp`, the last of the kit's eleven
        /// dialog types nothing used, and it is what an MMORPG usually calls a flight
        /// master. He is the right person for it because he is the one who can see the
        /// hills from where he stands; a new NPC would have needed a body, a place to
        /// stand and a reason to be there, and this needed none of the three.
        ///
        /// Three things about a Warp dialog that are not obvious:
        ///
        /// **`warpMap` may not be null, even to warp inside the same map.**
        /// `ValidateDialog` refuses a Warp dialog with an empty map and logs a warning,
        /// and a refused dialog is simply never shown - the menu entry disappears with no
        /// error a player or a builder would see. The island's own map info goes in it and
        /// the kit treats the warp as a teleport rather than a handover.
        ///
        /// **The quest dialog hangs off a menu.** A Quest dialog has no menus of its own -
        /// its buttons are accept, decline and complete - so an NPC who does anything
        /// besides a quest needs a Normal dialog on top with the quest behind an entry.
        /// Hilde has worked this way since she was built; the guard did not.
        ///
        /// **The warp is gated on the quest being under way**, by the same `showConditions`
        /// that hide Hilde's lines. A level-1 character put down at a dungeon door is not
        /// a shortcut, it is a death; and the offer reads as an answer to the errand he
        /// has just given rather than a service he advertises to strangers.
        /// </summary>
        private static NpcDialog BuildTowerGuardDialog(NpcDialog questDialog, Quest quest)
        {
            var island = AssetDatabase.LoadAssetAtPath<BaseMapInfo>($"{ResourcesDir}/MapInfos/BaseMap.asset");
            if (island == null)
                Debug.LogError($"[{nameof(DemoNpcBuilder)}] No BaseMap.asset; the guard's warp will not be shown.");

            var warp = Create<NpcDialog>($"{DialogDir}/TowerGuardWarp.asset");
            var warpSerialized = new SerializedObject(warp);
            warpSerialized.FindProperty("title").stringValue = "The hill road";
            warpSerialized.FindProperty("description").stringValue =
                "There is a drover's track off the back of the ridge that comes out at the crypt door. " +
                "I will whistle down to the lad with the cart and he will have you there before dark. " +
                "He does not do it for nothing.";
            warpSerialized.FindProperty("type").enumValueIndex = (int)NpcDialogType.Warp;
            warpSerialized.FindProperty("warpMap").objectReferenceValue = island;
            warpSerialized.FindProperty("warpPosition").vector3Value = DemoSundriesBuilder.GuidedWarpPosition;
            warpSerialized.FindProperty("warpOverrideRotation").boolValue = true;
            warpSerialized.FindProperty("warpRotation").vector3Value =
                new Vector3(0f, DemoSundriesBuilder.GuidedWarpYaw, 0f);
            // A toll, which is also the demo's only use of `confirmRequirement`. Small
            // enough that a player who has reached this quest can always pay it.
            warpSerialized.FindProperty("confirmRequirement.gold").intValue = DemoSundriesBuilder.GuidedWarpToll;
            warpSerialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(warp);

            var greeting = Create<NpcDialog>($"{DialogDir}/TowerGuardGreeting.asset");
            var serialized = new SerializedObject(greeting);
            serialized.FindProperty("title").stringValue = "Watchtower Guard";
            serialized.FindProperty("description").stringValue =
                "Mind the ladder. Best view on the island up here, and the worst thing to look at.";
            serialized.FindProperty("type").enumValueIndex = (int)NpcDialogType.Normal;
            SetMenus(serialized, new[]
            {
                new MenuSpec { Title = "What are you watching for?", Dialog = questDialog },
                new MenuSpec { Title = "Can you get me to that door?", Dialog = warp,
                    IfQuest = quest, QuestState = NpcDialogConditionType.QuestOngoing },
                new MenuSpec { Title = "I will leave you to it", Close = true },
            });
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(greeting);
            return greeting;
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

        /// <summary>
        /// Copies a crafting recipe onto a `CraftItem` dialog.
        ///
        /// **One dialog is one item.** `NpcDialog.itemCraft` is an `ItemCraft` in its own
        /// right, not a reference to an `ItemCraftFormula`, so an NPC who makes three
        /// things needs three dialogs behind three menu entries. The recipe is still read
        /// out of the formula asset DemoProgressionBuilder writes, so the smith charges
        /// what the forge charges and a change to one reaches both.
        /// </summary>
        private static void SetCraftFormula(SerializedObject serialized, string formulaName)
        {
            var formula = AssetDatabase.LoadAssetAtPath<ItemCraftFormula>(
                $"{ResourcesDir}/ItemCraftFormulas/{formulaName}.asset");
            if (formula == null)
            {
                Debug.LogError($"[{nameof(DemoNpcBuilder)}] No craft formula \"{formulaName}\". " +
                               "Run Build Progression first.");
                return;
            }
            SerializedProperty source = new SerializedObject(formula).FindProperty("itemCraft");
            SerializedProperty target = serialized.FindProperty("itemCraft");
            if (source == null || target == null)
                return;
            target.FindPropertyRelative("craftingItem").objectReferenceValue =
                source.FindPropertyRelative("craftingItem").objectReferenceValue;
            target.FindPropertyRelative("amount").intValue = source.FindPropertyRelative("amount").intValue;
            target.FindPropertyRelative("requireGold").intValue = source.FindPropertyRelative("requireGold").intValue;

            SerializedProperty from = source.FindPropertyRelative("requireItems");
            SerializedProperty to = target.FindPropertyRelative("requireItems");
            to.arraySize = from.arraySize;
            for (int i = 0; i < from.arraySize; ++i)
            {
                SerializedProperty a = from.GetArrayElementAtIndex(i);
                SerializedProperty b = to.GetArrayElementAtIndex(i);
                b.FindPropertyRelative("item").objectReferenceValue = a.FindPropertyRelative("item").objectReferenceValue;
                b.FindPropertyRelative("amount").intValue = a.FindPropertyRelative("amount").intValue;
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

        private static void PlaceInScene(NpcDialog banker, NpcDialog smith, NpcDialog merchant, NpcDialog innkeeper,
                                         NpcDialog elder, NpcDialog towerGuard, NpcDialog patrolGuard)
        {
            // One body per trade rather than four copies of the same villager: the keeper
            // of the strongbox in a warden's tabard, the elder in noble dress, the pedlar
            // as an ordinary villager and the innkeeper as the other one. The guards share
            // a body, but the one on his rounds is the prefab that can walk.
            NpcEntity keeperEntity = NpcBody("DemoKeeper");
            NpcEntity smithEntity = NpcBody("DemoSmith");
            NpcEntity villagerEntity = NpcBody("DemoVillager");
            NpcEntity innkeeperEntity = NpcBody("DemoInnkeeper");
            NpcEntity elderEntity = NpcBody("DemoElder");
            NpcEntity guardEntity = NpcBody("DemoGuard");
            NpcEntity patrolEntity = NpcBody("DemoPatrolGuard");
            if (keeperEntity == null || smithEntity == null || villagerEntity == null || innkeeperEntity == null ||
                elderEntity == null || guardEntity == null || patrolEntity == null)
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
                new Stand { Name = "Bram", Title = "Bram the Smith", Local = SmithLocalPosition, Yaw = Facing(SmithLocalPosition), Dialog = smith, Body = smithEntity },
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
            // **The prefab, not 00Init's instance.** The GameInstance in 00Init is a prefab
            // instance, so writing the scene leaves a property *override* and the prefab
            // keeps whatever it had before. This wrote the scene for weeks while four other
            // builders wrote the prefab, and the two drifted apart: the prefab still had
            // `DropOnGround` - the setting that made every drop on the island invisible -
            // long after the scene was fixed. Anything that instantiates the prefab, which
            // includes the LAN test harness, got the old settings.
            const string prefabPath = "Assets/OpenMMORPG/Demo/Prefabs/GameInstance.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            GameInstance instance = prefab != null ? prefab.GetComponent<GameInstance>() : null;
            if (instance == null)
            {
                Debug.LogError($"[{nameof(DemoNpcBuilder)}] No GameInstance at {prefabPath}.");
                return;
            }

            var serialized = new SerializedObject(instance);

            // Kills leave a lootable body rather than scattering loose items (set 2026-09-16,
            // when nothing on the island turned out to be lootable at all).
            //
            // The kit offers one mode for the whole game - `DropOnGround`, `CorpseLooting` or
            // `Immediately` - and the demo was on `DropOnGround`, which spawns an
            // `ItemDropEntity` per item. That entity draws **the item's own `dropModel`**, and
            // **not one of the demo's 44 items has one**, so every drop from every enemy landed
            // invisible: present, collidable, pickup-able, and impossible to find. It read as
            // "animals are not lootable" because animals are what anyone tries first.
            //
            // `CorpseLooting` needs no per-item art - `CorpseEntity` is one prefab carrying its
            // own particle marker, and it holds the whole kill's loot in a single body the player
            // activates like any other interactable, which is the flow the demo already uses for
            // NPCs. Giving 44 items a drop model each would have worked too and is a lot more
            // to keep correct as items are added.
            serialized.FindProperty("monsterDeadDropItemMode").intValue = (int)RewardingItemMode.CorpseLooting;
            // Matched to the body's own lifetime, so the corpse and the loot in it go together.
            // The kit ships 60 here against a body that is destroyed after 2, which left a sack
            // glittering on empty grass - see DemoEntityBuilder.CorpseLifetime.
            serialized.FindProperty("monsterCorpseAppearDuration").floatValue = DemoEntityBuilder.CorpseLifetime;

            serialized.FindProperty("playerStorage.slotLimit").intValue = 40;
            serialized.FindProperty("playerStorage.weightLimit").intValue = 0;
            // The guild's chest, which like the player's has no size at all by default -
            // a `GuildStorage` dialog on a zero-slot storage opens an empty grid. Larger
            // than one character's, because a company shares it.
            serialized.FindProperty("guildStorage.slotLimit").intValue = 80;
            serialized.FindProperty("guildStorage.weightLimit").intValue = 0;
            serialized.FindProperty("conversationDistance").floatValue = 3.5f;
            serialized.FindProperty("equipmentModelBonesSetupManager").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<BaseEquipmentModelBonesSetupManager>(DemoItemBuilder.BonesSetupPath);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(prefab);
            AssetDatabase.SaveAssets();
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
