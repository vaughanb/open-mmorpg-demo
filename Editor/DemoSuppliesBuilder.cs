using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// Three kinds of item the demo never made: arrows for the bows, a scroll that sends a
    /// player back to the shrine they are bound to, and gems for the sockets in good gear.
    ///
    /// The demo makes eight of the kit's twenty item types. These are the three worth the
    /// least work for the most shown: `AmmoItem` gives the ranger's bow something to spend,
    /// `WarpToRespawnPointItem` completes the shrine that was built with nothing to use it,
    /// and `SocketEnhancerItem` opens a progression axis - sockets in a piece of gear - that
    /// nothing in the demo touched.
    ///
    /// **Its own step, and a late one**, for the same reason as `Build Gear Upkeep`: the
    /// gems' bonuses name the attributes `Build Progression` writes and the arrows are
    /// crafted from the timber `Build Harvestables` writes, and both of those run after
    /// `Build Items`.
    /// </summary>
    public static class DemoSuppliesBuilder
    {
        private const string ResourcesDir = "Assets/OpenMMORPG/Demo/GameData/Resources";
        private const string ItemDir = ResourcesDir + "/Items";
        private const string AmmoTypeDir = ResourcesDir + "/AmmoTypes";

        public const string ArrowItem = "Arrow";
        public const string ScrollItem = "ScrollOfReturn";

        /// <summary>
        /// Every gem is `Type1`, and every socket accepts `Type1`.
        ///
        /// The kit offers eight socket enhancer types so a project can say "this gem only
        /// goes in a weapon" - a real thing to want, and one more rule than a demo needs to
        /// teach what a socket is. One type means any gem fits any socketed piece, and the
        /// choice a player makes is which bonus they want rather than which slot it is
        /// allowed in.
        /// </summary>
        private const SocketEnhancerType OneTypeFitsAll = SocketEnhancerType.Type1;

        private struct GemSpec
        {
            public string Name;
            public string Title;
            public string Description;
            public int Price;
            /// <summary>A stat on `CharacterStats`, by its serialized name, and how much of it.</summary>
            public string Stat;
            public float Amount;
            /// <summary>The drawn icon's file name, which is not the item's name - see AdoptItemIcon.</summary>
            public string Icon;
        }

        /// <summary>
        /// Three gems, one per thing a player might be short of. Flat amounts rather than
        /// rates: a percentage of a number the player cannot see is not a choice, it is a
        /// guess.
        /// </summary>
        private static readonly GemSpec[] Gems =
        {
            new GemSpec { Name = "GemGarnet", Title = "Garnet", Price = 180, Stat = "hp", Amount = 25f, Icon = "Garnet",
                Description = "Blood-dark, and warm to hold. Set into armour it makes a body harder to put down." },
            new GemSpec { Name = "GemSapphire", Title = "Sapphire", Price = 180, Stat = "mp", Amount = 20f, Icon = "Sapphire",
                Description = "Cold all the way through. Set into a staff it holds a little more of whatever a caster draws on." },
            new GemSpec { Name = "GemCitrine", Title = "Citrine", Price = 220, Stat = "atkSpeed", Amount = 0.08f, Icon = "Citrine",
                Description = "Cut in a hurry by somebody who knew what they were doing. Whatever holds it moves quicker." },
        };

        /// <summary>
        /// What gets sockets, and how many.
        ///
        /// The best of each kind rather than everything: a socket in a peasant tunic is a
        /// socket nobody will ever fill, and a set of gear where every piece takes gems
        /// turns the feature into bookkeeping. Two in the best weapon of each class's line
        /// and one in the heaviest armour, so a player meets the system on the gear they
        /// were already pleased to find.
        /// </summary>
        private static readonly Dictionary<string, int> Socketed = new Dictionary<string, int>
        {
            { "IronLongsword", 2 },
            { "YewLongbow", 2 },
            { "ElderStaff", 2 },
            { "KnightCuirass", 1 },
            { "RangerJerkin", 1 },
            { "WizardRobe", 1 },
        };

        [MenuItem("Open MMORPG/Demo/Build Supplies (arrows, scrolls, gems)", priority = 156)]
        public static void Build()
        {
            DemoItemBuilder.EnsureFolder(AmmoTypeDir);

            BuildArrows();
            BuildScroll();
            foreach (GemSpec spec in Gems)
                BuildGem(spec);
            int socketed = OpenSockets();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[{nameof(DemoSuppliesBuilder)}] Arrows, a return scroll and {Gems.Length} gem(s); " +
                      $"{socketed} piece(s) of gear given sockets. Run Build NPCs And Quests for the pedlar's " +
                      "stock and Wire Game Database to register them.");
        }

        // ---- arrows ----------------------------------------------------------

        /// <summary>
        /// The arrows, and the ammo type that makes a bow want them.
        ///
        /// **The requirement lives on the `WeaponType`, not on the weapon**, so this is what
        /// makes every bow in the demo need arrows - both of them, and any added later,
        /// which is the right level for the rule to sit at.
        ///
        /// It is also the one change here that can take something away: with an ammo type
        /// set and an empty quiver, `DecreaseAmmos` returns false and the shot simply does
        /// not happen. Three things keep that from stranding a ranger - they start with a
        /// hundred, Marek sells them for a gold each, and they are craftable from timber -
        /// and **no monster in the demo carries a bow**, so nothing else is affected. Check
        /// that last one again before giving anything else an ammo type: a monster has no
        /// inventory to draw from and would simply stop attacking.
        /// </summary>
        private static void BuildArrows()
        {
            var type = Create<AmmoType>($"{AmmoTypeDir}/{ArrowItem}.asset");
            var typeSerialized = new SerializedObject(type);
            typeSerialized.FindProperty("id").stringValue = ArrowItem;
            typeSerialized.FindProperty("defaultTitle").stringValue = "Arrow";
            typeSerialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(type);

            var arrow = Create<AmmoItem>($"{ItemDir}/{ArrowItem}.asset");
            var serialized = new SerializedObject(arrow);
            serialized.FindProperty("id").stringValue = ArrowItem;
            serialized.FindProperty("defaultTitle").stringValue = "Arrow";
            serialized.FindProperty("defaultDescription").stringValue =
                "Ash shaft, goose fletching. They are not meant to come back.";
            serialized.FindProperty("sellPrice").intValue = 1;
            serialized.FindProperty("weight").floatValue = 0.02f;
            serialized.FindProperty("maxStack").intValue = 200;
            serialized.FindProperty("ammoType").objectReferenceValue = type;
            DemoItemBuilder.AdoptItemIcon(serialized, "Arrows");

            // A little damage of their own, on top of the bow's. The element is left null
            // deliberately: an empty `DamageElement` falls back to the one on GameInstance,
            // which is what lets the demo add damage without defining any elements at all.
            SerializedProperty damages = serialized.FindProperty("increaseDamages");
            damages.arraySize = 1;
            SerializedProperty damage = damages.GetArrayElementAtIndex(0);
            damage.FindPropertyRelative("amount.baseAmount.min").floatValue = 1f;
            damage.FindPropertyRelative("amount.baseAmount.max").floatValue = 3f;

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(arrow);

            var bow = AssetDatabase.LoadAssetAtPath<WeaponType>($"{ResourcesDir}/WeaponTypes/Bow.asset");
            if (bow == null)
            {
                Debug.LogError($"[{nameof(DemoSuppliesBuilder)}] No Bow weapon type; the arrows have nothing to load.");
                return;
            }
            var bowSerialized = new SerializedObject(bow);
            bowSerialized.FindProperty("ammoType").objectReferenceValue = type;
            bowSerialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(bow);
        }

        // ---- the scroll ------------------------------------------------------

        /// <summary>
        /// A scroll that puts a player back where they are bound.
        ///
        /// It reads `RespawnMapName`/`RespawnPosition` - exactly what the Wayside Shrine
        /// writes - so the two features finish each other: the shrine was worth binding at
        /// and the scroll is what makes binding worth doing anywhere but where you already
        /// are. Nothing to configure but a cooldown; the kit does the rest.
        /// </summary>
        private static void BuildScroll()
        {
            var scroll = Create<WarpToRespawnPointItem>($"{ItemDir}/{ScrollItem}.asset");
            var serialized = new SerializedObject(scroll);
            serialized.FindProperty("id").stringValue = ScrollItem;
            serialized.FindProperty("defaultTitle").stringValue = "Scroll of Return";
            serialized.FindProperty("defaultDescription").stringValue =
                "Burn it and the runes you are bound to will pull the rest of you after them. Unpleasant, and quicker than walking.";
            serialized.FindProperty("sellPrice").intValue = 90;
            serialized.FindProperty("weight").floatValue = 0.1f;
            serialized.FindProperty("maxStack").intValue = 10;
            // Long enough that it is a way home and not a way out of a fight.
            serialized.FindProperty("useItemCooldown").floatValue = 30f;
            DemoItemBuilder.AdoptItemIcon(serialized, "ScrollOfReturn");
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(scroll);
        }

        // ---- gems ------------------------------------------------------------

        private static void BuildGem(GemSpec spec)
        {
            var gem = Create<SocketEnhancerItem>($"{ItemDir}/{spec.Name}.asset");
            var serialized = new SerializedObject(gem);
            serialized.FindProperty("id").stringValue = spec.Name;
            serialized.FindProperty("defaultTitle").stringValue = spec.Title;
            serialized.FindProperty("defaultDescription").stringValue = spec.Description;
            serialized.FindProperty("sellPrice").intValue = spec.Price;
            serialized.FindProperty("weight").floatValue = 0.05f;
            serialized.FindProperty("maxStack").intValue = 20;
            serialized.FindProperty("socketEnhancerType").enumValueIndex = (int)OneTypeFitsAll;
            DemoItemBuilder.AdoptItemIcon(serialized, spec.Icon);

            SerializedProperty stat = serialized.FindProperty($"socketEnhanceEffect.stats.{spec.Stat}");
            if (stat == null)
            {
                Debug.LogError($"[{nameof(DemoSuppliesBuilder)}] No stat \"{spec.Stat}\" on CharacterStats.");
            }
            else if (stat.propertyType == SerializedPropertyType.Integer)
            {
                stat.intValue = Mathf.RoundToInt(spec.Amount);
            }
            else
            {
                stat.floatValue = spec.Amount;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(gem);
        }

        /// <summary>
        /// Cuts sockets into the gear that is worth cutting them into.
        ///
        /// **There is no socket *count* field.** `maxSocket` looks like one and is
        /// `[System.Obsolete]`, `[HideInInspector]` and deprecated; the number of sockets a
        /// piece has is the **length of `availableSocketEnhancerTypes`**, one entry per
        /// socket saying what may go in that socket. The first cut of this set `maxSocket`
        /// to the count and the type list to one entry, and got the right answer by
        /// accident: `BaseEquipmentItem` migrates a non-zero `maxSocket` by expanding the
        /// type array to that length and resetting the field to zero, so the write appeared
        /// to be ignored while quietly doing the job. Written properly, nothing depends on
        /// a migration that is documented as going away.
        /// </summary>
        private static int OpenSockets()
        {
            int done = 0;
            foreach (KeyValuePair<string, int> entry in Socketed)
            {
                var item = AssetDatabase.LoadAssetAtPath<BaseItem>($"{ItemDir}/{entry.Key}.asset");
                if (item == null)
                {
                    Debug.LogWarning($"[{nameof(DemoSuppliesBuilder)}] No item \"{entry.Key}\" to cut sockets into.");
                    continue;
                }
                var serialized = new SerializedObject(item);
                SerializedProperty types = serialized.FindProperty("availableSocketEnhancerTypes");
                if (types == null)
                {
                    Debug.LogWarning($"[{nameof(DemoSuppliesBuilder)}] \"{entry.Key}\" is not equipment.");
                    continue;
                }
                types.arraySize = entry.Value;
                for (int i = 0; i < entry.Value; ++i)
                    types.GetArrayElementAtIndex(i).enumValueIndex = (int)OneTypeFitsAll;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(item);
                ++done;
            }
            return done;
        }

        // ---- for the database ------------------------------------------------

        public static List<Object> AllAmmoTypes()
        {
            var found = new List<Object>();
            foreach (string guid in AssetDatabase.FindAssets("t:AmmoType", new[] { AmmoTypeDir }))
                found.Add(AssetDatabase.LoadAssetAtPath<Object>(AssetDatabase.GUIDToAssetPath(guid)));
            return found;
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
