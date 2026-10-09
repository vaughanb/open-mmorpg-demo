using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// The twelve skills the three classes fight with, and everything they need to
    /// exist: the icons, and the area entities the ground-targeted ones spawn.
    ///
    /// Until these were written the demo had no skills at all, and the three classes
    /// were told apart only by the weapon they started with - a mage who could do
    /// nothing but poke with a staff. Four each, on the same shape: one granted at
    /// level one so a new character has something on the bar from the first fight,
    /// and three learned with skill points as the character levels, which is what
    /// puts the kit's skill list, its requirements and its levelling in front of
    /// anyone who plays for ten minutes.
    ///
    /// Since 2026-09-29 each class also has a passive (level two) and a toggle (level six),
    /// the two kinds of skill the kit has that the island had no example of. See the
    /// comment above the first of them in <see cref="Specs"/>.
    ///
    /// The kit ships four skill classes and the demo uses three of them, deliberately,
    /// because a demo's job is to show what is there: <see cref="Skill"/> for the
    /// ordinary attacks, buffs and heals, <see cref="SimpleAreaAttackSkill"/> for the
    /// ground-targeted ones, and <see cref="SimpleDashAttackSkill"/> for the warrior's
    /// charge.
    ///
    /// Run after `Build Items` - the skills point at the weapon types and the missile
    /// prefabs it writes - and before `Build Character Models`, which reads the
    /// animation table this feeds, and `Wire Game Database`, which registers the
    /// assets and hands each class its list.
    /// </summary>
    public static class DemoSkillBuilder
    {
        private const string GameDataDir = "Assets/OpenMMORPG/Demo/GameData";
        private const string ResourcesDir = GameDataDir + "/Resources";
        private const string SkillDir = ResourcesDir + "/Skills";
        private const string IconDir = "Assets/OpenMMORPG/Demo/Textures/Icons/Skills";
        private const string AreaDir = "Assets/OpenMMORPG/Demo/Prefabs/GamePlay/SkillAreas";
        private const string MissileDir = "Assets/OpenMMORPG/Demo/Prefabs/GamePlay/Missiles";
        private const string EntityDir = "Assets/OpenMMORPG/Demo/Prefabs/GamePlay/CharacterEntities";
        private const string MaterialDir = "Assets/OpenMMORPG/Demo/Materials";
        private const string AreaTexturePath = "Assets/OpenMMORPG/Demo/Textures/SkillArea.png";
        private const string AimTexturePath = "Assets/OpenMMORPG/Demo/Textures/SkillAim.png";

        public const string Warrior = "Warrior";
        public const string Ranger = "Ranger";
        public const string Mage = "Mage";
        /// <summary>Not a class: the owner of what every class has, which is the Attack button.</summary>
        public const string Everyone = "Everyone";

        /// <summary>
        /// How high a skill can be taken with skill points. The kit's rule hands out one
        /// point a level, so four skills at five levels is more than a character can
        /// finish on the island - which is the point: the points are a choice, not a
        /// queue to be worked through.
        /// </summary>
        private const int MaxSkillLevel = 5;

        /// <summary>
        /// The layer a spawned area sits on. `DamageEntity` is the kit's own, and the
        /// project's collision matrix lets it overlap every character layer - which is
        /// what the area needs, because it finds its targets with a trigger rather than
        /// by casting the way a missile does.
        /// </summary>
        private const int DamageEntityLayer = 12;

        /// <summary>
        /// The layer the aiming circles sit on. TransparentFX is the one layer only the gameplay
        /// camera draws - not the minimap's - and the kit leaves it out of every targeting and
        /// ground mask, so the circle can never be clicked or stood on.
        /// </summary>
        private const int TransparentFxLayer = 1;

        /// <summary>What the skill does, which decides the asset class and how it is filled in.</summary>
        public enum Shape
        {
            /// <summary>Swung in an arc in front of the caster.</summary>
            Melee,
            /// <summary>Fired at the target as a missile.</summary>
            Missile,
            /// <summary>Carries the caster into the target, then lands.</summary>
            Dash,
            /// <summary>Dropped on the ground and damages what stands in it.</summary>
            Area,
            /// <summary>Does no damage at all: a buff or a heal.</summary>
            Support,
        }

        /// <summary>Who a support skill's buff lands on.</summary>
        public enum BuffTo
        {
            None,
            Self,
            NearbyAllies,
            Ally,
            /// <summary>
            /// On the caster until pressed again: the kit's `Toggle`, a buff with no duration.
            /// A stance, not a spell - see the three in <see cref="Specs"/>.
            /// </summary>
            Toggle,
        }

        public struct SkillSpec
        {
            public string Name;
            public string Title;
            public string Description;

            /// <summary>
            /// The class that can learn it. Null for a monster's skill, which no class may
            /// learn - those name a <see cref="Monster"/> instead.
            /// </summary>
            public string Class;
            /// <summary>The monster that uses it, by the name of its MonsterCharacter spec.</summary>
            public string Monster;
            /// <summary>
            /// The chance it is reached for, and the health it is held back for. See
            /// <see cref="WriteMonsterSkills"/> - these do not mean quite what they look like.
            /// </summary>
            public float UseRate;
            public float UseWhenHpRate;
            /// <summary>
            /// The character level it may be learned at. One means it is granted outright
            /// at creation, so the bar is never empty; anything higher is bought with a
            /// skill point once the character is high enough.
            /// </summary>
            public int LearnLevel;

            public Shape Shape;
            /// <summary>The weapon type asset it needs equipped, or null for any.</summary>
            public string Weapon;
            /// <summary>
            /// Arrows a use takes from the quiver, through the kit's `RequireAmmoType.BasedOnWeapon`:
            /// checked before the cast (an empty quiver refuses it with "No Ammo"), spent on the
            /// server at the trigger, and the arrows' own damage added like a plain shot's. 0 is
            /// none. Until 2026-10-03 every bow skill was a free shot.
            /// </summary>
            public int Arrows;
            public bool RequireShield;

            public int Mp;
            public int MpPerLevel;
            /// <summary>
            /// Share of the caster's MAX mana taken on top of <see cref="Mp"/> (the kit's
            /// `consumeMpRate`, used for the real cost, the can-cast check and the tooltip alike).
            /// A flat cost shrinks to nothing against a pool that grows 40 a level, which is how a
            /// level-one mage's 10-mana bolt (5% of 184, back in two seconds of regen) never
            /// moved the bar. 0 for everything that is not a mage attack spell.
            /// </summary>
            public float MpRate;
            public float Cooldown;
            /// <summary>Taken off the cooldown at each skill level.</summary>
            public float CooldownPerLevel;
            public float Cast;
            /// <summary>
            /// Holds the cast through being hit. The kit interrupts a cast on ANY damage,
            /// so this is the difference between a skill that exists and one that does not
            /// - see <see cref="WriteCommon"/>.
            /// </summary>
            public bool CastCannotBeInterrupted;
            /// <summary>How far it can be raised. Zero takes the default.</summary>
            public int MaxLevel;

            /// <summary>Flat damage at skill level one. Zero where the damage is the weapon's.</summary>
            public float Min;
            public float Max;
            public float PerLevel;
            /// <summary>
            /// What the character's whole swing (weapon, Strength, buffs, refines) is multiplied
            /// by - 1.25 is a hit and a quarter. Zero means the skill does not use the weapon.
            /// Any skill with a rate runs on <see cref="MultiplayerARPG.SwingScaledWeaponSkill"/>:
            /// the kit's own weapon skill multiplies the weapon item's raw damage, which sees no
            /// Strength at all, and reads its rate as added on top (1.25 = 225%).
            /// </summary>
            public float WeaponRate;
            public float WeaponRatePerLevel;

            /// <summary>Reach: the melee arc's radius, the missile's range, or how far away an area can be dropped.</summary>
            public float Distance;
            /// <summary>The melee arc, in degrees.</summary>
            public float Fov;
            public float Radius;
            public string Missile;

            /// <summary>
            /// The clip played when it goes off, and how far into that clip it fires.
            /// Null leaves the skill on whatever the equipped weapon attacks with, which
            /// is what the bow wants: its shot is a generated clip with a measured loose.
            /// </summary>
            public string Clip;
            public float Trigger;
            /// <summary>
            /// Play speed for the clip. Zero is as authored. **Leave it at zero.** The kit
            /// applies a skill clip's speed twice - the use-skill component passes it to
            /// `PlayActionAnimation` as the multiplier and the playable multiplies it by the
            /// same value again - so 2 plays the clip at 4x while the skill's own timing runs
            /// at 2x, and the character stands idle for the difference. To make a clip
            /// shorter, trim it instead: see `DemoAnimationSet.Trimmed`. No skill uses this
            /// since 2026-09-23.
            /// </summary>
            public float ClipSpeed;
            /// <summary>The clip looped while casting. Only read when <see cref="Cast"/> is above zero.</summary>
            public string CastClip;
            /// <summary>The audio family played with the clip. Only read when <see cref="Clip"/> is set.</summary>
            public string Audio;

            public Glyph Glyph;

            /// <summary>Particles played while casting and when it goes off, from <see cref="DemoSkillEffectBuilder"/>.</summary>
            public string CastEffect;
            public string ActivateEffect;
            /// <summary>The tint the area entity's own particles take. Only read by the area skills.</summary>
            public Color AreaColour;
            /// <summary>
            /// An area skill's own landing, from <see cref="DemoSkillEffectBuilder"/>: set off where
            /// the area appears (AreaLandEffect), in place of the glowing disc and the spray every other
            /// area lands with. Frost Nova's ice and frost are the ground it covers, drawn.
            /// </summary>
            public string LandEffect;
            /// <summary>
            /// What plays on whoever it hit. Left null falls back to the game instance's
            /// own, which is the pale physical spark - see DemoSkillEffectBuilder.
            /// </summary>
            public string HitEffect;
            /// <summary>
            /// What goes off at the skill's trigger - the arrow leaving the string - rather than when
            /// its animation starts, which is when the kit plays <see cref="ActivateEffect"/>. For a
            /// bow that is the whole draw early. Played by BowEquipmentEntity from SkillReleaseEffects.
            /// </summary>
            public string ReleaseEffect;
            /// <summary>Worn by whatever it hit for as long as the debuff lasts: the debuff's own `effects`.</summary>
            public string DebuffEffect;
            /// <summary>
            /// Keeps the glowing disc under an area with a <see cref="LandEffect"/> of its own. Frost
            /// Nova's ice is its ground, and drops the disc; Volley's arrows land across a patch that
            /// still wants its edge shown for the three seconds it keeps biting.
            /// </summary>
            public bool KeepMarker;

            // ---- what it leaves behind ----------------------------------------

            public float BuffSeconds;
            /// <summary>
            /// An area skill that bites once, as a burst, whatever its debuff's length. Without
            /// it the patch stays for <see cref="BuffSeconds"/> - which is also how long the
            /// debuff lasts - and bites every 0.75s of that, which is right for Volley's rain
            /// and was wrong for Frost Nova (a 4s slow meant five bites).
            /// </summary>
            public bool Burst;
            /// <summary>
            /// Seconds something takes to fall out of the sky onto the area: the meteor. Above zero
            /// the area is a warning while it falls and bites once, when it lands; the strike is
            /// DemoSkillEffectBuilder.AddMeteorStrike, and the skill's own sound (Meteor.wav) goes
            /// with the fall rather than with the caster's hands. See <see cref="WriteAreaSkill"/>.
            /// </summary>
            public float FallSeconds;
            /// <summary>Movement taken off the victim, as a share: 0.4 is a 40% slow.</summary>
            public float SlowRate;
            /// <summary>Evasion taken off the victim, as a share.</summary>
            public float EvasionRate;
            /// <summary>
            /// Damage a second for as long as the debuff lasts, at skill level one, and what each
            /// level after adds to it. **The kit's own field is not a rate**: a debuff's
            /// `damageOverTimes` amount is the whole of the damage, paid out across the duration
            /// (`CharacterSkillAndBuffComponent` applies `1 / duration * dt` of it a frame), so
            /// <see cref="WriteDebuff"/> writes these times <see cref="BuffSeconds"/>. Until
            /// 2026-10-05 it wrote them as they stood, and "4 a second" was 4 in all - found when
            /// Withering Hex's curse ticked for 1.
            /// </summary>
            public float DamagePerSecond;
            public float DamagePerSecondPerLevel;
            public bool Stun;
            /// <summary>
            /// The kit's Freeze ailment for <see cref="BuffSeconds"/>: no moving, attacking,
            /// casting or using items, and the victim's animation stops where it is. Stun
            /// disallows the same actions but keeps animating.
            /// </summary>
            public bool Freeze;
            public float Knockback;

            public BuffTo BuffTo;
            /// <summary>
            /// Always on once learned, and never pressed: the kit's Passive skill type, whose
            /// `buff` stats are folded into the character's own. Costs nothing and has no clip.
            /// </summary>
            public bool Passive;
            public int HealHp;
            public int HealHpPerLevel;
            public int BuffDamage;
            public int BuffDamagePerLevel;
            /// <summary>Movement added as a share of the ordinary speed; negative slows.</summary>
            public float BuffMoveRate;
            public float BuffMoveRatePerLevel;
            /// <summary>Flat stats on the buff (or the passive), at skill level one and per level after.</summary>
            public float BuffHp, BuffHpPerLevel;
            public float BuffMp, BuffMpPerLevel;
            /// <summary>Mana back per second, on top of the class's own.</summary>
            public float BuffMpRegen, BuffMpRegenPerLevel;
            /// <summary>Armour against the default (physical) element. A knight's cuirass is 14.</summary>
            public float BuffArmor, BuffArmorPerLevel;
            /// <summary>
            /// Block chance, as a share. The kit rolls at least 5% however low this is - a floor,
            /// not a base that this adds to.
            /// </summary>
            public float BuffBlock, BuffBlockPerLevel;
            /// <summary>
            /// The share of a blocked blow that is stopped. **A block chance is nothing without
            /// it:** the kit takes `damage * blockDmgRate` off a blocked hit
            /// (`DefaultGameplayRule.GetBlockDamage`), floored at 5%, and the field is 0 on every
            /// class, attribute and item - the same trap as the crit multiplier.
            /// </summary>
            public float BuffBlockDamage;
            public float BuffCrit, BuffCritPerLevel;
            /// <summary>Added to the critical hit multiplier.</summary>
            public float BuffCritDamage, BuffCritDamagePerLevel;
            /// <summary>
            /// Mana taken per second for as long as a toggle is on. The kit drains it and never
            /// switches the toggle off at zero - ToggleBuffUpkeep does.
            /// </summary>
            public int DrainMp;
            /// <summary>
            /// Takes the whole of the `mpRecovery` stat away while the buff is on - the part of mana
            /// regeneration that attributes and gear give. The rule's own share (a percentage of the
            /// pool) is not a stat and carries on.
            /// </summary>
            public bool StopsMpRegen;
            /// <summary>The buff falls off when its wearer attacks, or is hit.</summary>
            public bool BreaksOnAttack, BreaksWhenHit;
            /// <summary>How far a buff reaches from the caster, for the ones that catch a group.</summary>
            public float BuffDistance;

            /// <summary>The entity prefab called up, for a summon. Null for everything else.</summary>
            public string SummonEntity;
            public int SummonCount;
            public int SummonMaxStack;
            public float SummonSeconds;
        }

        /// <summary>
        /// The set, in the order a character meets it.
        ///
        /// The numbers are pitched against the island rather than picked: a bandit has 55
        /// health at level one and a marauder 95, and weapons do 8 to 20 a swing, so a
        /// level-one skill that lands about half again what a swing does is worth pressing
        /// without making the ordinary attack pointless.
        ///
        /// **`Mp` is whatever the class keeps in its MP slot** (2026-10-06, see `ClassPower`):
        /// rage for the warrior, focus for the ranger, mana for the mage. Rage and focus are a
        /// flat 100 at every level, so those costs are written against 100 and do not rise with
        /// skill level (`MpPerLevel` 0); a cost of 0 is a free skill. Rage is earned in the fight
        /// (about 10 a landed swing), so a warrior opens with an auto-attack or a free Charge, and
        /// a toggle that cost rage could not be switched on before one. Focus refills at 6 a
        /// second, so it paces shots rather than running dry. Mana grows with level and
        /// Intelligence, so the mage's attack spells also take a share of the pool (`MpRate`).
        /// </summary>
        private static readonly SkillSpec[] Specs =
        {
            // ---- Warrior: short reach, and everything happens where he is standing ----

            new SkillSpec { Name = "Cleave", Title = "Cleave", Class = Warrior, LearnLevel = 1,
                Description = "A wide swing that carries through everyone in front of you.",
                Shape = Shape.Melee, Weapon = null,
                // 20 rage, WoW's Cleave: two landed swings' worth.
                Mp = 20, MpPerLevel = 0, Cooldown = 6f, CooldownPerLevel = 0.3f,
                WeaponRate = 1.25f, WeaponRatePerLevel = 0.15f, Min = 4f, Max = 7f, PerLevel = 2f,
                // A wider arc than the sword's own 90 degrees, and half a metre further:
                // this is the skill that hits the second bandit, and it has to reach him.
                Distance = 2.9f, Fov = 170f,
                // UAL2's third heavy swing - the flattest wide cut in the library, which is
                // what a 170-degree arc ought to look like: the sword hand sweeps 317
                // degrees round the body with only 0.29m of rise. (This was a Mixamo clip
                // until 2026-09-23; Mixamo cannot ship in a template - see DemoAnimationSet.)
                //
                // Trigger measured in the body's own frame, so the stance turn does not
                // count as swing. The two readings disagree here, unlike the Mixamo clip's:
                // the arm drives forward in the first third, reaches furthest ahead at 0.65,
                // and then the blade sweeps sideways across the front, fastest at 0.78. The
                // blow lands in that crossing, so 0.7 - between the two.
                Clip = "Sword_Heavy_C", Trigger = 0.7f, Audio = DemoAudioWiring.SwordSwing,
                // A steel ribbon off the blade for the length of the swing (2026-10-02, user's
                // request); built by DemoSkillEffectBuilder.BuildWeaponTrails.
                ActivateEffect = DemoSkillEffectBuilder.CleaveTrailName,
                Glyph = Glyph.Slash },

            // Each class has one passive and one toggle as well (2026-09-29): the kit's other two
            // kinds of skill, which the island had no example of. The passive comes at level two,
            // as the first thing a skill point can buy; the toggle at six, once there is enough
            // mana to spend on keeping one up. Each toggle is a trade rather than a free upgrade -
            // slower, fragile, or paid for by the second - because a stance with no cost is just
            // a passive that has to be remembered.
            new SkillSpec { Name = "Toughness", Title = "Toughness", Class = Warrior, LearnLevel = 2, Passive = true,
                Description = "Years of taking the hit. More health, and armour that turns a little more of every blow.",
                Shape = Shape.Support,
                BuffHp = 15f, BuffHpPerLevel = 15f, BuffArmor = 4f, BuffArmorPerLevel = 4f,
                Glyph = Glyph.Bastion },

            new SkillSpec { Name = "ShieldBash", Title = "Shield Bash", Class = Warrior, LearnLevel = 3,
                Description = "Drives the shield into a single enemy and puts them on the ground.",
                Shape = Shape.Melee, Weapon = null, RequireShield = true,
                // 10 rage, WoW's Shield Bash.
                Mp = 10, MpPerLevel = 0, Cooldown = 12f, CooldownPerLevel = 0.6f,
                WeaponRate = 0.6f, WeaponRatePerLevel = 0.1f,
                Distance = 2.2f, Fov = 60f,
                BuffSeconds = 1.5f, Stun = true, Knockback = 6f,
                // UAL2's one-shot shield strike, which is a bash by design: measured on the
                // shield hand it thrusts 0.46m at 9.3 m/s, against 0.42m at 10.3 for the
                // Mixamo block it replaces (2026-09-23). Near enough the same move.
                //
                // Trigger measured on the shield hand's FORWARD speed, not its total speed
                // or its reach, because both of those mislead here: the shield drives
                // forward in the first fifth of the clip (7.1 then 4.2 m/s) and then simply
                // holds, creeping to its furthest point at 0.69 long after the blow. It has
                // arrived by 0.18, so that is where the stun lands.
                Clip = "Shield_OneShot", Trigger = 0.18f, Audio = DemoAudioWiring.ShieldBash,
                // A short smear off the shield's rim through the thrust, not the hold after it.
                ActivateEffect = DemoSkillEffectBuilder.ShieldBashTrailName,
                Glyph = Glyph.Shield },

            new SkillSpec { Name = "Charge", Title = "Charge", Class = Warrior, LearnLevel = 5,
                Description = "Closes the ground to your target at a run, and lands on arrival.",
                Shape = Shape.Dash, Weapon = null,
                // Free, and it earns rage: the arrival hit is a blow paid for with nothing, so the
                // rule credits it like a swing. WoW's Charge is how a warrior starts a fight with rage.
                Mp = 0, MpPerLevel = 0, Cooldown = 16f, CooldownPerLevel = 1f,
                Min = 10f, Max = 15f, PerLevel = 4f,
                // The arrival shoves the target back (2026-10-01, user): a charge that
                // stops dead at the enemy's toes read as a run that happened to end. Harder
                // than Shield Bash's 6 - that one measured 0.85 m on a wolf - because this
                // is the whole warrior arriving at eleven metres a second, not a shield.
                Knockback = 9f,
                // The warrior's answer to an archer. Twelve metres is a little under the
                // bandits' 14-metre sight, so a charge begun on sight arrives.
                Distance = 12f,
                // UAL2's shield-forward sprint - a warrior running in behind his shield,
                // which is what a charge is. It replaced a Mixamo sprint on 2026-09-23, which
                // itself replaced UAL1's `Roll`, a dodge that read as a tumble.
                //
                // It loops, and it has to: the trigger is what STARTS the dash
                // (`SimpleDashAttackSkill.ApplySkillImplement` applies the force there),
                // and the force then runs on its own timing - `CalculateDuration` solves
                // for the distance to the target, so closing the full twelve metres takes
                // about a second against this clip's 0.67. A one-shot would finish with
                // the character still travelling.
                //
                // Triggered early, at 0.15, so the launch is within a few frames of the
                // run starting. `Roll`'s 0.35 on a 1.47s clip meant half a second of
                // winding up before anything moved, which is not what a charge is.
                Clip = "Sprint_Shield_Loop", Trigger = 0.15f, Audio = DemoAudioWiring.PunchSwing,
                // Dirt kicked up at the feet for as long as the run lasts; built by
                // DemoSkillEffectBuilder.BuildChargeDust. Played when the clip starts, so it
                // waits out the 0.15s to the trigger on its own.
                ActivateEffect = DemoSkillEffectBuilder.ChargeDustName,
                Glyph = Glyph.Chevrons },

            new SkillSpec { Name = "DefensiveStance", Title = "Defensive Stance", Class = Warrior, LearnLevel = 6,
                Description = "Guard up and feet set: much harder to hurt and quicker to block, but slow on your feet. " +
                              "Press again to drop it.",
                Shape = Shape.Support, BuffTo = BuffTo.Toggle,
                // Free to switch, so it is a decision made per fight rather than per session - and it
                // has to be: rage is empty out of combat, so a stance that cost rage could only be
                // taken up after the fight had started. WoW's stances are free too.
                Mp = 0, Cooldown = 3f,
                // Twenty armour at level one is a knight's cuirass and a half, which takes about a
                // sixth off every blow. Block: 15% of blows at level one (+3% a level), each of
                // them halved. The halving is what makes the chance worth anything - see
                // BuffBlockDamage; without it a block took 5% off and the stance's block was cosmetic.
                BuffArmor = 20f, BuffArmorPerLevel = 5f, BuffBlock = 0.15f, BuffBlockPerLevel = 0.03f,
                BuffBlockDamage = 0.5f,
                BuffMoveRate = -0.3f,
                // UAL2's guard: the sword comes up across the body and holds. The stance goes on
                // about a third of the way in, as the guard comes up; nothing is thrown, so the
                // exact frame matters less than it does for a swing.
                Clip = "Sword_Block", Trigger = 0.35f,
                Glyph = Glyph.Guard },

            new SkillSpec { Name = "RallyingCry", Title = "Rallying Cry", Class = Warrior, LearnLevel = 8,
                Description = "A shout that puts weight behind every blade nearby, yours and your party's.",
                Shape = Shape.Support, Weapon = null,
                // 10 rage, WoW's Battle Shout.
                Mp = 10, MpPerLevel = 0, Cooldown = 45f, CooldownPerLevel = 1.5f,
                BuffTo = BuffTo.NearbyAllies, BuffDistance = 14f, BuffSeconds = 20f,
                BuffDamage = 4, BuffDamagePerLevel = 2, BuffMoveRate = 0.1f,
                // UAL1's `Celebration` - both arms thrown up over the head - which reads as
                // a war cry once there is a shout under it. It was replaced by a Mixamo
                // flourish for a week and came back on 2026-09-23, when the Mixamo clips
                // had to come out; UAL2 has nothing closer.
                //
                // Measured by hand height against the head, because the gesture is upward,
                // not forward: the arms clear the head at 0.3s, stay up until about 1.6s,
                // and spend the remaining 2.4s of the 4s clip coming down. The character is
                // rooted for the clip's whole length, and four seconds of standing still for
                // a shout is far too long - so it plays `Celebration_Rally`, the first 2.2s
                // cut as a clip of its own (see DemoAnimationSet.Trimmed), at normal speed.
                // The buff lands at 0.14, as the arms clear the head.
                //
                // Not sped up with ClipSpeed, which was the first attempt: the kit applies a
                // skill clip's speed twice, so 2x played at 4x and the arms were up for a
                // sixth of a second. Measured live, then trimmed instead.
                Clip = "Celebration_Rally", Trigger = 0.14f, Audio = DemoAudioWiring.Shout,
                Glyph = Glyph.Banner },

            // ---- Ranger: everything is a shot, and the good ones are worth standing still for ----

            new SkillSpec { Name = "AimedShot", Title = "Aimed Shot", Class = Ranger, LearnLevel = 1,
                Description = "A drawn, deliberate shot. Slower than loosing, and worth it.",
                Shape = Shape.Missile, Weapon = "Bow", Arrows = 1, Missile = "ArrowAimed",
                // 35 focus, WoW's Aimed Shot: a third of the bar, back in six seconds.
                Mp = 35, MpPerLevel = 0, Cooldown = 7f, CooldownPerLevel = 0.4f,
                WeaponRate = 1.6f, WeaponRatePerLevel = 0.2f,
                Distance = 22f,
                // The draw IS the cast, held a quarter of a second at full stretch while the
                // arrowhead gathers light, and the shot is the release alone - which starts at
                // full stretch and looses 0.175s in.
                //
                // Until 2026-09-25 this cast for 0.45s on the draw and then played the bow's whole
                // joined shot, which begins with the arms at rest: the archer got 40% of the way
                // into a draw, dropped the bow and drew again, and loosed 1.66s after the key. Now
                // it looses at 1.59s and draws once.
                Cast = DemoAnimationSet.BowDrawSeconds + 0.25f,
                Clip = "Bow_Release", Trigger = DemoAnimationSet.ReleaseTrigger, CastClip = "Bow_Charge",
                Glyph = Glyph.Arrow,
                CastEffect = "FX_AimedDraw", ReleaseEffect = "FX_AimedRelease", HitEffect = "FX_HitAimed" },

            new SkillSpec { Name = "KeenEye", Title = "Keen Eye", Class = Ranger, LearnLevel = 2, Passive = true,
                Description = "An eye for the soft spot. Critical hits come more often, and land harder.",
                Shape = Shape.Support,
                BuffCrit = 0.03f, BuffCritPerLevel = 0.02f, BuffCritDamage = 0.1f, BuffCritDamagePerLevel = 0.05f,
                Glyph = Glyph.Eye },

            new SkillSpec { Name = "CripplingShot", Title = "Crippling Shot", Class = Ranger, LearnLevel = 3,
                Description = "Takes the legs out of whatever is running at you.",
                Shape = Shape.Missile, Weapon = "Bow", Arrows = 1, Missile = "ArrowCrippling",
                // 20 focus.
                Mp = 20, MpPerLevel = 0, Cooldown = 12f, CooldownPerLevel = 0.5f,
                WeaponRate = 0.9f, WeaponRatePerLevel = 0.1f,
                Distance = 20f,
                BuffSeconds = 5f, SlowRate = 0.4f,
                // The bow's own shot, as quick as a plain one: this is the arrow loosed at
                // something already running in. The roots at the victim's feet are the slow,
                // there for exactly as long as it is. With no cast, the draw's glow and creak are
                // the activate effect, which the kit plays as that shot's draw begins.
                Clip = null, Glyph = Glyph.SnareArrow,
                ActivateEffect = "FX_CripplingDraw",
                ReleaseEffect = "FX_CripplingRelease", HitEffect = "FX_HitCrippling", DebuffEffect = "FX_Crippled" },

            new SkillSpec { Name = "Volley", Title = "Volley", Class = Ranger, LearnLevel = 5,
                Description = "Arrows into a patch of ground, and keeps them coming.",
                Shape = Shape.Area, Weapon = "Bow",
                // A fan of arrows goes up at the loose (FX_VolleyRelease), so it costs a handful.
                Arrows = 5,
                // 45 focus: the ranger's big one. With everything on cooldown the four shots cost
                // about 10 focus a second against 6 back, so a long fight runs the bar down.
                Mp = 45, MpPerLevel = 0, Cooldown = 20f, CooldownPerLevel = 0.8f,
                Min = 6f, Max = 9f, PerLevel = 2f,
                Distance = 18f, Radius = 4f,
                BuffSeconds = 3f,
                // Drawn and loosed HIGH (2026-09-25): `Bow_Draw_High`/`Bow_Release_High`, the bow's
                // own pair leaned back to put the arrow up at 42-45 degrees (DemoAnimationSet.
                // EnsureHighAngleClips). It used to shoot level, cast on half a draw, then draw
                // again from rest - 1.8s to the loose; now 1.34s.
                Cast = DemoAnimationSet.BowDrawSeconds,
                Clip = "Bow_Release_High", Trigger = DemoAnimationSet.ReleaseTrigger, CastClip = "Bow_Charge_High",
                Glyph = Glyph.Volley,
                // A fan of arrows going up at the loose, and their rain coming down on the patch
                // (FX_VolleyRain, set off where the area lands) in waves timed to its bites. Until
                // 2026-09-25 there were no arrows in it at all, and the patch was Frost-blue.
                CastEffect = "FX_VolleyDraw", ReleaseEffect = "FX_VolleyRelease", LandEffect = DemoSkillEffectBuilder.VolleyRainName, KeepMarker = true,
                AreaColour = DemoSkillEffectBuilder.Timber, HitEffect = "FX_HitArrow" },

            new SkillSpec { Name = "FleetOfFoot", Title = "Fleet of Foot", Class = Ranger, LearnLevel = 6,
                Description = "Run light and fast across open country. It breaks the moment you loose an arrow or take a hit.",
                Shape = Shape.Support, BuffTo = BuffTo.Toggle,
                // Free, like WoW's Aspect of the Cheetah: it is a travel form, not a shot.
                Mp = 0, Cooldown = 3f,
                BuffMoveRate = 0.25f, BuffMoveRatePerLevel = 0.03f,
                // A travelling pace, not a fighting one: without these it would simply make the
                // ranger faster than everything on the island, forever. With them it gets you to
                // the fight and ends there.
                BreaksOnAttack = true, BreaksWhenHit = true,
                // UAL1's lean into a sprint, which is exactly the gesture.
                Clip = "Sprint_Enter", Trigger = 0.3f,
                Glyph = Glyph.Stride },

            new SkillSpec { Name = "HuntersMark", Title = "Hunter's Mark", Class = Ranger, LearnLevel = 8,
                Description = "Marks the quarry. It bleeds, and it stops being hard to hit.",
                Shape = Shape.Missile, Weapon = "Bow", Arrows = 1, Missile = "ArrowMark",
                // 15 focus.
                Mp = 15, MpPerLevel = 0, Cooldown = 15f, CooldownPerLevel = 0.5f,
                Min = 2f, Max = 4f, PerLevel = 1f,
                Distance = 22f,
                // The bleed is 4 damage in all at level one and 1 more a level - which is what it has
                // always actually done, although this said "4 a second" until 2026-10-05 (see
                // SkillSpec.DamagePerSecond). Kept as it plays rather than raised tenfold unasked: a
                // real 4 a second is 40 a mark, a ranger balance call.
                BuffSeconds = 10f, DamagePerSecond = 0.4f, DamagePerSecondPerLevel = 0.1f, EvasionRate = 0.25f,
                // The mark itself hangs over the quarry for all ten seconds - it is the thing
                // the party is meant to see - and the Bleeding it also leaves drips under it
                // (DemoCombatDataBuilder).
                Clip = null, Glyph = Glyph.Mark,
                ReleaseEffect = "FX_MarkRelease", HitEffect = "FX_HitMark", DebuffEffect = "FX_HuntersMark" },

            // ---- Mage: the only class whose damage is its own rather than its weapon's ----

            // Arcane rather than fire, because the missile it throws is the demo's
            // SpellBolt: a blue emissive ball. A skill called Firebolt that loosed that
            // would be the name arguing with the screen.
            new SkillSpec { Name = "ArcaneBolt", Title = "Arcane Bolt", Class = Mage, LearnLevel = 1,
                Description = "The first thing an apprentice learns, and the last thing they stop using.",
                Shape = Shape.Missile, Weapon = "Staff", Missile = "SpellBolt",
                //
                // Costs 10% of the pool on top of the flat 10 (2026-10-06): the user watched the
                // mage cast and never saw the meter move. Flat 10 of 184 was 5%, and the mage's
                // ~7 mana a second of regeneration (5.2 from the stat, 1.8 the rule's 1% of the
                // pool) refilled it before the next cast was off cooldown. Now 28 at level one -
                // 15% a bolt, about 2.3 a second more than it regenerates - and it keeps costing
                // a tenth as the pool grows.
                Mp = 10, MpRate = 0.10f, MpPerLevel = 3, Cooldown = 3f, CooldownPerLevel = 0.15f, Cast = 0.6f,
                // Raised from 16-22 on 2026-09-23, when the staff stopped firing bolts of its
                // own: the mage's damage now comes from its spells, on their cooldowns, with a
                // weak staff swing between them. Intelligence adds to all three attacking
                // spells on top of this (DemoProgressionBuilder.SpellPower).
                Min = 20f, Max = 26f, PerLevel = 6f,
                Distance = 20f,
                Clip = "Spell_Simple_Shoot", Trigger = 0.45f, CastClip = "Spell_Simple_Idle_Loop",
                Audio = DemoAudioWiring.SpellCast, Glyph = Glyph.Bolt,
                CastEffect = "FX_ArcaneCast", ActivateEffect = "FX_ArcaneRelease",
                HitEffect = "FX_HitArcane" },

            new SkillSpec { Name = "DeepReserves", Title = "Deep Reserves", Class = Mage, LearnLevel = 2, Passive = true,
                Description = "A deeper well to draw from. More mana, and it comes back faster.",
                Shape = Shape.Support,
                BuffMp = 15f, BuffMpPerLevel = 15f, BuffMpRegen = 0.5f, BuffMpRegenPerLevel = 0.5f,
                Glyph = Glyph.Well },

            new SkillSpec { Name = "FrostNova", Title = "Frost Nova", Class = Mage, LearnLevel = 3,
                Description = "Cold off the floor in every direction, and everything in it frozen where it stands.",
                Shape = Shape.Area, Weapon = "Staff",
                // 12% of the pool on top of the flat 16 (2026-10-06, see Arcane Bolt): a fifth of
                // the bar at level three, on a 15s cooldown it can afford.
                Mp = 16, MpRate = 0.12f, MpPerLevel = 4, Cooldown = 15f, CooldownPerLevel = 0.6f,
                // One burst since 2026-09-23 (`Burst`). It used to leave its patch down for
                // as long as the slow and bite every 0.75s of it: 10-14 four or five times,
                // about 60 a cast, which out-hit Meteor. Now it hits once, harder - between
                // Arcane Bolt (one target, every 3s) and Meteor (the big one, every 25s).
                Min = 18f, Max = 24f, PerLevel = 5f,
                Burst = true,
                // Cast at the mage's own feet, which is the whole shape of the skill: it is
                // what a mage does when something has already reached them.
                Distance = 0f, Radius = 4.5f,
                // The mage's crowd control (2026-09-23): three seconds frozen - the kit's Freeze,
                // so no moving, no attacking, no casting - where it was a 50% slow that kept
                // biting. Enough to step clear and get a Meteor (1.4s) and a Bolt off unhit,
                // which is the whole point now that the mage fights at arm's length. The Frost
                // element's own chill still lands with the hit.
                BuffSeconds = 3f, Freeze = true,
                Clip = "Spell_Simple_Shoot", Trigger = 0.4f, Audio = DemoAudioWiring.SkillImpact,
                Glyph = Glyph.Nova,
                // The caster draws the cold in (FX_FrostNova); the nova goes off where the area lands,
                // as ice breaking out of frosted ground (2026-09-25) - it was a white disc and a flash.
                ActivateEffect = "FX_FrostNova", LandEffect = DemoSkillEffectBuilder.FrostNovaBurstName,
                AreaColour = DemoSkillEffectBuilder.Frost, HitEffect = "FX_HitFrost" },

            new SkillSpec { Name = "Mend", Title = "Mend", Class = Mage, LearnLevel = 5,
                Description = "Closes a wound - your own, or the one standing in front of you.",
                Shape = Shape.Support, Weapon = "Staff",
                // The one player cast that holds through damage. Every other skill in the
                // demo breaks when its caster is hit, which is correct for a bolt and
                // absurd for a heal: being hit is the whole reason anyone casts this, and
                // interruptible it would never land once in a fight.
                Mp = 22, MpPerLevel = 5, Cooldown = 8f, CooldownPerLevel = 0.3f, Cast = 1f,
                CastCannotBeInterrupted = true,
                BuffTo = BuffTo.Ally, BuffDistance = 15f, BuffSeconds = 1f,
                HealHp = 30, HealHpPerLevel = 12,
                Clip = "Spell_Simple_Shoot", Trigger = 0.5f, CastClip = "Spell_Simple_Idle_Loop",
                Audio = DemoAudioWiring.SpellCast, Glyph = Glyph.Cross,
                CastEffect = "FX_MendCast", ActivateEffect = "FX_MendBloom" },

            new SkillSpec { Name = "ArcaneWard", Title = "Arcane Ward", Class = Mage, LearnLevel = 6,
                Description = "A skin of force that turns blows aside. While it holds, your mana stops coming back " +
                              "and the ward feeds on it. It fails when you run dry.",
                Shape = Shape.Support, BuffTo = BuffTo.Toggle,
                Mp = 10, Cooldown = 3f,
                // More armour than the warrior's stance, because the mage has none of his own and
                // pays for it by the second - the choice is the ward or the spells.
                //
                // The drain has to beat the mage's regeneration, which is mostly Intelligence and is
                // large: measured live at level eight, 16 a second (11 from the stat, the rest the
                // rule's 1% of the pool). The first cut drained 3 and the ward was free. So it shuts
                // the stat's share off and burns 10: about 5 a second net at level eight, a minute
                // and a half of a full pool standing still, well under one while casting.
                BuffArmor = 30f, BuffArmorPerLevel = 6f, DrainMp = 10, StopsMpRegen = true,
                // UAL1's two-handed spell stance coming up, which is the hands raising the ward.
                Clip = "Spell_Double_Enter", Trigger = 0.8f, Audio = DemoAudioWiring.SpellCast,
                Glyph = Glyph.Ward },

            new SkillSpec { Name = "Meteor", Title = "Meteor", Class = Mage, LearnLevel = 8,
                Description = "Slow to call down, and worth the wait if it lands on the right patch of ground.",
                Shape = Shape.Area, Weapon = "Staff",
                // 15% of the pool on top of the flat 32 (2026-10-06, see Arcane Bolt): the big
                // spell takes about a fifth of the bar.
                Mp = 32, MpRate = 0.15f, MpPerLevel = 6, Cooldown = 25f, CooldownPerLevel = 1f, Cast = 1.4f,
                Min = 32f, Max = 42f, PerLevel = 10f,
                Distance = 18f, Radius = 5f,
                // The meteor (2026-09-24): released at the end of the cast, it comes down out of
                // the sky over this long and the damage lands with it. Until then the skill had no
                // meteor at all - a disc, a spray of sparks, and a hit a second later. The 1.2s is
                // the delay the damage always had, now with something to watch through it.
                FallSeconds = 1.2f,
                // UAL1's two-handed spell pair: the staff held out in both hands through the
                // cast, then pushed forward on the launch - the same pair the Hierophant summons with,
                // which is at least a family resemblance. The one-handed `Spell_Simple` set
                // is the mage's everyday bolt, so the two-handed one is what marks this as
                // the thing worth standing still for. A Mixamo pair stood in for a week and
                // came out on 2026-09-23; neither Quaternius library has a better cast.
                //
                // The cast clip loops, so it covers the 1.4s cast whatever its length. The
                // launch is short (0.27s) and fires where the hands reach furthest forward,
                // measured at 0.52 of it. **That makes the finish a quick push, not a throw**:
                // measured live against the Mixamo pair it replaced, the cast is identical and
                // the whole skill ends 1.4s sooner, entirely because the Mixamo launch was a
                // 1.7s two-handed throw. A longer launch is the thing to look for if this
                // reads too slight for the island's biggest spell.
                //
                // The hands get the ordinary cast sound; Meteor.wav - a whoosh that booms - goes
                // with the falling rock, timed so the boom is the landing (FallSeconds).
                Clip = "Spell_Double_Shoot_Loop", Trigger = 0.5f, CastClip = "Spell_Double_Idle_Loop",
                Audio = DemoAudioWiring.SpellCast, Glyph = Glyph.Meteor,
                CastEffect = "FX_MeteorCast", ActivateEffect = "FX_MeteorLaunch",
                AreaColour = DemoSkillEffectBuilder.Ember, HitEffect = "FX_HitEmber" },

            // ---- The Hierophant, who holds the crypt's sanctum ------------------
            //
            // Three skills that between them give the fight a shape it did not have.
            // He had 645 health at the level the crypt is pitched at and one staff
            // attack, so the fight was long and flat: stand still, hold the attack key,
            // watch a bar go down. Now it has three phases, and they come out of the
            // kit's own settings rather than out of a script.
            //
            // Full health: the nova only, which lands at the player's feet and is the
            // one thing in the demo that says move. Below three quarters: he starts
            // calling cultists, so the room fills. Below half: he begins mending
            // himself, and the fight becomes a race he can win.
            //
            // The rates are NOT independent - see WriteMonsterSkills - and the health
            // gates do most of the work of separating them.

            new SkillSpec { Name = "UnholyNova", Title = "Unholy Nova", Monster = "Hierophant",
                Description = "The floor goes cold where he points.",
                Shape = Shape.Area, Weapon = "Staff", MaxLevel = 10,
                UseRate = 0.4f,
                Mp = 0, Cooldown = 9f,
                Min = 14f, Max = 20f, PerLevel = 2.5f,
                Distance = 14f, Radius = 4.5f,
                BuffSeconds = 3f, SlowRate = 0.35f,
                Clip = "Spell_Simple_Shoot", Trigger = 0.4f, Audio = DemoAudioWiring.SkillImpact,
                Glyph = Glyph.Sigil,
                ActivateEffect = "FX_UnholyRelease", AreaColour = DemoSkillEffectBuilder.Unholy,
                HitEffect = "FX_HitUnholy" },

            new SkillSpec { Name = "CallTheFaithful", Title = "Call the Faithful", Monster = "Hierophant",
                Description = "He is never alone for long.",
                Shape = Shape.Support, Weapon = "Staff", MaxLevel = 10,
                // Below three quarters, so the first stretch of the fight is his alone.
                UseRate = 0.25f, UseWhenHpRate = 0.75f,
                Mp = 0, Cooldown = 35f, Cast = 1.2f, CastCannotBeInterrupted = true,
                SummonEntity = "DemoCultistMale", SummonCount = 2, SummonMaxStack = 2, SummonSeconds = 40f,
                Clip = "Spell_Double_Shoot_Loop", Trigger = 0.5f, CastClip = "Spell_Double_Idle_Loop",
                Audio = DemoAudioWiring.SpellCast, Glyph = Glyph.Coven,
                CastEffect = "FX_UnholyCast", ActivateEffect = "FX_UnholyRelease" },

            new SkillSpec { Name = "RiteOfMending", Title = "Rite of Mending", Monster = "Hierophant",
                Description = "What the crypt takes, it gives back to him.",
                Shape = Shape.Support, Weapon = "Staff", MaxLevel = 10,
                UseRate = 0.3f, UseWhenHpRate = 0.5f,
                Mp = 0, Cooldown = 22f, Cast = 1.6f, CastCannotBeInterrupted = true,
                BuffTo = BuffTo.Self, BuffSeconds = 1f,
                // A fifth of his pool at the level the crypt is pitched at: enough that
                // letting it happen twice costs the fight, not so much that it cannot be
                // out-damaged.
                HealHp = 60, HealHpPerLevel = 8,
                Clip = "Spell_Simple_Shoot", Trigger = 0.5f, CastClip = "Spell_Simple_Idle_Loop",
                Audio = DemoAudioWiring.SpellCast, Glyph = Glyph.Vessel,
                CastEffect = "FX_UnholyCast", ActivateEffect = "FX_UnholyMend" },

            // ---- the cultists ---------------------------------------------------
            //
            // Their basic attack is already a spell (a violet bolt every couple of seconds - see the
            // Cultist MonsterSpec in DemoDatabaseWiring); this is the one they stop for. Both hands
            // up, the Hierophant's violet gathering on the body for a second and a half, and a
            // fatter bolt that leaves its mark burning on whoever it hit. The point of it is the
            // cast: it can be seen coming and a hit can break it (35% a hit, the same rule as every
            // cast - InterruptChanceUseSkillComponent), so it is the cultist's tell and the
            // player's opening. Interrupted, it still costs its cooldown: the kit starts that when
            // the cast begins.
            //
            // A monster's debuff always lands at skill level 1 - the kit applies its skills at
            // level one (`BaseSkill.ApplySkill`), though the hit itself is computed at the
            // monster's level - so the curse is 4 a second for 5 seconds at every level, and the
            // growth is all in the hit.
            new SkillSpec { Name = "WitheringHex", Title = "Withering Hex", Monster = "Cultist",
                Description = "A curse thrown from both hands. It goes on hurting after it lands.",
                Shape = Shape.Missile, Weapon = "Staff", Missile = "HexBolt", MaxLevel = 10,
                // Reached for on three decisions in ten once it is off cooldown - so about every
                // fifteen to twenty seconds a fight, the bolts between.
                UseRate = 0.3f,
                Mp = 0, Cooldown = 12f, Cast = 1.5f,
                Min = 6f, Max = 9f, PerLevel = 1.5f,
                Distance = 16f,
                BuffSeconds = 5f, DamagePerSecond = 4f,
                // The two-handed pair Meteor and Call the Faithful use: held through the cast and
                // pushed out on the launch, so it reads as a different thing from the one-handed
                // flick of the ordinary bolt.
                Clip = "Spell_Double_Shoot_Loop", Trigger = 0.5f, CastClip = "Spell_Double_Idle_Loop",
                Audio = DemoAudioWiring.SpellCast, Glyph = Glyph.Sigil,
                CastEffect = "FX_UnholyCast", ActivateEffect = "FX_UnholyRelease",
                HitEffect = "FX_HitUnholy", DebuffEffect = "FX_Withering" },
        };

        [MenuItem("Open MMORPG/Demo/Build Skills")]
        public static void BuildAll()
        {
            DemoItemBuilder.EnsureFolder(SkillDir);
            DemoItemBuilder.EnsureFolder(IconDir);
            DemoItemBuilder.EnsureFolder(AreaDir);

            Texture2D areaSprite = BuildAreaSprite();
            Texture2D aimSprite = BuildAimSprite();
            foreach (SkillSpec spec in Specs)
                Build(spec, areaSprite, aimSprite);
            BuildAutoAttack();
            WriteReleaseTable();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[{nameof(DemoSkillBuilder)}] Built {Specs.Length} skills." +
                      (adoptedIcons == 0
                          ? string.Empty
                          : $" Kept {adoptedIcons} icon(s) already in {IconDir} rather than drawing over them; " +
                            "run Regenerate Skill Icons to replace them with drawn ones."));
            adoptedIcons = 0;
        }

        // ---- the assets -------------------------------------------------------

        private static void Build(SkillSpec spec, Texture2D areaSprite, Texture2D aimSprite)
        {
            string path = $"{SkillDir}/{spec.Name}.asset";
            BaseSkill skill;
            switch (spec.Shape)
            {
                case Shape.Area:
                    skill = Create<SimpleAreaAttackSkill>(path);
                    break;
                case Shape.Dash:
                    skill = Create<SimpleDashAttackSkill>(path);
                    break;
                default:
                    skill = Create<Skill>(path);
                    // A skill that is a multiple of the weapon runs on the demo's class, which
                    // multiplies the character's whole swing rather than the weapon's raw
                    // item damage - see SwingScaledWeaponSkill. Swapped in place so the asset keeps
                    // its id and everything that points at it.
                    skill = EnsureSkillClass(path, skill, spec.Shape != Shape.Support && spec.WeaponRate > 0f);
                    break;
            }

            var serialized = new SerializedObject(skill);
            WriteCommon(serialized, spec);

            switch (spec.Shape)
            {
                case Shape.Area:
                    WriteAreaSkill(serialized, spec, areaSprite, aimSprite);
                    break;
                case Shape.Dash:
                    WriteDashSkill(serialized, spec);
                    break;
                default:
                    WritePlainSkill(serialized, spec);
                    break;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(skill);
        }

        /// <summary>Where the release effects are listed; the bow prefab points at it (DemoWeaponBuilder).</summary>
        public const string ReleaseTablePath = "Assets/OpenMMORPG/Demo/Prefabs/Effects/Skills/SkillReleaseEffects.asset";

        /// <summary>The release table, made empty if it is not there yet - the weapon builder runs first.</summary>
        public static SkillReleaseEffects ReleaseTable()
        {
            var table = AssetDatabase.LoadAssetAtPath<SkillReleaseEffects>(ReleaseTablePath);
            if (table != null)
                return table;
            DemoItemBuilder.EnsureFolder(System.IO.Path.GetDirectoryName(ReleaseTablePath).Replace('\\', '/'));
            table = ScriptableObject.CreateInstance<SkillReleaseEffects>();
            AssetDatabase.CreateAsset(table, ReleaseTablePath);
            return table;
        }

        /// <summary>
        /// Lists each skill's <see cref="SkillSpec.ReleaseEffect"/> for BowEquipmentEntity to play on the
        /// trigger. Rewritten in place, so the bow's reference to it survives.
        /// </summary>
        private static void WriteReleaseTable()
        {
            var entries = new List<SkillReleaseEffects.Entry>();
            foreach (SkillSpec spec in Specs)
            {
                if (string.IsNullOrEmpty(spec.ReleaseEffect))
                    continue;
                BaseSkill skill = Asset(spec.Name);
                GameEffect effect = DemoSkillEffectBuilder.Effect(spec.ReleaseEffect);
                if (skill == null || effect == null)
                    continue;
                entries.Add(new SkillReleaseEffects.Entry { skill = skill, effects = new[] { effect } });
            }
            SkillReleaseEffects table = ReleaseTable();
            table.entries = entries.ToArray();
            EditorUtility.SetDirty(table);
            AssetDatabase.SaveAssetIfDirty(table);
        }

        /// <summary>
        /// The Attack button: WoW's auto-attack toggle, which every class gets at level one and
        /// DemoAutoHotkeys pins to key 1. Kept out of <see cref="Specs"/> on purpose - that table
        /// also feeds the animation set and the monsters, and this is never cast: the player
        /// controller intercepts the hotkey (see DemoAutoAttackSkill).
        /// </summary>
        private static readonly SkillSpec AutoAttackSpec = new SkillSpec
        {
            Name = "AutoAttack", Title = "Attack", Class = Everyone, LearnLevel = 1, MaxLevel = 1,
            Description = "Attack your target until it falls, or press again to stop. " +
                          "With nothing targeted, attacks the nearest enemy. Attacking skills start it too.",
            Shape = Shape.Support, Glyph = Glyph.CrossedSwords,
        };

        private static void BuildAutoAttack()
        {
            string path = $"{SkillDir}/{AutoAttackSpec.Name}.asset";
            var skill = Create<DemoAutoAttackSkill>(path);
            var serialized = new SerializedObject(skill);
            WriteCommon(serialized, AutoAttackSpec);
            serialized.FindProperty("skillAttackType").enumValueIndex = (int)Skill.SkillAttackType.None;
            // Handed over by the class, never bought.
            Set(serialized, "requirement.characterLevel.amountIncreaseEachLevel", 0);
            Set(serialized, "requirement.skillPoint.baseAmount", 0f);
            Set(serialized, "requirement.skillPoint.amountIncreaseEachLevel", 0f);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(skill);
        }

        /// <summary>Everything every skill carries, whichever class of asset it is.</summary>
        private static void WriteCommon(SerializedObject serialized, SkillSpec spec)
        {
            // The id is hashed into the data id a character's learned skills are stored
            // under. Two skills sharing one and the bar loads the wrong one; leaving one
            // empty and it loads nothing.
            serialized.FindProperty("id").stringValue = spec.Name;
            serialized.FindProperty("defaultTitle").stringValue = spec.Title;
            serialized.FindProperty("defaultDescription").stringValue = spec.Description ?? string.Empty;
            serialized.FindProperty("icon").objectReferenceValue = BuildIcon(spec);

            // Active or passive is a field on the ordinary skill and a hard-coded property
            // on the area and dash ones, which are active by construction - so this is a
            // field that is simply not there on two of the three classes, rather than one
            // that has been renamed. Asked for by name it comes back null there - and the
            // passives are all plain `Skill`s, so they always find it.
            SerializedProperty skillType = serialized.FindProperty("skillType");
            if (skillType != null)
                skillType.enumValueIndex = (int)(spec.Passive ? SkillType.Passive : SkillType.Active);
            serialized.FindProperty("maxLevel").intValue = spec.MaxLevel > 0 ? spec.MaxLevel : MaxSkillLevel;

            Set(serialized, "consumeMp.baseAmount", spec.Mp);
            Set(serialized, "consumeMp.amountIncreaseEachLevel", spec.MpPerLevel);
            Set(serialized, "consumeMpRate.baseAmount", spec.MpRate);
            Set(serialized, "consumeMpRate.amountIncreaseEachLevel", 0f);
            Set(serialized, "coolDownDuration.baseAmount", spec.Cooldown);
            Set(serialized, "coolDownDuration.amountIncreaseEachLevel", -spec.CooldownPerLevel);
            Set(serialized, "castDuration.baseAmount", spec.Cast);
            // The kit interrupts a cast on ANY damage received, not on some dedicated
            // interrupt - `BaseCharacterEntity.ReceivedDamage` calls straight through to
            // `InterruptCastingSkill`. So this flag is not a nicety: on anything cast
            // while being hit it is the difference between a skill and a skill that never
            // once completes. The offensive casts keep it, because "do not stand in melee
            // and cast" is a thing worth teaching; the heal and the boss's two do not,
            // because being hit is the entire circumstance they are used in.
            serialized.FindProperty("canBeInterruptedWhileCasting").boolValue =
                spec.Cast > 0f && !spec.CastCannotBeInterrupted;
            // Rooted while it goes off. The kit reads this as a rate of the ordinary move
            // speed, so zero is a stand-still; it is what makes a long cast a decision.
            serialized.FindProperty("moveSpeedRateWhileUsingSkill").floatValue = 0f;

            // What it costs to learn and to raise. The first level of a granted skill is
            // handed over by the class rather than bought, so this is the price of the
            // second and up - and of the first, for the three that are not granted.
            Set(serialized, "requirement.characterLevel.baseAmount", spec.LearnLevel);
            // Two character levels for each skill level past the first: the kit pays one
            // skill point a level, so this is what stops a character pouring everything
            // into one skill and leaving the other three at nothing.
            Set(serialized, "requirement.characterLevel.amountIncreaseEachLevel", 2);
            Set(serialized, "requirement.skillPoint.baseAmount", 1f);
            Set(serialized, "requirement.skillPoint.amountIncreaseEachLevel", 1f);

            // The particles. Both lists are private serialised fields on BaseSkill, hence
            // the by-name write; an effect aimed at a socket a character does not have is
            // dropped in silence, so see DemoSkillEffectBuilder for which sockets exist.
            WriteEffect(serialized, "skillCastEffects", spec.CastEffect);
            WriteEffect(serialized, "skillActivateEffects", spec.ActivateEffect);
            // Not on BaseSkill: `Skill` and `SimpleAreaAttackSkill` each declare their own
            // `damageHitEffects`, and `SimpleDashAttackSkill` declares none at all - which
            // is why Charge names no hit effect and takes the game instance's fallback.
            // Written empty too, so taking the effect off a spec takes it off the asset - but only
            // where the field exists, since WriteEffect warns about a missing one.
            if (serialized.FindProperty("damageHitEffects") != null)
                WriteEffect(serialized, "damageHitEffects", spec.HitEffect);

            serialized.FindProperty("requireShield").boolValue = spec.RequireShield;
            // Written every time, so a spec that stops wanting arrows stops taking them.
            serialized.FindProperty("requireAmmoType").enumValueIndex = spec.Arrows > 0
                ? (int)RequireAmmoType.BasedOnWeapon
                : (int)RequireAmmoType.None;
            serialized.FindProperty("requireAmmoAmount").intValue = spec.Arrows;
            SerializedProperty weapons = serialized.FindProperty("availableWeapons");
            weapons.ClearArray();
            if (!string.IsNullOrEmpty(spec.Weapon))
            {
                var type = AssetDatabase.LoadAssetAtPath<WeaponType>($"{ResourcesDir}/WeaponTypes/{spec.Weapon}.asset");
                if (type == null)
                {
                    Debug.LogError($"[{nameof(DemoSkillBuilder)}] {spec.Name} wants a \"{spec.Weapon}\" weapon type; " +
                                   "run Build Items first.");
                }
                else
                {
                    weapons.InsertArrayElementAtIndex(0);
                    weapons.GetArrayElementAtIndex(0).objectReferenceValue = type;
                }
            }
        }

        /// <summary>A <see cref="Skill"/>: the melee swings, the missiles, the buff and the heal.</summary>
        private static void WritePlainSkill(SerializedObject serialized, SkillSpec spec)
        {
            bool attacks = spec.Shape != Shape.Support;
            // `BasedOnWeapon` is the difference between the warrior and the mage in one
            // field: the warrior's skills are his sword's damage multiplied, so a better
            // sword is a better Cleave, while the mage's carry their own numbers and a
            // staff is only the thing that lets them be cast.
            serialized.FindProperty("skillAttackType").enumValueIndex = !attacks
                ? (int)Skill.SkillAttackType.None
                : spec.WeaponRate > 0f
                    ? (int)Skill.SkillAttackType.BasedOnWeapon
                    : (int)Skill.SkillAttackType.Normal;

            if (attacks)
            {
                WriteDamageAmount(serialized, "damageAmount", spec);
                Set(serialized, "weaponDamageMultiplicator.baseAmount", spec.WeaponRate);
                Set(serialized, "weaponDamageMultiplicator.amountIncreaseEachLevel", spec.WeaponRatePerLevel);
                WriteDamageInfo(serialized, spec);
                WriteDebuff(serialized, spec);

                // Written every time, zero or not, so taking the knockback off a spec takes it off.
                Set(serialized, "knockbackEffect.force", spec.Knockback);
                Set(serialized, "knockbackEffect.deceleration", spec.Knockback * 2f);
                // With no force the kit never applies it (`Skill`: `knockbackEffect.force > 0`),
                // so the duration goes back to the kit's own default rather than to zero.
                Set(serialized, "knockbackEffect.duration", spec.Knockback > 0f ? 0.5f : 1f);
            }

            WriteBuff(serialized, spec);
            WriteSummon(serialized, spec);
        }

        /// <summary>
        /// The bodies a skill calls up. Only the Hierophant does, and he calls the
        /// cultists that already walk the crypt - the entity is the same prefab the
        /// spawn areas use, so a summoned one fights, drops and dies like any other.
        ///
        /// The entity itself is left to <see cref="WireSummons"/>, because it does not
        /// exist yet: the character entities are built two steps after the skills are,
        /// and the skills have to come first because the character models read them. The
        /// numbers are written here; the reference is filled in at the end.
        /// </summary>
        private static void WriteSummon(SerializedObject serialized, SkillSpec spec)
        {
            if (string.IsNullOrEmpty(spec.SummonEntity))
                return;
            Set(serialized, "summon.amountEachTime.baseAmount", spec.SummonCount);
            // Capped, or a long fight ends with the sanctum wall to wall in cultists: the
            // skill is reached for on a timer that does not care how the last one went.
            Set(serialized, "summon.maxStack.baseAmount", spec.SummonMaxStack);
            Set(serialized, "summon.duration.baseAmount", spec.SummonSeconds);
            // They come in at the level of the skill that called them, which is the
            // Hierophant's own - so the adds in the crypt match the crypt.
            Set(serialized, "summon.level.baseAmount", 1);
            Set(serialized, "summon.level.amountIncreaseEachLevel", 1);
        }

        /// <summary>A <see cref="SimpleAreaAttackSkill"/>: dropped on the ground and left there.</summary>
        private static void WriteAreaSkill(SerializedObject serialized, SkillSpec spec, Texture2D areaSprite, Texture2D aimSprite)
        {
            serialized.FindProperty("skillAttackType").enumValueIndex =
                (int)SimpleAreaAttackSkill.SkillAttackType.Normal;
            WriteDamageAmount(serialized, "damageAmount", spec);

            Set(serialized, "castDistance.baseAmount", spec.Distance);
            // How long the patch lasts, and how often it bites. Volley keeps raining for
            // three seconds and applies every three quarters of one; the meteor lands
            // once and is gone.
            //
            // A burst bites exactly once. The kit's area applies its first bite one
            // `applyDuration` after it appears (not on arrival) and is put away after
            // `areaDuration`, so a patch that lives 0.5s and bites every 0.3s bites at 0.3s
            // and is gone before a second could come. The debuff it leaves is separate and
            // keeps its full `BuffSeconds`.
            //
            // A strike (the meteor) bites once, when it lands: `applyDuration` is the fall. The
            // patch then stays only a little longer - long enough for what the fireball shed on
            // the way down to burn out, and less than a second fall, so it can never bite twice.
            // It must not be the fall exactly either: until 2026-09-24 Meteor's patch lived 1.2s
            // and bit at 1.2s, so the kit's timer putting it away (`PushBack(areaDuration)`) and
            // its timer biting (`ManagedUpdate`) fell due together - a race nothing settles.
            if (spec.FallSeconds > 0f)
            {
                Set(serialized, "areaDuration.baseAmount", spec.FallSeconds + Mathf.Min(StrikeLinger, spec.FallSeconds * 0.8f));
                Set(serialized, "applyDuration.baseAmount", spec.FallSeconds);
            }
            else if (spec.Burst)
            {
                Set(serialized, "areaDuration.baseAmount", BurstSeconds);
                Set(serialized, "applyDuration.baseAmount", BurstBite);
            }
            else
            {
                Set(serialized, "areaDuration.baseAmount", spec.BuffSeconds);
                Set(serialized, "applyDuration.baseAmount", spec.BuffSeconds > 2f ? 0.75f : spec.BuffSeconds);
            }

            serialized.FindProperty("areaDamageEntity").objectReferenceValue = BuildArea(spec, areaSprite);
            // The circle under the cursor while it is aimed. Written null where there is nothing
            // to aim, so a skill that stops being aimed loses its circle too.
            serialized.FindProperty("targetObjectPrefab").objectReferenceValue = BuildAimMarker(spec, aimSprite);
            WriteDebuff(serialized, spec);
        }

        /// <summary>How long a burst's patch lives, in seconds. See <see cref="SkillSpec.Burst"/>.</summary>
        private const float BurstSeconds = 0.5f;

        /// <summary>When a burst bites: shortly after it appears, as the ring goes out.</summary>
        private const float BurstBite = 0.3f;

        /// <summary>How long a strike's patch outlasts its landing. See <see cref="SkillSpec.FallSeconds"/>.</summary>
        private const float StrikeLinger = 1f;

        /// <summary>A <see cref="SimpleDashAttackSkill"/>: the warrior's charge.</summary>
        private static void WriteDashSkill(SerializedObject serialized, SkillSpec spec)
        {
            Set(serialized, "castDistance.baseAmount", spec.Distance);
            serialized.FindProperty("dashToEnemyPosition").boolValue = true;
            serialized.FindProperty("dashToEnemyStoppingDistance").floatValue = 1.6f;
            // ReplaceMovement, not the kit's default Dash. Both carry the character the same
            // way; the only difference is that Dash raises `MovementState.IsDash` for as long
            // as the force runs, and the model answers that flag with its dash clip - which
            // in this demo is the dodge `Roll`. The sprint clip hides it for its 0.67s, and
            // then a twelve-metre charge finished with the warrior tumbling head over heels
            // into the enemy (seen frame by frame in the client/server test, 2026-09-30).
            // Without the flag the remainder of the run plays as what it is: running.
            serialized.FindProperty("forceMode").enumValueIndex = (int)ApplyMovementForceMode.ReplaceMovement;
            // Fast enough to read as a charge rather than a walk, decelerating hard enough
            // that it stops where the target is rather than sliding past him.
            Set(serialized, "forceApplierData.speed", 18f);
            Set(serialized, "forceApplierData.deceleration", 12f);
            // Only a charge with no target selected uses this: with one, the kit solves the
            // duration from the distance instead. It must not be zero. The arrival damage
            // is fired by the entity's `DashAttackHandler` (DemoEntityBuilder) when the
            // force's elapsed time reaches its duration, and a zero-duration force never
            // does - it just decelerates until it drops under walking pace and is removed,
            // and the kit does not call its listeners on the frame the list goes empty. So
            // a free-aimed charge ran eleven metres and hit nothing. 0.6s is ~8.6m, still at
            // 11 m/s when it ends, so it always ends on the clock and always lands.
            Set(serialized, "forceApplierData.duration", 0.6f);

            // The damage lands on arrival, not on the way: a charge that hurt everything
            // it passed through would be a better Cleave than Cleave.
            SerializedProperty damages = serialized.FindProperty("postDashDamageAmounts");
            damages.arraySize = 1;
            WriteDamageAmount(serialized, "postDashDamageAmounts.Array.data[0]", spec);
            // Wider than the 2.5 m it was, for a reason that only shows with a real client:
            // the dash is applied and ended on the server, but the player's position is
            // the client's (NotSecure movement), and the server's copy of it trails the
            // client by a sync tick - over a metre at charge speed. Measured 2026-09-30: the
            // client stopped 2.2 m from a deer and the server, looking up from its older
            // position, found nothing within 2.5. Four metres covers that lag and is still
            // "on arrival" against a 1.6 m stopping distance.
            serialized.FindProperty("postDashEnemyLookupRadius").floatValue = 4f;
            // The shove on arrival, applied by the kit alongside the damage above
            // (`SimpleDashAttackSkill.OnPostDashAttack`, only when force > 0). Same shape as
            // the melee writer's: written every time so a spec without one clears it, and a
            // zero force keeps the kit's default duration rather than zero. On a player it
            // is a force on a client-driven entity, which RemoteForceUpkeep now ends.
            Set(serialized, "postDashKnockbackEffect.force", spec.Knockback);
            Set(serialized, "postDashKnockbackEffect.deceleration", spec.Knockback * 2f);
            Set(serialized, "postDashKnockbackEffect.duration", spec.Knockback > 0f ? 0.5f : 1f);
        }

        // ---- the pieces they share --------------------------------------------

        private static void WriteDamageAmount(SerializedObject serialized, string path, SkillSpec spec)
        {
            // The element is left null on purpose: the kit falls back to the default
            // damage element on the game instance, and the demo has only the one.
            Set(serialized, path + ".amount.baseAmount.min", spec.Min);
            Set(serialized, path + ".amount.baseAmount.max", spec.Max);
            Set(serialized, path + ".amount.amountIncreaseEachLevel.min", spec.PerLevel);
            Set(serialized, path + ".amount.amountIncreaseEachLevel.max", spec.PerLevel);
        }

        private static void WriteDamageInfo(SerializedObject serialized, SkillSpec spec)
        {
            bool missile = spec.Shape == Shape.Missile;
            serialized.FindProperty("damageInfo.damageType").enumValueIndex =
                (int)(missile ? DamageType.Missile : DamageType.Melee);

            if (missile)
            {
                Set(serialized, "damageInfo.missileDistance", spec.Distance);
                Set(serialized, "damageInfo.missileSpeed", 38f);
                // Short of the full range, the same way the weapons are set: the kit reads
                // this as the distance at which the character will start the shot, and one
                // begun at the very edge of the range lands in empty air by the time the
                // arrow arrives.
                Set(serialized, "damageInfo.startAttackDistance", spec.Distance * 0.85f);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{MissileDir}/{spec.Missile}.prefab");
                if (prefab == null)
                {
                    Debug.LogError($"[{nameof(DemoSkillBuilder)}] {spec.Name} wants the \"{spec.Missile}\" missile; " +
                                   "run Build Items first.");
                }
                else
                {
                    serialized.FindProperty("damageInfo.missileDamageEntity").objectReferenceValue =
                        prefab.GetComponent<MissileDamageEntity>();
                }
            }
            else
            {
                Set(serialized, "damageInfo.hitDistance", spec.Distance);
                Set(serialized, "damageInfo.hitFov", spec.Fov);
                Set(serialized, "damageInfo.startAttackDistance", spec.Distance * 0.6f);
                // A bash is one enemy by definition; a cleave is not.
                serialized.FindProperty("damageInfo.hitOnlySelectedTarget").boolValue = spec.Fov <= 90f;
            }
        }

        /// <summary>What an attacking skill leaves on whatever it hit.</summary>
        private static void WriteDebuff(SerializedObject serialized, SkillSpec spec)
        {
            bool lingers = spec.SlowRate > 0f || spec.EvasionRate > 0f || spec.DamagePerSecond > 0f || spec.Stun || spec.Freeze;
            serialized.FindProperty("isDebuff").boolValue = lingers;
            if (!lingers)
                return;

            // What the victim wears while it lasts. Written every time, empty or not, so taking
            // one off a spec takes it off the asset. None of the demo's debuffs showed on their
            // target until 2026-09-25: a crippled bandit simply walked slower, for no visible reason.
            WriteEffect(serialized, "debuff.effects", spec.DebuffEffect);

            Set(serialized, "debuff.duration.baseAmount", spec.BuffSeconds);
            Set(serialized, "debuff.duration.amountIncreaseEachLevel", spec.BuffSeconds * 0.1f);
            serialized.FindProperty("debuff.ailment").enumValueIndex = (int)(spec.Freeze ? AilmentPresets.Freeze
                : spec.Stun ? AilmentPresets.Stun
                : AilmentPresets.None);
            // Written every time, so taking the slow off a skill actually takes it off.
            Set(serialized, "debuff.increaseStatsRate.baseStats.moveSpeed", -spec.SlowRate);
            // Rates, not amounts: a flat number off a move speed would stop a slow bandit
            // dead and barely trouble a deer, and the demo has both. (The slow is written
            // above, with the ailment.)
            // Both written every time too: an evasion of zero, and an empty damage-over-time list.
            Set(serialized, "debuff.increaseStatsRate.baseStats.evasion", -spec.EvasionRate);
            SerializedProperty overTime = serialized.FindProperty("debuff.damageOverTimes");
            overTime.arraySize = spec.DamagePerSecond > 0f ? 1 : 0;
            if (spec.DamagePerSecond > 0f)
            {
                // The whole of it, not a rate - see SkillSpec.DamagePerSecond.
                float total = spec.DamagePerSecond * spec.BuffSeconds;
                float totalStep = spec.DamagePerSecondPerLevel * spec.BuffSeconds;
                Set(serialized, "debuff.damageOverTimes.Array.data[0].amount.baseAmount.min", total);
                Set(serialized, "debuff.damageOverTimes.Array.data[0].amount.baseAmount.max", total);
                Set(serialized, "debuff.damageOverTimes.Array.data[0].amount.amountIncreaseEachLevel.min", totalStep);
                Set(serialized, "debuff.damageOverTimes.Array.data[0].amount.amountIncreaseEachLevel.max", totalStep);
            }
        }

        /// <summary>What a support skill puts on its friends, and what a passive adds to its owner.</summary>
        private static void WriteBuff(SerializedObject serialized, SkillSpec spec)
        {
            SerializedProperty type = serialized.FindProperty("skillBuffType");
            // A passive's stats are its `buff`, which the kit reads for any Passive skill whatever
            // its buff type (`Skill.TryGetBuff`); the type stays None so pressing it does nothing.
            if (spec.BuffTo == BuffTo.None)
            {
                type.enumValueIndex = (int)Skill.SkillBuffType.None;
                if (spec.Passive)
                    WriteBuffStats(serialized, spec);
                return;
            }
            switch (spec.BuffTo)
            {
                case BuffTo.Toggle:
                    type.enumValueIndex = (int)Skill.SkillBuffType.Toggle;
                    break;
                case BuffTo.Self:
                    type.enumValueIndex = (int)Skill.SkillBuffType.BuffToUser;
                    break;
                case BuffTo.NearbyAllies:
                    type.enumValueIndex = (int)Skill.SkillBuffType.BuffToNearbyAllies;
                    break;
                case BuffTo.Ally:
                    type.enumValueIndex = (int)Skill.SkillBuffType.BuffToAlly;
                    break;
            }

            Set(serialized, "buffDistance.baseAmount", spec.BuffDistance);
            // A heal aimed at nobody heals the healer rather than failing: the demo is
            // usually played alone, and a heal that needed a second player to do anything
            // would read as broken rather than as social.
            serialized.FindProperty("buffToUserIfNoTarget").boolValue = true;

            Set(serialized, "buff.duration.baseAmount", spec.BuffSeconds);
            // Written every time, zero or not, like WriteBuffStats below: taking the heal or the
            // damage off a spec takes it off the asset.
            Set(serialized, "buff.recoveryHp.baseAmount", spec.HealHp);
            Set(serialized, "buff.recoveryHp.amountIncreaseEachLevel", spec.HealHpPerLevel);
            SerializedProperty damages = serialized.FindProperty("buff.increaseDamages");
            damages.arraySize = spec.BuffDamage > 0 ? 1 : 0;
            if (spec.BuffDamage > 0)
            {
                Set(serialized, "buff.increaseDamages.Array.data[0].amount.baseAmount.min", spec.BuffDamage);
                Set(serialized, "buff.increaseDamages.Array.data[0].amount.baseAmount.max", spec.BuffDamage);
                Set(serialized, "buff.increaseDamages.Array.data[0].amount.amountIncreaseEachLevel.min", spec.BuffDamagePerLevel);
                Set(serialized, "buff.increaseDamages.Array.data[0].amount.amountIncreaseEachLevel.max", spec.BuffDamagePerLevel);
            }

            // A toggle has no clock: `noDuration` keeps it until it is pressed again, and makes the
            // kit treat its duration as one second - so a recovery on it is a rate per second for as
            // long as it is on (`CharacterSkillAndBuffComponent`), which is what the ward's drain is.
            bool toggle = spec.BuffTo == BuffTo.Toggle;
            serialized.FindProperty("buff.noDuration").boolValue = toggle;
            Set(serialized, "buff.recoveryMp.baseAmount", -spec.DrainMp);
            Set(serialized, "buff.removeBuffWhenAttackChance.baseAmount", spec.BreaksOnAttack ? 1f : 0f);
            Set(serialized, "buff.removeBuffWhenAttackedChance.baseAmount", spec.BreaksWhenHit ? 1f : 0f);
            WriteBuffStats(serialized, spec);
        }

        /// <summary>
        /// The stats a buff or a passive adds. Written every time, zero or not, so taking one off
        /// a spec takes it off the asset.
        /// </summary>
        private static void WriteBuffStats(SerializedObject serialized, SkillSpec spec)
        {
            const string flat = "buff.increaseStats.baseStats.";
            const string flatStep = "buff.increaseStats.statsIncreaseEachLevel.";
            const string rate = "buff.increaseStatsRate.baseStats.";
            const string rateStep = "buff.increaseStatsRate.statsIncreaseEachLevel.";

            Set(serialized, flat + "hp", spec.BuffHp);
            Set(serialized, flatStep + "hp", spec.BuffHpPerLevel);
            Set(serialized, flat + "mp", spec.BuffMp);
            Set(serialized, flatStep + "mp", spec.BuffMpPerLevel);
            Set(serialized, flat + "mpRecovery", spec.BuffMpRegen);
            Set(serialized, flatStep + "mpRecovery", spec.BuffMpRegenPerLevel);
            Set(serialized, flat + "blockRate", spec.BuffBlock);
            Set(serialized, flatStep + "blockRate", spec.BuffBlockPerLevel);
            Set(serialized, flat + "blockDmgRate", spec.BuffBlockDamage);
            Set(serialized, flat + "criRate", spec.BuffCrit);
            Set(serialized, flatStep + "criRate", spec.BuffCritPerLevel);
            Set(serialized, flat + "criDmgRate", spec.BuffCritDamage);
            Set(serialized, flatStep + "criDmgRate", spec.BuffCritDamagePerLevel);
            // A rate, not an amount, like the slows: a flat speed would mean something different
            // on every body.
            Set(serialized, rate + "moveSpeed", spec.BuffMoveRate);
            Set(serialized, rateStep + "moveSpeed", spec.BuffMoveRatePerLevel);
            Set(serialized, rate + "mpRecovery", spec.StopsMpRegen ? -1f : 0f);

            // Armour is per damage element. The element is left null, which the kit reads as the
            // game instance's default - Physical, which is what every blow on the island is.
            SerializedProperty armors = serialized.FindProperty("buff.increaseArmors");
            armors.arraySize = spec.BuffArmor != 0f || spec.BuffArmorPerLevel != 0f ? 1 : 0;
            if (armors.arraySize > 0)
            {
                serialized.FindProperty("buff.increaseArmors.Array.data[0].damageElement").objectReferenceValue = null;
                Set(serialized, "buff.increaseArmors.Array.data[0].amount.baseAmount", spec.BuffArmor);
                Set(serialized, "buff.increaseArmors.Array.data[0].amount.amountIncreaseEachLevel", spec.BuffArmorPerLevel);
            }
        }

        /// <summary>Puts one effect prefab in one of the skill's effect lists, or empties it.</summary>
        private static void WriteEffect(SerializedObject on, string field, string effectName)
        {
            SerializedProperty list = on.FindProperty(field);
            if (list == null)
            {
                Debug.LogWarning($"[{nameof(DemoSkillBuilder)}] No field \"{field}\" on {on.targetObject.name}.");
                return;
            }
            list.ClearArray();
            if (string.IsNullOrEmpty(effectName))
                return;
            GameEffect effect = DemoSkillEffectBuilder.Effect(effectName);
            if (effect == null)
                return;
            list.InsertArrayElementAtIndex(0);
            list.GetArrayElementAtIndex(0).objectReferenceValue = effect;
        }

        /// <summary>Sets one field, and says so if the kit has renamed it rather than writing nothing.</summary>
        private static void Set(SerializedObject on, string path, float value)
        {
            SerializedProperty property = on.FindProperty(path);
            if (property == null)
            {
                Debug.LogWarning($"[{nameof(DemoSkillBuilder)}] No field \"{path}\" on {on.targetObject.name}.");
                return;
            }
            if (property.propertyType == SerializedPropertyType.Integer)
                property.intValue = Mathf.RoundToInt(value);
            else
                property.floatValue = value;
        }

        // ---- what the rest of the build asks for ------------------------------

        /// <summary>The radius a skill's area is built with, for an effect that has to match it. Zero if none.</summary>
        public static float AreaRadius(string name)
        {
            foreach (SkillSpec spec in Specs)
            {
                if (spec.Name == name)
                    return spec.Radius;
            }
            return 0f;
        }

        /// <summary>Every skill asset, for registering in the game database.</summary>
        public static List<Object> AllSkills()
        {
            var found = new List<Object>();
            foreach (string guid in AssetDatabase.FindAssets("t:BaseSkill", new[] { SkillDir }))
                found.Add(AssetDatabase.LoadAssetAtPath<Object>(AssetDatabase.GUIDToAssetPath(guid)));
            return found;
        }

        public static BaseSkill Asset(string name)
        {
            var skill = AssetDatabase.LoadAssetAtPath<BaseSkill>($"{SkillDir}/{name}.asset");
            if (skill == null)
                Debug.LogError($"[{nameof(DemoSkillBuilder)}] Missing skill \"{name}\"; run Build Skills first.");
            return skill;
        }

        /// <summary>The spec table, for the animation set to hang clips off.</summary>
        public static IEnumerable<SkillSpec> All()
        {
            return Specs;
        }

        /// <summary>
        /// Writes one class's skills onto its <see cref="PlayerCharacter"/>, in the table's
        /// order, which is the order they are learned in and so the skills window's.
        ///
        /// The same list does two jobs in the kit: it is what the class may learn, and
        /// the level it starts at. The first is written at level one, so a new character
        /// has it from the create screen; the others are written at zero, which
        /// leaves them in the skill window with their requirement showing, waiting for a
        /// point.
        /// </summary>
        public static void WriteClassSkills(SerializedObject playerCharacter, string className)
        {
            SerializedProperty list = playerCharacter.FindProperty("skills");
            if (list == null)
            {
                Debug.LogError($"[{nameof(DemoSkillBuilder)}] PlayerCharacter has no \"skills\" field.");
                return;
            }
            list.ClearArray();
            // The Attack button first, at level one for every class, so it is also the first
            // entry in the skills window.
            var granted = new List<SkillSpec> { AutoAttackSpec };
            foreach (SkillSpec spec in Specs)
            {
                if (spec.Class == className)
                    granted.Add(spec);
            }
            foreach (SkillSpec spec in granted)
            {
                BaseSkill skill = Asset(spec.Name);
                if (skill == null)
                    continue;
                list.InsertArrayElementAtIndex(list.arraySize);
                SerializedProperty entry = list.GetArrayElementAtIndex(list.arraySize - 1);
                entry.FindPropertyRelative("skill").objectReferenceValue = skill;
                entry.FindPropertyRelative("skillLevel.baseAmount").intValue = spec.LearnLevel <= 1 ? 1 : 0;
                // Float, despite living on an IncrementalInt - `baseAmount` is the int and
                // the per-level step is not. Written as an int Unity refuses it and logs
                // "type is not a supported int value", which is a warning, not an error,
                // and leaves the field at whatever it already held.
                entry.FindPropertyRelative("skillLevel.amountIncreaseEachLevel").floatValue = 0f;
                // The kit migrates this deprecated field up into `skillLevel` on load, and
                // would overwrite what was just written if it were left higher.
                entry.FindPropertyRelative("level").intValue = 0;
            }
        }

        /// <summary>
        /// Points every summoning skill at the entity it calls up.
        ///
        /// Separate from the rest of the build, and last, because of an ordering knot:
        /// the skills must be built before the character models (which hang a clip off
        /// each skill) and the entities are built from the models, so by the time the
        /// skills exist the thing the Hierophant summons does not. Running this from the
        /// database wiring, which is the last step to touch game data, is what unties it.
        /// </summary>
        public static void WireSummons()
        {
            foreach (SkillSpec spec in Specs)
            {
                if (string.IsNullOrEmpty(spec.SummonEntity))
                    continue;
                BaseSkill skill = Asset(spec.Name);
                if (skill == null)
                    continue;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{EntityDir}/{spec.SummonEntity}.prefab");
                var entity = prefab != null ? prefab.GetComponent<BaseMonsterCharacterEntity>() : null;
                if (entity == null)
                {
                    Debug.LogError($"[{nameof(DemoSkillBuilder)}] {spec.Name} summons \"{spec.SummonEntity}\", " +
                                   "which is not a monster entity under " + EntityDir +
                                   " - run Build Character Entities, then this again.");
                    continue;
                }
                var serialized = new SerializedObject(skill);
                serialized.FindProperty("summon.monsterCharacterEntity").objectReferenceValue = entity;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(skill);
            }
        }

        /// <summary>
        /// Writes one monster's skills onto its <see cref="MonsterCharacter"/>.
        ///
        /// `useRate` is not the chance of using that skill, and the rates do not add up
        /// to anything. `MonsterCharacter.RandomSkill` rolls ONE number per attack
        /// decision and walks a shuffled copy of the list, taking the first skill whose
        /// `useRate` the roll falls under. So the chance of using a skill AT ALL is the
        /// largest rate in the list, and everything below that is divided between the
        /// skills that qualify, by shuffle. Two skills at 0.3 do not give 0.6.
        ///
        /// The Hierophant's are pulled apart by `useWhenHpRate` instead, which is what
        /// actually gives the fight its phases: at full health only the nova qualifies at
        /// all, whatever the others are set to.
        ///
        /// Skill level follows the monster's own level, so the crypt's boss casts at the
        /// level the crypt is pitched at rather than at one.
        /// </summary>
        public static void WriteMonsterSkills(SerializedObject monsterCharacter, string monsterName)
        {
            SerializedProperty list = monsterCharacter.FindProperty("skills");
            if (list == null)
            {
                Debug.LogError($"[{nameof(DemoSkillBuilder)}] MonsterCharacter has no \"skills\" field.");
                return;
            }
            list.ClearArray();
            foreach (SkillSpec spec in Specs)
            {
                if (spec.Monster != monsterName)
                    continue;
                BaseSkill skill = Asset(spec.Name);
                if (skill == null)
                    continue;
                list.InsertArrayElementAtIndex(list.arraySize);
                SerializedProperty entry = list.GetArrayElementAtIndex(list.arraySize - 1);
                entry.FindPropertyRelative("skill").objectReferenceValue = skill;
                entry.FindPropertyRelative("skillLevel.baseAmount").intValue = 1;
                // Float, not int - see WriteClassSkills. Written as an int this silently
                // stays at zero, and the boss casts everything at level one: a nova for
                // 14-20 instead of the 40 the crypt is pitched at.
                entry.FindPropertyRelative("skillLevel.amountIncreaseEachLevel").floatValue = 1f;
                entry.FindPropertyRelative("useRate").floatValue = spec.UseRate;
                entry.FindPropertyRelative("useWhenHpRate").floatValue = spec.UseWhenHpRate;
                // Deprecated, and migrated up over `skillLevel` on load if left higher.
                entry.FindPropertyRelative("level").intValue = 0;
            }
        }

        // ---- the ground marker ------------------------------------------------

        /// <summary>
        /// The disc a ground-targeted skill leaves behind: a trigger the size of the
        /// area, and a circle draped over the ground to show where it is.
        ///
        /// One prefab per skill rather than one shared: the trigger's radius is the
        /// area's radius and lives in the prefab, so a shared entity would give the
        /// meteor and the volley the same footprint whatever the skills said.
        /// </summary>
        private static AreaDamageEntity BuildArea(SkillSpec spec, Texture2D sprite)
        {
            var root = new GameObject($"{spec.Name}Area");
            root.layer = DamageEntityLayer;
            try
            {
                var trigger = root.AddComponent<SphereCollider>();
                trigger.isTrigger = true;
                trigger.radius = spec.Radius;
                // Lifted to the middle of a character rather than left on the floor, so
                // the sphere catches a body rather than a pair of ankles.
                trigger.center = new Vector3(0f, 1f, 0f);

                // A skill with a landing of its own draws its own ground (Frost Nova's frost), so
                // it has no disc and no spray: the effect is set off where it lands, and that is all.
                bool ownLanding = !string.IsNullOrEmpty(spec.LandEffect);
                MeshRenderer discRenderer = null;
                if (!ownLanding || spec.KeepMarker)
                {
                    // The disc, draped over the ground by GroundCircle rather than laid level:
                    // until 2026-09-24 it was a flat quad, and on a hillside the rising ground cut
                    // a straight edge across it. It floats a hand's breadth up, as the quad did,
                    // because a surface written into the terrain's own plane z-fights with it.
                    var disc = new GameObject("Marker");
                    disc.layer = DamageEntityLayer;
                    disc.transform.SetParent(root.transform, false);
                    disc.AddComponent<MeshFilter>();
                    discRenderer = disc.AddComponent<MeshRenderer>();
                    discRenderer.sharedMaterial = AreaMaterial(spec, sprite);
                    discRenderer.shadowCastingMode = ShadowCastingMode.Off;
                    discRenderer.receiveShadows = false;
                    var circle = disc.AddComponent<GroundCircle>();
                    circle.radius = spec.Radius;
                    // The sprite's bright rim, not its outer edge, falls on the radius - the rim is
                    // what reads as the boundary. Until 2026-09-24 the disc was the radius across,
                    // which drew the rim at 86% of it: a Frost Nova's ring landed at 3.9m while the
                    // nova's own particle ring, and the trigger, reached 4.5m - and it would have
                    // landed visibly inside the aiming circle, which is drawn true.
                    circle.rimAt = AreaRimAt;
                }

                // An area entity has no effect sockets - it is not a character - so its
                // particles are parented straight on and play on awake. The entity lives
                // exactly as long as the patch does, so they do too, for free.
                bool strike = spec.FallSeconds > 0f;
                // Nor motes, under a landing of its own: Volley's dwell was pale blue specks drifting
                // over the patch, which read as frost, and its arrows now say "still dangerous".
                DemoSkillEffectBuilder.AddAreaParticles(root, spec.AreaColour, spec.Radius,
                                                        lingers: !ownLanding && !spec.Burst && spec.BuffSeconds > 2f,
                                                        landing: !strike && !ownLanding);
                // The meteor, for a strike: it falls onto the disc, which is its warning, and puts
                // the disc out when it lands. It takes the skill's own sound, which the caster's
                // animation therefore does not (DemoAnimationSet).
                if (strike)
                    DemoSkillEffectBuilder.AddMeteorStrike(root, spec.FallSeconds,
                                                           DemoAudioWiring.SkillClips(spec.Name, null), discRenderer);
                // Fetched from the pool rather than parented here: a burst area is put away after
                // half a second, and everything under it goes with it (AreaLandEffect).
                if (ownLanding)
                    root.AddComponent<AreaLandEffect>().effect = DemoSkillEffectBuilder.Effect(spec.LandEffect);

                var entity = root.AddComponent<AreaDamageEntity>();
                entity.canApplyDamageToUser = false;
                entity.canApplyDamageToAllies = false;

                string path = $"{AreaDir}/{root.name}.prefab";
                PrefabUtility.SaveAsPrefabAsset(root, path);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                GameObject saved = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                WriteNetworkId(saved, path);
                return saved.GetComponent<AreaDamageEntity>();
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// Gives the saved area its network asset id, and takes away the scene id it was
        /// born with.
        ///
        /// The missiles need none of this and the areas do, for a reason worth writing
        /// down: <see cref="MissileDamageEntity"/> carries no identity component, so the
        /// kit adds one with a generated id the first time it prepares the prefab, while
        /// <see cref="AreaDamageEntity"/> requires one - and a required component added to
        /// a GameObject that is still sitting in the scene validates as a scene object and
        /// takes a scene id. Saved as a prefab it keeps that: an empty `assetId` and a
        /// `sceneObjectId` of `MeteorArea_1`. Nothing complains. The skill simply spawns
        /// nothing when it is cast, because the server has no asset to spawn it from.
        /// </summary>
        private static void WriteNetworkId(GameObject prefab, string path)
        {
            var identity = prefab.GetComponent<LiteNetLibManager.LiteNetLibIdentity>();
            if (identity == null)
                return;
            var serialized = new SerializedObject(identity);
            serialized.FindProperty("assetId").stringValue = AssetDatabase.AssetPathToGUID(path);
            serialized.FindProperty("sceneObjectId").stringValue = string.Empty;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(prefab);
            AssetDatabase.SaveAssetIfDirty(prefab);
        }

        private static Material AreaMaterial(SkillSpec spec, Texture2D sprite)
        {
            // A disc kept under a landing of its own is the edge of the danger, not the show: the
            // arrows are. At the class colour it was a neon-green plate under Volley (live,
            // 2026-09-25), so it takes the skill's own colour, dimmed.
            if (spec.KeepMarker)
                return GlowMaterial($"{MaterialDir}/MI_SkillArea_{spec.Name}.mat", sprite, spec.AreaColour * 0.7f);
            return GlowMaterial($"{MaterialDir}/MI_SkillArea_{spec.Name}.mat", sprite, ClassColour(spec.Class) * 1.6f);
        }

        /// <summary>
        /// The ground markers' material: additive, so a disc reads as light on the grass
        /// rather than as a sticker laid over it, and unlit so it does not go out with
        /// the sun. Written property by property for the same reason the flame's is -
        /// the shader reads _SrcBlend and _DstBlend, and only the material inspector ever
        /// sets those from the Blend dropdown.
        /// </summary>
        private static Material GlowMaterial(string path, Texture2D sprite, Color colour)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                Debug.LogError($"[{nameof(DemoSkillBuilder)}] No URP unlit shader.");
                return null;
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                DemoItemBuilder.EnsureFolder(MaterialDir);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            material.SetTexture("_BaseMap", sprite);
            material.SetColor("_BaseColor", colour);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 2f);
            material.SetFloat("_Cull", (float)CullMode.Off);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_AlphaClip", 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.One);
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)BlendMode.One);
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// A soft ring: bright at the rim, faintly filled inside, gone outside. Drawn
        /// rather than shipped, for the same reason the flame's sprite is - it is
        /// arithmetic against a texture whose licence would otherwise have to be checked.
        /// </summary>
        private static Texture2D BuildAreaSprite()
        {
            const int size = 256;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; ++y)
            {
                for (int x = 0; x < size; ++x)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f;
                    float dy = (y + 0.5f) / size * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    // The rim as a bell around the edge of the circle, and a wash inside it
                    // that fades towards the middle, so the marker reads as a boundary
                    // rather than as a plate laid over the ground.
                    float rim = Mathf.Exp(-Mathf.Pow((r - AreaRimAt) / 0.10f, 2f));
                    float fill = r < 0.92f ? 0.22f * (1f - r * 0.45f) : 0f;
                    float a = Mathf.Clamp01(Mathf.Max(rim, fill)) * Mathf.Clamp01((1f - r) * 8f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }
            return WritePng(AreaTexturePath, size, pixels, sprite: false);
        }

        /// <summary>Where the landing disc's rim peaks, as a share of the sprite's radius.</summary>
        private const float AreaRimAt = 0.86f;

        // ---- the aiming circle ------------------------------------------------

        /// <summary>
        /// The circle under the cursor while a skill is aimed: the skill's own radius, draped
        /// over the ground by <see cref="GroundCircle"/>, which the kit's area aim
        /// controller places. Until 2026-09-24 no demo skill had one, and pressing Volley or
        /// Meteor showed nothing at all until the click that cast it.
        ///
        /// Only for a player's skill that is aimed at all. A monster aims with its AI, not a
        /// cursor, and a skill with no reach - Frost Nova - lands on its caster and is cast
        /// the moment its key is pressed (DemoPlayerController), so there is nothing to show.
        ///
        /// In the class colour, like the disc the skill leaves when it lands, so the circle
        /// and the landing read as one thing. One prefab per skill, because the radius is baked
        /// into it, as the area entity's own trigger is.
        /// </summary>
        private static GameObject BuildAimMarker(SkillSpec spec, Texture2D sprite)
        {
            if (spec.Class == null || spec.Distance <= 0f)
                return null;
            var root = new GameObject($"{spec.Name}AimMarker");
            try
            {
                root.layer = TransparentFxLayer;
                root.AddComponent<MeshFilter>();
                var renderer = root.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = GlowMaterial($"{MaterialDir}/MI_SkillAim_{spec.Name}.mat", sprite,
                                                       ClassColour(spec.Class) * AimBrightness);
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                var circle = root.AddComponent<GroundCircle>();
                circle.radius = spec.Radius;
                circle.rimAt = AimRimAt;
                // Pulsing, so an aim in progress reads as live rather than as something
                // lying on the ground.
                circle.pulseDepth = 0.3f;

                string path = $"{AreaDir}/{root.name}.prefab";
                PrefabUtility.SaveAsPrefabAsset(root, path);
                return AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// Brighter than the landing disc's 1.6: the circle is a thin line where the disc is a
        /// wash, and it has to hold up on sunlit grass, which additive light barely changes.
        /// </summary>
        private const float AimBrightness = 2.4f;

        /// <summary>Where the aiming circle's rim peaks, as a share of the sprite's radius.</summary>
        private const float AimRimAt = 0.9f;

        /// <summary>
        /// A crisp rim with a soft glow either side of it, a faint fill, and a dot at the aim
        /// point. The rim is drawn at <see cref="AimRimAt"/>, which the circle is sized by, so it
        /// lands on the skill's radius.
        ///
        /// Drawn only into a gap, like the icons: a hand-made circle at the same path is kept.
        /// One that is should keep its ring at the same share of the way out.
        /// </summary>
        private static Texture2D BuildAimSprite()
        {
            if (System.IO.File.Exists(AimTexturePath))
                return AssetDatabase.LoadAssetAtPath<Texture2D>(AimTexturePath);

            const int size = 512;
            const float rim = AimRimAt;
            // Half the band's width, as a share of the radius: about 9cm on Meteor's circle.
            const float halfBand = 0.016f;
            float pixel = 2f / size;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; ++y)
            {
                for (int x = 0; x < size; ++x)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f;
                    float dy = (y + 0.5f) / size * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float band = Mathf.Clamp01((halfBand - Mathf.Abs(r - rim)) / (1.5f * pixel) + 0.5f);
                    float glow = r > rim
                        ? 0.45f * Mathf.Exp(-(r - rim) / 0.025f)
                        : 0.30f * Mathf.Exp(-(rim - r) / 0.07f);
                    float fill = r < rim ? 0.08f : 0f;
                    float dot = 0.7f * Mathf.Clamp01((0.022f - r) / (1.5f * pixel) + 0.5f);
                    float a = Mathf.Max(Mathf.Max(band, glow), Mathf.Max(fill, dot)) *
                              Mathf.Clamp01((1f - r) / (2f * pixel));
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }
            WritePng(AimTexturePath, size, pixels, sprite: false);
            // Twice the landing disc's resolution, because the circle is a thin line magnified
            // across half the screen; compressed, because at this size uncompressed is 1.3 MB
            // of the demo's budget for one ring. It is white, so only alpha has to survive,
            // and BC7 keeps these gradients smooth.
            var importer = (TextureImporter)AssetImporter.GetAtPath(AimTexturePath);
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(AimTexturePath);
        }

        // ---- icons ------------------------------------------------------------

        /// <summary>
        /// What is drawn on a skill's plate. Each is a handful of discs, rings and thick
        /// strokes, because that is the vocabulary a 128-pixel icon reads in.
        /// </summary>
        public enum Glyph
        {
            Slash,
            Shield,
            Chevrons,
            Banner,
            Arrow,
            SnareArrow,
            Volley,
            Mark,
            Bolt,
            Nova,
            Cross,
            Meteor,
            Sigil,
            Vessel,
            Coven,
            CrossedSwords,
            // The passives and toggles (2026-09-29).
            Bastion,
            Guard,
            Eye,
            Stride,
            Well,
            Ward,
        }

        /// <summary>
        /// A drawn icon per skill: a rounded plate in the class's colour with a white
        /// mark on it.
        ///
        /// Drawn rather than painted, because a hotbar of blank squares is what the demo
        /// looked like with the skills in and no art for them, and because twelve icons
        /// in one house style is something arithmetic is better at than a search through
        /// CC0 icon sets for twelve that happen to match.
        /// </summary>
        private static Sprite BuildIcon(SkillSpec spec)
        {
            string path = $"{IconDir}/{spec.Name}.png";
            // An icon that is already there is somebody's art, and a drawn plate is only a
            // stand-in for not having any. So the generator fills the gap and never paints
            // over what it finds - `Regenerate Skill Icons` is the way to get the drawn set
            // back. The demo's fifteen were replaced by hand-drawn ones on 2026-09-17.
            if (!redrawIcons && System.IO.File.Exists(path))
            {
                ++adoptedIcons;
                return AdoptIcon(path);
            }
            return DrawIcon(spec, path);
        }

        /// <summary>How many icons the run in progress kept rather than drew.</summary>
        private static int adoptedIcons;

        /// <summary>
        /// Set only for the length of <see cref="RegenerateSkillIcons"/>, which is the one
        /// caller allowed to draw over an icon that is already there.
        /// </summary>
        private static bool redrawIcons;

        /// <summary>
        /// Takes an icon that is already in the folder as it is. The pixels are never
        /// touched; only the import settings, and only when they are wrong.
        ///
        /// They matter more than they look: a PNG dropped into the project imports as a
        /// plain Texture2D, and `LoadAssetAtPath&lt;Sprite&gt;` on one of those returns
        /// **null** rather than failing - which arrives much later as a skill with a blank
        /// slot in the hotbar, with nothing in the console to connect it to the drop.
        /// </summary>
        private static Sprite AdoptIcon(string path)
        {
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
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null)
                Debug.LogWarning($"[{nameof(DemoSkillBuilder)}] {path} is there but did not load as a " +
                                 "Sprite, so the skill using it has no icon. Check its import settings.");
            return sprite;
        }

        /// <summary>
        /// Redraws every skill icon from the rules, over whatever is in the folder.
        ///
        /// Each is written back to the path it already had, so a skill asset pointing at
        /// one still points at it afterwards - the file keeps its GUID and only its pixels
        /// change. Nothing but the icons is touched, which is why this does not need the
        /// full skill build.
        /// </summary>
        [MenuItem("Open MMORPG/Demo/Regenerate Skill Icons (overwrites hand-drawn icons)", priority = 140)]
        public static void RegenerateSkillIcons()
        {
            DemoItemBuilder.EnsureFolder(IconDir);
            redrawIcons = true;
            try
            {
                foreach (SkillSpec spec in Specs)
                    DrawIcon(spec, $"{IconDir}/{spec.Name}.png");
                DrawIcon(AutoAttackSpec, $"{IconDir}/{AutoAttackSpec.Name}.png");
            }
            finally
            {
                redrawIcons = false;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[{nameof(DemoSkillBuilder)}] Redrew {Specs.Length} skill icon(s) in {IconDir}, " +
                      "in place, so every skill still points at its own.");
        }

        /// <summary>Draws the stand-in plate and writes it to <paramref name="path"/>.</summary>
        private static Sprite DrawIcon(SkillSpec spec, string path)
        {
            const int size = 128;

            var shapes = GlyphShapes(spec.Glyph, size);
            Color plate = ClassColour(spec.Class);
            var pixels = new Color32[size * size];

            var centre = new Vector2(size * 0.5f, size * 0.5f);
            var half = new Vector2(size * 0.5f - 5f, size * 0.5f - 5f);

            for (int y = 0; y < size; ++y)
            {
                for (int x = 0; x < size; ++x)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);

                    float plateDistance = RoundedBox(p, centre, half, 20f);
                    float plateAlpha = Coverage(plateDistance);
                    if (plateAlpha <= 0f)
                    {
                        pixels[y * size + x] = new Color32(0, 0, 0, 0);
                        continue;
                    }

                    // Lighter at the top, so the plate reads as catching the light from
                    // above the way every other button in the kit's UI does.
                    float up = (float)y / size;
                    Color colour = Color.Lerp(plate * 0.55f, plate * 1.3f, up);
                    // A darker band inside the edge, which is what stops twelve icons in
                    // three colours running together in a row.
                    float rim = plateAlpha - Coverage(plateDistance + 3.5f);
                    colour = Color.Lerp(colour, plate * 0.28f, rim);

                    // The mark is drawn twice: once two pixels low in near-black at a
                    // third strength, then over it in white. Without the shadow a white
                    // stroke has no edge against the light end of the gradient.
                    float shadow = Coverage(Distance(shapes, p + new Vector2(0f, 2f)));
                    colour = Color.Lerp(colour, new Color(0.04f, 0.03f, 0.05f), shadow * 0.35f);
                    colour = Color.Lerp(colour, new Color(0.97f, 0.96f, 0.93f), Coverage(Distance(shapes, p)));

                    pixels[y * size + x] = new Color32(
                        (byte)Mathf.RoundToInt(Mathf.Clamp01(colour.r) * 255f),
                        (byte)Mathf.RoundToInt(Mathf.Clamp01(colour.g) * 255f),
                        (byte)Mathf.RoundToInt(Mathf.Clamp01(colour.b) * 255f),
                        (byte)Mathf.RoundToInt(Mathf.Clamp01(plateAlpha) * 255f));
                }
            }

            WritePng(path, size, pixels, sprite: true);
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }


        private static Color ClassColour(string className)
        {
            switch (className)
            {
                case Warrior: return new Color(0.58f, 0.21f, 0.17f);
                case Ranger: return new Color(0.20f, 0.44f, 0.25f);
                case Mage: return new Color(0.19f, 0.32f, 0.58f);
                // Bronze for what every class has, so it is nobody's colour.
                case Everyone: return new Color(0.50f, 0.38f, 0.20f);
                default: return new Color(0.33f, 0.22f, 0.44f);
            }
        }

        /// <summary>
        /// The marks themselves, written in a unit square with y upwards and scaled to
        /// the icon. Every one is a union of discs, rings and strokes, which is what
        /// <see cref="Distance"/> takes the minimum over.
        /// </summary>
        private static List<System.Func<Vector2, float>> GlyphShapes(Glyph glyph, int size)
        {
            var shapes = new List<System.Func<Vector2, float>>();
            System.Action<float, float, float> disc = (cx, cy, r) =>
                shapes.Add(p => (p - new Vector2(cx * size, cy * size)).magnitude - r * size);
            System.Action<float, float, float, float> ring = (cx, cy, r, t) =>
                shapes.Add(p => Mathf.Abs((p - new Vector2(cx * size, cy * size)).magnitude - r * size) - t * size * 0.5f);
            System.Action<float, float, float, float, float> bar = (x0, y0, x1, y1, t) =>
                shapes.Add(p => Segment(p, new Vector2(x0 * size, y0 * size), new Vector2(x1 * size, y1 * size)) - t * size * 0.5f);

            switch (glyph)
            {
                case Glyph.Slash:
                    bar(0.20f, 0.26f, 0.76f, 0.80f, 0.14f);
                    bar(0.42f, 0.18f, 0.82f, 0.58f, 0.07f);
                    break;

                case Glyph.Shield:
                    bar(0.27f, 0.76f, 0.73f, 0.76f, 0.11f);
                    bar(0.28f, 0.76f, 0.30f, 0.44f, 0.11f);
                    bar(0.72f, 0.76f, 0.70f, 0.44f, 0.11f);
                    bar(0.30f, 0.44f, 0.50f, 0.22f, 0.11f);
                    bar(0.70f, 0.44f, 0.50f, 0.22f, 0.11f);
                    break;

                case Glyph.Chevrons:
                    for (int i = 0; i < 3; ++i)
                    {
                        float x = 0.22f + i * 0.21f;
                        bar(x, 0.74f, x + 0.16f, 0.50f, 0.09f);
                        bar(x + 0.16f, 0.50f, x, 0.26f, 0.09f);
                    }
                    break;

                // A swallow-tailed standard. Concentric rings were the first try and read
                // as a bullseye, which is Hunter's Mark's job two icons along.
                case Glyph.Banner:
                    bar(0.32f, 0.12f, 0.32f, 0.88f, 0.09f);
                    bar(0.32f, 0.84f, 0.80f, 0.84f, 0.09f);
                    bar(0.32f, 0.56f, 0.80f, 0.56f, 0.09f);
                    bar(0.80f, 0.84f, 0.66f, 0.70f, 0.09f);
                    bar(0.80f, 0.56f, 0.66f, 0.70f, 0.09f);
                    break;

                case Glyph.Arrow:
                    bar(0.24f, 0.24f, 0.74f, 0.74f, 0.08f);
                    bar(0.74f, 0.74f, 0.74f, 0.54f, 0.08f);
                    bar(0.74f, 0.74f, 0.54f, 0.74f, 0.08f);
                    ring(0.50f, 0.50f, 0.36f, 0.05f);
                    break;

                case Glyph.SnareArrow:
                    bar(0.22f, 0.22f, 0.76f, 0.76f, 0.09f);
                    bar(0.76f, 0.76f, 0.76f, 0.54f, 0.09f);
                    bar(0.76f, 0.76f, 0.54f, 0.76f, 0.09f);
                    bar(0.22f, 0.62f, 0.62f, 0.22f, 0.10f);
                    break;

                case Glyph.Volley:
                    for (int i = 0; i < 3; ++i)
                    {
                        float o = -0.20f + i * 0.20f;
                        bar(0.26f + o * 0.5f, 0.22f + o, 0.62f + o * 0.5f, 0.58f + o, 0.06f);
                        bar(0.62f + o * 0.5f, 0.58f + o, 0.62f + o * 0.5f, 0.44f + o, 0.06f);
                        bar(0.62f + o * 0.5f, 0.58f + o, 0.48f + o * 0.5f, 0.58f + o, 0.06f);
                    }
                    break;

                case Glyph.Mark:
                    ring(0.50f, 0.50f, 0.26f, 0.08f);
                    disc(0.50f, 0.50f, 0.06f);
                    bar(0.50f, 0.84f, 0.50f, 0.68f, 0.07f);
                    bar(0.50f, 0.16f, 0.50f, 0.32f, 0.07f);
                    bar(0.16f, 0.50f, 0.32f, 0.50f, 0.07f);
                    bar(0.84f, 0.50f, 0.68f, 0.50f, 0.07f);
                    break;

                case Glyph.Bolt:
                    bar(0.62f, 0.84f, 0.36f, 0.52f, 0.10f);
                    bar(0.36f, 0.52f, 0.58f, 0.47f, 0.10f);
                    bar(0.58f, 0.47f, 0.36f, 0.16f, 0.10f);
                    break;

                case Glyph.Nova:
                    disc(0.50f, 0.50f, 0.10f);
                    for (int i = 0; i < 6; ++i)
                    {
                        float a = i * Mathf.PI / 3f;
                        bar(0.50f + Mathf.Cos(a) * 0.16f, 0.50f + Mathf.Sin(a) * 0.16f,
                            0.50f + Mathf.Cos(a) * 0.36f, 0.50f + Mathf.Sin(a) * 0.36f, 0.07f);
                    }
                    break;

                // Two blades crossed, each with a guard - the Attack button.
                case Glyph.CrossedSwords:
                    bar(0.22f, 0.22f, 0.78f, 0.78f, 0.08f);
                    bar(0.78f, 0.22f, 0.22f, 0.78f, 0.08f);
                    bar(0.20f, 0.36f, 0.36f, 0.20f, 0.07f);
                    bar(0.64f, 0.20f, 0.80f, 0.36f, 0.07f);
                    break;

                case Glyph.Cross:
                    bar(0.50f, 0.24f, 0.50f, 0.76f, 0.14f);
                    bar(0.24f, 0.50f, 0.76f, 0.50f, 0.14f);
                    break;

                case Glyph.Meteor:
                    disc(0.62f, 0.38f, 0.18f);
                    bar(0.20f, 0.84f, 0.44f, 0.60f, 0.08f);
                    bar(0.34f, 0.86f, 0.52f, 0.68f, 0.06f);
                    bar(0.16f, 0.68f, 0.34f, 0.50f, 0.06f);
                    break;

                // The Hierophant's three. Deliberately not the mage's: his nova is not
                // Frost Nova and his mending is not Mend, and an icon shared between a
                // thing you cast and a thing cast at you reads as a mistake.
                case Glyph.Sigil:
                    ring(0.50f, 0.52f, 0.30f, 0.075f);
                    bar(0.31f, 0.62f, 0.69f, 0.62f, 0.075f);
                    bar(0.31f, 0.62f, 0.50f, 0.28f, 0.075f);
                    bar(0.69f, 0.62f, 0.50f, 0.28f, 0.075f);
                    break;

                case Glyph.Vessel:
                    bar(0.28f, 0.72f, 0.72f, 0.72f, 0.09f);
                    bar(0.29f, 0.72f, 0.41f, 0.44f, 0.09f);
                    bar(0.71f, 0.72f, 0.59f, 0.44f, 0.09f);
                    bar(0.41f, 0.44f, 0.59f, 0.44f, 0.09f);
                    bar(0.50f, 0.44f, 0.50f, 0.24f, 0.09f);
                    bar(0.34f, 0.22f, 0.66f, 0.22f, 0.09f);
                    break;

                case Glyph.Coven:
                    ring(0.50f, 0.50f, 0.30f, 0.055f);
                    for (int i = 0; i < 3; ++i)
                    {
                        float a = Mathf.PI / 2f + i * 2f * Mathf.PI / 3f;
                        disc(0.50f + Mathf.Cos(a) * 0.30f, 0.50f + Mathf.Sin(a) * 0.30f, 0.10f);
                    }
                    break;

                // Toughness: a crenellated tower, for what stands and takes it.
                case Glyph.Bastion:
                    bar(0.30f, 0.20f, 0.30f, 0.66f, 0.10f);
                    bar(0.70f, 0.20f, 0.70f, 0.66f, 0.10f);
                    bar(0.26f, 0.20f, 0.74f, 0.20f, 0.10f);
                    bar(0.26f, 0.66f, 0.74f, 0.66f, 0.10f);
                    bar(0.30f, 0.66f, 0.30f, 0.80f, 0.10f);
                    bar(0.50f, 0.66f, 0.50f, 0.80f, 0.10f);
                    bar(0.70f, 0.66f, 0.70f, 0.80f, 0.10f);
                    break;

                // Defensive Stance: the shield's outline with a blade laid level across it.
                case Glyph.Guard:
                    bar(0.30f, 0.74f, 0.70f, 0.74f, 0.08f);
                    bar(0.31f, 0.74f, 0.33f, 0.46f, 0.08f);
                    bar(0.69f, 0.74f, 0.67f, 0.46f, 0.08f);
                    bar(0.33f, 0.46f, 0.50f, 0.26f, 0.08f);
                    bar(0.67f, 0.46f, 0.50f, 0.26f, 0.08f);
                    bar(0.14f, 0.54f, 0.86f, 0.54f, 0.08f);
                    bar(0.22f, 0.44f, 0.22f, 0.64f, 0.07f);
                    break;

                // Keen Eye: the lids as two shallow chevrons and the pupil between them.
                case Glyph.Eye:
                    bar(0.14f, 0.50f, 0.50f, 0.72f, 0.07f);
                    bar(0.50f, 0.72f, 0.86f, 0.50f, 0.07f);
                    bar(0.14f, 0.50f, 0.50f, 0.28f, 0.07f);
                    bar(0.50f, 0.28f, 0.86f, 0.50f, 0.07f);
                    disc(0.50f, 0.50f, 0.11f);
                    break;

                // Fleet of Foot: a forward chevron trailing speed lines.
                case Glyph.Stride:
                    bar(0.56f, 0.76f, 0.80f, 0.50f, 0.10f);
                    bar(0.80f, 0.50f, 0.56f, 0.24f, 0.10f);
                    bar(0.18f, 0.66f, 0.50f, 0.66f, 0.07f);
                    bar(0.12f, 0.50f, 0.56f, 0.50f, 0.07f);
                    bar(0.18f, 0.34f, 0.50f, 0.34f, 0.07f);
                    break;

                // Deep Reserves: a drop, its point up.
                case Glyph.Well:
                    disc(0.50f, 0.40f, 0.20f);
                    bar(0.33f, 0.48f, 0.50f, 0.84f, 0.09f);
                    bar(0.67f, 0.48f, 0.50f, 0.84f, 0.09f);
                    break;

                // Arcane Ward: a hexagon of force round a spark.
                case Glyph.Ward:
                    for (int i = 0; i < 6; ++i)
                    {
                        float a0 = Mathf.PI / 6f + i * Mathf.PI / 3f;
                        float a1 = a0 + Mathf.PI / 3f;
                        bar(0.50f + Mathf.Cos(a0) * 0.34f, 0.50f + Mathf.Sin(a0) * 0.34f,
                            0.50f + Mathf.Cos(a1) * 0.34f, 0.50f + Mathf.Sin(a1) * 0.34f, 0.08f);
                    }
                    disc(0.50f, 0.50f, 0.09f);
                    break;
            }
            return shapes;
        }

        private static float Distance(List<System.Func<Vector2, float>> shapes, Vector2 p)
        {
            float best = float.MaxValue;
            for (int i = 0; i < shapes.Count; ++i)
                best = Mathf.Min(best, shapes[i](p));
            return best;
        }

        /// <summary>
        /// How much of a pixel a shape covers, from its signed distance in pixels. Half a
        /// pixel either side of the edge is all the anti-aliasing an icon at this size
        /// needs, and it costs one clamp.
        /// </summary>
        private static float Coverage(float distance)
        {
            return Mathf.Clamp01(0.5f - distance);
        }

        private static float Segment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 pa = p - a;
            Vector2 ba = b - a;
            float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Mathf.Max(Vector2.Dot(ba, ba), 0.0001f));
            return (pa - ba * h).magnitude;
        }

        private static float RoundedBox(Vector2 p, Vector2 centre, Vector2 half, float radius)
        {
            var d = new Vector2(
                Mathf.Abs(p.x - centre.x) - (half.x - radius),
                Mathf.Abs(p.y - centre.y) - (half.y - radius));
            return new Vector2(Mathf.Max(d.x, 0f), Mathf.Max(d.y, 0f)).magnitude
                   + Mathf.Min(Mathf.Max(d.x, d.y), 0f) - radius;
        }

        private static Texture2D WritePng(string path, int size, Color32[] pixels, bool sprite)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply();
            DemoItemBuilder.EnsureFolder(System.IO.Path.GetDirectoryName(path).Replace('\\', '/'));
            System.IO.File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = sprite ? TextureImporterType.Sprite : TextureImporterType.Default;
                if (sprite)
                    importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.mipmapEnabled = !sprite;
                importer.sRGBTexture = true;
                // Flat colour over a soft gradient is the one thing block compression
                // visibly bands, and these are 64 KB either way.
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
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

        /// <summary>
        /// Puts a plain skill asset on <see cref="MultiplayerARPG.SwingScaledWeaponSkill"/> when it
        /// should be a weapon skill, and back on the kit's <see cref="Skill"/> when it should not,
        /// by swapping `m_Script` in place. **Swapping a ScriptableObject's script destroys the
        /// managed object**, so the asset is reloaded by path and the caller must use what this
        /// returns - the same trap as DemoDatabaseWiring.WriteGameplayRule.
        /// </summary>
        private static BaseSkill EnsureSkillClass(string path, BaseSkill skill, bool weaponSkill)
        {
            bool isDemo = skill is MultiplayerARPG.SwingScaledWeaponSkill;
            if (isDemo == weaponSkill)
                return skill;
            MonoScript script;
            if (weaponSkill)
            {
                script = DemoScriptAssets.Of(typeof(MultiplayerARPG.SwingScaledWeaponSkill));
            }
            else
            {
                var plain = ScriptableObject.CreateInstance<Skill>();
                script = MonoScript.FromScriptableObject(plain);
                Object.DestroyImmediate(plain);
            }
            if (script == null)
            {
                Debug.LogError($"[{nameof(DemoSkillBuilder)}] {path} keeps its class: the script for " +
                               (weaponSkill ? "SwingScaledWeaponSkill" : "Skill") + " could not be found.");
                return skill;
            }
            var serialized = new SerializedObject(skill);
            serialized.FindProperty("m_Script").objectReferenceValue = script;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var reloaded = AssetDatabase.LoadAssetAtPath<BaseSkill>(path);
            if (reloaded == null || (reloaded is MultiplayerARPG.SwingScaledWeaponSkill) != weaponSkill)
            {
                Debug.LogError($"[{nameof(DemoSkillBuilder)}] Swapping {path}'s skill class did not take.");
                return reloaded != null ? reloaded : skill;
            }
            return reloaded;
        }
    }
}
