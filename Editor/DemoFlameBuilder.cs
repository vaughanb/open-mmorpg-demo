using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// Builds the fire: the flame prefabs the torches and campfires burn, the materials
    /// they draw with and the one texture behind them.
    ///
    /// No art pack in the demo ships a fire, and the CC0 rule (see the demo credits)
    /// rules out borrowing one, so the flame is made here from nothing: a soft round
    /// sprite drawn in code, stretched along its rise and coloured through its life
    /// from white-yellow to a dark ember red. Added over each other a few dozen at a
    /// time, that is a fire; drawn one at a time it is a blob, which is why the emission
    /// rates are what they are.
    ///
    /// Two sizes come out of the one recipe, a torch head and a campfire, differing only
    /// in the numbers. Both carry a <see cref="DemoTorch"/> that keeps their hours.
    /// </summary>
    public static class DemoFlameBuilder
    {
        public const string TorchFlamePath = "Assets/OpenMMORPG/Demo/Prefabs/Effects/TorchFlame.prefab";
        public const string CampfireFlamePath = "Assets/OpenMMORPG/Demo/Prefabs/Effects/CampfireFlame.prefab";

        private const string TexturePath = "Assets/OpenMMORPG/Demo/Textures/FlameSoft.png";
        private const string FlameMaterialPath = "Assets/OpenMMORPG/Demo/Materials/FlameAdditive.mat";
        private const string SmokeMaterialPath = "Assets/OpenMMORPG/Demo/Materials/FlameSmoke.mat";

        /// <summary>The numbers that make a torch head a torch head and a campfire a campfire.</summary>
        private struct Recipe
        {
            /// <summary>Radius of the bed the flame rises from.</summary>
            public float Bed;
            /// <summary>Size of one flame sprite, least and most.</summary>
            public float FlameSizeMin, FlameSizeMax;
            /// <summary>How long a flame sprite lives, and so how tall the fire stands.</summary>
            public float FlameLifeMin, FlameLifeMax;
            public float FlameSpeedMin, FlameSpeedMax;
            public float FlameRate;
            public float EmberRate;
            public float SmokeRate;
            public float SmokeSize;
            public float GlowSize;
            public float LightIntensity;
            public float LightRange;
            /// <summary>How high above the bed the light sits: in the body of the fire, not at its foot.</summary>
            public float LightHeight;
            public float Sway;
            public DemoTorch.Schedule Schedule;
        }

        private static readonly Recipe Torch = new Recipe
        {
            Bed = 0.035f,
            FlameSizeMin = 0.13f, FlameSizeMax = 0.21f,
            FlameLifeMin = 0.35f, FlameLifeMax = 0.6f,
            FlameSpeedMin = 0.45f, FlameSpeedMax = 0.8f,
            FlameRate = 26f,
            EmberRate = 4f,
            SmokeRate = 2.5f, SmokeSize = 0.16f,
            GlowSize = 0.55f,
            LightIntensity = 1.6f, LightRange = 7f, LightHeight = 0.12f,
            Sway = 0.03f,
            Schedule = DemoTorch.Schedule.Night,
        };

        private static readonly Recipe Campfire = new Recipe
        {
            Bed = 0.17f,
            FlameSizeMin = 0.28f, FlameSizeMax = 0.46f,
            FlameLifeMin = 0.5f, FlameLifeMax = 0.9f,
            FlameSpeedMin = 0.8f, FlameSpeedMax = 1.4f,
            FlameRate = 60f,
            EmberRate = 12f,
            SmokeRate = 5f, SmokeSize = 0.38f,
            GlowSize = 1.3f,
            LightIntensity = 3.2f, LightRange = 13f, LightHeight = 0.55f,
            Sway = 0.06f,
            Schedule = DemoTorch.Schedule.Night,
        };

        [MenuItem("Open MMORPG/Demo/Build Flame Effects")]
        public static void Build()
        {
            Texture2D sprite = BuildSprite();
            Material flame = ParticleMaterial(FlameMaterialPath, sprite, true);
            Material smoke = ParticleMaterial(SmokeMaterialPath, sprite, false);

            BuildFlame(TorchFlamePath, Torch, flame, smoke);
            BuildFlame(CampfireFlamePath, Campfire, flame, smoke);
            AssetDatabase.SaveAssets();
            Debug.Log($"[{nameof(DemoFlameBuilder)}] Built the flame effects.");
        }

        /// <summary>Loads one of the flame prefabs, building them first if they are not there.</summary>
        public static GameObject Flame(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                Build();
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }
            return prefab;
        }

        /// <summary>
        /// Puts a fire on something: instantiates the flame prefab under it at the
        /// given local offset, and returns the torch component so the caller can set
        /// its hours.
        /// </summary>
        public static DemoTorch Light(string prefabPath, Transform holder, Vector3 localPosition, DemoTorch.Schedule schedule)
        {
            GameObject prefab = Flame(prefabPath);
            if (prefab == null)
            {
                Debug.LogError($"[{nameof(DemoFlameBuilder)}] No flame prefab at \"{prefabPath}\".");
                return null;
            }
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, holder);
            instance.transform.localPosition = localPosition;
            instance.transform.localRotation = Quaternion.identity;
            var torch = instance.GetComponent<DemoTorch>();
            torch.schedule = schedule;
            // Out or lit as the scene's preview hour says, so the saved scene is right
            // as built rather than after the first repaint.
            torch.Settle();
            return torch;
        }

        /// <summary>
        /// A soft round sprite: white, fading out to the edge. Drawn rather than
        /// shipped, because it is sixty lines of arithmetic against a texture whose
        /// licence would have to be checked.
        /// </summary>
        private static Texture2D BuildSprite()
        {
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
                    // Smoothed, then steepened: a plain linear fade has a visible edge
                    // where it meets zero, and a plain smoothstep is too fat in the middle
                    // for a flame, whose brightness lives in a small core.
                    a = a * a * (3f - 2f * a);
                    a = Mathf.Pow(a, 1.5f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply();
            DemoIslandBuilder.EnsureFolder(System.IO.Path.GetDirectoryName(TexturePath).Replace('\\', '/'));
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
                // A smooth gradient this small is the one kind of texture block
                // compression visibly bands, and at 128 square it is 64 KB either way.
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
        }

        /// <summary>
        /// A URP particle material, added for fire and blended for smoke.
        ///
        /// The blend is written into the material's properties directly rather than
        /// through the material inspector, which is the only thing that normally does it:
        /// the shader reads _SrcBlend and _DstBlend, and the inspector's job is to set
        /// them from the Blend dropdown. Setting the dropdown alone changes nothing.
        /// </summary>
        private static Material ParticleMaterial(string path, Texture2D sprite, bool additive)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
            {
                Debug.LogError($"[{nameof(DemoFlameBuilder)}] No URP particle shader.");
                return null;
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                DemoIslandBuilder.EnsureFolder(System.IO.Path.GetDirectoryName(path).Replace('\\', '/'));
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            material.SetTexture("_BaseMap", sprite);
            // Over white for the fire, so the sprite is brighter than anything lit can
            // be and the bloom picks it out; smoke is a thing that is lit, not a light.
            material.SetColor("_BaseColor", additive ? new Color(1.15f, 1.15f, 1.15f, 1f) : Color.white);
            material.SetFloat("_Surface", 1f);            // transparent
            material.SetFloat("_Blend", additive ? 2f : 0f); // additive / alpha
            material.SetFloat("_ColorMode", 0f);          // multiply the particle colour in
            material.SetFloat("_Cull", (float)CullMode.Off);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_AlphaClip", 0f);
            material.SetFloat("_SoftParticlesEnabled", 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.DisableKeyword("_ALPHAMODULATE_ON");
            material.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void BuildFlame(string path, Recipe recipe, Material flameMaterial, Material smokeMaterial)
        {
            var root = new GameObject(System.IO.Path.GetFileNameWithoutExtension(path));
            try
            {
                ParticleSystem fire = Fire(root.transform, recipe, flameMaterial);
                ParticleSystem embers = Embers(root.transform, recipe, flameMaterial);
                ParticleSystem smoke = Smoke(root.transform, recipe, smokeMaterial);
                ParticleSystem glow = Glow(root.transform, recipe, flameMaterial);

                var lamp = new GameObject("Lamp");
                lamp.transform.SetParent(root.transform, false);
                lamp.transform.localPosition = new Vector3(0f, recipe.LightHeight, 0f);
                Light light = lamp.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(1f, 0.62f, 0.30f);
                light.intensity = recipe.LightIntensity;
                light.range = recipe.LightRange;
                light.shadows = LightShadows.None;

                var torch = root.AddComponent<DemoTorch>();
                torch.lamp = light;
                torch.flames = new[] { fire, embers, smoke, glow };
                torch.intensity = recipe.LightIntensity;
                torch.sway = recipe.Sway;
                torch.schedule = recipe.Schedule;

                DemoIslandBuilder.EnsureFolder(System.IO.Path.GetDirectoryName(path).Replace('\\', '/'));
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// One emitter with the defaults every part of the fire shares: looping, not
        /// starting on its own (the torch starts it), simulated where it stands so a
        /// flame leans with its holder, and drawing no shadows.
        /// </summary>
        private static ParticleSystem Emitter(Transform parent, string name, Material material, ParticleSystemRenderMode mode, int most)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var system = go.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.playOnAwake = false;
            main.prewarm = false;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = most;
            main.startColor = Color.white;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = mode;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortMode = ParticleSystemSortMode.None;
            return system;
        }

        /// <summary>A cone standing on its point, so the emitter throws its particles upward.</summary>
        private static void RiseFrom(ParticleSystem system, float radius, float angle, float height)
        {
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = angle;
            shape.radius = radius;
            shape.radiusThickness = 1f;
            // A cone emits along its own +Z. Turned back a quarter, that is up.
            shape.rotation = new Vector3(-90f, 0f, 0f);
            shape.position = new Vector3(0f, height, 0f);
        }

        private static void Wander(ParticleSystem system, float strength, float frequency)
        {
            ParticleSystem.NoiseModule noise = system.noise;
            noise.enabled = true;
            noise.strength = strength;
            noise.frequency = frequency;
            noise.scrollSpeed = 1.2f;
            noise.damping = true;
            noise.octaveCount = 1;
            noise.quality = ParticleSystemNoiseQuality.Medium;
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
        /// The body of the fire: sprites that rise, stretch along their rise, and cool
        /// from white through orange to a red that all but vanishes, added over each
        /// other. The stretch is what makes them tongues rather than bubbles.
        /// </summary>
        private static ParticleSystem Fire(Transform parent, Recipe recipe, Material material)
        {
            ParticleSystem system = Emitter(parent, "Fire", material, ParticleSystemRenderMode.Stretch, 96);
            ParticleSystem.MainModule main = system.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(recipe.FlameLifeMin, recipe.FlameLifeMax);
            main.startSpeed = new ParticleSystem.MinMaxCurve(recipe.FlameSpeedMin, recipe.FlameSpeedMax);
            main.startSize = new ParticleSystem.MinMaxCurve(recipe.FlameSizeMin, recipe.FlameSizeMax);

            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = recipe.FlameRate;

            RiseFrom(system, recipe.Bed, 6f, 0f);
            // Enough to lean and lick, not enough to throw tongues sideways: at twice
            // this the campfire spread into a wide pale cloud rather than standing up.
            Wander(system, recipe.FlameSizeMax * 0.7f, 2.2f);
            // Orange within the first tenth of its life. Held yellow any longer, the
            // overlap of a few dozen sprites adds up to a pale blob with no red in it.
            Tint(system, Ramp(
                (0f, new Color(1f, 0.95f, 0.7f), 0f),
                (0.1f, new Color(1f, 0.8f, 0.4f), 1f),
                (0.4f, new Color(1f, 0.48f, 0.1f), 0.9f),
                (0.75f, new Color(0.7f, 0.14f, 0.02f), 0.4f),
                (1f, new Color(0.3f, 0.04f, 0f), 0f)));
            Shrink(system, new AnimationCurve(
                new Keyframe(0f, 0.55f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0.15f)));

            var renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.lengthScale = 1.9f;
            renderer.velocityScale = 0f;
            return system;
        }

        /// <summary>Sparks: few, small, bright, and carried up further than the flame reaches.</summary>
        private static ParticleSystem Embers(Transform parent, Recipe recipe, Material material)
        {
            ParticleSystem system = Emitter(parent, "Embers", material, ParticleSystemRenderMode.Billboard, 48);
            ParticleSystem.MainModule main = system.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.1f, 2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(recipe.FlameSpeedMax * 0.9f, recipe.FlameSpeedMax * 1.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(recipe.FlameSizeMin * 0.14f, recipe.FlameSizeMin * 0.26f);
            main.gravityModifier = -0.04f;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = recipe.EmberRate;

            RiseFrom(system, recipe.Bed * 1.2f, 20f, recipe.FlameSizeMax * 0.5f);
            Wander(system, recipe.FlameSizeMax * 2.2f, 1.4f);
            Tint(system, Ramp(
                (0f, new Color(1f, 0.85f, 0.5f), 1f),
                (0.5f, new Color(1f, 0.45f, 0.1f), 1f),
                (1f, new Color(0.8f, 0.15f, 0.02f), 0f)));
            return system;
        }

        /// <summary>
        /// Smoke: grey, blended rather than added, born small in the flame and growing
        /// as it thins. It is mostly for the campfire; a torch gives off a wisp.
        /// </summary>
        private static ParticleSystem Smoke(Transform parent, Recipe recipe, Material material)
        {
            ParticleSystem system = Emitter(parent, "Smoke", material, ParticleSystemRenderMode.Billboard, 32);
            ParticleSystem.MainModule main = system.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 2.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(recipe.FlameSpeedMin * 0.6f, recipe.FlameSpeedMin * 0.9f);
            main.startSize = recipe.SmokeSize;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = recipe.SmokeRate;

            ParticleSystem.RotationOverLifetimeModule spin = system.rotationOverLifetime;
            spin.enabled = true;
            spin.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);

            // Born above the flame's top, where the fire has already gone out.
            RiseFrom(system, recipe.Bed * 0.8f, 12f, recipe.FlameSpeedMax * recipe.FlameLifeMax * 0.8f);
            Wander(system, recipe.SmokeSize * 0.8f, 0.8f);
            Tint(system, Ramp(
                (0f, new Color(0.30f, 0.27f, 0.25f), 0f),
                (0.2f, new Color(0.34f, 0.32f, 0.31f), 0.32f),
                (1f, new Color(0.45f, 0.45f, 0.46f), 0f)));
            Shrink(system, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(1f, 2.2f)));
            return system;
        }

        /// <summary>
        /// The halo: one large, faint sprite that sits in the fire and breathes, so the
        /// air around the flame is warm rather than the flame being cut out of the dark.
        /// </summary>
        private static ParticleSystem Glow(Transform parent, Recipe recipe, Material material)
        {
            ParticleSystem system = Emitter(parent, "Glow", material, ParticleSystemRenderMode.Billboard, 4);
            ParticleSystem.MainModule main = system.main;
            main.startLifetime = 1f;
            main.startSpeed = 0f;
            main.startSize = recipe.GlowSize;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = 2f;

            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = recipe.Bed * 0.5f;
            shape.position = new Vector3(0f, recipe.FlameSpeedMax * recipe.FlameLifeMax * 0.3f, 0f);

            Tint(system, Ramp(
                (0f, new Color(1f, 0.6f, 0.25f), 0f),
                (0.5f, new Color(1f, 0.6f, 0.25f), 0.11f),
                (1f, new Color(1f, 0.5f, 0.2f), 0f)));
            Shrink(system, new AnimationCurve(new Keyframe(0f, 0.8f), new Keyframe(0.5f, 1f), new Keyframe(1f, 0.85f)));
            return system;
        }
    }
}
