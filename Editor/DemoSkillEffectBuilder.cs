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
    public static class DemoSkillEffectBuilder
    {
        private const string EffectDir = "Assets/OpenMMORPG/Demo/Prefabs/Effects/Skills";
        private const string MaterialDir = "Assets/OpenMMORPG/Demo/Materials";
        private const string TexturePath = "Assets/OpenMMORPG/Demo/Textures/SkillSoft.png";
        private const string MaterialPath = MaterialDir + "/MI_SkillEffect.mat";
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
            /// <summary>A flat ring running outward along the ground, to this radius.</summary>
            public float Ring;
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

            // Frost Nova: it goes off at the caster's feet, so the floor is the socket and
            // the ring is the whole read - it is the skill that tells you to move.
            new Recipe { Name = "FX_FrostNova", Socket = SocketFloor, Colour = Frost,
                         // Core well under a metre: at 0.9 the halo came out two metres
                         // across and swallowed the caster, which hid the one thing the
                         // skill is for - the ring showing how far it reaches.
                         LifeTime = 1.4f, Core = 0.45f, Burst = 0.18f, Ring = 4.5f },

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

            // ---- the Hierophant ----------------------------------------------

            // Shared by Call the Faithful (1.2s) and Rite of Mending (1.6s), so it is cut
            // to the longer of the two; the shorter cast simply wears it a moment longer.
            new Recipe { Name = "FX_UnholyCast", Socket = SocketBody, Colour = Unholy,
                         Loop = true, LifeTime = 1.95f, Core = 0.35f, Gather = true },
            new Recipe { Name = "FX_UnholyRelease", Socket = SocketBody, Colour = Unholy,
                         LifeTime = 0.9f, Core = 0.35f, Burst = 0.15f },
            new Recipe { Name = "FX_UnholyMend", Socket = SocketBody, Colour = Unholy,
                         LifeTime = 1.6f, Core = 0.35f, Rise = true },

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
            WriteDefaultHitEffect();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[{nameof(DemoSkillEffectBuilder)}] Built {Recipes.Length} skill effects in {EffectDir}.");
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
        public static void AddAreaParticles(GameObject areaRoot, Color colour, float radius, bool lingers)
        {
            Material material = EffectMaterial(BuildSprite());
            if (material == null)
                return;

            var holder = new GameObject("FX");
            holder.transform.SetParent(areaRoot.transform, false);

            // The landing, always: a spray up and out from the middle of the patch.
            ParticleSystem burst = Emitter(holder.transform, "Impact", material, 60);
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
                if (recipe.Ring > 0f)
                    Ring(root.transform, material, recipe);

                var effect = root.AddComponent<GameEffect>();
                effect.effectSocket = recipe.Socket;
                // `GameEffect.isLoop` is not "the emitters repeat" - it is "this never
                // expires": the component does `_destroyTime = isLoop ? -1 : Time.time +
                // lifeTime`, and -1 is read as never. Nothing stops a cast effect, so
                // setting it was what left Arcane Bolt sparkling on the hand for good.
                // It is left off wherever a lifetime is given, which is everywhere; the
                // emitters keep looping on their own from `Loop`.
                effect.isLoop = recipe.Loop && recipe.LifeTime <= 0f;
                effect.lifeTime = recipe.LifeTime;
                effect.PoolSize = 4;

                string path = $"{EffectDir}/{recipe.Name}.prefab";
                PrefabUtility.SaveAsPrefabAsset(root, path);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
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

        /// <summary>
        /// A flat ring running out along the floor. Sized to the skill's actual radius, so
        /// what a player is told to step out of is what will really hit them.
        /// </summary>
        private static void Ring(Transform parent, Material material, Recipe recipe)
        {
            ParticleSystem system = Emitter(parent, "Ring", material, 320, stretch: true);
            ParticleSystem.MainModule main = system.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = 0.35f;
            main.startLifetime = 0.55f;
            // Reaches the edge in about the time it lives, so the ring arrives where the
            // damage does rather than somewhere short of it.
            main.startSpeed = recipe.Ring / 0.55f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.16f);
            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 90) });
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.15f;
            shape.rotation = new Vector3(90f, 0f, 0f);
            shape.radiusThickness = 0f;
            Tint(system, Ramp((0f, recipe.Colour, 0.9f), (0.75f, recipe.Colour, 0.55f), (1f, recipe.Colour, 0f)));
            Shrink(system, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.35f)));
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
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
            {
                Debug.LogError($"[{nameof(DemoSkillEffectBuilder)}] No URP particle shader.");
                return null;
            }
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader);
                DemoItemBuilder.EnsureFolder(MaterialDir);
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            material.shader = shader;
            material.SetTexture("_BaseMap", sprite);
            // Over white, so the sprite is brighter than anything lit can be and the
            // bloom picks it out.
            material.SetColor("_BaseColor", new Color(1.15f, 1.15f, 1.15f, 1f));
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 2f);
            material.SetFloat("_ColorMode", 0f);
            material.SetFloat("_Cull", (float)CullMode.Off);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_AlphaClip", 0f);
            material.SetFloat("_SoftParticlesEnabled", 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.One);
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)BlendMode.One);
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.DisableKeyword("_ALPHAMODULATE_ON");
            material.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(material);
            return material;
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
