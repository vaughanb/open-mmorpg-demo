using MultiplayerARPG.GameData.Model.Playables;
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
        /// Scanned after the library, so anything the Quaternius library does not cover can
        /// simply be dropped in here. That is how the bow got real archery - see
        /// <see cref="BuildRanged"/>. A name found in the library wins, so a clip added
        /// here cannot silently shadow a library one. Inside the demo, because the demo
        /// must carry everything it plays.
        /// </summary>
        private const string ExtraAnimationDir = "Assets/OpenMMORPG/Demo/Animations";

        private const string WeaponTypeDir = "Assets/OpenMMORPG/Demo/GameData/Resources/WeaponTypes";

        /// <summary>
        /// Where the joined bow shot is written. Under Demo because it is generated here and
        /// the demo is what ships; the two clips it is made of stay where they were authored.
        /// </summary>
        private const string MadeAnimationDir = "Assets/OpenMMORPG/Demo/Animations";

        private static AnimationClip[] _clips;

        public static AnimationClip Clip(string name)
        {
            if (_clips == null)
            {
                var found = new System.Collections.Generic.List<AnimationClip>();
                // The clips DemoArtCollector has already extracted into the demo come
                // first, so a rebuilt character points at the demo's own copy and never
                // back at the library; the library itself covers whatever is not yet
                // extracted, and may be absent in a project that only has the demo.
                if (AssetDatabase.IsValidFolder(DemoArtCollector.ClipDir))
                {
                    foreach (string guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { DemoArtCollector.ClipDir }))
                        CollectClips(AssetDatabase.GUIDToAssetPath(guid), found);
                }
                if (AssetDatabase.LoadAssetAtPath<Object>(LibraryPath) != null)
                    CollectClips(LibraryPath, found);

                if (AssetDatabase.IsValidFolder(ExtraAnimationDir))
                {
                    foreach (string guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { ExtraAnimationDir }))
                        CollectClips(AssetDatabase.GUIDToAssetPath(guid), found);
                    foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { ExtraAnimationDir }))
                        CollectClips(AssetDatabase.GUIDToAssetPath(guid), found);
                }
                _clips = found.ToArray();
            }

            foreach (AnimationClip clip in _clips)
            {
                if (clip.name == name)
                    return clip;
            }
            Debug.LogError($"[{nameof(DemoAnimationSet)}] No clip named \"{name}\" in {LibraryPath} " +
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
                        built.Add(BuildRanged(weaponType, canCharge));
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

        /// <summary>Magic staff: the cast pose idles, and the shot is the ranged attack.</summary>
        public static WeaponAnimations BuildMagic(WeaponType weaponType)
        {
            return new WeaponAnimations
            {
                weaponType = weaponType,
                idleState = State("Spell_Simple_Idle_Loop"),
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
                rightHandAttackAnimations = new[] { Attack("Spell_Simple_Shoot", 0.45f) },
            };
        }
    }
}
