using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// Footprints on the beach and splashes in the sea (2026-10-05): the shared particle systems
    /// <see cref="FootstepEffectsHub"/> draws with, and the <see cref="FootstepEffects"/> component on the
    /// player entities that decides when.
    ///
    /// A partial of the skill-effect builder like the harvest effects, to reuse its emitter and material
    /// plumbing. **Everything is drawn here, nothing is shipped**: the print, the ripple and the drop are
    /// arithmetic written to `Demo/Textures` only if missing (delete one to redraw it), so there is no
    /// licence to check, and the foam and sand puffs reuse the dust billow the Charge already has.
    ///
    /// The hub is one prefab, not one effect per kind, because a footprint has to outlive the foot that
    /// made it and so cannot be a pooled `GameEffect` that is recycled in two seconds. Seven world-space
    /// systems that nothing plays by itself: the component `Emit`s into them.
    ///
    /// **The print quad.** A flat unit square in the x/z plane, normal up, toe at +z, with the texture's u
    /// across and v along. The left foot's is the same square with u reversed, so one white-and-grey boot
    /// print serves both feet and the sole's own asymmetry (the instep strip on the outer side) mirrors.
    /// Two systems rather than one, because a system draws a single mesh.
    ///
    /// **Depth (2026-10-05).** The print carries a normal map baked from a height field: the sole pressed
    /// 2 cm in with steep walls, a low ridge of displaced sand round it, and the boot's tread standing up in
    /// the bottom. The quad is lit, so the sun puts one wall in light and the other in shade and the ridge
    /// catches it - the hollow reads as a hollow from the way the light falls, not from a painted shadow, and
    /// turns with the time of day. Its colour is the sand's own (the interior a little darker, as pressed sand
    /// is), so the rim melts into the beach and only the relief shows. That needs three things a flat sticker
    /// did not: tangents on the quad (both feet's, the mirrored one included, by `RecalculateTangents`), the
    /// renderer's Tangent vertex stream, and the material's `_NORMALMAP`. It also receives shadows now, or a
    /// print under the character's own shadow stays sunlit.
    /// </summary>
    public static partial class DemoSkillEffectBuilder
    {
        private const string FootstepDir = "Assets/OpenMMORPG/Demo/Prefabs/Effects/Footsteps";
        private const string FootstepHubPath = FootstepDir + "/FootstepEffectsHub.prefab";
        private const string FootPrintLeftMeshPath = FootstepDir + "/FootPrintLeft.asset";
        private const string FootPrintRightMeshPath = FootstepDir + "/FootPrintRight.asset";
        private const string FootPrintTexturePath = "Assets/OpenMMORPG/Demo/Textures/FootPrint.png";
        private const string WaterRingTexturePath = "Assets/OpenMMORPG/Demo/Textures/WaterRing.png";
        private const string WaterDropTexturePath = "Assets/OpenMMORPG/Demo/Textures/WaterDrop.png";
        private const string FootPrintMaterialPath = MaterialDir + "/MI_FootPrint.mat";
        private const string WaterRingMaterialPath = MaterialDir + "/MI_WaterRing.mat";
        private const string WaterDropMaterialPath = MaterialDir + "/MI_WaterDrop.mat";
        private const string FootPrintNormalPath = "Assets/OpenMMORPG/Demo/Textures/FootPrintNormal.png";
        private const string IslandSeaMaterialPath = "Assets/OpenMMORPG/Demo/Materials/Island_Sea.mat";

        /// <summary>The print quad's size, in metres at model scale one: the boot plus the rim round it.</summary>
        private const float PrintQuadWidth = 0.17f;
        private const float PrintQuadLength = 0.36f;

        /// <summary>How much of the quad the sole itself spans, across and along; the rest is rim.</summary>
        private const float SoleSpanU = 0.78f;
        private const float SoleSpanV = 0.8f;

        /// <summary>How deep a print is pressed, in metres: the scale of the height field the normals come from.</summary>
        private const float PrintDepth = 0.02f;

        [MenuItem("Open MMORPG/Demo/Build Footstep Effects")]
        public static void BuildFootstepEffects()
        {
            DemoItemBuilder.EnsureFolder(FootstepDir);
            Material print = ParticleMaterial(FootPrintMaterialPath, FootPrintTexture(), ParticleBlend.Alpha, Color.white, lit: true);
            Texture2D printNormal = FootPrintNormalTexture();
            if (print != null && printNormal != null)
            {
                print.SetTexture("_BumpMap", printNormal);
                print.SetFloat("_BumpScale", 1f);
                print.EnableKeyword("_NORMALMAP");
                print.SetFloat("_ReceiveShadows", 1f);
                print.DisableKeyword("_RECEIVE_SHADOWS_OFF");
                EditorUtility.SetDirty(print);
            }
            Material ring = ParticleMaterial(WaterRingMaterialPath, WaterRingTexture(), ParticleBlend.Alpha, Color.white, lit: true);
            Material drop = ParticleMaterial(WaterDropMaterialPath, WaterDropTexture(), ParticleBlend.Alpha, Color.white, lit: true);
            // The billow the Charge's dust uses: foam on the sea and a puff of sand are both a soft lobed
            // white blob, and the particle's own colour makes it either.
            Material billow = ParticleMaterial(DustMaterialPath, FirePuffSprite(), ParticleBlend.Alpha, Color.white, lit: true);
            if (print == null || ring == null || drop == null || billow == null)
                return;

            BuildFootstepHub(print, ring, drop, billow);
            AssetDatabase.SaveAssets();
            int wired = WireFootstepEffects();
            Debug.Log($"[{nameof(DemoSkillEffectBuilder)}] Footstep hub built at {FootstepHubPath} and wired onto {wired} player entit{(wired == 1 ? "y" : "ies")}.");
        }

        private static void BuildFootstepHub(Material print, Material ring, Material drop, Material billow)
        {
            var root = new GameObject("FootstepEffectsHub");
            try
            {
                var hub = root.AddComponent<FootstepEffectsHub>();

                hub.printsLeft = FootstepPrints(root.transform, "PrintsLeft", print, FootPrintMesh(FootPrintLeftMeshPath, mirror: true));
                hub.printsRight = FootstepPrints(root.transform, "PrintsRight", print, FootPrintMesh(FootPrintRightMeshPath, mirror: false));

                // Drops: streaked along their flight, fading as they come back down.
                hub.droplets = FootstepEmitter(root.transform, "Droplets", drop, 700, ParticleSystemRenderMode.Stretch);
                ParticleSystem.MainModule dropMain = hub.droplets.main;
                dropMain.gravityModifier = 1f;
                ParticleSystemRenderer dropRenderer = hub.droplets.GetComponent<ParticleSystemRenderer>();
                dropRenderer.velocityScale = 0.05f;
                dropRenderer.lengthScale = 1.5f;
                Tint(hub.droplets, Ramp((0f, Color.white, 1f), (0.7f, Color.white, 0.9f), (1f, Color.white, 0f)));
                Shrink(hub.droplets, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.55f)));

                // Rings on the sea: flat, spreading fast and slowing, thinning as they go.
                hub.rings = FootstepEmitter(root.transform, "Rings", ring, 300, ParticleSystemRenderMode.HorizontalBillboard);
                Tint(hub.rings, Ramp((0f, Color.white, 0f), (0.08f, Color.white, 1f), (0.5f, Color.white, 0.55f), (1f, Color.white, 0f)));
                Shrink(hub.rings, new AnimationCurve(new Keyframe(0f, 0.18f), new Keyframe(0.3f, 0.62f), new Keyframe(1f, 1f)));

                // Foam behind a swimmer: a lobed patch that opens out a little and thins away.
                hub.foam = FootstepEmitter(root.transform, "Foam", billow, 300, ParticleSystemRenderMode.HorizontalBillboard);
                Tint(hub.foam, Ramp((0f, Color.white, 0f), (0.12f, Color.white, 1f), (0.55f, Color.white, 0.55f), (1f, Color.white, 0f)));
                Shrink(hub.foam, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 1f)));

                // Sand: grains thrown off a hard footfall; and billows for its dust and for the sea's spray.
                hub.sandGrains = FootstepEmitter(root.transform, "SandGrains", drop, 300, ParticleSystemRenderMode.Billboard);
                ParticleSystem.MainModule grainMain = hub.sandGrains.main;
                grainMain.gravityModifier = 1.6f;
                Tint(hub.sandGrains, Ramp((0f, Color.white, 1f), (0.7f, Color.white, 0.9f), (1f, Color.white, 0f)));

                hub.puffs = FootstepEmitter(root.transform, "Puffs", billow, 200, ParticleSystemRenderMode.Billboard);
                Tint(hub.puffs, Ramp((0f, Color.white, 0f), (0.12f, Color.white, 1f), (1f, Color.white, 0f)));
                Shrink(hub.puffs, new AnimationCurve(new Keyframe(0f, 0.55f), new Keyframe(1f, 1.5f)));

                PrefabUtility.SaveAsPrefabAsset(root, FootstepHubPath);
                AssetDatabase.ImportAsset(FootstepHubPath, ImportAssetOptions.ForceUpdate);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// An emitter nothing plays on its own: world space, no rate, no shape, no start speed, and
        /// `AlwaysSimulate` so a particle left behind is not frozen when the camera looks the other way.
        /// The component `Emit`s into it with a position, a velocity, a size, a colour and a lifetime of its own.
        /// </summary>
        private static ParticleSystem FootstepEmitter(Transform parent, string name, Material material, int most,
                                                      ParticleSystemRenderMode mode)
        {
            ParticleSystem system = Emitter(parent, name, material, most);
            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.playOnAwake = true;
            main.duration = 5f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            main.startSpeed = 0f;
            main.startLifetime = 1f;
            main.startSize = 1f;
            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = false;

            ParticleSystemRenderer renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = mode;
            return system;
        }

        /// <summary>One foot's footprints: a flat mesh particle, aligned to the world so its own 3D rotation lays it on the slope.</summary>
        private static ParticleSystem FootstepPrints(Transform parent, string name, Material material, Mesh mesh)
        {
            ParticleSystem system = FootstepEmitter(parent, name, material, 600, ParticleSystemRenderMode.Mesh);
            ParticleSystem.MainModule main = system.main;
            main.startRotation3D = true;
            main.startSize3D = true;
            main.startSizeX = 1f;
            main.startSizeY = 1f;
            main.startSizeZ = 1f;

            ParticleSystemRenderer renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.mesh = mesh;
            renderer.alignment = ParticleSystemRenderSpace.World;
            // The normal map needs the quad's tangents passed through, and a print under a shadow must be in it.
            renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream>
            {
                ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Normal,
                ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV, ParticleSystemVertexStream.Tangent,
            });
            // **Not instanced.** Mesh particles are GPU-instanced by default, and then this stream list
            // describes the per-instance data rather than the vertices: with Tangent in it the layout no longer
            // matches what URP's particle instancing reads, and only the first print in the system drew - every
            // other one was there, alive and opaque, and drew nothing (found 2026-10-05 by recolouring one red
            // in place). A few hundred quads built on the CPU cost nothing, and the mesh's tangents pass through.
            renderer.enableGPUInstancing = false;
            renderer.receiveShadows = true;
            // In for an instant, as a print is when a boot lands, and out over the last two fifths.
            Tint(system, Ramp((0f, Color.white, 0f), (0.01f, Color.white, 1f), (0.6f, Color.white, 1f), (1f, Color.white, 0f)));
            return system;
        }

        /// <summary>
        /// The print's quad: one unit square flat on x/z, normal up, u across and v along. The left foot's
        /// has u reversed. Refilled in place when it exists, so what uses it keeps it.
        /// </summary>
        private static Mesh FootPrintMesh(string path, bool mirror)
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool made = mesh == null;
            if (made)
                mesh = new Mesh();
            mesh.Clear();
            mesh.name = System.IO.Path.GetFileNameWithoutExtension(path);
            mesh.SetVertices(new[]
            {
                new Vector3(-0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f),
                new Vector3(0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, -0.5f),
            });
            mesh.SetNormals(new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up });
            float left = mirror ? 1f : 0f;
            float right = mirror ? 0f : 1f;
            mesh.SetUVs(0, new[]
            {
                new Vector2(left, 0f), new Vector2(left, 1f), new Vector2(right, 1f), new Vector2(right, 0f),
            });
            // Clockwise from above, which is the front face.
            mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            // Along u, with the handedness the mirrored quad needs, so the normal map lights both feet alike.
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            if (made)
            {
                DemoItemBuilder.EnsureFolder(FootstepDir);
                AssetDatabase.CreateAsset(mesh, path);
            }
            else
            {
                EditorUtility.SetDirty(mesh);
            }
            return mesh;
        }

        // ---- the textures -------------------------------------------------------------------------

        /// <summary>
        /// The print's colour and coverage: the sand's own colour (the particle's tint supplies it) with the
        /// pressed interior a little darker, and alpha reaching out past the sole over the rim of pushed-up sand
        /// before it fades - the relief itself is the normal map's (see <see cref="FootPrintNormalTexture"/>).
        /// Toe at the top (v = 1), the right foot's outer side on the right. Drawn only into a gap.
        /// </summary>
        private static Texture2D FootPrintTexture()
        {
            return DrawTexture(FootPrintTexturePath, 128, 256, (u, v) =>
            {
                float d = SoleDepth(u, v, out float tread);
                // The rim is the sand's own colour, so it is only half laid on: at full cover it averaged into
                // the hollow in the texture's smaller mips and washed the print out past four metres.
                float rim = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.32f, -0.06f, d));
                float alpha = rim * Mathf.Lerp(0.55f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.06f, 0.1f, d)));
                float pressed = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.02f, 0.3f, d));
                // Darker than the depth alone would make it: past a few metres the normal map's mips flatten
                // the relief out, and the colour is all that is left to see the print by. Measured: a hollow
                // at 0.66 of the sand was invisible from the gameplay camera in flat rain light; 0.5 holds.
                float shade = Mathf.Lerp(1f, 0.5f, pressed);
                shade = Mathf.Lerp(shade, 0.92f, tread * 0.5f);
                byte grey = (byte)Mathf.RoundToInt(Mathf.Clamp01(shade) * 255f);
                return new Color32(grey, grey, grey, (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha) * 255f));
            });
        }

        /// <summary>
        /// The print's relief as a tangent-space normal map, from a height field in units of
        /// <see cref="PrintDepth"/>: -1 on the pressed sole, walls over the outer quarter of it, a ridge of
        /// displaced sand just outside the edge, and the tread bars standing a third of the way back up. Slopes
        /// are taken in metres across the quad's real size, so the walls come out as steep as a 2 cm hollow in
        /// a 13 cm sole is. Drawn only into a gap.
        /// </summary>
        private static Texture2D FootPrintNormalTexture()
        {
            const int width = 128;
            const int height = 256;
            if (!System.IO.File.Exists(FootPrintNormalPath))
            {
                var field = new float[width * height];
                for (int y = 0; y < height; ++y)
                {
                    for (int x = 0; x < width; ++x)
                    {
                        float d = SoleDepth((x + 0.5f) / width, (y + 0.5f) / height, out float tread);
                        float press = -Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.02f, 0.25f, d));
                        float ridge = 0.35f * Mathf.Exp(-Mathf.Pow((d + 0.13f) / 0.1f, 2f));
                        field[y * width + x] = press + ridge + 0.35f * tread;
                    }
                }
                float metresPerU = PrintQuadWidth / width;
                float metresPerV = PrintQuadLength / height;
                var pixels = new Color32[width * height];
                for (int y = 0; y < height; ++y)
                {
                    for (int x = 0; x < width; ++x)
                    {
                        float left = field[y * width + Mathf.Max(0, x - 1)];
                        float right = field[y * width + Mathf.Min(width - 1, x + 1)];
                        float down = field[Mathf.Max(0, y - 1) * width + x];
                        float up = field[Mathf.Min(height - 1, y + 1) * width + x];
                        float dhdu = (right - left) * PrintDepth / (2f * metresPerU);
                        float dhdv = (up - down) * PrintDepth / (2f * metresPerV);
                        Vector3 n = new Vector3(-dhdu, -dhdv, 1f).normalized;
                        pixels[y * width + x] = new Color32(
                            (byte)Mathf.RoundToInt((n.x * 0.5f + 0.5f) * 255f),
                            (byte)Mathf.RoundToInt((n.y * 0.5f + 0.5f) * 255f),
                            (byte)Mathf.RoundToInt((n.z * 0.5f + 0.5f) * 255f), 255);
                    }
                }
                var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
                texture.SetPixels32(pixels);
                texture.Apply();
                DemoItemBuilder.EnsureFolder(System.IO.Path.GetDirectoryName(FootPrintNormalPath).Replace('\\', '/'));
                System.IO.File.WriteAllBytes(FootPrintNormalPath, texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(FootPrintNormalPath, ImportAssetOptions.ForceUpdate);
            }

            var importer = AssetImporter.GetAtPath(FootPrintNormalPath) as TextureImporter;
            if (importer != null && (importer.textureType != TextureImporterType.NormalMap || importer.wrapMode != TextureWrapMode.Clamp))
            {
                importer.textureType = TextureImporterType.NormalMap;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.mipmapEnabled = true;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(FootPrintNormalPath);
        }

        /// <summary>
        /// How far into the boot sole a point of the print's quad is: 1 at the middle of the forefoot or heel,
        /// 0 on the sole's edge, negative and growing roughly with distance outside it. The sole is a rounded
        /// forefoot that narrows to the toe, a short heel, and a thin ridge on the outer edge carrying it across
        /// the arch, laid in the middle <see cref="SoleSpanU"/> x <see cref="SoleSpanV"/> of the quad so there is
        /// room round it for the rim. <paramref name="tread"/> is 1 on the tread's bars, 0 between them and
        /// outside the sole.
        /// </summary>
        private static float SoleDepth(float u, float v, out float tread)
        {
            float su = (u - 0.5f) / SoleSpanU + 0.5f;
            float sv = (v - 0.5f) / SoleSpanV + 0.5f;
            float taper = 1f - 0.2f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.72f, 1f, sv));
            float forefoot = Depth(su, sv, 0.50f, 0.66f, 0.44f * taper, 0.33f, 2.4f);
            float heel = Depth(su, sv, 0.50f, 0.14f, 0.30f, 0.14f, 2.6f);
            float strip = Depth(su, sv, 0.72f, 0.31f, 0.065f, 0.15f, 2.2f);
            tread = 0f;
            if (forefoot > 0.2f)
                tread = Mathf.Max(tread, Bar(sv, 0.45f, 0.95f, 0.075f, 0.011f));
            if (heel > 0.2f)
                tread = Mathf.Max(tread, Bar(sv, 0.07f, 0.22f, 0.075f, 0.011f));
            return Mathf.Max(forefoot, Mathf.Max(heel, strip));
        }

        /// <summary>1 at the middle of a rounded rectangle, 0 at its edge, negative outside: how far in a point is.</summary>
        private static float Depth(float u, float v, float cx, float cy, float a, float b, float n)
        {
            float x = Mathf.Pow(Mathf.Abs(u - cx) / a, n);
            float y = Mathf.Pow(Mathf.Abs(v - cy) / b, n);
            return 1f - Mathf.Pow(x + y, 1f / n);
        }

        /// <summary>1 on a thin bar every <paramref name="pitch"/> from <paramref name="from"/> to <paramref name="to"/>, else 0.</summary>
        private static float Bar(float v, float from, float to, float pitch, float halfWidth)
        {
            if (v < from - halfWidth || v > to + halfWidth)
                return 0f;
            float phase = Mathf.Repeat(v - from + pitch * 0.5f, pitch) - pitch * 0.5f;
            return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(halfWidth * 0.6f, halfWidth, Mathf.Abs(phase)));
        }

        /// <summary>
        /// A ripple: a crisp crest at 0.8 of the way out with two fainter ones inside it, as a drop leaves.
        /// Drawn only into a gap.
        /// </summary>
        private static Texture2D WaterRingTexture()
        {
            return DrawSprite(WaterRingTexturePath, 256, (x, y, r) =>
            {
                float crest = Mathf.Exp(-Mathf.Pow((r - 0.80f) / 0.055f, 2f))
                              + 0.42f * Mathf.Exp(-Mathf.Pow((r - 0.60f) / 0.065f, 2f))
                              + 0.2f * Mathf.Exp(-Mathf.Pow((r - 0.40f) / 0.075f, 2f));
                return crest * Mathf.Clamp01((1f - r) * 30f);
            });
        }

        /// <summary>A small round bead of water or sand, soft only at the very edge. Drawn only into a gap.</summary>
        private static Texture2D WaterDropTexture()
        {
            return DrawSprite(WaterDropTexturePath, 64, (x, y, r) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1f, 0.55f, r)));
        }

        /// <summary>
        /// A texture drawn by <paramref name="colour"/>(u, v), u and v from 0 to 1, written only when the
        /// file is missing so a hand-made one at the same path is kept. Imported as transparent-alpha and
        /// clamped, like <see cref="DrawSprite"/>, which cannot colour its pixels.
        /// </summary>
        private static Texture2D DrawTexture(string path, int width, int height, System.Func<float, float, Color32> colour)
        {
            if (!System.IO.File.Exists(path))
            {
                var pixels = new Color32[width * height];
                for (int y = 0; y < height; ++y)
                {
                    for (int x = 0; x < width; ++x)
                        pixels[y * width + x] = colour((x + 0.5f) / width, (y + 0.5f) / height);
                }
                var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
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

        // ---- onto the players -----------------------------------------------------------------------

        /// <summary>
        /// Gives every player entity a <see cref="FootstepEffects"/> pointing at the hub, and returns how many
        /// it did. In place, on the built prefabs, so it is also how an existing entity gets one without
        /// rebuilding it (the entity builder calls <see cref="WireFootstepEffects(GameObject)"/> on a rebuild).
        /// The templates are skipped: `BaseCharacter` is an input, not an entity.
        /// </summary>
        private static readonly string[] EntityPrefabsToWire = new[]
        {
            "Assets/OpenMMORPG/Demo/Prefabs/GamePlay/CharacterEntities/DemoPlayerCharacterFemale.prefab",
            "Assets/OpenMMORPG/Demo/Prefabs/GamePlay/CharacterEntities/DemoPlayerCharacterMale.prefab",
            "Assets/OpenMMORPG/Demo/Prefabs/GamePlay/CharacterEntities/DemoBanditArcherFemale.prefab",
            "Assets/OpenMMORPG/Demo/Prefabs/GamePlay/CharacterEntities/DemoBanditArcherMale.prefab",
            "Assets/OpenMMORPG/Demo/Prefabs/GamePlay/CharacterEntities/DemoBanditFemale.prefab",
            "Assets/OpenMMORPG/Demo/Prefabs/GamePlay/CharacterEntities/DemoBanditMale.prefab",
            "Assets/OpenMMORPG/Demo/Prefabs/GamePlay/CharacterEntities/DemoCultistFemale.prefab",
            "Assets/OpenMMORPG/Demo/Prefabs/GamePlay/CharacterEntities/DemoCultistMale.prefab",
            "Assets/OpenMMORPG/Demo/Prefabs/GamePlay/CharacterEntities/DemoHierophant.prefab",
            "Assets/OpenMMORPG/Demo/Prefabs/GamePlay/CharacterEntities/DemoMarauderFemale.prefab",
            "Assets/OpenMMORPG/Demo/Prefabs/GamePlay/CharacterEntities/DemoMarauderMale.prefab",
            "Assets/OpenMMORPG/Demo/Prefabs/GamePlay/CharacterEntities/DemoDeer.prefab",
            "Assets/OpenMMORPG/Demo/Prefabs/GamePlay/CharacterEntities/DemoWolf.prefab",
            "Assets/OpenMMORPG/Demo/Prefabs/GamePlay/CharacterEntities/DemoWolfPup.prefab",
            "Assets/OpenMMORPG/Demo/Prefabs/GamePlay/Vehicles/DemoHorse.prefab",
            "Assets/OpenMMORPG_DemoBuilder/Templates/BaseEnemy.prefab",
        };

        /// <summary>
        /// Gives every player, humanoid enemy, and animal entity a <see cref="FootstepEffects"/> pointing at the hub.
        /// In place, on the built prefabs.
        /// </summary>
        public static int WireFootstepEffects()
        {
            int wired = 0;
            foreach (string path in EntityPrefabsToWire)
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
                    continue;
                GameObject contents = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    if (WireFootstepEffects(contents))
                    {
                        PrefabUtility.SaveAsPrefabAsset(contents, path);
                        ++wired;
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
            }
            return wired;
        }

        /// <summary>
        /// One entity's component, added if missing and pointed at the hub and the demo's sea level. False
        /// (and nothing added) until <c>Build Footstep Effects</c> has made the hub, so the entity builder can
        /// call it unconditionally.
        /// </summary>
        internal static bool WireFootstepEffects(GameObject entity)
        {
            var hub = AssetDatabase.LoadAssetAtPath<FootstepEffectsHub>(FootstepHubPath);
            if (hub == null)
                return false;
            var effects = entity.GetComponent<FootstepEffects>();
            if (effects == null)
                effects = entity.AddComponent<FootstepEffects>();
            effects.hubPrefab = hub;
            effects.seaLevel = DemoIslandBuilder.WaterLevel;
            // The water's drawn swell decides what is wet: see StylizedWaterSurface.
            effects.seaMaterial = AssetDatabase.LoadAssetAtPath<Material>(IslandSeaMaterialPath);
            // The WaterSplash family, as Wire Audio would put it on.
            DemoAudioWiring.WireSplashSounds(entity);

            bool isPlayer = entity.name.Contains("Player");
            effects.syncStepSounds = isPlayer;

            EditorUtility.SetDirty(effects);
            return true;
        }
    }
}
