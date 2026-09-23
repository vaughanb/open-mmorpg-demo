using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// Brings a Mixamo download onto the demo's characters and leaves behind a single
    /// `.anim` - **for local use only; it cannot ship.**
    ///
    /// Adobe lets a Mixamo animation go out inside a finished game, but not as a raw file
    /// in an engine template or an asset-store package, and the demo is both. The six
    /// clips this made in September sat in `Demo/Animations` until 2026-09-23 and had to
    /// come out; the skills now play CC0 clips from the two Quaternius libraries. Output
    /// goes to <see cref="ClipDir"/>, outside the kit, and `DemoAnimationSet.Clip` looks
    /// there last - so pointing a skill at a Mixamo clip in your own copy works, and
    /// `Verify Demo Is Self-Contained` then reports it as an outside dependency, which is
    /// the guard that keeps it from being shipped by accident.
    ///
    /// Three things have to happen to a Mixamo file for this rig, and each of them fails
    /// silently if it does not:
    ///
    /// 1. **Avatar Definition must be Create From This Model.** Copy From Other Avatar
    ///    against the library avatar imports **zero** clips - the copied description
    ///    carries UAL1's `Armature/root/pelvis` hierarchy and a Mixamo round trip has no
    ///    `Armature` node, so the take is dropped. Unity says so once, at import, and
    ///    never again.
    /// 2. **The download carries the whole library back with it.** These characters were
    ///    uploaded to Mixamo, so Mixamo returns them with their existing 43 takes plus the
    ///    new one: 44 takes, of which 43 are uncorrected duplicates of clips the demo
    ///    already has. Only the take named `mixamo.com` is wanted.
    /// 3. **Facing has to be measured, not assumed.** The demo's library is normalised
    ///    with Bake Into Pose + Based Upon Original + 180; a Mixamo clip authored facing
    ///    somewhere else needs its own offset, and the archery pair needed +90. So each
    ///    entry below carries an offset, and this tool reports the residual body yaw after
    ///    applying it. Set the offset, run again, and read the residual: near zero is
    ///    right.
    ///
    /// The FBXs are staged outside the kit (<see cref="StagingDir"/>) because they are
    /// ~31MB each - they include the skinned mesh, which a "without skin" download would
    /// not - and only the extracted clip belongs in the demo.
    /// </summary>
    public static class DemoMixamoImport
    {
        /// <summary>
        /// Where the downloads sit. Outside `Assets/OpenMMORPG` on purpose: these are
        /// source files for the demo, not part of it, and they are thirty times the size
        /// of what comes out of them.
        /// </summary>
        public const string StagingDir = "Assets/Animations/Mixamo";

        /// <summary>
        /// Where the extracted clip lands: **outside the kit**, beside the downloads. It was
        /// `Demo/Animations` until 2026-09-23, which is how Mixamo data got into the folder
        /// that ships. The hand-edited September clips were moved here with their ids
        /// intact, and the import still never overwrites a clip that is already here.
        /// </summary>
        public const string ClipDir = "Assets/Animations/Mixamo/Edited";

        /// <summary>The take a Mixamo download names its own animation. The other 43 are ours, coming home.</summary>
        private const string MixamoTake = "mixamo.com";

        private const string ProbeModel = "Assets/OpenMMORPG/Demo/Prefabs/GamePlay/CharacterModels/PlayerCharacterModel_Male.prefab";

        private struct Download
        {
            /// <summary>The file as Mixamo named it, without extension.</summary>
            public string File;
            /// <summary>What the clip is called once it is ours. Must not collide with a library clip name.</summary>
            public string Name;
            /// <summary>
            /// Rigid yaw applied through Bake Into Pose. Measured, not guessed - run the
            /// tool and read the residual it logs. The sign is not intuitive: the archery
            /// clips needed +90 where -90 rotated the wrong way.
            /// </summary>
            public float OffsetY;
            /// <summary>Whether it is a held pose rather than a one-shot.</summary>
            public bool Loop;
        }

        /// <summary>
        /// Measured, on the demo's own male model, through the PlayableGraph probe below.
        /// Body yaw off `transform.forward`, positive being bladed to the character's left:
        ///
        /// <code>
        ///   the demo's existing stances       start    mid
        ///     Spell_Double_Idle_Loop          +11.5   +11.5
        ///     Idle_Loop / Celebration         +13.8   +13.8
        ///     Sword_Idle                      +24.5   +24.5
        ///     Sword_Attack_Standing           +24.5   -25.1
        ///     Spell_Simple_Idle_Loop          +41.9   +41.9
        ///
        ///   the downloads, before any offset
        ///     Sword_Shield_Attack              +9.0   -20.2
        ///     Spell_2H_Cast                   +55.7    +5.0
        ///     Spell_2H_Attack                 +55.7    -6.8
        ///     Sword_Shield_PowerUp            +55.2   +63.1
        ///     Sword_Shield_Block              +56.8   +68.9
        /// </code>
        ///
        /// Two different answers come out of that, and the difference is whether the clip
        /// has an action in it.
        ///
        /// **The three that act are left alone.** `Sword_Shield_Attack` already lands
        /// inside the house range, and the two spells wind up wide at +55.7 but come round
        /// to +5.0 and -6.8 at the moment the spell goes off - square, which is where it
        /// matters, and the same shape as `Sword_Attack_Standing` swinging from +24.5
        /// through -25.1. An offset rotates the WHOLE clip rigidly, so squaring the
        /// wind-up would drag the release out to -37: it would trade a wide half-second of
        /// preparation for a crooked cast on the frame the player is actually reading.
        ///
        /// **The two that are held do get one.** A block and a flourish have no action to
        /// protect and sit on screen at +55 to +69 - thirty degrees more bladed than
        /// anything else the demo puts a character in, so entering one from `Sword_Idle`
        /// would lurch. The offset lands them on `Sword_Idle`'s own +24.5.
        /// </summary>
        /// <summary>
        /// Named for the skill each one drives rather than for the file Mixamo shipped.
        /// Three of them were renamed by hand in the editor first, which is fine - a
        /// rename keeps the GUID - but this table has to follow, or the next run writes
        /// the old name back as a second asset beside the renamed one and the skill keeps
        /// pointing at whichever it was given.
        /// </summary>
        private static readonly Download[] Downloads =
        {
            new Download { File = "Sword And Shield Attack", Name = "Cleave", OffsetY = 0f },
            new Download { File = "Standing 2H Cast Spell 01", Name = "Spell_2H_Cast", OffsetY = 0f },
            new Download { File = "Standing 2H Magic Attack 01", Name = "Spell_2H_Attack", OffsetY = 0f },
            // Positive, and it is worth saying why, because the obvious sign is wrong and
            // costs a round trip every time: the measured yaw comes out as
            // `authored - offset`, so a stance bladed at +55 needs a POSITIVE offset to
            // come back to +24. Tried at -32 first, which took it to +87.
            new Download { File = "Sword And Shield Power Up", Name = "Rallying_Cry", OffsetY = 32f },
            // Mixamo calls it a block and it drives Shield Bash: a braced shield going
            // forward reads either way. Not looped - it is a blow, not a stance held up.
            new Download { File = "Sword And Shield Block", Name = "Shield_Bash", OffsetY = 32f },
            // The warrior's Charge. A locomotion cycle rather than a one-shot, so it
            // loops: the dash runs on the force applier's own timing and the run has to
            // still be playing when the character arrives.
            new Download { File = "Sprint", Name = "Charge", OffsetY = 0f, Loop = true },
        };

        [MenuItem("Open MMORPG/Demo/Import Mixamo Animations")]
        public static void ImportAll()
        {
            if (!AssetDatabase.IsValidFolder(StagingDir))
            {
                Debug.LogError($"[{nameof(DemoMixamoImport)}] No {StagingDir}. Put the downloads there first.");
                return;
            }
            DemoItemBuilder.EnsureFolder(ClipDir);

            var report = new System.Text.StringBuilder();
            var kept = new List<string>();
            foreach (Download download in Downloads)
            {
                // Never overwrite a clip that is already here. These get hand-adjusted
                // after extraction - the three sword clips were edited to start on
                // `Sword_Idle`'s +24.5 exactly, and that work lives in the `.anim` and
                // nowhere else, because the FBX it came from is normally deleted once the
                // clip is out. An importer that re-extracted on every run would throw that
                // away silently and the only symptom would be a stance that drifted back.
                //
                // To genuinely redo one, delete its `.anim` first and run again.
                if (AssetDatabase.LoadAssetAtPath<AnimationClip>($"{ClipDir}/{download.Name}.anim") != null)
                {
                    kept.Add(download.Name);
                    continue;
                }

                string path = $"{StagingDir}/{download.File}.fbx";
                if (AssetDatabase.LoadAssetAtPath<Object>(path) == null)
                {
                    Debug.LogError($"[{nameof(DemoMixamoImport)}] Missing \"{path}\".");
                    continue;
                }
                if (!Configure(path, download))
                    continue;
                AnimationClip clip = Extract(path, download);
                if (clip == null)
                    continue;
                report.AppendLine($"  {download.Name}: {clip.length:0.00}s, offset {download.OffsetY:0}deg, " +
                                  $"residual body yaw {Facing(clip):+0.0;-0.0}deg");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            string keptLine = kept.Count == 0 ? string.Empty
                : $"Kept (already extracted, not touched): {string.Join(", ", kept)}.\n" +
                  "Delete a clip's .anim if you really want it re-extracted.\n";
            Debug.Log($"[{nameof(DemoMixamoImport)}] {ClipDir}:\n{report}{keptLine}" +
                      "Residual yaw is the body's angle off transform.forward at clip start. " +
                      "Anything much above a few degrees wants a different offset - unless the clip " +
                      "is authored bladed, which a stance legitimately is.");
        }

        /// <summary>
        /// Sets the rig, throws away the 43 takes that came home with the download, and
        /// applies the orientation correction.
        /// </summary>
        private static bool Configure(string path, Download download)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
            {
                Debug.LogError($"[{nameof(DemoMixamoImport)}] \"{path}\" is not a model.");
                return false;
            }

            // Create From This Model, never Copy From Other Avatar - see the class note.
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true;
            // Nothing here needs the materials; the mesh cannot be dropped through the
            // importer, which is the other half of why these stage outside the kit.
            importer.materialImportMode = ModelImporterMaterialImportMode.None;

            TakeInfo take = default;
            bool found = false;
            foreach (TakeInfo candidate in importer.importedTakeInfos)
            {
                if (candidate.name != MixamoTake)
                    continue;
                take = candidate;
                found = true;
                break;
            }
            if (!found)
            {
                Debug.LogError($"[{nameof(DemoMixamoImport)}] \"{path}\" has no take named \"{MixamoTake}\", so " +
                               "there is nothing in it that did not come from our own library. Is it a Mixamo file?");
                return false;
            }

            var clip = new ModelImporterClipAnimation
            {
                takeName = take.name,
                name = download.Name,
                firstFrame = take.startTime * take.sampleRate,
                lastFrame = take.stopTime * take.sampleRate,
                loopTime = download.Loop,
                // Bake Into Pose, Based Upon **Original**, plus a rigid offset. Body
                // Orientation was tried on the library and re-estimates forward per clip,
                // which produced a visible diagonal run; Original preserves what the
                // animator authored and the offset is a clean half or quarter turn.
                lockRootRotation = true,
                keepOriginalOrientation = true,
                rotationOffset = download.OffsetY,
                // Height is left alone: these are standing actions, and locking Y would
                // flatten the crouch in the block.
                lockRootHeightY = false,
                keepOriginalPositionY = true,
            };
            importer.clipAnimations = new[] { clip };
            importer.SaveAndReimport();
            return true;
        }

        /// <summary>
        /// Copies the one clip out of the FBX into its own asset.
        ///
        /// The `.anim` carries `m_LoopBlendOrientation` / `m_KeepOriginalOrientation` /
        /// `m_OrientationOffsetY` with it, so the correction set on the importer survives
        /// the extraction - which is what lets the 31MB source be thrown away afterwards.
        /// </summary>
        private static AnimationClip Extract(string path, Download download)
        {
            AnimationClip source = null;
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                var candidate = asset as AnimationClip;
                if (candidate == null || candidate.name.StartsWith("__"))
                    continue;
                source = candidate;
                break;
            }
            if (source == null)
            {
                Debug.LogError($"[{nameof(DemoMixamoImport)}] \"{path}\" imported no clip. If the rig is set to " +
                               "Copy From Other Avatar this is exactly what that looks like.");
                return null;
            }

            string outPath = $"{ClipDir}/{download.Name}.anim";
            var copy = Object.Instantiate(source);
            copy.name = download.Name;
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(outPath);
            if (existing != null)
                EditorUtility.CopySerialized(copy, existing);
            else
                AssetDatabase.CreateAsset(copy, outPath);
            if (existing != null)
                Object.DestroyImmediate(copy);
            AssetDatabase.ImportAsset(outPath, ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadAssetAtPath<AnimationClip>(outPath);
        }

        /// <summary>
        /// The body's yaw off `transform.forward` at the start of the clip, in degrees.
        ///
        /// Two things make this trustworthy where the obvious methods are not.
        ///
        /// It evaluates through a **PlayableGraph**, because that is the animation runtime
        /// and it is what applies `keepOriginalOrientation` / `orientationOffsetY`.
        /// `clip.SampleAnimation` and `AnimationMode` bypass the runtime and show every
        /// corrected clip at its raw authored angle, which reads as a bug that is not
        /// there.
        ///
        /// And it measures from the **hip line**, `LeftUpperLeg` to `RightUpperLeg`. Those
        /// bone positions are fixed relative to the pelvis, so the line reads pelvis yaw
        /// exactly. The shoulder line is useless: arms counter-rotate through a stride and
        /// have been measured 43 degrees off the hips on the same frame.
        ///
        /// Read at clip **start**. Mid-clip is meaningless for anything that turns while
        /// it plays, and for a one-shot the start is the only frame that has not begun.
        /// </summary>
        public static float Facing(AnimationClip clip)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ProbeModel);
            if (prefab == null)
            {
                Debug.LogWarning($"[{nameof(DemoMixamoImport)}] No probe model at {ProbeModel}; cannot measure facing.");
                return float.NaN;
            }
            GameObject instance = Object.Instantiate(prefab);
            try
            {
                var animator = instance.GetComponentInChildren<Animator>();
                if (animator == null || animator.avatar == null || !animator.isHuman)
                    return float.NaN;

                PlayableGraph graph = PlayableGraph.Create("DemoMixamoImport.Facing");
                try
                {
                    AnimationPlayableOutput output = AnimationPlayableOutput.Create(graph, "out", animator);
                    output.SetSourcePlayable(AnimationClipPlayable.Create(graph, clip));
                    graph.Evaluate(0.01f);
                }
                finally
                {
                    graph.Destroy();
                }

                Transform left = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
                Transform right = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
                if (left == null || right == null)
                    return float.NaN;

                Vector3 hips = right.position - left.position;
                Vector3 forward = Vector3.Cross(hips, Vector3.up).normalized;
                return Vector3.SignedAngle(instance.transform.forward, forward, Vector3.up);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>
        /// Deletes the staged downloads once their clips are extracted. Separate and
        /// manual, because it throws away 150MB that cannot be regenerated without going
        /// back to Mixamo.
        /// </summary>
        [MenuItem("Open MMORPG/Demo/Discard Staged Mixamo FBXs")]
        public static void DiscardStaged()
        {
            var missing = new List<string>();
            foreach (Download download in Downloads)
            {
                if (AssetDatabase.LoadAssetAtPath<AnimationClip>($"{ClipDir}/{download.Name}.anim") == null)
                    missing.Add(download.Name);
            }
            if (missing.Count > 0)
            {
                Debug.LogError($"[{nameof(DemoMixamoImport)}] Not discarding anything: {string.Join(", ", missing)} " +
                               "has no extracted clip yet. Run Import Mixamo Animations first.");
                return;
            }
            if (!EditorUtility.DisplayDialog("Discard staged Mixamo FBXs?",
                    $"Every clip is extracted into {ClipDir}. This deletes the {Downloads.Length} source FBXs " +
                    "(~150MB). They can only be recovered from Mixamo.", "Delete", "Keep"))
                return;
            foreach (Download download in Downloads)
                AssetDatabase.DeleteAsset($"{StagingDir}/{download.File}.fbx");
            AssetDatabase.Refresh();
            Debug.Log($"[{nameof(DemoMixamoImport)}] Discarded the staged FBXs.");
        }
    }
}
