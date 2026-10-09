using System.Collections.Generic;
using MultiplayerARPG.GameData.Model.Playables;
using UnityEditor;
using UnityEngine;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// Weapon sheathing for the demo: the socket on the back, where each kind of weapon rides
    /// on it, and the draw and sheathe clips that carry a weapon between hand and back.
    ///
    /// **The kit already does the mechanism** - see <see cref="DemoWeaponSheathing"/>. Every
    /// weapon item has `sheathModels`, a second set of models it shows on whatever socket they
    /// name while the character is sheathed (or the item sits in a weapon set that is not the
    /// active one), and `DefaultAnimations` has holster clips for each hand. This builder
    /// supplies the data.
    ///
    /// **Everything goes on the back, because that is what the library's clip does.** UAL1's
    /// `Sword_Enter` is a draw from over the right shoulder: the hand rises to above and behind
    /// the head, closes on a hilt, and sweeps down and forward into `Sword_Idle` - it ends on
    /// that clip's first pose to the centimetre. `Sword_Exit` is the same in reverse and ends
    /// on the unarmed stance. Neither library has a hip draw, so a hip scabbard would mean a
    /// clip that reaches for nothing. One reach, then, and every weapon rides where it ends -
    /// **except the bow**, which has a reach of its own: the user authored `Bow_Unsheath` and
    /// `Bow_Sheath` (2026-10-04) as the left hand reaching over the *left* shoulder, a bow being
    /// held in the left hand, so the bow rides on a second socket, `SheathBackLeft`, measured from
    /// that clip in exactly the same way and carried the mirror-image way down the back.
    ///
    /// **The socket's origin is where the hand closes.** `SheathBack` hangs off `spine_03` at the
    /// right hand's weapon socket at the hold in `Sword_Enter` (the hand is still, closed on the
    /// hilt, from 0.5s to 0.8s of it). Measured per body, so the male and female each get their own.
    ///
    /// **The weapons do not hang from it, though.** Until 2026-10-05 each one was hung by its grip
    /// from that very spot and laid along the hand's axis there, so nothing moved at the swap - but
    /// the spot is 0.74m above the hips, above the top of the head, and the axis is the reach's,
    /// 45 degrees off vertical. The hilt stood behind the head with the blade half a metre out to the
    /// left, the axe head was at ear height and the staff and bow towered over the character. The
    /// user asked for weapons where they would really be carried, with the hand only roughly at
    /// them. So **the socket's frame is the body's** (read in the idle: Z out of the back, Y up, X to
    /// the character's left) and each weapon is laid in it at a realistic spot - a hilt over the
    /// shoulder, a haft at the shoulder blade, a bow handle on the spine - as an offset from the
    /// hand's spot. The weapon now moves at the swap: 12cm for a sword's grip, 0.4m for a bow's
    /// handle (along its own length - the left hand's spot is right on the upper limb).
    ///
    /// **How far off the back each weapon rides is measured against every outfit in every pose, not
    /// against the bare body** - the first version was, and a ranger in the peasant tunic had his bow
    /// come out through its hem (reported 2026-10-04): a tunic flares below the belt, a robe hangs off
    /// the hips, and plate stands off the back. So the search lives in the editor and its results are
    /// the constants here: the back surface of nine bodies (peasant, ranger, knight, wizard, noble,
    /// warden, both genders) baked at idle, a walk, three phases of a jog and a crouch, into a 3cm
    /// heightfield in the socket's frame, and each weapon's own vertices tested against it over a
    /// grid of standoff and lean. **Clearance is not the goal, though: closeness is.** Searched for
    /// clearance (a margin over every outfit, through a heightfield dilated by a cell), every weapon
    /// stood 6-9cm off the nearest part of the back with a typical gap of 15-20cm, and read as
    /// floating - twice, the second time after the user had said so of the bow. The user then set
    /// both bows by hand, 9cm in (2026-10-05), preferring a little clipping into some armour to
    /// weapons that float. So the target is the closeness of that bow, measured on the undilated
    /// surface: on the everyday outfits (peasant, ranger) standing, the nearest point of the weapon
    /// dips about 1cm in and a typical point sits 5-10cm off; plate and robes take 6-8cm of clipping.
    /// Each weapon gets the standoff and lean that brings it nearest the body within that.
    /// Judge a change by a side-view render on several outfits.
    /// </summary>
    public static class DemoSheathBuilder
    {
        public const string SocketBack = "SheathBack";

        /// <summary>
        /// The bow's own socket, at the left shoulder. A bow is held in the **left** hand (the item
        /// is a right-hand weapon to the kit, but its model hangs off `LeftHand`), so the draw the
        /// user authored for it (2026-10-04, `Bow_Unsheath`/`Bow_Sheath`) reaches over the left
        /// shoulder - the sword's reach in a mirror. Its hand closes at the left shoulder rather than
        /// the right, so it has a socket of its own, measured the same way and with the same body axes.
        /// </summary>
        public const string SocketBackLeft = "SheathBackLeft";

        /// <summary>The draw: a hand reaching over the shoulder, closing on a hilt, sweeping forward.</summary>
        public const string DrawClip = "Sword_Enter";

        /// <summary>The same reach in reverse, ending on the unarmed stance.</summary>
        public const string SheatheClip = "Sword_Exit";

        /// <summary>
        /// The bow's draw and sheathe, hand-authored by the user in `Demo/Animations` (never
        /// regenerate or overwrite them). The same 1.3s and the same two timings as the sword's
        /// pair - checked, not assumed: the left hand closes on the bow at about 0.6s of the draw
        /// (the hold runs 0.5-0.7s) and seats it at about 0.7s of the sheathe (0.65-0.8s) - so
        /// they share <see cref="DrawTrigger"/> and <see cref="SheatheTrigger"/>. Both start and
        /// end on the plain idle, which is what a bow stands in.
        /// </summary>
        public const string BowDrawClip = "Bow_Unsheath";
        public const string BowSheatheClip = "Bow_Sheath";

        /// <summary>
        /// The moment of `Bow_Unsheath` the left socket is measured at: the middle of its hold.
        /// The grip moves less than 2cm between 0.5s and 0.7s.
        /// </summary>
        private const float BowHoldSeconds = 0.6f;

        private const string SpineBone = "spine_03";

        /// <summary>The stance the sockets' axes are read in - see <see cref="Measure"/>.</summary>
        private const string IdleClip = "Idle_Loop";
        private const string ModelDir = "Assets/OpenMMORPG/Demo/Prefabs/GamePlay/CharacterModels";

        /// <summary>
        /// The moment of the draw clip the socket is measured at, in seconds of the authored
        /// clip: the middle of the hold.
        /// </summary>
        private const float HoldSeconds = 0.65f;

        /// <summary>
        /// How much faster than authored the reach plays: 1.3s is long in a fight. Applied once -
        /// the kit's holster routine plays these at a multiplier of 1 - so the trigger below is
        /// a fraction of the *authored* clip and does not change with this.
        /// </summary>
        public const float SpeedRate = 1.4f;

        /// <summary>
        /// Fraction of the draw clip at which the weapon is in the hand. The hand closes on the
        /// hilt at about 0.55s of the 1.3s clip and starts the pull at 0.8s.
        /// </summary>
        public const float DrawTrigger = 0.46f;

        /// <summary>
        /// Fraction of the sheathe clip at which the weapon is on the back: the clip is the draw
        /// run backwards, so the hand is back on the hilt's spot at 1.3 - 0.6s.
        /// </summary>
        public const float SheatheTrigger = 0.54f;

        // ---- the carries ------------------------------------------------------
        // Each one is a point of the weapon, where that point goes - from the socket, so from the
        // hand at the hold, in the socket's body frame: X to the character's LEFT, Y up, Z out of
        // the back - how far the weapon leans off vertical (positive: its lower end toward the
        // character's left) and how far its lower end leans out from the back (negative leans it
        // in). See TryCarry, and the type's remarks for how the figures were found.
        //
        // The Z figures are the closeness the user set by hand on the two bows (2026-10-05) - see
        // BowAt - carried over to the rest: each weapon's nearest point dips about a centimetre into
        // the everyday outfits standing, as the bow's does, and plate or a robe takes 6-8cm of it.

        /// <summary>
        /// The sword: grip 12cm below the hand's spot and 4cm toward the right shoulder, so the hilt
        /// stands over the shoulder beside the head; the blade runs 25 degrees off vertical down to
        /// the left hip, its flat on the back. (Before 2026-10-05 the grip was the hand's spot
        /// itself - above the top of the head - and the blade lay along the reach, 45 degrees, its
        /// tip half a metre out to the left.)
        /// </summary>
        private static readonly Vector3 SwordAt = new Vector3(-0.04f, -0.12f, 0.01f);
        private const float SwordTilt = 25f;
        private const float SwordPitch = 0f;

        /// <summary>Axes and picks: gripped just under the head, which sits at the right shoulder blade; the haft runs down to the left.</summary>
        private static readonly Vector3 AxeAt = new Vector3(-0.04f, -0.24f, 0.02f);
        private const float AxePitch = 2f;
        private static readonly Vector3 PickAt = new Vector3(-0.04f, -0.24f, 0.04f);
        private const float PickPitch = 0f;
        private const float HaftTilt = 25f;

        /// <summary>
        /// The staff: its middle on the spine at mid-back, head over the right shoulder, foot by the
        /// left knee - leaned 6 degrees in at the foot, so it follows the back down past the hip.
        /// </summary>
        private static readonly Vector3 StaffAt = new Vector3(0.08f, -0.44f, 0.03f);
        private static readonly Vector3 StaffMiddle = new Vector3(0f, 0.22f, 0f);
        private const float StaffTilt = 20f;
        private const float StaffPitch = -6f;

        /// <summary>
        /// The bow, on the left socket: its handle on the spine at the shoulder blades, the upper limb
        /// over the left shoulder - where the left hand reaches - the lower one down to the right hip.
        /// **Set by the user by hand (2026-10-05)** on both bows, from (-0.13, -0.38, 0.12): 2cm toward
        /// the right and 9cm in, because every weapon floated off the back. These figures reproduce
        /// that pose, and the rest were brought in to match it - except that it now rides
        /// <see cref="BowOverQuiver"/> further out, on top of the quiver.
        /// </summary>
        private static readonly Vector3 BowAt = new Vector3(-0.15f, -0.38f, 0.03f + BowOverQuiver);
        private const float BowTilt = -25f;
        private const float BowPitch = 0f;

        /// <summary>
        /// How far the bow was lifted off the back (2026-10-05, the same day) to lie over the quiver
        /// rather than through it. The quiver hangs from the right shoulder to the left hip and the bow
        /// from the left shoulder to the right hip, so they cross over the shoulder blades, where the
        /// back is fullest; the bow at the user's 0.03 was 1-2cm off the back there, and no placement of
        /// a quiver with its arrows reachable over the right shoulder fitted under it - a 3cm-thin one
        /// still went 1cm through the bow. Strapped on, the quiver is the inner one and the bow rests on
        /// it: this is the least lift that clears the quiver's leather by 5mm on the male body (2.6cm on the
        /// female; the arrows by 2cm on both), measured mesh against mesh. They hang off the same bone,
        /// so no pose changes it.
        ///
        /// **0.03 -> 0.05 on 2026-10-06**, when the user's own quiver replaced the flat pouch: it is a
        /// real quiver, 11cm deep at the collar, and the bow crosses it there. Measured by ray casts on
        /// the baked male body (idle), the bow went 6.9cm into the collar at the old lift; with the quiver
        /// thinned to 80% (the model itself, see `Art/Weapons/Source~`) and the bow 2cm further out it
        /// goes about 4cm into the lip, only where the two are widest, and still sits 4.8cm off the back at
        /// its closest. Clearing it entirely would need 13cm, which is a bow floating off the back.
        /// </summary>
        private const float BowOverQuiver = 0.05f;

        /// <summary>
        /// The quiver (see `DemoWeaponBuilder.BuildQuiver`: origin at the mouth, +Y up to it, +Z its
        /// outer face) on the right socket, worn with every bow: its mouth on the spine just below the
        /// neck, leaning 28 degrees so its foot is at the left hip and the arrows stand up behind the
        /// right shoulder - their nocks 7cm to the right of and 9cm above where the right hand closes
        /// over the shoulder. The leather is pitched 4 degrees so its foot stands a little off the hip.
        ///
        /// Searched, like the weapons, against the back surface of eight bodies at idle (peasant,
        /// ranger, plate, robe and the bare bodies, both genders): mouth, lean, pitch and size, the
        /// arrows required to come out behind the shoulder at about neck height, the nearest point of
        /// the leather dipping no more than 1cm into the everyday outfits (it clears plate and robes),
        /// and least bow lift (<see cref="BowOverQuiver"/>). The authored lean, 40 degrees, put the foot
        /// past the side of the waist where the left arm swings; mouth at mid-back let the arrows slip
        /// under the bow but no hand could reach them there.
        /// </summary>
        private static readonly Vector3 QuiverAt = new Vector3(0.08f, -0.20f, 0.011f);
        private const float QuiverTilt = 28f;
        private const float QuiverPitch = -4f;

        /// <summary>The quiver prefab, under `Prefabs/GamePlay/Equipments`.</summary>
        public const string QuiverPrefab = "Quiver";

        /// <summary>
        /// The shield's centre (the prefab's origin), on the spine at the small of the back, at the
        /// sword's standoff: the plate's inner face is 3.3cm out from the origin, so it lies just outside
        /// the blade (half its 3.8cm thickness, and 1.4cm) and hides it. The shield is 85cm across; at
        /// 0.47 down its rim reached the neck and covered the grip, so it sits 0.55 down and the hilt
        /// rises over the rim - the thing the draw reaches for - with the blade's tip out at the lower
        /// left. Clear of the everyday outfits and plate standing; a jog's buttocks touch its lower
        /// rim by 1-2cm.
        /// </summary>
        private static readonly Vector3 ShieldAt = new Vector3(0.08f, -0.55f, 0.01f);

        // ---- the clips --------------------------------------------------------

        /// <summary>
        /// The holster states every humanoid carries. The same for the right hand, the left and a
        /// shield: the library has one reach, and when both hands have something the kit plays
        /// the right hand's clip on both layers anyway. Overwritten each run - these are
        /// generated, not tuned by hand.
        /// </summary>
        public static void WriteHolsters(DefaultAnimations anims)
        {
            if (anims == null)
                return;
            anims.rightHandWeaponSheathingAnimation = Holster();
            anims.leftHandWeaponSheathingAnimation = Holster();
            anims.leftHandShieldSheathingAnimation = Holster();
        }

        private static HolsterAnimation Holster()
        {
            return new HolsterAnimation
            {
                sheathState = DemoAnimationSet.Action(SheatheClip, SpeedRate),
                sheathedDurationRate = SheatheTrigger,
                unsheathState = DemoAnimationSet.Action(DrawClip, SpeedRate),
                unsheathedDurationRate = DrawTrigger,
            };
        }

        /// <summary>
        /// The bow's own draw and sheathe - the left hand over the left shoulder, authored by the user -
        /// for the **Bow weapon set's** right-hand slot: the kit looks a weapon's own holster up
        /// by its weapon type first and falls back to the default set's (the sword's reach) only when
        /// that has no clip, so this is all it takes for a bow to leave the sword's draw behind. The
        /// bow is a right-hand weapon to the kit, hence the right-hand slot, whichever hand it is held in.
        /// </summary>
        public static void WriteBowHolster(WeaponAnimations set)
        {
            if (set == null)
                return;
            set.rightHandWeaponSheathingAnimation = new HolsterAnimation
            {
                sheathState = DemoAnimationSet.Action(BowSheatheClip, SpeedRate),
                sheathedDurationRate = SheatheTrigger,
                unsheathState = DemoAnimationSet.Action(BowDrawClip, SpeedRate),
                unsheathedDurationRate = DrawTrigger,
            };
        }

        // ---- where each weapon rides ------------------------------------------

        /// <summary>
        /// The pose of a weapon type's sheath model on its socket. Prefabs follow the weapon socket
        /// convention (grip at the origin, length along +Y, flat facing Z): blades hang point down
        /// with the flat against the back, and axes, picks and the staff are turned head up, so the
        /// head rides at the shoulder. A bow is hung by its handle, the prefab's origin, the flat of
        /// its limbs against the back. A shield faces out.
        /// </summary>
        public static bool TryCarry(string weaponType, out string socket, out Vector3 position, out Vector3 euler)
        {
            Quaternion rotation;
            socket = SocketBack;
            switch (weaponType)
            {
                case "Sword":
                    Lay(Vector3.zero, false, SwordAt, SwordTilt, SwordPitch, out position, out rotation);
                    break;
                case "Axe":
                    Lay(new Vector3(0f, 0.58f, 0f), true, AxeAt, HaftTilt, AxePitch, out position, out rotation);
                    break;
                case "Pickaxe":
                    Lay(new Vector3(0f, 0.53f, 0f), true, PickAt, HaftTilt, PickPitch, out position, out rotation);
                    break;
                case "Staff":
                    Lay(StaffMiddle, true, StaffAt, StaffTilt, StaffPitch, out position, out rotation);
                    break;
                case "Bow":
                    // On the left shoulder's socket (see SocketBackLeft): the left hand draws it.
                    socket = SocketBackLeft;
                    Lay(Vector3.zero, false, BowAt, BowTilt, BowPitch, out position, out rotation);
                    break;
                case "Shield":
                    // Faces out: the prefab's face is along +X, the socket's Z is out of the back.
                    rotation = Quaternion.Euler(0f, -90f, 0f);
                    position = ShieldAt;
                    break;
                default:
                    position = Vector3.zero;
                    euler = Vector3.zero;
                    return false;
            }
            euler = rotation.eulerAngles;
            return true;
        }

        /// <summary>
        /// Where the quiver rides: on <see cref="SocketBack"/>, the same in the hand set and the sheathed
        /// set, so it does not move when the bow is drawn. False for anything but a bow.
        /// </summary>
        public static bool TryQuiverCarry(string weaponType, out string socket, out Vector3 position, out Vector3 euler)
        {
            socket = SocketBack;
            position = QuiverAt;
            euler = (Quaternion.Euler(0f, 0f, QuiverTilt) * Quaternion.Euler(QuiverPitch, 0f, 0f)).eulerAngles;
            return weaponType == "Bow";
        }

        /// <summary>
        /// Lays a long weapon on the back: the prefab's <paramref name="point"/> goes to
        /// <paramref name="at"/>, the weapon runs <paramref name="tilt"/> degrees off vertical (its
        /// lower end toward the character's left for a positive figure) and its lower end leans
        /// <paramref name="pitch"/> degrees out from the back. <paramref name="headUp"/> turns the
        /// prefab's +Y end to the top - and its face into the back, which keeps an axe's edge
        /// facing out over the shoulder.
        /// </summary>
        private static void Lay(Vector3 point, bool headUp, Vector3 at, float tilt, float pitch, out Vector3 position, out Quaternion rotation)
        {
            float t = tilt * Mathf.Deg2Rad;
            float p = pitch * Mathf.Deg2Rad;
            Vector3 down = new Vector3(Mathf.Sin(t), -Mathf.Cos(t), 0f);
            Vector3 lower = (down * Mathf.Cos(p) + Vector3.forward * Mathf.Sin(p)).normalized;
            Vector3 axis = headUp ? -lower : lower;
            Vector3 face = Vector3.ProjectOnPlane(headUp ? Vector3.back : Vector3.forward, axis).normalized;
            rotation = Quaternion.LookRotation(face, axis);
            position = at - rotation * point;
        }

        // ---- the socket -------------------------------------------------------

        /// <summary>
        /// Hangs the back socket off `spine_03` of an assembled model and returns its container.
        /// Measured from the draw clip on this very body; falls back to the male's measurement
        /// (spine-local) if the clip or the rig cannot be read, with a warning.
        /// </summary>
        public static EquipmentContainer BackContainer(GameObject model, Transform rightHandSocket)
        {
            return Container(model, rightHandSocket, SocketBack, DrawClip, HoldSeconds,
                             new Vector3(0.112f, 0.379f, -0.154f), Quaternion.Euler(BodyFallbackEuler));
        }

        /// <summary>
        /// The bow's socket at the left shoulder, measured off the left hand's grip at the hold in
        /// the user's `Bow_Unsheath`. See <see cref="SocketBackLeft"/>.
        /// </summary>
        public static EquipmentContainer BackLeftContainer(GameObject model, Transform leftHandSocket)
        {
            return Container(model, leftHandSocket, SocketBackLeft, BowDrawClip, BowHoldSeconds,
                             BowFallbackPosition, Quaternion.Euler(BodyFallbackEuler));
        }

        /// <summary>The male body's measurement of the left socket's position, spine-local, for when it cannot be measured.</summary>
        private static readonly Vector3 BowFallbackPosition = new Vector3(-0.101f, 0.392f, -0.166f);

        /// <summary>Both sockets' axes on the male body (the body's, at idle), spine-local, for when they cannot be measured.</summary>
        private static readonly Vector3 BodyFallbackEuler = new Vector3(1.3f, 180f, 0f);

        private static EquipmentContainer Container(GameObject model, Transform handSocket, string socketName, string clipName,
                                                    float holdSeconds, Vector3 fallbackPosition, Quaternion fallbackRotation)
        {
            Transform spine = Find(model.transform, SpineBone);
            if (spine == null)
            {
                Debug.LogError($"[{nameof(DemoSheathBuilder)}] No \"{SpineBone}\" on \"{model.name}\" to hang \"{socketName}\" from.");
                return null;
            }
            Transform socket = Find(spine, socketName);
            if (socket == null)
            {
                socket = new GameObject(socketName).transform;
                socket.SetParent(spine, false);
            }

            Vector3 position;
            Quaternion rotation;
            if (!Measure(model, handSocket, spine, clipName, holdSeconds, out position, out rotation))
            {
                Debug.LogWarning($"[{nameof(DemoSheathBuilder)}] Could not measure \"{socketName}\" on \"{model.name}\" " +
                                 $"(no \"{clipName}\", or no humanoid rig); using the male body's measurement.");
                position = fallbackPosition;
                rotation = fallbackRotation;
            }
            socket.localPosition = position;
            socket.localRotation = rotation;
            socket.localScale = Vector3.one;
            return new EquipmentContainer { equipSocket = socketName, transform = socket };
        }

        /// <summary>
        /// Reads a socket off the hold in a draw clip: the weapon socket of the hand that makes the
        /// draw, expressed in `spine_03`'s space, with the frame described in the type's remarks.
        /// The pose is sampled onto the model and then undone, because every later build step
        /// reads the bind pose.
        /// </summary>
        private static bool Measure(GameObject model, Transform hand, Transform spine, string clipName, float holdSeconds,
                                    out Vector3 localPosition, out Quaternion localRotation)
        {
            localPosition = Vector3.zero;
            localRotation = Quaternion.identity;
            if (model == null || hand == null)
                return false;
            AnimationClip clip = DemoAnimationSet.Clip(clipName);
            var animator = model.GetComponent<Animator>();
            if (clip == null || animator == null || !animator.isHuman)
                return false;
            Transform rightLeg = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            Transform leftLeg = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            if (rightLeg == null || leftLeg == null)
                return false;

            Transform[] all = model.GetComponentsInChildren<Transform>(true);
            var positions = new Vector3[all.Length];
            var rotations = new Quaternion[all.Length];
            for (int i = 0; i < all.Length; ++i)
            {
                positions[i] = all[i].localPosition;
                rotations[i] = all[i].localRotation;
            }

            // The frame: the body's own axes as it stands, read in the idle - Z out of the back, Y up,
            // X to the character's left - taken from the thighs so the library's turn of the whole
            // body (it faces -Z) does not matter. Not the hand's axes at the hold: those are 45
            // degrees off vertical and turned toward the shoulder, and weapons laid along them sat
            // along the reach rather than down the back.
            AnimationClip idle = DemoAnimationSet.Clip(IdleClip);
            if (idle != null)
                idle.SampleAnimation(model, 0f);
            Vector3 right = rightLeg.position - leftLeg.position;
            right.y = 0f;
            right.Normalize();
            Vector3 forward = Vector3.Cross(right, Vector3.up);
            localRotation = Quaternion.Inverse(spine.rotation) * Quaternion.LookRotation(-forward, Vector3.up);

            // The origin: where the hand closes at the hold.
            clip.SampleAnimation(model, holdSeconds);
            localPosition = spine.InverseTransformPoint(hand.position);

            for (int i = 0; i < all.Length; ++i)
            {
                all[i].localPosition = positions[i];
                all[i].localRotation = rotations[i];
            }
            return true;
        }

        private static Transform Find(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name)
                    return t;
            }
            return null;
        }

        // ---- patching what is already built -----------------------------------

        /// <summary>
        /// Gives sheathing to a project whose models and items are already built, and touches
        /// nothing else - the same bargain as `Refresh Skill Animations`: the full build is the
        /// whole entity chain, and this is two sockets, the holster clips and a list of models. Safe
        /// to run again: the sockets are re-measured in place and the holsters rewritten, and a
        /// sheath pose already on an item is kept (see <see cref="ResetSheathPosesMenu"/>).
        ///
        /// Run **Collect Demo Art** after, so the sword's two library clips are extracted into the
        /// demo (the bow's are the user's own, already there), and **Build Map Server** if the MMO
        /// flow is in use.
        /// </summary>
        [MenuItem("Open MMORPG/Demo/Build Weapon Sheathing")]
        public static void BuildAll()
        {
            int models = PatchModels();
            int items = DemoItemBuilder.RefreshSheathModels();
            // The Z key: an entry on the GameInstance prefab's input settings.
            DemoControllerBuilder.ConfigureKeys();
            // The draw and sheathe sounds, from WeaponSheath*/WeaponUnsheath* in Demo/Audio.
            int sounds = DemoAudioWiring.WireSheathSoundsOnPlayers();
            // New clips arrive with the importer's defaults; every demo clip is preloaded in the
            // background, or its first play stalls the main thread.
            DemoDatabaseWiring.PreloadAudio();
            AssetDatabase.SaveAssets();
            Debug.Log($"[{nameof(DemoSheathBuilder)}] Sheathing on {models} character model(s) and {items} item(s), " +
                      $"the {DemoWeaponSheathing.KeyName} key bound and sounds on {sounds} player entit{(sounds == 1 ? "y" : "ies")}. " +
                      "Run Collect Demo Art after, so the draw and sheathe clips are extracted into the demo.");
        }

        /// <summary>
        /// Writes every weapon's and the shield's sheath pose from <see cref="TryCarry"/>, over any
        /// pose already on the item - the one way to apply a change to the carries here, since every
        /// other build keeps what it finds (the user tunes these by hand; see
        /// `DemoItemBuilder.WriteSheathModel`). Asks first, from the menu.
        /// </summary>
        [MenuItem("Open MMORPG/Demo/Reset Weapon Sheath Poses (overwrites hand edits)")]
        public static void ResetSheathPosesMenu()
        {
            if (!EditorUtility.DisplayDialog("Reset Weapon Sheath Poses",
                    "Every weapon's and the shield's position on the back will be rewritten from DemoSheathBuilder, " +
                    "including any you have set by hand on the items.", "Reset", "Cancel"))
                return;
            ResetSheathPoses();
        }

        internal static int ResetSheathPoses()
        {
            int items = DemoItemBuilder.RefreshSheathModels(overwritePoses: true);
            AssetDatabase.SaveAssets();
            Debug.Log($"[{nameof(DemoSheathBuilder)}] Rewrote the sheath pose on {items} item(s) from the builder's carries.");
            return items;
        }

        /// <summary>
        /// The socket, its container and the holster states on every equipment-driven model: the
        /// ones a player wears gear on. A baked-outfit NPC or monster never changes weapons, so
        /// it never puts one away.
        /// </summary>
        private static int PatchModels()
        {
            int patched = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { ModelDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var existing = prefab != null ? prefab.GetComponent<PlayableCharacterModel>() : null;
                if (existing == null || !IsEquipmentDriven(existing))
                    continue;
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var model = root.GetComponent<PlayableCharacterModel>();
                    Transform hand = null, leftHand = null;
                    foreach (EquipmentContainer container in model.EquipmentContainers)
                    {
                        if (container.equipSocket == DemoItemBuilder.SocketRightHand)
                            hand = container.transform;
                        if (container.equipSocket == DemoItemBuilder.SocketLeftHand)
                            leftHand = container.transform;
                    }
                    EquipmentContainer back = BackContainer(root, hand);
                    EquipmentContainer backLeft = BackLeftContainer(root, leftHand);
                    if (back == null || backLeft == null)
                        continue;
                    var containers = new List<EquipmentContainer>();
                    foreach (EquipmentContainer container in model.EquipmentContainers)
                    {
                        if (container.equipSocket != SocketBack && container.equipSocket != SocketBackLeft)
                            containers.Add(container);
                    }
                    containers.Add(back);
                    containers.Add(backLeft);
                    model.EquipmentContainers = containers.ToArray();
                    WriteHolsters(model.defaultAnimations);
                    DemoAnimationSet.ApplyMasks(model.defaultAnimations);
                    // The bow draws with its own clips; every other weapon uses the default pair.
                    if (model.weaponAnimations != null)
                    {
                        foreach (WeaponAnimations set in model.weaponAnimations)
                        {
                            if (set.weaponType == null || set.weaponType.name != "Bow")
                                continue;
                            WriteBowHolster(set);
                            DemoAnimationSet.ApplyMasks(set);
                        }
                    }
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    ++patched;
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
            return patched;
        }

        /// <summary>A model whose Body slot has a bare part to swap out - what `Build` calls equipment driven.</summary>
        private static bool IsEquipmentDriven(PlayableCharacterModel model)
        {
            foreach (EquipmentContainer container in model.EquipmentContainers)
            {
                if (container.equipSocket == "Body" && container.defaultModel != null)
                    return true;
            }
            return false;
        }
    }
}
