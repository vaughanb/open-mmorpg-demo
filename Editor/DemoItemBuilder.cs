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
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(type);
            }
        }

        /// <summary>
        /// Builds the projectiles the ranged weapons fire.
        ///
        /// A missile weapon is inert without one: the kit reads `missileDamageEntity` off the
        /// weapon type and spawns it, so a bow with that field left empty draws and fires
        /// nothing at all. Neither the kit nor the demo shipped a projectile, but the arrow
        /// model was already built and sitting unused beside the bow.
        /// </summary>
        private static void BuildMissiles()
        {
            EnsureFolder(MissileDir);
            BuildMissile("ArrowMissile", EquipmentDir + "/Arrow.prefab", 0.05f);
            BuildMissile("SpellBolt", null, 0.14f);
        }

        private static void BuildMissile(string name, string modelPath, float radius)
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
            else
            {
                GameObject glow = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Object.DestroyImmediate(glow.GetComponent<Collider>());
                glow.transform.SetParent(root.transform, false);
                glow.transform.localScale = Vector3.one * radius * 2f;
                glow.name = "Model";
                glow.GetComponent<MeshRenderer>().sharedMaterial = SpellBoltMaterial();
            }

            var missile = root.AddComponent<MissileDamageEntity>();
            missile.hitLayers = HitLayers;
            missile.sphereCastRadius = radius;
            missile.destroyDelay = 0f;

            string path = MissileDir + "/" + name + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }

        /// <summary>A plain emissive ball for the mage's bolt. No art pack here ships one.</summary>
        private static Material SpellBoltMaterial()
        {
            string path = MaterialDir + "/MI_SpellBolt.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing == null)
            {
                existing = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                EnsureFolder(MaterialDir);
                AssetDatabase.CreateAsset(existing, path);
            }
            var colour = new Color(0.42f, 0.62f, 1f);
            existing.SetColor("_BaseColor", colour);
            existing.EnableKeyword("_EMISSION");
            existing.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            existing.SetColor("_EmissionColor", colour * 3f);
            EditorUtility.SetDirty(existing);
            return existing;
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

                string socket = spec.TypeAsset == "Bow" ? SocketLeftHand : SocketRightHand;
                Vector3 facing = IsBladed(spec.TypeAsset) ? BladeFacing : Vector3.zero;
                Vector3 gripPosition, gripEuler, gripScale;
                ResolveGrip(spec.Name, facing, out gripPosition, out gripEuler, out gripScale);
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
        /// Staves and bows are left at zero: a staff has no edge, and the bow's plane is
        /// already correct for the archery clips.
        /// </summary>
        private static readonly Vector3 BladeFacing = new Vector3(0f, 80f, 0f);

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
        private static void ResolveGrip(string itemName, Vector3 defaultEuler,
                                        out Vector3 localPosition, out Vector3 localEuler, out Vector3 localScale)
        {
            localPosition = Vector3.zero;
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
            ResolveGrip("PaintedRoundShield", ShieldFacing, out shieldPosition, out shieldEuler, out shieldScale);
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

            BuildProvisions();

            var junk = Create<JunkItem>($"{ResourcesDir}/Items/BanditInsignia.asset");
            var junkSerialized = new SerializedObject(junk);
            WriteCommon(junkSerialized, new ItemSpec
            {
                Name = "BanditInsignia",
                Title = "Bandit Insignia",
                Description = "A crude token the camp's crew wear. Proof of a kill.",
                Price = 15,
                Weight = 0.1f,
            });
            junkSerialized.FindProperty("maxStack").intValue = 50;
            junkSerialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(junk);
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
