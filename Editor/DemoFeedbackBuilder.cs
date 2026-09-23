using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// The small pieces of in-world feedback the kit's template left broken: damage numbers,
    /// the level-up flourish, the click-to-move destination marker, and the signs that hang
    /// over a character (safe area, vending).
    ///
    /// All four came across from the kit's template pointing at assets that were never
    /// brought over, and all four failed quietly:
    ///
    /// - **Damage numbers never appeared.** Each of the six combat-text prefabs keeps its text
    ///   in a `CanvasGroup` at alpha 0 and leaves it to an animator to fade it in - an animator
    ///   whose controller did not exist. So every hit, miss, crit and heal was spawned, pooled
    ///   and put away again without ever being visible.
    /// - **The level-up flourish stood still**, its controller missing the same way: a flat
    ///   "LEVEL UP!" at chest height for two seconds.
    /// - **The destination marker was a legacy `Projector`** with a missing material, and URP
    ///   does not draw Projectors at all. The demo's controller had been left without one.
    /// - **The safe-area sign lay at the character's feet** (see <see cref="DemoNameplateSign"/>).
    ///
    /// Everything here writes into existing prefabs in place, so GUIDs and every reference to
    /// them survive, and draws its one texture only into a gap.
    /// </summary>
    public static class DemoFeedbackBuilder
    {
        private const string DemoDir = "Assets/OpenMMORPG/Demo";
        private const string AnimationDir = DemoDir + "/Animations/UI";
        private const string CombatTextDir = DemoDir + "/Prefabs/UI/GamePlay/CombatText";
        private const string LevelUpPath = DemoDir + "/Prefabs/Effects/LevelUpEffect.prefab";
        internal const string MarkerPath = DemoDir + "/Prefabs/GamePlay/TargetObject.prefab";
        private const string MarkerTexturePath = DemoDir + "/Textures/DestinationRing.png";
        private const string PlayerControllerPath = DemoDir + "/Prefabs/GamePlay/DemoPlayerController.prefab";
        private const string SafeAreaSignPath = DemoDir + "/Prefabs/GamePlay/RelatesObjects/CanvasIsInSafeArea.prefab";
        private const string VendingSignPath = DemoDir + "/Prefabs/GamePlay/RelatesObjects/OwningCharacter/CanvasVendingSign.prefab";

        [MenuItem("Open MMORPG/Demo/Build Feedback Effects")]
        public static void BuildAll()
        {
            DemoItemBuilder.EnsureFolder(AnimationDir);
            int texts = BuildCombatText();
            BuildLevelUp();
            BuildDestinationMarker();
            RaiseSign(SafeAreaSignPath, SafeAreaSignOffset, SafeAreaSignSize);
            RaiseSign(VendingSignPath, VendingSignOffset);
            AssetDatabase.SaveAssets();
            Debug.Log($"[{nameof(DemoFeedbackBuilder)}] Built the combat text ({texts} prefabs), the level-up " +
                      "flourish and the destination marker, and hung the safe-area and vending signs over the nameplate.");
        }

        // ---- damage numbers ----------------------------------------------------

        /// <summary>
        /// Pops in, rises and fades - about a second and a third, inside the two seconds the
        /// kit keeps a combat text out of its pool before putting it back.
        ///
        /// The animator sits on each prefab's `Text` child, so the curves bind to that
        /// object's own components. A pooled text is switched off and on again between uses,
        /// and an animator that does not keep its state on disable starts its clip over, so
        /// every reuse plays it from the beginning.
        /// </summary>
        private static int BuildCombatText()
        {
            AnimationClip clip = Clip($"{AnimationDir}/CombatText.anim");
            SetCurve(clip, "", typeof(CanvasGroup), "m_Alpha",
                Linear(new Vector2(0f, 0f), new Vector2(0.08f, 1f), new Vector2(0.85f, 1f), new Vector2(1.3f, 0f)));
            // Canvas units; the root is scaled with distance, so this is a steady rise on screen.
            SetCurve(clip, "", typeof(RectTransform), "m_AnchoredPosition.y", EaseOut(0f, 0f, 1.3f, 70f));
            AnimationCurve pop = Linear(new Vector2(0f, 1.5f), new Vector2(0.12f, 1f), new Vector2(1.3f, 1f));
            SetCurve(clip, "", typeof(RectTransform), "m_LocalScale.x", pop);
            SetCurve(clip, "", typeof(RectTransform), "m_LocalScale.y", pop);
            AnimatorController controller = Controller($"{AnimationDir}/CombatText.controller", clip);

            int done = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { CombatTextDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    foreach (Animator animator in root.GetComponentsInChildren<Animator>(true))
                        animator.runtimeAnimatorController = controller;
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    ++done;
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
            return done;
        }

        // ---- level up ----------------------------------------------------------

        /// <summary>
        /// The "LEVEL UP!" label swells in at chest height, rises over the head and fades, in
        /// the two seconds the effect lives. The animator is on the effect's root and the
        /// label is its `Canvas` child, sized in world units at 0.025 to the canvas unit.
        /// The particles are left as they are: the kit's streaks rising through the label.
        /// </summary>
        private static void BuildLevelUp()
        {
            AnimationClip clip = Clip($"{AnimationDir}/LevelUp.anim");
            SetCurve(clip, "Canvas", typeof(CanvasGroup), "m_Alpha",
                Linear(new Vector2(0f, 0f), new Vector2(0.15f, 1f), new Vector2(1.5f, 1f), new Vector2(2f, 0f)));
            SetCurve(clip, "Canvas", typeof(RectTransform), "m_AnchoredPosition.y", EaseOut(0f, 1.5f, 2f, 2.6f));
            AnimationCurve swell = Linear(new Vector2(0f, 0.04f), new Vector2(0.2f, 0.025f), new Vector2(2f, 0.025f));
            SetCurve(clip, "Canvas", typeof(RectTransform), "m_LocalScale.x", swell);
            SetCurve(clip, "Canvas", typeof(RectTransform), "m_LocalScale.y", swell);
            AnimatorController controller = Controller($"{AnimationDir}/LevelUp.controller", clip);

            GameObject root = PrefabUtility.LoadPrefabContents(LevelUpPath);
            try
            {
                Animator animator = root.GetComponent<Animator>();
                if (animator == null)
                {
                    Debug.LogError($"[{nameof(DemoFeedbackBuilder)}] {LevelUpPath} has no Animator on its root.");
                    return;
                }
                animator.runtimeAnimatorController = controller;
                PrefabUtility.SaveAsPrefabAsset(root, LevelUpPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ---- destination marker ------------------------------------------------

        private static readonly Color MarkerColour = new Color(1f, 0.86f, 0.45f, 0.9f);

        private const float MarkerDiameter = 0.9f;

        /// <summary>
        /// A ring on the ground where a click-to-move is headed.
        ///
        /// The kit's marker was a legacy `Projector` - which URP never draws - with a missing
        /// material, over a green navigation arrow lying flat. It is now a flat sprite ring
        /// with no collider, so it cannot catch the next click, tilted to the slope under it
        /// by <see cref="DemoGroundMarker"/> - level, it sank half into any hillside. It is only ever seen with
        /// the click-to-move setting on: with it off, the demo's controller cancels ground
        /// clicks and the kit hides the marker while there is no destination.
        /// </summary>
        private static void BuildDestinationMarker()
        {
            Sprite ring = RingSprite();
            GameObject root = PrefabUtility.LoadPrefabContents(MarkerPath);
            try
            {
                for (int i = root.transform.childCount - 1; i >= 0; --i)
                    Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
                if (root.GetComponent<DemoGroundMarker>() == null)
                    root.AddComponent<DemoGroundMarker>();
                var ringObject = new GameObject("Ring");
                ringObject.transform.SetParent(root.transform, false);
                // Lifted a little so it sits on the ground rather than in it.
                ringObject.transform.localPosition = new Vector3(0f, 0.05f, 0f);
                ringObject.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                ringObject.transform.localScale = Vector3.one * MarkerDiameter;
                var renderer = ringObject.AddComponent<SpriteRenderer>();
                renderer.sprite = ring;
                renderer.color = MarkerColour;
                renderer.sharedMaterial = AssetDatabase.GetBuiltinExtraResource<Material>("Sprites-Default.mat");
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                PrefabUtility.SaveAsPrefabAsset(root, MarkerPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            // The controller builder points at the same prefab; this covers a controller that
            // was built before the marker was.
            var controllerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerControllerPath);
            var controller = controllerPrefab != null ? controllerPrefab.GetComponent<PlayerCharacterController>() : null;
            if (controller == null)
                return;
            var serialized = new SerializedObject(controller);
            serialized.FindProperty("targetObjectPrefab").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<GameObject>(MarkerPath);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(controllerPrefab);
        }

        /// <summary>
        /// A white ring on transparency, tinted by the renderer. Drawn only when the file is
        /// missing, so a hand-drawn replacement at the same path is kept.
        /// </summary>
        private static Sprite RingSprite()
        {
            const int size = 128;
            if (!File.Exists(MarkerTexturePath))
            {
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                float centre = (size - 1) * 0.5f;
                float half = size * 0.5f;
                var pixels = new Color32[size * size];
                for (int y = 0; y < size; ++y)
                {
                    for (int x = 0; x < size; ++x)
                    {
                        float r = Mathf.Sqrt((x - centre) * (x - centre) + (y - centre) * (y - centre)) / half;
                        // A band from 0.74 to 0.94 of the radius with a pixel and a half of
                        // soft edge either side, over a faint disc so the centre still reads.
                        float edge = 1.5f / half;
                        float band = Mathf.Clamp01((r - 0.74f) / edge + 0.5f) * Mathf.Clamp01((0.94f - r) / edge + 0.5f);
                        float fill = r < 0.74f ? 0.12f : 0f;
                        float alpha = Mathf.Max(band, fill);
                        pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                    }
                }
                texture.SetPixels32(pixels);
                File.WriteAllBytes(MarkerTexturePath, texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(MarkerTexturePath);
            }
            var importer = (TextureImporter)AssetImporter.GetAtPath(MarkerTexturePath);
            if (importer.textureType != TextureImporterType.Sprite || importer.spritePixelsPerUnit != size)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = size;
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(MarkerTexturePath);
        }

        // ---- signs over the nameplate ------------------------------------------

        /// <summary>
        /// Just over the head. Seen from the default camera, which looks down steeply, a sign
        /// half a metre higher already reads as belonging to whoever stands behind.
        /// </summary>
        private static readonly Vector3 SafeAreaSignOffset = new Vector3(0f, 0.3f, 0f);

        /// <summary>The shield's width in metres; the template's was 0.64m, as wide as the shoulders.</summary>
        private const float SafeAreaSignSize = 0.4f;

        /// <summary>Above the safe-area shield, since a stall is mostly set up in town.</summary>
        private static readonly Vector3 VendingSignOffset = new Vector3(0f, 0.8f, 0f);

        /// <summary>
        /// Puts a <see cref="DemoNameplateSign"/> on the sign's root and takes away any
        /// `FollowBone`, which cannot find the model's Animator from where the kit spawns the
        /// sign and only leaves it on the ground. With a size, the sign's `Sign` child is
        /// scaled to that width.
        ///
        /// The content is also pulled to the centre of the sign's canvas. The vending sign's
        /// template held its title 200 canvas units - two metres - up inside the canvas, the
        /// only thing lifting it off the feet; but the billboard turns the canvas about its
        /// root, so from the demo's steep camera that offset swung the title a metre to one
        /// side. The height now comes from the nameplate alone and the billboard turns the
        /// sign about its own middle.
        /// </summary>
        private static void RaiseSign(string path, Vector3 offset, float size = 0f)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach (UtilsComponents.FollowBone follow in root.GetComponentsInChildren<UtilsComponents.FollowBone>(true))
                    Object.DestroyImmediate(follow);
                foreach (Transform child in root.transform)
                {
                    if (child is RectTransform content)
                        content.anchoredPosition = Vector2.zero;
                }
                var sign = root.GetComponent<DemoNameplateSign>();
                if (sign == null)
                    sign = root.AddComponent<DemoNameplateSign>();
                sign.offset = offset;
                var image = root.transform.Find("Sign") as RectTransform;
                if (size > 0f && image != null && image.rect.width > 0f)
                    image.localScale = Vector3.one * (size / image.rect.width);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ---- the pieces they share ---------------------------------------------

        /// <summary>The clip at the path, emptied for rewriting, or a new one.</summary>
        private static AnimationClip Clip(string path)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null)
            {
                clip = new AnimationClip { frameRate = 60f };
                AssetDatabase.CreateAsset(clip, path);
            }
            clip.ClearCurves();
            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
            return clip;
        }

        private static void SetCurve(AnimationClip clip, string path, System.Type type, string property, AnimationCurve curve)
        {
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, type, property), curve);
        }

        /// <summary>The controller at the path playing the clip as its one state, created if missing.</summary>
        private static AnimatorController Controller(string path, AnimationClip clip)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null)
                return AnimatorController.CreateAnimatorControllerAtPathWithClip(path, clip);
            AnimatorState state = controller.layers[0].stateMachine.defaultState;
            if (state == null)
                state = controller.layers[0].stateMachine.AddState(clip.name);
            state.motion = clip;
            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static AnimationCurve Linear(params Vector2[] points)
        {
            var curve = new AnimationCurve();
            foreach (Vector2 point in points)
                curve.AddKey(point.x, point.y);
            for (int i = 0; i < curve.length; ++i)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
            }
            return curve;
        }

        /// <summary>Fast at the start and settling at the end: a quadratic ease-out.</summary>
        private static AnimationCurve EaseOut(float t0, float v0, float t1, float v1)
        {
            float slope = 2f * (v1 - v0) / (t1 - t0);
            return new AnimationCurve(new Keyframe(t0, v0, 0f, slope), new Keyframe(t1, v1, 0f, 0f));
        }
    }
}
