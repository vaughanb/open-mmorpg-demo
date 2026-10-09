using UnityEditor;
using UnityEngine;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// The debris of harvesting (2026-10-03): a few chips and a puff of dust where a tool lands on a
    /// tree, a boulder, an iron vein or a mushroom, and a bigger burst where the node comes down.
    ///
    /// A hit is small and short, for the reason every hit effect here is: it fires on every blow. The
    /// finisher is where the budget goes, because a node simply vanishes the instant it is felled
    /// (destroyDelay 0) and something has to cover that - chips thrown from the trunk and a cloud of
    /// dust, and from the crown the leaves or needles that were on it.
    ///
    /// Chips are solid, **lit** cubes (a meteor's clods are unlit and read as flat at dusk), dust is
    /// the lit alpha billow the Charge uses, and the only additive thing is the sparks off stone and
    /// ore. They are fetched from the kit's pool by <see cref="HarvestImpactEffects"/>, which also plays
    /// the sound, and are placed on a node prefab by <see cref="WireHarvestEffects"/>.
    ///
    /// Each effect points +Z toward whoever swung (yaw only), so a hit's cone throws its debris back at
    /// the player and a felling's dome is symmetric. Particles simulate in world space: the pooled
    /// effect object stays where it was put while its debris flies.
    /// </summary>
    public static partial class DemoSkillEffectBuilder
    {
        private const string HarvestEffectDir = "Assets/OpenMMORPG/Demo/Prefabs/Effects/Harvest";
        private const string HarvestNodeDir = "Assets/OpenMMORPG/Demo/Prefabs/GamePlay/Harvestables";
        private const string ChipMaterialPath = MaterialDir + "/MI_HarvestChips.mat";

        private enum FoliageKind { None, Leaf, Needle }

        /// <summary>What one harvest effect is made of. Each non-zero count adds one emitter.</summary>
        private struct HarvestFx
        {
            public string Name;
            /// <summary>A node coming down rather than being struck: a dome from where it stood, and it bounces.</summary>
            public bool Felling;
            public float Life;

            public int Chips;
            public Color ChipA, ChipB;
            public float ChipMin, ChipMax;
            /// <summary>Long thin splinters (wood) rather than cubes (stone).</summary>
            public bool Splinters;
            public float ChipSpeedMin, ChipSpeedMax;

            public int Dust;
            public Color DustColour;
            public float DustMin, DustMax, DustAlpha;

            public int Sparks;
            public Color SparkColour;

            public int Motes;
            public Color MoteColour;

            public FoliageKind Foliage;
            public int Leaves;
            public Color LeafA, LeafB;
        }

        // Wood. Fresh-cut is pale, bark is dark; a dead tree is silver-grey all through.
        private static readonly Color WoodPale = new Color(0.78f, 0.60f, 0.38f);
        private static readonly Color WoodBark = new Color(0.30f, 0.20f, 0.12f);
        private static readonly Color DeadPale = new Color(0.66f, 0.62f, 0.56f);
        private static readonly Color DeadDark = new Color(0.36f, 0.33f, 0.30f);
        private static readonly Color WoodDust = new Color(0.82f, 0.72f, 0.54f);
        private static readonly Color DeadDust = new Color(0.76f, 0.73f, 0.68f);
        // Stone.
        private static readonly Color StonePale = new Color(0.62f, 0.61f, 0.58f);
        private static readonly Color StoneDark = new Color(0.30f, 0.30f, 0.30f);
        private static readonly Color StoneDust = new Color(0.80f, 0.78f, 0.74f);
        // Ore: the vein's dark red-brown rock, iron grey, rust.
        private static readonly Color OreRock = new Color(0.34f, 0.24f, 0.22f);
        private static readonly Color OreIron = new Color(0.52f, 0.52f, 0.56f);
        private static readonly Color RustDust = new Color(0.76f, 0.50f, 0.36f);
        private static readonly Color OreSpark = new Color(1.00f, 0.66f, 0.26f);
        private static readonly Color StoneSpark = new Color(1.00f, 0.92f, 0.70f);
        // Leaves, needles and spores.
        private static readonly Color LeafGreen = new Color(0.30f, 0.52f, 0.16f);
        private static readonly Color LeafLight = new Color(0.50f, 0.64f, 0.20f);
        private static readonly Color NeedleDark = new Color(0.10f, 0.26f, 0.12f);
        private static readonly Color NeedleLight = new Color(0.20f, 0.38f, 0.16f);
        private static readonly Color SporeDust = new Color(0.96f, 0.93f, 0.82f);
        private static readonly Color SporeMote = new Color(0.94f, 0.88f, 0.66f);

        public const string ChopChipsName = "FX_ChopChips";
        public const string ChopChipsDeadName = "FX_ChopChipsDead";
        public const string RockChipsName = "FX_RockChips";
        public const string OreSparksName = "FX_OreSparks";
        public const string SporePuffName = "FX_SporePuff";
        public const string TreeFellLeafName = "FX_TreeFellLeaf";
        public const string TreeFellNeedleName = "FX_TreeFellNeedle";
        public const string TreeFellDeadName = "FX_TreeFellDead";
        public const string RockBreakName = "FX_RockBreak";
        public const string VeinBreakName = "FX_VeinBreak";
        public const string SporeBurstName = "FX_SporeBurst";

        private static readonly HarvestFx[] HarvestEffects =
        {
            // ---- struck: small, quick, thrown back at the player -------------------------
            new HarvestFx
            {
                Name = ChopChipsName, Life = 1.2f,
                Chips = 18, ChipA = WoodPale, ChipB = WoodBark, ChipMin = 0.6f, ChipMax = 1.0f, Splinters = true,
                ChipSpeedMin = 3.2f, ChipSpeedMax = 7f,
                Dust = 3, DustColour = WoodDust, DustMin = 1.2f, DustMax = 1.7f, DustAlpha = 0.7f,
            },
            new HarvestFx
            {
                Name = ChopChipsDeadName, Life = 1.2f,
                Chips = 16, ChipA = DeadPale, ChipB = DeadDark, ChipMin = 0.6f, ChipMax = 1.0f, Splinters = true,
                ChipSpeedMin = 3.2f, ChipSpeedMax = 7f,
                Dust = 3, DustColour = DeadDust, DustMin = 1.2f, DustMax = 1.8f, DustAlpha = 0.7f,
            },
            new HarvestFx
            {
                Name = RockChipsName, Life = 1.2f,
                Chips = 16, ChipA = StonePale, ChipB = StoneDark, ChipMin = 0.07f, ChipMax = 0.16f,
                ChipSpeedMin = 4f, ChipSpeedMax = 8.5f,
                Dust = 3, DustColour = StoneDust, DustMin = 1.2f, DustMax = 1.8f, DustAlpha = 0.7f,
                Sparks = 12, SparkColour = StoneSpark,
            },
            new HarvestFx
            {
                Name = OreSparksName, Life = 1.2f,
                Chips = 14, ChipA = OreRock, ChipB = OreIron, ChipMin = 0.06f, ChipMax = 0.14f,
                ChipSpeedMin = 4f, ChipSpeedMax = 8.5f,
                Dust = 3, DustColour = RustDust, DustMin = 1.1f, DustMax = 1.6f, DustAlpha = 0.7f,
                Sparks = 34, SparkColour = OreSpark,
            },
            new HarvestFx
            {
                Name = SporePuffName, Life = 1.6f,
                Dust = 4, DustColour = SporeDust, DustMin = 1.0f, DustMax = 1.5f, DustAlpha = 0.6f,
                Motes = 28, MoteColour = SporeMote,
            },

            // ---- felled: thrown from where it stood, all round ---------------------------
            new HarvestFx
            {
                Name = TreeFellLeafName, Felling = true, Life = 3.4f,
                Chips = 40, ChipA = WoodPale, ChipB = WoodBark, ChipMin = 0.8f, ChipMax = 1.5f, Splinters = true,
                ChipSpeedMin = 3.6f, ChipSpeedMax = 8.5f,
                Dust = 10, DustColour = WoodDust, DustMin = 2.2f, DustMax = 3.4f, DustAlpha = 0.7f,
                Foliage = FoliageKind.Leaf, Leaves = 80, LeafA = LeafGreen, LeafB = LeafLight,
            },
            new HarvestFx
            {
                Name = TreeFellNeedleName, Felling = true, Life = 3.4f,
                Chips = 40, ChipA = WoodPale, ChipB = WoodBark, ChipMin = 0.8f, ChipMax = 1.5f, Splinters = true,
                ChipSpeedMin = 3.6f, ChipSpeedMax = 8.5f,
                Dust = 10, DustColour = WoodDust, DustMin = 2.2f, DustMax = 3.4f, DustAlpha = 0.7f,
                Foliage = FoliageKind.Needle, Leaves = 130, LeafA = NeedleDark, LeafB = NeedleLight,
            },
            new HarvestFx
            {
                Name = TreeFellDeadName, Felling = true, Life = 3.0f,
                Chips = 40, ChipA = DeadPale, ChipB = DeadDark, ChipMin = 0.8f, ChipMax = 1.5f, Splinters = true,
                ChipSpeedMin = 3.6f, ChipSpeedMax = 8.5f,
                Dust = 10, DustColour = DeadDust, DustMin = 2.2f, DustMax = 3.4f, DustAlpha = 0.7f,
            },
            new HarvestFx
            {
                Name = RockBreakName, Felling = true, Life = 3.2f,
                Chips = 44, ChipA = StonePale, ChipB = StoneDark, ChipMin = 0.10f, ChipMax = 0.28f,
                ChipSpeedMin = 3.6f, ChipSpeedMax = 9.5f,
                Dust = 12, DustColour = StoneDust, DustMin = 2.4f, DustMax = 3.6f, DustAlpha = 0.7f,
                Sparks = 14, SparkColour = StoneSpark,
            },
            new HarvestFx
            {
                Name = VeinBreakName, Felling = true, Life = 3.2f,
                Chips = 40, ChipA = OreRock, ChipB = OreIron, ChipMin = 0.10f, ChipMax = 0.26f,
                ChipSpeedMin = 3.6f, ChipSpeedMax = 9.5f,
                Dust = 10, DustColour = RustDust, DustMin = 2.2f, DustMax = 3.4f, DustAlpha = 0.7f,
                Sparks = 60, SparkColour = OreSpark,
            },
            new HarvestFx
            {
                Name = SporeBurstName, Felling = true, Life = 2.6f,
                Dust = 8, DustColour = SporeDust, DustMin = 1.6f, DustMax = 2.4f, DustAlpha = 0.6f,
                Motes = 60, MoteColour = SporeMote,
            },
        };

        [MenuItem("Open MMORPG/Demo/Build Harvest Effects")]
        public static void BuildHarvestEffects()
        {
            DemoItemBuilder.EnsureFolder(HarvestEffectDir);
            Material chips = ParticleMaterial(ChipMaterialPath, null, ParticleBlend.Opaque, Color.white, lit: true);
            Material dust = ParticleMaterial(DustMaterialPath, FirePuffSprite(), ParticleBlend.Alpha, Color.white, lit: true);
            Material glow = EffectMaterial(BuildSprite());
            if (chips == null || dust == null || glow == null)
                return;

            int built = 0;
            foreach (HarvestFx fx in HarvestEffects)
            {
                BuildHarvestEffect(fx, chips, dust, glow);
                ++built;
            }
            AssetDatabase.SaveAssets();
            int wired = WireHarvestEffects();
            Debug.Log($"[{nameof(DemoSkillEffectBuilder)}] {built} harvest effects built in {HarvestEffectDir} and " +
                      $"wired onto {wired} harvest node(s).");
        }

        private static void BuildHarvestEffect(HarvestFx fx, Material chips, Material dust, Material glow)
        {
            string path = $"{HarvestEffectDir}/{fx.Name}.prefab";
            var root = new GameObject(fx.Name);
            try
            {
                if (fx.Chips > 0)
                    HarvestChips(root.transform, chips, fx);
                if (fx.Dust > 0)
                    HarvestDust(root.transform, dust, fx);
                if (fx.Sparks > 0)
                    HarvestSparks(root.transform, glow, fx);
                if (fx.Motes > 0)
                    HarvestMotes(root.transform, glow, fx);
                if (fx.Foliage != FoliageKind.None)
                    HarvestFoliage(root.transform, chips, fx);

                var effect = root.AddComponent<GameEffect>();
                effect.effectSocket = string.Empty;
                effect.stayInPlace = true;
                effect.isLoop = false;
                effect.lifeTime = fx.Life;
                // A tool lands every second or so and several players can be at work on one wood.
                effect.PoolSize = fx.Felling ? 4 : 8;

                PrefabUtility.SaveAsPrefabAsset(root, path);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>The shape every emitter in a harvest effect shares: a cone back at the player for a hit, a dome for a felling.</summary>
        private static void HarvestShape(ParticleSystem system, HarvestFx fx, float coneAngle, float radius)
        {
            if (fx.Felling)
            {
                Dome(system, radius);
                return;
            }
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = coneAngle;
            shape.radius = radius;
            // Toward the player and a little up; the cone's own axis is +Z, the effect's facing.
            shape.rotation = new Vector3(-14f, 0f, 0f);
        }

        /// <summary>
        /// Solid chips, thrown and falling. Wood comes off as splinters - long and thin - and stone as
        /// squat cubes; a felling's bounce off the ground (terrain only, so they never ricochet off the
        /// player or the next tree), a blow's do not, being gone before they land.
        /// </summary>
        private static void HarvestChips(Transform parent, Material material, HarvestFx fx)
        {
            ParticleSystem chips = Emitter(parent, "Chips", material, fx.Chips + 8);
            chips.transform.localPosition = new Vector3(0f, fx.Felling ? 0.7f : 0f, 0f);
            ParticleSystem.MainModule main = chips.main;
            main.loop = false;
            main.duration = 0.2f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = fx.Felling ? new ParticleSystem.MinMaxCurve(1.0f, 1.8f) : new ParticleSystem.MinMaxCurve(0.45f, 0.75f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(fx.ChipSpeedMin, fx.ChipSpeedMax);
            main.gravityModifier = 2.4f;
            if (fx.Splinters)
            {
                // The mesh is a unit cube: x and y the thickness, z the length.
                main.startSize3D = true;
                main.startSizeX = new ParticleSystem.MinMaxCurve(0.03f, 0.055f);
                main.startSizeY = new ParticleSystem.MinMaxCurve(0.02f, 0.04f);
                main.startSizeZ = new ParticleSystem.MinMaxCurve(0.18f * fx.ChipMin, 0.30f * fx.ChipMax);
            }
            else
            {
                main.startSize = new ParticleSystem.MinMaxCurve(fx.ChipMin, fx.ChipMax);
            }
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(fx.ChipA, fx.ChipB);
            ParticleSystem.EmissionModule emission = chips.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)fx.Chips) });
            HarvestShape(chips, fx, 36f, fx.Felling ? 0.35f : 0.06f);

            ParticleSystem.RotationOverLifetimeModule spin = chips.rotationOverLifetime;
            spin.enabled = true;
            spin.separateAxes = true;
            spin.x = new ParticleSystem.MinMaxCurve(-9f, 9f);
            spin.y = new ParticleSystem.MinMaxCurve(-9f, 9f);
            spin.z = new ParticleSystem.MinMaxCurve(-9f, 9f);
            // Solid, so they cannot fade: they shrink to nothing at the very end instead.
            Shrink(chips, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.8f, 1f), new Keyframe(1f, 0f)));

            if (fx.Felling)
            {
                ParticleSystem.CollisionModule collision = chips.collision;
                collision.enabled = true;
                collision.type = ParticleSystemCollisionType.World;
                collision.mode = ParticleSystemCollisionMode.Collision3D;
                collision.collidesWith = 1; // Default: the terrain and the scenery, not entities
                collision.dampen = 0.4f;
                collision.bounce = 0.3f;
                collision.lifetimeLoss = 0f;
                collision.quality = ParticleSystemCollisionQuality.Medium;
                collision.radiusScale = 0.5f;
            }

            var renderer = chips.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            renderer.alignment = ParticleSystemRenderSpace.World;
        }

        /// <summary>
        /// The cloud: soft billows that bloom and thin. Small and pale for a blow, a rolling ring along
        /// the ground for a felling. Lit and alpha-blended, so it is dust by day and gloom at night.
        /// </summary>
        private static void HarvestDust(Transform parent, Material material, HarvestFx fx)
        {
            ParticleSystem puff = Emitter(parent, "Dust", material, fx.Dust + 4);
            puff.transform.localPosition = new Vector3(0f, fx.Felling ? 0.25f : 0f, 0f);
            ParticleSystem.MainModule main = puff.main;
            main.loop = false;
            main.duration = 0.2f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = fx.Felling ? new ParticleSystem.MinMaxCurve(1.1f, 1.7f) : new ParticleSystem.MinMaxCurve(0.45f, 0.8f);
            main.startSpeed = fx.Felling ? new ParticleSystem.MinMaxCurve(1.4f, 3.2f) : new ParticleSystem.MinMaxCurve(0.5f, 1.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(fx.DustMin, fx.DustMax);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = -0.04f;
            ParticleSystem.EmissionModule emission = puff.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)fx.Dust) });
            HarvestShape(puff, fx, 32f, fx.Felling ? 0.5f : 0.08f);
            Drag(puff, 1f, 0.12f);
            Spin(puff, 0.5f);
            Tint(puff, Ramp((0f, fx.DustColour, 0f), (0.12f, fx.DustColour, fx.DustAlpha),
                            (0.6f, fx.DustColour, fx.DustAlpha * 0.5f), (1f, fx.DustColour, 0f)));
            Shrink(puff, new AnimationCurve(new Keyframe(0f, 0.55f), new Keyframe(1f, 1.5f)));
            SettleDust(puff);
        }

        /// <summary>Sparks struck off stone and ore, in short bright arcs. Additive, so they read at night too.</summary>
        private static void HarvestSparks(Transform parent, Material material, HarvestFx fx)
        {
            ParticleSystem sparks = Emitter(parent, "Sparks", material, fx.Sparks + 6, stretch: true);
            sparks.transform.localPosition = new Vector3(0f, fx.Felling ? 0.6f : 0f, 0f);
            ParticleSystem.MainModule main = sparks.main;
            main.loop = false;
            main.duration = 0.15f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.42f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.14f);
            main.gravityModifier = 2f;
            ParticleSystem.EmissionModule emission = sparks.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)fx.Sparks) });
            HarvestShape(sparks, fx, 55f, fx.Felling ? 0.3f : 0.05f);
            Tint(sparks, Ramp((0f, Color.white, 1f), (0.3f, fx.SparkColour, 1f), (1f, fx.SparkColour, 0f)));
        }

        /// <summary>A mushroom's spores: soft motes that hang and drift up, not thrown.</summary>
        private static void HarvestMotes(Transform parent, Material material, HarvestFx fx)
        {
            ParticleSystem motes = Emitter(parent, "Motes", material, fx.Motes + 6);
            motes.transform.localPosition = new Vector3(0f, fx.Felling ? 0.3f : 0.1f, 0f);
            ParticleSystem.MainModule main = motes.main;
            main.loop = false;
            main.duration = 0.2f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.1f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.14f);
            main.gravityModifier = -0.12f;
            ParticleSystem.EmissionModule emission = motes.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)fx.Motes) });
            HarvestShape(motes, fx, 50f, fx.Felling ? 0.3f : 0.06f);
            Drag(motes, 0.5f, 0.2f);
            ParticleSystem.NoiseModule noise = motes.noise;
            noise.enabled = true;
            noise.strength = 0.25f;
            noise.frequency = 0.8f;
            Tint(motes, Ramp((0f, fx.MoteColour, 0f), (0.2f, fx.MoteColour, 0.9f), (1f, fx.MoteColour, 0f)));
        }

        /// <summary>
        /// What was on the tree, coming down. A child called Foliage so that <see cref="HarvestImpactEffects"/>
        /// can lift it to the crown (it cannot know a tree's height from here): leaves are small flat
        /// quads, fluttering as they fall; needles are thin sticks that drop straighter.
        /// </summary>
        private static void HarvestFoliage(Transform parent, Material material, HarvestFx fx)
        {
            bool needles = fx.Foliage == FoliageKind.Needle;
            ParticleSystem leaves = Emitter(parent, "Foliage", material, fx.Leaves + 10);
            leaves.transform.localPosition = new Vector3(0f, 3f, 0f);
            ParticleSystem.MainModule main = leaves.main;
            main.loop = false;
            main.duration = 0.35f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 2.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, needles ? 1.6f : 2.2f);
            main.gravityModifier = needles ? 0.45f : 0.22f;
            if (needles)
            {
                main.startSize3D = true;
                main.startSizeX = new ParticleSystem.MinMaxCurve(0.018f, 0.03f);
                main.startSizeY = new ParticleSystem.MinMaxCurve(0.018f, 0.03f);
                main.startSizeZ = new ParticleSystem.MinMaxCurve(0.14f, 0.26f);
            }
            else
            {
                main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.22f);
            }
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(fx.LeafA, fx.LeafB);
            ParticleSystem.EmissionModule emission = leaves.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)fx.Leaves) });
            ParticleSystem.ShapeModule shape = leaves.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 1.1f;
            ParticleSystem.RotationOverLifetimeModule spin = leaves.rotationOverLifetime;
            spin.enabled = true;
            spin.separateAxes = true;
            spin.x = new ParticleSystem.MinMaxCurve(-4f, 4f);
            spin.y = new ParticleSystem.MinMaxCurve(-4f, 4f);
            spin.z = new ParticleSystem.MinMaxCurve(-4f, 4f);
            ParticleSystem.NoiseModule noise = leaves.noise;
            noise.enabled = true;
            noise.strength = needles ? 0.25f : 0.7f;
            noise.frequency = 0.9f;
            // Gone by shrinking away, as every solid particle here.
            Shrink(leaves, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.85f, 1f), new Keyframe(1f, 0f)));
            var renderer = leaves.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = Resources.GetBuiltinResource<Mesh>(needles ? "Cube.fbx" : "Quad.fbx");
            renderer.alignment = ParticleSystemRenderSpace.World;
        }

        // ---- wiring onto the nodes -----------------------------------------------------

        /// <summary>Which hit effect, felling effect and crown height a node gets, read off its name as the builder named it.</summary>
        private static bool HarvestEffectsFor(string nodeName, out string hit, out string fell, out float crown)
        {
            hit = fell = null;
            crown = 0f;
            if (nodeName.Contains("IronVein"))
            {
                hit = OreSparksName;
                fell = VeinBreakName;
            }
            else if (nodeName.Contains("Rock"))
            {
                hit = RockChipsName;
                fell = RockBreakName;
            }
            else if (nodeName.Contains("Pine"))
            {
                hit = ChopChipsName;
                fell = TreeFellNeedleName;
                crown = 0.78f;
            }
            else if (nodeName.Contains("DeadTree"))
            {
                hit = ChopChipsDeadName;
                fell = TreeFellDeadName;
            }
            else if (nodeName.Contains("Tree"))
            {
                hit = ChopChipsName;
                fell = TreeFellLeafName;
                crown = 0.78f;
            }
            else if (nodeName.Contains("Mushroom"))
            {
                hit = SporePuffName;
                fell = SporeBurstName;
            }
            return hit != null;
        }

        /// <summary>
        /// The effects onto every node prefab. Adds the <see cref="HarvestImpactEffects"/> component where
        /// the sound wiring has not (the mushrooms have no clips) and fills only its effect slots, so
        /// Wire Audio and this can run in either order.
        /// </summary>
        public static int WireHarvestEffects()
        {
            int nodes = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { HarvestNodeDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    if (root.GetComponent<HarvestableEntity>() != null && WireHarvestEffectsOn(root))
                    {
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                        ++nodes;
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
            return nodes;
        }

        /// <summary>One node's effects. DemoHarvestBuilder calls this as it builds a node, so a rebuilt node comes out wired.</summary>
        internal static bool WireHarvestEffectsOn(GameObject root)
        {
            if (!HarvestEffectsFor(root.name, out string hit, out string fell, out float crown))
                return false;
            var hitEffect = AssetDatabase.LoadAssetAtPath<GameObject>($"{HarvestEffectDir}/{hit}.prefab")?.GetComponent<GameEffect>();
            var fellEffect = AssetDatabase.LoadAssetAtPath<GameObject>($"{HarvestEffectDir}/{fell}.prefab")?.GetComponent<GameEffect>();
            if (hitEffect == null && fellEffect == null)
                return false;
            var impact = root.GetComponent<HarvestImpactEffects>();
            if (impact == null)
                impact = root.AddComponent<HarvestImpactEffects>();
            var serialized = new SerializedObject(impact);
            serialized.FindProperty("hitEffect").objectReferenceValue = hitEffect;
            serialized.FindProperty("fellEffect").objectReferenceValue = fellEffect;
            serialized.FindProperty("crownShare").floatValue = crown;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(impact);
            return true;
        }
    }
}
