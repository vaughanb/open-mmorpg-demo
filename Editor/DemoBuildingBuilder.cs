using UnityEditor;
using UnityEngine;
using UnityEngine.Events;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// The one thing on the island a player builds themselves: a campfire, carried as a kit
    /// and set down wherever they are standing.
    ///
    /// It is here because `BuildingItem` was the single largest hole in the demo's coverage
    /// of the kit. One item turns on five systems that had never appeared: the item type
    /// itself, `BuildingEntity`, the aim-and-place build controls, `StorageEntity`, and
    /// `CampFireEntity`'s item conversion - which is the part that makes it more than a
    /// prop, because the demo already has raw venison and a stew to turn it into.
    ///
    /// **A campfire is a storage that cooks.** `CampFireEntity : StorageEntity :
    /// BuildingEntity`, so it is a container you open like the bank's strongbox, and what
    /// it holds it works on: timber burns, venison put in beside it comes out as stew.
    /// Nothing else in the demo converts one item into another over time.
    /// </summary>
    public static class DemoBuildingBuilder
    {
        private const string GameDataDir = "Assets/OpenMMORPG/Demo/GameData";
        private const string ItemDir = GameDataDir + "/Resources/Items";
        private const string PrefabDir = "Assets/OpenMMORPG/Demo/Prefabs/GamePlay/Buildings";
        private const string CampfirePath = PrefabDir + "/DemoCampfire.prefab";
        private const string KitName = "CampfireKit";

        /// <summary>
        /// One campfire per player. The alternative to a lifetime, and the better one here:
        /// a lifetime on a *storage* building quietly eats whatever was left inside it when
        /// it expires, whereas a limit just stops the island filling up with fires. It also
        /// happens to be the only place the demo shows `buildLimit` working.
        /// </summary>
        private const int OnePerPlayer = 1;

        /// <summary>
        /// What the fire burns and what it makes.
        ///
        /// Timber is the fuel - the same timber the island's trees drop - and a fire only
        /// works while it has some. Venison is the one conversion: the deer that were
        /// nothing but a quest item and a merchant sale now feed the player who shot them.
        /// Both intervals are short enough to watch happen, because a demo conversion the
        /// player never sees finish has not demonstrated anything.
        /// </summary>
        private struct Conversion
        {
            public string Item;
            public int Amount;
            public bool IsFuel;
            public float Interval;
            public string Into;
            public int IntoAmount;
        }

        private static readonly Conversion[] Conversions =
        {
            new Conversion { Item = "Timber", Amount = 1, IsFuel = true, Interval = 40f },
            new Conversion { Item = "Venison", Amount = 1, IsFuel = false, Interval = 12f, Into = "Stew", IntoAmount = 1 },
        };

        [MenuItem("Open MMORPG/Demo/Build Player Buildings", priority = 154)]
        public static void Build()
        {
            DemoItemBuilder.EnsureFolder(PrefabDir);

            GameObject prefab = BuildCampfire();
            if (prefab == null)
                return;
            BuildKitItem(prefab);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[{nameof(DemoBuildingBuilder)}] Built the campfire and its kit. " +
                      "Run Wire Game Database so the item is registered, and Build NPCs And Quests " +
                      "if the pedlar's stock changed.");
        }

        // ---- the campfire ----------------------------------------------------

        private static GameObject BuildCampfire()
        {
            var fire = new GameObject("DemoCampfire");

            // The same two props the village green's fire is made of, so a player's camp
            // reads as the same object as the one they have been warming their hands at.
            GameObject basket = DemoSceneBuilder.Prop("Firepit", fire.transform, Vector3.zero, 0f, false);
            DemoSceneBuilder.Prop("CampfireTripod", fire.transform, Vector3.zero, 20f, false);
            if (basket == null)
            {
                Debug.LogError($"[{nameof(DemoBuildingBuilder)}] No Firepit prop to build a campfire from.");
                Object.DestroyImmediate(fire);
                return null;
            }

            // The flame, down in the basket where the village's is, and switched **off**:
            // an unlit fire is what an empty campfire should look like, and the entity's own
            // turn-on event is what lights it.
            MultiplayerARPG.Demo.DemoTorch torch = DemoFlameBuilder.Light(
                DemoFlameBuilder.CampfireFlamePath, fire.transform, new Vector3(0f, 0.85f, 0f),
                MultiplayerARPG.Demo.DemoTorch.Schedule.Always);
            GameObject flame = torch != null ? torch.gameObject : null;
            if (flame != null)
                flame.SetActive(false);

            var collider = fire.AddComponent<CapsuleCollider>();
            collider.height = 1.1f;
            collider.radius = 0.42f;
            collider.center = new Vector3(0f, 0.55f, 0f);

            CampFireEntity entity = fire.AddComponent<CampFireEntity>();
            var serialized = new SerializedObject(entity);

            // Placed on open ground rather than snapped to a socket. Without this a
            // building can only go on a `BuildingArea`, and the island has none - the kit
            // expects foundations and walls, and a campfire is neither.
            serialized.FindProperty("canBuildOnAnySurface").boolValue = true;
            serialized.FindProperty("buildLimit").intValue = OnePerPlayer;
            serialized.FindProperty("lifeTime").floatValue = 0f;
            serialized.FindProperty("maxHp").intValue = 100;
            serialized.FindProperty("canBeAttacked").boolValue = true;
            serialized.FindProperty("buildDistance").floatValue = 5f;
            serialized.FindProperty("storage.slotLimit").intValue = 8;
            serialized.FindProperty("storage.weightLimit").intValue = 0;
            serialized.FindProperty("canUseByEveryone").boolValue = true;
            serialized.FindProperty("lockable").boolValue = false;

            WriteConversions(serialized);
            WriteDroppingItems(serialized);
            if (flame != null)
            {
                WireFlame(serialized, "onTurnOn", flame, true);
                WireFlame(serialized, "onTurnOff", flame, false);
                WireFlame(serialized, "onInitialTurnOn", flame, true);
                WireFlame(serialized, "onInitialTurnOff", flame, false);
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(fire, CampfirePath);
            DemoEntityBuilder.GiveOwnNetworkId(CampfirePath);
            AssetDatabase.ImportAsset(CampfirePath, ImportAssetOptions.ForceUpdate);
            Object.DestroyImmediate(fire);
            return AssetDatabase.LoadAssetAtPath<GameObject>(CampfirePath);
        }

        private static void WriteConversions(SerializedObject serialized)
        {
            SerializedProperty converts = serialized.FindProperty("convertItems");
            converts.arraySize = Conversions.Length;
            for (int i = 0; i < Conversions.Length; ++i)
            {
                Conversion spec = Conversions[i];
                SerializedProperty entry = converts.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("item.item").objectReferenceValue = LoadItem(spec.Item);
                entry.FindPropertyRelative("item.amount").intValue = spec.Amount;
                entry.FindPropertyRelative("isFuel").boolValue = spec.IsFuel;
                entry.FindPropertyRelative("convertInterval").floatValue = spec.Interval;
                entry.FindPropertyRelative("convertedItem.item").objectReferenceValue =
                    string.IsNullOrEmpty(spec.Into) ? null : LoadItem(spec.Into);
                entry.FindPropertyRelative("convertedItem.amount").intValue = spec.IntoAmount;
            }
        }

        /// <summary>
        /// Break the fire down and you get some of the wood back, which is what stops a
        /// misplaced campfire being a waste of a kit.
        /// </summary>
        private static void WriteDroppingItems(SerializedObject serialized)
        {
            SerializedProperty drops = serialized.FindProperty("droppingItems");
            BaseItem timber = LoadItem("Timber");
            drops.arraySize = timber == null ? 0 : 1;
            if (timber == null)
                return;
            SerializedProperty entry = drops.GetArrayElementAtIndex(0);
            entry.FindPropertyRelative("item").objectReferenceValue = timber;
            entry.FindPropertyRelative("amount").intValue = 2;
        }

        /// <summary>
        /// Points one of the campfire's own turn-on/turn-off events at the flame object, so
        /// the fire is lit exactly while it has fuel in it.
        ///
        /// Written as a **persistent** listener - the kind the inspector shows - rather than
        /// added in code, because the prefab has to carry it: there is no demo component on
        /// this entity to add a runtime one from, and adding one would be a script where a
        /// UnityEvent already does the job. `m_Mode` has to be `Bool` and the argument set,
        /// or the call is serialized as "SetActive with no argument" and silently does
        /// nothing at runtime.
        /// </summary>
        private static void WireFlame(SerializedObject serialized, string eventName, GameObject flame, bool on)
        {
            SerializedProperty calls = serialized.FindProperty(eventName + ".m_PersistentCalls.m_Calls");
            if (calls == null)
            {
                Debug.LogWarning($"[{nameof(DemoBuildingBuilder)}] No event \"{eventName}\" on the campfire.");
                return;
            }
            calls.arraySize = 1;
            SerializedProperty call = calls.GetArrayElementAtIndex(0);
            call.FindPropertyRelative("m_Target").objectReferenceValue = flame;
            call.FindPropertyRelative("m_MethodName").stringValue = "SetActive";
            call.FindPropertyRelative("m_Mode").enumValueIndex = (int)PersistentListenerMode.Bool;
            call.FindPropertyRelative("m_CallState").enumValueIndex = (int)UnityEventCallState.RuntimeOnly;
            call.FindPropertyRelative("m_Arguments.m_BoolArgument").boolValue = on;
            SerializedProperty typeName = call.FindPropertyRelative("m_TargetAssemblyTypeName");
            if (typeName != null)
                typeName.stringValue = "UnityEngine.GameObject, UnityEngine";
        }

        // ---- the kit -------------------------------------------------------

        /// <summary>
        /// The item the player carries and sets down.
        ///
        /// **This asset is what registers the campfire prefab at runtime.**
        /// `BuildingItem.PrepareRelatesData` is the only thing in the kit that fills
        /// `GameInstance.BuildingEntities`, so a building with no item behind it exists in
        /// a scene and nowhere in the database - which is the whole reason the demo's craft
        /// stations are currently switched off (see DemoCraftStationBuilder).
        /// </summary>
        private static void BuildKitItem(GameObject campfire)
        {
            string path = $"{ItemDir}/{KitName}.asset";
            var kit = AssetDatabase.LoadAssetAtPath<BuildingItem>(path);
            if (kit == null)
            {
                kit = ScriptableObject.CreateInstance<BuildingItem>();
                AssetDatabase.CreateAsset(kit, path);
            }
            var serialized = new SerializedObject(kit);
            serialized.FindProperty("id").stringValue = KitName;
            serialized.FindProperty("defaultTitle").stringValue = "Campfire Kit";
            serialized.FindProperty("defaultDescription").stringValue =
                "Tripod, kettle and a bundle of kindling. Set it down, feed it timber, and it will cook what you put beside it.";
            serialized.FindProperty("sellPrice").intValue = 45;
            serialized.FindProperty("weight").floatValue = 4f;
            serialized.FindProperty("maxStack").intValue = 5;
            serialized.FindProperty("buildingEntity").objectReferenceValue = campfire.GetComponent<BuildingEntity>();
            DemoItemBuilder.AdoptItemIcon(serialized, "CampfireKit");
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(kit);
        }

        private static BaseItem LoadItem(string name)
        {
            var item = AssetDatabase.LoadAssetAtPath<BaseItem>($"{ItemDir}/{name}.asset");
            if (item == null)
            {
                Debug.LogError($"[{nameof(DemoBuildingBuilder)}] No item \"{name}\". Run Build Items, " +
                               "Build Harvestables and Build Progression first.");
            }
            return item;
        }
    }
}
