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

        public static AnimState State(string clip, float speedRate = 0f)
        {
            return new AnimState { clip = Clip(clip), animSpeedRate = speedRate };
        }

        public static ActionState Action(string clip, float speedRate = 0f)
        {
            return new ActionState { clip = Clip(clip), animSpeedRate = speedRate };
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

        /// <summary>Eight-way jog - the standing run, and the set every character uses most.</summary>
        public static MoveStates JogMoves()
        {
            return Moves8("Jog_Fwd_Loop", "Jog_Bwd_Loop", "Jog_Left_Loop", "Jog_Right_Loop",
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
        /// </summary>
        public static MoveStates CrouchMoves()
        {
            return Moves8("Crouch_Fwd_Loop", "Crouch_Bwd_Loop", "Crouch_Left_Loop", "Crouch_Right_Loop",
                          "Crouch_Fwd_L_Loop", "Crouch_Fwd_R_Loop", "Crouch_Bwd_L_Loop", "Crouch_Bwd_Loop");
        }

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
        /// The ladder set. On a ladder the kit only ever asks for up and down - the sideways
        /// shuffles are wired for completeness - and the vertical slots are the ones that
        /// matter, which <see cref="Moves8"/> fills with the forward clip, so they are set
        /// by hand.
        /// </summary>
        public static MoveStates ClimbMoves()
        {
            MoveStates moves = Moves8("Climb_Up_Loop", "Climb_Down_Loop", "Climb_Left_Loop", "Climb_Right_Loop",
                                      "Climb_Up_Loop", "Climb_Up_Loop", "Climb_Down_Loop", "Climb_Down_Loop");
            moves.upState = State("Climb_Up_Loop");
            moves.downState = State("Climb_Down_Loop");
            return moves;
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
            return new DefaultAnimations
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
                // the pull-up onto whatever the ladder leans against. It ships nothing for
                // stepping onto a ladder from the top, so that is instant.
                //
                // Every clip here has to run longer than a second. The kit moves the
                // character to the enter or exit point with a lerp whose factor is
                // `time - start / duration`, precedence and all: for a duration over a
                // second that is a large positive number, which clamps to one and puts the
                // character where they are going, and for anything shorter it is a large
                // negative one, which clamps to zero and leaves them where they were - so a
                // sub-second exit at the top drops the player back down the ladder. The
                // ledge pull-up is 0.63s and is played at half speed for exactly that
                // reason, which also makes it read as an effort rather than a hop.
                climbIdleState = State("Climb_Idle_Loop"),
                climbMoveStates = ClimbMoves(),
                climbBottomEnterExitStates = new EnterExitStates
                {
                    enterState = Action("Climb_Enter"),
                    exitState = Action("Climb_Exit"),
                },
                climbTopEnterExitStates = new EnterExitStates
                {
                    exitState = Action("ClimbLedge", 0.5f),
                },

                jumpState = State("Jump_Start"),
                fallState = State("Jump_Loop"),
                landedState = State("Jump_Land"),

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
                var weaponType = AssetDatabase.LoadAssetAtPath<WeaponType>(AssetDatabase.GUIDToAssetPath(guid));
                if (weaponType == null)
                    continue;
                switch (weaponType.name)
                {
                    case "Staff":
                        built.Add(BuildMagic(weaponType));
                        break;
                    case "Bow":
                        built.Add(BuildRanged(weaponType, canCharge && DemoItemBuilder.BowsCharge));
                        break;
                    case "Unarmed":
                        // Left out on purpose. With no set of its own it falls through to
                        // the default animations, which are the empty-handed ones.
                        break;
                    default:
                        built.Add(BuildMelee(weaponType));
                        break;
                }
            }
            if (built.Count == 0)
                Debug.LogError($"[{nameof(DemoAnimationSet)}] No weapon types under {WeaponTypeDir}, so every " +
                               "character will animate as though empty-handed. Run Build Items first.");
            return built.ToArray();
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
            foreach (DemoSkillBuilder.SkillSpec spec in DemoSkillBuilder.All())
            {
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
                    ActionAnimation activate = Attack(spec.Clip, spec.Trigger, spec.ClipSpeed,
                                                      DemoAudioWiring.SkillClips(spec.Name, spec.Audio));
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
                landedState = State("Jump_Land"),
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
            return new WeaponAnimations
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
                landedState = State("Jump_Land"),
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
            AnimationClip draw = Clip("Bow_Draw");
            if (draw == null)
                return null;
            string path = $"{MadeAnimationDir}/Bow_Charge.anim";
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
                landedState = State("Jump_Land"),
                hurtState = Action("Hit_Chest"),
                deadState = State("Death01"),
                pickupState = Action("PickUp_Table"),
                rightHandAttackAnimations = new[] { StaffSwing() },
            };
        }
    }
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
