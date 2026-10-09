using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// The particles the spells throw: what gathers in the hand while a spell is cast,
    /// what leaves it when the spell goes off, and what sits on the ground where an area
    /// skill lands.
    ///
    /// Every skill in the demo had empty `skillCastEffects` and `skillActivateEffects`
    /// until these were written, so a mage cast with nothing to show for it but the
    /// animation. The sockets to hang them on already existed - `DemoCharacterBuilder`
    /// builds `Floor`, `Body`, `Head`, `RightHand` and `LeftHand` onto every character -
    /// and that half matters, because <see cref="GameEntityModel.InstantiateEffect"/>
    /// does `continue` on a socket it cannot find. An effect aimed at a socket that is
    /// not there is dropped in silence, however correctly authored.
    ///
    /// Drawn rather than shipped, for the same reason the torches are: it is arithmetic
    /// against a texture, and anything downloaded would have a licence to check against
    /// [[open-mmorpg-demo-cc0-only]].
    /// </summary>
    public static partial class DemoSkillEffectBuilder
    {
        private const string EffectDir = "Assets/OpenMMORPG/Demo/Prefabs/Effects/Skills";
        private const string MaterialDir = "Assets/OpenMMORPG/Demo/Materials";
        private const string TexturePath = "Assets/OpenMMORPG/Demo/Textures/SkillSoft.png";
        private const string MaterialPath = MaterialDir + "/MI_SkillEffect.mat";
        private const string FirePuffPath = "Assets/OpenMMORPG/Demo/Textures/SkillFirePuff.png";
        private const string ShockwavePath = "Assets/OpenMMORPG/Demo/Textures/SkillShockwave.png";
        private const string FireMaterialPath = MaterialDir + "/MI_SkillFire.mat";
        private const string SmokeMaterialPath = MaterialDir + "/MI_SkillSmoke.mat";
        private const string ShockwaveMaterialPath = MaterialDir + "/MI_SkillShockwave.mat";
        private const string DebrisMaterialPath = MaterialDir + "/MI_SkillDebris.mat";
        private const string DustMaterialPath = MaterialDir + "/MI_SkillDust.mat";
        private const string MeteorLightPath = EffectDir + "/FX_MeteorFlashLight.prefab";
        private const string FrostLightPath = EffectDir + "/FX_FrostFlashLight.prefab";
        private const string FrostTexturePath = "Assets/OpenMMORPG/Demo/Textures/SkillFrost.png";
        private const string FrostMaterialPath = MaterialDir + "/MI_FrostPatch.mat";
        private const string MistMaterialPath = MaterialDir + "/MI_SkillMist.mat";
        private const string IceMaterialPath = MaterialDir + "/MI_Ice.mat";
        private const string IceShardPath = EffectDir + "/IceShard.asset";

        /// <summary>The meteor's explosion, fetched by <see cref="DemoMeteorStrike"/> where the rock lands.</summary>
        public const string MeteorImpactName = "FX_MeteorImpact";

        /// <summary>Frost Nova going off, fetched by <see cref="AreaLandEffect"/> where the nova lands.</summary>
        public const string FrostNovaBurstName = "FX_FrostNovaBurst";

        /// <summary>The ice on a frozen character, played by the kit for any Freeze ailment.</summary>
        public const string FrozenName = "FX_Frozen";
        /// <summary>The dirt a charging warrior kicks up; Charge's activate effect.</summary>
        public const string ChargeDustName = "FX_ChargeDust";

        /// <summary>
        /// Where the frost sprite's ragged edge sits, on average, as a share of the way out - what
        /// the patch is sized by, so the frost reaches the nova's real radius.
        /// </summary>
        private const float FrostEdgeAt = 0.86f;
        private const string GameInstancePath = "Assets/OpenMMORPG/Demo/Prefabs/GameInstance.prefab";

        /// <summary>
        /// The sockets on a demo character, from `DemoCharacterBuilder.EffectSockets`.
        /// Named here rather than typed loose, because a misspelling is not an error -
        /// it is an effect that never appears.
        /// </summary>
        public const string SocketFloor = "Floor";
        public const string SocketBody = "Body";
        public const string SocketRightHand = "RightHand";

        /// <summary>
        /// The demo's spell colours. Arcane matches the `SpellBolt` missile material the
        /// mage already throws (0.42, 0.62, 1.0), so the gather in the hand and the thing
        /// that leaves it are the same blue.
        /// </summary>
        public static readonly Color Arcane = new Color(0.42f, 0.62f, 1.00f);
        public static readonly Color Frost = new Color(0.60f, 0.88f, 1.00f);
        public static readonly Color Mend = new Color(1.00f, 0.82f, 0.42f);
        public static readonly Color Ember = new Color(1.00f, 0.50f, 0.18f);
        public static readonly Color Unholy = new Color(0.62f, 0.35f, 0.90f);

        /// <summary>The meteor's fire, hottest to coolest, and the smoke it leaves.</summary>
        private static readonly Color FireHot = new Color(1.00f, 0.90f, 0.62f);
        private static readonly Color FireDeep = new Color(0.80f, 0.20f, 0.05f);
        private static readonly Color Smoke = new Color(0.17f, 0.15f, 0.14f);

        /// <summary>What an effect is made of. Each flag adds one emitter to the prefab.</summary>
        private struct Recipe
        {
            public string Name;
            public string Socket;
            public Color Colour;
            /// <summary>
            /// Seconds before the effect is pushed back to the pool. **Required**, and
            /// required for looping effects too - see the note on <see cref="Loop"/>.
            /// </summary>
            public float LifeTime;

            /// <summary>
            /// Whether the emitters run continuously rather than firing once.
            ///
            /// **This does not mean the effect lives forever, and it must not.** Nothing
            /// in the kit ever stops a cast effect: `DefaultCharacterUseSkillComponent`
            /// calls `InstantiateEffect(skill.SkillCastEffects)` and keeps no handle, and
            /// the one place that calls `GameEffect.DestroyEffect` is the **buff** cache,
            /// keyed by buff id. So an effect left with `GameEffect.isLoop` set gets
            /// `_destroyTime = -1`, which its update reads as "never", and it sparkles on
            /// the caster's hand for the rest of the session - one more every time the
            /// spell is cast.
            ///
            /// So `Loop` governs the *emitters* and `LifeTime` governs the *effect*. Set
            /// `LifeTime` to the skill's cast duration plus a short tail, so the gather is
            /// still there as the spell leaves the hand and the release covers its going.
            /// </summary>
            public bool Loop;

            /// <summary>A soft core sitting at the socket. The size it holds.</summary>
            public float Core;
            /// <summary>Motes falling inward onto the socket - what "charging up" looks like.</summary>
            public bool Gather;
            /// <summary>Motes drifting upward. A heal, or embers off a gathering fire.</summary>
            public bool Rise;
            /// <summary>A one-shot spray outward. The size of the sphere it sprays from.</summary>
            public float Burst;

            /// <summary>
            /// Seconds before any emitter starts. A draw's glow waits for the bow to come up: the
            /// first 0.7s of the draw is the archer taking the arrow, with the bow hand at the hip.
            /// </summary>
            public float Delay;
            /// <summary>
            /// Worn for as long as a buff lasts (<see cref="BuffGameEffect"/>): never ends on its own,
            /// and <see cref="LifeTime"/> is then its fade once the buff has gone.
            /// </summary>
            public bool Buff;
            /// <summary>Carries a <see cref="GameEffectSounds"/>, filled by DemoAudioWiring.WireRangerSounds.</summary>
            public bool Sounds;
            /// <summary>
            /// Seconds before that sound plays. The draws' creak waits 0.3s: the first half-second of the
            /// draw is the archer reaching for the arrow, and the string is only pulled after that.
            /// </summary>
            public float SoundDelay;
        }

        private static readonly Recipe[] Recipes =
        {
            // ---- the mage ----------------------------------------------------

            // Arcane Bolt: blue gathering in the fist, then thrown out of it.
            // LifeTime is Arcane Bolt's 0.6s cast plus a tail; see Recipe.Loop.
            new Recipe { Name = "FX_ArcaneCast", Socket = SocketRightHand, Colour = Arcane,
                         Loop = true, LifeTime = 0.95f, Core = 0.16f, Gather = true },
            new Recipe { Name = "FX_ArcaneRelease", Socket = SocketRightHand, Colour = Arcane,
                         LifeTime = 0.7f, Core = 0.22f, Burst = 0.10f },

            // Frost Nova: only the drawing-in of the cold, on the caster, in the moment between
            // the key and the nova. The nova itself is FX_FrostNovaBurst, set off by the area
            // where it lands (BuildFrostNovaBurst). Until 2026-09-25 this was the whole spell - a
            // flash, a white disc and a ring of motes at the caster's feet - and it played a
            // third of a second before the damage did.
            new Recipe { Name = "FX_FrostNova", Socket = SocketBody, Colour = Frost,
                         LifeTime = 0.45f, Core = 0.22f, Gather = true },

            // Mend: warm, and on the body rather than the hand, because it lands on
            // whoever was healed rather than leaving the caster.
            // Mend casts for 1s.
            new Recipe { Name = "FX_MendCast", Socket = SocketBody, Colour = Mend,
                         Loop = true, LifeTime = 1.35f, Core = 0.20f, Rise = true },
            new Recipe { Name = "FX_MendBloom", Socket = SocketBody, Colour = Mend,
                         // Burst radius doubles as its speed (see Burst), so 0.25 threw
                         // gold four metres and read as an explosion rather than a mending.
                         LifeTime = 1.2f, Core = 0.35f, Rise = true, Burst = 0.10f },

            // Meteor: a long two-handed call-down, so the gather is on the body and big
            // enough to read across a room. The landing belongs to the area entity.
            // Meteor casts for 1.4s.
            new Recipe { Name = "FX_MeteorCast", Socket = SocketBody, Colour = Ember,
                         Loop = true, LifeTime = 1.75f, Core = 0.45f, Gather = true, Rise = true },
            new Recipe { Name = "FX_MeteorLaunch", Socket = SocketBody, Colour = Ember,
                         LifeTime = 0.9f, Core = 0.4f, Burst = 0.16f },

            // ---- the ranger ----------------------------------------------------
            //
            // The draws light the arrowhead - the bow hand, where the head sits at full draw - once
            // the bow is up. Each lives for its skill's cast plus a tail: Aimed Shot 1.42s, Volley
            // 1.17s. The releases are built in DemoSkillEffectBuilder.Ranger, bar these two small ones.
            // Sized up after a live look (2026-09-25): at the mage's hand-glow size a daylight sky
            // behind the bow swallowed it whole. Gold rather than the pale shot colour for the same
            // reason - near-white additive on a bright sky is no change at all.
            new Recipe { Name = "FX_AimedDraw", Socket = SocketLeftHand, Colour = new Color(1f, 0.74f, 0.28f),
                         Loop = true, LifeTime = 1.75f, Delay = 0.7f, Core = 0.17f, Gather = true, Sounds = true,
                         SoundDelay = 0.3f },
            new Recipe { Name = "FX_VolleyDraw", Socket = SocketLeftHand, Colour = Timber,
                         Loop = true, LifeTime = 1.45f, Delay = 0.7f, Core = 0.13f, Gather = true, Sounds = true,
                         SoundDelay = 0.3f },
            // Crippling Shot has no cast - it is the bow's own shot - so this is its skill's
            // *activate* effect, which the kit plays as the attack animation starts: the start of
            // the draw. That shot looses at 1.34s, the same moment as Volley's, hence Volley's
            // timings. A deeper green than the arrow's own, which is pale enough to vanish
            // against a daylight sky (see the gold above). Silent until 2026-10-02.
            new Recipe { Name = "FX_CripplingDraw", Socket = SocketLeftHand, Colour = new Color(0.35f, 0.85f, 0.18f),
                         Loop = true, LifeTime = 1.45f, Delay = 0.7f, Core = 0.13f, Gather = true, Sounds = true,
                         SoundDelay = 0.3f },
            new Recipe { Name = "FX_CripplingRelease", Socket = SocketLeftHand, Colour = ArrowCrippling,
                         LifeTime = 0.45f, Core = 0.12f, Burst = 0.05f },
            new Recipe { Name = "FX_MarkRelease", Socket = SocketLeftHand, Colour = ArrowMark,
                         LifeTime = 0.45f, Core = 0.12f, Burst = 0.05f },

            // ---- what the debuffs look like on whoever has them -----------------
            //
            // Burning and Chilled are the mage's (Meteor, Frost Nova), done here because they had
            // the same gap as the ranger's: nothing on the victim said anything was wrong.
            new Recipe { Name = "FX_Burning", Socket = SocketBody, Colour = Ember,
                         Loop = true, Buff = true, LifeTime = 0.9f, Rise = true },
            new Recipe { Name = "FX_Chilled", Socket = SocketBody, Colour = Frost,
                         Loop = true, Buff = true, LifeTime = 0.9f, Rise = true },

            // ---- the Hierophant ----------------------------------------------

            // Shared by Call the Faithful (1.2s) and Rite of Mending (1.6s), so it is cut
            // to the longer of the two; the shorter cast simply wears it a moment longer.
            new Recipe { Name = "FX_UnholyCast", Socket = SocketBody, Colour = Unholy,
                         Loop = true, LifeTime = 1.95f, Core = 0.35f, Gather = true },
            new Recipe { Name = "FX_UnholyRelease", Socket = SocketBody, Colour = Unholy,
                         LifeTime = 0.9f, Core = 0.35f, Burst = 0.15f },
            new Recipe { Name = "FX_UnholyMend", Socket = SocketBody, Colour = Unholy,
                         LifeTime = 1.6f, Core = 0.35f, Rise = true },

            // ---- the cultists ------------------------------------------------
            //
            // Withering Hex casts with the Hierophant's FX_UnholyCast (its 1.5s is inside that
            // effect's 1.95) and leaves this on whoever it hit, for as long as the curse eats at
            // them: violet motes rising, the same shape as Burning's embers.
            new Recipe { Name = "FX_Withering", Socket = SocketBody, Colour = Unholy,
                         Loop = true, Buff = true, LifeTime = 0.9f, Rise = true },

            // ---- what plays on whoever was hit --------------------------------
            //
            // These spawn on the VICTIM's model, at the victim's own `Body` socket, from
            // `DamageableEntity.PlayHitEffects`. So they have to be small and brief:
            // unlike a cast, one of these fires on every single blow that lands, from
            // every character in the fight. Half a second and a handful of sparks.
            //
            // `FX_HitPhysical` is the fallback for everything that does not name its own,
            // set on GameInstance - see WriteDefaultHitEffect. It is pale rather than
            // coloured, because it stands in for a sword, an axe, an arrow, a bandit's
            // fist and a deer running into a rock.
            new Recipe { Name = "FX_HitPhysical", Socket = SocketBody, Colour = new Color(1f, 0.93f, 0.78f),
                         LifeTime = 0.45f, Core = 0.16f, Burst = 0.06f },
            new Recipe { Name = "FX_HitArcane", Socket = SocketBody, Colour = Arcane,
                         LifeTime = 0.5f, Core = 0.2f, Burst = 0.07f },
            new Recipe { Name = "FX_HitFrost", Socket = SocketBody, Colour = Frost,
                         LifeTime = 0.55f, Core = 0.22f, Burst = 0.08f },
            new Recipe { Name = "FX_HitEmber", Socket = SocketBody, Colour = Ember,
                         LifeTime = 0.6f, Core = 0.26f, Burst = 0.09f },
            new Recipe { Name = "FX_HitUnholy", Socket = SocketBody, Colour = Unholy,
                         LifeTime = 0.55f, Core = 0.22f, Burst = 0.08f },
            // The ranger's. Each carries the arrow's thunk (ArrowImpact, falling back to the
            // weapon hit), played so it carries to the archer twenty metres off - see GameEffectSounds.
            new Recipe { Name = "FX_HitAimed", Socket = SocketBody, Colour = ArrowAimed,
                         LifeTime = 0.55f, Core = 0.24f, Burst = 0.1f, Sounds = true },
            new Recipe { Name = "FX_HitCrippling", Socket = SocketBody, Colour = ArrowCrippling,
                         LifeTime = 0.5f, Core = 0.18f, Burst = 0.07f, Sounds = true },
            new Recipe { Name = "FX_HitMark", Socket = SocketBody, Colour = ArrowMark,
                         LifeTime = 0.55f, Core = 0.2f, Burst = 0.08f, Sounds = true },
            new Recipe { Name = "FX_HitArrow", Socket = SocketBody, Colour = Timber,
                         LifeTime = 0.4f, Core = 0.14f, Burst = 0.06f, Sounds = true },
        };

        [MenuItem("Open MMORPG/Demo/Build Skill Effects")]
        public static void BuildAll()
        {
            DemoItemBuilder.EnsureFolder(EffectDir);
            Texture2D sprite = BuildSprite();
            Material material = EffectMaterial(sprite);
            if (material == null)
                return;

            foreach (Recipe recipe in Recipes)
                Build(recipe, material);
            BuildMeteorImpact();
            BuildFrostNovaBurst();
            BuildFrozen();
            BuildRanger();
            BuildChargeDust();
            BuildWeaponTrails();
            WriteDefaultHitEffect();
            WriteFreezeEffect();
            // The ice's sounds live on the crystals, and the ranger's on its effects, all of which
            // were just rebuilt from nothing.
            DemoAudioWiring.WireIceSounds();
            DemoAudioWiring.WireRangerSounds();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[{nameof(DemoSkillEffectBuilder)}] Built {Recipes.Length} skill effects, the meteor's " +
                      $"explosion, Frost Nova's burst and the frozen character's ice in {EffectDir}.");
        }

        // ---- the warrior's weapon trails ------------------------------------

        public const string CleaveTrailName = "FX_CleaveTrail";
        public const string ShieldBashTrailName = "FX_ShieldBashTrail";
        private const string TrailMaterialPath = MaterialDir + "/MI_WeaponTrail.mat";

        /// <summary>Steel in daylight: pale, a touch blue, alpha-blended so it shows over sunlit grass where additive white would vanish.</summary>
        private static readonly Color SteelTrail = new Color(0.85f, 0.92f, 1.00f, 0.50f);
        /// <summary>The shield's smear: warmer and shorter-lived than the blade's.</summary>
        private static readonly Color ShieldTrail = new Color(1.00f, 0.94f, 0.82f, 0.50f);

        /// <summary>
        /// Ribbons behind the warrior's swings (2026-10-02, user's request). Each is a
        /// <see cref="GameEffect"/> carrying a <see cref="MultiplayerARPG.WeaponTrail"/>,
        /// fired as the skill's activate effect - the kit plays those as the animation starts,
        /// which is when a swing begins - and the script reads the equipped weapon's own mesh for
        /// the edge, so nothing is added to the weapon prefabs and a looted longsword trails as
        /// the starter shortsword does.
        ///
        /// Timing is the clip's. Cleave's `Sword_Heavy_C` is 0.70s and the blade sweeps across
        /// the front at the end of it, so the ribbon samples the whole clip and fades a fifth of
        /// a second behind. Shield Bash's `Shield_OneShot` drives the shield out in its first
        /// fifth and then holds (see DemoSkillBuilder), so its trail is short: the thrust, not
        /// the hold. Charge gets none - its clip is a run behind a shield and its dust already
        /// says charge; a blade ribbon on a sprint read as a mistake.
        ///
        /// The effect socket only places the pooled object; the ribbon is built in world space
        /// off the weapon, so `Floor` (the model root, proven by the charge dust) serves both.
        /// </summary>
        private static void BuildWeaponTrails()
        {
            Material material = ParticleMaterial(TrailMaterialPath, null, ParticleBlend.Alpha, Color.white);
            if (material == null)
                return;
            // Measured on the beach (2026-10-02): a band from 0.3 of the blade with a 0.22s fade
            // left a milky sheet the size of the swing round the whole character; from 0.55 with
            // 0.16s it is an arc behind the blade, which is what a trail is.
            BuildWeaponTrail(CleaveTrailName, material, SocketRightHandEquip, SteelTrail, emit: 0.70f, fade: 0.16f, startFraction: 0.55f);
            BuildWeaponTrail(ShieldBashTrailName, material, SocketLeftHandEquip, ShieldTrail, emit: 0.30f, fade: 0.18f, startFraction: 0f);
        }

        /// <summary>Equipment container names, as DemoItemBuilder writes them on the models.</summary>
        private const string SocketRightHandEquip = "RightHand";
        private const string SocketLeftHandEquip = "LeftHand";

        private static void BuildWeaponTrail(string name, Material material, string equipSocket, Color colour, float emit, float fade, float startFraction)
        {
            string path = $"{EffectDir}/{name}.prefab";
            var root = new GameObject(name);
            try
            {
                var effect = root.AddComponent<GameEffect>();
                effect.effectSocket = SocketFloor;
                effect.isLoop = false;
                effect.lifeTime = emit + fade + 0.1f;
                effect.PoolSize = 4;

                var trail = root.AddComponent<MultiplayerARPG.WeaponTrail>();
                trail.equipSocket = equipSocket;
                trail.emitSeconds = emit;
                trail.fadeSeconds = fade;
                trail.colour = colour;
                trail.startFraction = startFraction;
                trail.material = material;

                PrefabUtility.SaveAsPrefabAsset(root, path);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // ---- the warrior's charge -------------------------------------------

        private static readonly Color Dust = new Color(0.66f, 0.56f, 0.42f);

        /// <summary>
        /// Dirt kicked up at the feet of a charging warrior: a clod of it thrown back as the run
        /// starts, then a trail of puffs for as long as the run can last, left standing on the
        /// ground behind him. It is the one thing that says "this is a charge, not a jog":
        /// the sprint clip alone, seen from the demo's camera, is a man running slightly fast.
        ///
        /// Hung on the `Floor` socket, which is the model root at the feet. The puffs simulate
        /// in world space so they stay where they were kicked up while the socket runs on;
        /// every other recipe is local, because a glow in a fist has to travel with the fist.
        ///
        /// Timing is the dash's, not the clip's. The kit plays an activate effect as the clip
        /// starts, the dash begins at the 0.15s trigger, and `CalculateDuration` solves the run
        /// from the distance - up to about 0.9s for the full twelve metres. So the kick waits
        /// 0.15s, the trail runs for a second after it, and the effect lives long enough for
        /// the last puff to fade. A short charge simply stops inside its own dust, which reads
        /// as stopping hard, which is right.
        ///
        /// Lit and alpha-blended like the mist, not additive like the spells: dust is not light,
        /// and additive tan over daylight sand is invisible. At night it goes dark, as dust does.
        /// </summary>
        private static void BuildChargeDust()
        {
            Material dust = ParticleMaterial(DustMaterialPath, FirePuffSprite(), ParticleBlend.Alpha, Color.white, lit: true);
            if (dust == null)
                return;

            string path = $"{EffectDir}/{ChargeDustName}.prefab";
            AudioClip[] sounds = ExistingSounds(path);
            var root = new GameObject(ChargeDustName);
            try
            {
                ChargeKick(root.transform, dust);
                ChargeTrail(root.transform, dust);

                var effect = root.AddComponent<GameEffect>();
                effect.effectSocket = "Floor";
                effect.isLoop = false;
                // 0.15s to the trigger, a second of trail, and the longest puff's fade.
                effect.lifeTime = 2.2f;
                effect.PoolSize = 4;
                effect.randomSoundEffects = sounds;

                PrefabUtility.SaveAsPrefabAsset(root, path);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// The clod thrown back as the feet dig in: a dozen puffs out of a cone pointing behind
        /// the character, low and fast, that settle in half a second.
        /// </summary>
        private static void ChargeKick(Transform parent, Material material)
        {
            ParticleSystem kick = Emitter(parent, "Kick", material, 24);
            ParticleSystem.MainModule main = kick.main;
            main.loop = false;
            main.duration = 0.2f;
            main.startDelay = 0.15f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.6f, 3.2f);
            // Big and dense, or it is not there: the first pass at 0.35-0.6 m and half alpha
            // emitted its twelve puffs (counted live) and drew nothing the demo camera could
            // see from its eight metres - the billow sprite is mostly gap, and tan on sand
            // needs the rest. Measured visible at 0.9 m and 0.95 alpha; this is a little under.
            main.startSize = new ParticleSystem.MinMaxCurve(0.7f, 1.1f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = 0.12f;
            ParticleSystem.EmissionModule emission = kick.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 14) });
            ParticleSystem.ShapeModule shape = kick.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 30f;
            shape.radius = 0.15f;
            // Straight back and a little up: the cone's own axis is +Z, the character's forward.
            shape.rotation = new Vector3(-20f, 180f, 0f);
            shape.position = new Vector3(0f, 0.1f, 0f);
            Drag(kick, 1f, 0.12f);
            Spin(kick, 0.6f);
            Tint(kick, Ramp((0f, Dust, 0f), (0.1f, Dust, 0.8f), (0.6f, Dust, 0.45f), (1f, Dust, 0f)));
            Shrink(kick, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 1.5f)));
            SettleDust(kick);
        }

        /// <summary>
        /// Puffs rising off the heels for the length of the run, each left where it was made.
        /// Thirty a second is a steady haze at charge speed, with the ground still visible through it.
        /// </summary>
        private static void ChargeTrail(Transform parent, Material material)
        {
            ParticleSystem trail = Emitter(parent, "Trail", material, 60);
            ParticleSystem.MainModule main = trail.main;
            main.loop = false;
            main.duration = 1f;
            main.startDelay = 0.15f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 0.9f);
            // Sized with the kick above, for the same reason.
            main.startSize = new ParticleSystem.MinMaxCurve(0.8f, 1.2f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = -0.03f;
            ParticleSystem.EmissionModule emission = trail.emission;
            emission.rateOverTime = 45f;
            ParticleSystem.ShapeModule shape = trail.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = 0.35f;
            shape.position = new Vector3(0f, 0.05f, 0f);
            Drag(trail, 1f, 0.1f);
            Spin(trail, 0.4f);
            Tint(trail, Ramp((0f, Dust, 0f), (0.12f, Dust, 0.7f), (0.6f, Dust, 0.4f), (1f, Dust, 0f)));
            Shrink(trail, new AnimationCurve(new Keyframe(0f, 0.7f), new Keyframe(1f, 1.4f)));
            SettleDust(trail);
        }

        /// <summary>Dust draws behind the character that kicked it up, not through him.</summary>
        private static void SettleDust(ParticleSystem system)
        {
            var renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.sortMode = ParticleSystemSortMode.Distance;
            renderer.sortingFudge = 10f;
        }

        /// <summary>
        /// Points the game instance's fallback hit effect at ours.
        ///
        /// This is the one that matters most, and it is not a skill setting:
        /// `DamageableEntity.PlayHitEffects` starts from
        /// `GameInstance.defaultDamageHitEffects` and only replaces it if the damage names
        /// a source that has its own. So this single reference is what every ordinary
        /// sword swing, arrow and monster blow in the demo plays - a skill's own
        /// `damageHitEffects` is the exception, not the rule.
        ///
        /// What was there was the kit template's placeholder: `Sprites/Default` with **no
        /// texture**, which draws untextured quads for a full two seconds.
        /// </summary>
        private static void WriteDefaultHitEffect()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GameInstancePath);
            if (prefab == null)
            {
                Debug.LogWarning($"[{nameof(DemoSkillEffectBuilder)}] No {GameInstancePath}; " +
                                 "the fallback hit effect is left as it was.");
                return;
            }
            var instance = prefab.GetComponent<GameInstance>();
            GameEffect hit = Effect("FX_HitPhysical");
            if (instance == null || hit == null)
                return;
            var serialized = new SerializedObject(instance);
            SerializedProperty list = serialized.FindProperty("defaultDamageHitEffects");
            if (list == null)
            {
                Debug.LogWarning($"[{nameof(DemoSkillEffectBuilder)}] GameInstance has no " +
                                 "\"defaultDamageHitEffects\"; has the kit renamed it?");
                return;
            }
            list.ClearArray();
            list.InsertArrayElementAtIndex(0);
            list.GetArrayElementAtIndex(0).objectReferenceValue = hit;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(prefab);
            AssetDatabase.SaveAssetIfDirty(prefab);
        }

        /// <summary>Where an effect prefab lives, built or not.</summary>
        public static string EffectPath(string name)
        {
            return $"{EffectDir}/{name}.prefab";
        }

        /// <summary>Looks one up for the skill builder to hang on a skill.</summary>
        public static GameEffect Effect(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{EffectDir}/{name}.prefab");
            if (prefab == null)
            {
                Debug.LogError($"[{nameof(DemoSkillEffectBuilder)}] Missing effect \"{name}\"; " +
                               "run Build Skill Effects first.");
                return null;
            }
            return prefab.GetComponent<GameEffect>();
        }

        /// <summary>
        /// The particles an area skill leaves on the ground, added straight to the area
        /// entity rather than made into a <see cref="GameEffect"/>.
        ///
        /// An area entity is not a character and has no effect sockets, so the kit's
        /// socket lookup has nothing to find. Parenting the emitters to the entity and
        /// letting them play on awake is the whole mechanism: the entity is spawned when
        /// the skill lands and despawned when the area expires, and the particles live
        /// exactly that long by construction.
        /// </summary>
        public static void AddAreaParticles(GameObject areaRoot, Color colour, float radius, bool lingers, bool landing = true)
        {
            if (!landing && !lingers)
                return;
            Material material = EffectMaterial(BuildSprite());
            if (material == null)
                return;

            var holder = new GameObject("FX");
            holder.transform.SetParent(areaRoot.transform, false);

            // The landing: a spray up and out from the middle of the patch, as it appears. Not
            // for a strike (the meteor), which appears as the warning and lands a fall later -
            // its landing is the explosion AddMeteorStrike sets up.
            if (landing)
                AddLandingSpray(holder.transform, material, colour, radius);

            if (!lingers)
                return;

            // A patch that keeps biting gets something that keeps moving, so a player can
            // tell "still dangerous" from "already gone" without counting seconds.
            ParticleSystem dwell = Emitter(holder.transform, "Dwell", material, 80);
            ParticleSystem.MainModule dwellMain = dwell.main;
            dwellMain.loop = true;
            dwellMain.playOnAwake = true;
            dwellMain.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
            dwellMain.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.1f);
            dwellMain.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.3f);
            ParticleSystem.EmissionModule dwellEmission = dwell.emission;
            dwellEmission.rateOverTime = 28f;
            ParticleSystem.ShapeModule dwellShape = dwell.shape;
            dwellShape.enabled = true;
            dwellShape.shapeType = ParticleSystemShapeType.Circle;
            dwellShape.radius = radius * 0.92f;
            dwellShape.rotation = new Vector3(90f, 0f, 0f);   // lay the disc flat
            Tint(dwell, Ramp((0f, colour, 0f), (0.35f, colour, 0.7f), (1f, colour, 0f)));
        }

        private static void AddLandingSpray(Transform holder, Material material, Color colour, float radius)
        {
            ParticleSystem burst = Emitter(holder, "Impact", material, 60);
            ParticleSystem.MainModule burstMain = burst.main;
            burstMain.loop = false;
            burstMain.playOnAwake = true;
            burstMain.duration = 0.5f;
            burstMain.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
            burstMain.startSpeed = new ParticleSystem.MinMaxCurve(radius * 0.8f, radius * 1.6f);
            burstMain.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.42f);
            burstMain.gravityModifier = 0.5f;
            ParticleSystem.EmissionModule burstEmission = burst.emission;
            burstEmission.rateOverTime = 0f;
            burstEmission.SetBursts(new[] { new ParticleSystem.Burst(0f, 40) });
            ParticleSystem.ShapeModule burstShape = burst.shape;
            burstShape.enabled = true;
            burstShape.shapeType = ParticleSystemShapeType.Hemisphere;
            burstShape.radius = radius * 0.3f;
            Tint(burst, Ramp((0f, colour, 0.9f), (0.7f, colour, 0.5f), (1f, colour, 0f)));
        }

        // ---- the meteor --------------------------------------------------------

        /// <summary>
        /// Puts a meteor on a strike skill's area: a fireball that falls out of the sky onto the
        /// patch over <paramref name="fallSeconds"/>, and a <see cref="DemoMeteorStrike"/> to fly it
        /// and set off <see cref="MeteorImpactName"/> where it lands.
        ///
        /// Until 2026-09-24 a Meteor was a glowing disc and a spray of sparks, with no meteor in it.
        ///
        /// The ball is two layers of light riding with the rock (a white-hot heart in a wide orange
        /// glow - the same falloff that makes the bolt read as light), and three things it sheds
        /// into the world as it goes: licks of flame, sparks and a smoke trail, emitted per metre
        /// so the tail is as dense wherever the fall is fast, plus a ribbon, which interpolates
        /// along the path where particles clump. Everything shed must burn out within the area's
        /// linger after the strike, or it vanishes with the area; see DemoSkillBuilder.
        /// </summary>
        public static void AddMeteorStrike(GameObject areaRoot, float fallSeconds, AudioClip[] sounds, Renderer telegraph)
        {
            Material glow = EffectMaterial(BuildSprite());
            Material fire = ParticleMaterial(FireMaterialPath, FirePuffSprite(), ParticleBlend.Additive, Color.white);
            Material smoke = ParticleMaterial(SmokeMaterialPath, FirePuffSprite(), ParticleBlend.Alpha, Color.white);
            if (glow == null || fire == null || smoke == null)
                return;

            var body = new GameObject("Meteor");
            body.transform.SetParent(areaRoot.transform, false);
            AddMissileCore(body, glow, Ember, 5f, 0.32f, "Glow");
            AddMissileCore(body, glow, FireHot, 2f, 1f, "Core");

            // Licks of flame torn off the ball and left behind it. Short-lived on purpose: at the
            // speed it lands at, half a second of flame was a fifteen-metre tail, and with the
            // ribbon inside it the whole fall read as a beam from the sky rather than as a ball.
            ParticleSystem flames = Emitter(body.transform, "Flames", fire, 300);
            ParticleSystem.MainModule flamesMain = flames.main;
            flamesMain.simulationSpace = ParticleSystemSimulationSpace.World;
            flamesMain.loop = true;
            flamesMain.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.32f);
            flamesMain.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 2f);
            flamesMain.startSize = new ParticleSystem.MinMaxCurve(1f, 1.8f);
            flamesMain.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            ParticleSystem.EmissionModule flamesEmission = flames.emission;
            flamesEmission.rateOverTime = 30f;
            flamesEmission.rateOverDistance = 3.5f;
            ParticleSystem.ShapeModule flamesShape = flames.shape;
            flamesShape.enabled = true;
            flamesShape.shapeType = ParticleSystemShapeType.Sphere;
            flamesShape.radius = 0.4f;
            Spin(flames, 1f);
            Tint(flames, Ramp((0f, FireHot, 0.85f), (0.35f, Ember, 0.75f), (0.75f, FireDeep, 0.35f), (1f, FireDeep, 0f)));
            Shrink(flames, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.35f)));
            flames.GetComponent<ParticleSystemRenderer>().sortingFudge = -8f;

            // Sparks thrown off it, falling away under their own weight.
            ParticleSystem sparks = Emitter(body.transform, "Sparks", glow, 300, stretch: true);
            ParticleSystem.MainModule sparksMain = sparks.main;
            sparksMain.simulationSpace = ParticleSystemSimulationSpace.World;
            sparksMain.loop = true;
            sparksMain.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
            sparksMain.startSpeed = new ParticleSystem.MinMaxCurve(1f, 3.5f);
            sparksMain.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.09f);
            sparksMain.gravityModifier = 0.5f;
            ParticleSystem.EmissionModule sparksEmission = sparks.emission;
            sparksEmission.rateOverTime = 0f;
            sparksEmission.rateOverDistance = 6f;
            ParticleSystem.ShapeModule sparksShape = sparks.shape;
            sparksShape.enabled = true;
            sparksShape.shapeType = ParticleSystemShapeType.Sphere;
            sparksShape.radius = 0.4f;
            Tint(sparks, Ramp((0f, FireHot, 1f), (0.5f, Ember, 0.8f), (1f, Ember, 0f)));

            // Smoke, drawn behind the fire: the one dark thing in it, which is what makes the
            // rest read as burning rather than as a lamp.
            ParticleSystem trail = Emitter(body.transform, "Smoke", smoke, 120);
            ParticleSystem.MainModule trailMain = trail.main;
            trailMain.simulationSpace = ParticleSystemSimulationSpace.World;
            trailMain.loop = true;
            trailMain.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.1f);
            trailMain.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.8f);
            trailMain.startSize = new ParticleSystem.MinMaxCurve(0.9f, 1.5f);
            trailMain.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            ParticleSystem.EmissionModule trailEmission = trail.emission;
            trailEmission.rateOverTime = 0f;
            trailEmission.rateOverDistance = 2.2f;
            ParticleSystem.ShapeModule trailShape = trail.shape;
            trailShape.enabled = true;
            trailShape.shapeType = ParticleSystemShapeType.Sphere;
            trailShape.radius = 0.3f;
            Spin(trail, 0.4f);
            Tint(trail, Ramp((0f, Smoke, 0f), (0.15f, Smoke, 0.42f), (1f, Smoke, 0f)));
            Shrink(trail, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 2f)));
            var trailRenderer = trail.GetComponent<ParticleSystemRenderer>();
            trailRenderer.sortMode = ParticleSystemSortMode.Distance;
            trailRenderer.sortingFudge = 10f;

            var ribbonObject = new GameObject("Ribbon");
            ribbonObject.transform.SetParent(body.transform, false);
            var ribbon = ribbonObject.AddComponent<TrailRenderer>();
            // Short and orange: a long white one was most of the beam described above.
            ribbon.time = 0.16f;
            ribbon.startWidth = 0.9f;
            ribbon.endWidth = 0f;
            ribbon.minVertexDistance = 0.2f;
            ribbon.autodestruct = false;
            ribbon.emitting = true;
            ribbon.alignment = LineAlignment.View;
            ribbon.textureMode = LineTextureMode.Stretch;
            ribbon.numCapVertices = 2;
            ribbon.shadowCastingMode = ShadowCastingMode.Off;
            ribbon.receiveShadows = false;
            ribbon.sharedMaterial = RibbonMaterial();
            var ribbonColours = new Gradient();
            ribbonColours.SetKeys(
                new[] { new GradientColorKey(Ember, 0f), new GradientColorKey(FireDeep, 1f) },
                new[] { new GradientAlphaKey(0.75f, 0f), new GradientAlphaKey(0f, 1f) });
            ribbon.colorGradient = ribbonColours;

            // The light it carries: the ground under it, and anyone standing there, reddening as it
            // comes down. At the demo's default camera pitch the rock itself is only on screen for
            // its last few metres; this is how the fall shows before then.
            var lightObject = new GameObject("Light");
            lightObject.transform.SetParent(body.transform, false);
            var carried = lightObject.AddComponent<Light>();
            carried.type = LightType.Point;
            carried.color = new Color(1f, 0.58f, 0.26f);
            // Past the height the fall starts at, so the ground under it is lit from the top.
            carried.range = 22f;
            carried.intensity = 0f;
            carried.shadows = LightShadows.None;

            var strike = areaRoot.AddComponent<DemoMeteorStrike>();
            strike.fallSeconds = fallSeconds;
            strike.body = body.transform;
            strike.glow = carried;
            strike.telegraph = telegraph;
            strike.impactEffect = Effect(MeteorImpactName);
            strike.sounds = sounds ?? new AudioClip[0];
        }

        /// <summary>
        /// Where the meteor lands: `FX_MeteorImpact`, a pooled GameEffect that <see cref="DemoMeteorStrike"/>
        /// fetches at the point of impact, so it plays out on its own three seconds instead of the
        /// area's.
        ///
        /// Built in the order it reads: a flash that lights the ground around it, a ring running
        /// out along the ground to the area's real edge, a ball of fire rolling up out of the
        /// middle, sparks and clods of earth thrown out of it, and the smoke and embers that are
        /// left once the fire has gone.
        /// </summary>
        private static void BuildMeteorImpact()
        {
            Material glow = EffectMaterial(BuildSprite());
            Material fire = ParticleMaterial(FireMaterialPath, FirePuffSprite(), ParticleBlend.Additive, Color.white);
            Material smoke = ParticleMaterial(SmokeMaterialPath, FirePuffSprite(), ParticleBlend.Alpha, Color.white);
            Material shock = ParticleMaterial(ShockwaveMaterialPath, ShockwaveSprite(), ParticleBlend.Additive, Color.white);
            Material debris = ParticleMaterial(DebrisMaterialPath, null, ParticleBlend.Opaque, Color.white);
            Light light = FlashLight(MeteorLightPath, new Color(1f, 0.62f, 0.3f), 16f);
            if (glow == null || fire == null || smoke == null || shock == null || debris == null)
                return;
            float radius = DemoSkillBuilder.AreaRadius("Meteor");
            if (radius <= 0f)
                radius = 5f;

            string path = $"{EffectDir}/{MeteorImpactName}.prefab";
            AudioClip[] sounds = ExistingSounds(path);
            var root = new GameObject(MeteorImpactName);
            try
            {
                ImpactFlash(root.transform, glow, light, radius);
                ImpactShockwave(root.transform, shock, radius);
                ImpactFireball(root.transform, fire);
                ImpactSparks(root.transform, glow);
                ImpactDebris(root.transform, debris);
                ImpactSmoke(root.transform, smoke);
                ImpactEmbers(root.transform, glow);
                ImpactAfterglow(root.transform, glow, radius);

                var effect = root.AddComponent<GameEffect>();
                effect.effectSocket = string.Empty;
                effect.stayInPlace = true;
                effect.isLoop = false;
                // As long as the smoke takes to clear.
                effect.lifeTime = 3.6f;
                effect.PoolSize = 3;
                // Silent by design - the boom is DemoMeteorStrike's, timed to its whoosh - but a
                // sound wired on by hand survives a rebuild like any other effect's.
                effect.randomSoundEffects = sounds;

                PrefabUtility.SaveAsPrefabAsset(root, path);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// A white-out at the point of impact, carrying a point light that lights the ground and
        /// anyone standing near for the moment it lasts - at night the only light for twenty metres.
        /// The light is the particle system's own (its Lights module), so it lives and fades with
        /// the flash and needs no script.
        /// </summary>
        private static void ImpactFlash(Transform parent, Material material, Light light, float radius)
        {
            ParticleSystem flash = Emitter(parent, "Flash", material, 2);
            flash.transform.localPosition = new Vector3(0f, 1f, 0f);
            ParticleSystem.MainModule main = flash.main;
            main.loop = false;
            main.duration = 0.5f;
            main.startLifetime = 0.45f;
            main.startSpeed = 0f;
            main.startSize = radius * 1.5f;
            ParticleSystem.EmissionModule emission = flash.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
            ParticleSystem.ShapeModule shape = flash.shape;
            shape.enabled = false;
            Tint(flash, Ramp((0f, Color.white, 1f), (0.25f, FireHot, 0.8f), (1f, Ember, 0f)));
            Shrink(flash, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(0.2f, 1.1f), new Keyframe(1f, 0.8f)));
            if (light == null)
                return;
            ParticleSystem.LightsModule lights = flash.lights;
            lights.enabled = true;
            lights.light = light;
            lights.ratio = 1f;
            lights.maxLights = 1;
            lights.useParticleColor = true;
            lights.alphaAffectsIntensity = true;
            lights.sizeAffectsRange = false;
            lights.intensityMultiplier = 7f;
            lights.rangeMultiplier = 1f;
        }

        /// <summary>
        /// A ring laid on the ground, running out to just past the area's edge in half a second:
        /// the blast radius, drawn. Flat in the world, so it clips a hillside - over too quickly
        /// to matter.
        /// </summary>
        private static void ImpactShockwave(Transform parent, Material material, float radius)
        {
            ParticleSystem wave = Emitter(parent, "Shockwave", material, 2);
            wave.transform.localPosition = new Vector3(0f, 0.25f, 0f);
            ParticleSystem.MainModule main = wave.main;
            main.loop = false;
            main.duration = 0.5f;
            main.startLifetime = 0.5f;
            main.startSpeed = 0f;
            // The texture's ring sits at 0.86 of its width, so this puts it a little outside
            // the radius at full size.
            main.startSize = radius * 2.5f;
            ParticleSystem.EmissionModule emission = wave.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
            ParticleSystem.ShapeModule shape = wave.shape;
            shape.enabled = false;
            Tint(wave, Ramp((0f, FireHot, 0.95f), (0.4f, Ember, 0.6f), (1f, Ember, 0f)));
            Shrink(wave, new AnimationCurve(new Keyframe(0f, 0.08f, 0f, 3f), new Keyframe(0.35f, 0.75f), new Keyframe(1f, 1f)));
            wave.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
        }

        /// <summary>
        /// The fireball: two dozen billows of the noisy fire sprite thrown up and out, slowed hard
        /// so they pile up into one rolling ball rather than scattering, swelling and cooling from
        /// white through orange to a dull red as they rise.
        /// </summary>
        private static void ImpactFireball(Transform parent, Material material)
        {
            ParticleSystem balls = Emitter(parent, "Fireball", material, 40);
            ParticleSystem.MainModule main = balls.main;
            main.loop = false;
            main.duration = 0.3f;
            // Under a second, so the fire has gone before it can hang about as a red cloud - the
            // first pass lived 1.25s and ended as a glowing blob - and the smoke takes over.
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.55f, 0.95f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(4f, 9f);
            main.startSize = new ParticleSystem.MinMaxCurve(2.2f, 3.8f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            // Buoyant: the ball lifts off the ground as it burns, into a plume.
            main.gravityModifier = -0.35f;
            ParticleSystem.EmissionModule emission = balls.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 18), new ParticleSystem.Burst(0.05f, 8) });
            Dome(balls, 1f);
            Drag(balls, 1.5f, 0.15f);
            Spin(balls, 0.7f);
            Tint(balls, Ramp((0f, Color.white, 1f), (0.1f, FireHot, 1f), (0.35f, Ember, 0.9f),
                             (0.7f, FireDeep, 0.45f), (1f, FireDeep, 0f)));
            Shrink(balls, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(0.3f, 1.25f), new Keyframe(1f, 1.7f)));
            balls.GetComponent<ParticleSystemRenderer>().sortingFudge = -10f;
        }

        /// <summary>Sparks flung out of the blast in arcs.</summary>
        private static void ImpactSparks(Transform parent, Material material)
        {
            ParticleSystem sparks = Emitter(parent, "Sparks", material, 200, stretch: true);
            ParticleSystem.MainModule main = sparks.main;
            main.loop = false;
            main.duration = 0.3f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.3f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(7f, 17f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.11f);
            main.gravityModifier = 1.4f;
            ParticleSystem.EmissionModule emission = sparks.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 90) });
            Dome(sparks, 0.5f);
            Tint(sparks, Ramp((0f, Color.white, 1f), (0.3f, FireHot, 1f), (0.7f, Ember, 0.7f), (1f, Ember, 0f)));
            sparks.GetComponent<ParticleSystemRenderer>().sortingFudge = -12f;
        }

        /// <summary>
        /// Clods of earth: small dark tumbling cubes thrown up and falling back, bouncing off what
        /// they land on. Opaque, not glowing - the one solid thing in the burst.
        /// </summary>
        private static void ImpactDebris(Transform parent, Material material)
        {
            ParticleSystem clods = Emitter(parent, "Debris", material, 40);
            ParticleSystem.MainModule main = clods.main;
            main.loop = false;
            main.duration = 0.3f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(5f, 11f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.3f);
            main.gravityModifier = 2.4f;
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.16f, 0.13f, 0.11f), new Color(0.26f, 0.22f, 0.18f));
            ParticleSystem.EmissionModule emission = clods.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 22) });
            Dome(clods, 0.6f);
            ParticleSystem.RotationOverLifetimeModule spin = clods.rotationOverLifetime;
            spin.enabled = true;
            spin.separateAxes = true;
            spin.x = new ParticleSystem.MinMaxCurve(-6f, 6f);
            spin.y = new ParticleSystem.MinMaxCurve(-6f, 6f);
            spin.z = new ParticleSystem.MinMaxCurve(-6f, 6f);
            // Opaque, so they cannot fade: they shrink away at the very end instead.
            Shrink(clods, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.85f, 1f), new Keyframe(1f, 0f)));
            ParticleSystem.CollisionModule collision = clods.collision;
            collision.enabled = true;
            collision.type = ParticleSystemCollisionType.World;
            collision.mode = ParticleSystemCollisionMode.Collision3D;
            collision.dampen = 0.35f;
            collision.bounce = 0.25f;
            collision.lifetimeLoss = 0f;
            collision.quality = ParticleSystemCollisionQuality.Medium;
            collision.radiusScale = 0.5f;
            var renderer = clods.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            renderer.alignment = ParticleSystemRenderSpace.World;
        }

        /// <summary>
        /// The smoke the fire leaves: sixteen dark billows, a beat behind the flash, rising and
        /// spreading and thinning for three seconds - the part of the impact still there after
        /// everything bright is gone.
        /// </summary>
        private static void ImpactSmoke(Transform parent, Material material)
        {
            ParticleSystem smoke = Emitter(parent, "Smoke", material, 40);
            ParticleSystem.MainModule main = smoke.main;
            main.loop = false;
            main.duration = 0.3f;
            main.startDelay = 0.18f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.2f, 3.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 4.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(2.8f, 4.4f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = -0.18f;
            ParticleSystem.EmissionModule emission = smoke.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 16) });
            Dome(smoke, 2.2f);
            Drag(smoke, 1f, 0.08f);
            Spin(smoke, 0.35f);
            // Between the two passes: at a third opaque it was there by count and gone by eye,
            // and at 0.85 near-black it was a cloud that swallowed half the screen.
            Color soot = Color.Lerp(Smoke, Color.white, 0.08f);
            Color thinning = Color.Lerp(Smoke, Color.white, 0.25f);
            Tint(smoke, Ramp((0f, soot, 0f), (0.1f, soot, 0.72f), (0.55f, thinning, 0.45f), (1f, thinning, 0f)));
            Shrink(smoke, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 1.9f)));
            var renderer = smoke.GetComponent<ParticleSystemRenderer>();
            renderer.sortMode = ParticleSystemSortMode.Distance;
            renderer.sortingFudge = 10f;
        }

        /// <summary>Embers drifting up out of the smoke after the blast, on a little turbulence.</summary>
        private static void ImpactEmbers(Transform parent, Material material)
        {
            ParticleSystem embers = Emitter(parent, "Embers", material, 100);
            ParticleSystem.MainModule main = embers.main;
            main.loop = false;
            main.duration = 0.3f;
            main.startDelay = 0.1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.09f);
            main.gravityModifier = -0.12f;
            ParticleSystem.EmissionModule emission = embers.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 45) });
            Dome(embers, 1.2f);
            ParticleSystem.NoiseModule noise = embers.noise;
            noise.enabled = true;
            noise.strength = 0.6f;
            noise.frequency = 0.8f;
            Tint(embers, Ramp((0f, FireHot, 1f), (0.5f, Ember, 0.8f), (1f, Ember, 0f)));
        }

        /// <summary>The ground glowing where it was hit, for a second after the fire has risen off it.</summary>
        private static void ImpactAfterglow(Transform parent, Material material, float radius)
        {
            ParticleSystem glow = Emitter(parent, "Afterglow", material, 2);
            glow.transform.localPosition = new Vector3(0f, 0.15f, 0f);
            ParticleSystem.MainModule main = glow.main;
            main.loop = false;
            main.duration = 0.3f;
            main.startLifetime = 1.4f;
            main.startSpeed = 0f;
            main.startSize = radius * 1.6f;
            ParticleSystem.EmissionModule emission = glow.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0.05f, 1) });
            ParticleSystem.ShapeModule shape = glow.shape;
            shape.enabled = false;
            Tint(glow, Ramp((0f, Ember, 0f), (0.15f, Ember, 0.55f), (1f, FireDeep, 0f)));
            glow.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
        }

        /// <summary>A half-sphere shape, dome up: out of the ground and into the air.</summary>
        private static void Dome(ParticleSystem system, float radius)
        {
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = radius;
            // The hemisphere bulges along the shape's +Z; this turns it to +Y.
            shape.rotation = new Vector3(-90f, 0f, 0f);
        }

        /// <summary>Slows particles toward <paramref name="limit"/> m/s: a burst that piles up rather than scatters.</summary>
        private static void Drag(ParticleSystem system, float limit, float dampen)
        {
            ParticleSystem.LimitVelocityOverLifetimeModule drag = system.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.limit = limit;
            drag.dampen = dampen;
        }

        /// <summary>Turns each particle at up to <paramref name="radiansPerSecond"/> either way.</summary>
        private static void Spin(ParticleSystem system, float radiansPerSecond)
        {
            ParticleSystem.RotationOverLifetimeModule spin = system.rotationOverLifetime;
            spin.enabled = true;
            spin.z = new ParticleSystem.MinMaxCurve(-radiansPerSecond, radiansPerSecond);
        }

        /// <summary>
        /// The light a flash carries, as a prefab because that is what the Lights module takes.
        /// Coloured rather than white - the meteor's orange, the nova's pale blue - so it reads as
        /// fire or cold lighting the ground rather than as a lamp.
        /// </summary>
        private static Light FlashLight(string path, Color colour, float range)
        {
            var go = new GameObject(System.IO.Path.GetFileNameWithoutExtension(path));
            try
            {
                var light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = colour;
                light.range = range;
                light.intensity = 1f;
                light.shadows = LightShadows.None;
                DemoItemBuilder.EnsureFolder(EffectDir);
                PrefabUtility.SaveAsPrefabAsset(go, path);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
            var saved = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            return saved != null ? saved.GetComponent<Light>() : null;
        }

        // ---- Frost Nova --------------------------------------------------------

        /// <summary>
        /// Metres a second the nova runs outward. The shock front, the motes along the ground, the
        /// ice breaking out behind them and the frost spreading with them all keep this pace, so
        /// they reach the edge together - and with the damage, which a burst area bites with 0.3s
        /// after it appears (DemoSkillBuilder's BurstBite): 4.5 metres at 14 is 0.32s.
        /// </summary>
        private const float FrostWaveSpeed = 14f;

        /// <summary>The frost's breath: the mist a nova rolls out and a frozen character gives off.</summary>
        private static readonly Color ColdAir = new Color(0.84f, 0.93f, 1.00f);

        /// <summary>
        /// Frost Nova going off: `FX_FrostNovaBurst`, a pooled GameEffect that <see cref="AreaLandEffect"/>
        /// fetches where the nova's area appears - the frame its damage is decided.
        ///
        /// Until 2026-09-25 the whole spell was a flash, a disc of white light the size of the area and
        /// a ring of motes at the caster's feet: light, where the spell is cold, and gone at once. Built
        /// in the order it reads: a cold flash lighting the ground, a shock front and a ring of motes
        /// running out to the edge, ice breaking out of the ground behind them (<see cref="DemoIceShards"/>)
        /// over frost spreading with them (<see cref="GroundCircle"/>), cold air rolling out along the
        /// ground and glitter hanging in it - then the ice shattering and the frost melting away over the
        /// next two seconds. Whatever it caught keeps its own ice for as long as it stays frozen:
        /// <see cref="BuildFrozen"/>.
        /// </summary>
        private static void BuildFrostNovaBurst()
        {
            Material glow = EffectMaterial(BuildSprite());
            Material shock = ParticleMaterial(ShockwaveMaterialPath, ShockwaveSprite(), ParticleBlend.Additive, Color.white);
            Material mist = ParticleMaterial(MistMaterialPath, FirePuffSprite(), ParticleBlend.Alpha, Color.white, lit: true);
            Material frost = FrostMaterial();
            Material ice = IceMaterial();
            Mesh shard = IceShardMesh();
            Light light = FlashLight(FrostLightPath, new Color(0.62f, 0.84f, 1f), 12f);
            if (glow == null || shock == null || mist == null || frost == null || ice == null)
                return;
            float radius = DemoSkillBuilder.AreaRadius("FrostNova");
            if (radius <= 0f)
                radius = 4.5f;

            string path = $"{EffectDir}/{FrostNovaBurstName}.prefab";
            AudioClip[] sounds = ExistingSounds(path);
            var root = new GameObject(FrostNovaBurstName);
            try
            {
                FrostFlash(root.transform, glow, light, radius);
                FrostShockwave(root.transform, shock, radius);
                FrostRing(root.transform, glow, radius);

                // Over twice the wave's time, with the ease GroundCircle gives it - fast off the
                // middle and slowing - so it runs just behind the shock front and settles on the edge
                // a moment after it.
                FrostPatch(root.transform, frost, radius, segments: 64, rings: 12,
                           grow: 2f * radius / FrostWaveSpeed, hold: 1.3f, fade: 1.3f);

                DemoIceShards shards = IceShards(root.transform, shard, ice, glow, 360);
                // A dozen clumps of three, the biggest out at the rim.
                shards.count = 36;
                shards.clump = 3;
                // Clear of the caster, who stands in the middle.
                shards.innerRadius = 1.1f;
                shards.outerRadius = radius * 0.95f;
                shards.length = new Vector2(0.5f, 1.5f);
                shards.thickness = new Vector2(0.16f, 0.34f);
                shards.lean = 26f;
                shards.waveSpeed = FrostWaveSpeed;
                shards.growSeconds = 0.12f;
                shards.holdSeconds = 1.1f;
                shards.shatterSeconds = 0.35f;
                shards.fragmentsEach = 6;
                shards.chipsEach = 3;
                // The field breaks over half a second: a shatter as the first goes and two more
                // through it, rather than one for three dozen crystals.
                shards.shatterSoundEvery = 12;
                shards.soundVolume = 0.8f;

                FrostMist(root.transform, mist, radius);
                FrostGlints(root.transform, glow, radius);

                var effect = root.AddComponent<GameEffect>();
                effect.effectSocket = string.Empty;
                effect.stayInPlace = true;
                effect.isLoop = false;
                // Until the frost has gone: grown, held and faded by 3.24s.
                effect.lifeTime = 3.4f;
                effect.PoolSize = 2;
                // Nothing of its own: the nova's sound is the caster's clip, and the ice's are on
                // its crystals (DemoAudioWiring.WireIceSounds). A sound wired on by hand survives
                // a rebuild like any other effect's.
                effect.randomSoundEffects = sounds;

                PrefabUtility.SaveAsPrefabAsset(root, path);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// The ice on a frozen character: `FX_Frozen`, which the kit plays at the feet of anything a
        /// Freeze ailment lands on and keeps there while it lasts (<see cref="WriteFreezeEffect"/>).
        /// Crystals close round the feet, frost under them, a breath of cold air and glitter; when the
        /// freeze ends, <see cref="DemoFrozenEffect"/> breaks the crystals and melts the frost.
        ///
        /// Knee-high at most and a hand's breadth out, because the body is not measured: a wolf is
        /// long where a bandit is tall, and anything taller would come up through a wolf's back.
        /// </summary>
        private static void BuildFrozen()
        {
            Material glow = EffectMaterial(BuildSprite());
            Material mist = ParticleMaterial(MistMaterialPath, FirePuffSprite(), ParticleBlend.Alpha, Color.white, lit: true);
            Material frost = FrostMaterial();
            Material ice = IceMaterial();
            Mesh shard = IceShardMesh();
            if (glow == null || mist == null || frost == null || ice == null)
                return;

            string path = $"{EffectDir}/{FrozenName}.prefab";
            AudioClip[] sounds = ExistingSounds(path);
            var root = new GameObject(FrozenName);
            try
            {
                FrostPatch(root.transform, frost, 0.95f, segments: 32, rings: 4, grow: 0.2f, hold: -1f, fade: 0.5f);

                DemoIceShards shards = IceShards(root.transform, shard, ice, glow, 120);
                shards.count = 9;
                shards.clump = 1;
                shards.innerRadius = 0.26f;
                shards.outerRadius = 0.5f;
                shards.length = new Vector2(0.35f, 0.75f);
                shards.thickness = new Vector2(0.12f, 0.22f);
                shards.lean = 16f;
                // All at once, and standing until the freeze ends.
                shards.waveSpeed = 0f;
                shards.growSeconds = 0.1f;
                shards.holdSeconds = -1f;
                shards.shatterSeconds = 0.3f;
                shards.fragmentsEach = 7;
                shards.chipsEach = 2;
                // Quieter than the nova's: one nova often freezes three at once.
                shards.soundVolume = 0.7f;

                FrozenMist(root.transform, mist);
                FrozenGlints(root.transform, glow);

                var effect = root.AddComponent<DemoFrozenEffect>();
                effect.effectSocket = SocketFloor;
                // Following the character rather than left where it froze: if the character goes
                // while frozen (cleared away dead, logged out), the kit sees what it follows is gone
                // and ends the effect. Left in place, a looping effect would stand there for good.
                effect.stayInPlace = false;
                // It has no end of its own; the freeze ending ends it.
                effect.isLoop = true;
                // Time for the crystals to break and their glitter to fall, once it does.
                effect.lifeTime = 1.1f;
                // Enough for everything one nova catches.
                effect.PoolSize = 6;
                effect.randomSoundEffects = sounds;

                PrefabUtility.SaveAsPrefabAsset(root, path);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// Puts `FX_Frozen` on the game instance's freeze effects, which the kit plays on anything a
        /// Freeze ailment lands on. The list was empty. Added to rather than written over, so an
        /// effect put there by hand stays.
        /// </summary>
        private static void WriteFreezeEffect()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GameInstancePath);
            GameInstance instance = prefab != null ? prefab.GetComponent<GameInstance>() : null;
            GameEffect frozen = Effect(FrozenName);
            if (instance == null || frozen == null)
            {
                Debug.LogWarning($"[{nameof(DemoSkillEffectBuilder)}] No {GameInstancePath} or no {FrozenName}; " +
                                 "frozen characters are left without ice.");
                return;
            }
            var serialized = new SerializedObject(instance);
            SerializedProperty list = serialized.FindProperty("freezeEffects");
            if (list == null)
            {
                Debug.LogWarning($"[{nameof(DemoSkillEffectBuilder)}] GameInstance has no \"freezeEffects\"; " +
                                 "has the kit renamed it?");
                return;
            }
            for (int i = 0; i < list.arraySize; ++i)
            {
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == frozen)
                    return;
            }
            list.InsertArrayElementAtIndex(list.arraySize);
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = frozen;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(prefab);
            AssetDatabase.SaveAssetIfDirty(prefab);
        }

        /// <summary>
        /// The crack of cold where the nova starts: a pale flash off the ground, small next to the
        /// meteor's - the ice is the spell's shape, not this. It carries a blue light that lights the
        /// ground and whoever is near, then stays on, dim, while the ice stands: at night, what the
        /// crystals are seen by. Flash and light share one long-lived particle; the flash is over in
        /// its first sixth, and the light follows its own curve.
        /// </summary>
        private static void FrostFlash(Transform parent, Material material, Light light, float radius)
        {
            ParticleSystem flash = Emitter(parent, "Flash", material, 2);
            flash.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            ParticleSystem.MainModule main = flash.main;
            main.loop = false;
            main.duration = 0.3f;
            main.startLifetime = 1.8f;
            main.startSpeed = 0f;
            main.startSize = radius * 0.7f;
            ParticleSystem.EmissionModule emission = flash.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
            ParticleSystem.ShapeModule shape = flash.shape;
            shape.enabled = false;
            Color hot = Color.Lerp(Frost, Color.white, 0.6f);
            Tint(flash, Ramp((0f, hot, 0.8f), (0.06f, Frost, 0.45f), (0.17f, Frost, 0f), (1f, Frost, 0f)));
            Shrink(flash, new AnimationCurve(new Keyframe(0f, 0.4f), new Keyframe(0.05f, 1f), new Keyframe(0.17f, 1.1f),
                                             new Keyframe(1f, 1.1f)));
            if (light == null)
                return;
            ParticleSystem.LightsModule lights = flash.lights;
            lights.enabled = true;
            lights.light = light;
            lights.ratio = 1f;
            lights.maxLights = 1;
            lights.useParticleColor = true;
            // Its own curve, not the flash's fade.
            lights.alphaAffectsIntensity = false;
            lights.sizeAffectsRange = false;
            lights.intensity = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 1f), new Keyframe(0.12f, 0.22f), new Keyframe(0.7f, 0.15f), new Keyframe(1f, 0f)));
            lights.intensityMultiplier = 6f;
            lights.rangeMultiplier = 1f;
        }

        /// <summary>
        /// The shock front: the shockwave ring laid on the ground, pale, running out at the wave's pace
        /// to a little past the edge. Flat in the world, like the meteor's.
        /// </summary>
        private static void FrostShockwave(Transform parent, Material material, float radius)
        {
            ParticleSystem wave = Emitter(parent, "Shockwave", material, 2);
            wave.transform.localPosition = new Vector3(0f, 0.2f, 0f);
            ParticleSystem.MainModule main = wave.main;
            main.loop = false;
            main.duration = 0.3f;
            // A little past the edge, so it is still bright as it crosses it.
            main.startLifetime = 1.15f * radius / FrostWaveSpeed;
            main.startSpeed = 0f;
            // The texture's ring sits at 0.86 of the way out. At full size that puts it 1.15 radii
            // out, and grown linearly it keeps the wave's pace all the way there.
            main.startSize = 2f * 1.15f * radius / 0.86f;
            ParticleSystem.EmissionModule emission = wave.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
            ParticleSystem.ShapeModule shape = wave.shape;
            shape.enabled = false;
            Color hot = Color.Lerp(Frost, Color.white, 0.5f);
            Tint(wave, Ramp((0f, hot, 0.9f), (0.7f, Frost, 0.55f), (1f, Frost, 0f)));
            Shrink(wave, AnimationCurve.Linear(0f, 0.02f, 1f, 1f));
            var renderer = wave.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            // Wider than half the screen from close up, which is where particles are clamped by default.
            renderer.maxParticleSize = 4f;
        }

        /// <summary>Motes streaking out along the ground at the wave's pace: the leading edge of the cold.</summary>
        private static void FrostRing(Transform parent, Material material, float radius)
        {
            ParticleSystem ring = Emitter(parent, "Ring", material, 200, stretch: true);
            ring.transform.localPosition = new Vector3(0f, 0.15f, 0f);
            ParticleSystem.MainModule main = ring.main;
            main.loop = false;
            main.duration = 0.3f;
            // Reaching the edge as they die.
            main.startLifetime = radius / FrostWaveSpeed;
            main.startSpeed = FrostWaveSpeed;
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.14f);
            ParticleSystem.EmissionModule emission = ring.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 120) });
            ParticleSystem.ShapeModule shape = ring.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.15f;
            shape.rotation = new Vector3(90f, 0f, 0f);   // lay the circle flat
            shape.radiusThickness = 0f;
            Color hot = Color.Lerp(Frost, Color.white, 0.5f);
            Tint(ring, Ramp((0f, hot, 0.95f), (0.75f, Frost, 0.6f), (1f, Frost, 0f)));
            Shrink(ring, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.5f)));
        }

        /// <summary>
        /// Frost on the ground: the frost sprite on a <see cref="GroundCircle"/>, so it lies over a
        /// hillside the way the aiming circle does, spreading from its middle as it appears. Its ragged
        /// edge, not the sprite's corner, is put on <paramref name="radius"/>.
        /// </summary>
        private static GroundCircle FrostPatch(Transform parent, Material material, float radius, int segments,
                                                   int rings, float grow, float hold, float fade)
        {
            var go = new GameObject("Frost");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            var circle = go.AddComponent<GroundCircle>();
            circle.radius = radius;
            circle.rimAt = FrostEdgeAt;
            circle.segments = segments;
            circle.rings = rings;
            // Half the aiming circle's lift: frost lies on the ground. Still off it, because a surface
            // in the terrain's own plane flickers through it.
            circle.lift = 0.06f;
            circle.pulseDepth = 0f;
            circle.growSeconds = grow;
            circle.holdSeconds = hold;
            circle.fadeSeconds = fade;
            return circle;
        }

        /// <summary>
        /// A <see cref="DemoIceShards"/> and the glitter it throws. The crystals are made by the
        /// component when it wakes; the prefab holds only the mesh and material to make them from.
        /// </summary>
        private static DemoIceShards IceShards(Transform parent, Mesh mesh, Material ice, Material glitter, int most)
        {
            var go = new GameObject("Ice");
            go.transform.SetParent(parent, false);
            var shards = go.AddComponent<DemoIceShards>();
            shards.mesh = mesh;
            shards.material = ice;
            shards.fragments = Glitter(go.transform, glitter, most);
            return shards;
        }

        /// <summary>
        /// Where the crystals throw their glitter: bits of ice that tumble and fall, catching the
        /// light as they go. Nothing is emitted from here - <see cref="DemoIceShards"/> emits into it
        /// by hand - and it simulates in the world, so what a crystal throws stays where it was thrown.
        /// </summary>
        private static ParticleSystem Glitter(Transform parent, Material material, int most)
        {
            ParticleSystem glitter = Emitter(parent, "Glitter", material, most, stretch: true);
            ParticleSystem.MainModule main = glitter.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.9f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.09f);
            main.gravityModifier = 1.1f;
            ParticleSystem.EmissionModule emission = glitter.emission;
            emission.rateOverTime = 0f;
            ParticleSystem.ShapeModule shape = glitter.shape;
            shape.enabled = false;
            Color hot = Color.Lerp(Frost, Color.white, 0.7f);
            Tint(glitter, Ramp((0f, hot, 1f), (0.5f, Frost, 0.8f), (1f, Frost, 0f)));
            Shrink(glitter, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.4f)));
            return glitter;
        }

        /// <summary>
        /// Cold air rolling out over the frost: pale billows thrown outward from the middle and
        /// slowed hard, so they settle into a thin bank and fade.
        ///
        /// Kept off the ground. A billboard that reaches into the ground is cut off in a straight
        /// line where it meets it, and the first pass - two-metre billows at knee height - drew a
        /// lit rectangle across the frost at night. So each billow rides at least 0.43 of its own
        /// size up, where the sprite has faded to nothing, and drifts up as it grows to stay there.
        /// Soft particles would hide the cut too, but see <see cref="ParticleMaterial"/>.
        /// </summary>
        private static void FrostMist(Transform parent, Material material, float radius)
        {
            ParticleSystem mist = Emitter(parent, "Mist", material, 30);
            mist.transform.localPosition = new Vector3(0f, 0.6f, 0f);
            ParticleSystem.MainModule main = mist.main;
            main.loop = false;
            main.duration = 0.2f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.3f, 2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(radius * 0.6f, radius * 1.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.9f, 1.4f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            // Rising 0.3m over a life, which is what the growth below needs to stay clear.
            main.gravityModifier = -0.03f;
            ParticleSystem.EmissionModule emission = mist.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 18) });
            ParticleSystem.ShapeModule shape = mist.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.5f;
            shape.rotation = new Vector3(90f, 0f, 0f);
            Drag(mist, 0.4f, 0.08f);
            Spin(mist, 0.3f);
            // Thin: at 0.3 it hid the enemies it had just frozen.
            Tint(mist, Ramp((0f, ColdAir, 0f), (0.12f, ColdAir, 0.24f), (0.6f, ColdAir, 0.14f), (1f, ColdAir, 0f)));
            Shrink(mist, new AnimationCurve(new Keyframe(0f, 0.7f), new Keyframe(1f, 1.35f)));
            mist.GetComponent<ParticleSystemRenderer>().sortMode = ParticleSystemSortMode.Distance;
        }

        /// <summary>
        /// Glitter hanging in the cold air over the patch, rising a little and winking as it drifts:
        /// what is still glittering after the ice has broken.
        /// </summary>
        private static void FrostGlints(Transform parent, Material material, float radius)
        {
            ParticleSystem glints = Emitter(parent, "Glints", material, 120);
            glints.transform.localPosition = new Vector3(0f, 0.3f, 0f);
            ParticleSystem.MainModule main = glints.main;
            main.loop = false;
            main.duration = 0.4f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.07f);
            main.gravityModifier = -0.04f;
            ParticleSystem.EmissionModule emission = glints.emission;
            emission.rateOverTime = 0f;
            // Three bursts while the ice comes up.
            emission.SetBursts(new[]
            {
                new ParticleSystem.Burst(0.05f, 20), new ParticleSystem.Burst(0.15f, 25), new ParticleSystem.Burst(0.28f, 30),
            });
            ParticleSystem.ShapeModule shape = glints.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = radius * 0.9f;
            shape.radiusThickness = 1f;
            shape.rotation = new Vector3(90f, 0f, 0f);
            ParticleSystem.NoiseModule noise = glints.noise;
            noise.enabled = true;
            noise.strength = 0.25f;
            noise.frequency = 0.6f;
            Twinkle(glints, Color.Lerp(Frost, Color.white, 0.5f));
        }

        /// <summary>
        /// A breath of cold air curling off the ice round a frozen character's legs - kept off the
        /// ground the way the nova's mist is (<see cref="FrostMist"/>).
        /// </summary>
        private static void FrozenMist(Transform parent, Material material)
        {
            ParticleSystem mist = Emitter(parent, "Mist", material, 16);
            mist.transform.localPosition = new Vector3(0f, 0.45f, 0f);
            ParticleSystem.MainModule main = mist.main;
            main.loop = true;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 1.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.45f, 0.75f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = -0.02f;
            ParticleSystem.EmissionModule emission = mist.emission;
            emission.rateOverTime = 5f;
            ParticleSystem.ShapeModule shape = mist.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.4f;
            shape.rotation = new Vector3(90f, 0f, 0f);
            Spin(mist, 0.3f);
            Tint(mist, Ramp((0f, ColdAir, 0f), (0.3f, ColdAir, 0.2f), (1f, ColdAir, 0f)));
            Shrink(mist, new AnimationCurve(new Keyframe(0f, 0.7f), new Keyframe(1f, 1.3f)));
        }

        /// <summary>Glitter winking round a frozen character's legs.</summary>
        private static void FrozenGlints(Transform parent, Material material)
        {
            ParticleSystem glints = Emitter(parent, "Glints", material, 30);
            glints.transform.localPosition = new Vector3(0f, 0.45f, 0f);
            ParticleSystem.MainModule main = glints.main;
            main.loop = true;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.055f);
            ParticleSystem.EmissionModule emission = glints.emission;
            emission.rateOverTime = 10f;
            ParticleSystem.ShapeModule shape = glints.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(0.9f, 0.8f, 0.9f);
            Twinkle(glints, Color.Lerp(Frost, Color.white, 0.5f));
        }

        /// <summary>
        /// Winks on and off over its life, like frost catching the light. Lifetimes that differ from
        /// particle to particle keep them from winking together.
        /// </summary>
        private static void Twinkle(ParticleSystem system, Color colour)
        {
            Tint(system, Ramp((0f, colour, 0f), (0.1f, colour, 1f), (0.25f, colour, 0.15f), (0.42f, colour, 0.95f),
                              (0.6f, colour, 0.2f), (0.78f, colour, 0.85f), (1f, colour, 0f)));
        }

        /// <summary>
        /// The crystals' material: lit, so their facets catch the sun and the sky like ice instead of
        /// glowing like the rest of the spell; see-through, with URP's "preserve specular" so the
        /// highlights stay at full strength on a crystal the grass shows through; and faintly
        /// emissive, so at night it is still there.
        ///
        /// Set up through URP's own <see cref="BaseShaderGUI.SetMaterialKeywords"/> rather than
        /// property by property: a lit transparent surface is a dozen settings and keywords, and the
        /// material inspector would re-derive them on the first edit anyway.
        /// </summary>
        private static Material IceMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogError($"[{nameof(DemoSkillEffectBuilder)}] No URP lit shader.");
                return null;
            }
            var material = AssetDatabase.LoadAssetAtPath<Material>(IceMaterialPath);
            if (material == null)
            {
                material = new Material(shader);
                DemoItemBuilder.EnsureFolder(MaterialDir);
                AssetDatabase.CreateAsset(material, IceMaterialPath);
            }
            material.shader = shader;
            material.SetColor("_BaseColor", new Color(0.70f, 0.88f, 1.00f, 0.6f));
            material.SetFloat("_Smoothness", 0.93f);
            material.SetFloat("_Metallic", 0f);
            material.SetColor("_EmissionColor", new Color(0.12f, 0.30f, 0.50f));
            // Without an emissive flag URP turns the emission keyword off, whatever the colour.
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_BlendModePreserveSpecular", 1f);
            material.SetFloat("_Cull", (float)CullMode.Back);
            material.SetFloat("_AlphaClip", 0f);
            BaseShaderGUI.SetMaterialKeywords(material);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// The frost's material: the frost sprite, blended over the ground rather than added to it, so
        /// it whitens the grass instead of lighting it. Drawn ahead of every other transparent thing,
        /// because it lies under them: transparent things are sorted by their middles, and at the
        /// default queue a crystal standing behind the patch's middle was drawn first and then
        /// frosted over.
        ///
        /// Lit, like the ground it lies on - white in the sun, blue in the nova's own light, dim at
        /// night - with a little emission so it never goes out altogether. The first pass was unlit,
        /// and at night it was a bright disc on black ground: the very circle it replaced.
        /// </summary>
        private static Material FrostMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogError($"[{nameof(DemoSkillEffectBuilder)}] No URP lit shader.");
                return null;
            }
            var material = AssetDatabase.LoadAssetAtPath<Material>(FrostMaterialPath);
            if (material == null)
            {
                material = new Material(shader);
                DemoItemBuilder.EnsureFolder(MaterialDir);
                AssetDatabase.CreateAsset(material, FrostMaterialPath);
            }
            material.shader = shader;
            material.SetTexture("_BaseMap", FrostSprite());
            material.SetColor("_BaseColor", new Color(0.86f, 0.94f, 1.00f, 0.92f));
            // A little sheen: frost glints where the light catches it.
            material.SetFloat("_Smoothness", 0.55f);
            material.SetFloat("_Metallic", 0f);
            material.SetColor("_EmissionColor", new Color(0.16f, 0.24f, 0.32f));
            // Without an emissive flag URP turns the emission keyword off, whatever the colour.
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_BlendModePreserveSpecular", 0f);
            material.SetFloat("_Cull", (float)CullMode.Off);
            material.SetFloat("_AlphaClip", 0f);
            // Ten ahead of the transparent queue, through the offset rather than the queue itself,
            // which URP recomputes from the offset whenever the material is touched.
            material.SetFloat("_QueueOffset", -10f);
            BaseShaderGUI.SetMaterialKeywords(material);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// The crystal: a six-sided prism narrowing to a point a little off its axis, flat-shaded so
        /// every face catches the light on its own - base on the ground at the origin, tip at y = 1,
        /// one unit across, as <see cref="DemoIceShards"/> expects. An asset because a prefab cannot
        /// hold a mesh made in code, and refilled in place when it exists, so whatever uses it keeps it.
        /// </summary>
        private static Mesh IceShardMesh()
        {
            const int sides = 6;
            // Where the point starts, as a share of the height.
            const float shoulder = 0.68f;
            var apex = new Vector3(0.05f, 1f, 0.03f);
            var bottom = new Vector3[sides];
            var top = new Vector3[sides];
            for (int i = 0; i < sides; ++i)
            {
                float angle = i * Mathf.PI * 2f / sides;
                var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                bottom[i] = direction * 0.5f;
                top[i] = direction * 0.42f + Vector3.up * shoulder;
            }

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var triangles = new List<int>();
            for (int i = 0; i < sides; ++i)
            {
                int next = (i + 1) % sides;
                Vector3 outward = (bottom[i] + bottom[next]).normalized;
                Face(vertices, normals, triangles, bottom[i], top[i], top[next], outward);
                Face(vertices, normals, triangles, bottom[i], top[next], bottom[next], outward);
                Face(vertices, normals, triangles, top[i], apex, top[next], outward + Vector3.up);
                // Capped underneath: a crystal leaning hard on a slope can show its foot.
                Face(vertices, normals, triangles, Vector3.zero, bottom[next], bottom[i], Vector3.down);
            }

            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(IceShardPath);
            bool made = mesh == null;
            if (made)
                mesh = new Mesh();
            mesh.Clear();
            mesh.name = "IceShard";
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            if (made)
            {
                DemoItemBuilder.EnsureFolder(EffectDir);
                AssetDatabase.CreateAsset(mesh, IceShardPath);
            }
            else
            {
                EditorUtility.SetDirty(mesh);
            }
            return mesh;
        }

        /// <summary>One flat triangle, wound to face <paramref name="outward"/>.</summary>
        private static void Face(List<Vector3> vertices, List<Vector3> normals, List<int> triangles,
                                 Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
        {
            // Unity's front faces wind clockwise, which puts this cross product on the viewer's side.
            Vector3 normal = Vector3.Cross(b - a, c - a);
            if (Vector3.Dot(normal, outward) < 0f)
            {
                (b, c) = (c, b);
                normal = -normal;
            }
            normal.Normalize();
            int start = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            normals.Add(normal);
            normals.Add(normal);
            normals.Add(normal);
            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
        }

        /// <summary>
        /// Frost as it forms on the ground: a thin mottled rime, thicker in a band just inside its
        /// edge where the wave stopped, grown over with feathers - branches running outward from near
        /// the middle and shooting side branches at sixty degrees, the way frost grows across a
        /// window - and flecked with glints. The edge is ragged, and on average
        /// <see cref="FrostEdgeAt"/> of the way out. Drawn only into a gap.
        /// </summary>
        private static Texture2D FrostSprite()
        {
            const int size = 512;
            float[] frost = System.IO.File.Exists(FrostTexturePath) ? null : DrawFrost(size);
            return DrawSprite(FrostTexturePath, size, (u, v, r) =>
                frost[Mathf.Min(size - 1, (int)(v * size)) * size + Mathf.Min(size - 1, (int)(u * size))]);
        }

        private static float[] DrawFrost(int size)
        {
            var random = new System.Random(20260925);
            // The ragged edge: waves of a few lengths round the circle, averaging FrostEdgeAt, and
            // never more than a tenth either side of it - so it stays inside the sprite.
            var phases = new float[4];
            for (int i = 0; i < phases.Length; ++i)
                phases[i] = (float)random.NextDouble() * Mathf.PI * 2f;
            System.Func<float, float> edge = angle => FrostEdgeAt
                + 0.045f * Mathf.Sin(3f * angle + phases[0])
                + 0.030f * Mathf.Sin(7f * angle + phases[1])
                + 0.018f * Mathf.Sin(13f * angle + phases[2])
                + 0.010f * Mathf.Sin(29f * angle + phases[3]);

            var rime = new float[size * size];
            for (int y = 0; y < size; ++y)
            {
                for (int x = 0; x < size; ++x)
                {
                    float u = (x + 0.5f) / size * 2f - 1f;
                    float v = (y + 0.5f) / size * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float rim = edge(Mathf.Atan2(v, u));
                    float inside = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(rim, rim - 0.05f, r));
                    if (inside <= 0f)
                        continue;
                    float mottle = Fractal(x * 7f / size, y * 7f / size);
                    float front = Mathf.Exp(-Mathf.Pow((rim - 0.06f - r) / 0.07f, 2f));
                    rime[y * size + x] = inside * (0.16f + 0.3f * mottle + 0.22f * front);
                }
            }

            var feathers = new float[size * size];
            for (int i = 0; i < 45; ++i)
            {
                float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                float from = Mathf.Lerp(0.04f, 0.55f, (float)random.NextDouble());
                Vector2 start = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * from;
                float heading = angle + Mathf.Lerp(-0.35f, 0.35f, (float)random.NextDouble());
                Feather(feathers, size, random, edge, start, heading,
                        Mathf.Lerp(0.2f, 0.55f, (float)random.NextDouble()), 0.75f, 2);
            }
            for (int i = 0; i < 1200; ++i)
            {
                float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                float r = Mathf.Sqrt((float)random.NextDouble()) * (edge(angle) - 0.03f);
                Stamp(feathers, size, new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * r,
                      Mathf.Lerp(0.5f, 1f, (float)random.NextDouble()), 0.4f);
            }

            for (int i = 0; i < rime.Length; ++i)
                rime[i] = Mathf.Clamp01(rime[i] + feathers[i] * 0.85f);
            return rime;
        }

        /// <summary>
        /// One frost feather, walked outward in small wandering steps from <paramref name="start"/> (in
        /// the sprite's -1..1 space), shooting side branches at sixty degrees either way as it goes -
        /// each a smaller feather of its own - until it has gone <paramref name="reach"/> or met the edge.
        /// </summary>
        private static void Feather(float[] feathers, int size, System.Random random, System.Func<float, float> edge,
                                    Vector2 start, float heading, float reach, float strength, int depth)
        {
            // A texel and a half a step.
            float stride = 3f / size;
            Vector2 at = start;
            float walked = 0f;
            float shootAt = Mathf.Lerp(0.02f, 0.05f, (float)random.NextDouble());
            int side = random.Next(2) == 0 ? 1 : -1;
            while (walked < reach && at.magnitude < edge(Mathf.Atan2(at.y, at.x)) - 0.01f)
            {
                Stamp(feathers, size, at, strength * Mathf.Lerp(1f, 0.45f, walked / reach), depth == 2 ? 0.9f : 0.6f);
                heading += Mathf.Lerp(-0.07f, 0.07f, (float)random.NextDouble());
                at += new Vector2(Mathf.Cos(heading), Mathf.Sin(heading)) * stride;
                walked += stride;
                if (depth > 0 && walked >= shootAt)
                {
                    Feather(feathers, size, random, edge, at, heading + side * Mathf.PI / 3f,
                            (reach - walked) * Mathf.Lerp(0.25f, 0.5f, (float)random.NextDouble()), strength * 0.75f, depth - 1);
                    side = -side;
                    shootAt = walked + Mathf.Lerp(0.025f, 0.06f, (float)random.NextDouble());
                }
            }
        }

        /// <summary>
        /// A soft dot at <paramref name="at"/> (in the sprite's -1..1 space), <paramref name="width"/>
        /// texels either side of a one-texel core, keeping whichever is brighter where dots overlap -
        /// so crossing feathers do not burn out where they meet.
        /// </summary>
        private static void Stamp(float[] feathers, int size, Vector2 at, float strength, float width)
        {
            float cx = (at.x * 0.5f + 0.5f) * size - 0.5f;
            float cy = (at.y * 0.5f + 0.5f) * size - 0.5f;
            float reach = width + 0.8f;
            for (int y = Mathf.FloorToInt(cy - reach); y <= Mathf.CeilToInt(cy + reach); ++y)
            {
                for (int x = Mathf.FloorToInt(cx - reach); x <= Mathf.CeilToInt(cx + reach); ++x)
                {
                    if (x < 0 || y < 0 || x >= size || y >= size)
                        continue;
                    float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    float a = strength * Mathf.Clamp01(1f - d / reach);
                    int i = y * size + x;
                    if (a > feathers[i])
                        feathers[i] = a;
                }
            }
        }

        /// <summary>Four octaves of Perlin noise, stretched to fill nought to one.</summary>
        private static float Fractal(float x, float y)
        {
            float noise = 0f;
            float amplitude = 0.5f;
            float frequency = 1f;
            for (int octave = 0; octave < 4; ++octave)
            {
                noise += amplitude * Mathf.PerlinNoise(x * frequency + 11.3f * octave, y * frequency + 7.9f * octave);
                frequency *= 2f;
                amplitude *= 0.5f;
            }
            return Mathf.Clamp01((noise / 0.9375f - 0.3f) / 0.4f);
        }

        /// <summary>The trail colours. A shot's job is to be read in flight, so each says what it does.</summary>
        public static readonly Color ArrowPlain = new Color(0.85f, 0.82f, 0.70f);
        public static readonly Color ArrowAimed = new Color(1.00f, 0.95f, 0.72f);
        public static readonly Color ArrowCrippling = new Color(0.50f, 0.90f, 0.45f);
        public static readonly Color ArrowMark = new Color(1.00f, 0.72f, 0.25f);

        /// <summary>
        /// Dresses a missile: a streak behind it, and for a spell the glowing body of the
        /// thing itself.
        ///
        /// <paramref name="coreSize"/> above zero replaces the need for a mesh. The mage's
        /// bolt was a lit primitive sphere, which is a ball with a highlight on it - a
        /// thrown spell should be a light, and two additive billboards do that far better
        /// than any small mesh can.
        ///
        /// **The trail emits over distance, not over time.** That is the difference between
        /// a trail and a mess on these: a pooled missile that emitted per second would spit
        /// particles while it sat in the pool and lay a denser streak the slower it flew.
        /// Per metre, a still missile emits nothing at all and the streak reads the same at
        /// any speed. Lifetimes are kept short for the same reason - a pooled object
        /// reappearing across the map with live particles still on it would draw a line
        /// between the two places.
        /// </summary>
        public static void AddMissileDressing(GameObject root, Color colour, float trailWidth, float coreSize)
        {
            Material material = EffectMaterial(BuildSprite());
            if (material == null)
                return;

            if (coreSize > 0f)
            {
                Color hot = Color.Lerp(colour, Color.white, 0.8f);
                AddMissileCore(root, material, colour, coreSize * 2.4f, 0.22f, "Glow");
                AddMissileCore(root, material, hot, coreSize, 1f, "Core");
            }

            AddMissileRibbon(root, material, colour, trailWidth);

            var go = new GameObject("Sparks");
            go.transform.SetParent(root.transform, false);
            var trail = go.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = trail.main;
            main.loop = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 220;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.14f, 0.28f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(trailWidth * 0.6f, trailWidth);
            main.startColor = Color.white;

            ParticleSystem.EmissionModule emission = trail.emission;
            emission.rateOverTime = 0f;
            // Over-distance particles are emitted at each frame's position rather than
            // interpolated across the step, so at a bow's 38 m/s they land in clumps a
            // frame apart however high the rate is - which is why the ribbon exists and
            // why raising this is not an alternative to it.
            //
            // Pitched to stand on its own all the same. The ribbon cannot be verified
            // outside play mode, so if it disappoints, this is what is left, and 25 a
            // metre reads as a shot rather than as three dots.
            emission.rateOverDistance = 25f;

            ParticleSystem.ShapeModule shape = trail.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = trailWidth * 0.35f;

            Tint(trail, Ramp((0f, colour, 0.85f), (0.5f, colour, 0.45f), (1f, colour, 0f)));
            Shrink(trail, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.2f)));

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortMode = ParticleSystemSortMode.None;
        }

        /// <summary>
        /// The streak itself, as a <see cref="TrailRenderer"/> rather than particles,
        /// because a trail renderer interpolates along the path between frames and an
        /// emitter does not.
        ///
        /// **A pooled projectile with a trail renderer draws a line across the map.** The
        /// component keeps its points when the object is disabled, so the next shot - which
        /// is the same object, fetched from the pool somewhere else entirely - joins the
        /// two positions with a ribbon. The fix is `Clear()` on every fetch, wired here as
        /// a persistent listener on `PoolDescriptor.onGetInstance` so it survives into the
        /// prefab rather than needing a script of its own.
        /// </summary>
        private static void AddMissileRibbon(GameObject root, Material material, Color colour, float width)
        {
            var go = new GameObject("Ribbon");
            go.transform.SetParent(root.transform, false);
            var trail = go.AddComponent<TrailRenderer>();
            trail.time = 0.13f;
            trail.startWidth = width * 1.5f;
            trail.endWidth = 0f;
            trail.minVertexDistance = 0.04f;
            trail.autodestruct = false;
            trail.emitting = true;
            trail.alignment = LineAlignment.View;
            trail.textureMode = LineTextureMode.Stretch;
            trail.numCapVertices = 2;
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.receiveShadows = false;
            // NOT the particle material: URP's Particles/Unlit reads per-particle vertex
            // streams a TrailRenderer never writes. Precautionary rather than diagnosed -
            // a trail renderer cannot be made to draw in edit mode at all (a manually fed
            // one reports 81 points and correct bounds but `isVisible` false, with
            // Sprites/Default as much as with anything else), so this one is the standard
            // safe choice and wants a look in play mode.
            trail.sharedMaterial = RibbonMaterial();
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(colour, 0f), new GradientColorKey(colour, 1f) },
                new[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0f, 1f) });
            trail.colorGradient = gradient;

            var pool = root.GetComponent<PoolDescriptor>();
            if (pool == null)
            {
                Debug.LogWarning($"[{nameof(DemoSkillEffectBuilder)}] \"{root.name}\" has no PoolDescriptor yet, " +
                                 "so its trail will not be cleared when it is fetched from the pool and the " +
                                 "second shot will draw a ribbon from wherever the first one ended. Add the " +
                                 "missile component before dressing it.");
                return;
            }
            UnityEditor.Events.UnityEventTools.AddVoidPersistentListener(
                pool.onGetInstance, trail.Clear);
        }

        /// <summary>
        /// The trail renderers' material: plain URP unlit, blended additive. Separate from
        /// the particle one for the reason given at its use - the particle shader draws
        /// nothing on a trail.
        /// </summary>
        private static Material RibbonMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                Debug.LogError($"[{nameof(DemoSkillEffectBuilder)}] No URP unlit shader.");
                return null;
            }
            string path = MaterialDir + "/MI_SkillRibbon.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                DemoItemBuilder.EnsureFolder(MaterialDir);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            material.SetTexture("_BaseMap", BuildSprite());
            material.SetColor("_BaseColor", Color.white);
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

        /// <summary>One layer of a spell missile's body: a single billboard that rides with it.</summary>
        private static void AddMissileCore(GameObject root, Material material, Color colour,
                                           float size, float alpha, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            var system = go.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.playOnAwake = true;
            // Local, unlike the trail: this one IS the missile and has to travel with it.
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 4;
            main.startLifetime = 0.5f;
            main.startSpeed = 0f;
            main.startSize = size;
            main.startColor = Color.white;
            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = 12f;
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = false;
            Tint(system, Ramp((0f, colour, alpha), (1f, colour, alpha)));

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortMode = ParticleSystemSortMode.None;
        }

        // ---- building one effect ---------------------------------------------

        private static void Build(Recipe recipe, Material material)
        {
            string path = $"{EffectDir}/{recipe.Name}.prefab";
            AudioClip[] sounds = ExistingSounds(path);
            var root = new GameObject(recipe.Name);
            try
            {
                if (recipe.Core > 0f)
                    Core(root.transform, material, recipe);
                if (recipe.Gather)
                    Gather(root.transform, material, recipe);
                if (recipe.Rise)
                    Rise(root.transform, material, recipe);
                if (recipe.Burst > 0f)
                    Burst(root.transform, material, recipe);

                if (recipe.Delay > 0f)
                {
                    foreach (ParticleSystem system in root.GetComponentsInChildren<ParticleSystem>())
                    {
                        ParticleSystem.MainModule main = system.main;
                        main.startDelay = recipe.Delay;
                    }
                }
                if (recipe.Sounds)
                {
                    var voice = root.AddComponent<GameEffectSounds>();
                    voice.volume = recipe.Socket == SocketBody ? 0.7f : 0.6f;
                    voice.near = 5f;
                    voice.delay = recipe.SoundDelay;
                }

                GameEffect effect = recipe.Buff ? root.AddComponent<BuffGameEffect>() : root.AddComponent<GameEffect>();
                effect.effectSocket = recipe.Socket;
                // `GameEffect.isLoop` is not "the emitters repeat" - it is "this never
                // expires": the component does `_destroyTime = isLoop ? -1 : Time.time +
                // lifeTime`, and -1 is read as never. Nothing stops a cast effect, so
                // setting it was what left Arcane Bolt sparkling on the hand for good.
                // It is left off wherever a lifetime is given, which is everywhere; the
                // emitters keep looping on their own from `Loop`.
                // A worn effect is the exception: the buff ending ends it.
                effect.isLoop = recipe.Buff || (recipe.Loop && recipe.LifeTime <= 0f);
                effect.lifeTime = recipe.LifeTime;
                effect.PoolSize = 4;
                effect.randomSoundEffects = sounds;

                PrefabUtility.SaveAsPrefabAsset(root, path);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// The sounds on an effect prefab that is about to be built again, so the rebuild keeps them.
        ///
        /// They are not this builder's: Wire Audio writes them after it (the weapon-hit family onto
        /// `FX_HitPhysical`, which every sword, arrow and bite in the demo plays). Each effect is
        /// rebuilt from a fresh object, so until 2026-09-24 running this step on its own silenced
        /// every weapon hit until Wire Audio was run again.
        /// </summary>
        private static AudioClip[] ExistingSounds(string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameEffect>(path);
            if (existing == null || existing.randomSoundEffects == null)
                return new AudioClip[0];
            return existing.randomSoundEffects;
        }

        /// <summary>
        /// The light at the middle of the effect, in two layers: a small near-white core
        /// and a wide dim halo around it.
        ///
        /// One layer was not enough. A single soft ball at the effect's colour reads as a
        /// coloured blob; what makes something look like it is emitting light is the
        /// falloff - a hot centre that has burnt past its own hue into white, with the
        /// colour living in the glow around it. Additive blending does the rest.
        /// </summary>
        private static void Core(Transform parent, Material material, Recipe recipe)
        {
            float life = recipe.Loop ? 0.8f : Mathf.Max(0.25f, recipe.LifeTime * 0.6f);
            Color hot = Color.Lerp(recipe.Colour, Color.white, 0.75f);

            ParticleSystem halo = Emitter(parent, "Halo", material, 8);
            ParticleSystem.MainModule haloMain = halo.main;
            haloMain.loop = recipe.Loop;
            haloMain.startLifetime = life;
            haloMain.startSpeed = 0f;
            haloMain.startSize = recipe.Core * 2.2f;
            ParticleSystem.EmissionModule haloEmission = halo.emission;
            haloEmission.rateOverTime = recipe.Loop ? 4f : 0f;
            if (!recipe.Loop)
                haloEmission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
            Tint(halo, Ramp((0f, recipe.Colour, 0f), (0.25f, recipe.Colour, 0.17f), (1f, recipe.Colour, 0f)));
            Shrink(halo, new AnimationCurve(new Keyframe(0f, 0.55f), new Keyframe(0.35f, 1.15f), new Keyframe(1f, 0.9f)));

            ParticleSystem core = Emitter(parent, "Core", material, 8);
            ParticleSystem.MainModule main = core.main;
            main.loop = recipe.Loop;
            main.startLifetime = life;
            main.startSpeed = 0f;
            main.startSize = recipe.Core * 0.85f;
            ParticleSystem.EmissionModule emission = core.emission;
            emission.rateOverTime = recipe.Loop ? 4f : 0f;
            if (!recipe.Loop)
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
            Tint(core, Ramp((0f, hot, 0f), (0.2f, hot, 1f), (0.6f, recipe.Colour, 0.8f), (1f, recipe.Colour, 0f)));
            Shrink(core, new AnimationCurve(new Keyframe(0f, 0.4f), new Keyframe(0.3f, 1.1f), new Keyframe(1f, 0.6f)));
        }

        /// <summary>
        /// Motes falling inward onto the socket. A negative start speed on a sphere shape
        /// is what does it - the particles are born on the shell and travel to the centre,
        /// which is "charging up" without needing a single keyframe.
        /// </summary>
        private static void Gather(Transform parent, Material material, Recipe recipe)
        {
            ParticleSystem system = Emitter(parent, "Gather", material, 400, stretch: true);
            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.55f);
            main.startSpeed = -Mathf.Max(1.4f, recipe.Core * 9f);
            // Small. Density is what reads as energy; big motes read as a handful of
            // dots however many of them there are.
            main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.065f);
            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = 190f;
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = Mathf.Max(0.45f, recipe.Core * 3.4f);
            shape.radiusThickness = 0.35f;
            Tint(system, Ramp((0f, recipe.Colour, 0f), (0.4f, recipe.Colour, 0.9f), (1f, recipe.Colour, 0.1f)));
            Shrink(system, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 1.1f)));
        }

        /// <summary>Motes drifting up and fading. A heal, or embers off something building.</summary>
        private static void Rise(Transform parent, Material material, Recipe recipe)
        {
            ParticleSystem system = Emitter(parent, "Rise", material, 220);
            ParticleSystem.MainModule main = system.main;
            main.loop = recipe.Loop;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.3f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.35f, 0.9f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.075f);
            main.gravityModifier = -0.08f;
            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = recipe.Loop ? 75f : 0f;
            if (!recipe.Loop)
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 240) });
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = Mathf.Max(0.2f, recipe.Core * 1.4f);
            Tint(system, Ramp((0f, recipe.Colour, 0f), (0.3f, recipe.Colour, 0.85f), (1f, recipe.Colour, 0f)));
        }

        /// <summary>The one-shot spray: what leaves the hand when the spell goes off.</summary>
        private static void Burst(Transform parent, Material material, Recipe recipe)
        {
            ParticleSystem system = Emitter(parent, "Burst", material, 220, stretch: true);
            ParticleSystem.MainModule main = system.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = 0.4f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(recipe.Burst * 12f, recipe.Burst * 26f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.035f, 0.09f);
            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 140) });
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = recipe.Burst;
            Tint(system, Ramp((0f, recipe.Colour, 0.95f), (0.6f, recipe.Colour, 0.6f), (1f, recipe.Colour, 0f)));
            Shrink(system, new AnimationCurve(new Keyframe(0f, 1.1f), new Keyframe(1f, 0.3f)));
        }

        // ---- shared plumbing --------------------------------------------------

        /// <summary>
        /// One emitter. `stretch` draws each particle smeared along its own velocity,
        /// which is most of what separates a spell from a handful of dots: a mote that
        /// leaves a streak reads as moving fast, and forty streaks converging read as one
        /// thing gathering. Still billboards where there is no motion to smear - a core
        /// stretched by its own zero velocity just disappears.
        /// </summary>
        private static ParticleSystem Emitter(Transform parent, string name, Material material, int most,
                                              bool stretch = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var system = go.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = system.main;
            main.playOnAwake = false;
            main.prewarm = false;
            // Local, so the whole effect rides the socket it is hung on: a gather in the
            // fist has to travel with the fist through the cast.
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = most;
            main.startColor = Color.white;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = stretch ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
            if (stretch)
            {
                // Short dashes, not spikes. 0.09 was the first try and at a gather's
                // 1.4 m/s it drew 13cm streaks off a 3cm mote - the effect read as a
                // sea urchin rather than as something being drawn inward.
                renderer.velocityScale = 0.022f;
                renderer.lengthScale = 2.2f;
            }
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortMode = ParticleSystemSortMode.None;
            return system;
        }

        private static void Tint(ParticleSystem system, Gradient gradient)
        {
            ParticleSystem.ColorOverLifetimeModule colour = system.colorOverLifetime;
            colour.enabled = true;
            colour.color = new ParticleSystem.MinMaxGradient(gradient);
        }

        private static void Shrink(ParticleSystem system, AnimationCurve curve)
        {
            ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, curve);
        }

        private static Gradient Ramp(params (float at, Color colour, float alpha)[] keys)
        {
            var colours = new GradientColorKey[keys.Length];
            var alphas = new GradientAlphaKey[keys.Length];
            for (int i = 0; i < keys.Length; ++i)
            {
                colours[i] = new GradientColorKey(keys[i].colour, keys[i].at);
                alphas[i] = new GradientAlphaKey(keys[i].alpha, keys[i].at);
            }
            var gradient = new Gradient();
            gradient.SetKeys(colours, alphas);
            return gradient;
        }

        /// <summary>
        /// Additive and unlit, so a spell is a light on the scene rather than a decal over
        /// it - and so it still reads at night, when the demo's sun has gone.
        ///
        /// The blend is written property by property for the same reason the torch's is:
        /// the shader reads _SrcBlend and _DstBlend, and the material inspector's Blend
        /// dropdown is the only thing that normally sets them.
        /// </summary>
        private static Material EffectMaterial(Texture2D sprite)
        {
            // Over white, so the sprite is brighter than anything lit can be and the
            // bloom picks it out.
            return ParticleMaterial(MaterialPath, sprite, ParticleBlend.Additive, new Color(1.15f, 1.15f, 1.15f, 1f));
        }

        /// <summary>How a particle material draws.</summary>
        private enum ParticleBlend
        {
            /// <summary>Light on the scene: fire, sparks, glows.</summary>
            Additive,
            /// <summary>Laid over it: smoke, which has to be able to be darker than what is behind.</summary>
            Alpha,
            /// <summary>Solid: the meteor's clods of earth.</summary>
            Opaque,
        }

        /// <summary>
        /// A URP particle material, found or made at <paramref name="path"/> and rewritten every
        /// time, so a change here reaches it. Each particle's own colour multiplies the texture,
        /// which is how one white sprite serves every spell. Unlit unless <paramref name="lit"/>:
        /// light is what most of these are, and it has to show at night; mist is not, and unlit it
        /// glowed there.
        /// </summary>
        private static Material ParticleMaterial(string path, Texture2D sprite, ParticleBlend blend, Color colour,
                                                 bool lit = false)
        {
            Shader shader = Shader.Find(lit ? "Universal Render Pipeline/Particles/Lit"
                                            : "Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
            {
                Debug.LogError($"[{nameof(DemoSkillEffectBuilder)}] No URP particle shader.");
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
            material.SetFloat("_ColorMode", 0f);
            material.SetFloat("_Cull", (float)CullMode.Off);
            material.SetFloat("_AlphaClip", 0f);
            // Never soft. It would fade billows where they meet the ground rather than cut a line
            // there, but it reads the camera's depth texture, which is the project's setting and
            // not the demo's: without one the shader reads garbage, and on some platforms that
            // is every particle gone. Billows are kept off the ground instead (FrostMist).
            material.SetFloat("_SoftParticlesEnabled", 0f);
            if (lit)
            {
                // Matte: mist and smoke have no sheen.
                material.SetFloat("_Smoothness", 0f);
                material.SetFloat("_Metallic", 0f);
                // The lit shader defaults this on, and URP then rewrites the blend below into its
                // premultiplied form the next time it validates the material.
                material.SetFloat("_BlendModePreserveSpecular", 0f);
            }
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.DisableKeyword("_ALPHAMODULATE_ON");
            if (blend == ParticleBlend.Opaque)
            {
                material.SetFloat("_Surface", 0f);
                material.SetFloat("_ZWrite", 1f);
                material.SetFloat("_SrcBlend", (float)BlendMode.One);
                material.SetFloat("_DstBlend", (float)BlendMode.Zero);
                material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                material.SetFloat("_DstBlendAlpha", (float)BlendMode.Zero);
                material.SetOverrideTag("RenderType", "Opaque");
                material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = (int)RenderQueue.Geometry;
            }
            else
            {
                bool additive = blend == ParticleBlend.Additive;
                // The shader reads _SrcBlend and _DstBlend; only the material inspector sets them
                // from _Blend, so both are written.
                material.SetFloat("_Surface", 1f);
                material.SetFloat("_Blend", additive ? 2f : 0f);
                material.SetFloat("_ZWrite", 0f);
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
                material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                material.SetFloat("_DstBlendAlpha", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
                material.SetOverrideTag("RenderType", "Transparent");
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = (int)RenderQueue.Transparent;
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// A billow: the soft round sprite broken up by fractal noise, so a pile of them reads as
        /// fire or smoke rather than as a heap of balls. Drawn only into a gap.
        /// </summary>
        private static Texture2D FirePuffSprite()
        {
            return DrawSprite(FirePuffPath, 128, (x, y, r) =>
            {
                float noise = 0f;
                float amplitude = 0.5f;
                float frequency = 3f;
                for (int octave = 0; octave < 4; ++octave)
                {
                    noise += amplitude * Mathf.PerlinNoise(x * frequency + 11.3f * octave, y * frequency + 7.9f * octave);
                    frequency *= 2f;
                    amplitude *= 0.5f;
                }
                noise /= 0.9375f;
                // The noise stretched to its full range, so the billow has lobes and gaps in it
                // rather than a faint mottle: a first pass at a gentler contrast rendered as smooth
                // blobs, and sixteen of them as one featureless dark cloud.
                float detail = Mathf.Clamp01((noise - 0.32f) / 0.36f);
                float falloff = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1f, 0.2f, r));
                return falloff * (0.25f + 0.85f * detail);
            });
        }

        /// <summary>
        /// A shock front: a bright edge at 0.86 of the way out with a glow trailing in behind it,
        /// and nothing in the middle. Drawn only into a gap.
        /// </summary>
        private static Texture2D ShockwaveSprite()
        {
            const float front = 0.86f;
            return DrawSprite(ShockwavePath, 256, (x, y, r) =>
            {
                float a = r <= front
                    ? 0.85f * Mathf.Exp(-(front - r) / 0.1f)
                    : Mathf.Exp(-Mathf.Pow((r - front) / 0.035f, 2f));
                return a * Mathf.Clamp01((1f - r) * 40f);
            });
        }

        /// <summary>
        /// A white sprite whose alpha is <paramref name="alpha"/>(x, y, r) - x and y from 0 to 1
        /// across it, r from the middle to the edge - written only when the file is missing, so a
        /// hand-made one at the same path is kept.
        /// </summary>
        private static Texture2D DrawSprite(string path, int size, System.Func<float, float, float, float> alpha)
        {
            if (!System.IO.File.Exists(path))
            {
                var pixels = new Color32[size * size];
                for (int y = 0; y < size; ++y)
                {
                    for (int x = 0; x < size; ++x)
                    {
                        float u = (x + 0.5f) / size;
                        float v = (y + 0.5f) / size;
                        float r = Mathf.Sqrt((u * 2f - 1f) * (u * 2f - 1f) + (v * 2f - 1f) * (v * 2f - 1f));
                        float a = Mathf.Clamp01(alpha(u, v, r));
                        pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                    }
                }
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                texture.SetPixels32(pixels);
                texture.Apply();
                DemoItemBuilder.EnsureFolder(System.IO.Path.GetDirectoryName(path).Replace('\\', '/'));
                System.IO.File.WriteAllBytes(path, texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }

            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null && (importer.textureType != TextureImporterType.Default || !importer.alphaIsTransparency ||
                                     importer.wrapMode != TextureWrapMode.Clamp))
            {
                importer.textureType = TextureImporterType.Default;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.mipmapEnabled = true;
                importer.sRGBTexture = true;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>
        /// A soft round sprite: white in the middle, gone at the edge. The same shape the
        /// torch flame uses and for the same reason - it is sixty lines of arithmetic
        /// against a texture whose licence would otherwise have to be checked. Its own
        /// copy rather than the flame's, so neither tool has to be run before the other.
        /// </summary>
        private static Texture2D BuildSprite()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
            if (existing != null)
                return existing;

            const int size = 128;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; ++y)
            {
                for (int x = 0; x < size; ++x)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f;
                    float dy = (y + 0.5f) / size * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - r);
                    a = a * a * (3f - 2f * a);
                    // Steeper than the flame's: a spell mote wants a small hot core and a
                    // long soft falloff, or fifty of them together read as fog.
                    a = Mathf.Pow(a, 2.1f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply();
            DemoItemBuilder.EnsureFolder(System.IO.Path.GetDirectoryName(TexturePath).Replace('\\', '/'));
            System.IO.File.WriteAllBytes(TexturePath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(TexturePath, ImportAssetOptions.ForceUpdate);

            var importer = AssetImporter.GetAtPath(TexturePath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.mipmapEnabled = true;
                importer.sRGBTexture = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
        }
    }
}
