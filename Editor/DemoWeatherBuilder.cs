using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// Builds the island's weather (<see cref="WeatherSystem"/>) into a scene: the rain, its splashes,
    /// its sound, and the references that tie them to the sky cycle and the wind.
    ///
    /// Two entry points, because they answer two different needs.
    ///
    /// <c>Build Weather</c> is the one to run on a scene that is already there. It makes what is
    /// missing and repairs what is broken - the rain clip, once the user has dropped it in, or a
    /// reference lost to a rebuilt sea - and **leaves every value that is already set alone**, so a
    /// shower tuned by hand in the inspector survives it. <c>Rebuild Weather</c> is the one that
    /// throws the weather object away and starts from the defaults. A full scene regeneration reaches
    /// the same code from <see cref="DemoSceneBuilder"/>, where the scene is new anyway.
    ///
    /// The rain's textures and materials are generated here rather than imported, for the same
    /// reason the underwater quad is: a soft streak and a thin ring are a few lines of arithmetic, and
    /// a generated one cannot drift out of step with the shader that draws it. They are made once and
    /// then left alone, so they can be tuned by hand.
    /// </summary>
    public static class DemoWeatherBuilder
    {
        private const string TextureDir = "Assets/OpenMMORPG/Demo/Textures";
        private const string MaterialDir = "Assets/OpenMMORPG/Demo/Materials";
        private const string StreakTexturePath = TextureDir + "/RainStreak.png";
        private const string SplashTexturePath = TextureDir + "/RainSplash.png";
        private const string StreakMaterialPath = MaterialDir + "/RainStreak.mat";
        private const string SplashMaterialPath = MaterialDir + "/RainSplash.mat";
        public const string RootName = "Weather";
        public const string WeatherPrefabPath = "Assets/OpenMMORPG/Demo/Prefabs/Weather.prefab";

        // ---- menu ---------------------------------------------------------------------------------

        [MenuItem("Open MMORPG/Demo/Build Weather")]
        public static void BuildInOpenScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            GameObject existing = FindRoot(scene);
            bool created = existing == null;
            GameObject root = Build(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            WeatherSystem weather = root.GetComponent<WeatherSystem>();
            Debug.Log($"[{nameof(DemoWeatherBuilder)}] {(created ? "Built" : "Checked")} the weather in {scene.name}: " +
                      $"{(weather.loopSource != null && weather.loopSource.clip != null ? "rain loop \"" + weather.loopSource.clip.name + "\" wired" : "NO RAIN LOOP - drop " + DemoAudioWiring.RainAndThunder + ".wav into Demo/Audio and run this again")}.");
        }

        [MenuItem("Open MMORPG/Demo/Rebuild Weather (resets its settings)")]
        public static void RebuildInOpenScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            GameObject existing = FindRoot(scene);
            if (existing != null)
                Object.DestroyImmediate(existing);
            BuildInOpenScene();
        }

        [MenuItem("Open MMORPG/Demo/Weather/Rain Now")]
        private static void RainNow()
        {
            Force(WeatherSystem.WeatherMode.ForceRain);
        }

        [MenuItem("Open MMORPG/Demo/Weather/Clear Skies")]
        private static void ClearSkies()
        {
            Force(WeatherSystem.WeatherMode.ForceClear);
        }

        [MenuItem("Open MMORPG/Demo/Weather/Back To The Schedule")]
        private static void BackToSchedule()
        {
            Force(WeatherSystem.WeatherMode.Automatic);
        }

        [MenuItem("Open MMORPG/Demo/Weather/Rain Now", true)]
        [MenuItem("Open MMORPG/Demo/Weather/Clear Skies", true)]
        [MenuItem("Open MMORPG/Demo/Weather/Back To The Schedule", true)]
        private static bool PlayingOnly()
        {
            return Application.isPlaying;
        }

        [MenuItem("Open MMORPG/Demo/Weather/Log Forecast")]
        private static void LogForecast()
        {
            var weather = Object.FindFirstObjectByType<WeatherSystem>();
            if (weather == null)
            {
                Debug.LogWarning($"[{nameof(DemoWeatherBuilder)}] No WeatherSystem in the open scene - run Build Weather first.");
                return;
            }
            double now = WeatherSystem.NowSeconds();
            var text = new System.Text.StringBuilder("[DemoWeatherBuilder] The next showers (local time):");
            foreach (WeatherSystem.Shower s in weather.Forecast(now, 8))
            {
                var start = System.DateTimeOffset.FromUnixTimeSeconds((long)s.start).ToLocalTime();
                var end = System.DateTimeOffset.FromUnixTimeSeconds((long)s.end).ToLocalTime();
                text.Append($"\n  {start:HH:mm:ss} - {end:HH:mm:ss}  ({(s.end - s.start) / 60.0:0.0} min, peak {s.peak:0.00}){(s.start <= now ? "  <- raining now" : "")}");
            }
            Debug.Log(text.ToString());
        }

        private static void Force(WeatherSystem.WeatherMode mode)
        {
            var weather = Object.FindFirstObjectByType<WeatherSystem>();
            if (weather == null)
            {
                Debug.LogWarning($"[{nameof(DemoWeatherBuilder)}] No WeatherSystem in the scene.");
                return;
            }
            weather.SetMode(mode);
            Debug.Log($"[{nameof(DemoWeatherBuilder)}] Weather is now {mode}.");
        }

        // ---- the build ----------------------------------------------------------------------------

        private static GameObject FindRoot(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == RootName)
                    return root;
            }
            return null;
        }

        /// <summary>
        /// Makes the weather in a scene, or repairs the one that is there. Returns its root. Call after
        /// the sky cycle and the wind exist, so there is something to hand the cloud and the storm to.
        /// </summary>
        public static GameObject Build(Scene scene)
        {
            GameObject root = FindRoot(scene);
            if (root == null)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WeatherPrefabPath);
                if (prefab != null)
                {
                    root = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                    root.name = RootName;
                }
                else
                {
                    root = new GameObject(RootName);
                    EditorSceneManager.MoveGameObjectToScene(root, scene);
                }
            }

            var weather = root.GetComponent<WeatherSystem>();
            bool fresh = weather == null;
            if (fresh)
                weather = root.AddComponent<WeatherSystem>();

            weather.streaks = EnsureStreaks(root.transform, weather.streaks);
            weather.splashes = EnsureSplashes(root.transform, weather.splashes);
            EnsureSound(root.transform, weather);

            if (weather.skyCycle == null)
                weather.skyCycle = FindInScene<DayNightSkyCycle>(scene);
            if (weather.wind == null)
                weather.wind = FindInScene<FoliageWind>(scene);

            if (fresh)
            {
                weather.seaLevel = DemoIslandBuilder.WaterLevel;
                // What stops a drop: the ground, a roof, a wall, a tree. Never a character - rain
                // does not break on the player, and a drop that did would turn him into a fountain.
                weather.blockers = LayerMask.GetMask("Default", "Building", "Harvestable");
            }

            ConfigureCycle(weather.skyCycle);
            if (!PrefabUtility.IsPartOfAnyPrefab(root))
            {
                PrefabUtility.SaveAsPrefabAssetAndConnect(root, WeatherPrefabPath, InteractionMode.AutomatedAction);
            }
            EditorUtility.SetDirty(weather);
            return root;
        }

        /// <summary>
        /// Gives the sky cycle the numbers it now owns. It takes over the sun's shadow strength and the
        /// scene's linear fog distances (it already owned the fog colour) so that cloud can move them,
        /// and it must start from what the scene had: copied here from the light and from the same
        /// constants <see cref="DemoSceneBuilder"/> sets the fog from, so that on a clear day nothing
        /// looks any different from before.
        /// </summary>
        private static void ConfigureCycle(DayNightSkyCycle cycle)
        {
            if (cycle == null)
                return;
            cycle.clearFogStart = DemoSceneBuilder.ClearFogStart;
            cycle.clearFogEnd = DemoSceneBuilder.ClearFogEnd;
            // Only read while the sky is clear: a light left graded by a preview is not the clear value.
            if (cycle.sun != null && cycle.previewOvercast <= 0f && cycle.Overcast <= 0f)
                cycle.clearShadowStrength = cycle.sun.shadowStrength;
            EditorUtility.SetDirty(cycle);
        }

        private static T FindInScene<T>(Scene scene) where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                T found = root.GetComponentInChildren<T>(true);
                if (found != null)
                    return found;
            }
            return null;
        }

        // ---- the rain -----------------------------------------------------------------------------

        /// <summary>
        /// The streak system.
        ///
        /// Emission is off and so is the shape: <see cref="WeatherSystem"/> emits every drop itself, with
        /// its position, velocity and exact lifetime, which is the only way to make a drop end on the
        /// roof it lands on. Simulation space is World, so a drop stays where it was made when the
        /// camera moves on - it is not carried along with the player like a snow globe.
        /// </summary>
        private static ParticleSystem EnsureStreaks(Transform parent, ParticleSystem existing)
        {
            return existing != null ? existing : NewStreakSystem(parent, "Streaks");
        }

        private static ParticleSystem NewStreakSystem(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.prewarm = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.startLifetime = 1f;
            main.startSpeed = 0f;
            main.startSize = 0.02f;
            main.startColor = Color.white;
            main.gravityModifier = 0f;
            main.maxParticles = 6000;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = false;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            // Length = width * lengthScale + speed * velocityScale. A 24 m/s drop at 0.035 is most of a metre
            // long, which is what a drop looks like at the shutter speed of an eye. The leading end is drawn
            // 10 ms of travel ahead of the particle (WeatherSystem.HeadLead), which the emitter allows for.
            renderer.lengthScale = 1f;
            renderer.velocityScale = 0.035f;
            renderer.cameraVelocityScale = 0f;
            // A drop is a hair wide at distance; without a floor it drops below a pixel and shimmers.
            renderer.minParticleSize = 0.0004f;
            renderer.maxParticleSize = 0.5f;
            renderer.sharedMaterial = StreakMaterial();
            Unlit(renderer);
            return ps;
        }

        /// <summary>
        /// The splash rings. A system of their own, emitted into by <see cref="WeatherSystem"/> at the exact
        /// point and moment a drop lands - not a death sub-emitter on the streaks, which puts the ring
        /// where the particle is when it dies: up to a whole frame of fall past the surface (40 cm at 60
        /// fps, and more when the game is slower), so rings sank into roofs and slipped under eaves at
        /// random.
        /// </summary>
        private static ParticleSystem EnsureSplashes(Transform parent, ParticleSystem existing)
        {
            if (existing != null)
                return existing;

            var go = new GameObject("Splashes");
            go.transform.SetParent(parent, false);
            var splash = go.AddComponent<ParticleSystem>();
            splash.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = splash.main;
            main.loop = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.22f, 0.38f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.16f, 0.30f); // overridden per ring by WeatherSystem.splashSize
            main.startColor = Color.white;
            main.maxParticles = 4000;

            ParticleSystem.EmissionModule emission = splash.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = splash.shape;
            shape.enabled = false;

            // Opens quickly and thins as it goes, like a ripple.
            ParticleSystem.SizeOverLifetimeModule size = splash.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.25f, 0f, 3.2f), new Keyframe(1f, 1f, 0.3f, 0f)));

            ParticleSystem.ColorOverLifetimeModule colour = splash.colorOverLifetime;
            colour.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.9f, 0.12f), new GradientAlphaKey(0f, 1f) });
            colour.color = fade;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            renderer.sharedMaterial = SplashMaterial();
            Unlit(renderer);
            return splash;
        }

        /// <summary>Rain is not lit, does not cast or take a shadow, and is not worth a reflection.</summary>
        private static void Unlit(ParticleSystemRenderer renderer)
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            renderer.allowOcclusionWhenDynamic = false;
        }

        // ---- the sound ----------------------------------------------------------------------------

        /// <summary>
        /// The rain loop: a 2D source, so it is not heard from anywhere, driven by a
        /// <see cref="AmbientSoundLoop"/> so it obeys the ambient volume slider like every other bed, and a
        /// low-pass filter so it can be muffled under a roof and under the sea.
        ///
        /// The clip is found by file-name family (<see cref="DemoAudioWiring.RainAndThunder"/>) like
        /// every other sound in the demo. With none the object is still built and the rain is silent;
        /// run this again once the clip is in.
        /// </summary>
        private static void EnsureSound(Transform parent, WeatherSystem weather)
        {
            AudioSource source = weather.loopSource;
            if (source == null)
            {
                Transform child = parent.Find("Rain");
                var go = child != null ? child.gameObject : new GameObject("Rain");
                if (child == null)
                    go.transform.SetParent(parent, false);
                source = go.GetComponent<AudioSource>();
                if (source == null)
                    source = go.AddComponent<AudioSource>();
                weather.loopSource = source;
            }

            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.volume = 0f;

            if (source.clip == null)
            {
                AudioClip[] clips = DemoAudioWiring.Clips(DemoAudioWiring.RainAndThunder);
                if (clips.Length > 0)
                    source.clip = clips[0];
            }

            var ambient = source.GetComponent<AmbientSoundLoop>();
            if (ambient == null)
            {
                ambient = source.gameObject.AddComponent<AmbientSoundLoop>();
                // Full, and left there: the weather fades the loop through the bed's runtime gain, which
                // is never saved, so nothing it does to the volume can end up in the scene.
                ambient.baseVolume = 1f;
            }
            weather.loop = ambient;

            var filter = source.GetComponent<AudioLowPassFilter>();
            if (filter == null)
            {
                filter = source.gameObject.AddComponent<AudioLowPassFilter>();
                filter.cutoffFrequency = 22000f;
            }
            weather.lowPass = filter;
            EditorUtility.SetDirty(source);
        }

        // ---- textures and materials ---------------------------------------------------------------

        /// <summary>
        /// A soft vertical streak: brightest down the middle, fading to nothing at both sides and at
        /// both ends. Symmetric along its length because a stretched billboard has no way to say which
        /// end is the head, and a gradient that only looks right one way up looks wrong half the time.
        /// </summary>
        private static Texture2D StreakTexture()
        {
            return EnsureTexture(StreakTexturePath, 16, 128, (u, v) =>
            {
                float across = (u - 0.5f) / 0.2f;
                float side = Mathf.Exp(-across * across);
                float along = Mathf.Pow(Mathf.Sin(Mathf.Clamp01(v) * Mathf.PI), 0.7f);
                return side * along;
            });
        }

        /// <summary>A thin ring with a soft edge and a faint dot at its centre: a drop on a puddle.</summary>
        private static Texture2D SplashTexture()
        {
            return EnsureTexture(SplashTexturePath, 64, 64, (u, v) =>
            {
                float dx = u * 2f - 1f;
                float dy = v * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float ring = Mathf.Exp(-Mathf.Pow((r - 0.68f) / 0.11f, 2f));
                float dot = Mathf.Exp(-Mathf.Pow(r / 0.1f, 2f)) * 0.55f;
                float edge = Mathf.Clamp01((1f - r) / 0.12f);
                return Mathf.Clamp01(ring + dot) * edge;
            });
        }

        private static Texture2D EnsureTexture(string path, int width, int height, System.Func<float, float, float> alpha)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null)
                return existing;

            var pixels = new Color32[width * height];
            for (int y = 0; y < height; ++y)
            {
                for (int x = 0; x < width; ++x)
                {
                    float a = Mathf.Clamp01(alpha((x + 0.5f) / width, (y + 0.5f) / height));
                    pixels[y * width + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply();
            DemoIslandBuilder.EnsureFolder(TextureDir);
            System.IO.File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.mipmapEnabled = true;
                importer.sRGBTexture = true;
                // Smooth gradients this small are what block compression bands, and at these sizes the
                // difference is a few kilobytes.
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static Material StreakMaterial()
        {
            return ParticleMaterial(StreakMaterialPath, StreakTexture());
        }

        private static Material SplashMaterial()
        {
            return ParticleMaterial(SplashMaterialPath, SplashTexture());
        }

        /// <summary>
        /// An alpha-blended URP unlit particle material. Made once; a material that exists is returned as
        /// it is, so tuning it by hand survives a rebuild. The blend is written into the properties
        /// directly - the shader reads _SrcBlend and _DstBlend and only the material inspector ever sets
        /// them from the Blend dropdown, so setting the dropdown alone changes nothing.
        /// </summary>
        private static Material ParticleMaterial(string path, Texture2D sprite)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
                return material;

            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
            {
                Debug.LogError($"[{nameof(DemoWeatherBuilder)}] No URP particle shader.");
                return null;
            }

            material = new Material(shader);
            material.SetTexture("_BaseMap", sprite);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Surface", 1f);               // transparent
            material.SetFloat("_Blend", 0f);                 // alpha
            material.SetFloat("_ColorMode", 0f);             // multiply the particle colour in
            material.SetFloat("_Cull", (float)CullMode.Off);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_AlphaClip", 0f);
            material.SetFloat("_SoftParticlesEnabled", 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            // Fade out within a few metres of the lens. A drop passing a hand's breadth from the camera
            // is a long white stick across the picture; faded, it is a flicker, which is what it is.
            material.SetFloat("_CameraFadingEnabled", 1f);
            material.SetFloat("_CameraNearFadeDistance", 1.2f);
            material.SetFloat("_CameraFarFadeDistance", 4.5f);
            material.EnableKeyword("_FADING_ON");
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.DisableKeyword("_ALPHAMODULATE_ON");
            material.renderQueue = (int)RenderQueue.Transparent;
            DemoIslandBuilder.EnsureFolder(MaterialDir);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}
