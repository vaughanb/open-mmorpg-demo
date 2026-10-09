using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// Puts the Ludo AI saddle on the demo's horse.
    ///
    /// The model is **modelled in place**: `Art/Saddle/Saddle.fbx` is the saddle already fitted to this horse's
    /// back and to the seated rider (scale, pitch, drop on the spine, fenders and stirrups swung and draped over
    /// the barrel), with its origin on the point where the rider's pelvis rests. So the builder's job is to make
    /// the material, skin the model to the horse, and move the vehicle's Seat anchor up onto the saddle. How the
    /// fit was made is in `Art/Saddle/Source~/README.md`.
    ///
    /// **The saddle is skinned to two bones, not parented to one.** Hung off Torso2, as the rider is, it works
    /// standing and in the gallop (the spine bones move as one there), but the walk clip bends the spine at the
    /// Torso/Torso2 joint, which is under the middle of the saddle: the hide behind the joint then moves up to 4 cm
    /// against a rigid saddle and shows through its skirts (1 159 hide vertices past the leather by more than 3 mm
    /// over 24 walk frames, worst 4.2 cm). So the seat and everything ahead of it ride Torso2, everything behind
    /// <see cref="RampFrom"/> rides Torso, and the leather blends between them over a hand's width
    /// (<see cref="RampFrom"/> to <see cref="RampTo"/>, along the horse). Stiff like a real saddle, with no more than
    /// 2 cm of give; it brings the walk to 162 vertices past the leather, worst 1.5 cm, and the gallop to 18, worst
    /// 1.1 cm. The dish is at full Torso2 weight, so it stays exactly with the rider's pelvis, which is glued to Torso2.
    ///
    /// Measured against the horse's own clips in the horse's bind frame (Horse.fbx, Walk and Gallop, 24 frames): the
    /// hide vertices within 6 cm of the leather, each paired with its nearest leather vertex, their outward motion
    /// against it minus the clearance they started with. Weights copied from the hide's own skinning (every bone,
    /// Gaussian average) measured better still, 0.3 cm, but stretched edges by up to 11 cm and smeared the conchos.
    ///
    /// Known limit: the hide under the rear skirts follows the hip bones, which rock 5-14 cm against any spine bone in
    /// the walk and gallop, so the rear skirts float above the croup (or sink a little into it) at some phases. A
    /// saddle blanket under the skirts would be the physical answer.
    ///
    /// It has no collider and no network identity.
    /// </summary>
    public static class DemoSaddleBuilder
    {
        private const string ArtDir = "Assets/OpenMMORPG/Demo/Art/Saddle";
        public const string ModelPath = ArtDir + "/Saddle.fbx";
        private const string MaterialPath = ArtDir + "/Materials/Saddle.mat";
        private const string SkinnedMeshPath = ArtDir + "/SaddleSkinned.asset";
        private const string SaddleObjectName = "Saddle";

        /// <summary>
        /// Where the rider's pelvis rests on the saddle, in the horse's entity space (X across, Y up, Z the way it
        /// faces): the lowest point of the seat's dish. It is the FBX's origin. It sits 9.5 cm above the horse's
        /// back, which is what the old seat (<c>MeasureSaddle</c>, 1.46 m) had no room for. Printed by
        /// `Source~/fit_saddle.py` as <c>seat_pt</c>.
        /// </summary>
        public static readonly Vector3 SeatPoint = new Vector3(0f, 1.5881f, -0.2440f);

        /// <summary>The bone behind the joint: in the walk, the one the hide at the saddle's rear follows.</summary>
        private const string RearBoneName = "Torso";

        /// <summary>
        /// Where along the horse (entity Z) the leather passes from riding Torso (behind) to Torso2 (ahead). The dish is
        /// at -0.244, so it is entirely Torso2. Tried 0.12 to 0.45 m wide: the poke counts differ by under 20%, and the
        /// stiffer the better the stretch, so this is the narrow end that still leaves the dish alone.
        /// </summary>
        public const float RampFrom = -0.42f;
        public const float RampTo = -0.26f;

        public static bool Available => AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath) != null;

        [MenuItem("Open MMORPG/Demo/Build Saddle")]
        public static void BuildAll()
        {
            if (!Available)
            {
                Debug.LogError($"[{nameof(DemoSaddleBuilder)}] No saddle at {ModelPath}.");
                return;
            }
            EnsureMaterial();
            PatchHorse();
        }

        /// <summary>
        /// URP Lit from the three delivered maps, set up exactly like the weapons' materials: metallic in the
        /// mask's R, smoothness in its A. The FBX importer binds this by the FBX's material name, so it has to
        /// exist before the model is first imported (otherwise Unity extracts a default one and keeps it).
        /// </summary>
        public static Material EnsureMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Saddle" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{ArtDir}/Saddle_BaseColor.png"));
            material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{ArtDir}/Saddle_Normal.png"));
            material.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{ArtDir}/Saddle_Mask.png"));
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Metallic", 1f);
            material.SetFloat("_Smoothness", 1f);
            material.SetFloat("_SmoothnessTextureChannel", 0f);
            material.SetFloat("_BumpScale", 1f);
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
            material.EnableKeyword("_NORMALMAP");
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
            return material;
        }

        /// <summary>
        /// Saddles a horse model instance (the object carrying the Animator and the horse's skinned mesh): skins the
        /// saddle to the bones, writes <c>SaddleSkinned.asset</c> and returns true. Safe to run again; it rebuilds
        /// the same thing.
        ///
        /// Everything is worked out from the horse's **bind pose** (its mesh and bindposes, not wherever the bones
        /// happen to be): `DemoFootIKBuilder.EnsureQuadruped` samples the idle on the instance, and a pose that is
        /// not the rest pose would skin the saddle to the wrong place.
        /// </summary>
        public static bool EnsureSaddle(GameObject model)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            SkinnedMeshRenderer horse = null;
            foreach (SkinnedMeshRenderer candidate in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (candidate.name != SaddleObjectName)
                {
                    horse = candidate;
                    break;
                }
            }
            if (source == null || material == null || horse == null || horse.sharedMesh == null)
            {
                Debug.LogWarning($"[{nameof(DemoSaddleBuilder)}] No saddle model, material or horse mesh on {model.name}; leaving it unsaddled.");
                return false;
            }
            Transform seatBone = null, rearBone = null;
            foreach (Transform bone in horse.bones)
            {
                if (bone == null)
                    continue;
                if (bone.name == DemoMountBuilder.SeatBoneName)
                    seatBone = bone;
                else if (bone.name == RearBoneName)
                    rearBone = bone;
            }
            if (seatBone == null || rearBone == null)
            {
                Debug.LogWarning($"[{nameof(DemoSaddleBuilder)}] {model.name} has no \"{DemoMountBuilder.SeatBoneName}\" and \"{RearBoneName}\" bones; no saddle.");
                return false;
            }

            Transform root = model.transform.root;
            // Where the saddle stands at rest, in world space: the FBX origin on the seat point.
            Matrix4x4 saddleToWorld = root.localToWorldMatrix * Matrix4x4.Translate(SeatPoint);

            // An earlier version parented the model to a bone; take it away.
            foreach (Transform stale in model.GetComponentsInChildren<Transform>(true))
            {
                if (stale.name == SaddleObjectName && stale.GetComponent<SkinnedMeshRenderer>() == null && stale != model.transform)
                {
                    Object.DestroyImmediate(stale.gameObject);
                    break;
                }
            }

            Mesh mesh = BuildSkinnedMesh(source.GetComponentInChildren<MeshFilter>().sharedMesh, horse, saddleToWorld, seatBone, rearBone);

            Transform saddle = model.transform.Find(SaddleObjectName);
            if (saddle == null)
            {
                var created = new GameObject(SaddleObjectName);
                created.transform.SetParent(model.transform, false);
                saddle = created.transform;
            }
            saddle.position = saddleToWorld.GetColumn(3);
            saddle.rotation = saddleToWorld.rotation;
            saddle.localScale = Vector3.one;

            var smr = saddle.GetComponent<SkinnedMeshRenderer>();
            if (smr == null)
                smr = saddle.gameObject.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.sharedMaterial = material;
            smr.bones = new[] { seatBone, rearBone };
            smr.rootBone = seatBone;
            smr.updateWhenOffscreen = false;
            smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            smr.receiveShadows = true;
            smr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.BlendProbes;
            smr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.BlendProbes;
            // Bounds are kept in the root bone's space; the mesh's own, carried there and loosened for the flex.
            Matrix4x4 toRoot = seatBone.worldToLocalMatrix * saddleToWorld;
            Bounds local = default;
            bool first = true;
            Vector3 c = mesh.bounds.center, e = mesh.bounds.extents;
            for (int i = 0; i < 8; ++i)
            {
                Vector3 corner = toRoot.MultiplyPoint3x4(c + Vector3.Scale(e, new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f)));
                if (first) { local = new Bounds(corner, Vector3.zero); first = false; }
                else local.Encapsulate(corner);
            }
            local.Expand(0.25f);
            smr.localBounds = local;
            EditorUtility.SetDirty(smr);
            return true;
        }

        /// <summary>
        /// The saddle's mesh with two bone weights per vertex: Torso2 ahead of <see cref="RampTo"/>, Torso behind
        /// <see cref="RampFrom"/>, a smoothstep between. Written into one asset, <c>SaddleSkinned.asset</c>.
        /// </summary>
        private static Mesh BuildSkinnedMesh(Mesh source, SkinnedMeshRenderer horse, Matrix4x4 saddleToWorld, Transform front, Transform rear)
        {
            Vector3[] vertices = source.vertices;
            var boneWeights = new BoneWeight[vertices.Length];
            for (int k = 0; k < vertices.Length; ++k)
            {
                float z = saddleToWorld.MultiplyPoint3x4(vertices[k]).z;
                float t = Mathf.Clamp01((z - RampFrom) / (RampTo - RampFrom));
                float ahead = t * t * (3f - 2f * t);
                // bone 0 = Torso2, bone 1 = Torso; the stronger influence first
                boneWeights[k] = new BoneWeight
                {
                    boneIndex0 = ahead >= 0.5f ? 0 : 1, weight0 = Mathf.Max(ahead, 1f - ahead),
                    boneIndex1 = ahead >= 0.5f ? 1 : 0, weight1 = Mathf.Min(ahead, 1f - ahead),
                };
            }

            // mesh space of the saddle -> each bone's space, at rest
            Matrix4x4 worldToHide = horse.worldToLocalMatrix;
            var bind = new[]
            {
                horse.sharedMesh.bindposes[System.Array.IndexOf(horse.bones, front)] * worldToHide * saddleToWorld,
                horse.sharedMesh.bindposes[System.Array.IndexOf(horse.bones, rear)] * worldToHide * saddleToWorld,
            };

            // Written into the same asset every time, so the horse prefab's reference to it survives. (Not
            // EditorUtility.CopySerialized: onto a generated mesh it corrupts the vertex stride.)
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(SkinnedMeshPath);
            bool created = mesh == null;
            if (created)
                mesh = new Mesh { name = "SaddleSkinned" };
            else
                mesh.Clear();
            mesh.indexFormat = source.indexFormat;
            mesh.vertices = vertices;
            mesh.normals = source.normals;
            mesh.tangents = source.tangents;
            mesh.uv = source.uv;
            mesh.subMeshCount = 1;
            mesh.SetTriangles(source.GetTriangles(0), 0);
            mesh.boneWeights = boneWeights;
            mesh.bindposes = bind;
            mesh.RecalculateBounds();
            if (created)
                AssetDatabase.CreateAsset(mesh, SkinnedMeshPath);
            else
                EditorUtility.SetDirty(mesh);
            AssetDatabase.SaveAssetIfDirty(mesh);
            return mesh;
        }

        /// <summary>
        /// Saddles the horse prefab on disk without rebuilding it (a full Build Mounts also re-measures the
        /// riding pose and rewrites every other part of the horse), and moves its Seat anchor onto the saddle.
        /// The seat is set absolutely, <see cref="SeatPoint"/> minus where the rider's pelvis sits, so running
        /// this twice changes nothing.
        /// </summary>
        public static void PatchHorse()
        {
            string path = DemoMountBuilder.HorsePrefabPath;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
            {
                Debug.LogError($"[{nameof(DemoSaddleBuilder)}] No horse at {path}. Run Build Mounts first.");
                return;
            }
            Vector3 contact = DemoMountBuilder.MeasureRiderContact();
            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                Transform model = contents.transform.Find("Model");
                if (model == null || !EnsureSaddle(model.gameObject))
                    return;
                Transform seat = contents.transform.Find("Transforms/Seat");
                Vector3 want = SeatPoint - contact;
                if (seat != null && (seat.localPosition - want).sqrMagnitude > 1e-8f)
                {
                    Debug.Log($"[{nameof(DemoSaddleBuilder)}] Seat {seat.localPosition:F3} -> {want:F3}.");
                    seat.localPosition = want;
                }
                PrefabUtility.SaveAsPrefabAsset(contents, path);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                Debug.Log($"[{nameof(DemoSaddleBuilder)}] Saddled {path}.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        /// <summary>
        /// Writes what `Art/Saddle/Source~/fit_saddle.py` fits against: the horse's skinned mesh in its bind pose and
        /// the male rider in the riding pose (legs spread as in game), both in the horse's entity space / the
        /// rider's root frame, as little-endian floats, plus the rider's hips. Re-run it, then the script, after
        /// changing the horse, the riding clip or the leg spread.
        /// </summary>
        [MenuItem("Open MMORPG/Demo/Dump Saddle Fit Inputs")]
        public static void DumpFitInputs()
        {
            string dir = Path.Combine(Application.dataPath, "OpenMMORPG/Demo/Art/Saddle/Source~");
            Directory.CreateDirectory(dir);

            GameObject contents = PrefabUtility.LoadPrefabContents(DemoMountBuilder.HorsePrefabPath);
            try
            {
                SkinnedMeshRenderer skin = null;
                foreach (SkinnedMeshRenderer candidate in contents.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (candidate.name != SaddleObjectName)
                    {
                        skin = candidate;
                        break;
                    }
                }
                Mesh mesh = skin.sharedMesh;
                Matrix4x4 toEntity = contents.transform.worldToLocalMatrix * skin.localToWorldMatrix;
                WriteVectors(Path.Combine(dir, "dump_horse_verts.bin"), mesh.vertices, toEntity);
                WriteInts(Path.Combine(dir, "dump_horse_tris.bin"), mesh.triangles);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DemoMountBuilder.RiderModelPath);
            AnimationClip sit = DemoAnimationSet.Clip(DemoMountBuilder.SitClip);
            var rider = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            var graph = UnityEngine.Playables.PlayableGraph.Create("DemoSaddleBuilder.DumpRider");
            try
            {
                rider.transform.position = Vector3.zero;
                rider.transform.rotation = Quaternion.identity;
                Animator animator = rider.GetComponentInChildren<Animator>();
                animator.applyRootMotion = false;
                var output = UnityEngine.Animations.AnimationPlayableOutput.Create(graph, "out", animator);
                UnityEngine.Playables.PlayableOutputExtensions.SetSourcePlayable(output,
                    UnityEngine.Animations.AnimationClipPlayable.Create(graph, sit));
                graph.Evaluate(0.3f);
                var legs = animator.gameObject.AddComponent<RiderLegSpread>();
                legs.Configure(DemoMountBuilder.RiderAbductionDegrees, DemoMountBuilder.RiderDropDegrees);
                legs.Apply();

                var vertices = new List<Vector3>();
                var triangles = new List<int>();
                foreach (SkinnedMeshRenderer smr in rider.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    if (!smr.enabled || !smr.gameObject.activeInHierarchy)
                        continue;
                    var baked = new Mesh();
                    smr.BakeMesh(baked, true);
                    int firstVertex = vertices.Count;
                    foreach (Vector3 v in baked.vertices)
                        vertices.Add(smr.transform.TransformPoint(v));
                    foreach (int t in baked.triangles)
                        triangles.Add(t + firstVertex);
                    Object.DestroyImmediate(baked);
                }
                WriteVectors(Path.Combine(dir, "dump_rider_verts.bin"), vertices.ToArray(), Matrix4x4.identity);
                WriteInts(Path.Combine(dir, "dump_rider_tris.bin"), triangles.ToArray());
                Vector3 hips = animator.GetBoneTransform(HumanBodyBones.Hips).position;
                File.WriteAllText(Path.Combine(dir, "dump_rider.json"), string.Format(CultureInfo.InvariantCulture,
                    "{{\"hips\": [{0:R}, {1:R}, {2:R}]}}\n", hips.x, hips.y, hips.z));
            }
            finally
            {
                if (graph.IsValid())
                    graph.Destroy();
                Object.DestroyImmediate(rider);
            }
            Debug.Log($"[{nameof(DemoSaddleBuilder)}] Wrote the fit inputs to {dir}.");
        }

        private static void WriteVectors(string path, Vector3[] vertices, Matrix4x4 transform)
        {
            using (var w = new BinaryWriter(File.Create(path)))
            {
                w.Write(vertices.Length);
                foreach (Vector3 v in vertices)
                {
                    Vector3 p = transform.MultiplyPoint3x4(v);
                    w.Write(p.x);
                    w.Write(p.y);
                    w.Write(p.z);
                }
            }
        }

        private static void WriteInts(string path, int[] values)
        {
            using (var w = new BinaryWriter(File.Create(path)))
            {
                w.Write(values.Length);
                foreach (int i in values)
                    w.Write(i);
            }
        }
    }
}
