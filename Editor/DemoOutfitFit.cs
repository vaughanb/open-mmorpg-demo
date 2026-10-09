using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// Keeps the knight's cuirass from sinking into the knight's greaves when the hips move (user,
    /// 2026-10-06: "the character's underwear is poking through their pants when wearing the knight armor
    /// leggings and jogging forward").
    ///
    /// **It was never underwear.** With every bare body part hidden under the set, the ragged grey and light
    /// patches on the seat of the leggings are the cuirass's own hanging belt tab. The tab is cut as part of the
    /// body piece, so it is skinned like the body piece - to `spine_02` and `pelvis` - while the leather shorts it
    /// hangs over belong to the legs piece and are skinned to the thighs. Standing still the tab lies on top of
    /// the shorts. In a jog the thighs swing, the seat of the shorts moves out through a tab that stayed with the
    /// spine, and what is left showing is the patches. It is the same on both bodies, and no idle-pose audit can
    /// see it (`Audit Outfits` samples `Idle_Loop`); it takes a stride.
    ///
    /// **The fix is the weights, not the shape.** Every vertex of the cuirass that hangs over the shorts takes
    /// the skinning of the nearest vertex of the leggings, blended in over a hand's breadth at the waist, so the
    /// tab rides with the shorts below the belt and with the body above it. Positions, normals, UVs and the
    /// bind pose are the original's, so a character standing in the T-pose is identical to the shipped piece;
    /// only what happens under motion changes. Measured on the jog: 640 of the cuirass's 5,417 vertices change,
    /// and the patches are gone from every one of 8 frames, from the back, front and side.
    ///
    /// A garment copy rather than an edit of the library's FBX, which is not ours and is reimported over any
    /// change: a mesh asset with the new weights and a prefab variant of the FBX that wears it, both written
    /// under `Demo/` so they ship. Rewritten each time; there is nothing on either to tune by hand.
    ///
    /// Only the player's cuirass is fitted. `GuardModel`, `KeeperModel` and the two marauders graft the same
    /// FBX (the female marauder the female one) straight from the library in `DemoCharacterBuilder` and have
    /// the same patches in motion; `FitIfNeeded` is the way to give them the fitted piece.
    /// </summary>
    public static class DemoOutfitFit
    {
        private const string GarmentDir = "Assets/OpenMMORPG/Demo/Prefabs/GamePlay/Equipments/Outfits";
        private const string MeshDir = "Assets/OpenMMORPG/Demo/Meshes/Outfits";
        private const string ItemPath = "Assets/OpenMMORPG/Demo/GameData/Resources/Items/KnightCuirass.asset";

        /// <summary>The cuirass models that hang over a pair of leggings, and the leggings each is fitted to.</summary>
        private static readonly Dictionary<string, string> Pairs = new Dictionary<string, string>
        {
            { "Male_Knight_Body_Armor", "Male_Knight_Legs_Armor" },
        };

        private const string FittedSuffix = "_Fitted";

        /// <summary>
        /// Half the span, in metres, over which the cuirass hands over from its own skinning to the
        /// leggings'. Centred on the top of the leggings: at the waistband the belt is the body piece's, and a
        /// hand's breadth below it the tab is on the seat of the shorts.
        /// </summary>
        private const float HandoverHalfSpan = 0.05f;

        /// <summary>
        /// How far from a legging vertex a cuirass vertex can be and still count as lying on it. The tab
        /// measures 1-3cm from the shorts; the front of the cuirass is further than this from them and keeps
        /// its own skinning.
        /// </summary>
        private const float Reach = 0.06f;

        /// <summary>
        /// Only the leggings' top, a hand's breadth either side of the waist, is looked up. Further down they
        /// are greaves and thigh plate, which nothing on the cuirass hangs over.
        /// </summary>
        private const float LeggingsDepth = 0.30f;

        [MenuItem("Open MMORPG/Demo/Fit Knight Cuirass")]
        public static void FitKnightCuirassMenu()
        {
            var item = AssetDatabase.LoadAssetAtPath<ArmorItem>(ItemPath);
            SerializedProperty models = item != null ? new SerializedObject(item).FindProperty("equipmentModels") : null;
            if (models == null || models.arraySize == 0)
            {
                Debug.LogError($"[{nameof(DemoOutfitFit)}] {ItemPath} has no model to fit. Run Build Items first.");
                return;
            }
            var serialized = new SerializedObject(item);
            SerializedProperty meshPrefab = serialized.FindProperty("equipmentModels").GetArrayElementAtIndex(0)
                                                      .FindPropertyRelative("meshPrefab");
            string fitted = FitIfNeeded(SourcePath(meshPrefab.objectReferenceValue as GameObject));
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(fitted);
            if (prefab == null || !fitted.EndsWith(FittedSuffix + ".prefab"))
                return;
            meshPrefab.objectReferenceValue = prefab;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(item);
            AssetDatabase.SaveAssets();
            Debug.Log($"[{nameof(DemoOutfitFit)}] KnightCuirass now wears {fitted}.");
        }

        /// <summary>
        /// The library model a fitted prefab was made from, or the model itself if it is not one: what
        /// <see cref="FitIfNeeded"/> wants, since an item already pointing at the fitted piece has to be
        /// fitted again from the FBX and not from its own output.
        /// </summary>
        private static string SourcePath(GameObject model)
        {
            if (model == null)
                return null;
            GameObject source = model;
            while (AssetDatabase.GetAssetPath(source).EndsWith(".prefab"))
            {
                GameObject up = PrefabUtility.GetCorrespondingObjectFromSource(source);
                if (up == null)
                    break;
                source = up;
            }
            return AssetDatabase.GetAssetPath(source);
        }

        /// <summary>
        /// The path of the fitted prefab for a cuirass model, building it, or the path it was given if that
        /// model has no leggings to be fitted to. Safe to call on any armour model.
        /// </summary>
        internal static string FitIfNeeded(string modelPath)
        {
            if (string.IsNullOrEmpty(modelPath) || !modelPath.EndsWith(".fbx"))
                return modelPath;
            string name = System.IO.Path.GetFileNameWithoutExtension(modelPath);
            string legsName;
            if (!Pairs.TryGetValue(name, out legsName))
                return modelPath;

            string folder = System.IO.Path.GetDirectoryName(modelPath).Replace('\\', '/');
            var body = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            var legs = AssetDatabase.LoadAssetAtPath<GameObject>($"{folder}/{legsName}.fbx");
            if (body == null || legs == null)
            {
                Debug.LogError($"[{nameof(DemoOutfitFit)}] Cannot fit {name}: {folder}/{legsName}.fbx is not there.");
                return modelPath;
            }

            Mesh mesh = FittedMesh(body, legs, $"{MeshDir}/{name}{FittedSuffix}.asset");
            if (mesh == null)
                return modelPath;

            DemoItemBuilder.EnsureFolder(GarmentDir);
            string path = $"{GarmentDir}/{name}{FittedSuffix}.prefab";
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(body);
            try
            {
                instance.GetComponentInChildren<SkinnedMeshRenderer>(true).sharedMesh = mesh;
                instance.name = name + FittedSuffix;
                PrefabUtility.SaveAsPrefabAsset(instance, path);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
            // Without this the prefab the rest of the pipeline loads is the in-memory one, which can have
            // lost its mesh reference while the file on disk is right.
            AssetDatabase.ImportAsset(path);
            return path;
        }

        /// <summary>
        /// The cuirass's mesh with the skinning of everything that hangs over the leggings taken from them.
        /// Written into the existing asset when there is one, never deleted and recreated: a prefab that
        /// points at a mesh asset loses the reference to it when the asset is replaced.
        /// </summary>
        private static Mesh FittedMesh(GameObject body, GameObject legs, string meshPath)
        {
            SkinnedMeshRenderer bodyRenderer = body.GetComponentInChildren<SkinnedMeshRenderer>(true);
            SkinnedMeshRenderer legsRenderer = legs.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (bodyRenderer == null || legsRenderer == null || bodyRenderer.sharedMesh == null || legsRenderer.sharedMesh == null)
            {
                Debug.LogError($"[{nameof(DemoOutfitFit)}] {body.name} or {legs.name} has no skinned mesh.");
                return null;
            }
            Mesh source = bodyRenderer.sharedMesh;
            Mesh legging = legsRenderer.sharedMesh;

            // The two meshes index their bones separately, so weights are carried across by bone name.
            var bodyBone = new Dictionary<string, int>();
            for (int i = 0; i < bodyRenderer.bones.Length; ++i)
                bodyBone[bodyRenderer.bones[i].name] = i;
            var leggingToBody = new int[legsRenderer.bones.Length];
            for (int i = 0; i < leggingToBody.Length; ++i)
            {
                int index;
                leggingToBody[i] = bodyBone.TryGetValue(legsRenderer.bones[i].name, out index) ? index : -1;
            }

            // Z is up: every piece of the pack is authored upright in Blender and stood on its feet by the
            // (270, 180, 0) rotation on its armature, which is the renderer's, not the mesh's.
            Vector3[] bodyVertices = source.vertices;
            Vector3[] leggingVertices = legging.vertices;
            BoneWeight[] bodyWeights = source.boneWeights;
            BoneWeight[] leggingWeights = legging.boneWeights;
            float waist = legging.bounds.max.z;
            float low = waist - HandoverHalfSpan;
            float high = waist + HandoverHalfSpan;

            var near = new List<int>();
            for (int j = 0; j < leggingVertices.Length; ++j)
            {
                if (leggingVertices[j].z > waist - LeggingsDepth)
                    near.Add(j);
            }

            var fitted = (BoneWeight[])bodyWeights.Clone();
            int changed = 0;
            for (int i = 0; i < bodyVertices.Length; ++i)
            {
                if (bodyVertices[i].z >= high)
                    continue;
                float closest = float.MaxValue;
                int partner = -1;
                foreach (int j in near)
                {
                    float distance = (leggingVertices[j] - bodyVertices[i]).sqrMagnitude;
                    if (distance < closest)
                    {
                        closest = distance;
                        partner = j;
                    }
                }
                if (partner < 0 || Mathf.Sqrt(closest) > Reach)
                    continue;

                float t = Mathf.Clamp01((high - bodyVertices[i].z) / (high - low));
                t = t * t * (3f - 2f * t);

                var blend = new Dictionary<int, float>();
                Add(blend, bodyWeights[i], null, 1f - t);
                Add(blend, leggingWeights[partner], leggingToBody, t);
                fitted[i] = Strongest(blend);
                ++changed;
            }
            if (changed == 0)
            {
                Debug.LogError($"[{nameof(DemoOutfitFit)}] Nothing on {body.name} lies on {legs.name}; is the model upright (Z up)?");
                return null;
            }

            DemoItemBuilder.EnsureFolder(MeshDir);
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            bool created = mesh == null;
            if (created)
                mesh = new Mesh();
            Copy(source, mesh);
            mesh.name = System.IO.Path.GetFileNameWithoutExtension(meshPath);
            mesh.boneWeights = fitted;
            if (created)
                AssetDatabase.CreateAsset(mesh, meshPath);
            else
                EditorUtility.SetDirty(mesh);
            AssetDatabase.SaveAssets();

            Debug.Log($"[{nameof(DemoOutfitFit)}] {body.name}: {changed} of {bodyVertices.Length} vertices take {legs.name}'s skinning below z={high:0.00}.");
            return mesh;
        }

        private static void Add(Dictionary<int, float> blend, BoneWeight weight, int[] remap, float scale)
        {
            Add(blend, weight.boneIndex0, weight.weight0, remap, scale);
            Add(blend, weight.boneIndex1, weight.weight1, remap, scale);
            Add(blend, weight.boneIndex2, weight.weight2, remap, scale);
            Add(blend, weight.boneIndex3, weight.weight3, remap, scale);
        }

        private static void Add(Dictionary<int, float> blend, int bone, float weight, int[] remap, float scale)
        {
            if (remap != null)
                bone = remap[bone];
            if (weight <= 0f || scale <= 0f || bone < 0)
                return;
            float current;
            blend.TryGetValue(bone, out current);
            blend[bone] = current + weight * scale;
        }

        /// <summary>The four strongest influences, renormalised: what a mesh can carry at the default skin quality.</summary>
        private static BoneWeight Strongest(Dictionary<int, float> blend)
        {
            var ranked = new List<KeyValuePair<int, float>>(blend);
            ranked.Sort((a, b) => b.Value.CompareTo(a.Value));
            int count = Mathf.Min(4, ranked.Count);
            float sum = 0f;
            for (int i = 0; i < count; ++i)
                sum += ranked[i].Value;
            var weight = new BoneWeight();
            if (count > 0) { weight.boneIndex0 = ranked[0].Key; weight.weight0 = ranked[0].Value / sum; }
            if (count > 1) { weight.boneIndex1 = ranked[1].Key; weight.weight1 = ranked[1].Value / sum; }
            if (count > 2) { weight.boneIndex2 = ranked[2].Key; weight.weight2 = ranked[2].Value / sum; }
            if (count > 3) { weight.boneIndex3 = ranked[3].Key; weight.weight3 = ranked[3].Value / sum; }
            return weight;
        }

        /// <summary>
        /// Everything about a mesh but its weights, carried by hand: `EditorUtility.CopySerialized` corrupts
        /// the vertex stride of a mesh (see `BaseBodySplitter.SaveMeshes`), so it is never used on one.
        /// </summary>
        private static void Copy(Mesh from, Mesh to)
        {
            to.Clear();
            to.indexFormat = from.indexFormat;
            to.vertices = from.vertices;
            if (from.normals.Length > 0)
                to.normals = from.normals;
            if (from.tangents.Length > 0)
                to.tangents = from.tangents;
            if (from.colors32.Length > 0)
                to.colors32 = from.colors32;
            var uvs = new List<Vector4>();
            for (int channel = 0; channel < 8; ++channel)
            {
                uvs.Clear();
                from.GetUVs(channel, uvs);
                if (uvs.Count > 0)
                    to.SetUVs(channel, uvs);
            }
            to.subMeshCount = from.subMeshCount;
            for (int i = 0; i < from.subMeshCount; ++i)
                to.SetIndices(from.GetIndices(i), from.GetTopology(i), i);
            to.bindposes = from.bindposes;
            to.bounds = from.bounds;
            if (from.blendShapeCount > 0)
                Debug.LogWarning($"[{nameof(DemoOutfitFit)}] {from.name} has blend shapes, which the fitted copy does not carry.");
        }
    }
}
