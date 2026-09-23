using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// The three data types that give the island's combat a shape beyond one number hitting
    /// another: damage elements, the ailments they leave behind, and set bonuses on the
    /// armour the enemies drop.
    ///
    /// All three were empty lists. **Every hit in the demo was the same kind of hit** - the
    /// mage's frost nova and meteor were frost and fire in name only, a wolf's bite and an
    /// arrow were identical in kind, and armour resisted one undifferentiated thing. Three
    /// complete armour sets dropped off three enemy families and wearing all six pieces was
    /// worth exactly as much as wearing six unrelated ones.
    ///
    /// They are built together because they only mean anything together: an element is a
    /// label until something resists it, a resistance is a number until something deals
    /// that element, and a set bonus is the natural place to hang a resistance.
    /// </summary>
    public static class DemoCombatDataBuilder
    {
        private const string ResourcesDir = "Assets/OpenMMORPG/Demo/GameData/Resources";
        private const string ItemDir = ResourcesDir + "/Items";
        private const string SkillDir = ResourcesDir + "/Skills";
        private const string ElementDir = ResourcesDir + "/DamageElements";
        private const string StatusDir = ResourcesDir + "/StatusEffects";
        private const string SetDir = ResourcesDir + "/EquipmentSets";
        private const string GameInstancePath = "Assets/OpenMMORPG/Demo/Prefabs/GameInstance.prefab";

        public const string Physical = "Physical";
        public const string Fire = "Fire";
        public const string Frost = "Frost";

        /// <summary>
        /// How much of an element a character can ever resist away. Short of 1, because a
        /// resistance that can reach 100% turns an element off, and an element nothing can
        /// hurt you with is not a trade-off, it is a switch.
        /// </summary>
        private const float MaxResistance = 0.75f;

        // ---- elements --------------------------------------------------------

        private struct ElementSpec
        {
            public string Name;
            public string Title;
            public string Description;
        }

        /// <summary>
        /// Three, not eight. Physical is what everything on the island already did; fire
        /// and frost are the mage's two, and exist so that his kit is a different thing
        /// from a bow rather than a differently-coloured one.
        /// </summary>
        private static readonly ElementSpec[] Elements =
        {
            new ElementSpec { Name = Physical, Title = "Physical",
                Description = "Edges, points and weight. What almost everything on the island hits with." },
            new ElementSpec { Name = Fire, Title = "Fire",
                Description = "Burns on after the blow lands." },
            new ElementSpec { Name = Frost, Title = "Frost",
                Description = "Takes the speed out of whatever it touches." },
        };

        // ---- ailments --------------------------------------------------------

        private struct AilmentSpec
        {
            public string Name;
            public string Title;
            public string Description;
            public float Seconds;
            /// <summary>Health per second while it lasts. Negative is damage.</summary>
            public int HpPerSecond;
            /// <summary>A rate on move speed, so -0.3 is thirty percent slower.</summary>
            public float MoveSpeedRate;
            /// <summary>The skill that applies it, and at what level of itself.</summary>
            public string FromSkill;
        }

        /// <summary>
        /// Three ailments, each hung on a skill that already existed and already said it
        /// did this.
        ///
        /// **A `StatusEffect` is a named, resistable buff** - it wraps a `Buff` and adds the
        /// resistance maths. So a burn is a buff with negative health recovery, which the
        /// kit pays out once a second for the duration, and a chill is a buff with a
        /// negative rate on move speed. Nothing new has to be invented; what the assets buy
        /// is that the effect has a name, shows in the buff bar, and can be resisted by
        /// gear.
        /// </summary>
        private static readonly AilmentSpec[] Ailments =
        {
            new AilmentSpec { Name = "Burning", Title = "Burning", Seconds = 6f, HpPerSecond = -7, FromSkill = "Meteor",
                Description = "Still alight. It will go out on its own, eventually." },
            new AilmentSpec { Name = "Chilled", Title = "Chilled", Seconds = 5f, MoveSpeedRate = -0.35f, FromSkill = "FrostNova",
                Description = "Slowed to a wade. Everything takes longer than it should." },
            new AilmentSpec { Name = "Bleeding", Title = "Bleeding", Seconds = 8f, HpPerSecond = -5, FromSkill = "HuntersMark",
                Description = "An arrow wound that will not close while you keep moving." },
        };

        // ---- sets ------------------------------------------------------------

        private struct SetSpec
        {
            public string Name;
            public string Title;
            public string[] Pieces;
            /// <summary>One bonus per step, starting at **two** pieces worn. They stack.</summary>
            public Bonus[] Steps;
        }

        private struct Bonus
        {
            public string Note;
            public float Hp;
            public float Mp;
            public float MoveSpeed;
            public float AtkSpeed;
            public float CriRate;
            /// <summary>Flat armour against an element.</summary>
            public string ArmorElement;
            public float Armor;
            /// <summary>A share of an element's damage ignored, 0..1.</summary>
            public string ResistElement;
            public float Resist;
        }

        /// <summary>
        /// The three sets the island already drops, and what wearing them together is worth.
        ///
        /// **A set's bonus steps start at two pieces and stack.** `GetBuffs` walks
        /// `effects[0..setAmount)`, and `setAmount` counts pieces *beyond the first* - so
        /// `effects[0]` is the two-piece bonus and a player in four pieces has the first
        /// three entries at once. Three steps each, which the wizard's four-piece set can
        /// reach and the six-piece sets clear with room to spare.
        ///
        /// Each set is shaped like the people who wear it: the knight's plate is armour and
        /// health, the ranger's leathers are speed, the wizard's robes are the only thing on
        /// the island that resists fire and frost - which is also the only reason a mage
        /// would wear cloth into a crypt.
        /// </summary>
        private static readonly SetSpec[] Sets =
        {
            new SetSpec
            {
                Name = "KnightSet", Title = "Knight's Harness",
                Pieces = new[] { "KnightHelm", "KnightCuirass", "KnightGauntlets", "KnightGreaves", "KnightSabatons", "KnightPauldrons" },
                Steps = new[]
                {
                    new Bonus { Note = "two pieces", Hp = 25f },
                    new Bonus { Note = "three pieces", ArmorElement = Physical, Armor = 6f },
                    new Bonus { Note = "four pieces", Hp = 45f, ArmorElement = Physical, Armor = 8f },
                },
            },
            new SetSpec
            {
                Name = "RangerSet", Title = "Ranger's Kit",
                Pieces = new[] { "RangerHood", "RangerJerkin", "RangerBracers", "RangerBreeches", "RangerBoots", "RangerPauldron" },
                Steps = new[]
                {
                    new Bonus { Note = "two pieces", MoveSpeed = 0.4f },
                    new Bonus { Note = "three pieces", CriRate = 0.04f },
                    new Bonus { Note = "four pieces", AtkSpeed = 0.1f, MoveSpeed = 0.4f },
                },
            },
            new SetSpec
            {
                Name = "WizardSet", Title = "Wizard's Vestments",
                Pieces = new[] { "WizardRobe", "WizardSleeves", "WizardTrousers", "WizardShoes" },
                Steps = new[]
                {
                    new Bonus { Note = "two pieces", Mp = 20f },
                    new Bonus { Note = "three pieces", ResistElement = Fire, Resist = 0.15f },
                    new Bonus { Note = "four pieces", ResistElement = Frost, Resist = 0.15f, Mp = 30f },
                },
            },
        };

        /// <summary>
        /// Which skill deals which element. Everything not named here stays physical, which
        /// includes every weapon, every monster and both the warrior's and the ranger's
        /// lines - a cleave is a cleave.
        /// </summary>
        private static readonly Dictionary<string, string> SkillElements = new Dictionary<string, string>
        {
            { "Meteor", Fire },
            { "FrostNova", Frost },
            { "UnholyNova", Fire },
        };

        [MenuItem("Open MMORPG/Demo/Build Combat Data (elements, ailments, sets)", priority = 157)]
        public static void Build()
        {
            DemoItemBuilder.EnsureFolder(ElementDir);
            DemoItemBuilder.EnsureFolder(StatusDir);
            DemoItemBuilder.EnsureFolder(SetDir);

            foreach (ElementSpec spec in Elements)
                BuildElement(spec);
            foreach (AilmentSpec spec in Ailments)
                BuildAilment(spec);
            foreach (SetSpec spec in Sets)
                BuildSet(spec);

            int weapons = ElementOnWeapons();
            int armour = ElementOnArmour();
            int pieces = AssignSets();
            int skills = ElementAndAilmentOnSkills();
            SetDefaultElement();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[{nameof(DemoCombatDataBuilder)}] {Elements.Length} element(s), {Ailments.Length} ailment(s), " +
                      $"{Sets.Length} set(s); {weapons} weapon(s) and {armour} piece(s) of armour given an element, " +
                      $"{pieces} piece(s) put in a set, {skills} skill(s) touched. " +
                      "Run Wire Game Database to register the elements and ailments.");
        }

        private static void BuildElement(ElementSpec spec)
        {
            var element = Create<DamageElement>($"{ElementDir}/{spec.Name}.asset");
            var serialized = new SerializedObject(element);
            serialized.FindProperty("id").stringValue = spec.Name;
            serialized.FindProperty("defaultTitle").stringValue = spec.Title;
            serialized.FindProperty("defaultDescription").stringValue = spec.Description;
            serialized.FindProperty("maxResistanceAmount").floatValue = MaxResistance;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(element);
        }

        private static void BuildAilment(AilmentSpec spec)
        {
            var status = Create<StatusEffect>($"{StatusDir}/{spec.Name}.asset");
            var serialized = new SerializedObject(status);
            serialized.FindProperty("id").stringValue = spec.Name;
            serialized.FindProperty("defaultTitle").stringValue = spec.Title;
            serialized.FindProperty("defaultDescription").stringValue = spec.Description;
            serialized.FindProperty("buff.duration.baseAmount").floatValue = spec.Seconds;
            serialized.FindProperty("buff.recoveryHp.baseAmount").intValue = spec.HpPerSecond;
            SerializedProperty speed = serialized.FindProperty("buff.increaseStatsRate.baseStats.moveSpeed");
            if (speed != null)
                speed.floatValue = spec.MoveSpeedRate;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(status);
        }

        private static void BuildSet(SetSpec spec)
        {
            var set = Create<EquipmentSet>($"{SetDir}/{spec.Name}.asset");
            var serialized = new SerializedObject(set);
            serialized.FindProperty("id").stringValue = spec.Name;
            serialized.FindProperty("defaultTitle").stringValue = spec.Title;

            SerializedProperty effects = serialized.FindProperty("effects");
            effects.arraySize = spec.Steps.Length;
            for (int i = 0; i < spec.Steps.Length; ++i)
            {
                Bonus step = spec.Steps[i];
                SerializedProperty effect = effects.GetArrayElementAtIndex(i);
                SetStat(effect, "stats.hp", step.Hp);
                SetStat(effect, "stats.mp", step.Mp);
                SetStat(effect, "stats.moveSpeed", step.MoveSpeed);
                SetStat(effect, "stats.atkSpeed", step.AtkSpeed);
                SetStat(effect, "stats.criRate", step.CriRate);
                SetElementAmount(effect, "armors", step.ArmorElement, step.Armor);
                SetElementAmount(effect, "resistances", step.ResistElement, step.Resist);
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(set);
        }

        private static void SetStat(SerializedProperty effect, string path, float value)
        {
            SerializedProperty property = effect.FindPropertyRelative(path);
            if (property == null)
                return;
            if (property.propertyType == SerializedPropertyType.Integer)
                property.intValue = Mathf.RoundToInt(value);
            else
                property.floatValue = value;
        }

        private static void SetElementAmount(SerializedProperty effect, string field, string elementName, float amount)
        {
            SerializedProperty list = effect.FindPropertyRelative(field);
            if (list == null)
                return;
            if (string.IsNullOrEmpty(elementName) || amount == 0f)
            {
                list.arraySize = 0;
                return;
            }
            list.arraySize = 1;
            SerializedProperty entry = list.GetArrayElementAtIndex(0);
            entry.FindPropertyRelative("damageElement").objectReferenceValue = Element(elementName);
            entry.FindPropertyRelative("amount").floatValue = amount;
        }

        // ---- assignment ------------------------------------------------------

        /// <summary>
        /// Names the element every weapon deals.
        ///
        /// It is `Physical` for all of them, which is what they already did by falling back
        /// to `GameInstance`'s default - but a fallback is invisible. Written out, the
        /// tooltip says what kind of damage a sword does, and the armour below has something
        /// to name when it resists it.
        /// </summary>
        private static int ElementOnWeapons()
        {
            int done = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:WeaponItem", new[] { ItemDir }))
            {
                var item = AssetDatabase.LoadAssetAtPath<WeaponItem>(AssetDatabase.GUIDToAssetPath(guid));
                if (item == null)
                    continue;
                var serialized = new SerializedObject(item);
                SerializedProperty element = serialized.FindProperty("damageAmount.damageElement");
                if (element == null)
                    continue;
                element.objectReferenceValue = Element(Physical);
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(item);
                ++done;
            }
            return done;
        }

        private static int ElementOnArmour()
        {
            int done = 0;
            foreach (string filter in new[] { "t:ArmorItem", "t:ShieldItem" })
            {
                foreach (string guid in AssetDatabase.FindAssets(filter, new[] { ItemDir }))
                {
                    var item = AssetDatabase.LoadAssetAtPath<BaseItem>(AssetDatabase.GUIDToAssetPath(guid));
                    if (item == null)
                        continue;
                    var serialized = new SerializedObject(item);
                    SerializedProperty element = serialized.FindProperty("armorAmount.damageElement");
                    if (element == null)
                        continue;
                    element.objectReferenceValue = Element(Physical);
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(item);
                    ++done;
                }
            }
            return done;
        }

        private static int AssignSets()
        {
            int done = 0;
            foreach (SetSpec spec in Sets)
            {
                var set = AssetDatabase.LoadAssetAtPath<EquipmentSet>($"{SetDir}/{spec.Name}.asset");
                foreach (string piece in spec.Pieces)
                {
                    var item = AssetDatabase.LoadAssetAtPath<BaseItem>($"{ItemDir}/{piece}.asset");
                    if (item == null)
                    {
                        Debug.LogWarning($"[{nameof(DemoCombatDataBuilder)}] No \"{piece}\" for {spec.Title}.");
                        continue;
                    }
                    var serialized = new SerializedObject(item);
                    SerializedProperty property = serialized.FindProperty("equipmentSet");
                    if (property == null)
                        continue;
                    property.objectReferenceValue = set;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(item);
                    ++done;
                }
            }
            return done;
        }

        /// <summary>
        /// Gives the two elemental skills their element, and hangs each ailment on the skill
        /// that was already described as doing it.
        ///
        /// `Skill.attackStatusEffects` is the hook - a list of {status effect, level} the
        /// kit applies to whatever the skill hits. The demo's skills already carried debuffs
        /// that did this anonymously; naming them means the buff bar says *burning* rather
        /// than showing an unlabelled timer.
        /// </summary>
        private static int ElementAndAilmentOnSkills()
        {
            int done = 0;
            foreach (KeyValuePair<string, string> entry in SkillElements)
            {
                var skill = AssetDatabase.LoadAssetAtPath<BaseSkill>($"{SkillDir}/{entry.Key}.asset");
                if (skill == null)
                {
                    Debug.LogWarning($"[{nameof(DemoCombatDataBuilder)}] No skill \"{entry.Key}\" to give an element.");
                    continue;
                }
                var serialized = new SerializedObject(skill);
                SerializedProperty element = serialized.FindProperty("damageAmount.damageElement");
                if (element != null)
                {
                    element.objectReferenceValue = Element(entry.Value);
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(skill);
                    ++done;
                }
            }

            foreach (AilmentSpec spec in Ailments)
            {
                if (string.IsNullOrEmpty(spec.FromSkill))
                    continue;
                var skill = AssetDatabase.LoadAssetAtPath<BaseSkill>($"{SkillDir}/{spec.FromSkill}.asset");
                var status = AssetDatabase.LoadAssetAtPath<StatusEffect>($"{StatusDir}/{spec.Name}.asset");
                if (skill == null || status == null)
                {
                    Debug.LogWarning($"[{nameof(DemoCombatDataBuilder)}] Cannot hang {spec.Title} on \"{spec.FromSkill}\".");
                    continue;
                }
                var serialized = new SerializedObject(skill);
                SerializedProperty list = serialized.FindProperty("attackStatusEffects");
                if (list == null)
                {
                    Debug.LogWarning($"[{nameof(DemoCombatDataBuilder)}] \"{spec.FromSkill}\" takes no attack status effects.");
                    continue;
                }
                list.arraySize = 1;
                SerializedProperty applying = list.GetArrayElementAtIndex(0);
                applying.FindPropertyRelative("statusEffect").objectReferenceValue = status;
                applying.FindPropertyRelative("buffLevel.baseAmount").intValue = 1;
                applying.FindPropertyRelative("buffLevel.amountIncreaseEachLevel").floatValue = 0f;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(skill);
                ++done;
            }
            return done;
        }

        /// <summary>
        /// Points `GameInstance` at `Physical` as the fallback element.
        ///
        /// Without this the kit **invents one at runtime** - `GameInstance` creates a
        /// nameless `DamageElement` when the field is null, which is why the demo worked
        /// with no elements at all and also why nothing could ever resist anything. With a
        /// real asset there, the thing that was already happening becomes the thing the
        /// armour is talking about.
        ///
        /// **Written to the prefab, not to 00Init's instance.** The GameInstance in 00Init
        /// is a prefab instance, so writing the scene leaves a property *override* and the
        /// prefab keeps whatever it had. Four of the six builders that configure
        /// GameInstance already write the prefab; this one wrote the scene, and the two
        /// disagreed - found by the harness, which instantiates the prefab and so ran with
        /// no default element at all while 00Init had one.
        /// </summary>
        private static void SetDefaultElement()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GameInstancePath);
            GameInstance instance = prefab != null ? prefab.GetComponent<GameInstance>() : null;
            if (instance == null)
            {
                Debug.LogError($"[{nameof(DemoCombatDataBuilder)}] No GameInstance at {GameInstancePath}.");
                return;
            }
            var serialized = new SerializedObject(instance);
            serialized.FindProperty("defaultDamageElement").objectReferenceValue = Element(Physical);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(prefab);
        }

        private static DamageElement Element(string name)
        {
            return AssetDatabase.LoadAssetAtPath<DamageElement>($"{ElementDir}/{name}.asset");
        }

        // ---- for the database ------------------------------------------------

        public static List<Object> AllElements()
        {
            return All("t:DamageElement", ElementDir);
        }

        public static List<Object> AllStatusEffects()
        {
            return All("t:StatusEffect", StatusDir);
        }

        private static List<Object> All(string filter, string folder)
        {
            var found = new List<Object>();
            foreach (string guid in AssetDatabase.FindAssets(filter, new[] { folder }))
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
