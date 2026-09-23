using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// How tall a character may be: the ramp, the measurement that makes it mean something,
    /// the component on each body and the slider on the create screen.
    ///
    /// Size is not a body part and could not be done the way hair was. The kit's body-part
    /// system swaps and recolours meshes it instantiates as fake equipment, one at a time;
    /// size is the whole character at once, down to the sword in its hand. See
    /// <see cref="MultiplayerARPG.Demo.DemoCharacterSize"/>, which is what actually applies
    /// it - a scale on each model the entity owns.
    ///
    /// What this tool contributes is **the numbers**. The ramp is authored in metres rather
    /// than in percentages, and each body is measured so its own multipliers can be worked
    /// out: the male's crown stands at 1.81m and the female's at 1.77m, so one shared set of
    /// percentages would put the two ends of the slider in two different places for them and
    /// the readout would be wrong for one of the two. Measured and divided, "1.75 m" means
    /// 1.75 metres on either body - and the slider keeps its stop when the player switches
    /// between them, so the switch shows the difference between two bodies rather than
    /// between two heights.
    /// </summary>
    public static class DemoCharacterSizeBuilder
    {
        private const string EntityDir = "Assets/OpenMMORPG/Demo/Prefabs/GamePlay/CharacterEntities";
        private const string CanvasHomePath = "Assets/OpenMMORPG/Demo/Prefabs/UI/Home/CanvasHome.prefab";

        /// <summary>
        /// The ramp, shortest first, in metres.
        ///
        /// **Where the ends sit is a measured decision, not a taste one.** Everything in the
        /// demo is built around a character 1.8m tall: that is the capsule, the navmesh is
        /// baked for it, and the doorways are cut for it. The capsule does not change with
        /// the slider (see <see cref="MultiplayerARPG.Demo.DemoCharacterSize"/>), so the only
        /// thing the ends have to respect is what the eye can catch - a head through a
        /// lintel, or a body so small it stops reading as the same species.
        ///
        /// The village's round-arched doorway is **2.318m** to the crown, measured off
        /// `Door_1_Round`, and the crypt is built from the same wall modules. The tallest
        /// stop here is 1.95m, which walks under that with a third of a metre to spare even
        /// with a helmet on. The shortest is 1.55m, which is a small adult rather than a
        /// child: below about 1.5 the proportions of these bodies - they are stylised, with
        /// large heads - stop reading as short and start reading as young, which is not a
        /// thing the demo should be offering.
        ///
        /// Nine stops, five centimetres apart: enough that dragging feels continuous, coarse
        /// enough that each one is a visibly different person standing next to the last.
        /// </summary>
        public static readonly float[] Heights =
        {
            1.55f, 1.60f, 1.65f, 1.70f, 1.75f, 1.80f, 1.85f, 1.90f, 1.95f,
        };

        /// <summary>
        /// The bare body meshes, which is what a character is measured over.
        ///
        /// Hair is deliberately not: it is a choice made two windows away, and a body whose
        /// measured height rose and fell with the hairstyle would hand two characters of the
        /// same stated height two different scales. The demo's bodies name their own meshes
        /// BareHead, BareBody, BareArms, BareLegs and BareFeet (see DemoCharacterBuilder).
        /// </summary>
        private const string BareMeshPrefix = "Bare";

        [MenuItem("Open MMORPG/Demo/Build Character Size")]
        public static void Build()
        {
            var report = new System.Text.StringBuilder();
            int bodies = WriteComponents(report);
            bool ui = BuildSlider();

            AssetDatabase.SaveAssets();
            Debug.Log($"[{nameof(DemoCharacterSizeBuilder)}] Character size built: {Heights.Length} stops from " +
                      $"{Heights[0].ToString("F2", CultureInfo.InvariantCulture)}m to " +
                      $"{Heights[Heights.Length - 1].ToString("F2", CultureInfo.InvariantCulture)}m on {bodies} " +
                      $"bodies, {(ui ? "slider on the create screen" : "NO SLIDER")}." +
                      $"\nMeasured bodies:{report}");
        }

        // ------------------------------------------------------------------
        // The bodies
        // ------------------------------------------------------------------

        private static int WriteComponents(System.Text.StringBuilder report)
        {
            var titles = new string[Heights.Length];
            for (int i = 0; i < Heights.Length; ++i)
                titles[i] = Heights[i].ToString("0.00", CultureInfo.InvariantCulture) + " m";

            int written = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { EntityDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    // Players only. An NPC or a monster has no create screen and no saved
                    // size, and a component that can never be driven is one that only looks
                    // like it might do something.
                    var player = root.GetComponent<BasePlayerCharacterEntity>();
                    var existing = root.GetComponent<MultiplayerARPG.Demo.DemoCharacterSize>();
                    if (player == null)
                    {
                        if (existing == null)
                            continue;
                        Object.DestroyImmediate(existing);
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                        continue;
                    }

                    float height = MeasureStandingHeight(root);
                    if (height <= 0.1f)
                    {
                        Debug.LogError($"[{nameof(DemoCharacterSizeBuilder)}] Could not measure \"{path}\"; " +
                                       "left it at whatever size it already had.");
                        continue;
                    }

                    MultiplayerARPG.Demo.DemoCharacterSize size = existing != null
                        ? existing
                        : root.AddComponent<MultiplayerARPG.Demo.DemoCharacterSize>();
                    size.standingHeight = height;
                    size.scales = ScalesFor(height);
                    size.sizeTitles = titles;
                    size.defaultSize = NearestStop(height);
                    size.anchors = AnchorsOf(root);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    written++;

                    report.Append("\n  ").Append(System.IO.Path.GetFileNameWithoutExtension(path))
                          .Append(" stands ").Append(height.ToString("F3", CultureInfo.InvariantCulture))
                          .Append("m, so the ramp runs x")
                          .Append(size.scales[0].ToString("F3", CultureInfo.InvariantCulture)).Append(" to x")
                          .Append(size.scales[size.scales.Length - 1].ToString("F3", CultureInfo.InvariantCulture))
                          .Append(", starting on ").Append(titles[size.defaultSize - 1]);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
            return written;
        }

        private static float[] ScalesFor(float height)
        {
            var scales = new float[Heights.Length];
            for (int i = 0; i < Heights.Length; ++i)
                scales[i] = Heights[i] / height;
            return scales;
        }

        /// <summary>
        /// Which stop a body is already closest to, 1-based. That is where the slider starts,
        /// so a player who never touches it gets the character the demo has always had - to
        /// within the two and a half centimetres that half a stop is worth.
        /// </summary>
        private static int NearestStop(float height)
        {
            int best = 1;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < Heights.Length; ++i)
            {
                float distance = Mathf.Abs(Heights[i] - height);
                if (distance >= bestDistance)
                    continue;
                bestDistance = distance;
                best = i + 1;
            }
            return best;
        }

        /// <summary>
        /// How far this body's crown stands above the entity's own origin, which is the
        /// ground it is placed on.
        ///
        /// Measured from the top of the mesh rather than its full extent on purpose: the
        /// bind pose the meshes are baked in has the soles a little below the origin, while
        /// the idle the character actually stands in puts them on it, so top-to-bottom would
        /// read about a tenth of a metre tall than the character ever looks. The rig agrees
        /// with the origin - the toe bone sits at 0.015 - so the crown above zero is the
        /// height.
        /// </summary>
        private static float MeasureStandingHeight(GameObject root)
        {
            float bare = 0f;
            float any = 0f;
            foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer.sharedMesh == null || !ActiveUnder(renderer.transform, root.transform))
                    continue;
                float top = TopOf(renderer, root.transform);
                any = Mathf.Max(any, top);
                if (renderer.name.StartsWith(BareMeshPrefix))
                    bare = Mathf.Max(bare, top);
            }
            // The fallback takes the hair with it and reads a couple of centimetres tall.
            // It is here so a renamed mesh gives a wrong number rather than none at all.
            if (bare <= 0.1f && any > 0.1f)
            {
                Debug.LogWarning($"[{nameof(DemoCharacterSizeBuilder)}] No \"{BareMeshPrefix}...\" meshes on " +
                                 $"\"{root.name}\"; measured over everything it wears instead.");
                return any;
            }
            return bare;
        }

        /// <summary>
        /// Whether a mesh is switched on, walked by hand rather than read off
        /// `activeInHierarchy`, because the prefab being measured is open in a preview scene
        /// rather than in the one on screen. It is the hairstyles this keeps out: a body
        /// carries all of them and switches on the one it is wearing.
        /// </summary>
        private static bool ActiveUnder(Transform child, Transform root)
        {
            Transform branch = child;
            while (branch != null)
            {
                if (!branch.gameObject.activeSelf)
                    return false;
                if (branch == root)
                    return true;
                branch = branch.parent;
            }
            return true;
        }

        /// <summary>
        /// The top of one skinned mesh, in the entity's space. Baked rather than read off
        /// <see cref="Renderer.bounds"/>, which for a skinned mesh is the animation bounds -
        /// padded, and padded by however much the longest clip needed.
        /// </summary>
        private static float TopOf(SkinnedMeshRenderer renderer, Transform root)
        {
            var baked = new Mesh();
            try
            {
                renderer.BakeMesh(baked, true);
                Bounds local = baked.bounds;
                if (local.size == Vector3.zero)
                    return root.InverseTransformPoint(renderer.bounds.max).y;
                float top = float.MinValue;
                for (int i = 0; i < 8; ++i)
                {
                    var corner = new Vector3(
                        (i & 1) == 0 ? local.min.x : local.max.x,
                        (i & 2) == 0 ? local.min.y : local.max.y,
                        (i & 4) == 0 ? local.min.z : local.max.z);
                    top = Mathf.Max(top, root.InverseTransformPoint(renderer.transform.TransformPoint(corner)).y);
                }
                return top;
            }
            finally
            {
                Object.DestroyImmediate(baked);
            }
        }

        /// <summary>
        /// The anchors that ride up and down with the body: the nameplate, the chat bubble
        /// and the damage numbers, which DemoEntityBuilder hangs off a "Transforms" child and
        /// places by the stock character's height. The minimap icon is in there too, at zero,
        /// where scaling leaves it.
        /// </summary>
        private static Transform[] AnchorsOf(GameObject root)
        {
            Transform transforms = root.transform.Find("Transforms");
            if (transforms == null)
            {
                Debug.LogWarning($"[{nameof(DemoCharacterSizeBuilder)}] No \"Transforms\" child on " +
                                 $"\"{root.name}\"; its nameplate will not move with its size.");
                return new Transform[0];
            }
            var anchors = new List<Transform>();
            foreach (Transform child in transforms)
                anchors.Add(child);
            return anchors.ToArray();
        }

        // ------------------------------------------------------------------
        // The create screen
        // ------------------------------------------------------------------

        /// <summary>
        /// The window goes directly above the skin slider, the two of them making the demo's
        /// own pair at the foot of the right column, and it is laid out to match it exactly:
        /// same height, readout on its own line under the title, full-width slider below.
        /// Where the column finds the room is <see cref="DemoCreateColumn.Restack"/>'s
        /// business.
        /// </summary>
        private const string WindowName = "Window--Size";
        private const float WindowHeight = 84f;

        /// <summary>The colour the slider's fill takes: the menu's own bronze.</summary>
        private static readonly Color SliderFill = DemoPalette.Header;

        private static bool BuildSlider()
        {
            GameObject canvas = PrefabUtility.LoadPrefabContents(CanvasHomePath);
            try
            {
                Transform create = DemoCreateColumn.FindDeep(canvas.transform, "UICharacterCreate");
                if (create == null)
                {
                    Debug.LogError($"[{nameof(DemoCharacterSizeBuilder)}] No UICharacterCreate in {CanvasHomePath}.");
                    return false;
                }

                GameObject window = DemoCreateColumn.Clone(create, "Window--Beard", WindowName, "Size", WindowHeight);
                if (window == null)
                    return false;

                // The skin window's own two rows, to the unit - it has a swatch on the left of
                // its readout and this has nothing, so the only difference is the inset.
                Text label = DemoCreateColumn.Label(window.transform, "SizeName",
                    new Vector2(10f, -52f), new Vector2(-8f, -32f), TextAnchor.MiddleLeft);
                Slider slider = DemoCreateColumn.Build(window.transform, "SizeSlider",
                    new Vector2(10f, -78f), new Vector2(-10f, -58f), SliderFill, Heights.Length);

                var driver = create.GetComponent<MultiplayerARPG.Demo.DemoSizeSlider>();
                if (driver == null)
                    driver = create.gameObject.AddComponent<MultiplayerARPG.Demo.DemoSizeSlider>();
                driver.slider = slider;
                driver.label = label;

                DemoCreateColumn.Restack(create);
                // The screen before this one, which is not this tool's business except that
                // its Back button has to agree with the one Restack has just moved.
                DemoCreateColumn.PlaceBackButton(DemoCreateColumn.FindDeep(canvas.transform, "UICharacterList"));
                PrefabUtility.SaveAsPrefabAsset(canvas, CanvasHomePath);
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(canvas);
            }
        }
    }
}
