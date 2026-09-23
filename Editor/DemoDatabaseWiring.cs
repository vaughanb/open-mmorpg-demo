using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// Points the demo's game data at the rebuilt island and characters.
    ///
    /// The entity prefabs already carry their own character data — that link is
    /// inherited from the prefabs they were cloned from — so what is left is
    /// registering the new entities in the database and moving the spawn point onto
    /// the island, which the old data still placed out at (-50, 10, -200).
    /// </summary>
    public static class DemoDatabaseWiring
    {
        private const string GameDataDir = "Assets/OpenMMORPG/Demo/GameData";
        private const string EntityDir = "Assets/OpenMMORPG/Demo/Prefabs/GamePlay/CharacterEntities";

        [MenuItem("Open MMORPG/Demo/Wire Game Database")]
        public static void Wire()
        {
            var database = AssetDatabase.LoadAssetAtPath<ScriptableObject>($"{GameDataDir}/GameDatabase.asset");
            var map = AssetDatabase.LoadAssetAtPath<ScriptableObject>($"{GameDataDir}/Resources/MapInfos/BaseMap.asset");
            if (database == null || map == null)
            {
                Debug.LogError($"[{nameof(DemoDatabaseWiring)}] Demo game data is missing under {GameDataDir}.");
                return;
            }

            WriteMap(map);
            WriteClasses(map);
            WriteMonsters();
            WriteSwimSpeed();
            WriteDatabase(database);
            WireDungeon(map);
            PreloadAudio();

            AssetDatabase.SaveAssets();
            Debug.Log($"[{nameof(DemoDatabaseWiring)}] Wired the demo database.");
        }

        /// <summary>
        /// Where players arrive, relative to the middle of the green, and which way they
        /// face when they do. Kept clear of the firepit — see <see cref="WriteMap"/>.
        /// `DemoSceneBuilder` checks the result against the built scene and complains if
        /// anything has moved into it.
        /// </summary>
        private static readonly Vector2 ArrivalOffset = new Vector2(-5f, 0f);
        private const float ArrivalYaw = 270f;

        private static void WriteMap(ScriptableObject map)
        {
            var serialized = new SerializedObject(map);
            // The map's id is the key the whole game looks it up by, and the name a saved
            // character carries around as the place it logged out.
            serialized.FindProperty("id").stringValue = "VerdantIsle";
            serialized.FindProperty("defaultTitle").stringValue = "Verdant Isle";
            // Players arrive on the village green, a few paces off the middle of it and
            // turned to look back across it.
            //
            // Not *on* the middle: that is where the scene builder stands the firepit, and
            // the two had independently picked the same spot. A character that materialises
            // inside a mesh collider is thrown out of it by the physics, which in game reads
            // as dying the instant you arrive — and then again on every attempt to get up,
            // because the respawn point is the arrival point. It looks for all the world
            // like a broken respawn, and the respawn code is fine.
            Vector2 arrival = DemoIslandBuilder.VillageCentre + ArrivalOffset;
            serialized.FindProperty("startPosition").vector3Value =
                new Vector3(arrival.x, DemoIslandBuilder.HeightAt(arrival.x, arrival.y) + 0.5f, arrival.y);
            serialized.FindProperty("startRotation").vector3Value = new Vector3(0f, ArrivalYaw, 0f);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(map);
        }

        private struct ClassSpec
        {
            public string Name;
            public string Title;
            public string Description;
            public string Weapon;
            public float Hp;
            public float HpPerLevel;
            public float Mp;
            public float MpPerLevel;
            public float MoveSpeed;
            public float AtkSpeed;
        }

        /// <summary>
        /// The three things a new character can be.
        ///
        /// They are told apart three ways. By what they fight with: the warrior swings a
        /// sword, the ranger looses arrows and the mage throws bolts, which is three weapon
        /// types at three ranges. By their pools, which lean the same way, so the mage
        /// cannot simply stand in the open and out-trade a warrior. And by their four
        /// skills each, from <see cref="DemoSkillBuilder"/> - one granted at level one and
        /// three bought with skill points as the character levels.
        ///
        /// All three start in the same peasant clothes. The starting kit is deliberately
        /// poor so that the armour taken off an enemy is worth putting on.
        /// </summary>
        private static readonly ClassSpec[] Classes =
        {
            new ClassSpec { Name = "Warrior", Title = "Warrior",
                Description = "Fights up close and can afford to be hit while doing it.",
                Weapon = "IronShortsword",
                Hp = 150f, HpPerLevel = 30f, Mp = 30f, MpPerLevel = 5f, MoveSpeed = 4.0f, AtkSpeed = 1f },
            new ClassSpec { Name = "Ranger", Title = "Ranger",
                Description = "Keeps her distance and makes the ground between you count.",
                Weapon = "HuntingBow",
                Hp = 115f, HpPerLevel = 22f, Mp = 55f, MpPerLevel = 9f, MoveSpeed = 4.5f, AtkSpeed = 1.15f },
            new ClassSpec { Name = "Mage", Title = "Mage",
                Description = "Hits hardest at range and worst of the three in reach of a blade.",
                Weapon = "ApprenticeStaff",
                Hp = 95f, HpPerLevel = 18f, Mp = 120f, MpPerLevel = 24f, MoveSpeed = 4.0f, AtkSpeed = 0.9f },
        };

        /// <summary>Peasant clothes, worn by every class at level one.</summary>
        private static readonly string[] StartingClothes =
        {
            "PeasantTunic", "PeasantSleeves", "PeasantTrousers", "PeasantShoes",
        };

        private static void WriteClasses(ScriptableObject map)
        {
            DemoItemBuilder.EnsureFolder($"{GameDataDir}/Resources/PlayerCharacters");
            foreach (ClassSpec spec in Classes)
            {
                string path = $"{GameDataDir}/Resources/PlayerCharacters/{spec.Name}.asset";
                var asset = AssetDatabase.LoadAssetAtPath<PlayerCharacter>(path);
                if (asset == null)
                {
                    asset = ScriptableObject.CreateInstance<PlayerCharacter>();
                    AssetDatabase.CreateAsset(asset, path);
                }
                var serialized = new SerializedObject(asset);
                // The id is hashed into the data id every saved character carries. Two
                // classes sharing one, or leaving one empty, and the game cannot tell which
                // of them a character was.
                serialized.FindProperty("id").stringValue = spec.Name;
                serialized.FindProperty("defaultTitle").stringValue = spec.Title;
                SerializedProperty description = serialized.FindProperty("defaultDescription");
                if (description != null)
                    description.stringValue = spec.Description;

                Stat(serialized, "stats.baseStats.hp", spec.Hp);
                Stat(serialized, "stats.statsIncreaseEachLevel.hp", spec.HpPerLevel);
                Stat(serialized, "stats.baseStats.hpRecovery", 2f);
                Stat(serialized, "stats.statsIncreaseEachLevel.hpRecovery", 0.4f);
                Stat(serialized, "stats.baseStats.mp", spec.Mp);
                Stat(serialized, "stats.statsIncreaseEachLevel.mp", spec.MpPerLevel);
                Stat(serialized, "stats.baseStats.mpRecovery", 2f);
                Stat(serialized, "stats.baseStats.stamina", 100f);
                Stat(serialized, "stats.statsIncreaseEachLevel.stamina", 6f);
                Stat(serialized, "stats.baseStats.staminaRecovery", 12f);
                Stat(serialized, "stats.baseStats.moveSpeed", spec.MoveSpeed);
                Stat(serialized, "stats.baseStats.atkSpeed", spec.AtkSpeed);
                Stat(serialized, "stats.baseStats.weightLimit", 140f);
                Stat(serialized, "stats.statsIncreaseEachLevel.weightLimit", 6f);
                Stat(serialized, "stats.baseStats.criRate", 0.05f);

                serialized.FindProperty("startMap").objectReferenceValue = map;
                serialized.FindProperty("rightHandEquipItem").objectReferenceValue = Item(spec.Weapon);
                DemoSkillBuilder.WriteClassSkills(serialized, spec.Name);

                var clothes = new Object[StartingClothes.Length];
                for (int i = 0; i < StartingClothes.Length; ++i)
                    clothes[i] = Item(StartingClothes[i]);
                SetList(serialized, "armorItems", clothes);

                // Five potions for everyone, and a hundred arrows for whoever starts with a
                // bow. **The arrows are not a nicety.** The ammo requirement lives on the
                // Bow weapon type, so from the moment it is set a ranger with an empty
                // quiver cannot shoot at all - the shot is refused, silently, by
                // `DecreaseAmmos`. Starting stock, a gold apiece at Marek's and a craft
                // recipe are the three things between that rule and a class that does not
                // work. Keyed off the class's own starting weapon rather than its name, so
                // a second bow class would be armed too.
                var starting = new List<KeyValuePair<string, int>>
                {
                    new KeyValuePair<string, int>("MinorHealingPotion", 5),
                };
                if (spec.Weapon == "HuntingBow" || spec.Weapon == "YewLongbow")
                    starting.Add(new KeyValuePair<string, int>(DemoSuppliesBuilder.ArrowItem, 100));

                SerializedProperty startItems = serialized.FindProperty("startItems");
                startItems.arraySize = starting.Count;
                for (int i = 0; i < starting.Count; ++i)
                {
                    SerializedProperty entry = startItems.GetArrayElementAtIndex(i);
                    entry.FindPropertyRelative("item").objectReferenceValue = Item(starting[i].Key);
                    entry.FindPropertyRelative("amount").intValue = starting[i].Value;
                }

                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(asset);
            }
        }

        private struct MonsterSpec
        {
            public string Name;
            public string Title;
            public string Weapon;
            public float Hp;
            public float HpPerLevel;
            public float MoveSpeed;
            public float AtkSpeed;
            public float VisualRange;
            public string[] Loot;
            /// <summary>The chance of the first piece of loot; the rest fall away from it. Zero means the usual tenth.</summary>
            public float LootRate;
            /// <summary>Experience for a kill at level one, and how much more each level adds.</summary>
            public float Exp;
            public float ExpPerLevel;
            /// <summary>Gold for a kill at level one, and how much more each level adds.</summary>
            public float Gold;
            public float GoldPerLevel;
            /// <summary>
            /// How it behaves when it sees a player. Left at the default `Normal` a monster
            /// fights back once struck; `NoHarm` never fights at all, which is what makes
            /// the deer game rather than an enemy.
            /// </summary>
            public MonsterCharacteristic Characteristic;
            /// <summary>
            /// Game rather than an enemy: it drops only what came off its own body, with
            /// none of the insignia and potions that a bandit is carrying because it is a
            /// person with pockets.
            /// </summary>
            public bool Quarry;
            /// <summary>
            /// What it hits for at level one, and how much each level adds.
            ///
            /// **These were never written until 2026-09-16, and the kit's default is zero** -
            /// so every monster but the bandit, whose asset had been tuned by hand, walked up
            /// to the player and dealt no damage at all. A marauder, a cultist and the crypt's
            /// boss were all harmless.
            /// </summary>
            public float DamageMin, DamageMax, DamagePerLevel;
            /// <summary>
            /// The family's own trophy - its common drop, and what the merchant buys. Empty
            /// means the bandit insignia, which is what every human enemy dropped until
            /// 2026-09-16, cultists and the crypt's master included.
            /// </summary>
            public string Token;
            /// <summary>
            /// How far it can reach. Zero means the bandit's 1.5m, which suits anything
            /// man-sized swinging something.
            ///
            /// The companion trap: `MonsterCharacter` carries its own `damageInfo`, whose
            /// `startAttackDistance` defaults to **0.5**, and `Damage.GetDistance()` returns
            /// `min(hitDistance, startAttackDistance)` whenever that is above zero. Left alone,
            /// every monster in the demo would only attack from half a metre - closer than two
            /// capsules can stand. It is written as 0.85 of the reach here, as the weapons are.
            /// </summary>
            public float HitDistance;
        }

        /// <summary>
        /// The three things on the island that fight back, and what comes off each.
        ///
        /// Each family wears one of the outfits and drops the pieces of it, so what a player
        /// takes off a body is what they watched it wearing — and because the three outfits
        /// are the three classes' armour, killing the right enemy is how a character of that
        /// class gears up. A warrior wants the marauders in plate, a ranger wants the
        /// bandits, a mage wants the cultists.
        /// </summary>
        private static readonly MonsterSpec[] Monsters =
        {
            // Kills pay experience and gold, scaled so that the island's level bands each
            // take a handful of kills: the kit's default table asks 20 points for level
            // two and 295 for level ten, so a bandit at level one is worth a third of a
            // level and a cultist at level eight a fifth of one. Left at the kit's zero,
            // as they were until 2026-09-14, a character never levelled at all.
            new MonsterSpec { Name = "BaseEnemy", Title = "Bandit", Weapon = "BanditAxe",
                Hp = 55f, HpPerLevel = 18f, MoveSpeed = 3.6f, AtkSpeed = 0.85f, VisualRange = 14f,
                Exp = 9f, ExpPerLevel = 2.5f, Gold = 3f, GoldPerLevel = 0.8f,
                DamageMin = 4f, DamageMax = 8f, DamagePerLevel = 2f, HitDistance = 1.5f,
                Loot = new[] { "RangerBoots", "RangerBracers", "RangerPauldron", "RangerHood", "RangerBreeches", "RangerJerkin", "HuntingBow" } },
            new MonsterSpec { Name = "Marauder", Title = "Marauder", Weapon = "IronLongsword",
                Hp = 95f, HpPerLevel = 26f, MoveSpeed = 3.2f, AtkSpeed = 0.7f, VisualRange = 15f,
                Exp = 18f, ExpPerLevel = 4f, Gold = 7f, GoldPerLevel = 1.2f,
                // Plate and a longsword: the heaviest regular hit on the island, and the longest
                // reach of the three human families.
                DamageMin = 7f, DamageMax = 12f, DamagePerLevel = 3f, HitDistance = 1.8f, Token = "MarauderSeal",
                Loot = new[] { "KnightSabatons", "KnightGauntlets", "KnightPauldrons", "KnightHelm", "KnightGreaves", "KnightCuirass", "IronLongsword" } },
            new MonsterSpec { Name = "Cultist", Title = "Cultist", Weapon = "ApprenticeStaff",
                Hp = 45f, HpPerLevel = 14f, MoveSpeed = 3.4f, AtkSpeed = 1.0f, VisualRange = 16f,
                Exp = 16f, ExpPerLevel = 3.5f, Gold = 6f, GoldPerLevel = 1f,
                DamageMin = 5f, DamageMax = 9f, DamagePerLevel = 2.2f, HitDistance = 1.6f, Token = "CultistSigil",
                Loot = new[] { "WizardShoes", "WizardSleeves", "WizardTrousers", "WizardRobe", "ElderStaff" } },
            // The crypt's master. Five times a cultist's health at the same level and a
            // faster staff, so at the level the crypt is pitched at - ten - the fight is
            // long enough to be a fight but no more dangerous a hit than a cultist's; the
            // danger is the guard that comes with him. He drops the mage's set at three
            // times the rate, robe first, since he is the one wearing the good one.
            new MonsterSpec { Name = "Hierophant", Title = "Hierophant", Weapon = "ElderStaff",
                Hp = 240f, HpPerLevel = 45f, MoveSpeed = 3.6f, AtkSpeed = 1.15f, VisualRange = 18f,
                Exp = 135f, ExpPerLevel = 15f, Gold = 55f, GoldPerLevel = 6f,
                // No harder a single blow than a cultist's, relative to the level the crypt is
                // pitched at - the danger is his guard and his health, as the note above says.
                DamageMin = 10f, DamageMax = 16f, DamagePerLevel = 4f, HitDistance = 2f, Token = "CultistSigil",
                Loot = new[] { "WizardRobe", "ElderStaff", "WizardSleeves", "WizardTrousers", "WizardShoes" }, LootRate = 0.30f },
            // The island's game. Not an enemy: a deer never fights, and `NoHarm` is how the
            // kit says so — it wanders, it can be shot, and it will not turn on the player.
            // `DemoFlee` on the entity gives it the one thing the kit has no setting for,
            // which is the sense to run once it has been hit.
            //
            // Cheap to kill and cheap to lose. The health is a couple of arrows' worth and
            // the experience deliberately slight, because hunting is meant to be a living
            // rather than a way to level: it is the hide and the venison that pay, and they
            // drop nearly every time, unlike armour off a body.
            new MonsterSpec { Name = "Deer", Title = "Deer", Weapon = null,
                Hp = 30f, HpPerLevel = 6f, MoveSpeed = 6.5f, AtkSpeed = 1f, VisualRange = 18f,
                Exp = 4f, ExpPerLevel = 1f, Gold = 0f, GoldPerLevel = 0f,
                Characteristic = MonsterCharacteristic.NoHarm, Quarry = true,
                Loot = new[] { "Venison", "DeerHide" }, LootRate = 0.85f },
            // The first thing on the island that will actually fight a new character.
            //
            // Everything else that fights is a person, and the weakest of those - a level
            // one bandit - has 55 health and an axe. A character who walks out of the
            // village at level one has no business meeting that yet, and until the wolves
            // there was nothing between the green and it: the first enemy a player met was
            // whichever band they happened to walk into, several levels above them.
            //
            // So the wolf is pitched deliberately below a bandit at every level: half the
            // health, no weapon, and a bite rather than an axe. It is `Aggressive` where
            // the people are `Normal`, which reverses who starts the fight - a bandit
            // stands in its field until struck, a wolf comes at you - and that is the whole
            // point of it. A low-level enemy nobody can find is not a low-level enemy. Its
            // sight is kept short (12m against a bandit's 14) so that coming at you stays
            // its decision from close by rather than a charge across the fields.
            //
            // No gold: a wolf has no pockets. What it leaves is off its own body, like the
            // deer's, and worth less - see BuildQuarry.
            new MonsterSpec { Name = "Wolf", Title = "Wolf", Weapon = null,
                Hp = 28f, HpPerLevel = 8f, MoveSpeed = 4.2f, AtkSpeed = 1.15f, VisualRange = 12f,
                Exp = 6f, ExpPerLevel = 1.5f, Gold = 0f, GoldPerLevel = 0f,
                Characteristic = MonsterCharacteristic.Aggressive, Quarry = true,
                // Under a bandit here too, and a short reach because a bite is short. It starts
                // the fight, so it must be the gentlest thing that does.
                DamageMin = 3f, DamageMax = 6f, DamagePerLevel = 1.6f, HitDistance = 1.4f,
                Loot = new[] { "WolfPelt", "WolfFang" }, LootRate = 0.55f },
            // The pet, which is a monster in every way the kit cares about and in no way
            // the player does. Summoned by the Pup's Collar, it fights whatever its owner
            // fights and dies like anything else.
            //
            // **Nothing, exp or gold, for killing it**, and no loot. A summoned creature
            // carrying a reward is a creature somebody farms: a player could summon, kill
            // and re-summon their own pet for pelts. That is not a hypothetical the demo
            // has to survive, but a reward table on a thing the player spawns at will is
            // wrong in a way that is easier to avoid than to notice.
            //
            // Pitched under the wolf it grows out of - a pup, not a wolf on a lead - so it
            // helps a level-one character without fighting for them.
            new MonsterSpec { Name = "WolfPup", Title = "Wolf Pup", Weapon = null,
                Hp = 22f, HpPerLevel = 7f, MoveSpeed = 4.6f, AtkSpeed = 1.2f, VisualRange = 10f,
                Exp = 0f, ExpPerLevel = 0f, Gold = 0f, GoldPerLevel = 0f,
                Characteristic = MonsterCharacteristic.Aggressive,
                DamageMin = 2f, DamageMax = 4f, DamagePerLevel = 1.2f, HitDistance = 1.2f,
                Loot = new string[0] },
        };

        /// <summary>
        /// Health a monster gets back each second while it is being hit, once the rule's
        /// percentage is cancelled out. The kit heals every character by a share of its
        /// maximum health every second - a hundredth in the demo's rule - on top of the
        /// `hpRecovery` stat, and it heals in a fight exactly as it does out of one. For
        /// a character that is fine: it is how they get ready for the next fight. For a
        /// monster with a big pool it is a wall: the Hierophant's 645 points came back
        /// at seven and a half a second, which is a starter sword's whole output, and
        /// the fight could not end. So each monster's `hpRecovery` is set to cancel the
        /// share, base and per level, and leave this much.
        /// </summary>
        private const float MonsterRegenPerSecond = 0.5f;

        /// <summary>
        /// How fast a character swims, as a share of its run speed. The demo's rule asset
        /// was left at a tenth (the kit's own default is a half), which made the sea feel
        /// like treacle: four metres a second on the beach, forty centimetres in the water.
        /// Most MMOs swim at about run speed; a little under reads as water.
        /// </summary>
        private const float SwimMoveSpeedRate = 0.8f;

        private static void WriteSwimSpeed()
        {
            var rule = AssetDatabase.LoadAssetAtPath<ScriptableObject>($"{GameDataDir}/GameplayRule.asset");
            if (rule == null)
                return;
            var serialized = new SerializedObject(rule);
            SerializedProperty rate = serialized.FindProperty("moveSpeedRateWhileSwimming");
            if (rate == null)
                return;
            rate.floatValue = SwimMoveSpeedRate;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(rule);
        }

        /// <summary>The rule's percentage regen, read from the demo's rule so the cancel cannot drift from it.</summary>
        private static float HpRecoveryRatePerSecond()
        {
            var rule = AssetDatabase.LoadAssetAtPath<ScriptableObject>($"{GameDataDir}/GameplayRule.asset");
            if (rule == null)
                return 0f;
            SerializedProperty rate = new SerializedObject(rule).FindProperty("hpRecoveryRatePerSeconds");
            return rate == null ? 0f : rate.floatValue;
        }

        private static void WriteMonsters()
        {
            DemoItemBuilder.EnsureFolder($"{GameDataDir}/Resources/MonsterCharacters");
            float regenRate = HpRecoveryRatePerSecond();
            foreach (MonsterSpec spec in Monsters)
            {
                string path = $"{GameDataDir}/Resources/MonsterCharacters/{spec.Name}.asset";
                var asset = AssetDatabase.LoadAssetAtPath<MonsterCharacter>(path);
                if (asset == null)
                {
                    asset = ScriptableObject.CreateInstance<MonsterCharacter>();
                    AssetDatabase.CreateAsset(asset, path);
                }
                var serialized = new SerializedObject(asset);
                serialized.FindProperty("id").stringValue = spec.Title;
                serialized.FindProperty("defaultTitle").stringValue = spec.Title;
                Stat(serialized, "stats.baseStats.hp", spec.Hp);
                Stat(serialized, "stats.statsIncreaseEachLevel.hp", spec.HpPerLevel);
                Stat(serialized, "stats.baseStats.moveSpeed", spec.MoveSpeed);
                Stat(serialized, "stats.baseStats.atkSpeed", spec.AtkSpeed);
                Stat(serialized, "stats.baseStats.hpRecovery", MonsterRegenPerSecond - spec.Hp * regenRate);
                Stat(serialized, "stats.statsIncreaseEachLevel.hpRecovery", -spec.HpPerLevel * regenRate);
                Stat(serialized, "visualRange", spec.VisualRange);

                // What it hits for, and how far it can reach to do it. Both were left at the
                // kit's defaults until 2026-09-16 - zero damage, and a half-metre reach that
                // two capsules cannot close - so only the bandit, whose asset had been tuned
                // by hand, could fight at all. See the MonsterSpec fields for the mechanism.
                float reach = spec.HitDistance > 0f ? spec.HitDistance : 1.5f;
                Stat(serialized, "damageInfo.hitDistance", reach);
                Stat(serialized, "damageInfo.hitFov", 90f);
                Stat(serialized, "damageInfo.startAttackDistance", reach * 0.85f);
                Stat(serialized, "damageAmount.amount.baseAmount.min", spec.DamageMin);
                Stat(serialized, "damageAmount.amount.baseAmount.max", spec.DamageMax);
                Stat(serialized, "damageAmount.amount.amountIncreaseEachLevel.min", spec.DamagePerLevel);
                Stat(serialized, "damageAmount.amount.amountIncreaseEachLevel.max", spec.DamagePerLevel);

                Stat(serialized, "randomExp.baseAmount.min", Mathf.Round(spec.Exp * 0.9f));
                Stat(serialized, "randomExp.baseAmount.max", Mathf.Round(spec.Exp * 1.1f));
                Stat(serialized, "randomExp.amountIncreaseEachLevel.min", spec.ExpPerLevel);
                Stat(serialized, "randomExp.amountIncreaseEachLevel.max", spec.ExpPerLevel);
                Stat(serialized, "randomGold.baseAmount.min", Mathf.Round(spec.Gold * 0.7f));
                Stat(serialized, "randomGold.baseAmount.max", Mathf.Round(spec.Gold * 1.3f));
                Stat(serialized, "randomGold.amountIncreaseEachLevel.min", spec.GoldPerLevel);
                Stat(serialized, "randomGold.amountIncreaseEachLevel.max", spec.GoldPerLevel);
                SerializedProperty weapon = serialized.FindProperty("rightHandEquipItem");
                if (weapon != null)
                    weapon.objectReferenceValue = string.IsNullOrEmpty(spec.Weapon) ? null : Item(spec.Weapon);
                SerializedProperty characteristic = serialized.FindProperty("characteristic");
                if (characteristic != null)
                    characteristic.enumValueIndex = (int)spec.Characteristic;
                // What it does while nothing has roused it. The kit flags a wandering
                // monster `IsWalking`, so this is the pace of the walk animation as well as
                // the speed; a deer drifting between grazing spots moves slower than a
                // bandit on patrol.
                if (spec.Quarry)
                    Stat(serialized, "wanderMoveSpeed", 1.1f);
                DemoSkillBuilder.WriteMonsterSkills(serialized, spec.Name);
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(asset);

                WriteDrops(asset, spec.Loot, spec.LootRate > 0f ? spec.LootRate : 0.10f, spec.Quarry, spec.Token);
            }
        }

        /// <summary>Sets one stat, and says so if the kit has renamed the field.</summary>
        private static void Stat(SerializedObject on, string path, float value)
        {
            SerializedProperty property = on.FindProperty(path);
            if (property == null)
            {
                Debug.LogWarning($"[{nameof(DemoDatabaseWiring)}] No stat \"{path}\" on {on.targetObject.name}.");
                return;
            }
            if (property.propertyType == SerializedPropertyType.Integer)
                property.intValue = Mathf.RoundToInt(value);
            else
                property.floatValue = value;
        }

        /// <summary>
        /// What an enemy leaves behind: its own gear.
        ///
        /// Each family wears one of the three outfits and drops the pieces of it, so the
        /// armour a player picks up is the armour they just fought — and since those three
        /// outfits are the three classes' armour, killing the right enemy is how a character
        /// gears into its class. The list is ordered cheapest first and the rates fall along
        /// it, which makes a full set something to work towards rather than something the
        /// first kill hands over. The insignia is the common drop that gives the merchant a
        /// reason to exist, and a potion often enough that the next fight is not gated on
        /// walking home.
        ///
        /// Game is the exception, and takes neither of those two: a deer is not carrying a
        /// bandit's token or a bottle of physic. It drops what came off its own body, and
        /// it drops it reliably, because a hunt that usually yields nothing is not a living.
        /// </summary>
        private static void WriteDrops(ScriptableObject monsterData, string[] loot, float rate, bool quarry = false, string token = null)
        {
            var serialized = new SerializedObject(monsterData);
            SerializedProperty list = serialized.FindProperty("itemDropManager.randomItems");
            if (list == null)
            {
                Debug.LogError($"[{nameof(DemoDatabaseWiring)}] No drop list on {monsterData.name}.");
                return;
            }

            int carried = quarry ? 0 : 2;
            list.arraySize = carried + loot.Length;
            if (!quarry)
            {
                WriteDrop(list.GetArrayElementAtIndex(0), string.IsNullOrEmpty(token) ? "BanditInsignia" : token, 0.55f, 2);
                WriteDrop(list.GetArrayElementAtIndex(1), "MinorHealingPotion", 0.30f, 2);
            }
            for (int i = 0; i < loot.Length; ++i)
                WriteDrop(list.GetArrayElementAtIndex(carried + i), loot[i], Mathf.Max(0.025f, rate - i * rate * 0.12f), 1);

            // At most three of those at once, so a kill is a handful rather than a haul.
            serialized.FindProperty("itemDropManager.minDropItems").intValue = 1;
            serialized.FindProperty("itemDropManager.maxDropItems").intValue = 3;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(monsterData);
        }

        private static void WriteDrop(SerializedProperty entry, string item, float rate, int most)
        {
            entry.FindPropertyRelative("item").objectReferenceValue = Item(item);
            entry.FindPropertyRelative("minAmount").intValue = 1;
            entry.FindPropertyRelative("maxAmount").intValue = most;
            entry.FindPropertyRelative("minLevel").intValue = 0;
            entry.FindPropertyRelative("maxLevel").intValue = 1;
            entry.FindPropertyRelative("dropRate").floatValue = rate;
        }

        private static Object Item(string name)
        {
            var item = AssetDatabase.LoadAssetAtPath<Object>($"{GameDataDir}/Resources/Items/{name}.asset");
            if (item == null)
                Debug.LogError($"[{nameof(DemoDatabaseWiring)}] Missing item \"{name}\". Run Build Items first.");
            return item;
        }

        /// <summary>
        /// Gives the demo a clock, and points the game at it.
        ///
        /// Without one the kit makes an unconfigured updater at runtime, which runs a day
        /// in fifteen minutes — too slow to notice in a demo somebody looks at for five.
        /// Eight minutes is short enough that a player who stops to fight the bandits sees
        /// the light change while they do it.
        /// </summary>
        private static void WriteClock()
        {
            const string path = "Assets/OpenMMORPG/Demo/GameData/DayNightClock.asset";
            var clock = AssetDatabase.LoadAssetAtPath<DefaultDayNightTimeUpdater>(path);
            if (clock == null)
            {
                clock = ScriptableObject.CreateInstance<DefaultDayNightTimeUpdater>();
                AssetDatabase.CreateAsset(clock, path);
            }
            var settings = new SerializedObject(clock);
            settings.FindProperty("secondsToOneDay").floatValue = 8f * 60f;
            settings.FindProperty("startHourOfDay").intValue = 9;
            settings.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(clock);

            GameObject instance = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/OpenMMORPG/Demo/Prefabs/GameInstance.prefab");
            if (instance == null)
            {
                Debug.LogError($"[{nameof(DemoDatabaseWiring)}] No GameInstance prefab to give the clock to.");
                return;
            }
            var game = instance.GetComponent<GameInstance>();
            var serialized = new SerializedObject(game);
            serialized.FindProperty("dayNightTimeUpdater").objectReferenceValue = clock;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(instance);
            AssetDatabase.SaveAssets();
        }

        private static void WriteDatabase(ScriptableObject database)
        {
            var serialized = new SerializedObject(database);

            // One entity per body a player can be; the create screen lists these.
            var bodies = new Object[DemoEntityBuilder.PlayerBodies.Length];
            for (int i = 0; i < bodies.Length; ++i)
                bodies[i] = Component<BasePlayerCharacterEntity>(DemoEntityBuilder.PlayerEntityPath(DemoEntityBuilder.PlayerBodies[i]));
            SetList(serialized, "playerCharacterEntities", bodies);
            SetList(serialized, "monsterCharacterEntities", new[]
            {
                Component<BaseMonsterCharacterEntity>($"{EntityDir}/DemoBanditMale.prefab"),
                Component<BaseMonsterCharacterEntity>($"{EntityDir}/DemoBanditFemale.prefab"),
                Component<BaseMonsterCharacterEntity>($"{EntityDir}/DemoMarauderMale.prefab"),
                Component<BaseMonsterCharacterEntity>($"{EntityDir}/DemoMarauderFemale.prefab"),
                Component<BaseMonsterCharacterEntity>($"{EntityDir}/DemoCultistMale.prefab"),
                Component<BaseMonsterCharacterEntity>($"{EntityDir}/DemoCultistFemale.prefab"),
                Component<BaseMonsterCharacterEntity>($"{EntityDir}/DemoHierophant.prefab"),
                // The island's game. A monster to the kit, which is how it can be shot and
                // looted, but a harmless one — see the Deer spec above.
                Component<BaseMonsterCharacterEntity>($"{EntityDir}/DemoDeer.prefab"),
                // The starter enemy, and the only one that is not a person.
                Component<BaseMonsterCharacterEntity>($"{EntityDir}/DemoWolf.prefab"),
                // The pet. It has to be in this list like any other monster entity - the
                // `PetItem` registers the prefab through `PrepareRelatesData` as well, but
                // a summoned creature the database does not know is one the client cannot
                // spawn.
                Component<BaseMonsterCharacterEntity>($"{EntityDir}/DemoWolfPup.prefab"),
            });
            // Every map the game can put a character on. The island is the start map by
            // being first; the crypt is only reached through its door.
            SetList(serialized, "mapInfos", Maps());

            // The classes a character can be, and the enemy families it can fight. These
            // two lists were never written before, because with one of each the kit could
            // not tell the difference. With three, anything left out of them simply does
            // not exist: an unlisted class cannot be chosen and an unlisted monster has no
            // data id, so its drops resolve to nothing.
            SetList(serialized, "playerCharacters", ClassAssets());
            SetList(serialized, "monsterCharacters", MonsterAssets());
            WriteClassesOntoEntity();

            // The database is what makes an asset exist to the game: anything left out
            // of these lists has no data id at runtime, so an item cannot be spawned and
            // a quest cannot be assigned even though the asset is sitting right there.
            SetList(serialized, "items", DemoItemBuilder.AllItems().ToArray());
            SetList(serialized, "weaponTypes", DemoItemBuilder.AllOfType("WeaponTypes", "t:WeaponType").ToArray());
            SetList(serialized, "armorTypes", DemoItemBuilder.AllOfType("ArmorTypes", "t:ArmorType").ToArray());
            // The twelve skills. A skill left out of this list has no data id, so a class
            // that lists it has nothing to grant and nothing to offer for a skill point -
            // the skill window simply comes up empty, with no error to say why.
            SetList(serialized, "skills", DemoSkillBuilder.AllSkills().ToArray());
            // Last, because the Hierophant's summon points at a character entity that did
            // not exist yet when the skills were built - see WireSummons.
            DemoSkillBuilder.WireSummons();
            // NPC dialogs are not listed here; the kit collects those from the
            // NpcDatabase when it walks each map's NPCs.
            SetList(serialized, "quests", DemoNpcBuilder.AllQuests().ToArray());
            // The harvestable definitions. Their entity prefabs are not listed here — the
            // spawn areas in the map register those themselves when the scene loads.
            SetList(serialized, "harvestables", DemoHarvestBuilder.AllHarvestables().ToArray());
            // What a founded guild has to spend its levels on and to fly over itself. The
            // guild system was never off - the settings, the roles, the exp tree and the
            // windows were all there - these two lists were simply empty, so a guild
            // collected a skill point a level and had nothing to buy with it.
            SetList(serialized, "guildSkills", DemoGuildBuilder.AllSkills().ToArray());
            SetList(serialized, "guildIcons", DemoGuildBuilder.AllIcons().ToArray());
            // The arrows' type. `AmmoItem.PrepareRelatesData` registers it anyway, the way
            // the horse's vehicle entity is registered by the whistle - listing it here as
            // well costs nothing and means the database says what the game has rather than
            // leaving a reader to find it hanging off an item.
            SetList(serialized, "ammoTypes", DemoSuppliesBuilder.AllAmmoTypes().ToArray());
            // The elements everything now deals and resists, and the ailments the mage's
            // two elemental skills and the ranger's mark leave behind. Equipment sets are
            // deliberately not listed: there is no list for them, because an item registers
            // its own set through `PrepareRelatesData`.
            SetList(serialized, "damageElements", DemoCombatDataBuilder.AllElements().ToArray());
            SetList(serialized, "statusEffects", DemoCombatDataBuilder.AllStatusEffects().ToArray());
            WriteClock();

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(database);
        }

        /// <summary>
        /// Tells each player entity which classes are allowed to wear it.
        ///
        /// The create screen pairs a body with a class, and it only offers the classes the
        /// chosen body lists. The demo has two bodies and three classes, and any class may
        /// be either body, so all three belong on both — left as the template shipped, with
        /// the single class it was cloned from, the screen offers exactly one thing to be
        /// however many classes the database holds. The entity is a prefab rather than
        /// data, which is why this reaches out of the database to set it.
        /// </summary>
        private static void WriteClassesOntoEntity()
        {
            foreach (string gender in DemoEntityBuilder.PlayerBodies)
                WriteClassesOntoEntity(DemoEntityBuilder.PlayerEntityPath(gender));
        }

        private static void WriteClassesOntoEntity(string path)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                Debug.LogError($"[{nameof(DemoDatabaseWiring)}] Missing \"{path}\".");
                return;
            }
            var entity = prefab.GetComponent<BasePlayerCharacterEntity>();
            if (entity == null)
            {
                Debug.LogError($"[{nameof(DemoDatabaseWiring)}] \"{path}\" is not a player character.");
                return;
            }
            var serialized = new SerializedObject(entity);
            SetList(serialized, "characterDatabases", ClassAssets());
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(prefab);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }

        // ---- audio -------------------------------------------------------------

        private const string AudioDir = "Assets/OpenMMORPG/Demo/Audio";

        /// <summary>
        /// Makes every demo clip load with the things that reference it, in the
        /// background, instead of on the main thread the first time it is played.
        ///
        /// Unity's importer default since 2022 is the opposite: no preload, no
        /// background load, so an AudioSource's first Play() of a clip reads its samples
        /// synchronously. That is harmless until it happens while a scene is loading
        /// with its activation held - which is how the kit changes maps, holding the new
        /// scene at 90% until it is ready - because Unity serialises asset loads and a
        /// synchronous one queued behind a held scene load waits for it forever, with
        /// the main thread blocked. A character walking out of the crypt took a footstep
        /// or a hit in the two seconds the island takes to load, and the editor froze at
        /// exactly that point twice. Preloaded clips are read while the scene that needs
        /// them loads, which is the one time a load cannot get in anyone's way.
        /// </summary>
        internal static void PreloadAudio()
        {
            int changed = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { AudioDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                // The music is the one thing here that must NOT be resident: two tracks of
                // three minutes are about 64 MB of PCM between them, held for the session, to
                // save a stall under a piece that fades in over two seconds. DemoAudioWiring
                // sets them to stream instead, and this would undo it on every run.
                if (DemoAudioWiring.IsMusic(System.IO.Path.GetFileNameWithoutExtension(path)))
                    continue;
                var importer = AssetImporter.GetAtPath(path) as AudioImporter;
                if (importer == null)
                    continue;
                AudioImporterSampleSettings settings = importer.defaultSampleSettings;
                if (settings.preloadAudioData && importer.loadInBackground)
                    continue;
                settings.preloadAudioData = true;
                importer.defaultSampleSettings = settings;
                importer.loadInBackground = true;
                importer.SaveAndReimport();
                ++changed;
            }
            if (changed > 0)
                Debug.Log($"[{nameof(DemoDatabaseWiring)}] Set {changed} audio clip(s) under {AudioDir} to preload in the background.");
        }

        // ---- the dungeon -------------------------------------------------------

        /// <summary>
        /// Opens the way between the island and the crypt.
        ///
        /// The kit does not read warp portals off the scene; it spawns them when a map
        /// loads, from the warp portal database, one list per map. So the crypt door and
        /// the landing's door are two entries here, each pointing at the other map and
        /// the spot in it to arrive on, and the positions come from the two scene
        /// builders so the door and the gate cannot drift apart. The map spawn server
        /// also has to be told to host the crypt, or the warp would find no server to
        /// send the character to, and the scene has to be in the build for the same
        /// reason - and for the editor to be able to load it at all.
        /// </summary>
        private static void WireDungeon(ScriptableObject island)
        {
            var dungeon = AssetDatabase.LoadAssetAtPath<ScriptableObject>(DemoDungeonBuilder.MapInfoPath);
            GameObject gatePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DemoDungeonBuilder.GatePrefabPath);
            if (dungeon == null || gatePrefab == null)
            {
                Debug.LogWarning($"[{nameof(DemoDatabaseWiring)}] No dungeon to wire: run Open MMORPG > Demo > Regenerate Dungeon Scene first.");
                return;
            }
            var gate = gatePrefab.GetComponent<WarpPortalEntity>();

            var portals = AssetDatabase.LoadAssetAtPath<ScriptableObject>($"{GameDataDir}/WarpPortalDatabase.asset");
            if (portals == null)
            {
                Debug.LogError($"[{nameof(DemoDatabaseWiring)}] No warp portal database under {GameDataDir}.");
                return;
            }
            var serialized = new SerializedObject(portals);
            SerializedProperty maps = serialized.FindProperty("maps");
            maps.arraySize = 2;
            // Down: the crypt door on the island leads to the landing.
            SerializedProperty down = maps.GetArrayElementAtIndex(0);
            down.FindPropertyRelative("mapInfo").objectReferenceValue = island;
            down.FindPropertyRelative("warpPortals").arraySize = 1;
            WritePortal(down.FindPropertyRelative("warpPortals").GetArrayElementAtIndex(0), gate,
                DemoSceneBuilder.CryptGateWorld, DemoSceneBuilder.CryptGateYaw,
                dungeon, DemoDungeonBuilder.ArrivalPosition, DemoDungeonBuilder.ArrivalYaw);
            // Up: the landing's door leads back out onto the doorstep.
            SerializedProperty up = maps.GetArrayElementAtIndex(1);
            up.FindPropertyRelative("mapInfo").objectReferenceValue = dungeon;
            up.FindPropertyRelative("warpPortals").arraySize = 1;
            WritePortal(up.FindPropertyRelative("warpPortals").GetArrayElementAtIndex(0), gate,
                DemoDungeonBuilder.ExitGatePosition, DemoDungeonBuilder.ExitGateYaw,
                island, DemoSceneBuilder.CryptArrivalWorld, DemoSceneBuilder.CryptArrivalYaw);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(portals);

            WriteSpawningMaps();
            WriteBuildScenes();
        }

        private static void WritePortal(SerializedProperty portal, Object entityPrefab, Vector3 at, float yaw, Object toMap, Vector3 toPosition, float toYaw)
        {
            portal.FindPropertyRelative("entityPrefab").objectReferenceValue = entityPrefab;
            portal.FindPropertyRelative("position").vector3Value = at;
            portal.FindPropertyRelative("rotation").vector3Value = new Vector3(0f, yaw, 0f);
            portal.FindPropertyRelative("warpPortalType").enumValueIndex = 0;
            portal.FindPropertyRelative("warpToMapInfo").objectReferenceValue = toMap;
            portal.FindPropertyRelative("warpToPosition").vector3Value = toPosition;
            portal.FindPropertyRelative("warpOverrideRotation").boolValue = true;
            portal.FindPropertyRelative("warpToRotation").vector3Value = new Vector3(0f, toYaw, 0f);
            portal.FindPropertyRelative("warpPointsByCondition").arraySize = 0;
        }

        /// <summary>
        /// Tells the map spawn server to host every map. It launches one map server
        /// process per entry, and a map with no process is a map a warp cannot reach:
        /// the character is told it is warping and then nothing happens.
        /// </summary>
        private static void WriteSpawningMaps()
        {
            const string path = "Assets/OpenMMORPG/Demo/Prefabs/MMOServerInstance.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                Debug.LogError($"[{nameof(DemoDatabaseWiring)}] Missing \"{path}\".");
                return;
            }
            bool found = false;
            foreach (Component component in prefab.GetComponentsInChildren<Component>(true))
            {
                if (component == null)
                    continue;
                var serialized = new SerializedObject(component);
                if (serialized.FindProperty("spawningMaps") == null)
                    continue;
                SetList(serialized, "spawningMaps", Maps());
                serialized.ApplyModifiedPropertiesWithoutUndo();
                found = true;
            }
            if (!found)
            {
                Debug.LogError($"[{nameof(DemoDatabaseWiring)}] Nothing on \"{path}\" has a spawningMaps list.");
                return;
            }
            EditorUtility.SetDirty(prefab);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }

        /// <summary>Puts the dungeon scene in the build, after the maps that are there already.</summary>
        private static void WriteBuildScenes()
        {
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            foreach (EditorBuildSettingsScene scene in scenes)
            {
                if (scene.path == DemoDungeonBuilder.ScenePath)
                {
                    scene.enabled = true;
                    EditorBuildSettings.scenes = scenes.ToArray();
                    return;
                }
            }
            scenes.Add(new EditorBuildSettingsScene(DemoDungeonBuilder.ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log($"[{nameof(DemoDatabaseWiring)}] Added {DemoDungeonBuilder.ScenePath} to the build settings.");
        }

        /// <summary>The maps, island first: the first map in the database is the start map.</summary>
        private static Object[] Maps()
        {
            var maps = new System.Collections.Generic.List<Object>();
            maps.Add(AssetDatabase.LoadAssetAtPath<ScriptableObject>($"{GameDataDir}/Resources/MapInfos/BaseMap.asset"));
            var dungeon = AssetDatabase.LoadAssetAtPath<ScriptableObject>(DemoDungeonBuilder.MapInfoPath);
            if (dungeon != null)
                maps.Add(dungeon);
            return maps.ToArray();
        }

        private static Object[] ClassAssets()
        {
            var found = new Object[Classes.Length];
            for (int i = 0; i < Classes.Length; ++i)
                found[i] = AssetDatabase.LoadAssetAtPath<PlayerCharacter>(
                    $"{GameDataDir}/Resources/PlayerCharacters/{Classes[i].Name}.asset");
            return found;
        }

        private static Object[] MonsterAssets()
        {
            var found = new Object[Monsters.Length];
            for (int i = 0; i < Monsters.Length; ++i)
                found[i] = AssetDatabase.LoadAssetAtPath<MonsterCharacter>(
                    $"{GameDataDir}/Resources/MonsterCharacters/{Monsters[i].Name}.asset");
            return found;
        }

        private static Object Component<T>(string prefabPath) where T : Component
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogError($"[{nameof(DemoDatabaseWiring)}] Missing prefab \"{prefabPath}\".");
                return null;
            }
            T component = prefab.GetComponent<T>();
            if (component == null)
                Debug.LogError($"[{nameof(DemoDatabaseWiring)}] \"{prefabPath}\" has no {typeof(T).Name}.");
            return component;
        }

        private static void SetList(SerializedObject serialized, string field, Object[] values)
        {
            SerializedProperty list = serialized.FindProperty(field);
            if (list == null)
            {
                Debug.LogError($"[{nameof(DemoDatabaseWiring)}] GameDatabase has no field \"{field}\".");
                return;
            }
            list.ClearArray();
            for (int i = 0; i < values.Length; ++i)
            {
                if (values[i] == null)
                    continue;
                list.InsertArrayElementAtIndex(list.arraySize);
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = values[i];
            }
        }
    }
}
