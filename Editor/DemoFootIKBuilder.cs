using System.Collections.Generic;
using MultiplayerARPG.GameData.Model.Playables;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// Puts <see cref="HumanoidFootIK"/> on every humanoid body, with what it needs measured off
    /// the body's own idle rather than typed in, sampled through a <see cref="PlayableGraph"/>
    /// the way the game plays it:
    /// <list type="bullet">
    /// <item>each foot's sole contact points - heel, ball and toe tip, found on the skinned mesh -
    /// and the height they rest at, which is what keeps a sole out of the floor and locks a
    /// nearly planted one onto it;</item>
    /// <item>how high the ankle stands while the foot is flat (8.4cm male, 6.8cm female), the
    /// fallback when there are no contact points.</item>
    /// </list>
    ///
    /// Patches the model prefabs in place, keeping any value tuned by hand on the component
    /// except the measured one, so it can be run on its own at any time. It is also called
    /// by <see cref="DemoCharacterBuilder"/> for each body it builds, so rebuilding the models
    /// does not lose it.
    ///
    /// The animals - deer, collie, wolf (and so the wolf pup) and the horse - get
    /// <see cref="QuadrupedFootIK"/> instead, wired by the bone names of the rig they all
    /// share (<see cref="EnsureQuadruped"/>). <see cref="DemoWildlifeBuilder"/> and
    /// <see cref="DemoMountBuilder"/> call that on a rebuild. It re-measures the legs every
    /// time and sets the step and lift limits only when it first adds the component.
    /// </summary>
    public static class DemoFootIKBuilder
    {
        private const string ModelDir = "Assets/OpenMMORPG/Demo/Prefabs/GamePlay/CharacterModels";

        [MenuItem("Open MMORPG/Demo/Build Foot IK")]
        public static void BuildAll()
        {
            // The forward jogs float above the floor on their own, which IK keeps rather
            // than fixes - lower them first. See DemoAnimationSet.Grounded.
            DemoAnimationSet.EnsureGrounded();
            int patched = 0, animals = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { ModelDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject contents = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    if (Ensure(contents))
                        ++patched;
                    else if (EnsureQuadruped(contents, IdleOf(contents)))
                        ++animals;
                    else
                        continue;
                    PrefabUtility.SaveAsPrefabAsset(contents, path);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
            }
            // The horse is not under the character models: it is a vehicle, and its model is
            // a child of the vehicle prefab.
            GameObject horse = PrefabUtility.LoadPrefabContents(HorsePath);
            try
            {
                Animator animator = horse.GetComponentInChildren<Animator>(true);
                if (animator != null && EnsureQuadruped(animator.gameObject, IdleOf(animator.gameObject)))
                {
                    PrefabUtility.SaveAsPrefabAsset(horse, HorsePath);
                    ++animals;
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(horse);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[{nameof(DemoFootIKBuilder)}] Foot IK on {patched} bodies and {animals} animals.");
        }

        private const string HorsePath = "Assets/OpenMMORPG/Demo/Prefabs/GamePlay/Vehicles/DemoHorse.prefab";

        /// <summary>
        /// The four legs of the Quaternius *Ultimate Animated Animals* rig, which all the
        /// demo's animals carry: the two bones the solve bends, the end of the chain, the IK
        /// control the paw or hoof hangs from, and the paw bone itself. See
        /// <see cref="QuadrupedFootIK"/> for why the paw is not on the end of the leg.
        /// </summary>
        private static readonly (string upper, string lower, string end, string foot, string paw, bool front)[] AnimalLegs =
        {
            ("FrontUpperLeg.L", "FrontLowerLeg.L", "FrontLowerLeg.L_end", "IKFrontLeg.L", "FF.L", true),
            ("FrontUpperLeg.R", "FrontLowerLeg.R", "FrontLowerLeg.R_end", "IKFrontLeg.R", "FF.R", true),
            ("BackUpperLeg.L", "BackLowerLeg.L", "BackLowerLeg.L_end", "IKBackLeg.L", "FFB.L", false),
            ("BackUpperLeg.R", "BackLowerLeg.R", "BackLowerLeg.R_end", "IKBackLeg.R", "FFB.R", false),
        };

        /// <summary>The idle an animal stands in: its character model's, or the horse's controller's.</summary>
        public static AnimationClip IdleOf(GameObject root)
        {
            var model = root.GetComponent<PlayableCharacterModel>();
            if (model != null && model.defaultAnimations.idleState.clip != null)
                return model.defaultAnimations.idleState.clip;
            var animator = root.GetComponent<Animator>();
            if (animator == null || animator.runtimeAnimatorController == null)
                return null;
            AnimationClip found = null;
            foreach (AnimationClip clip in animator.runtimeAnimatorController.animationClips)
            {
                if (clip.name == "Idle")
                    return clip;
                if (found == null && clip.name.StartsWith("Idle"))
                    found = clip;
            }
            return found;
        }

        /// <summary>
        /// Adds and measures <see cref="QuadrupedFootIK"/> on an animal. False, and nothing
        /// added, when the body does not carry the animal rig or has no idle.
        ///
        /// Everything size-dependent is set as a share of the animal's leg (the height of its
        /// upper leg joints in the idle), because a step a horse takes in its stride is a rock
        /// a collie has to climb: 0.95 m of leg on the horse, 0.28 m on the collie.
        /// </summary>
        public static bool EnsureQuadruped(GameObject root, AnimationClip idle)
        {
            Animator animator = root.GetComponent<Animator>();
            if (animator == null || animator.isHuman || idle == null)
                return false;
            Transform body = FindBone(root.transform, "Body");
            if (body == null)
                return false;
            var legs = new QuadrupedFootIK.Leg[AnimalLegs.Length];
            for (int i = 0; i < AnimalLegs.Length; ++i)
            {
                var names = AnimalLegs[i];
                Transform paw = FindBone(root.transform, names.paw);
                legs[i] = new QuadrupedFootIK.Leg
                {
                    upper = FindBone(root.transform, names.upper),
                    lower = FindBone(root.transform, names.lower),
                    end = FindBone(root.transform, names.end),
                    foot = FindBone(root.transform, names.foot),
                    contacts = paw != null ? new[] { paw, FindBone(paw, names.paw + "_end") ?? paw } : new Transform[0],
                    front = names.front,
                };
                if (legs[i].upper == null || legs[i].lower == null || legs[i].end == null || legs[i].foot == null)
                    return false;
            }

            // Generic clips move bones as well as turning them, so both are put back afterwards.
            Quaternion[] rotations = CaptureLocalRotations(root.transform);
            Vector3[] positions = CaptureLocalPositions(root.transform);
            var rests = new float[legs.Length];
            for (int i = 0; i < rests.Length; ++i)
                rests[i] = float.MaxValue;
            float legLength = 0f;
            PlayableGraph graph = PlayableGraph.Create($"{root.name}.PawMeasure");
            try
            {
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var clip = AnimationClipPlayable.Create(graph, idle);
                var output = AnimationPlayableOutput.Create(graph, "Output", animator);
                output.SetSourcePlayable(clip);
                const int steps = 20;
                for (int s = 0; s < steps; ++s)
                {
                    clip.SetTime(idle.length * s / steps);
                    graph.Evaluate(0f);
                    for (int i = 0; i < legs.Length; ++i)
                    {
                        var leg = legs[i];
                        foreach (Transform contact in leg.contacts)
                            rests[i] = Mathf.Min(rests[i], root.transform.InverseTransformPoint(contact.position).y);
                        if (s != 0)
                            continue;
                        // Which way the joint bulges in the idle, for when a leg is dead straight.
                        Vector3 middle = (leg.upper.position + leg.end.position) * 0.5f;
                        leg.bendsForward = Vector3.Dot(leg.lower.position - middle, root.transform.forward) >= 0f;
                        legLength += root.transform.InverseTransformPoint(leg.upper.position).y / legs.Length;
                    }
                }
            }
            finally
            {
                graph.Destroy();
                RestoreLocalRotations(root.transform, rotations);
                RestoreLocalPositions(root.transform, positions);
            }
            if (legLength <= 0f)
                return false;
            for (int i = 0; i < legs.Length; ++i)
            {
                if (rests[i] == float.MaxValue)
                    return false;
                legs[i].restHeight = (float)System.Math.Round(rests[i], 3);
            }

            var ik = root.GetComponent<QuadrupedFootIK>();
            bool added = ik == null;
            if (added)
                ik = root.AddComponent<QuadrupedFootIK>();
            // The rig and what was measured off it, every time: the bones and each paw's rest.
            ik.body = body;
            ik.legs = legs;
            // The limits are only starting points, scaled from the leg, so they are written when
            // the component is first put on and then left to whoever tunes them. Until
            // 2026-09-29 they were rewritten on every run - and Build Wildlife, Build Mounts and
            // Build Foot IK all run this - which contradicted the promise above that hand tuning
            // is kept.
            if (added)
            {
                ik.maxStepUp = (float)System.Math.Round(0.4f * legLength, 3);
                ik.maxStepDown = (float)System.Math.Round(0.45f * legLength, 3);
                ik.maxBodyRaise = (float)System.Math.Round(0.1f * legLength, 3);
                ik.lockHeight = (float)System.Math.Round(0.04f * legLength, 3);
                ik.liftFade = (float)System.Math.Round(0.12f * legLength, 3);
            }
            string bends = "", restText = "";
            foreach (var leg in legs)
            {
                bends += leg.bendsForward ? "F" : "B";
                restText += $" {leg.restHeight:F3}";
            }
            Debug.Log($"[{nameof(DemoFootIKBuilder)}] \"{root.name}\": four legs, {legLength:F2} m of leg, " +
                      $"paws rest at{restText} m, joints bend {bends} (FL FR BL BR).");
            return true;
        }

        private static Transform FindBone(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name)
                    return t;
            }
            return null;
        }

        private static Vector3[] CaptureLocalPositions(Transform root)
        {
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            var positions = new Vector3[all.Length];
            for (int i = 0; i < all.Length; ++i)
                positions[i] = all[i].localPosition;
            return positions;
        }

        private static void RestoreLocalPositions(Transform root, Vector3[] positions)
        {
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length && i < positions.Length; ++i)
                all[i].localPosition = positions[i];
        }

        /// <summary>
        /// Adds and measures the component on a body. False, and nothing added, when the body
        /// is not a humanoid with an idle to measure.
        /// </summary>
        public static bool Ensure(GameObject root)
        {
            Animator animator = root.GetComponent<Animator>();
            var model = root.GetComponent<PlayableCharacterModel>();
            if (animator == null || !animator.isHuman || model == null)
                return false;
            AnimationClip idle = model.defaultAnimations.idleState.clip;
            if (idle == null)
            {
                Debug.LogWarning($"[{nameof(DemoFootIKBuilder)}] \"{root.name}\" has no idle to measure its feet from; skipped.");
                return false;
            }
            var ik = root.GetComponent<HumanoidFootIK>();
            if (ik == null)
                ik = root.AddComponent<HumanoidFootIK>();
            // The sole first: it bakes the skinned mesh, and only the first pose sampled in an
            // editor call reaches the skinning (see MeasureContacts).
            ik.hasContacts = MeasureContacts(root, animator, idle, ik);
            ik.plantedAnkleHeight = MeasurePlantedAnkle(root, animator, idle);
            Debug.Log($"[{nameof(DemoFootIKBuilder)}] \"{root.name}\": planted ankle {ik.plantedAnkleHeight:F3} m, " +
                      (ik.hasContacts
                          ? $"sole rests at {ik.soleRestHeight:F3} m."
                          : "no sole contact points found."));
            return true;
        }

        /// <summary>
        /// Finds each foot's heel, ball and toe tip on the skinned mesh as it stands in the
        /// idle's first frame (see <see cref="FindContacts"/>). The heel and ball are stored in
        /// the foot bone's space and the toe tip in the toe bone's, so each follows its own
        /// bone's bend.
        ///
        /// **Must be the first pose this editor call samples on this object.** Bones follow
        /// every evaluation, but the skinned meshes keep the first pose sampled in an editor
        /// tick, and <see cref="SkinnedMeshRenderer.BakeMesh(Mesh)"/> bakes that one - a
        /// measurement taken after another sample is of the wrong pose, with no warning.
        /// </summary>
        private static bool MeasureContacts(GameObject root, Animator animator, AnimationClip idle, HumanoidFootIK ik)
        {
            Transform leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            Transform rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            Transform leftToes = animator.GetBoneTransform(HumanBodyBones.LeftToes);
            Transform rightToes = animator.GetBoneTransform(HumanBodyBones.RightToes);
            if (leftToes == null)
                leftToes = leftFoot;
            if (rightToes == null)
                rightToes = rightFoot;
            Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            Vector3 hipsPosition = hips.localPosition;
            Quaternion[] rotations = CaptureLocalRotations(root.transform);

            PlayableGraph graph = PlayableGraph.Create($"{root.name}.SoleMeasure");
            var vertices = new List<Vector3>();
            try
            {
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var clip = AnimationClipPlayable.Create(graph, idle);
                var output = AnimationPlayableOutput.Create(graph, "Output", animator);
                output.SetSourcePlayable(clip);
                clip.SetTime(0f);
                graph.Evaluate(0f);

                var baked = new Mesh();
                foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(false))
                {
                    if (!renderer.enabled || renderer.sharedMesh == null)
                        continue;
                    renderer.BakeMesh(baked);
                    Matrix4x4 toWorld = Matrix4x4.TRS(renderer.transform.position, renderer.transform.rotation, Vector3.one);
                    foreach (Vector3 vertex in baked.vertices)
                        vertices.Add(toWorld.MultiplyPoint3x4(vertex));
                }
                Object.DestroyImmediate(baked);

                if (!FindContacts(vertices, leftFoot, leftToes, rightFoot, out Vector3 leftHeel, out Vector3 leftBall, out Vector3 leftTip) ||
                    !FindContacts(vertices, rightFoot, rightToes, leftFoot, out Vector3 rightHeel, out Vector3 rightBall, out Vector3 rightTip))
                    return false;
                ik.leftHeel = Round(leftFoot.InverseTransformPoint(leftHeel));
                ik.leftBall = Round(leftFoot.InverseTransformPoint(leftBall));
                ik.leftToe = Round(leftToes.InverseTransformPoint(leftTip));
                ik.rightHeel = Round(rightFoot.InverseTransformPoint(rightHeel));
                ik.rightBall = Round(rightFoot.InverseTransformPoint(rightBall));
                ik.rightToe = Round(rightToes.InverseTransformPoint(rightTip));
                float rest = Mathf.Min(Mathf.Min(Mathf.Min(leftHeel.y, leftBall.y), leftTip.y),
                                       Mathf.Min(Mathf.Min(rightHeel.y, rightBall.y), rightTip.y));
                ik.soleRestHeight = (float)System.Math.Round(root.transform.InverseTransformPoint(new Vector3(0f, rest, 0f)).y, 3);
                return true;
            }
            finally
            {
                graph.Destroy();
                hips.localPosition = hipsPosition;
                RestoreLocalRotations(root.transform, rotations);
            }
        }

        /// <summary>
        /// The heel, ball and toe tip under one foot: of the vertices nearer this foot than the
        /// other and low enough to be sole, the rearmost and foremost along the foot, and the
        /// one closest under the toe joint.
        /// </summary>
        private static bool FindContacts(List<Vector3> vertices, Transform foot, Transform toes, Transform otherFoot,
            out Vector3 heel, out Vector3 ball, out Vector3 tip)
        {
            heel = ball = tip = Vector3.zero;
            Vector3 along = toes.position - foot.position;
            along.y = 0f;
            if (along.sqrMagnitude < 1e-6f)
                along = foot.forward;
            along.y = 0f;
            along.Normalize();

            const float reach = 0.3f;
            float lowest = float.MaxValue;
            foreach (Vector3 v in vertices)
            {
                if (IsUnder(v, foot, otherFoot, reach))
                    lowest = Mathf.Min(lowest, v.y);
            }
            if (lowest == float.MaxValue)
                return false;

            float rear = float.MaxValue, front = float.MinValue, nearest = float.MaxValue;
            foreach (Vector3 v in vertices)
            {
                if (v.y > lowest + 0.02f || !IsUnder(v, foot, otherFoot, reach))
                    continue;
                Vector3 flat = v - foot.position;
                flat.y = 0f;
                Vector3 underJoint = v - toes.position;
                underJoint.y = 0f;
                if (underJoint.sqrMagnitude < nearest)
                {
                    nearest = underJoint.sqrMagnitude;
                    ball = v;
                }
                float a = Vector3.Dot(flat, along);
                if (a < rear)
                {
                    rear = a;
                    heel = v;
                }
                if (a > front)
                {
                    front = a;
                    tip = v;
                }
            }
            return rear < front;
        }

        private static bool IsUnder(Vector3 v, Transform foot, Transform otherFoot, float reach)
        {
            Vector3 mine = v - foot.position;
            Vector3 theirs = v - otherFoot.position;
            mine.y = theirs.y = 0f;
            return mine.magnitude < reach && mine.sqrMagnitude < theirs.sqrMagnitude;
        }

        private static Vector3 Round(Vector3 v)
        {
            return new Vector3((float)System.Math.Round(v.x, 4), (float)System.Math.Round(v.y, 4), (float)System.Math.Round(v.z, 4));
        }

        /// <summary>
        /// The lowest either ankle stands above the model's origin through the idle. Measured
        /// against the root in the root's own frame and at scale one, which is how the
        /// component reads it back.
        /// </summary>
        private static float MeasurePlantedAnkle(GameObject root, Animator animator, AnimationClip idle)
        {
            Transform left = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            Transform right = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            Vector3 hipsPosition = hips.localPosition;
            Quaternion[] rotations = CaptureLocalRotations(root.transform);

            PlayableGraph graph = PlayableGraph.Create($"{root.name}.FootMeasure");
            float lowest = float.MaxValue;
            try
            {
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var clip = AnimationClipPlayable.Create(graph, idle);
                var output = AnimationPlayableOutput.Create(graph, "Output", animator);
                output.SetSourcePlayable(clip);
                const int steps = 30;
                for (int i = 0; i < steps; ++i)
                {
                    clip.SetTime(idle.length * i / steps);
                    graph.Evaluate(0f);
                    float l = root.transform.InverseTransformPoint(left.position).y;
                    float r = root.transform.InverseTransformPoint(right.position).y;
                    lowest = Mathf.Min(lowest, Mathf.Min(l, r));
                }
            }
            finally
            {
                graph.Destroy();
                // Leave the prefab in its bind pose, not the last sampled frame.
                hips.localPosition = hipsPosition;
                RestoreLocalRotations(root.transform, rotations);
            }
            return (float)System.Math.Round(lowest, 3);
        }

        private static Quaternion[] CaptureLocalRotations(Transform root)
        {
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            var rotations = new Quaternion[all.Length];
            for (int i = 0; i < all.Length; ++i)
                rotations[i] = all[i].localRotation;
            return rotations;
        }

        private static void RestoreLocalRotations(Transform root, Quaternion[] rotations)
        {
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length && i < rotations.Length; ++i)
                all[i].localRotation = rotations[i];
        }
    }
}
