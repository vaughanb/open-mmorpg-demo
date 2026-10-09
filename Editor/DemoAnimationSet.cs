using MultiplayerARPG.GameData.Model.Playables;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// Maps the Quaternius Universal Animation Library onto the kit's playable
    /// animation states.
    ///
    /// The library is a humanoid clip set with no controller and no per-character
    /// rig, so every character in the demo — players, townsfolk, bandits — shares
    /// these clips and only differs in which weapon set it overrides.
    ///
    /// Jog and crouch are wired eight ways and crawl four, from the re-exported library.
    /// Walk and swim still repeat one clip in every direction because the library has no
    /// directional clips for them - see <see cref="SprintMoves"/> for why a substitute is
    /// worse there than the repeat.
    /// </summary>
    public static class DemoAnimationSet
    {
        public const string LibraryPath = "Assets/Plugins/Quaternius/Animations/UAL1_Fixed.fbx";

        /// <summary>
        /// Quaternius's Universal Animation Library 2 (CC0, 2026): 134 clips on the same
        /// universal rig as UAL1, so they drive the demo's bodies with no retargeting. Added
        /// on 2026-09-23 to replace the six Mixamo skill clips, which could not ship - Adobe
        /// allows Mixamo in a finished game but not as raw files in an engine template.
        ///
        /// **Imported with UAL1's exact clip settings**: humanoid, own avatar, every clip
        /// turned 180 degrees with its original orientation and height kept, looping only
        /// where the name ends `_Loop`. Measured before choosing that: a UAL2 idle and a
        /// UAL1 idle sampled raw on the same body face the same way to two decimals, so it
        /// needs UAL1's turn, not a new one. The in-place export (`UAL2.fbx`), not
        /// `UAL2_RM.fbx` - the kit moves characters itself and every clip here is in place.
        /// </summary>
        public const string SecondLibraryPath = "Assets/Plugins/Quaternius/Animations/UAL2/UAL2.fbx";

        /// <summary>
        /// Every source library, in priority order. The two share exactly one clip name,
        /// `A_TPose`, and it resolves to UAL1; nothing the demo plays is affected.
        /// </summary>
        public static readonly string[] LibraryPaths = { LibraryPath, SecondLibraryPath };

        /// <summary>
        /// Every animation the demo owns, in one folder: the clips extracted from the two
        /// Quaternius libraries and the bow shot generated here. Anything the libraries do
        /// not cover can be dropped in - **if it is CC0**, because this folder ships inside
        /// the kit. Mixamo clips lived here until 2026-09-23 and had to come out; see
        /// <see cref="SecondLibraryPath"/>.
        ///
        /// This used to be two places - extracted clips under `Demo/Art/Animations` with
        /// the demo's own beside them in `Demo/Animations` - which meant neither folder
        /// answered "where are the animations". It is the same folder as
        /// <see cref="DemoArtCollector.ClipDir"/> now, and that is the point.
        /// </summary>
        private const string ExtraAnimationDir = DemoArtCollector.ClipDir;

        private const string WeaponTypeDir = "Assets/OpenMMORPG/Demo/GameData/Resources/WeaponTypes";

        /// <summary>
        /// Where the joined bow shot is written. Under Demo because it is generated here and
        /// the demo is what ships; the two clips it is made of stay where they were authored.
        /// </summary>
        private const string MadeAnimationDir = "Assets/OpenMMORPG/Demo/Animations";

        private static AnimationClip[] _clips;

        /// <summary>
        /// Clips the demo holds a character in that the library does not flag as looping.
        ///
        /// The library's convention is a `_Loop` suffix, and `Sword_Idle` does not have
        /// one - but it is the idle for the whole sword and axe set, so a swordsman stands
        /// in it indefinitely. An `AnimationClipPlayable` loops only if the clip itself
        /// says to, so unflagged it plays its 1.67s once and then holds its last frame
        /// forever. In game that hides behind every other state change; on the character
        /// screens, where the character does nothing else, it is the whole of what you see.
        ///
        /// Safe to force: measured start against end across all 52 bones, `Sword_Idle`
        /// closes on itself to 0.6 degrees, which is the same order as the clips the
        /// library does flag (`Idle_Loop` 0.4, `Spell_Simple_Idle_Loop` 0.5). It is a
        /// clean cycle that was simply never marked as one.
        ///
        /// Audited the other way too: of the 38 distinct clips the built models use in a
        /// state that must loop, this is the only one missing the flag.
        /// </summary>
        private static readonly string[] MustLoop = { "Sword_Idle" };

        /// <summary>
        /// Sets the loop flag on <see cref="MustLoop"/>, at the library importer so every
        /// future extraction inherits it, and on the already-extracted copy so the demo is
        /// right without waiting for a re-collect.
        /// </summary>
        /// <summary>A library clip cut short, imported as a clip of its own.</summary>
        private struct TrimmedClip
        {
            public string Name;
            public string Source;
            public float Seconds;
        }

        /// <summary>
        /// Clips the demo plays shorter than they were authored.
        ///
        /// **Trimmed, not sped up, because a skill's clip speed is applied twice.** The
        /// kit's use-skill component passes the clip's `animSpeedRate` on to
        /// `PlayActionAnimation` as the play-speed multiplier, and the playable then
        /// multiplies it by the same `animSpeedRate` again - so a clip set to 2x plays at
        /// 4x while the skill's own timing runs at 2x. Measured on 2026-09-23: Rallying
        /// Cry at 2x showed its arms up for 0.15s of a 2s action and stood idle for the
        /// rest; at 1x the same clip played exactly as authored. A shorter clip at 1x has
        /// no such trap.
        /// </summary>
        private static readonly TrimmedClip[] Trimmed =
        {
            // UAL1's `Celebration` is 4s: arms up from 0.3s to 1.6s, then 2.4s lowering them.
            // The shout needs the first part; 2.2s keeps the whole gesture and the start of
            // the lowering, and the blend back to idle does the rest.
            new TrimmedClip { Name = "Celebration_Rally", Source = "Celebration", Seconds = 2.2f },
        };

        /// <summary>
        /// Adds each trimmed clip to its library's import as an extra clip on the same take,
        /// copying the source clip's settings, so it resolves by name like any other and the
        /// art collector extracts it into the demo. Reimports only when something changed -
        /// UAL1 is a 64MB file.
        /// </summary>
        public static void EnsureTrimmedClips()
        {
            foreach (string library in LibraryPaths)
            {
                var importer = AssetImporter.GetAtPath(library) as ModelImporter;
                if (importer == null)
                    continue;
                var clips = new List<ModelImporterClipAnimation>(importer.clipAnimations);
                if (clips.Count == 0)
                    clips.AddRange(importer.defaultClipAnimations);
                bool changed = false;
                foreach (TrimmedClip trim in Trimmed)
                {
                    ModelImporterClipAnimation source = clips.Find(c => c.name == trim.Source);
                    if (source == null)
                        continue;
                    float lastFrame = source.firstFrame + trim.Seconds * 30f;
                    ModelImporterClipAnimation existing = clips.Find(c => c.name == trim.Name);
                    if (existing != null && Mathf.Approximately(existing.lastFrame, lastFrame))
                        continue;
                    if (existing != null)
                        clips.Remove(existing);
                    var copy = new ModelImporterClipAnimation
                    {
                        name = trim.Name,
                        takeName = source.takeName,
                        firstFrame = source.firstFrame,
                        lastFrame = lastFrame,
                        rotationOffset = source.rotationOffset,
                        keepOriginalOrientation = source.keepOriginalOrientation,
                        keepOriginalPositionY = source.keepOriginalPositionY,
                        keepOriginalPositionXZ = source.keepOriginalPositionXZ,
                        lockRootRotation = source.lockRootRotation,
                        lockRootHeightY = source.lockRootHeightY,
                        lockRootPositionXZ = source.lockRootPositionXZ,
                        heightFromFeet = source.heightFromFeet,
                        loopTime = false,
                        loopPose = source.loopPose,
                        maskType = source.maskType,
                    };
                    clips.Add(copy);
                    changed = true;
                    Debug.Log($"[{nameof(DemoAnimationSet)}] Added \"{trim.Name}\" ({trim.Seconds}s of \"{trim.Source}\") to {library}.");
                }
                if (!changed)
                    continue;
                importer.clipAnimations = clips.ToArray();
                EditorUtility.SetDirty(importer);
                importer.SaveAndReimport();
                _clips = null;
            }
        }

        public static void EnsureLooping()
        {
            foreach (string name in MustLoop)
            {
                FixCollectedClip(name);
                FixLibraryClip(name);
            }
        }

        /// <summary>A clip whose feet do not reach the floor, and how far to lower it.</summary>
        private struct GroundedClip
        {
            public string Name;
            public float Lower;
        }

        /// <summary>
        /// Clips that run above the ground, lowered by their root height offset (the import
        /// setting "Root Transform Position (Y) > Offset", `level` on an extracted clip).
        ///
        /// **The forward jogs never touch the floor.** Measured 2026-09-23 as the lowest
        /// vertex of the skinned body over 48 samples of the cycle: `Idle_Loop` rests the sole
        /// at -1.1cm, the three forward jogs never come lower than +3.3 to +3.8cm - on both
        /// bodies - so a running character floated about 4.5cm the whole time. Lowered by that
        /// they bottom out at -1.4cm (male) and -1.1cm (female), level with the idle. Foot IK
        /// cannot fix this: it keeps the animation's own height above the ground, so a clip
        /// that never lands never lands.
        ///
        /// **A positive offset lowers the clip.** Measured: -0.045 raised the jog to +8.0cm.
        ///
        /// Checked and deliberately left alone: `Jog_Bwd*` and `Sprint*` reach -2cm already;
        /// `Walk_Loop` and the side jogs dip *below* the ground (to -6 and -9cm) only in part of
        /// the cycle and stand above it in the rest, which a single offset cannot correct.
        /// </summary>
        private static readonly GroundedClip[] Grounded =
        {
            new GroundedClip { Name = "Jog_Fwd_Loop", Lower = 0.045f },
            new GroundedClip { Name = "Jog_Fwd_L_Loop", Lower = 0.045f },
            new GroundedClip { Name = "Jog_Fwd_R_Loop", Lower = 0.045f },
        };

        /// <summary>
        /// Clips whose drop to the floor has to stay in the pose: "Root Transform Position (Y)
        /// > Bake Into Pose", `loopBlendPositionY` on an extracted clip, `lockRootHeightY` on the
        /// library's import.
        ///
        /// **Every corpse floated.** Unbaked, the library's clips keep the height they start
        /// at and hand any change after that to root motion, which the kit's animator does not
        /// apply. A clip that stays low is placed right - crouch, crawl and sitting all start
        /// low and sit on the ground - but one that goes from standing to lying keeps its
        /// standing height, so a dead character lay flat in mid-air. Measured 2026-09-24 at the
        /// end of `Death01` on the male bandit: hips 0.95m, lowest bone 0.87m above the ground.
        /// Baked: hips 0.09m, lowest bone 0.01m, and the first frame is unchanged.
        ///
        /// The animals' `Death` takes lie on the ground as they are: their rigs name no root
        /// motion bone, so nothing is taken out of the pose.
        /// </summary>
        private static readonly string[] HeightInPose = { "Death01" };

        /// <summary>
        /// Applies <see cref="Grounded"/> and <see cref="HeightInPose"/> to the extracted clips
        /// the demo plays, and to the library's import so a fresh collect comes out the same.
        /// The art collector never overwrites an extracted clip, so the first is what holds.
        /// </summary>
        public static void EnsureGrounded()
        {
            foreach (string name in HeightInPose)
            {
                string path = $"{DemoArtCollector.ClipDir}/{name}.anim";
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (clip == null)
                    continue;
                AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
                if (settings.loopBlendPositionY)
                    continue;
                settings.loopBlendPositionY = true;
                AnimationUtility.SetAnimationClipSettings(clip, settings);
                EditorUtility.SetDirty(clip);
                AssetDatabase.SaveAssetIfDirty(clip);
                Debug.Log($"[{nameof(DemoAnimationSet)}] Baked \"{name}\"'s height into its pose in {path}.");
            }

            foreach (GroundedClip grounded in Grounded)
            {
                string path = $"{DemoArtCollector.ClipDir}/{grounded.Name}.anim";
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (clip == null)
                    continue;
                AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
                if (Mathf.Approximately(settings.level, grounded.Lower))
                    continue;
                settings.level = grounded.Lower;
                AnimationUtility.SetAnimationClipSettings(clip, settings);
                EditorUtility.SetDirty(clip);
                AssetDatabase.SaveAssetIfDirty(clip);
                Debug.Log($"[{nameof(DemoAnimationSet)}] Lowered \"{grounded.Name}\" by {grounded.Lower}m in {path}.");
            }

            foreach (string library in LibraryPaths)
            {
                var importer = AssetImporter.GetAtPath(library) as ModelImporter;
                if (importer == null)
                    continue;
                ModelImporterClipAnimation[] clips = importer.clipAnimations;
                if (clips.Length == 0)
                    clips = importer.defaultClipAnimations;
                bool changed = false;
                foreach (ModelImporterClipAnimation clip in clips)
                {
                    foreach (GroundedClip grounded in Grounded)
                    {
                        if (clip.name != grounded.Name || Mathf.Approximately(clip.heightOffset, grounded.Lower))
                            continue;
                        clip.heightOffset = grounded.Lower;
                        changed = true;
                    }
                    if (!clip.lockRootHeightY && System.Array.IndexOf(HeightInPose, clip.name) >= 0)
                    {
                        clip.lockRootHeightY = true;
                        changed = true;
                    }
                }
                // Slow (UAL1 is 64MB), so only when something actually changed.
                if (!changed)
                    continue;
                importer.clipAnimations = clips;
                EditorUtility.SetDirty(importer);
                importer.SaveAndReimport();
                _clips = null;
                Debug.Log($"[{nameof(DemoAnimationSet)}] Grounded the forward jogs and the death clip in {library}.");
            }
        }

        private static void FixCollectedClip(string name)
        {
            string path = $"{DemoArtCollector.ClipDir}/{name}.anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null || clip.isLooping)
                return;
            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssetIfDirty(clip);
            Debug.Log($"[{nameof(DemoAnimationSet)}] Set \"{name}\" to loop in {path}.");
        }

        private static void FixLibraryClip(string name)
        {
            var importer = AssetImporter.GetAtPath(LibraryPath) as ModelImporter;
            if (importer == null)
                return;
            ModelImporterClipAnimation[] clips = importer.clipAnimations;
            if (clips.Length == 0)
                clips = importer.defaultClipAnimations;
            bool changed = false;
            foreach (ModelImporterClipAnimation clip in clips)
            {
                if (clip.name != name || clip.loopTime)
                    continue;
                clip.loopTime = true;
                changed = true;
            }
            // Reimporting the library is slow - it is a 64MB file with 119 takes - so it
            // only happens when something actually needs changing.
            if (!changed)
                return;
            importer.clipAnimations = clips;
            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();
            Debug.Log($"[{nameof(DemoAnimationSet)}] Set \"{name}\" to loop in {LibraryPath}.");
        }

        public static AnimationClip Clip(string name)
        {
            if (_clips == null)
            {
                var found = new System.Collections.Generic.List<AnimationClip>();
                // The demo's own folder first, so a rebuilt character points at the copy
                // that ships and never back at the library. That matters beyond tidiness:
                // the extracted copies carry hand edits the library does not have - the
                // corrected stances, the loop flag on Sword_Idle - and a rebuild that
                // resolved to the library would silently undo them.
                //
                // The library is scanned second and covers anything not yet extracted. It
                // may be absent entirely in a project that only has the demo, which is the
                // state the demo is meant to ship in.
                if (AssetDatabase.IsValidFolder(ExtraAnimationDir))
                {
                    foreach (string guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { ExtraAnimationDir }))
                        CollectClips(AssetDatabase.GUIDToAssetPath(guid), found);
                    foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { ExtraAnimationDir }))
                        CollectClips(AssetDatabase.GUIDToAssetPath(guid), found);
                }
                foreach (string library in LibraryPaths)
                {
                    if (AssetDatabase.LoadAssetAtPath<Object>(library) != null)
                        CollectClips(library, found);
                }
                // Last, and only for local use: clips made by DemoMixamoImport. Nothing the
                // demo ships names one, and if a local edit does, Verify reports the demo as
                // reaching outside itself - which is the point.
                if (AssetDatabase.IsValidFolder(DemoMixamoImport.ClipDir))
                {
                    foreach (string guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { DemoMixamoImport.ClipDir }))
                        CollectClips(AssetDatabase.GUIDToAssetPath(guid), found);
                }

                _clips = found.ToArray();
            }

            foreach (AnimationClip clip in _clips)
            {
                if (clip.name == name)
                    return clip;
            }
            Debug.LogError($"[{nameof(DemoAnimationSet)}] No clip named \"{name}\" in {string.Join(", ", LibraryPaths)} " +
                           $"or {ExtraAnimationDir}.");
            return null;
        }

        /// <summary>Adds every clip in one asset, skipping duplicates so the library keeps priority.</summary>
        private static void CollectClips(string path, System.Collections.Generic.List<AnimationClip> into)
        {
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                AnimationClip clip = asset as AnimationClip;
                if (clip == null || clip.name.StartsWith("__"))
                    continue;
                bool already = false;
                foreach (AnimationClip existing in into)
                {
                    if (existing.name != clip.name)
                        continue;
                    already = true;
                    break;
                }
                if (!already)
                    into.Add(clip);
            }
        }

        /// <summary>
        /// How much of `Jump_Land` a landing plays, in seconds. The library's clip is 1.27s:
        /// the impact and crouch are done by 0.3s and the rest is the body straightening up.
        ///
        /// **Shorter because the kit holds the base layer on the landing clip for its whole
        /// length, whatever the player is doing.** A character that lands running keeps its
        /// move input and its speed, so the long clip plays on a body that is skating forward
        /// in a landing crouch - 4.7 m of it in a 2026-10-04 harness run. It was hidden before
        /// the jump itself was fixed: the old ground snap pulled the character to the floor in
        /// one frame, which tripped the kit's landing pause and froze it for a second in the
        /// pose. A natural fall never trips that pause (the kit clears its airborne flag 0.42 m
        /// above the ground, before the character touches it), so the landing has to be short
        /// enough to be left moving in. 0.6s is the impact, the crouch and the start of the
        /// rise; the blend into the gait does the rest.
        ///
        /// **Cut, not sped up** - the same reasoning as <see cref="Trimmed"/>, and here for a
        /// second reason: the kit forces airborne clips to play at 1x, but re-applies the state's
        /// `animSpeedRate` whenever the move-speed multiplier changes, so a rate on a landing
        /// plays at 1x standing and at the full rate running.
        /// </summary>
        public const float LandSeconds = 0.6f;

        private const string LandClipName = "Jump_Land_Short";

        /// <summary>
        /// The first <see cref="LandSeconds"/> of `Jump_Land`, as a clip of its own: the source's
        /// own keys up to the cut, tangents and all, so the motion is the library's to the last
        /// bit (resampling it at 60 fps gave 7x the keys and a 2.4MB file for 0.6s).
        /// Made once; remade in place (same file, same GUID) only if <see cref="LandSeconds"/>
        /// has changed since. Null if the library has no `Jump_Land`.
        /// </summary>
        public static AnimationClip LandClip()
        {
            AnimationClip source = Clip("Jump_Land");
            if (source == null)
                return null;
            string path = $"{MadeAnimationDir}/{LandClipName}.anim";
            if (!AssetDatabase.IsValidFolder(MadeAnimationDir))
                AssetDatabase.CreateFolder("Assets/OpenMMORPG/Demo", "Animations");
            float length = Mathf.Min(LandSeconds, source.length);
            AnimationClip made = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (made != null && Mathf.Abs(made.length - length) < 0.001f)
                return made;
            if (made == null)
            {
                made = new AnimationClip();
                AssetDatabase.CreateAsset(made, path);
            }
            else
            {
                made.ClearCurves();
            }
            made.frameRate = source.frameRate;

            const float Epsilon = 0.0001f;
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(source))
            {
                AnimationCurve curve = AnimationUtility.GetEditorCurve(source, binding);
                if (curve == null)
                    continue;
                var keys = new List<Keyframe>();
                foreach (Keyframe key in curve.keys)
                {
                    if (key.time <= length + Epsilon)
                        keys.Add(key);
                }
                // The cut falls between two keys unless it lands on a frame: end on the value the
                // source has there, leaving it at the speed it was moving, so the clip does not
                // stop dead one frame short.
                if (keys.Count > 0 && keys[keys.Count - 1].time < length - Epsilon)
                {
                    const float Step = 1f / 120f;
                    float slope = (curve.Evaluate(length + Step) - curve.Evaluate(length - Step)) / (2f * Step);
                    keys.Add(new Keyframe(length, curve.Evaluate(length), slope, slope));
                }
                AnimationUtility.SetEditorCurve(made, binding, new AnimationCurve(keys.ToArray()));
            }

            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(source);
            settings.loopTime = false;
            settings.stopTime = length;
            AnimationUtility.SetAnimationClipSettings(made, settings);
            EditorUtility.SetDirty(made);
            AssetDatabase.SaveAssetIfDirty(made);
            Debug.Log($"[{nameof(DemoAnimationSet)}] Made \"{LandClipName}\" ({length:0.00}s of \"Jump_Land\") in {MadeAnimationDir}.");
            return made;
        }

        /// <summary>The landing state: <see cref="LandClip"/>, or the full library clip if that cannot be made.</summary>
        public static AnimState Landing()
        {
            AnimationClip clip = LandClip();
            return clip != null ? new AnimState { clip = clip } : State("Jump_Land");
        }

        public static AnimState State(string clip, float speedRate = 0f)
        {
            return new AnimState { clip = Clip(clip), animSpeedRate = speedRate };
        }

        public static ActionState Action(string clip, float speedRate = 0f)
        {
            return new ActionState { clip = Clip(clip), animSpeedRate = speedRate };
        }

        /// <summary>Where <see cref="UpperBodyMask"/> lives. Made once and then left alone.</summary>
        public const string UpperBodyMaskPath = MadeAnimationDir + "/UpperBodyMask.mask";

        /// <summary>
        /// The humanoid parts an action keeps when it must not touch the legs: spine, head,
        /// both arms and their fingers and hand IK. Root is off with the legs, so the hips
        /// stay where the locomotion layer put them.
        /// </summary>
        private static readonly AvatarMaskBodyPart[] UpperBodyParts =
        {
            AvatarMaskBodyPart.Body, AvatarMaskBodyPart.Head,
            AvatarMaskBodyPart.LeftArm, AvatarMaskBodyPart.RightArm,
            AvatarMaskBodyPart.LeftFingers, AvatarMaskBodyPart.RightFingers,
            AvatarMaskBodyPart.LeftHandIK, AvatarMaskBodyPart.RightHandIK,
        };

        /// <summary>
        /// The mask that plays a clip on the upper body only. **A kit state with no mask plays
        /// on every bone**: the playable puts an action on its own layer above locomotion, and
        /// an empty mask there means the clip's legs and hips replace the jog's - so a character
        /// who picks something up while running stands still from the waist down, and a hit
        /// reaction freezes the stride.
        ///
        /// A humanoid body-part mask, not a list of bones, because every body in the demo - both
        /// genders, every outfit - shares one humanoid avatar and the clips are humanoid, so
        /// the mask fits all of them. It is **not** the kit's `TopMask` under `Core/Resources`:
        /// that one is a transform list for the kit's own rig (`Root_M/Spine1_M/...`) and names
        /// nothing in the Quaternius skeleton.
        ///
        /// Loaded if it exists, so a hand-tuned mask survives a rebuild and its GUID, which the
        /// model prefabs point at, stays the same.
        /// </summary>
        public static AvatarMask UpperBodyMask()
        {
            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(UpperBodyMaskPath);
            if (mask != null)
                return mask;

            if (!AssetDatabase.IsValidFolder(MadeAnimationDir))
                AssetDatabase.CreateFolder(System.IO.Path.GetDirectoryName(MadeAnimationDir).Replace('\\', '/'),
                                           System.IO.Path.GetFileName(MadeAnimationDir));
            mask = new AvatarMask { name = "UpperBodyMask" };
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; ++i)
            {
                var part = (AvatarMaskBodyPart)i;
                mask.SetHumanoidBodyPartActive(part, System.Array.IndexOf(UpperBodyParts, part) >= 0);
            }
            AssetDatabase.CreateAsset(mask, UpperBodyMaskPath);
            Debug.Log($"[{nameof(DemoAnimationSet)}] Made {UpperBodyMaskPath}.");
            return mask;
        }

        /// <summary>
        /// Clips that *are* the character's movement, which a mask would strip of its point:
        /// Charge plays `Sprint_Shield_Loop` and Fleet of Foot `Sprint_Enter`, and on the upper
        /// body alone the character would run on the spot with its legs in the jog.
        /// </summary>
        private static bool IsLocomotion(AnimationClip clip)
        {
            return clip != null && clip.name.StartsWith("Sprint");
        }

        /// <summary>
        /// Plays this state on the upper body in every situation. For a gesture the legs have
        /// no part in - picking something up. Standing, the legs idle; running, they run.
        /// </summary>
        private static void OnlyUpperBody(ActionState state)
        {
            if (state == null || state.clip == null || state.avatarMask != null)
                return;
            state.avatarMask = UpperBodyMask();
        }

        /// <summary>
        /// Plays this state on the whole body standing still, and on the upper body only while
        /// the character is running or in the air. For a swing, a cast or a flinch: with the
        /// feet planted they are part of the pose, and moving they must not override the stride.
        ///
        /// The kit picks the mask when the action *starts* - grounded and moving, then
        /// `avatarMaskWhileMoving` (sprinting uses it too unless it has its own), airborne then
        /// `avatarMaskWhileAirbourne`, otherwise `avatarMask`. Leaving `avatarMask` empty is what
        /// keeps the standing case exactly as it was.
        ///
        /// A mask already on the field is left, so one set by hand is not undone by a rebuild.
        /// </summary>
        private static void UpperBodyWhileMoving(ActionState state)
        {
            if (state == null || state.clip == null || IsLocomotion(state.clip))
                return;
            AvatarMask mask = UpperBodyMask();
            if (state.avatarMaskWhileMoving == null)
                state.avatarMaskWhileMoving = mask;
            if (state.avatarMaskWhileAirbourne == null)
                state.avatarMaskWhileAirbourne = mask;
        }

        private static void UpperBodyWhileMoving(ActionAnimation animation)
        {
            if (animation != null)
                UpperBodyWhileMoving(animation.state);
        }

        private static void UpperBodyWhileMoving(ActionAnimation[] animations)
        {
            if (animations == null)
                return;
            foreach (ActionAnimation animation in animations)
                UpperBodyWhileMoving(animation);
        }

        /// <summary>
        /// A draw or sheathe on the upper body in every situation, like a pickup: the reach is
        /// the arms and the shoulders, a character who draws on the run keeps running, and one
        /// who draws standing keeps its feet where the idle put them.
        /// </summary>
        private static void OnlyUpperBody(HolsterAnimation holster)
        {
            if (holster == null)
                return;
            OnlyUpperBody(holster.sheathState);
            OnlyUpperBody(holster.unsheathState);
        }

        /// <summary>
        /// Masks the unarmed set: pickup and the draw/sheathe reach on the upper body always;
        /// hurt, charge, attack and spell states on it while moving or airborne. Ladder and
        /// vehicle states, which are whole-body by nature, are left alone, as are the unused
        /// reload states.
        /// Idempotent, so <see cref="DemoCharacterBuilder"/> can run it over models already built.
        /// </summary>
        public static void ApplyMasks(DefaultAnimations anims)
        {
            if (anims == null)
                return;
            OnlyUpperBody(anims.pickupState);
            OnlyUpperBody(anims.rightHandWeaponSheathingAnimation);
            OnlyUpperBody(anims.leftHandWeaponSheathingAnimation);
            OnlyUpperBody(anims.leftHandShieldSheathingAnimation);
            UpperBodyWhileMoving(anims.hurtState);
            UpperBodyWhileMoving(anims.rightHandChargeState);
            UpperBodyWhileMoving(anims.leftHandChargeState);
            UpperBodyWhileMoving(anims.rightHandAttackAnimations);
            UpperBodyWhileMoving(anims.leftHandAttackAnimations);
            UpperBodyWhileMoving(anims.skillCastState);
            UpperBodyWhileMoving(anims.skillActivateAnimation);
        }

        /// <summary>One weapon's set, masked the same way as <see cref="ApplyMasks(DefaultAnimations)"/>.</summary>
        public static void ApplyMasks(WeaponAnimations set)
        {
            if (set == null)
                return;
            OnlyUpperBody(set.pickupState);
            // A weapon set's own draw and sheathe (today only the bow's), upper body like the default's.
            OnlyUpperBody(set.rightHandWeaponSheathingAnimation);
            OnlyUpperBody(set.leftHandWeaponSheathingAnimation);
            UpperBodyWhileMoving(set.hurtState);
            UpperBodyWhileMoving(set.rightHandChargeState);
            UpperBodyWhileMoving(set.leftHandChargeState);
            UpperBodyWhileMoving(set.rightHandAttackAnimations);
            UpperBodyWhileMoving(set.leftHandAttackAnimations);
        }

        /// <summary>One skill's cast and activate states, including the per-weapon variants.</summary>
        public static void ApplyMasks(SkillAnimations skill)
        {
            if (skill == null)
                return;
            UpperBodyWhileMoving(skill.castState);
            UpperBodyWhileMoving(skill.activateAnimation);
            if (skill.castStatesByWeaponTypes != null)
            {
                foreach (WeaponActionState state in skill.castStatesByWeaponTypes)
                    UpperBodyWhileMoving(state);
            }
            if (skill.activateAnimationsByWeaponTypes != null)
            {
                foreach (WeaponActionAnimation animation in skill.activateAnimationsByWeaponTypes)
                    UpperBodyWhileMoving(animation);
            }
        }

        /// <summary>
        /// Every direction plays the same clip. Still correct for the gaits the library
        /// only ships facing forward - walk and swim - where a directional substitute
        /// would be worse than the repeat (see <see cref="SprintMoves"/> for the numbers).
        /// </summary>
        public static MoveStates Moves(string clip, float speedRate = 0f)
        {
            return new MoveStates
            {
                forwardState = State(clip, speedRate),
                backwardState = State(clip, speedRate),
                leftState = State(clip, speedRate),
                rightState = State(clip, speedRate),
                upState = State(clip, speedRate),
                downState = State(clip, speedRate),
                forwardLeftState = State(clip, speedRate),
                forwardRightState = State(clip, speedRate),
                backwardLeftState = State(clip, speedRate),
                backwardRightState = State(clip, speedRate),
            };
        }

        /// <summary>
        /// A full eight-way set, one clip per direction.
        ///
        /// The direction of every clip below was read off its own root motion rather than
        /// trusted from its name: each was measured for how far and which way it actually
        /// travels over its length. That matters because the library carries two similar
        /// families - `Jog_Fwd_L_Loop` really is a forward-left diagonal (315 degrees),
        /// while `Jog_Fwd_LeanL_Loop` travels dead ahead and only leans, so it belongs in
        /// no directional slot at all.
        ///
        /// `upState` and `downState` keep the forward clip: those are the vertical
        /// swim/fly slots and the library has nothing for them.
        /// </summary>
        public static MoveStates Moves8(string fwd, string bwd, string left, string right,
                                        string fwdLeft, string fwdRight,
                                        string bwdLeft, string bwdRight, float speedRate = 0f)
        {
            return new MoveStates
            {
                forwardState = State(fwd, speedRate),
                backwardState = State(bwd, speedRate),
                leftState = State(left, speedRate),
                rightState = State(right, speedRate),
                upState = State(fwd, speedRate),
                downState = State(fwd, speedRate),
                forwardLeftState = State(fwdLeft, speedRate),
                forwardRightState = State(fwdRight, speedRate),
                backwardLeftState = State(bwdLeft, speedRate),
                backwardRightState = State(bwdRight, speedRate),
            };
        }

        /// <summary>Four-way set for gaits with no diagonals; each diagonal takes the
        /// forward or backward clip it is nearest to.</summary>
        public static MoveStates Moves4(string fwd, string bwd, string left, string right,
                                        float speedRate = 0f)
        {
            return Moves8(fwd, bwd, left, right, fwd, fwd, bwd, bwd, speedRate);
        }

        /// <summary>
        /// The forward jog: the user's own `Demo/Animations/Jog_Fwd.anim` (2026-10-06), the library's
        /// `Jog_Fwd_Loop` with the arm swing roughly halved (shoulders, arms and forearms at 40-60% of the
        /// amplitude, chest twist and head turn at about 70%). Same length, stride and bob as the library's, so
        /// the run speed is unchanged, and it is already baked to face forward (`orientationOffsetY` 0).
        ///
        /// **It is not in <see cref="Grounded"/>, and its `level` is 0 on purpose.** The 0.045 that lowers the
        /// library's jog is baked into this clip's own curves, and it came with `level` 0.045 as well, which
        /// lowered it a second time: lowest vertex -5.7cm against the library's -1.5cm, hips 4.7cm low, on both
        /// bodies. At 0 it bottoms out at -1.2cm (male). Do not add it to the table.
        ///
        /// The diagonals are still the library's, with the full swing.
        /// </summary>
        public const string JogForwardClip = "Jog_Fwd";

        /// <summary>The library jog that <see cref="JogForwardClip"/> replaced, for `Refresh Jog` to find.</summary>
        public const string LegacyJogForwardClip = "Jog_Fwd_Loop";

        /// <summary>Eight-way jog - the standing run, and the set every character uses most.</summary>
        public static MoveStates JogMoves()
        {
            return Moves8(JogForwardClip, "Jog_Bwd_Loop", "Jog_Left_Loop", "Jog_Right_Loop",
                          "Jog_Fwd_L_Loop", "Jog_Fwd_R_Loop", "Jog_Bwd_L_Loop", "Jog_Bwd_R_Loop");
        }

        /// <summary>
        /// Eight-way crouch, with one substitution.
        ///
        /// The back-right diagonal uses the straight-back clip rather than
        /// `Crouch_Bwd_R_Loop`, which is defective in the library: it covers only 0.22m in
        /// 2.37s where its mirror `Crouch_Bwd_L_Loop` covers 1.39m in 2.00s, so the feet
        /// cycle almost in place while the character slides. Two independent measurements
        /// agree it is the odd one out - that travel distance, and a mirror-yaw check where
        /// the pair failed to cancel (52.2 against -36.3 degrees) while every other
        /// directional pair cancelled to within a few degrees.
        ///
        /// The cost of the substitution is that backing left keeps a true diagonal while
        /// backing right gets a straight-back cycle, so the two are no longer mirror images.
        /// That is far less visible than the sliding, but if it ever reads oddly the
        /// consistent alternative is to use `Crouch_Bwd_Loop` for both back diagonals.
        ///
        /// Each clip plays at the rate that makes its feet keep pace with the ground the
        /// character really covers in that direction (see <see cref="CrouchSpeed"/>), so a
        /// crouch walk neither skates nor treads in place.
        /// </summary>
        public static MoveStates CrouchMoves()
        {
            float forward = CrouchSpeed;
            float side = CrouchSpeed * CrouchSideMoveSpeedRate;
            float back = CrouchSpeed * CrouchBackMoveSpeedRate;
            return new MoveStates
            {
                forwardState = State("Crouch_Fwd_Loop", forward / CrouchFwdPace),
                backwardState = State("Crouch_Bwd_Loop", back / CrouchBwdPace),
                leftState = State("Crouch_Left_Loop", side / CrouchLeftPace),
                rightState = State("Crouch_Right_Loop", side / CrouchRightPace),
                upState = State("Crouch_Fwd_Loop", forward / CrouchFwdPace),
                downState = State("Crouch_Fwd_Loop", forward / CrouchFwdPace),
                forwardLeftState = State("Crouch_Fwd_L_Loop", forward / CrouchFwdLeftPace),
                forwardRightState = State("Crouch_Fwd_R_Loop", forward / CrouchFwdRightPace),
                backwardLeftState = State("Crouch_Bwd_L_Loop", back / CrouchBwdLeftPace),
                backwardRightState = State("Crouch_Bwd_Loop", back / CrouchBwdPace),
            };
        }

        // How fast each crouch clip carries the body at its authored pace, in metres a second.
        // The library's clips are in place (no root motion), so this was read off the feet
        // (2026-10-04): sampled 400 times a cycle, the body is taken to move opposite to
        // whichever of the four contact points (each heel and toe) is lowest that frame, which
        // holds for any gait that always has a foot down. The same measure gives Walk_Loop
        // 0.95 against the 0.96 on record, and Crouch_Bwd_L_Loop 0.73 against 0.70.
        // Crouch_Bwd_R_Loop is not measured: it is not used (see above).
        private const float CrouchFwdPace = 0.69f;
        private const float CrouchBwdPace = 0.69f;
        private const float CrouchLeftPace = 0.49f;
        private const float CrouchRightPace = 0.41f;
        private const float CrouchFwdLeftPace = 0.67f;
        private const float CrouchFwdRightPace = 0.69f;
        private const float CrouchBwdLeftPace = 0.73f;

        /// <summary>
        /// How much faster than authored the forward crouch plays - the one knob for how fast
        /// a crouching character goes. At its own pace the clip is a 2 s cycle covering 0.69 m/s,
        /// a creep. The kit's rule crouches at 0.35 of run speed, 1.4 m/s: at 1x the feet
        /// skated, and keeping up would take 2x, a scurry. 1.5 plays it as a brisk sneak, at
        /// 1.04 m/s.
        /// </summary>
        private const float CrouchForwardRate = 1.5f;

        /// <summary>A forward crouch, in metres a second, at the base move speed.</summary>
        public static float CrouchSpeed
        {
            get { return CrouchFwdPace * CrouchForwardRate; }
        }

        /// <summary>
        /// The gameplay rule's `moveSpeedRateWhileCrouching`: the crouch as a share of the base
        /// move speed. Written by DemoDatabaseWiring.
        /// </summary>
        public static float CrouchMoveSpeedRate
        {
            get { return CrouchSpeed / BaseMoveSpeed; }
        }

        /// <summary>
        /// The movement component's crouch rates across and back, as shares of the forward
        /// crouch. The side clips cover only 0.41-0.49 m/s, so at the kit's 1.0 across they would
        /// have to play at 2.1x-2.5x; 0.65 keeps them under 1.7x. Backward is the
        /// kit's own 0.75, the same as standing. Written onto the players by DemoEntityBuilder.
        /// </summary>
        public const float CrouchSideMoveSpeedRate = 0.65f;
        public const float CrouchBackMoveSpeedRate = 0.75f;

        /// <summary>Four-way crawl. Real crawl clips now exist, so crouch no longer stands in.</summary>
        public static MoveStates CrawlMoves()
        {
            return Moves4("Crawl_Fwd_Loop", "Crawl_Bwd_Loop", "Crawl_Left_Loop", "Crawl_Right_Loop");
        }

        /// <summary>
        /// Sprint exists facing forward only, so anything off-axis borrows the jog strafe.
        /// Measured, they are close enough to read as one gait: jog covers 5.3 m/s against
        /// sprint's 8.1. Walk is not given the same treatment - at 0.96 m/s it is five times
        /// slower than the jog, so a borrowed strafe there would read as slow motion, and
        /// walk keeps repeating its forward clip instead.
        /// </summary>
        public static MoveStates SprintMoves()
        {
            return Moves8("Sprint_Loop", "Jog_Bwd_Loop", "Jog_Left_Loop", "Jog_Right_Loop",
                          "Sprint_Loop", "Sprint_Loop", "Jog_Bwd_L_Loop", "Jog_Bwd_R_Loop");
        }

        /// <summary>
        /// The base move speed the kit scales every move clip by, the warrior's and mage's
        /// 4 m/s from DemoDatabaseWiring (the ranger's 4.5 plays the climb a tenth faster).
        /// The kit plays a move clip at <c>animSpeedRate x moveSpeed / baseMoveSpeed</c>, so a
        /// clip's rate is set against this.
        /// </summary>
        public const float BaseMoveSpeed = 4f;

        /// <summary>The climb loops' cycle, in seconds.</summary>
        private const float ClimbLoopLength = 1.2667f;

        /// <summary>
        /// How many rungs the body rises in one cycle of the climb loop. Each foot steps
        /// once a cycle, so this is a step. Three is the loop's own stride to within a
        /// third of a rung: in <c>Climb_Up_Loop</c> a planted foot descends through the
        /// body 0.58 m in half a cycle (measured 2026-10-02), which is the body climbing
        /// 1.16 m a cycle at the authored pace, and the watchtower's rungs are 0.34 m
        /// apart. Whole rungs so that each step lands on one and LadderLimbIK has only a
        /// finger's width to correct; the small mismatch is a planted foot drifting a few
        /// centimetres, which the hold absorbs.
        /// </summary>
        private const int RungsPerStep = 3;

        /// <summary>
        /// How fast the climb loops are to rise at their authored pace, in metres a second:
        /// <see cref="RungsPerStep"/> rungs a cycle. The climb speed scales the clip's rate,
        /// so this fixes the rise per cycle whatever the speed.
        /// </summary>
        private static float ClimbLoopRise
        {
            get { return RungsPerStep * DemoSceneBuilder.WatchtowerRungSpacing / ClimbLoopLength; }
        }

        /// <summary>
        /// The ladder set. On a ladder the kit only ever asks for up and down - the sideways
        /// shuffles are wired for completeness - and the vertical slots are the ones that
        /// matter, which <see cref="Moves8"/> fills with the forward clip, so they are set
        /// by hand.
        ///
        /// The rate makes the loop keep pace with the climb. The kit scales every move clip by
        /// the entity's current move speed over its base speed, and a climber's current speed
        /// is <see cref="MultiplayerARPG.EasedCharacterLadderComponent.DefaultClimbSpeed"/> (set by the
        /// demo's ladder component; the kit climbs at running pace, which slid the feet eight
        /// rungs for every one they took). Wanted: the clip at <c>ClimbSpeed / ClimbLoopRise</c>.
        /// Given: <c>rate x ClimbSpeed / BaseMoveSpeed</c>. So the rate is the base speed over
        /// the loop's rise, whatever the climb speed is set to.
        /// </summary>
        public static MoveStates ClimbMoves()
        {
            float rate = BaseMoveSpeed / ClimbLoopRise;
            MoveStates moves = Moves8("Climb_Up_Loop", "Climb_Down_Loop", "Climb_Left_Loop", "Climb_Right_Loop",
                                      "Climb_Up_Loop", "Climb_Up_Loop", "Climb_Down_Loop", "Climb_Down_Loop");
            moves.upState = State("Climb_Up_Loop", rate);
            moves.downState = State("Climb_Down_Loop", rate);
            return moves;
        }

        /// <summary>
        /// Stepping onto a ladder from the top: the user's clip, cut in and out over a frame
        /// rather than the model's tenth of a second. Its pose begins facing away from the
        /// wall while the entity is turned to the wall the moment it takes hold (see
        /// EasedCharacterLadderComponent), so a longer fade-in would show the body facing the wall
        /// and swinging back; and the fade-out is where its hanging pose hands over to the
        /// climb idle, which EasedCharacterLadderComponent tracks by the fade's weight whatever its
        /// length.
        /// </summary>
        public static ActionState TopEnter()
        {
            ActionState state = Action("Ladder_Climb_Down_Start");
            state.transitionDuration = 0.02f;
            return state;
        }

        /// <summary>
        /// The climb idle, at its authored pace: the kit scales it by the same speed ratio as
        /// the loops (<see cref="ClimbMoves"/>), which would play it at a third, so the rate
        /// undoes that.
        /// </summary>
        public static AnimState ClimbIdle()
        {
            return State("Climb_Idle_Loop", BaseMoveSpeed / MultiplayerARPG.EasedCharacterLadderComponent.DefaultClimbSpeed);
        }

        public static ActionAnimation Attack(string clip, float triggerRate, float speedRate = 0f, AudioClip[] audio = null)
        {
            return new ActionAnimation
            {
                state = Action(clip, speedRate),
                triggerDurationRates = new[] { triggerRate },
                durationType = AnimationDurationType.ByClipLength,
                // Played as the swing starts; see DemoAudioWiring for the families.
                audioClips = audio ?? new AudioClip[0],
            };
        }

        /// <summary>Unarmed locomotion, reactions and fist attacks. Weapons override on top of this.</summary>
        public static DefaultAnimations BuildDefault()
        {
            var anims = new DefaultAnimations
            {
                idleState = State("Idle_Loop"),
                moveStates = JogMoves(),
                sprintStates = SprintMoves(),
                walkStates = Moves("Walk_Loop"),

                crouchIdleState = State("Crouch_Idle_Loop"),
                crouchMoveStates = CrouchMoves(),
                crawlIdleState = State("Crawl_Idle_Loop"),
                crawlMoveStates = CrawlMoves(),

                swimIdleState = State("Swim_Idle_Loop"),
                swimMoveStates = Moves("Swim_Fwd_Loop"),

                // On a ladder. The library's enter and exit are the bottom pair - stepping
                // onto the rungs from the ground and back off them - and the ledge clip is
                // the pull-up onto whatever the ladder leans against. Stepping onto a ladder
                // from the top is the user's own Ladder_Climb_Down_Start (2026-10-02, hand
                // animated, CC0 like the rest of their art - never regenerate it): a turn at
                // the edge and a lowering onto the rungs. Unlike the library's in-place clips
                // its pose carries the body down and out, so EasedCharacterLadderComponent moves the
                // entity by the remainder only (see its TopEnterPoseOffset). Played at its
                // authored pace.
                //
                // The character is carried along the enter or exit path by EasedCharacterLadderComponent
                // for exactly the clip's length (the kit's own lerp has a precedence bug that
                // snapped it to the end), so the rates below are the whole of the timing:
                // 1.47 s of stepping on and off at 1.4 is 1.05 s, and 0.63 s of pull-up at
                // 0.6 is the same.
                climbIdleState = ClimbIdle(),
                climbMoveStates = ClimbMoves(),
                climbBottomEnterExitStates = new EnterExitStates
                {
                    enterState = Action("Climb_Enter", 1.4f),
                    exitState = Action("Climb_Exit", 1.4f),
                },
                climbTopEnterExitStates = new EnterExitStates
                {
                    enterState = TopEnter(),
                    exitState = Action("ClimbLedge", 0.6f),
                },

                jumpState = State("Jump_Start"),
                fallState = State("Jump_Loop"),
                landedState = Landing(),

                hurtState = Action("Hit_Chest"),
                deadState = State("Death01"),

                dashStartState = State("Roll"),
                dashLoopState = State("Roll"),
                dashEndState = State("Roll"),

                sittingStartState = State("Sitting_Enter"),
                sittingLoopState = State("Sitting_Idle_Loop"),
                sittingEndState = State("Sitting_Exit"),

                pickupState = Action("PickUp_Table"),

                // Fists: two clips alternate, each landing near the end of the swing.
                rightHandAttackAnimations = new[]
                {
                    Attack("Punch_Jab", 0.55f),
                    Attack("Punch_Cross", 0.5f),
                },
                leftHandAttackAnimations = new[]
                {
                    Attack("Punch_Cross", 0.5f),
                },

                skillCastState = Action("Spell_Simple_Idle_Loop"),
                skillActivateAnimation = Attack("Spell_Simple_Shoot", 0.45f),
            };
            // How a weapon gets to and from the back; see DemoSheathBuilder. On the default set
            // alone: a weapon set that names no holster clip falls back to these, so one reach
            // serves every weapon.
            DemoSheathBuilder.WriteHolsters(anims);
            ApplyMasks(anims);
            return anims;
        }

        /// <summary>
        /// The default set of a body that is never without its weapon: <see cref="BuildDefault()"/>
        /// with that weapon's stance, gaits and attack laid over it. Null gives the plain set.
        ///
        /// **This is the only way a monster fights with what is in its hand.** A monster equips
        /// nothing - the kit's `MonsterCharacter` has no weapon slot, and its attacks run on
        /// `GameInstance.MonsterWeaponItem`, whose weapon type is generated at runtime and
        /// matches no set in `weaponAnimations`. So the kit plays a monster's default set
        /// whatever it holds, and left at the plain one a bandit with an axe in his fist
        /// punched with it.
        ///
        /// The weapon's own set, from the same <see cref="BuildWeaponSet"/> a player's comes
        /// from, so an enemy stands and swings exactly as a player holding that weapon does -
        /// and the grip, which was captured against that stance on the animation bench, sits
        /// right in its fist too. A bow fires in one go: nothing but a player can charge.
        /// </summary>
        /// <param name="casts">
        /// A body whose basic attack is a spell rather than a blow with what it holds: the
        /// cultists (see `DemoCharacterBuilder.Variant.Casts`). It keeps the weapon's stance and
        /// gaits and attacks with <see cref="CasterAttack"/>.
        /// </param>
        public static DefaultAnimations BuildDefault(WeaponType wields, bool casts = false)
        {
            DefaultAnimations anims = BuildDefault();
            WeaponAnimations set = BuildWeaponSet(wields, false);
            if (set == null)
                return anims;
            anims.idleState = set.idleState;
            anims.moveStates = set.moveStates;
            anims.sprintStates = set.sprintStates;
            anims.walkStates = set.walkStates;
            anims.rightHandAttackAnimations = casts ? new[] { CasterAttack() } : set.rightHandAttackAnimations;
            if (set.leftHandAttackAnimations != null && set.leftHandAttackAnimations.Length > 0)
                anims.leftHandAttackAnimations = set.leftHandAttackAnimations;
            UpperBodyWhileMoving(anims.rightHandAttackAnimations);
            return anims;
        }

        /// <summary>
        /// Seconds a caster holds after each bolt before the next can start. The flick is 0.5s,
        /// so on its own it would be a bolt every half second - a machine gun, not a spell. Held,
        /// it comes round about every two seconds, as the bandit archer's shot does.
        /// </summary>
        public const float CasterAttackRecovery = 1.3f;

        /// <summary>
        /// A caster's basic attack (2026-10-05): the one-handed spell flick Arcane Bolt ends in,
        /// thrown where the hand is furthest forward, with the cast sound. What it throws is the
        /// monster's own missile (`MonsterSpec.Missile` in DemoDatabaseWiring) - this is only the
        /// hand that throws it.
        /// </summary>
        public static ActionAnimation CasterAttack()
        {
            ActionAnimation cast = Attack("Spell_Simple_Shoot", 0.45f,
                                          audio: DemoAudioWiring.Clips(DemoAudioWiring.SpellCast));
            cast.extendDuration = CasterAttackRecovery;
            return cast;
        }

        /// <summary>
        /// One set per weapon type the demo can equip.
        ///
        /// Without these the model carries no weapon animations at all, and a character
        /// falls back to <see cref="BuildDefault"/> — the empty-handed set — whatever it
        /// happens to be holding. The result reads as a broken attachment rather than as
        /// the wrong clip: the character stands in the unarmed idle, fist closed around
        /// nothing, with a sword sticking out of it at whatever angle that pose leaves the
        /// hand. Wiring the melee set puts the same hand into a guard the sword was drawn
        /// for, and the weapon sits in it properly.
        /// </summary>
        /// <param name="canCharge">
        /// Whether this character can hold a shot before loosing it. Only a player can: the
        /// kit starts a charge from <c>ShooterPlayerCharacterController</c> and nowhere else,
        /// so monsters never do, however their weapon is set up.
        ///
        /// It is not enough on its own. A charge is only ever *started* for a weapon that
        /// fires on release, which is what <see cref="DemoItemBuilder.BowsCharge"/> decides,
        /// so the two have to agree: with charging off, a player wired for it would play the
        /// release on its own and every shot would begin at full stretch - no draw, no string
        /// to pull. <see cref="BuildRanged"/> is therefore given both, not just the first.
        /// </param>
        public static WeaponAnimations[] BuildWeaponAnimations(bool canCharge)
        {
            var built = new System.Collections.Generic.List<WeaponAnimations>();
            foreach (string guid in AssetDatabase.FindAssets("t:WeaponType", new[] { WeaponTypeDir }))
            {
                WeaponAnimations set = BuildWeaponSet(
                    AssetDatabase.LoadAssetAtPath<WeaponType>(AssetDatabase.GUIDToAssetPath(guid)), canCharge);
                if (set != null)
                    built.Add(set);
            }
            if (built.Count == 0)
                Debug.LogError($"[{nameof(DemoAnimationSet)}] No weapon types under {WeaponTypeDir}, so every " +
                               "character will animate as though empty-handed. Run Build Items first.");
            return built.ToArray();
        }

        /// <summary>
        /// The set for one weapon type, or null for bare hands, which have no set of their own.
        /// See <see cref="BuildWeaponAnimations"/> for <paramref name="canCharge"/>.
        /// </summary>
        public static WeaponAnimations BuildWeaponSet(WeaponType weaponType, bool canCharge)
        {
            if (weaponType == null)
                return null;
            WeaponAnimations set;
            switch (weaponType.name)
            {
                case "Staff":
                    set = BuildMagic(weaponType);
                    break;
                case "Bow":
                    set = BuildRanged(weaponType, canCharge && DemoItemBuilder.BowsCharge);
                    break;
                case "Unarmed":
                    // Left out on purpose. With no set of its own it falls through to
                    // the default animations, which are the empty-handed ones.
                    return null;
                default:
                    set = BuildMelee(weaponType);
                    break;
            }
            ApplyMasks(set);
            return set;
        }

        /// <summary>The demo's weapon type of that name, or null, saying so, if there is none.</summary>
        public static WeaponType WeaponTypeNamed(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;
            var weaponType = AssetDatabase.LoadAssetAtPath<WeaponType>($"{WeaponTypeDir}/{name}.asset");
            if (weaponType == null)
                Debug.LogError($"[{nameof(DemoAnimationSet)}] No weapon type \"{name}\" under {WeaponTypeDir}. Run Build Items first.");
            return weaponType;
        }

        /// <summary>
        /// One entry per skill that wants its own clip, which is most of them.
        ///
        /// This has to exist, and the reason is not obvious: the kit does NOT fall back
        /// to the equipped weapon's `skillActivateAnimation` when a skill has no entry
        /// here - <c>PlayableCharacterModel.GetSkillActivateAnimation</c> falls all the
        /// way through to <c>defaultAnimations</c>, which in this demo is the unarmed set
        /// and casts a spell. Left empty, every skill in the game plays
        /// <c>Spell_Simple_Shoot</c>: the warrior cleaves by waving a hand, and the
        /// archer looses an arrow the same way.
        ///
        /// The clips are hung on the weapon type rather than on the skill outright,
        /// because that is the level the kit looks them up at and because it is true: a
        /// Cleave is a sword's swing, and if a warrior ever picks up a staff it should
        /// not be one.
        /// </summary>
        public static SkillAnimations[] BuildSkillAnimations()
        {
            var built = new System.Collections.Generic.List<SkillAnimations>();
            // Volley's clips are made from the bow's pair, so they exist before anything names them.
            EnsureHighAngleClips();
            foreach (DemoSkillBuilder.SkillSpec spec in DemoSkillBuilder.All())
            {
                // A passive is never pressed, so there is nothing to play; an entry for one would
                // fall to the weapon's attack below and read as though it could be.
                if (spec.Passive)
                    continue;
                BaseSkill skill = DemoSkillBuilder.Asset(spec.Name);
                if (skill == null)
                    continue;

                var anims = new SkillAnimations { skill = skill };
                // Everything the demo's skills need a weapon for names one, and the rest
                // are the warrior's, which work with anything he can hold.
                WeaponType weaponType = string.IsNullOrEmpty(spec.Weapon)
                    ? null
                    : AssetDatabase.LoadAssetAtPath<WeaponType>($"{WeaponTypeDir}/{spec.Weapon}.asset");

                if (spec.Clip != null)
                {
                    // A strike's own sound belongs to what falls, not to the hands that called it
                    // down (the meteor plays Meteor.wav as it lands), so its launch takes only the
                    // generic family.
                    //
                    // A bow skill takes none here at all. The kit plays an action's sound as the
                    // animation starts, and a bow's starts well before the arrow goes - the release
                    // clip looses 0.175s in. Its sound is on its release effect instead, which goes
                    // off on the trigger (SkillReleaseEffects).
                    string ownSound = spec.FallSeconds > 0f ? null : spec.Name;
                    AudioClip[] audio = spec.Weapon == "Bow"
                        ? new AudioClip[0]
                        : DemoAudioWiring.SkillClips(ownSound, spec.Audio);
                    ActionAnimation activate = Attack(spec.Clip, spec.Trigger, spec.ClipSpeed, audio);
                    anims.activateAnimationType = SkillActivateAnimationType.UseActivateAnimation;
                    anims.activateAnimation = activate;
                    if (weaponType != null)
                        anims.activateAnimationsByWeaponTypes = new[] { ForWeapon(weaponType, activate) };
                }
                else
                {
                    // The bow. Its shot is a generated clip whose trigger is measured off
                    // the loose, so a skill that fired on any other frame would put the
                    // arrow in the air while the string was still being drawn - and the
                    // kit already has that animation, as the weapon's attack.
                    anims.activateAnimationType = SkillActivateAnimationType.UseAttackAnimation;
                }

                if (spec.CastClip != null)
                {
                    ActionState cast = Action(spec.CastClip);
                    anims.castState = cast;
                    if (weaponType != null)
                        anims.castStatesByWeaponTypes = new[] { ForWeapon(weaponType, cast) };
                }

                ApplyMasks(anims);
                built.Add(anims);
            }
            return built.ToArray();
        }

        /// <summary>
        /// The same animation, tagged with the weapon it belongs to. Copied field by
        /// field rather than cast, because the kit's per-weapon types derive from the
        /// plain ones rather than wrapping them.
        /// </summary>
        private static WeaponActionAnimation ForWeapon(WeaponType weaponType, ActionAnimation from)
        {
            return new WeaponActionAnimation
            {
                weaponType = weaponType,
                state = from.state,
                triggerDurationRates = from.triggerDurationRates,
                durationType = from.durationType,
                fixedDuration = from.fixedDuration,
                extendDuration = from.extendDuration,
                audioClips = from.audioClips,
            };
        }

        private static WeaponActionState ForWeapon(WeaponType weaponType, ActionState from)
        {
            return new WeaponActionState
            {
                weaponType = weaponType,
                clip = from.clip,
                animSpeedRate = from.animSpeedRate,
            };
        }

        /// <summary>One-handed melee: idle on guard, a single swing that connects mid-arc.</summary>
        public static WeaponAnimations BuildMelee(WeaponType weaponType)
        {
            return new WeaponAnimations
            {
                weaponType = weaponType,
                idleState = State("Sword_Idle"),
                moveStates = JogMoves(),
                sprintStates = SprintMoves(),
                walkStates = Moves("Walk_Loop"),
                crouchIdleState = State("Crouch_Idle_Loop"),
                crouchMoveStates = CrouchMoves(),
                crawlIdleState = State("Crawl_Idle_Loop"),
                crawlMoveStates = CrawlMoves(),
                swimIdleState = State("Swim_Idle_Loop"),
                swimMoveStates = Moves("Swim_Fwd_Loop"),
                jumpState = State("Jump_Start"),
                fallState = State("Jump_Loop"),
                landedState = Landing(),
                hurtState = Action("Hit_Chest"),
                deadState = State("Death01"),
                pickupState = Action("PickUp_Table"),
                rightHandAttackAnimations = new[] { Attack("Sword_Attack", 0.45f, audio: DemoAudioWiring.Clips(DemoAudioWiring.SwordSwing)) },
            };
        }

        /// <summary>
        /// Bow: raise, draw, loose, recover — on `Bow_Draw` and `Bow_Release`, purpose-made
        /// for this rig and living in `Assets/Animations`. <see cref="BowShot"/> joins them
        /// into the single clip an attack has to be, and says why.
        ///
        /// Bow is a two-handed weapon, which the kit puts in the right hand however the
        /// bow's own mesh is socketed, so the shot belongs in the right-hand slot.
        ///
        /// Neither clip needs a rotation offset, unlike the Mixamo downloads they replaced:
        /// those were authored aiming along +X and needed Bake Into Pose with a +90 offset
        /// to face down `transform.forward` at all. These aim down it already — the draw
        /// brings the bow arm from 83 degrees off forward at rest to within 5 degrees by
        /// three fifths of the way through, and holds there.
        ///
        /// The pair also bookends properly, which is what makes `Idle_Loop` the right idle
        /// rather than the stand-in it used to be: the draw starts from arms at rest and the
        /// release ends back there, so the bow is only ever raised while it is being used.
        /// </summary>
        public static WeaponAnimations BuildRanged(WeaponType weaponType, bool canCharge)
        {
            var set = new WeaponAnimations
            {
                weaponType = weaponType,
                idleState = State("Idle_Loop"),
                moveStates = JogMoves(),
                sprintStates = SprintMoves(),
                walkStates = Moves("Walk_Loop"),
                crouchIdleState = State("Crouch_Idle_Loop"),
                crouchMoveStates = CrouchMoves(),
                crawlIdleState = State("Crawl_Idle_Loop"),
                crawlMoveStates = CrawlMoves(),
                swimIdleState = State("Swim_Idle_Loop"),
                swimMoveStates = Moves("Swim_Fwd_Loop"),
                jumpState = State("Jump_Start"),
                fallState = State("Jump_Loop"),
                landedState = Landing(),
                hurtState = Action("Hit_Chest"),
                deadState = State("Death01"),
                pickupState = Action("PickUp_Table"),
                // A player holds the draw and looses on release, so the two clips stay two:
                // the draw is the charge and the release is the attack. Everything else
                // fires in one go and gets them joined, because a monster that cannot charge
                // would otherwise start every shot already at full stretch.
                rightHandChargeState = canCharge
                    ? new ActionState { clip = BowCharge() }
                    : new ActionState(),
                rightHandAttackAnimations = canCharge
                    ? new[] { Attack("Bow_Release", LooseAfterRelease / Clip("Bow_Release").length) }
                    : new[] { BowAttack() },
            };
            // The bow draws from the left shoulder with the user's own pair of clips, not the
            // default set's sword reach - see DemoSheathBuilder.
            DemoSheathBuilder.WriteBowHolster(set);
            return set;
        }

        /// <summary>
        /// Joins <c>Bow_Draw</c> and <c>Bow_Release</c> into the one clip an attack needs,
        /// writing it to <see cref="MadeAnimationDir"/> and returning it.
        ///
        /// They have to be one clip because this demo never charges. The kit plays a charge
        /// state only when <c>ShooterPlayerCharacterController</c> asks it to, and the demo
        /// runs the ordinary point-and-click <c>PlayerCharacterController</c>, which has no
        /// such path — nor do monsters, so the bandits would be no better off. Left as a
        /// charge plus an attack, the draw would never play and every shot would begin with
        /// the archer already at full stretch: a pop into the drawn pose, then the loose.
        ///
        /// Joining them is mechanical. Both are humanoid clips over the same bindings, so
        /// the shot is the draw's curves followed by the release's, offset by the draw's
        /// length. The seam needs no blending because the draw ends where the release begins
        /// — hands 1.54m and 1.52m off the ground at one end, 1.52m and 1.51m at the other,
        /// 75.0cm apart against 75.3cm.
        ///
        /// Rebuilt whenever either source is newer than it, so editing a clip and building
        /// the models again is enough.
        /// </summary>
        /// <summary>
        /// The bow's one attack: the joined shot, triggering the arrow where it actually
        /// leaves the string.
        ///
        /// Measured rather than judged. Through `Bow_Release` the bow arm holds within 5
        /// degrees of `transform.forward` until 0.15s, and between 0.15s and 0.20s the
        /// string hand jumps from 0.6 to 3.7 m/s as it snaps back past the ear. That spike
        /// is the loose, 0.175s into the release and so 1.208s into the joined 1.733s clip.
        /// </summary>
        public static ActionAnimation BowAttack()
        {
            float drawLength;
            AnimationClip shot = BowShot(out drawLength);
            if (shot == null)
                return new ActionAnimation();
            return new ActionAnimation
            {
                state = new ActionState { clip = shot },
                // Off the retimed draw, not the authored one: slowing the nock moves the
                // loose later, and a trigger left where it was would fire the arrow while
                // the archer was still drawing.
                triggerDurationRates = new[] { (drawLength + LooseAfterRelease) / shot.length },
                durationType = AnimationDurationType.ByClipLength,
            };
        }

        /// <summary>How far into <c>Bow_Release</c> the arrow leaves the string, in seconds.</summary>
        private const float LooseAfterRelease = 0.175f;

        /// <summary>
        /// The stretch applied to the nock — the moment in <c>Bow_Draw</c> where the string
        /// hand comes down off the raise and onto the string.
        ///
        /// Measured at 59Hz, the draw cruises between 2 and 4.3 m/s for its whole length
        /// except here, where it peaks at <b>10.3 m/s at 0.525s</b> — two and a half times
        /// anything else in the clip, which reads as a snatch rather than a nock. The window
        /// either side of that is where it climbs and falls away again.
        ///
        /// <see cref="NockSlowdown"/> is 2.6 so the peak lands at about 4 m/s: the clip's own
        /// cruising maximum, rather than some number picked for feel. The stretch is eased in
        /// and out over a raised cosine instead of being applied flat, because a flat stretch
        /// only moves the problem — the speed would step by a factor of 2.6 at each end of
        /// the window, which is a worse discontinuity than the one being fixed.
        /// </summary>
        private const float NockFrom = 0.43f;

        private const float NockTo = 0.60f;
        private const float NockSlowdown = 2.6f;

        /// <summary>Output rate of the retimed draw, in frames per second.</summary>
        private const float RetimeRate = 60f;

        /// <summary>
        /// The clip played while a shot is being held: the draw, then the drawn pose held.
        ///
        /// The kit loops a charge state for as long as the button is down, and the draw on
        /// its own does not loop — it starts at rest and ends at full stretch, so it would
        /// snap back and re-draw roughly every second. The tail buys <see cref="HoldTail"/>
        /// seconds before that can happen, which is far longer than a shot is normally held;
        /// hold past it and the raise plays again.
        /// </summary>
        public static AnimationClip BowCharge()
        {
            return BowCharge("Bow_Draw", "Bow_Charge");
        }

        /// <summary>
        /// A charge clip made from any draw: <paramref name="drawName"/> retimed through the nock,
        /// then held. Aimed Shot casts on `Bow_Charge`, Volley on `Bow_Charge_High`.
        /// </summary>
        public static AnimationClip BowCharge(string drawName, string chargeName)
        {
            AnimationClip draw = Clip(drawName);
            if (draw == null)
                return null;
            string path = $"{MadeAnimationDir}/{chargeName}.anim";
            if (!AssetDatabase.IsValidFolder(MadeAnimationDir))
                AssetDatabase.CreateFolder("Assets/OpenMMORPG/Demo", "Animations");
            AnimationClip charge = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (charge == null)
            {
                charge = new AnimationClip();
                AssetDatabase.CreateAsset(charge, path);
            }
            else
            {
                charge.ClearCurves();
            }
            charge.frameRate = draw.frameRate;
            float length = Retime(charge, draw);
            Hold(charge, length, HoldTail);

            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(draw);
            settings.loopTime = false;
            settings.stopTime = length + HoldTail;
            AnimationUtility.SetAnimationClipSettings(charge, settings);
            EditorUtility.SetDirty(charge);
            AssetDatabase.SaveAssetIfDirty(charge);
            return charge;
        }

        /// <summary>How long the drawn pose is held before the charge clip wraps, in seconds.</summary>
        private const float HoldTail = 4f;

        /// <summary>
        /// Extends every curve by repeating the value it ends on, and holds it there.
        ///
        /// The tangents have to be flattened by hand at both ends of the hold. A key added
        /// the ordinary way gets a smoothed tangent worked out from its neighbours, so the
        /// curve leaves the drawn pose carrying the speed the draw arrived at and comes back
        /// to it only in time for the last key — the archer wanders 16cm through the hold
        /// and drifts back. Flat tangents make it the still pose it is supposed to be.
        /// </summary>
        private static void Hold(AnimationClip clip, float from, float through)
        {
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
            {
                AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null || curve.length == 0)
                    continue;
                Keyframe[] keys = curve.keys;
                int last = keys.Length - 1;
                keys[last].outTangent = 0f;
                System.Array.Resize(ref keys, keys.Length + 1);
                keys[last + 1] = new Keyframe(from + through, keys[last].value, 0f, 0f);
                curve.keys = keys;
                AnimationUtility.SetEditorCurve(clip, binding, curve);
            }
        }

        public static AnimationClip BowShot(out float drawLength)
        {
            drawLength = 0f;
            AnimationClip draw = Clip("Bow_Draw");
            AnimationClip release = Clip("Bow_Release");
            if (draw == null || release == null)
                return null;

            string path = $"{MadeAnimationDir}/Bow_Shoot.anim";
            if (!AssetDatabase.IsValidFolder(MadeAnimationDir))
                AssetDatabase.CreateFolder("Assets/OpenMMORPG/Demo", "Animations");

            AnimationClip shot = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (shot == null)
            {
                shot = new AnimationClip();
                AssetDatabase.CreateAsset(shot, path);
            }
            else
            {
                shot.ClearCurves();
            }
            shot.frameRate = draw.frameRate;

            drawLength = Retime(shot, draw);
            Append(shot, release, drawLength);

            // Carried over from the draw, which is where the shot starts: both clips bake
            // the root into the pose and neither travels, so there is nothing to stitch.
            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(draw);
            settings.loopTime = false;
            settings.stopTime = drawLength + release.length;
            AnimationUtility.SetAnimationClipSettings(shot, settings);

            EditorUtility.SetDirty(shot);
            AssetDatabase.SaveAssetIfDirty(shot);
            return shot;
        }

        /// <summary>
        /// Copies the draw onto the shot with the nock stretched, and returns how long it
        /// ended up.
        ///
        /// Resampled rather than key-shifted. Stretching part of a clip means warping time
        /// unevenly, and a key carries tangents in the old timing that no longer describe
        /// the curve through it once its neighbours have moved — the motion overshoots at
        /// exactly the moment being smoothed. Reading the curves through the warp and
        /// writing fresh keys at a fixed rate sidesteps that entirely.
        /// </summary>
        private static float Retime(AnimationClip onto, AnimationClip from)
        {
            // Walk the source clip finely, accumulating output time as it goes, so that the
            // warp is whatever the slowdown curve says it is rather than a formula to invert.
            int steps = Mathf.CeilToInt(from.length * 1000f);
            var sourceAt = new float[steps + 1];
            var outputAt = new float[steps + 1];
            float step = from.length / steps;
            for (int i = 1; i <= steps; ++i)
            {
                float at = step * (i - 0.5f);
                sourceAt[i] = step * i;
                outputAt[i] = outputAt[i - 1] + step * Slowdown(at);
            }
            float length = outputAt[steps];

            // The last frame lands exactly on the end rather than a fraction past it. A
            // frame beyond it would put keys after the join, where the release's own keys
            // already are, and the two interleave into a 35mm lurch at the seam.
            int frames = Mathf.FloorToInt(length * RetimeRate);
            var times = new float[frames + 2];
            for (int f = 0; f <= frames; ++f)
                times[f] = f / RetimeRate;
            times[frames + 1] = length;
            int count = times[frames] >= length - 0.0001f ? frames : frames + 1;
            var back = new float[count + 1];
            for (int f = 0, i = 0; f <= count; ++f)
            {
                float want = Mathf.Min(length, times[f]);
                while (i < steps && outputAt[i + 1] < want)
                    ++i;
                float span = outputAt[i + 1] - outputAt[i];
                float across = span > 0f ? (want - outputAt[i]) / span : 0f;
                back[f] = Mathf.Lerp(sourceAt[i], sourceAt[i + 1], across);
            }

            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(from))
            {
                AnimationCurve source = AnimationUtility.GetEditorCurve(from, binding);
                if (source == null)
                    continue;
                var retimed = new AnimationCurve();
                for (int f = 0; f <= count; ++f)
                    retimed.AddKey(Mathf.Min(length, times[f]), source.Evaluate(back[f]));
                AnimationUtility.SetEditorCurve(onto, binding, retimed);
            }
            return length;
        }

        /// <summary>
        /// How much longer a moment of the draw takes than it was authored to. One outside
        /// the nock, <see cref="NockSlowdown"/> at the middle of it, eased between.
        /// </summary>
        private static float Slowdown(float at)
        {
            if (at <= NockFrom || at >= NockTo)
                return 1f;
            float across = (at - NockFrom) / (NockTo - NockFrom);
            return 1f + (NockSlowdown - 1f) * 0.5f * (1f - Mathf.Cos(across * 2f * Mathf.PI));
        }

        // ---- the high-angle pair: Volley ----------------------------------------

        /// <summary>
        /// Seconds the retimed draw takes: `Bow_Charge` less its held tail. A bow skill whose cast is
        /// the draw casts for at least this long, so the release starts at full stretch.
        /// </summary>
        public const float BowDrawSeconds = 1.169f;

        /// <summary>
        /// Where the arrow leaves the string, as a share of `Bow_Release` (0.175s of 0.7s): a skill
        /// that plays the release on its own triggers here.
        /// </summary>
        public const float ReleaseTrigger = 0.25f;

        /// <summary>
        /// Muscle added to each of Spine, Chest and UpperChest on both of their bending axes at full
        /// draw, and to each arm's Down-Up. Found by search, not by eye (2026-09-25): on the male
        /// model the spine alone raises the arrow line about 20 degrees per unit on each axis, but at
        /// the 1.0 it would take to reach 40 it folds the archer 14cm shorter. Spread between the
        /// spine and the shoulders it reaches about 40 degrees with the head 6cm lower - an archer
        /// leaning back into the shot rather than one bent double.
        /// </summary>
        private const float HighSpine = 0.6f;

        private const float HighArms = 0.28f;

        private static readonly string[] HighSpineMuscles =
        {
            "Spine Front-Back", "Chest Front-Back", "UpperChest Front-Back",
            "Spine Left-Right", "Chest Left-Right", "UpperChest Left-Right",
        };

        /// <summary>
        /// Makes Volley's two clips - `Bow_Draw_High` and `Bow_Release_High` - from the bow's own
        /// pair, and the charge clip its cast plays (`Bow_Charge_High`). Rebuilt every time, like
        /// the joined shot, so an edit to the authored pair carries through.
        ///
        /// Volley shot level until 2026-09-25, and its rain came down out of an empty sky. A volley
        /// is loosed high so the arrows drop onto the ground they are aimed at; that needs the arrow
        /// up at about forty degrees, which neither authored clip has.
        ///
        /// **How:** a lean, added in muscle space and weighted by how far the bow arm is raised.
        /// The source's own `Left Arm Down-Up` curve says that - -0.5 at rest, 0.4 at full draw -
        /// so the lean comes in as the bow comes up and goes out as it comes down, and both ends
        /// are the authored rest pose, untouched: the pair still bookends on `Idle_Loop`, and the
        /// draw still meets the release, because the weight is the same function of the same curve
        /// at the seam. The archer stands bladed 60-70 degrees to the shot, so the arrow line rises
        /// with the spine's SIDE bend as much as its backward one - hence both axes.
        /// </summary>
        public static void EnsureHighAngleClips()
        {
            AnimationClip draw = HighAngle("Bow_Draw");
            AnimationClip release = HighAngle("Bow_Release");
            _clips = null;
            if (draw == null || release == null)
                return;
            BowCharge("Bow_Draw_High", "Bow_Charge_High");
            _clips = null;
        }

        private static AnimationClip HighAngle(string sourceName)
        {
            AnimationClip source = Clip(sourceName);
            if (source == null)
                return null;
            string path = $"{MadeAnimationDir}/{sourceName}_High.anim";
            AnimationClip made = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (made == null)
            {
                made = new AnimationClip();
                AssetDatabase.CreateAsset(made, path);
            }
            else
            {
                made.ClearCurves();
            }
            made.frameRate = source.frameRate;

            EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(source);
            AnimationCurve raise = null;
            foreach (EditorCurveBinding binding in bindings)
            {
                if (binding.propertyName == "Left Arm Down-Up")
                    raise = AnimationUtility.GetEditorCurve(source, binding);
            }
            if (raise == null)
            {
                Debug.LogError($"[{nameof(DemoAnimationSet)}] {sourceName} has no Left Arm Down-Up curve, so " +
                               "there is no telling when the bow is raised; Volley is left without its high clips.");
                return null;
            }

            int frames = Mathf.CeilToInt(source.length * RetimeRate);
            foreach (EditorCurveBinding binding in bindings)
            {
                AnimationCurve curve = AnimationUtility.GetEditorCurve(source, binding);
                if (curve == null)
                    continue;
                float offset = HighOffset(binding.propertyName);
                if (offset == 0f)
                {
                    AnimationUtility.SetEditorCurve(made, binding, curve);
                    continue;
                }
                var leaned = new AnimationCurve();
                for (int f = 0; f <= frames; ++f)
                {
                    float t = Mathf.Min(source.length, f / RetimeRate);
                    leaned.AddKey(t, curve.Evaluate(t) + offset * Raised(raise.Evaluate(t)));
                }
                AnimationUtility.SetEditorCurve(made, binding, leaned);
            }

            AnimationUtility.SetAnimationClipSettings(made, AnimationUtility.GetAnimationClipSettings(source));
            EditorUtility.SetDirty(made);
            AssetDatabase.SaveAssetIfDirty(made);
            return made;
        }

        private static float HighOffset(string muscle)
        {
            if (System.Array.IndexOf(HighSpineMuscles, muscle) >= 0)
                return HighSpine;
            switch (muscle)
            {
                case "Left Arm Down-Up":
                case "Right Arm Down-Up":
                    return HighArms;
                case "Left Shoulder Down-Up":
                case "Right Shoulder Down-Up":
                    return HighArms * 0.5f;
                default:
                    return 0f;
            }
        }

        /// <summary>
        /// How far into the lean the archer is, from the bow arm's own Down-Up: none below -0.2
        /// (the arm still hanging while the arrow is taken), all of it from 0.36, eased between.
        /// </summary>
        private static float Raised(float bowArm)
        {
            float across = Mathf.InverseLerp(-0.2f, 0.36f, bowArm);
            return across * across * (3f - 2f * across);
        }

        /// <summary>Copies one clip's curves onto another, shifted along by <paramref name="at"/>.</summary>
        private static void Append(AnimationClip onto, AnimationClip from, float at)
        {
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(from))
            {
                AnimationCurve source = AnimationUtility.GetEditorCurve(from, binding);
                if (source == null)
                    continue;
                AnimationCurve grown = AnimationUtility.GetEditorCurve(onto, binding) ?? new AnimationCurve();
                foreach (Keyframe key in source.keys)
                {
                    Keyframe moved = key;
                    moved.time = key.time + at;
                    grown.AddKey(moved);
                }
                AnimationUtility.SetEditorCurve(onto, binding, grown);
            }
        }

        /// <summary>
        /// Magic staff: the mage's own idle, and a two-handed swing for the attack.
        ///
        /// The attack was `Spell_Simple_Shoot` until 2026-09-23, back when the staff fired
        /// bolts; it is now a melee weapon (see `DemoItemBuilder`'s Staff), and the spells are
        /// the skills. The swing is UAL2's `Sword_Heavy_A`, a two-handed sweep from the right
        /// hip across the front, measured on the male body in the hips' own frame: the hand is
        /// fastest at 0.53 of the clip (15 m/s) and furthest forward at 0.63, so the blow lands
        /// at <see cref="StaffSwingTrigger"/>. `Sword_Heavy_B` was the other candidate and is a
        /// follow-through, not a blow - it ends with the hands behind the body.
        ///
        /// The idle is <c>Mage_Idle</c>, authored for this demo and living in
        /// <see cref="DemoArtCollector.ClipDir"/> rather than in the library - the first
        /// hand-made clip the staff set uses. It replaced the library's
        /// <c>Spell_Simple_Idle_Loop</c>, which is still the **cast** pose in
        /// <see cref="BuildDefault"/> and is left alone there.
        ///
        /// Keyed off the weapon, not the class, because that is the only thing the kit's
        /// animation sets know about: anyone holding a staff stands like this, and in this
        /// demo that is the mage.
        /// </summary>
        public static WeaponAnimations BuildMagic(WeaponType weaponType)
        {
            return new WeaponAnimations
            {
                weaponType = weaponType,
                idleState = State("Mage_Idle"),
                moveStates = JogMoves(),
                sprintStates = SprintMoves(),
                walkStates = Moves("Walk_Loop"),
                crouchIdleState = State("Crouch_Idle_Loop"),
                crouchMoveStates = CrouchMoves(),
                crawlIdleState = State("Crawl_Idle_Loop"),
                crawlMoveStates = CrawlMoves(),
                swimIdleState = State("Swim_Idle_Loop"),
                swimMoveStates = Moves("Swim_Fwd_Loop"),
                jumpState = State("Jump_Start"),
                fallState = State("Jump_Loop"),
                landedState = Landing(),
                hurtState = Action("Hit_Chest"),
                deadState = State("Death01"),
                pickupState = Action("PickUp_Table"),
                rightHandAttackAnimations = new[] { StaffSwing() },
            };
        }

        /// <summary>Where the staff's blow lands, as a fraction of the swing. See <see cref="BuildMagic"/>.</summary>
        public const float StaffSwingTrigger = 0.58f;

        /// <summary>
        /// Seconds the mage holds after a staff swing before the next can start. The swing is
        /// 0.73s, which on its own would be a blow and a half a second - a flurry, which is not
        /// what a staff is for. Held, it comes round about as often as the warrior's sword and
        /// hits for a fraction of it, which is the point: between spells, not instead of them.
        /// </summary>
        public const float StaffSwingRecovery = 0.55f;

        public static ActionAnimation StaffSwing()
        {
            ActionAnimation swing = Attack("Sword_Heavy_A", StaffSwingTrigger,
                                           audio: DemoAudioWiring.Clips(DemoAudioWiring.SwordSwing));
            swing.extendDuration = StaffSwingRecovery;
            return swing;
        }
    }
}
