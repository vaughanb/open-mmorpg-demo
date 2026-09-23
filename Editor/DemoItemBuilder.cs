using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// Creates the demo's equipment: the weapon and armour types, and the items that
    /// use them.
    ///
    /// Armour items point straight at the Quaternius outfit models. Those are skinned
    /// to their own copy of the shared skeleton, and the kit rebinds them onto the
    /// wearer by bone name at runtime, so no per-item rigging is needed — but that
    /// only works through the by-bone-names setup manager, which this also creates.
    /// The manager GameInstance falls back to matches by HumanBodyBones instead, and
    /// the outfit parts carry no humanoid avatar for it to read.
    ///
    /// Every armour slot lines up with an EquipmentContainer on the character model,
    /// so equipping a chest piece swaps the bare torso for it and leaves the rest.
    /// </summary>
    public static class DemoItemBuilder
    {
        /// <summary>
        /// Whether bows are drawn and held before loosing. True only for the shooter
        /// controller, which is the one thing in the kit that starts a charge; with the
        /// target-based controller a bow set to fire on release never fires at all.
        /// </summary>
        public const bool BowsCharge = false;

        private const string GameDataDir = "Assets/OpenMMORPG/Demo/GameData";
        private const string ResourcesDir = GameDataDir + "/Resources";
        private const string OutfitDir = "Assets/Plugins/Quaternius/Characters/Models/Outfits";
        private const string EquipmentDir = "Assets/OpenMMORPG/Demo/Prefabs/GamePlay/Equipments";
        private const string MissileDir = "Assets/OpenMMORPG/Demo/Prefabs/GamePlay/Missiles";
        private const string MaterialDir = "Assets/OpenMMORPG/Demo/Materials";

        /// <summary>
        /// What a missile is allowed to hit: the two character layers, plus the world so an
        /// arrow that misses stops in the scenery instead of flying on through it.
        /// </summary>
        private static int HitLayers
        {
            get { return (1 << 17) | (1 << 18) | (1 << 13) | (1 << 0); }
        }

        public const string BonesSetupPath = GameDataDir + "/EquipmentBonesSetup.asset";

        /// <summary>Armour slots. These names are both the equip position and the model socket.</summary>
        public static readonly string[] ArmourSlots = { "Head", "Body", "Arms", "Legs", "Feet", "Pauldron" };

        public const string SocketRightHand = "RightHand";
        public const string SocketLeftHand = "LeftHand";

        [MenuItem("Open MMORPG/Demo/Build Items")]
        public static void BuildAll()
        {
            EnsureFolder(ResourcesDir + "/WeaponTypes");
            EnsureFolder(ResourcesDir + "/ArmorTypes");
            EnsureFolder(ResourcesDir + "/Items");

            BaseEquipmentModelBonesSetupManager bonesSetup = BuildBonesSetup();
            BuildMissiles();
            BuildWeaponTypes();
            BuildArmorTypes();
            BuildWeapons();
            BuildShield(bonesSetup);
            BuildArmour(bonesSetup);
            BuildConsumables();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[{nameof(DemoItemBuilder)}] Built demo items.");
        }

        private static BaseEquipmentModelBonesSetupManager BuildBonesSetup()
        {
            var manager = AssetDatabase.LoadAssetAtPath<EquipmentModelBonesSetupByBoneNamesManager>(BonesSetupPath);
            if (manager == null)
            {
                manager = ScriptableObject.CreateInstance<EquipmentModelBonesSetupByBoneNamesManager>();
                AssetDatabase.CreateAsset(manager, BonesSetupPath);
            }
            // The outfit parts already share the character's root bone name, so their
            // own root can stay; only the bone array needs remapping.
            var serialized = new SerializedObject(manager);
            serialized.FindProperty("changeRootBone").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(manager);
            return manager;
        }

        // ---- types -----------------------------------------------------------

        private struct WeaponSpec
        {
            public string Name;
            public string Title;
            public WeaponItemEquipType EquipType;
            public float HitDistance;
            public float HitFov;
            public DamageType Damage;
            /// <summary>Prefab under MissileDir. Only read when Damage is Missile.</summary>
            public string Missile;
            public float MissileDistance;
            public float MissileSpeed;
        }

        // One type per class: the warrior swings, the ranger shoots, the mage casts. The axe
        // is nobody's class weapon — it is what the bandits carry, and a warrior can pick one
        // up and use it.
        private static readonly WeaponSpec[] WeaponSpecs =
        {
            new WeaponSpec { Name = "Sword", Title = "Sword", EquipType = WeaponItemEquipType.MainHandOnly,
                HitDistance = 2.4f, HitFov = 90f, Damage = DamageType.Melee },
            new WeaponSpec { Name = "Axe", Title = "Axe", EquipType = WeaponItemEquipType.MainHandOnly,
                HitDistance = 2.2f, HitFov = 80f, Damage = DamageType.Melee },
            new WeaponSpec { Name = "Bow", Title = "Bow", EquipType = WeaponItemEquipType.TwoHand,
                HitDistance = 2f, HitFov = 60f, Damage = DamageType.Missile,
                Missile = "ArrowMissile", MissileDistance = 18f, MissileSpeed = 38f },
            new WeaponSpec { Name = "Staff", Title = "Staff", EquipType = WeaponItemEquipType.TwoHand,
                HitDistance = 3.0f, HitFov = 70f, Damage = DamageType.Missile,
                Missile = "SpellBolt", MissileDistance = 14f, MissileSpeed = 22f },
            // Fists. The asset already existed but nothing authored it, so it sat on the kit's
            // defaults with the same half-metre `startAttackDistance` the sword had - a
            // character with nothing equipped could not reach anything either. Shorter and
            // narrower than a blade because it is an arm's length, not a weapon's.
            //
            // `hitOnlySelectedTarget` is left alone here: it is TRUE on this one, which is the
            // kit's own choice for fists and the builder does not write that field.
            new WeaponSpec { Name = "Unarmed", Title = "Unarmed", EquipType = WeaponItemEquipType.MainHandOnly,
                HitDistance = 1.5f, HitFov = 90f, Damage = DamageType.Melee },
        };

        private static void BuildWeaponTypes()
        {
            foreach (WeaponSpec spec in WeaponSpecs)
            {
                var type = Create<WeaponType>($"{ResourcesDir}/WeaponTypes/{spec.Name}.asset");
                var serialized = new SerializedObject(type);
                serialized.FindProperty("id").stringValue = spec.Name;
                serialized.FindProperty("defaultTitle").stringValue = spec.Title;
                serialized.FindProperty("equipType").enumValueIndex = (int)spec.EquipType;
                serialized.FindProperty("damageInfo.damageType").enumValueIndex = (int)spec.Damage;
                serialized.FindProperty("damageInfo.hitDistance").floatValue = spec.HitDistance;
                serialized.FindProperty("damageInfo.hitFov").floatValue = spec.HitFov;
                if (spec.Damage == DamageType.Missile)
                {
                    serialized.FindProperty("damageInfo.missileDistance").floatValue = spec.MissileDistance;
                    serialized.FindProperty("damageInfo.missileSpeed").floatValue = spec.MissileSpeed;
                    // Has to stay under the missile's own range or the kit recalculates it,
                    // and the character would walk into melee before loosing anything.
                    serialized.FindProperty("damageInfo.startAttackDistance").floatValue = spec.MissileDistance * 0.85f;
                    serialized.FindProperty("damageInfo.missileDamageEntity").objectReferenceValue =
                        Load<MissileDamageEntity>($"{MissileDir}/{spec.Missile}.prefab");
                }
                else
                {
                    // Melee needs this set every bit as much as a missile does, and leaving it
                    // out is why a warrior could not hit anything (found 2026-09-16).
                    //
                    // `Damage.GetDistance()` returns `min(hitDistance, startAttackDistance)`
                    // whenever the latter is above zero, and the kit's default is **0.5**. So a
                    // sword authored with 2.4m of reach was asked to attack only once the target
                    // was within half a metre of the blade - and it is measured from the weapon's
                    // damage transform, against the target's hit box. The character would walk up
                    // to an enemy, stop, and stand there never swinging. The same number is also
                    // the enemy detector's radius (`PlayerCharacterController_Inputs` line ~221),
                    // so nearby-enemy targeting barely reached past the character either.
                    //
                    // 0.85 of the reach, matching what the missiles use: far enough out to swing
                    // at arm's length, with enough margin that a moving target is still inside
                    // `hitDistance` when the blow actually lands.
                    serialized.FindProperty("damageInfo.startAttackDistance").floatValue = spec.HitDistance * 0.85f;
                }
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(type);
            }

            AdoptUnarmedIcon();
        }

        /// <summary>
        /// Puts the drawn fist on `DefaultWeaponItem`, which is the item a character holds
        /// when it holds nothing.
        ///
        /// **It is the one item in the demo no builder makes.** It came with the kit and
        /// was never adopted, so it sat with an empty `icon` while `Unarmed.png` sat in the
        /// icon folder - the same shape as the mana potion, and found the same way, by
        /// listing the icon folder against the item list. The Equipment Icon Generator
        /// cannot help: it photographs an item's model, and a fist has none.
        ///
        /// Only the icon is written. Its `id` is empty and its damage comes from the
        /// `Unarmed` weapon type above, and both of those are the kit's arrangement rather
        /// than a gap - adopting it further would mean owning an asset the demo did not
        /// author.
        /// </summary>
        private static void AdoptUnarmedIcon()
        {
            var unarmed = AssetDatabase.LoadAssetAtPath<BaseItem>($"{ResourcesDir}/Items/DefaultWeaponItem.asset");
            if (unarmed == null)
            {
                Debug.LogWarning($"[{nameof(DemoItemBuilder)}] No DefaultWeaponItem to put the fist on.");
                return;
            }
            var serialized = new SerializedObject(unarmed);
            AdoptItemIcon(serialized, "Unarmed");
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(unarmed);
        }

        /// <summary>
        /// Builds the projectiles the ranged weapons fire.
        ///
        /// A missile weapon is inert without one: the kit reads `missileDamageEntity` off the
        /// weapon type and spawns it, so a bow with that field left empty draws and fires
        /// nothing at all. Neither the kit nor the demo shipped a projectile, but the arrow
        /// model was already built and sitting unused beside the bow.
        /// </summary>
        /// <summary>
        /// The things that fly. One per use rather than one shared arrow, because a shot
        /// in flight is the only tell a player gets between a skill and an ordinary
        /// attack: the bow's plain shot is pale, an Aimed Shot is bright, a Crippling Shot
        /// is green and a Hunter's Mark is amber. All four are the same arrow mesh — it is
        /// the streak that differs, which is what is actually visible at twenty metres.
        /// </summary>
        private static void BuildMissiles()
        {
            EnsureFolder(MissileDir);
            string arrow = EquipmentDir + "/Arrow.prefab";
            BuildMissile("ArrowMissile", arrow, 0.05f, DemoSkillEffectBuilder.ArrowPlain, 0.07f);
            BuildMissile("ArrowAimed", arrow, 0.05f, DemoSkillEffectBuilder.ArrowAimed, 0.10f);
            BuildMissile("ArrowCrippling", arrow, 0.05f, DemoSkillEffectBuilder.ArrowCrippling, 0.10f);
            BuildMissile("ArrowMark", arrow, 0.05f, DemoSkillEffectBuilder.ArrowMark, 0.10f);
            // No mesh: the core is what you see, and it is two additive billboards rather
            // than the lit primitive sphere this used to be.
            BuildMissile("SpellBolt", null, 0.14f, DemoSkillEffectBuilder.Arcane, 0.16f, core: 0.20f);
        }

        private static void BuildMissile(string name, string modelPath, float radius,
                                         Color trailColour, float trailWidth, float core = 0f)
        {
            var root = new GameObject(name);
            if (modelPath != null)
            {
                GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
                if (source == null)
                {
                    Debug.LogError($"[{nameof(DemoItemBuilder)}] Missing missile model \"{modelPath}\".");
                }
                else
                {
                    var model = (GameObject)PrefabUtility.InstantiatePrefab(source);
                    PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                    model.transform.SetParent(root.transform, false);
                    model.name = "Model";
                    // The arrow is modelled point-up along +Y and a missile travels along its
                    // own +Z, so it has to be tipped over to fly point first.
                    model.transform.localEulerAngles = new Vector3(90f, 0f, 0f);
                }
            }

            var missile = root.AddComponent<MissileDamageEntity>();
            missile.hitLayers = HitLayers;
            missile.sphereCastRadius = radius;
            missile.destroyDelay = 0f;

            // After the missile component, not before: the trail hooks its own Clear onto
            // that component's `onGetInstance`, and without it a pooled shot draws a
            // ribbon from wherever the previous one died.
            DemoSkillEffectBuilder.AddMissileDressing(root, trailColour, trailWidth, core);

            string path = MissileDir + "/" + name + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }


        private static void BuildArmorTypes()
        {
            foreach (string slot in ArmourSlots)
            {
                var type = Create<ArmorType>($"{ResourcesDir}/ArmorTypes/{slot}.asset");
                var serialized = new SerializedObject(type);
                serialized.FindProperty("id").stringValue = slot;
                serialized.FindProperty("defaultTitle").stringValue = slot;
                serialized.FindProperty("equipPosition").stringValue = slot;
                serialized.FindProperty("equippableSlots").intValue = 1;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(type);
            }
        }

        // ---- items -----------------------------------------------------------

        private struct ItemSpec
        {
            public string Name;
            public string Title;
            public string Description;
            public string Model;
            public string TypeAsset;
            public float Min;
            public float Max;
            public int Price;
            public float Weight;
        }

        private static readonly ItemSpec[] WeaponItems =
        {
            new ItemSpec { Name = "IronLongsword", Title = "Iron Longsword", Description = "A plain soldier's blade, kept sharp.",
                Model = "Longsword", TypeAsset = "Sword", Min = 12f, Max = 18f, Price = 120, Weight = 3.5f },
            new ItemSpec { Name = "IronShortsword", Title = "Iron Shortsword", Description = "Short, quick, and easy to carry.",
                Model = "ShortSword", TypeAsset = "Sword", Min = 8f, Max = 12f, Price = 60, Weight = 2f },
            new ItemSpec { Name = "BanditAxe", Title = "Bandit Axe", Description = "Taken from the camp on the headland.",
                Model = "Axe", TypeAsset = "Axe", Min = 10f, Max = 16f, Price = 80, Weight = 3f },
            new ItemSpec { Name = "ApprenticeStaff", Title = "Apprentice Staff", Description = "The stone at its tip is still warm.",
                Model = "MageStaff", TypeAsset = "Staff", Min = 9f, Max = 14f, Price = 100, Weight = 2.5f },
            new ItemSpec { Name = "HuntingBow", Title = "Hunting Bow", Description = "Ash and sinew. Made for deer, not for men.",
                Model = "Bow", TypeAsset = "Bow", Min = 7f, Max = 11f, Price = 70, Weight = 1.8f },
            new ItemSpec { Name = "YewLongbow", Title = "Yew Longbow", Description = "Taller than the archer, and slow to draw.",
                Model = "Bow", TypeAsset = "Bow", Min = 13f, Max = 19f, Price = 150, Weight = 2.4f },
            new ItemSpec { Name = "ElderStaff", Title = "Elder Staff", Description = "Cut from a tree that was old when the island was settled.",
                Model = "MageStaff", TypeAsset = "Staff", Min = 14f, Max = 20f, Price = 160, Weight = 3f },
        };

        private static void BuildWeapons()
        {
            foreach (ItemSpec spec in WeaponItems)
            {
                var item = Create<WeaponItem>($"{ResourcesDir}/Items/{spec.Name}.asset");
                var serialized = new SerializedObject(item);
                WriteCommon(serialized, spec);
                serialized.FindProperty("weaponType").objectReferenceValue =
                    Load<WeaponType>($"{ResourcesDir}/WeaponTypes/{spec.TypeAsset}.asset");
                serialized.FindProperty("damageAmount.amount.baseAmount.min").floatValue = spec.Min;
                serialized.FindProperty("damageAmount.amount.baseAmount.max").floatValue = spec.Max;
                serialized.FindProperty("damageAmount.amount.amountIncreaseEachLevel.min").floatValue = spec.Min * 0.35f;
                serialized.FindProperty("damageAmount.amount.amountIncreaseEachLevel.max").floatValue = spec.Max * 0.35f;
                // Weapons are rigid props hung off the hand socket, not skinned.
                //
                // A bow is the exception, and it has to be the OFF hand. The archery clips
                // hold the bow out in the left hand and draw the string with the right, so
                // hanging it off the right socket like every other weapon put it in the
                // drawing hand - sticking up behind the head while the left hand reached
                // forward holding nothing. The item is still a right-hand weapon as far as
                // the kit is concerned; only the visual model moves.
                // A bow is drawn and held, not clicked. The shooter controller starts a
                // charge only for a weapon that fires on release, and that one flag is what
                // turns "press to shoot" into "hold to draw, let go to loose" - the draw
                // animation is never played without it. Everything else fires on press.
                // ...but only the shooter controller ever starts that charge. The target-based
                // controller the demo plays through now (see DemoControllerBuilder) never
                // does, and a fire-on-release weapon that was never charged simply does not
                // fire - the bow went dead in the ranger's hands. So bows fire on press
                // unless the shooter is what is being built.
                serialized.FindProperty("fireType").enumValueIndex =
                    (int)(spec.TypeAsset == "Bow" && BowsCharge ? FireType.FireOnRelease : FireType.SingleFire);

                bool isBow = spec.TypeAsset == "Bow";
                string socket = isBow ? SocketLeftHand : SocketRightHand;
                Vector3 facing = IsBladed(spec.TypeAsset) ? BladeFacing : isBow ? BowFacing : Vector3.zero;
                Vector3 seat = isBow ? BowSeat : Vector3.zero;
                Vector3 gripPosition, gripEuler, gripScale;
                ResolveGrip(spec.Name, facing, seat, out gripPosition, out gripEuler, out gripScale);
                WriteModel(serialized, "equipmentModels", socket, $"{EquipmentDir}/{spec.Model}.prefab", null,
                           gripEuler, gripPosition, gripScale);
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(item);
                DemoAudioWiring.WireWeaponItem(item);
            }
        }

        /// <summary>
        /// Roll about the shield's grip bar, turning its face toward what the character faces.
        /// A round shield is centre-gripped, so the bar is the only thing the hand fixes -
        /// rolling about it is the one adjustment that does not slide the shield off the fist.
        ///
        /// **This is a partial fix and cannot be made exact.** The demo has no shield-bearing
        /// animation: the left hand in `Sword_Idle` is a sword-guard hand, turned palm-in
        /// across the chest, and the grip bar ends up 41.9 degrees off the body's forward. A
        /// rigid shield can therefore never face nearer than **48 degrees** off in that pose,
        /// whatever roll is applied - the residual is the bar's own tilt, which roll cannot
        /// touch. This value takes the guard pose from 82 degrees off down to that 48-degree
        /// floor; locomotion poses vary because the left arm swings freely in them.
        ///
        /// The real fix is a shield-bearing clip (Mixamo has them, and that is how the bow got
        /// real archery). Drop one in `Assets/Animations`, wire it as the shield idle, and
        /// re-measure this: the floor moves with the pose.
        /// </summary>
        private static readonly Vector3 ShieldFacing = new Vector3(0f, 70f, 0f);

        /// <summary>
        /// Roll about a blade's own length, so the **edge** leads a swing instead of the flat.
        ///
        /// Every weapon prefab is built with its length on +Y and its flat facing Z, which
        /// left the blades slapping rather than cutting: measured against the swing direction
        /// of the blade tip, `Sword_Attack_Standing` was **88 degrees off the edge at peak
        /// speed** (i.e. almost exactly flat-on) and `Sword_Attack` 58 degrees. Rolling 80
        /// degrees brings those to 21 and 29, and does the same for the axe and short sword
        /// (55 -> 31 and 54 -> 31).
        ///
        /// It is 80 rather than a round 90 because the Quaternius swings are not planar
        /// cuts - the wrist rolls through the arc - so this is the value that minimises the
        /// speed-weighted error across both attack clips rather than squaring up one frame.
        /// Re-measure if the attack animations are ever replaced.
        ///
        /// Staves are left at zero: a staff has no edge. Bows have their own, below.
        /// </summary>
        private static readonly Vector3 BladeFacing = new Vector3(0f, 80f, 0f);

        /// <summary>
        /// Roll and seat for a bow, so that the string faces the archer instead of the target.
        ///
        /// Every weapon prefab is built with its length on +Y and its flat facing Z, which for
        /// a bow means the plane holding the string and both limbs starts out **square to the
        /// draw**: measured on the archery clips, the bow's flat pointed down the character's
        /// forward and the limbs bowed out to the character's left, so the drawing hand ended
        /// up 0.70m off the side of the bow's plane rather than behind the string. Rolled, the
        /// same hand sits 0.63m straight back along the string's own pull axis and within 2cm
        /// of the plane - which is the difference between a string that can be drawn and one
        /// that cannot.
        ///
        /// These are the numbers captured for <c>HuntingBow</c> in the AnimationEditing scene,
        /// promoted from that one item to the default for the class. They were the only bow
        /// grip that had ever been tuned, so <c>YewLongbow</c> was still being held flat-on.
        /// A capture in <see cref="DemoWeaponGripOverrides"/> still wins over this, as always.
        /// </summary>
        private static readonly Vector3 BowFacing = new Vector3(12.474f, 79.6f, 3.744f);

        /// <summary>Where the bow's grip sits in the fist, alongside <see cref="BowFacing"/>.</summary>
        private static readonly Vector3 BowSeat = new Vector3(-0.0142f, -0.0309f, 0.0873f);

        /// <summary>
        /// The grip to write for an item: a grip captured from the AnimationEditing scene when
        /// one exists, otherwise this builder's own default orientation for that weapon class.
        ///
        /// Captured values live in <see cref="DemoWeaponGripOverrides"/> rather than on the item
        /// asset, because this builder rewrites `equipmentModels` from scratch every run - an
        /// offset typed onto the asset would last exactly until the next `Build Items`. Tune a
        /// grip in the scene, run "Save Weapon Grips From Scene", and it survives from then on.
        ///
        /// An entry replaces the default orientation outright rather than adding to it, so the
        /// captured transform is the whole grip. Delete the entry to hand the item back to
        /// <see cref="BladeFacing"/> / <see cref="ShieldFacing"/>.
        /// </summary>
        private static void ResolveGrip(string itemName, Vector3 defaultEuler, Vector3 defaultPosition,
                                        out Vector3 localPosition, out Vector3 localEuler, out Vector3 localScale)
        {
            localPosition = defaultPosition;
            localEuler = defaultEuler;
            localScale = Vector3.one;

            var overrides = AssetDatabase.LoadAssetAtPath<DemoWeaponGripOverrides>(
                DemoWeaponGripOverrides.AssetPath);
            if (overrides == null)
                return;
            DemoWeaponGripOverrides.Entry entry = overrides.Find(itemName);
            if (entry == null)
                return;

            localPosition = entry.localPosition;
            localEuler = entry.localEulerAngles;
            localScale = entry.localScale;
        }

        /// <summary>Weapons with a cutting edge, which care which way round the blade sits.</summary>
        private static bool IsBladed(string weaponType)
        {
            return weaponType == "Sword" || weaponType == "Axe";
        }

        private static void BuildShield(BaseEquipmentModelBonesSetupManager bonesSetup)
        {
            var item = Create<ShieldItem>($"{ResourcesDir}/Items/PaintedRoundShield.asset");
            var serialized = new SerializedObject(item);
            WriteCommon(serialized, new ItemSpec
            {
                Name = "PaintedRoundShield",
                Title = "Painted Round Shield",
                Description = "Limewood boards behind an iron boss, painted with coiling beasts.",
                Price = 90,
                Weight = 4f,
            });
            serialized.FindProperty("armorAmount.amount.baseAmount").floatValue = 6f;
            serialized.FindProperty("armorAmount.amount.amountIncreaseEachLevel").floatValue = 1.2f;
            Vector3 shieldPosition, shieldEuler, shieldScale;
            ResolveGrip("PaintedRoundShield", ShieldFacing, Vector3.zero, out shieldPosition, out shieldEuler, out shieldScale);
            WriteModel(serialized, "equipmentModels", SocketLeftHand, $"{EquipmentDir}/VikingShield.prefab", null,
                       shieldEuler, shieldPosition, shieldScale);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(item);
        }

        private struct ArmourSpec
        {
            public string Name;
            public string Title;
            public string Slot;
            public string Model;
            public float Armour;
            public int Price;
        }

        private static readonly ArmourSpec[] ArmourItems =
        {
            new ArmourSpec { Name = "PeasantTunic", Title = "Peasant Tunic", Slot = "Body", Model = "Male_Peasant_Body", Armour = 3f, Price = 25 },
            new ArmourSpec { Name = "PeasantSleeves", Title = "Peasant Sleeves", Slot = "Arms", Model = "Male_Peasant_Arms", Armour = 1f, Price = 15 },
            new ArmourSpec { Name = "PeasantTrousers", Title = "Peasant Trousers", Slot = "Legs", Model = "Male_Peasant_Legs", Armour = 2f, Price = 20 },
            new ArmourSpec { Name = "PeasantShoes", Title = "Peasant Shoes", Slot = "Feet", Model = "Male_Peasant_Feet", Armour = 1f, Price = 12 },

            new ArmourSpec { Name = "RangerHood", Title = "Ranger Hood", Slot = "Head", Model = "Male_Ranger_Head_Hood", Armour = 4f, Price = 70 },
            new ArmourSpec { Name = "RangerJerkin", Title = "Ranger Jerkin", Slot = "Body", Model = "Male_Ranger_Body", Armour = 8f, Price = 140 },
            new ArmourSpec { Name = "RangerBracers", Title = "Ranger Bracers", Slot = "Arms", Model = "Male_Ranger_Arms", Armour = 4f, Price = 65 },
            new ArmourSpec { Name = "RangerBreeches", Title = "Ranger Breeches", Slot = "Legs", Model = "Male_Ranger_Legs", Armour = 6f, Price = 95 },
            new ArmourSpec { Name = "RangerBoots", Title = "Ranger Boots", Slot = "Feet", Model = "Male_Ranger_Feet_Boots", Armour = 4f, Price = 60 },
            new ArmourSpec { Name = "RangerPauldron", Title = "Ranger Pauldron", Slot = "Pauldron", Model = "Male_Ranger_Acc_Pauldron", Armour = 3f, Price = 55 },

            new ArmourSpec { Name = "KnightHelm", Title = "Knight Armet", Slot = "Head", Model = "Male_Knight_Head_Armet", Armour = 7f, Price = 150 },
            new ArmourSpec { Name = "KnightCuirass", Title = "Knight Cuirass", Slot = "Body", Model = "Male_Knight_Body_Armor", Armour = 14f, Price = 280 },
            new ArmourSpec { Name = "KnightGauntlets", Title = "Knight Gauntlets", Slot = "Arms", Model = "Male_Knight_Arms", Armour = 6f, Price = 120 },
            new ArmourSpec { Name = "KnightGreaves", Title = "Knight Greaves", Slot = "Legs", Model = "Male_Knight_Legs_Armor", Armour = 10f, Price = 190 },
            new ArmourSpec { Name = "KnightSabatons", Title = "Knight Sabatons", Slot = "Feet", Model = "Male_Knight_Feet_Armor", Armour = 6f, Price = 110 },
            new ArmourSpec { Name = "KnightPauldrons", Title = "Knight Pauldrons", Slot = "Pauldron", Model = "Male_Knight_Acc_Pauldron_Round", Armour = 5f, Price = 105 },

            new ArmourSpec { Name = "WizardRobe", Title = "Wizard Robe", Slot = "Body", Model = "Male_Wizard_Body", Armour = 7f, Price = 160 },
            new ArmourSpec { Name = "WizardSleeves", Title = "Wizard Sleeves", Slot = "Arms", Model = "Male_Wizard_Arms", Armour = 3f, Price = 70 },
            new ArmourSpec { Name = "WizardTrousers", Title = "Wizard Trousers", Slot = "Legs", Model = "Male_Wizard_Legs", Armour = 5f, Price = 100 },
            new ArmourSpec { Name = "WizardShoes", Title = "Wizard Shoes", Slot = "Feet", Model = "Male_Wizard_Feet", Armour = 3f, Price = 65 },
        };

        private static void BuildArmour(BaseEquipmentModelBonesSetupManager bonesSetup)
        {
            foreach (ArmourSpec spec in ArmourItems)
            {
                var item = Create<ArmorItem>($"{ResourcesDir}/Items/{spec.Name}.asset");
                var serialized = new SerializedObject(item);
                WriteCommon(serialized, new ItemSpec
                {
                    Name = spec.Name,
                    Title = spec.Title,
                    Description = $"Worn on the {spec.Slot.ToLowerInvariant()}.",
                    Price = spec.Price,
                    Weight = 1.5f,
                });
                serialized.FindProperty("armorType").objectReferenceValue =
                    Load<ArmorType>($"{ResourcesDir}/ArmorTypes/{spec.Slot}.asset");
                serialized.FindProperty("armorAmount.amount.baseAmount").floatValue = spec.Armour;
                serialized.FindProperty("armorAmount.amount.amountIncreaseEachLevel").floatValue = spec.Armour * 0.2f;
                // Skinned, so it is parented to the model root and rebound by bone name.
                WriteModel(serialized, "equipmentModels", spec.Slot, $"{OutfitDir}/{spec.Model}.fbx", bonesSetup);
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(item);
            }
        }

        private static void BuildConsumables()
        {
            var potion = Create<PotionItem>($"{ResourcesDir}/Items/MinorHealingPotion.asset");
            var serialized = new SerializedObject(potion);
            WriteCommon(serialized, new ItemSpec
            {
                Name = "MinorHealingPotion",
                Title = "Minor Healing Potion",
                Description = "Restores a little health at once.",
                Price = 20,
                Weight = 0.2f,
            });
            serialized.FindProperty("maxStack").intValue = 20;
            serialized.FindProperty("buff.recoveryHp.baseAmount").intValue = 60;
            serialized.FindProperty("useItemCooldown").floatValue = 2f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(potion);

            // The other half of the pair, and a hole nobody noticed until the icons were
            // counted: `ManaPotion.png` had been drawn and there was no item to put it on,
            // so a mage who ran dry had nothing to drink but spiced wine at 6 mana a cup.
            // Priced above the healing one because mana is the scarcer of the two on this
            // island - there is no mana-bearing food at all.
            var mana = Create<PotionItem>($"{ResourcesDir}/Items/MinorManaPotion.asset");
            var manaSerialized = new SerializedObject(mana);
            WriteCommon(manaSerialized, new ItemSpec
            {
                Name = "MinorManaPotion",
                Title = "Minor Mana Potion",
                Description = "Restores a little of whatever a caster spends.",
                Price = 25,
                Weight = 0.2f,
            });
            manaSerialized.FindProperty("maxStack").intValue = 20;
            manaSerialized.FindProperty("buff.recoveryMp.baseAmount").intValue = 40;
            manaSerialized.FindProperty("useItemCooldown").floatValue = 2f;
            AdoptItemIcon(manaSerialized, "ManaPotion");
            manaSerialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(mana);

            BuildProvisions();

            // One trophy per enemy family, because a cultist carrying a *bandit's* insignia
            // reads as a mistake - and until 2026-09-16 every human on the island dropped the
            // bandit one. Each is the common drop for its own family and the thing the merchant
            // buys; they are priced by how hard the family is to kill.
            var trophies = new[]
            {
                new ItemSpec { Name = "BanditInsignia", Title = "Bandit Insignia",
                    Description = "A crude token the camp's crew wear. Proof of a kill.",
                    Price = 15, Weight = 0.1f },
                new ItemSpec { Name = "MarauderSeal", Title = "Marauder's Seal",
                    Description = "Cast lead on a broken chain, stamped with a mailed fist. The heavies wear them openly.",
                    Price = 28, Weight = 0.15f },
                new ItemSpec { Name = "CultistSigil", Title = "Cultist's Sigil",
                    Description = "A ring of scratched bone, still warm. Whatever it means, they all carry one.",
                    Price = 22, Weight = 0.1f },
            };
            foreach (ItemSpec trophy in trophies)
            {
                var junk = Create<JunkItem>($"{ResourcesDir}/Items/{trophy.Name}.asset");
                var junkSerialized = new SerializedObject(junk);
                WriteCommon(junkSerialized, trophy);
                junkSerialized.FindProperty("maxStack").intValue = 50;
                junkSerialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(junk);
            }

            BuildQuarry();
        }

        /// <summary>
        /// What comes off the island's animals, hunted or fought.
        ///
        /// The deer are the one thing on the map worth killing that is not trying to kill
        /// you, and this is the point of doing it: both parts sell, so hunting is a living
        /// for a character who would rather not fight the bandit camp, and the venison is
        /// worth a little more than the hide because it is the part that spoils.
        ///
        /// The wolves are the opposite trade - they pick the fight - so what they leave is
        /// a consolation rather than a wage, and priced below a deer's.
        /// </summary>
        private static void BuildQuarry()
        {
            var quarry = new[]
            {
                new ItemSpec { Name = "Venison", Title = "Venison",
                    Description = "A cut of deer meat, still cold. The alehouse will take it.",
                    Price = 22, Weight = 0.8f },
                new ItemSpec { Name = "DeerHide", Title = "Deer Hide",
                    Description = "A whole hide, rolled and tied. Worth curing.",
                    Price = 16, Weight = 1.2f },
                // What comes off a wolf. Worth rather less than a deer between them: a wolf
                // is the fight you are given rather than the one you go looking for, and
                // paying well for it would make the village ring the better hunting ground.
                new ItemSpec { Name = "WolfPelt", Title = "Wolf Pelt",
                    Description = "Grey and coarse, still smelling of the moor. The tanner will take it.",
                    Price = 18, Weight = 1.0f },
                new ItemSpec { Name = "WolfFang", Title = "Wolf Fang",
                    Description = "Longer than a finger joint and just as thick. They are strung and sold as charms.",
                    Price = 9, Weight = 0.1f },
            };
            foreach (ItemSpec spec in quarry)
            {
                var item = Create<JunkItem>($"{ResourcesDir}/Items/{spec.Name}.asset");
                var serialized = new SerializedObject(item);
                WriteCommon(serialized, spec);
                serialized.FindProperty("maxStack").intValue = 20;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(item);
            }
        }

        /// <summary>Something the alehouse sells: what it does for you, and for how long.</summary>
        private struct Provision
        {
            public string Name, Title, Description;
            public int Price;
            public float Weight;
            /// <summary>Recovered every second the meal lasts.</summary>
            public int Hp, Mp, Stamina, Food, Water;
            /// <summary>How long it lasts, in seconds.</summary>
            public float Seconds;
        }

        /// <summary>
        /// The alehouse's food and drink. All of them are potions to the kit - a buff
        /// applied on use - but where the healing potion is a burst, a meal is slow: the
        /// buff has a duration, and the recovery is paid out every second of it, so bread
        /// mends you over a quarter of a minute and a stew over a fair bit more. Food and
        /// water recovery are set as well, for a project that turns those stats on; the
        /// demo does not, and they cost nothing while it does not.
        ///
        /// No icons: the user draws those. The assets are at Items/&lt;Name&gt;.asset.
        /// </summary>
        private static readonly Provision[] Provisions =
        {
            new Provision { Name = "Bread", Title = "Loaf of Bread", Price = 8, Weight = 0.3f,
                Description = "Still warm from the alehouse oven. Mends you slowly while it goes down.",
                Hp = 4, Food = 10, Seconds = 15f },
            new Provision { Name = "Cheese", Title = "Wedge of Cheese", Price = 12, Weight = 0.3f,
                Description = "Sharp and salty. Keeps the legs going.",
                Hp = 3, Stamina = 4, Food = 8, Seconds = 15f },
            new Provision { Name = "Stew", Title = "Hearty Stew", Price = 18, Weight = 0.5f,
                Description = "Mutton, roots and a whole day's simmering. The best thing on the island.",
                Hp = 10, Food = 25, Seconds = 12f },
            new Provision { Name = "Ale", Title = "Mug of Ale", Price = 6, Weight = 0.4f,
                Description = "Brewed in the barrels behind the counter. Cloudy, strong and cheap.",
                Stamina = 8, Water = 15, Seconds = 10f },
            new Provision { Name = "SpicedWine", Title = "Spiced Wine", Price = 15, Weight = 0.4f,
                Description = "Warmed with cloves. Clears the head, or feels as though it does.",
                Mp = 6, Water = 10, Seconds = 10f },
        };

        private static void BuildProvisions()
        {
            foreach (Provision provision in Provisions)
            {
                var item = Create<PotionItem>($"{ResourcesDir}/Items/{provision.Name}.asset");
                var serialized = new SerializedObject(item);
                WriteCommon(serialized, new ItemSpec
                {
                    Name = provision.Name,
                    Title = provision.Title,
                    Description = provision.Description,
                    Price = provision.Price,
                    Weight = provision.Weight,
                });
                serialized.FindProperty("maxStack").intValue = 10;
                serialized.FindProperty("useItemCooldown").floatValue = 1.5f;
                serialized.FindProperty("buff.duration.baseAmount").floatValue = provision.Seconds;
                serialized.FindProperty("buff.recoveryHp.baseAmount").intValue = provision.Hp;
                serialized.FindProperty("buff.recoveryMp.baseAmount").intValue = provision.Mp;
                serialized.FindProperty("buff.recoveryStamina.baseAmount").intValue = provision.Stamina;
                serialized.FindProperty("buff.recoveryFood.baseAmount").intValue = provision.Food;
                serialized.FindProperty("buff.recoveryWater.baseAmount").intValue = provision.Water;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(item);
            }
        }

        /// <summary>The names of everything the alehouse sells, for its shop dialog.</summary>
        public static string[] ProvisionNames()
        {
            var names = new string[Provisions.Length];
            for (int i = 0; i < Provisions.Length; ++i)
                names[i] = Provisions[i].Name;
            return names;
        }

        // ---- helpers ---------------------------------------------------------

        // ---- upkeep: durability, repair, refine and dismantle -----------------

        /// <summary>
        /// Gear wears out, which is what gives the island's smith something to do.
        ///
        /// Every piece of equipment shipped with `maxDurability: 0` until 2026-09-22, and
        /// zero does not mean "very tough" - it means the whole system is switched off.
        /// `DefaultGameplayRule` already decreases durability on every blow landed and
        /// taken (0.5 a hit off a weapon, 0.5 off a shield, 0.1 off each armour piece) and
        /// `GetEquipmentStatsRate` already steps what a worn item is worth **down** long
        /// before it breaks: full value above half durability, then 75%, 50%, 25%, and
        /// nothing at all under 5%. All of that was dead code against a max of zero.
        ///
        /// **Nothing is destroyed.** `destroyIfBroken` stays false, so a broken sword is a
        /// useless sword and not a lost one - the right trade for a demo, where losing the
        /// thing you spent the island's gold on is a story nobody wants to tell.
        ///
        /// **A step of its own, and not part of `Build Items`,** because gear dismantles
        /// into the island's materials and refines with its stone - and `Stone` and
        /// `Timber` are built by `Build Harvestables` while `Leather` comes from
        /// `Build Progression`, both of which run *after* the items do. Written from here
        /// the references would be null on a clean rebuild, silently, and the smith would
        /// hand back nothing. Re-running `Build Items` does not undo this: it writes only
        /// the fields it owns and leaves the rest of each asset alone.
        /// </summary>
        [MenuItem("Open MMORPG/Demo/Build Gear Upkeep", priority = 153)]
        public static void BuildUpkeep()
        {
            foreach (string material in new[] { ScrapMetal, ScrapWood, ScrapHide })
            {
                if (LoadItem(material) != null)
                    continue;
                Debug.LogError($"[{nameof(DemoItemBuilder)}] No \"{material}\" item. Run Build Items, " +
                               "Build Harvestables and Build Progression first - gear is dismantled into " +
                               "the materials those steps create.");
                return;
            }

            BuildItemRefines();

            int written = 0;
            foreach (ItemSpec spec in WeaponItems)
                written += WriteUpkeep(spec.Name, spec.Price, WeaponDurability(spec), WeaponScrap(spec.TypeAsset)) ? 1 : 0;
            // The shield is hit like armour and worn out like a weapon: half a point every
            // blow it takes, three quarters of one for a blow it blocks.
            written += WriteUpkeep("PaintedRoundShield", 90, 150f, ScrapWood) ? 1 : 0;
            foreach (ArmourSpec spec in ArmourItems)
                written += WriteUpkeep(spec.Name, spec.Price, ArmourDurability(spec.Armour), ArmourScrap(spec.Model)) ? 1 : 0;

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[{nameof(DemoItemBuilder)}] {written} pieces of gear can now wear out, be repaired, " +
                      "refined and dismantled.");
        }

        /// <summary>
        /// What a blade or a bow breaks down into. Steel and stone on one side, wood and
        /// horn on the other - the demo has no ingot, so `Stone` is its metal.
        /// </summary>
        private static string WeaponScrap(string weaponType)
        {
            return weaponType == "Bow" || weaponType == "Staff" ? ScrapWood : ScrapMetal;
        }

        /// <summary>
        /// What a piece of armour breaks down into, read off the outfit it is cut from:
        /// the knight's plate is metal and everything else on the island is cloth or hide.
        /// </summary>
        private static string ArmourScrap(string model)
        {
            return model.Contains("Knight") ? ScrapMetal : ScrapHide;
        }

        private static bool WriteUpkeep(string name, int price, float durability, string scrap)
        {
            var item = Load<BaseItem>($"{ResourcesDir}/Items/{name}.asset");
            if (item == null)
            {
                Debug.LogWarning($"[{nameof(DemoItemBuilder)}] No item \"{name}\" to give durability to.");
                return false;
            }
            var serialized = new SerializedObject(item);
            serialized.FindProperty("maxDurability").floatValue = durability;
            serialized.FindProperty("destroyIfBroken").boolValue = false;

            // **The trap: durability alone does not make a thing repairable.** Repair
            // prices live on the `ItemRefine` asset, not on the item, so an item with a
            // max durability and no refine asset wears out and then cannot be mended -
            // `TryGetRepairPrice` returns `UI_ERROR_INVALID_DATA` and the smith's window
            // shows an item he will not touch, with nothing on screen to say why.
            serialized.FindProperty("itemRefine").objectReferenceValue = RefineFor(price);

            // A smith breaks gear down for the material in it; Marek pays for what he can
            // resell. So dismantling returns stuff and selling returns coin, and the two
            // are worth reaching for at different times.
            serialized.FindProperty("dismantleReturnGold").intValue = 0;
            SerializedProperty returns = serialized.FindProperty("dismantleReturnItems");
            BaseItem material = string.IsNullOrEmpty(scrap) ? null : LoadItem(scrap);
            if (material == null)
            {
                returns.arraySize = 0;
            }
            else
            {
                returns.arraySize = 1;
                SerializedProperty entry = returns.GetArrayElementAtIndex(0);
                entry.FindPropertyRelative("item").objectReferenceValue = material;
                // Counted rather than valued, and capped. Paying out a third of the
                // item's price in material looks fair until you notice stone sells for
                // three gold: a knight's cuirass came back as **thirty-one stone**, ten
                // recipes' worth, which makes an afternoon at the quarry pointless. A
                // better piece should yield more and no piece should yield a stockpile.
                entry.FindPropertyRelative("amount").intValue =
                    Mathf.Clamp(1 + price / 40, 1, 8);
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(item);
            return true;
        }

        /// <summary>What a piece of gear breaks down into: metal, wood or hide.</summary>
        private const string ScrapMetal = "Stone";
        private const string ScrapWood = "Timber";
        private const string ScrapHide = "Leather";

        /// <summary>
        /// How tough a weapon is: better blades hold an edge longer.
        ///
        /// At half a point a blow, ninety plus five a damage point is something like three
        /// hundred swings for the shortsword and close to four for the elder staff - a
        /// couple of trips out to the headland and back, so a player meets the smith once
        /// or twice over the island rather than every evening or never.
        /// </summary>
        private static float WeaponDurability(ItemSpec spec)
        {
            return 90f + spec.Max * 5f;
        }

        /// <summary>
        /// How tough a piece of armour is.
        ///
        /// Much lower than a weapon's, because armour wears a fifth as fast - a tenth of a
        /// point per blow **received**, and the same tenth off every piece worn at once. At
        /// these numbers a full set comes out of the crypt visibly worn rather than
        /// untouched, and the peasant rags wear through faster than the knight's plate
        /// because the rate is flat and their pool is smaller, which is the right way round.
        /// </summary>
        private static float ArmourDurability(float armour)
        {
            return 40f + armour * 2f;
        }

        /// <summary>
        /// The three grades of workmanship, and what a smith charges to put each right.
        ///
        /// One asset per grade rather than one per item: `ItemRefine` carries flat gold
        /// amounts, not a rate, so everything sharing an asset is repaired for the same
        /// price - which is fine for three bands and wrong for one shared asset.
        /// </summary>
        private struct RefineGrade
        {
            public string Name;
            /// <summary>Everything priced under this belongs to the grade above it in the table.</summary>
            public int UnderPrice;
            /// <summary>What a full repair costs at the worst band; the easier bands scale off it.</summary>
            public int RepairGold;
            /// <summary>Gold for each step of refinement, from +1 upward.</summary>
            public int[] RefineGold;
            /// <summary>Stone for each step, ground away on the wheel.</summary>
            public int[] RefineStone;
        }

        private static readonly RefineGrade[] RefineGrades =
        {
            new RefineGrade { Name = "PlainGear", UnderPrice = 80, RepairGold = 30,
                RefineGold = new[] { 40, 110, 260 }, RefineStone = new[] { 2, 5, 9 } },
            new RefineGrade { Name = "FineGear", UnderPrice = 160, RepairGold = 70,
                RefineGold = new[] { 90, 240, 520 }, RefineStone = new[] { 4, 9, 16 } },
            new RefineGrade { Name = "MasterworkGear", UnderPrice = int.MaxValue, RepairGold = 140,
                RefineGold = new[] { 180, 460, 980 }, RefineStone = new[] { 7, 15, 26 } },
        };

        /// <summary>
        /// What a repair costs, by how far gone the thing is.
        ///
        /// Each entry's rate is the **top** of a band, not the bottom: `TryGetRepairPrice`
        /// sorts the list high to low and returns the first entry whose rate the item is
        /// still under, so the 1.0 entry prices anything from three quarters up and the
        /// 0.25 entry prices a wreck. Hence the prices run the other way from the rates.
        /// </summary>
        private static readonly float[] RepairBands = { 1f, 0.75f, 0.5f, 0.25f };
        private static readonly float[] RepairBandCost = { 0.3f, 0.55f, 0.8f, 1f };

        /// <summary>
        /// Three steps of refinement and no more.
        ///
        /// Item level is what refining raises, and the item builder already gives a weapon
        /// **35% of its base damage per level** and armour 20% of its base - so +3 is a
        /// blade worth twice what it was. That is the lever to pull if this ever wants
        /// retuning, and the reason the ladder is short and the gold steep.
        ///
        /// Failure costs the materials and nothing else: no destroyed item, no lost levels.
        /// The kit supports both and a demo should not teach a player to fear its own
        /// features.
        /// </summary>
        private static readonly float[] RefineSuccess = { 0.85f, 0.55f, 0.3f };

        private static void BuildItemRefines()
        {
            EnsureFolder($"{ResourcesDir}/ItemRefines");
            foreach (RefineGrade grade in RefineGrades)
            {
                var refine = Create<ItemRefine>($"{ResourcesDir}/ItemRefines/{grade.Name}.asset");
                var serialized = new SerializedObject(refine);
                serialized.FindProperty("id").stringValue = grade.Name;
                serialized.FindProperty("defaultTitle").stringValue = grade.Name;

                SerializedProperty prices = serialized.FindProperty("repairPrices");
                prices.arraySize = RepairBands.Length;
                for (int i = 0; i < RepairBands.Length; ++i)
                {
                    SerializedProperty price = prices.GetArrayElementAtIndex(i);
                    price.FindPropertyRelative("durabilityRate").floatValue = RepairBands[i];
                    price.FindPropertyRelative("requireGold").intValue =
                        Mathf.RoundToInt(grade.RepairGold * RepairBandCost[i]);
                    price.FindPropertyRelative("requireItems").arraySize = 0;
                    price.FindPropertyRelative("requireCurrencies").arraySize = 0;
                }

                BaseItem stone = LoadItem(ScrapMetal);
                SerializedProperty levels = serialized.FindProperty("levels");
                levels.arraySize = RefineSuccess.Length;
                for (int i = 0; i < RefineSuccess.Length; ++i)
                {
                    SerializedProperty level = levels.GetArrayElementAtIndex(i);
                    level.FindPropertyRelative("successRate").floatValue = RefineSuccess[i];
                    level.FindPropertyRelative("requireGold").intValue = grade.RefineGold[i];
                    level.FindPropertyRelative("refineFailDecreaseLevels").intValue = 0;
                    level.FindPropertyRelative("refineFailDestroyItem").boolValue = false;
                    level.FindPropertyRelative("availableEnhancers").arraySize = 0;
                    level.FindPropertyRelative("requireCurrencies").arraySize = 0;
                    SerializedProperty requires = level.FindPropertyRelative("requireItems");
                    requires.arraySize = stone == null ? 0 : 1;
                    if (stone != null)
                    {
                        SerializedProperty entry = requires.GetArrayElementAtIndex(0);
                        entry.FindPropertyRelative("item").objectReferenceValue = stone;
                        entry.FindPropertyRelative("amount").intValue = grade.RefineStone[i];
                    }
                }

                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(refine);
            }
        }

        private static ItemRefine RefineFor(int price)
        {
            foreach (RefineGrade grade in RefineGrades)
            {
                if (price < grade.UnderPrice)
                    return Load<ItemRefine>($"{ResourcesDir}/ItemRefines/{grade.Name}.asset");
            }
            return null;
        }

        private static BaseItem LoadItem(string name)
        {
            return Load<BaseItem>($"{ResourcesDir}/Items/{name}.asset");
        }

        private static void WriteCommon(SerializedObject serialized, ItemSpec spec)
        {
            serialized.FindProperty("id").stringValue = spec.Name;
            serialized.FindProperty("defaultTitle").stringValue = spec.Title;
            serialized.FindProperty("defaultDescription").stringValue = spec.Description ?? string.Empty;
            serialized.FindProperty("sellPrice").intValue = spec.Price;
            serialized.FindProperty("weight").floatValue = spec.Weight;
            SerializedProperty stack = serialized.FindProperty("maxStack");
            if (stack.intValue < 1)
                stack.intValue = 1;
        }

        private static void WriteModel(SerializedObject serialized, string field, string socket, string prefabPath,
                                       BaseEquipmentModelBonesSetupManager bonesSetup, Vector3 localEuler = default(Vector3),
                                       Vector3? localPosition = null, Vector3? localScale = null)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogError($"[{nameof(DemoItemBuilder)}] Missing equipment model \"{prefabPath}\".");
                return;
            }
            SerializedProperty models = serialized.FindProperty(field);
            models.arraySize = 1;
            SerializedProperty model = models.GetArrayElementAtIndex(0);
            model.FindPropertyRelative("equipSocket").stringValue = socket;
            model.FindPropertyRelative("meshPrefab").objectReferenceValue = prefab;
            model.FindPropertyRelative("localPosition").vector3Value = localPosition ?? Vector3.zero;
            model.FindPropertyRelative("localEulerAngles").vector3Value = localEuler;
            model.FindPropertyRelative("localScale").vector3Value = localScale ?? Vector3.one;
            // Rigid props keep their own transform; skinned pieces get rebound instead.
            model.FindPropertyRelative("doNotSetupBones").boolValue = bonesSetup == null;
            model.FindPropertyRelative("equipmentModelBonesSetupManager").objectReferenceValue = bonesSetup;
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

        private static T Load<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
                Debug.LogError($"[{nameof(DemoItemBuilder)}] Missing asset \"{path}\".");
            return asset;
        }

        /// <summary>Everything the builder produced, for registering in the game database.</summary>
        public static List<Object> AllItems()
        {
            var items = new List<Object>();
            foreach (string guid in AssetDatabase.FindAssets("t:BaseItem", new[] { ResourcesDir + "/Items" }))
                items.Add(AssetDatabase.LoadAssetAtPath<Object>(AssetDatabase.GUIDToAssetPath(guid)));
            return items;
        }

        public static List<Object> AllOfType(string folder, string filter)
        {
            var found = new List<Object>();
            foreach (string guid in AssetDatabase.FindAssets(filter, new[] { ResourcesDir + "/" + folder }))
                found.Add(AssetDatabase.LoadAssetAtPath<Object>(AssetDatabase.GUIDToAssetPath(guid)));
            return found;
        }

        /// <summary>
        /// Puts a drawn icon on an item, by file name.
        ///
        /// **An item's icon has to be assigned; dropping the PNG in the folder does
        /// nothing.** Nothing in the kit or the demo matches art to items by name - the
        /// equipment icons are bound by `Tools > Equipment Icon Generator` when it is run,
        /// and everything else is bound by a builder. Six items drawn on 2026-09-22 sat in
        /// `Textures/Icons/Items` with six empty `icon` fields and nothing in the console,
        /// which is the same silent shape as a PNG that imported as a plain texture.
        ///
        /// **The file is not named after the item**, and never has been: `MinorHealingPotion`
        /// wears `HealthPotion.png` and `IronLongsword` wears `IronLongsword_icon.png`. So
        /// the builder that owns an item names its icon, rather than a rule guessing.
        ///
        /// The import settings are corrected on the way past, and only when they are wrong:
        /// a PNG dropped into the project imports as a plain `Texture2D`, and
        /// `LoadAssetAtPath&lt;Sprite&gt;` on one of those returns **null** rather than
        /// failing - a blank slot in the inventory with nothing to explain it.
        /// </summary>
        public static Sprite AdoptItemIcon(SerializedObject item, string iconName)
        {
            if (string.IsNullOrEmpty(iconName))
                return null;
            string path = $"{IconDir}/{iconName}.png";
            if (!System.IO.File.Exists(path))
            {
                Debug.LogWarning($"[{nameof(DemoItemBuilder)}] No icon at \"{path}\"; " +
                                 "that item will show a blank square.");
                return null;
            }

            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null &&
                (importer.textureType != TextureImporterType.Sprite ||
                 importer.spriteImportMode != SpriteImportMode.Single ||
                 !importer.alphaIsTransparency))
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = true;
                importer.SaveAndReimport();
            }

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null)
            {
                Debug.LogWarning($"[{nameof(DemoItemBuilder)}] {path} is there but did not load as a " +
                                 "Sprite. Check its import settings.");
                return null;
            }
            SerializedProperty icon = item.FindProperty("icon");
            if (icon != null)
                icon.objectReferenceValue = sprite;
            return sprite;
        }

        /// <summary>Where the demo's drawn item art lives.</summary>
        private const string IconDir = "Assets/OpenMMORPG/Demo/Textures/Icons/Items";

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            int split = path.LastIndexOf('/');
            EnsureFolder(path.Substring(0, split));
            AssetDatabase.CreateFolder(path.Substring(0, split), path.Substring(split + 1));
        }
    }
}
