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

        public const string Warrior = "Warrior";
        public const string Ranger = "Ranger";
        public const string Mage = "Mage";

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
            public bool RequireShield;

            public int Mp;
            public int MpPerLevel;
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
            /// <summary>What the weapon's own damage is multiplied by. Zero means the skill does not use it.</summary>
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
            /// What plays on whoever it hit. Left null falls back to the game instance's
            /// own, which is the pale physical spark - see DemoSkillEffectBuilder.
            /// </summary>
            public string HitEffect;

            // ---- what it leaves behind ----------------------------------------

            public float BuffSeconds;
            /// <summary>
            /// An area skill that bites once, as a burst, whatever its debuff's length. Without
            /// it the patch stays for <see cref="BuffSeconds"/> - which is also how long the
            /// debuff lasts - and bites every 0.75s of that, which is right for Volley's rain
            /// and was wrong for Frost Nova (a 4s slow meant five bites).
            /// </summary>
            public bool Burst;
            /// <summary>Movement taken off the victim, as a share: 0.4 is a 40% slow.</summary>
            public float SlowRate;
            /// <summary>Evasion taken off the victim, as a share.</summary>
            public float EvasionRate;
            public float DamagePerSecond;
            public bool Stun;
            public float Knockback;

            public BuffTo BuffTo;
            public int HealHp;
            public int HealHpPerLevel;
            public int BuffDamage;
            public int BuffDamagePerLevel;
            public float BuffMoveRate;
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
        /// without making the ordinary attack pointless. The costs are pitched against the
        /// classes' mana - a warrior has 30 at level one, a mage 120 - so the warrior gets
        /// two or three skills out of a fight and the mage can keep casting.
        /// </summary>
        private static readonly SkillSpec[] Specs =
        {
            // ---- Warrior: short reach, and everything happens where he is standing ----

            new SkillSpec { Name = "Cleave", Title = "Cleave", Class = Warrior, LearnLevel = 1,
                Description = "A wide swing that carries through everyone in front of you.",
                Shape = Shape.Melee, Weapon = null,
                Mp = 8, MpPerLevel = 2, Cooldown = 6f, CooldownPerLevel = 0.3f,
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
                Glyph = Glyph.Slash },

            new SkillSpec { Name = "ShieldBash", Title = "Shield Bash", Class = Warrior, LearnLevel = 3,
                Description = "Drives the shield into a single enemy and puts them on the ground.",
                Shape = Shape.Melee, Weapon = null, RequireShield = true,
                Mp = 10, MpPerLevel = 2, Cooldown = 12f, CooldownPerLevel = 0.6f,
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
                Glyph = Glyph.Shield },

            new SkillSpec { Name = "Charge", Title = "Charge", Class = Warrior, LearnLevel = 5,
                Description = "Closes the ground to your target at a run, and lands on arrival.",
                Shape = Shape.Dash, Weapon = null,
                Mp = 14, MpPerLevel = 3, Cooldown = 16f, CooldownPerLevel = 1f,
                Min = 10f, Max = 15f, PerLevel = 4f,
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
                Glyph = Glyph.Chevrons },

            new SkillSpec { Name = "RallyingCry", Title = "Rallying Cry", Class = Warrior, LearnLevel = 8,
                Description = "A shout that puts weight behind every blade nearby, yours and your party's.",
                Shape = Shape.Support, Weapon = null,
                Mp = 18, MpPerLevel = 4, Cooldown = 45f, CooldownPerLevel = 1.5f,
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
                Shape = Shape.Missile, Weapon = "Bow", Missile = "ArrowAimed",
                Mp = 8, MpPerLevel = 2, Cooldown = 7f, CooldownPerLevel = 0.4f, Cast = 0.45f,
                WeaponRate = 1.6f, WeaponRatePerLevel = 0.2f,
                Distance = 22f,
                // Left on the bow's own joined draw-and-loose, so the arrow leaves the
                // string where it is seen to; the draw stands in for the cast.
                Clip = null, CastClip = "Bow_Charge", Glyph = Glyph.Arrow },

            new SkillSpec { Name = "CripplingShot", Title = "Crippling Shot", Class = Ranger, LearnLevel = 3,
                Description = "Takes the legs out of whatever is running at you.",
                Shape = Shape.Missile, Weapon = "Bow", Missile = "ArrowCrippling",
                Mp = 10, MpPerLevel = 2, Cooldown = 12f, CooldownPerLevel = 0.5f,
                WeaponRate = 0.9f, WeaponRatePerLevel = 0.1f,
                Distance = 20f,
                BuffSeconds = 5f, SlowRate = 0.4f,
                Clip = null, Glyph = Glyph.SnareArrow },

            new SkillSpec { Name = "Volley", Title = "Volley", Class = Ranger, LearnLevel = 5,
                Description = "Arrows into a patch of ground, and keeps them coming.",
                Shape = Shape.Area, Weapon = "Bow",
                Mp = 20, MpPerLevel = 4, Cooldown = 20f, CooldownPerLevel = 0.8f, Cast = 0.6f,
                Min = 6f, Max = 9f, PerLevel = 2f,
                Distance = 18f, Radius = 4f,
                BuffSeconds = 3f,
                Clip = null, CastClip = "Bow_Charge", Glyph = Glyph.Volley,
                AreaColour = DemoSkillEffectBuilder.Frost, HitEffect = "FX_HitPhysical" },

            new SkillSpec { Name = "HuntersMark", Title = "Hunter's Mark", Class = Ranger, LearnLevel = 8,
                Description = "Marks the quarry. It bleeds, and it stops being hard to hit.",
                Shape = Shape.Missile, Weapon = "Bow", Missile = "ArrowMark",
                Mp = 12, MpPerLevel = 2, Cooldown = 15f, CooldownPerLevel = 0.5f,
                Min = 2f, Max = 4f, PerLevel = 1f,
                Distance = 22f,
                BuffSeconds = 10f, DamagePerSecond = 4f, EvasionRate = 0.25f,
                Clip = null, Glyph = Glyph.Mark },

            // ---- Mage: the only class whose damage is its own rather than its weapon's ----

            // Arcane rather than fire, because the missile it throws is the demo's
            // SpellBolt: a blue emissive ball. A skill called Firebolt that loosed that
            // would be the name arguing with the screen.
            new SkillSpec { Name = "ArcaneBolt", Title = "Arcane Bolt", Class = Mage, LearnLevel = 1,
                Description = "The first thing an apprentice learns, and the last thing they stop using.",
                Shape = Shape.Missile, Weapon = "Staff", Missile = "SpellBolt",
                Mp = 10, MpPerLevel = 3, Cooldown = 3f, CooldownPerLevel = 0.15f, Cast = 0.6f,
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

            new SkillSpec { Name = "FrostNova", Title = "Frost Nova", Class = Mage, LearnLevel = 3,
                Description = "Cold off the floor in every direction, and nothing in it moves quickly again.",
                Shape = Shape.Area, Weapon = "Staff",
                Mp = 16, MpPerLevel = 4, Cooldown = 15f, CooldownPerLevel = 0.6f,
                // One burst since 2026-09-23 (`Burst`). It used to leave its patch down for
                // as long as the slow and bite every 0.75s of it: 10-14 four or five times,
                // about 60 a cast, which out-hit Meteor. Now it hits once, harder, and the
                // slow still runs its full four seconds - between Arcane Bolt (one target,
                // every 3s) and Meteor (the big one, every 25s).
                Min = 18f, Max = 24f, PerLevel = 5f,
                Burst = true,
                // Cast at the mage's own feet, which is the whole shape of the skill: it is
                // what a mage does when something has already reached them.
                Distance = 0f, Radius = 4.5f,
                BuffSeconds = 4f, SlowRate = 0.5f,
                Clip = "Spell_Simple_Shoot", Trigger = 0.4f, Audio = DemoAudioWiring.SkillImpact,
                Glyph = Glyph.Nova,
                ActivateEffect = "FX_FrostNova", AreaColour = DemoSkillEffectBuilder.Frost,
                HitEffect = "FX_HitFrost" },

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

            new SkillSpec { Name = "Meteor", Title = "Meteor", Class = Mage, LearnLevel = 8,
                Description = "Slow to call down, and worth the wait if it lands on the right patch of ground.",
                Shape = Shape.Area, Weapon = "Staff",
                Mp = 32, MpPerLevel = 6, Cooldown = 25f, CooldownPerLevel = 1f, Cast = 1.4f,
                Min = 32f, Max = 42f, PerLevel = 10f,
                Distance = 18f, Radius = 5f,
                BuffSeconds = 1.2f,
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
                Clip = "Spell_Double_Shoot_Loop", Trigger = 0.5f, CastClip = "Spell_Double_Idle_Loop",
                Audio = DemoAudioWiring.SkillImpact, Glyph = Glyph.Meteor,
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
        };

        [MenuItem("Open MMORPG/Demo/Build Skills")]
        public static void BuildAll()
        {
            DemoItemBuilder.EnsureFolder(SkillDir);
            DemoItemBuilder.EnsureFolder(IconDir);
            DemoItemBuilder.EnsureFolder(AreaDir);

            Texture2D areaSprite = BuildAreaSprite();
            foreach (SkillSpec spec in Specs)
                Build(spec, areaSprite);

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

        private static void Build(SkillSpec spec, Texture2D areaSprite)
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
                    break;
            }

            var serialized = new SerializedObject(skill);
            WriteCommon(serialized, spec);

            switch (spec.Shape)
            {
                case Shape.Area:
                    WriteAreaSkill(serialized, spec, areaSprite);
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
            // that has been renamed. Asked for by name it comes back null, and everything
            // in the demo is active anyway.
            SerializedProperty skillType = serialized.FindProperty("skillType");
            if (skillType != null)
                skillType.enumValueIndex = (int)SkillType.Active;
            serialized.FindProperty("maxLevel").intValue = spec.MaxLevel > 0 ? spec.MaxLevel : MaxSkillLevel;

            Set(serialized, "consumeMp.baseAmount", spec.Mp);
            Set(serialized, "consumeMp.amountIncreaseEachLevel", spec.MpPerLevel);
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
            if (!string.IsNullOrEmpty(spec.HitEffect))
                WriteEffect(serialized, "damageHitEffects", spec.HitEffect);

            serialized.FindProperty("requireShield").boolValue = spec.RequireShield;
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

                if (spec.Knockback > 0f)
                {
                    Set(serialized, "knockbackEffect.force", spec.Knockback);
                    Set(serialized, "knockbackEffect.deceleration", spec.Knockback * 2f);
                    Set(serialized, "knockbackEffect.duration", 0.5f);
                }
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
        private static void WriteAreaSkill(SerializedObject serialized, SkillSpec spec, Texture2D areaSprite)
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
            if (spec.Burst)
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
            WriteDebuff(serialized, spec);
        }

        /// <summary>How long a burst's patch lives, in seconds. See <see cref="SkillSpec.Burst"/>.</summary>
        private const float BurstSeconds = 0.5f;

        /// <summary>When a burst bites: shortly after it appears, as the ring goes out.</summary>
        private const float BurstBite = 0.3f;

        /// <summary>A <see cref="SimpleDashAttackSkill"/>: the warrior's charge.</summary>
        private static void WriteDashSkill(SerializedObject serialized, SkillSpec spec)
        {
            Set(serialized, "castDistance.baseAmount", spec.Distance);
            serialized.FindProperty("dashToEnemyPosition").boolValue = true;
            serialized.FindProperty("dashToEnemyStoppingDistance").floatValue = 1.6f;
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
            serialized.FindProperty("postDashEnemyLookupRadius").floatValue = 2.5f;
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
            bool lingers = spec.SlowRate > 0f || spec.EvasionRate > 0f || spec.DamagePerSecond > 0f || spec.Stun;
            serialized.FindProperty("isDebuff").boolValue = lingers;
            if (!lingers)
                return;

            Set(serialized, "debuff.duration.baseAmount", spec.BuffSeconds);
            Set(serialized, "debuff.duration.amountIncreaseEachLevel", spec.BuffSeconds * 0.1f);
            if (spec.Stun)
                serialized.FindProperty("debuff.ailment").enumValueIndex = (int)AilmentPresets.Stun;
            // Rates, not amounts: a flat number off a move speed would stop a slow bandit
            // dead and barely trouble a deer, and the demo has both.
            if (spec.SlowRate > 0f)
                Set(serialized, "debuff.increaseStatsRate.baseStats.moveSpeed", -spec.SlowRate);
            if (spec.EvasionRate > 0f)
                Set(serialized, "debuff.increaseStatsRate.baseStats.evasion", -spec.EvasionRate);
            if (spec.DamagePerSecond > 0f)
            {
                SerializedProperty overTime = serialized.FindProperty("debuff.damageOverTimes");
                overTime.arraySize = 1;
                Set(serialized, "debuff.damageOverTimes.Array.data[0].amount.baseAmount.min", spec.DamagePerSecond);
                Set(serialized, "debuff.damageOverTimes.Array.data[0].amount.baseAmount.max", spec.DamagePerSecond);
                Set(serialized, "debuff.damageOverTimes.Array.data[0].amount.amountIncreaseEachLevel.min", 1f);
                Set(serialized, "debuff.damageOverTimes.Array.data[0].amount.amountIncreaseEachLevel.max", 1f);
            }
        }

        /// <summary>What a support skill puts on its friends.</summary>
        private static void WriteBuff(SerializedObject serialized, SkillSpec spec)
        {
            SerializedProperty type = serialized.FindProperty("skillBuffType");
            switch (spec.BuffTo)
            {
                case BuffTo.None:
                    type.enumValueIndex = (int)Skill.SkillBuffType.None;
                    return;
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
            if (spec.HealHp > 0)
            {
                Set(serialized, "buff.recoveryHp.baseAmount", spec.HealHp);
                Set(serialized, "buff.recoveryHp.amountIncreaseEachLevel", spec.HealHpPerLevel);
            }
            if (spec.BuffMoveRate > 0f)
                Set(serialized, "buff.increaseStatsRate.baseStats.moveSpeed", spec.BuffMoveRate);
            if (spec.BuffDamage > 0)
            {
                SerializedProperty damages = serialized.FindProperty("buff.increaseDamages");
                damages.arraySize = 1;
                Set(serialized, "buff.increaseDamages.Array.data[0].amount.baseAmount.min", spec.BuffDamage);
                Set(serialized, "buff.increaseDamages.Array.data[0].amount.baseAmount.max", spec.BuffDamage);
                Set(serialized, "buff.increaseDamages.Array.data[0].amount.amountIncreaseEachLevel.min", spec.BuffDamagePerLevel);
                Set(serialized, "buff.increaseDamages.Array.data[0].amount.amountIncreaseEachLevel.max", spec.BuffDamagePerLevel);
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
        /// Writes one class's four skills onto its <see cref="PlayerCharacter"/>.
        ///
        /// The same list does two jobs in the kit: it is what the class may learn, and
        /// the level it starts at. The first is written at level one, so a new character
        /// has it from the create screen; the other three are written at zero, which
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
            foreach (SkillSpec spec in Specs)
            {
                if (spec.Class != className)
                    continue;
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
        /// area, and a flat quad to show where it is.
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

                GameObject disc = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Object.DestroyImmediate(disc.GetComponent<Collider>());
                disc.name = "Marker";
                disc.layer = DamageEntityLayer;
                disc.transform.SetParent(root.transform, false);
                // Laid flat, and a finger above the ground: a quad written into the
                // terrain's own plane z-fights with it from every angle.
                disc.transform.localEulerAngles = new Vector3(90f, 0f, 0f);
                disc.transform.localPosition = new Vector3(0f, 0.06f, 0f);
                disc.transform.localScale = Vector3.one * spec.Radius * 2f;
                disc.GetComponent<MeshRenderer>().sharedMaterial = AreaMaterial(spec, sprite);

                // An area entity has no effect sockets - it is not a character - so its
                // particles are parented straight on and play on awake. The entity lives
                // exactly as long as the patch does, so they do too, for free.
                DemoSkillEffectBuilder.AddAreaParticles(root, spec.AreaColour, spec.Radius,
                                                        lingers: !spec.Burst && spec.BuffSeconds > 2f);

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

        /// <summary>
        /// The marker's material: additive, so the disc reads as light on the grass
        /// rather than as a sticker laid over it, and unlit so it does not go out with
        /// the sun. Written property by property for the same reason the flame's is -
        /// the shader reads _SrcBlend and _DstBlend, and only the material inspector ever
        /// sets those from the Blend dropdown.
        /// </summary>
        private static Material AreaMaterial(SkillSpec spec, Texture2D sprite)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                Debug.LogError($"[{nameof(DemoSkillBuilder)}] No URP unlit shader.");
                return null;
            }

            string path = $"{MaterialDir}/MI_SkillArea_{spec.Name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                DemoItemBuilder.EnsureFolder(MaterialDir);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            material.SetTexture("_BaseMap", sprite);
            material.SetColor("_BaseColor", ClassColour(spec.Class) * 1.6f);
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
                    float rim = Mathf.Exp(-Mathf.Pow((r - 0.86f) / 0.10f, 2f));
                    float fill = r < 0.92f ? 0.22f * (1f - r * 0.45f) : 0f;
                    float a = Mathf.Clamp01(Mathf.Max(rim, fill)) * Mathf.Clamp01((1f - r) * 8f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }
            return WritePng(AreaTexturePath, size, pixels, sprite: false);
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
    }
}
