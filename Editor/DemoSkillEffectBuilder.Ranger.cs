using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// The ranger's effects, and the debuffs' (2026-09-25).
    ///
    /// Until then the ranger had one thing the mage did not lack: a coloured streak behind each
    /// arrow. No cast, no release, no hit of its own, and three debuffs - a slow, a mark and a bleed
    /// - that nothing showed on the thing that had them. The pieces here, in the order a shot reads:
    ///
    /// - **the draw** (`FX_AimedDraw`, `FX_VolleyDraw`, `FX_CripplingDraw`): light gathering on the
    ///   arrowhead once the bow is up, recipes in the main file;
    /// - **the release** (`FX_AimedRelease`, `FX_VolleyRelease`, and the two small ones), set off by
    ///   BowEquipmentEntity on the skill's trigger - the loose - rather than by the kit, which would play them a
    ///   whole draw early (SkillReleaseEffects). Pointed by <see cref="GameEffectAim"/>;
    /// - **the rain** (`FX_VolleyRain`): real arrows, <see cref="DemoVolleyRain"/>;
    /// - **what the victim wears** (`FX_Crippled`, `FX_HuntersMark`, `FX_Bleeding`): each a
    ///   <see cref="BuffGameEffect"/>, on for exactly as long as its debuff.
    /// </summary>
    public static partial class DemoSkillEffectBuilder
    {
        public const string SocketLeftHand = "LeftHand";

        /// <summary>Volley's arrows coming down, fetched by <see cref="AreaLandEffect"/> where its area lands.</summary>
        public const string VolleyRainName = "FX_VolleyRain";

        /// <summary>Ash and fletching: Volley's colour, where it was the mage's Frost until 2026-09-25.</summary>
        public static readonly Color Timber = new Color(0.86f, 0.70f, 0.46f);

        private static readonly Color Dirt = new Color(0.40f, 0.33f, 0.24f);
        private static readonly Color Blood = new Color(0.62f, 0.03f, 0.04f);
        /// <summary>
        /// Dark, not leaf-bright: the vines have to show on grass, which is itself green and lit.
        /// The first pass at (0.36, 0.52, 0.20) was a pale smudge there (live, 2026-09-25).
        /// </summary>
        private static readonly Color Vine = new Color(0.15f, 0.26f, 0.07f);

        private const string ArrowPrefabPath = "Assets/OpenMMORPG/Demo/Prefabs/GamePlay/Equipments/Arrow.prefab";
        private const string VinesTexturePath = "Assets/OpenMMORPG/Demo/Textures/SkillVines.png";
        private const string ReticleTexturePath = "Assets/OpenMMORPG/Demo/Textures/SkillReticle.png";
        private const string VinesMaterialPath = MaterialDir + "/MI_SkillVines.mat";
        private const string BloodMaterialPath = MaterialDir + "/MI_SkillBlood.mat";
        private const string DirtMaterialPath = MaterialDir + "/MI_SkillDirt.mat";
        private const string ReticleAlphaMaterialPath = MaterialDir + "/MI_SkillReticleSolid.mat";
        private const string ArrowLightPath = EffectDir + "/FX_ArrowFlashLight.prefab";

        /// <summary>Everything in this file. Run by Build Skill Effects, before Build Skills reads them.</summary>
        private static void BuildRanger()
        {
            BuildAimedRelease();
            BuildVolleyRelease();
            BuildVolleyRain();
            BuildCrippled();
            BuildHuntersMark();
            BuildBleeding();
        }

        // ---- releases ----------------------------------------------------------

        /// <summary>
        /// Aimed Shot leaving the string: a flash at the bow hand with a light in it, a ring snapping
        /// out round the arrow's line, and a cone of sparks thrown after it. The ring and sparks go
        /// where the archer faces (<see cref="GameEffectAim"/>), not where the hand's bone points.
        /// </summary>
        private static void BuildAimedRelease()
        {
            Material glow = EffectMaterial(BuildSprite());
            Material shock = ParticleMaterial(ShockwaveMaterialPath, ShockwaveSprite(), ParticleBlend.Additive, Color.white);
            Light light = FlashLight(ArrowLightPath, new Color(1f, 0.9f, 0.7f), 4f);
            if (glow == null || shock == null)
                return;

            const string name = "FX_AimedRelease";
            var root = new GameObject(name);
            try
            {
                ParticleSystem core = ReleaseFlash(root.transform, glow, ArrowAimed, 0.22f);
                // A glint, not a lamp: at 5 it lit the archer's arms and face flat white for the
                // length of the flash (measured live, 2026-09-25). The light is what reads at night;
                // by day the flash itself carries it.
                FlashLightOn(core, light, 1.2f);

                Transform pivot = Pivot(root.transform);
                ParticleSystem sparks = AimedSpray(pivot, "Sparks", glow, ArrowAimed, most: 60,
                                                   speed: new Vector2(14f, 30f), life: new Vector2(0.12f, 0.3f),
                                                   size: new Vector2(0.025f, 0.05f), cone: 7f, velocityScale: 0.02f);
                ParticleSystem ring = AimedRing(pivot, shock, ArrowAimed, size: 0.95f, life: 0.3f);
                GameEffectAim aim = root.AddComponent<GameEffectAim>();
                aim.pivot = pivot;
                aim.pitch = 0f;
                aim.systems = new[] { sparks, ring };
                aim.counts = new[] { 45, 1 };

                Sounds(root, volume: 0.85f, near: 6f);
                SaveEffect<GameEffect>(root, name, SocketLeftHand, lifeTime: 0.6f, pool: 4);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// Volley going up: a dozen long streaks leaving the bow steeply, fanned a little, with a
        /// spit of sparks - what the rain that comes down on the patch was.
        /// </summary>
        private static void BuildVolleyRelease()
        {
            Material glow = EffectMaterial(BuildSprite());
            if (glow == null)
                return;

            const string name = "FX_VolleyRelease";
            var root = new GameObject(name);
            try
            {
                ReleaseFlash(root.transform, glow, Timber, 0.16f);
                Transform pivot = Pivot(root.transform);
                // At the high clips' own angle (42-45 degrees at the loose), a little over.
                const float pitch = 50f;
                ParticleSystem arrows = AimedSpray(pivot, "Arrows", glow, Color.Lerp(Timber, Color.white, 0.45f), most: 30,
                                                   speed: new Vector2(26f, 34f), life: new Vector2(0.35f, 0.55f),
                                                   size: new Vector2(0.045f, 0.07f), cone: 11f, velocityScale: 0.03f);
                ParticleSystem.MainModule arrowsMain = arrows.main;
                arrowsMain.gravityModifier = 0.4f;
                ParticleSystem sparks = AimedSpray(pivot, "Sparks", glow, Timber, most: 40,
                                                   speed: new Vector2(5f, 11f), life: new Vector2(0.15f, 0.3f),
                                                   size: new Vector2(0.02f, 0.04f), cone: 28f, velocityScale: 0.02f);
                GameEffectAim aim = root.AddComponent<GameEffectAim>();
                aim.pivot = pivot;
                aim.pitch = pitch;
                aim.systems = new[] { arrows, sparks };
                aim.counts = new[] { 12, 24 };

                Sounds(root, volume: 0.85f, near: 6f);
                SaveEffect<GameEffect>(root, name, SocketLeftHand, lifeTime: 0.7f, pool: 2);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // ---- the rain ----------------------------------------------------------

        /// <summary>
        /// `FX_VolleyRain`: <see cref="DemoVolleyRain"/> and the three systems it emits into by hand -
        /// the streak laid along each falling arrow, the dirt it kicks up, and the clods thrown.
        /// Lives until the stuck arrows have sunk.
        /// </summary>
        private static void BuildVolleyRain()
        {
            Material glow = EffectMaterial(BuildSprite());
            Material dust = ParticleMaterial(SmokeMaterialPath, FirePuffSprite(), ParticleBlend.Alpha, Color.white);
            Material dirt = ParticleMaterial(DirtMaterialPath, BuildSprite(), ParticleBlend.Alpha, Color.white);
            var arrow = AssetDatabase.LoadAssetAtPath<GameObject>(ArrowPrefabPath);
            if (glow == null || dust == null || dirt == null)
                return;
            if (arrow == null)
                Debug.LogWarning($"[{nameof(DemoSkillEffectBuilder)}] No arrow at {ArrowPrefabPath}; Volley's rain will " +
                                 "have streaks and dirt but no arrows in it. Run Build Weapons.");
            float radius = DemoSkillBuilder.AreaRadius("Volley");
            if (radius <= 0f)
                radius = 4f;

            var root = new GameObject(VolleyRainName);
            try
            {
                DemoVolleyRain rain = root.AddComponent<DemoVolleyRain>();
                rain.arrowPrefab = arrow;
                rain.radius = radius;
                rain.streak = RainStreak(root.transform, glow);
                rain.dust = RainDust(root.transform, dust);
                rain.chips = RainChips(root.transform, dirt);
                rain.arrowsPerWave = 12;
                rain.dustEach = 5;
                rain.streakPerMetre = 22f;
                // Every second arrow: six thunks a wave, spread over the wave's half second by the
                // landings themselves. The clips are single impacts (2026-09-25) - audio generators
                // put every hit of a "cluster" on the same instant, so the spacing is left to the rain.
                rain.impactSoundEvery = 2;
                // A touch over life size. At the distance a volley is watched from, a true-size
                // shaft is a hairline and the rain read as dotted lines (live, 2026-09-25).
                rain.arrowScale = 1.3f;

                // Arrows sunk by sinkAt + sinkSeconds (4.2s); a breath more for the last dirt.
                SaveEffect<GameEffect>(root, VolleyRainName, string.Empty, lifeTime: 4.5f, pool: 2,
                                                           stayInPlace: true);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static ParticleSystem RainStreak(Transform parent, Material material)
        {
            ParticleSystem system = Emitter(parent, "Streak", material, 1500);
            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.14f, 0.2f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.12f);
            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = 0f;
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = false;
            Color pale = Color.Lerp(Timber, Color.white, 0.55f);
            Tint(system, Ramp((0f, pale, 0.55f), (1f, Timber, 0f)));
            Shrink(system, AnimationCurve.Linear(0f, 1f, 1f, 0.3f));
            return system;
        }

        private static ParticleSystem RainDust(Transform parent, Material material)
        {
            ParticleSystem system = Emitter(parent, "Dust", material, 300);
            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.1f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.3f, 0.55f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, 2f * Mathf.PI);
            main.gravityModifier = 0.05f;
            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = 0f;
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = false;
            Drag(system, 1.5f, 0.35f);
            Tint(system, Ramp((0f, Dirt, 0f), (0.1f, Dirt, 0.85f), (1f, Dirt, 0f)));
            Shrink(system, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(1f, 1.6f)));
            return system;
        }

        private static ParticleSystem RainChips(Transform parent, Material material)
        {
            ParticleSystem system = Emitter(parent, "Chips", material, 300);
            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.7f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.06f);
            main.gravityModifier = 1.2f;
            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = 0f;
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = false;
            Color dark = Dirt * 0.55f;
            dark.a = 1f;
            Tint(system, Ramp((0f, dark, 1f), (0.8f, dark, 1f), (1f, dark, 0f)));
            return system;
        }

        // ---- what the victim wears ----------------------------------------------

        /// <summary>
        /// Crippling Shot's slow: thorny vines spreading over the ground round the feet, green
        /// tendrils winding up the legs, and a ring drawing in at the ankles every second - the
        /// binding tightening. On for exactly as long as the slow.
        /// </summary>
        private static void BuildCrippled()
        {
            Material glow = EffectMaterial(BuildSprite());
            Material shock = ParticleMaterial(ShockwaveMaterialPath, ShockwaveSprite(), ParticleBlend.Additive, Color.white);
            Material vines = GroundDecalMaterial(VinesMaterialPath, VinesSprite(), Vine, new Color(0.03f, 0.07f, 0.01f));
            // Laid over the scene, not added to it. The first pass was additive, like the spells,
            // and on sunlit grass green-on-green additive came out as a faint pale smudge: a worn
            // effect has to keep its colour against whatever it stands on, day or night.
            Material leaf = ParticleMaterial(DirtMaterialPath, BuildSprite(), ParticleBlend.Alpha, Color.white);
            if (glow == null || shock == null || vines == null || leaf == null)
                return;

            const string name = "FX_Crippled";
            var root = new GameObject(name);
            try
            {
                GroundCircle patch = FrostPatch(root.transform, vines, 0.85f, segments: 32, rings: 4,
                                                    grow: 0.25f, hold: -1f, fade: 0.45f);
                patch.gameObject.name = "Vines";

                // Dark strands winding up the legs. Leaf-green motes were invisible against the grass
                // behind them (29 alive and none to be seen, 2026-09-25); dark on sunlit green is not.
                ParticleSystem tendrils = Emitter(root.transform, "Tendrils", leaf, 160, stretch: true);
                ParticleSystem.MainModule main = tendrils.main;
                main.loop = true;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 0.85f);
                main.startSpeed = 0f;
                main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.08f);
                ParticleSystem.EmissionModule emission = tendrils.emission;
                emission.rateOverTime = 40f;
                ParticleSystem.ShapeModule shape = tendrils.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Circle;
                shape.radius = 0.32f;
                shape.radiusThickness = 0.2f;
                shape.rotation = new Vector3(90f, 0f, 0f);
                ParticleSystem.VelocityOverLifetimeModule velocity = tendrils.velocityOverLifetime;
                velocity.enabled = true;
                velocity.space = ParticleSystemSimulationSpace.Local;
                // All three in one mode, or Unity refuses the lot ("Particle Velocity curves must
                // all be in the same mode") and the tendrils never rise.
                velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
                velocity.y = new ParticleSystem.MinMaxCurve(0.8f, 1.2f);
                velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
                velocity.orbitalY = new ParticleSystem.MinMaxCurve(2.6f);
                velocity.radial = new ParticleSystem.MinMaxCurve(-0.15f);
                Color strand = Vine * 0.8f;
                strand.a = 1f;
                Tint(tendrils, Ramp((0f, strand, 0f), (0.15f, strand, 1f), (0.8f, strand, 0.9f), (1f, strand, 0f)));
                var strandRenderer = tendrils.GetComponent<ParticleSystemRenderer>();
                strandRenderer.velocityScale = 0.09f;
                strandRenderer.lengthScale = 1.6f;

                // The binding, by contrast, is light: a green glow drawing in at the ankles, the one
                // part that says "magic" rather than "undergrowth".
                ParticleSystem binding = Emitter(root.transform, "Binding", shock, 4);
                binding.transform.localPosition = new Vector3(0f, 0.22f, 0f);
                ParticleSystem.MainModule bindingMain = binding.main;
                bindingMain.loop = true;
                bindingMain.startLifetime = 0.8f;
                bindingMain.startSpeed = 0f;
                bindingMain.startSize = 1.5f;
                ParticleSystem.EmissionModule bindingEmission = binding.emission;
                bindingEmission.rateOverTime = 0.9f;
                ParticleSystem.ShapeModule bindingShape = binding.shape;
                bindingShape.enabled = false;
                Tint(binding, Ramp((0f, ArrowCrippling, 0f), (0.35f, ArrowCrippling, 0.9f), (1f, ArrowCrippling, 0f)));
                Shrink(binding, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.42f)));
                binding.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;

                Sounds(root, volume: 0.8f, near: 5f);
                SaveEffect<BuffGameEffect>(root, name, SocketFloor, lifeTime: 0.6f, pool: 6, loop: true);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// The Hunter's Mark: an amber reticle turning slowly over the quarry's head, which lands by
        /// closing in from wide - a lock-on - and pings outward every second after. It is what the
        /// party is meant to see across a fight, so it is the one debuff here that is big.
        /// Lifted by <see cref="OverheadEffectPivot"/>, because a wolf has no Head socket.
        /// </summary>
        private static void BuildHuntersMark()
        {
            Material glow = EffectMaterial(BuildSprite());
            // Solid amber rather than additive light: additive amber on a noon sky came out white,
            // and white is the colour the kit's own markers are not. The glow behind it stays light.
            Material reticle = ParticleMaterial(ReticleAlphaMaterialPath, ReticleSprite(), ParticleBlend.Alpha, Color.white);
            if (glow == null || reticle == null)
                return;

            const string name = "FX_HuntersMark";
            var root = new GameObject(name);
            try
            {
                var pivot = new GameObject("Mark").transform;
                pivot.SetParent(root.transform, false);
                pivot.localPosition = new Vector3(0f, 2f, 0f);
                Color hot = new Color(1f, 0.66f, 0.12f);

                // The glow behind it, so it reads against a bright sky as well as a dark wood.
                ParticleSystem halo = Emitter(pivot, "Halo", glow, 1);
                Forever(halo, 0.95f);
                Tint(halo, Ramp((0f, ArrowMark, 0f), (0.0003f, ArrowMark, 0.3f), (1f, ArrowMark, 0.3f)));

                ParticleSystem mark = Emitter(pivot, "Reticle", reticle, 1);
                Forever(mark, 0.7f);
                Tint(mark, Ramp((0f, hot, 0f), (0.0003f, hot, 1f), (1f, hot, 1f)));
                ParticleSystem.RotationOverLifetimeModule spin = mark.rotationOverLifetime;
                spin.enabled = true;
                spin.z = new ParticleSystem.MinMaxCurve(40f * Mathf.Deg2Rad);

                ParticleSystem lockOn = Emitter(pivot, "LockOn", reticle, 1);
                ParticleSystem.MainModule lockMain = lockOn.main;
                lockMain.loop = false;
                lockMain.duration = 0.3f;
                lockMain.startLifetime = 0.3f;
                lockMain.startSpeed = 0f;
                lockMain.startSize = 1.7f;
                ParticleSystem.EmissionModule lockEmission = lockOn.emission;
                lockEmission.rateOverTime = 0f;
                lockEmission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
                ParticleSystem.ShapeModule lockOnShape0 = lockOn.shape;
                lockOnShape0.enabled = false;
                // It ends exactly the reticle's size: 1.7 x 0.41 = 0.7.
                Shrink(lockOn, new AnimationCurve(new Keyframe(0f, 1f, 0f, -3f), new Keyframe(1f, 0.41f)));
                Tint(lockOn, Ramp((0f, hot, 0f), (0.3f, hot, 0.9f), (1f, hot, 0.2f)));

                ParticleSystem ping = Emitter(pivot, "Ping", reticle, 3);
                ParticleSystem.MainModule pingMain = ping.main;
                pingMain.loop = true;
                pingMain.startLifetime = 0.8f;
                pingMain.startSpeed = 0f;
                pingMain.startSize = 0.7f;
                pingMain.startDelay = 0.35f;
                ParticleSystem.EmissionModule pingEmission = ping.emission;
                pingEmission.rateOverTime = 0.9f;
                ParticleSystem.ShapeModule pingShape0 = ping.shape;
                pingShape0.enabled = false;
                Shrink(ping, AnimationCurve.Linear(0f, 1f, 1f, 1.9f));
                Tint(ping, Ramp((0f, ArrowMark, 0.55f), (1f, ArrowMark, 0f)));

                OverheadEffectPivot overhead = root.AddComponent<OverheadEffectPivot>();
                overhead.pivot = pivot;
                // Over the kit's target arrow, which stands on the head of whatever is selected -
                // and a marked thing is nearly always the selected thing. At 0.4, and still at 1.15,
                // the two sat on one another and read as one muddle (live, 2026-09-25).
                overhead.clearance = 1.75f;

                Sounds(root, volume: 0.8f, near: 6f);
                SaveEffect<BuffGameEffect>(root, name, SocketFloor, lifeTime: 0.35f, pool: 4, loop: true);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// Bleeding: dark drops falling from the wound for as long as it runs. Alpha-blended, not
        /// additive - blood has to be darker than what it runs down, and additive can only brighten.
        /// </summary>
        private static void BuildBleeding()
        {
            Material blood = ParticleMaterial(BloodMaterialPath, BuildSprite(), ParticleBlend.Alpha, Color.white);
            if (blood == null)
                return;

            const string name = "FX_Bleeding";
            var root = new GameObject(name);
            try
            {
                ParticleSystem drips = Emitter(root.transform, "Drips", blood, 60, stretch: true);
                ParticleSystem.MainModule main = drips.main;
                main.loop = true;
                // Where they fell from, not riding the body down with them.
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.8f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.45f);
                // Big enough to see against a wolf's dark coat, which is what a ranger marks most.
                main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.08f);
                main.gravityModifier = 1f;
                ParticleSystem.EmissionModule emission = drips.emission;
                emission.rateOverTime = 10f;
                ParticleSystem.ShapeModule shape = drips.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.14f;
                Tint(drips, Ramp((0f, Blood, 0.95f), (0.8f, Blood, 0.9f), (1f, Blood, 0f)));
                var renderer = drips.GetComponent<ParticleSystemRenderer>();
                renderer.velocityScale = 0.04f;
                renderer.lengthScale = 1.4f;

                SaveEffect<BuffGameEffect>(root, name, SocketBody, lifeTime: 0.9f, pool: 6, loop: true);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // ---- pieces --------------------------------------------------------------

        /// <summary>A one-shot flash at the socket: a hot core in a wide glow, the release's light.</summary>
        private static ParticleSystem ReleaseFlash(Transform parent, Material material, Color colour, float size)
        {
            Core(parent, material, new Recipe { Colour = colour, LifeTime = 0.4f, Core = size });
            return parent.Find("Core").GetComponent<ParticleSystem>();
        }

        private static Transform Pivot(Transform parent)
        {
            var pivot = new GameObject("Aim").transform;
            pivot.SetParent(parent, false);
            return pivot;
        }

        /// <summary>
        /// A cone of streaks along the pivot's +Z, emitted by <see cref="GameEffectAim"/> only - it has
        /// no emission of its own - and left in the world where it was thrown.
        /// </summary>
        private static ParticleSystem AimedSpray(Transform pivot, string name, Material material, Color colour, int most,
                                                 Vector2 speed, Vector2 life, Vector2 size, float cone, float velocityScale)
        {
            ParticleSystem system = Emitter(pivot, name, material, most, stretch: true);
            ParticleSystem.MainModule main = system.main;
            main.loop = false;
            main.duration = 0.2f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
            main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = 0f;
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = cone;
            shape.radius = 0.03f;
            Color hot = Color.Lerp(colour, Color.white, 0.5f);
            Tint(system, Ramp((0f, hot, 1f), (0.5f, colour, 0.7f), (1f, colour, 0f)));
            Shrink(system, AnimationCurve.Linear(0f, 1f, 1f, 0.4f));
            system.GetComponent<ParticleSystemRenderer>().velocityScale = velocityScale;
            return system;
        }

        /// <summary>A ring snapping out round the line of the shot, square to it, a hand ahead of the bow.</summary>
        private static ParticleSystem AimedRing(Transform pivot, Material material, Color colour, float size, float life)
        {
            ParticleSystem ring = Emitter(pivot, "Ring", material, 2);
            ring.transform.localPosition = new Vector3(0f, 0f, 0.3f);
            ParticleSystem.MainModule main = ring.main;
            main.loop = false;
            main.duration = 0.2f;
            main.startLifetime = life;
            main.startSpeed = 0f;
            main.startSize = size;
            ParticleSystem.EmissionModule emission = ring.emission;
            emission.rateOverTime = 0f;
            ParticleSystem.ShapeModule ringShape0 = ring.shape;
            ringShape0.enabled = false;
            Color hot = Color.Lerp(colour, Color.white, 0.5f);
            Tint(ring, Ramp((0f, hot, 0.95f), (1f, colour, 0f)));
            Shrink(ring, new AnimationCurve(new Keyframe(0f, 0.15f, 0f, 3f), new Keyframe(1f, 1f)));
            // Local: the quad lies in the pivot's XY plane, facing along the shot.
            ring.GetComponent<ParticleSystemRenderer>().alignment = ParticleSystemRenderSpace.Local;
            return ring;
        }

        /// <summary>One particle that stays until the effect is put away: a mark, not a pulse.</summary>
        private static void Forever(ParticleSystem system, float size)
        {
            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.startLifetime = 3600f;
            main.startSpeed = 0f;
            main.startSize = size;
            main.maxParticles = 1;
            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
            ParticleSystem.ShapeModule systemShape0 = system.shape;
            systemShape0.enabled = false;
        }

        private static void FlashLightOn(ParticleSystem system, Light light, float intensity)
        {
            if (light == null)
                return;
            ParticleSystem.LightsModule lights = system.lights;
            lights.enabled = true;
            lights.light = light;
            lights.ratio = 1f;
            lights.maxLights = 1;
            lights.useParticleColor = true;
            lights.alphaAffectsIntensity = true;
            lights.sizeAffectsRange = false;
            lights.intensityMultiplier = intensity;
            lights.rangeMultiplier = 1f;
        }

        private static void Sounds(GameObject root, float volume, float near)
        {
            GameEffectSounds sounds = root.AddComponent<GameEffectSounds>();
            sounds.volume = volume;
            sounds.near = near;
            // Filled by DemoAudioWiring.WireRangerSounds, which runs at the end of Build Skill Effects.
            sounds.clips = new AudioClip[0];
        }

        private static T SaveEffect<T>(GameObject root, string name, string socket, float lifeTime, int pool,
                                       bool loop = false, bool stayInPlace = false) where T : GameEffect
        {
            string path = EffectPath(name);
            var effect = root.AddComponent<T>();
            effect.effectSocket = socket;
            effect.stayInPlace = stayInPlace;
            // For a worn effect, "never ends on its own": the debuff ending ends it, and lifeTime is
            // then the fade. Everything else ends on its lifetime; see Recipe.Loop.
            effect.isLoop = loop;
            effect.lifeTime = lifeTime;
            effect.PoolSize = pool;
            // Whatever sound the effect already carries, read before the save replaces it - so a
            // clip wired on by hand survives a rebuild, as the main file's effects do. It used to
            // be emptied here and refilled by WireRangerSounds, which knows only its own families.
            effect.randomSoundEffects = ExistingSounds(path);
            PrefabUtility.SaveAsPrefabAsset(root, path);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadAssetAtPath<T>(path);
        }

        /// <summary>
        /// A lit, transparent material for something lying on the ground (the vines), made the way the
        /// frost's is and for the same reason: unlit, it glows in the dark.
        /// </summary>
        private static Material GroundDecalMaterial(string path, Texture2D texture, Color colour, Color emission)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                return null;
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                DemoItemBuilder.EnsureFolder(MaterialDir);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            material.SetTexture("_BaseMap", texture);
            colour.a = 1f;
            material.SetColor("_BaseColor", colour);
            material.SetFloat("_Smoothness", 0.2f);
            material.SetFloat("_Metallic", 0f);
            material.SetColor("_EmissionColor", emission);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_BlendModePreserveSpecular", 0f);
            material.SetFloat("_Cull", (float)CullMode.Off);
            material.SetFloat("_AlphaClip", 0f);
            material.SetFloat("_QueueOffset", -10f);
            BaseShaderGUI.SetMaterialKeywords(material);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// Four thorny vines wound round a ring, wavering in and out across it. Drawn only into a gap,
        /// so a hand-drawn one at the same path is kept.
        /// </summary>
        private static Texture2D VinesSprite()
        {
            return DrawSprite(VinesTexturePath, 256, (x, y, r) =>
            {
                float theta = Mathf.Atan2(y - 0.5f, x - 0.5f);
                float a = 0f;
                for (int k = 0; k < 4; ++k)
                {
                    float phase = k * 1.7f;
                    float centre = 0.6f + 0.17f * Mathf.Sin(3f * theta + phase) + 0.05f * Mathf.Sin(7f * theta + 2.3f * phase);
                    float width = 0.048f + 0.014f * Mathf.Sin(5f * theta + phase);
                    float off = r - centre;
                    a = Mathf.Max(a, Mathf.Clamp01(1.4f - Mathf.Abs(off) / width));

                    // Thorns: a sawtooth off one side of each vine, eleven a turn.
                    float turns = (theta / (2f * Mathf.PI) + 0.5f) * 11f + k * 0.37f;
                    float along = turns - Mathf.Floor(turns);
                    const float window = 0.14f;
                    if (along < window)
                    {
                        float side = (k % 2 == 0 ? 1f : -1f) * off - width * 0.6f;
                        float reach = 0.1f * (1f - along / window);
                        if (side > -0.01f && side < reach)
                            a = Mathf.Max(a, Mathf.Clamp01((reach - side) / 0.012f));
                    }
                }
                return a * Mathf.Clamp01((1f - r) * 20f);
            });
        }

        /// <summary>
        /// The hunter's reticle: a ring broken at the four points of the compass, a tick at each
        /// break pointing in, and a small diamond in the middle. Drawn only into a gap.
        /// </summary>
        private static Texture2D ReticleSprite()
        {
            return DrawSprite(ReticleTexturePath, 256, (x, y, r) =>
            {
                float theta = Mathf.Atan2(y - 0.5f, x - 0.5f);
                // Radians to the nearest of the four breaks.
                float toBreak = Mathf.Abs(Mathf.Repeat(theta + Mathf.PI * 0.25f, Mathf.PI * 0.5f) - Mathf.PI * 0.25f);

                float ring = Mathf.Clamp01(1f - Mathf.Abs(r - 0.72f) / 0.045f) * Mathf.Clamp01((toBreak - 0.2f) / 0.04f);

                float tick = 0f;
                if (r > 0.52f && r < 0.96f)
                {
                    float halfWidth = 0.1f * (r - 0.52f) / 0.44f;
                    tick = Mathf.Clamp01((halfWidth - toBreak * r) / 0.012f);
                }

                float diamond = Mathf.Clamp01((0.1f - (Mathf.Abs(x - 0.5f) + Mathf.Abs(y - 0.5f)) * 2f) / 0.02f);
                return Mathf.Max(ring, Mathf.Max(tick, diamond));
            });
        }
    }
}
