using System.Collections.Generic;
using System.Text;
using MultiplayerARPG.GameData.Model.Playables;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// Dresses every character the demo ships and looks for the two ways cloth and skin can
    /// fail to meet: a bare body part drawn on top of a garment, and a garment you can see
    /// through.
    ///
    /// Both are easy to miss by eye and easy to trade for one another by accident. Skin
    /// under a garment has to sit far enough inside it to stay hidden — these garments are
    /// cut straight onto the body and in places right through it, as much as 36mm inside the
    /// skin across the back of the peasant shirt — while skin behind a collar has to stay
    /// near the surface, or the opening it is there to fill shows a gap at the rim instead.
    /// Push the skin in to fix the first and you open the second, so neither number means
    /// anything without the other, and both are measured here.
    ///
    /// Three passes, because a fault can live in any of three places:
    /// <list type="bullet">
    /// <item>every character model the demo builds, exactly as it ships, accessories and
    /// all — what you actually meet in the village;</item>
    /// <item>the player wearing each armour set, taken from the item data rather than
    /// guessed, since a player's clothes arrive through equipment and not through the
    /// model;</item>
    /// <item>every outfit in the library on both bodies, which is the broad sweep — and the
    /// only pass that covers the female garments, since the armour items carry male meshes
    /// only.</item>
    /// </list>
    ///
    /// **Read the pictures, not just the numbers.** Skin showing through a hole a garment is
    /// *meant* to have — the gap between the ranger's two collar straps, the shadow between
    /// a helm and a gorget, the nape between hair and a bodice — is counted the same as skin
    /// coming through solid cloth, because from a single frame the two are the same thing.
    /// A run that scores a few hundred pixels of those is clean; what you are looking for is
    /// a patch in the middle of a panel.
    /// </summary>
    public static class DemoOutfitAudit
    {
        private const string OutfitDir = "Assets/Plugins/Quaternius/Characters/Models/Outfits";
        private const string ModelDir = "Assets/OpenMMORPG/Demo/Prefabs/GamePlay/CharacterModels";
        private const string ItemDir = "Assets/OpenMMORPG/Demo/GameData";

        private const int Width = 800;
        private const int Height = 640;

        /// <summary>How many yaws around the character each body is looked at from.</summary>
        private const int Views = 8;

        /// <summary>
        /// How far back the camera stands, in metres, and how wide a lens it uses. The two
        /// go together: the range is set by what has to be cleared and the field of view
        /// then chosen to frame the same shoulders either way.
        ///
        /// These are the shortest ranges used. A character that reaches further than this
        /// pushes the camera out to clear it — see <see cref="Clearance"/>. From inside an
        /// arm, backface culling hides its near wall and every surface behind it reads as a
        /// hole; from inside a hand you are looking at the inside of a thumb.
        /// </summary>
        private const float SkinRange = 1.40f;

        private const float SeeRange = 1.60f;

        /// <summary>
        /// How tall a slice of the character each test frames, in metres — shoulders and a
        /// collar. The camera's range varies with what it has to clear, so the lens is
        /// worked out from this to keep every picture at the same scale.
        /// </summary>
        private const float SkinFraming = 0.41f;

        private const float SeeFraming = 0.42f;

        /// <summary>
        /// How much daylight to leave between the camera and the character's furthest
        /// extremity, in metres.
        /// </summary>
        private const float Clearance = 0.45f;

        /// <summary>
        /// A patch of skin this big or bigger is taken to be a limb rather than a fault.
        /// Only reached by patches touching no edge of the frame; the neck and the arms run
        /// off the edges of it and are told apart by that alone in most views.
        /// </summary>
        private const int LimbPatch = 3000;

        /// <summary>
        /// The pose everything is measured in.
        ///
        /// Not the bind pose. These bodies are modelled in a T-pose, which puts an
        /// outstretched arm on the line between the camera and the collar from either side —
        /// no distance fixes that, since the hand simply fills whatever frame the shoulders
        /// are framed in. Standing the character up the way a player actually sees it clears
        /// every view and measures the pose that matters.
        /// </summary>
        private const string PoseClip = "Idle_Loop";

        /// <summary>
        /// Where a picture of each fault is left, so that a number can be looked at. Under
        /// Temp so it is thrown away with the rest of the editor's scratch space rather than
        /// turning up in the project.
        /// </summary>
        private const string ShotDir = "Temp/OutfitAudit";

        /// <summary>
        /// The layer everything this tool makes is put on, with the camera and the light set
        /// to see nothing else.
        ///
        /// Without it the audit measures whatever scene happens to be open in the editor.
        /// A new scene is opened additively — the one already loaded stays loaded, and its
        /// objects go on rendering into every shot. That is not merely noise: scenery
        /// standing behind the character fills in the background, so a hole in a garment
        /// shows a hillside instead of the backdrop and is counted as covered. The test
        /// silently reports nothing wrong.
        /// </summary>
        private const int LonelyLayer = 31;

        private static readonly string[] Sockets = { "Head", "Body", "Arms", "Legs", "Feet" };

        /// <summary>The outfits the library ships, which is also how armour items are named.</summary>
        private static readonly string[] Outfits = { "Knight", "Noble", "Peasant", "Ranger", "Wizard" };

        [MenuItem("Open MMORPG/Demo/Audit Outfits")]
        public static void Audit()
        {
            // Cleared first, so that what is left is this run's and not a mixture of runs.
            if (System.IO.Directory.Exists(ShotDir))
                System.IO.Directory.Delete(ShotDir, true);

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var lightGo = new GameObject("Light");
            EditorSceneManager.MoveGameObjectToScene(lightGo, scene);
            Light light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            light.cullingMask = 1 << LonelyLayer;
            lightGo.transform.rotation = Quaternion.Euler(25f, 200f, 0f);

            var camGo = new GameObject("Camera");
            EditorSceneManager.MoveGameObjectToScene(camGo, scene);
            Camera camera = camGo.AddComponent<Camera>();
            camera.nearClipPlane = 0.05f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.cullingMask = 1 << LonelyLayer;

            Material green = Unlit(new Color(0f, 1f, 0f));
            Material grey = Unlit(new Color(0.5f, 0.5f, 0.5f));

            var report = new StringBuilder();
            report.AppendLine($"[{nameof(DemoOutfitAudit)}] body parts over cloth / see-through, in pixels");
            int worst = 0;

            report.AppendLine("  as built:");
            var built = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:GameObject", new[] { ModelDir }))
                built.Add(AssetDatabase.GUIDToAssetPath(guid));
            built.Sort();
            foreach (string path in built)
            {
                GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (model == null)
                    continue;
                worst = Mathf.Max(worst, Row(report, scene, camera, green, grey, model,
                    new List<Worn>(), System.IO.Path.GetFileNameWithoutExtension(path)));
            }

            report.AppendLine("  player, wearing each set:");
            GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(
                $"{ModelDir}/PlayerCharacterModel_Male.prefab");
            if (player != null)
            {
                foreach (string outfit in Outfits)
                {
                    List<Worn> set = ArmourSet(outfit);
                    if (set.Count == 0)
                        continue;
                    worst = Mathf.Max(worst, Row(report, scene, camera, green, grey, player,
                        set, $"equipped {outfit}"));
                }
            }

            report.AppendLine("  library, one piece per slot:");
            foreach (string gender in new[] { "Male", "Female" })
            {
                GameObject body = AssetDatabase.LoadAssetAtPath<GameObject>(
                    $"{ModelDir}/PlayerCharacterModel_{gender}.prefab");
                if (body == null)
                {
                    Debug.LogError($"[{nameof(DemoOutfitAudit)}] No {gender} character model to dress.");
                    continue;
                }
                foreach (string outfit in Outfits)
                {
                    List<Worn> pieces = LibrarySet(gender, outfit);
                    if (pieces.Count == 0)
                        continue;
                    worst = Mathf.Max(worst, Row(report, scene, camera, green, grey, body,
                        pieces, $"{gender} {outfit}"));
                }
            }

            if (worst == 0)
            {
                Debug.Log(report.ToString());
            }
            else
            {
                report.AppendLine($"  pictures of each fault are in {ShotDir}");
                Debug.LogWarning(report.ToString());
            }
            EditorSceneManager.CloseScene(scene, true);
        }

        /// <summary>One garment on one socket.</summary>
        private struct Worn
        {
            public string Socket;
            public GameObject Piece;
        }

        private static int Row(StringBuilder report, Scene scene, Camera camera, Material green,
            Material grey, GameObject prefab, List<Worn> worn, string label)
        {
            int skin, see;
            Measure(scene, camera, prefab, worn, label, green, grey, out skin, out see);
            report.AppendLine($"    {label,-28} skin {skin,6}   see-through {see,6}");
            return skin + see;
        }

        private static Material Unlit(Color colour)
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            material.SetColor("_BaseColor", colour);
            // Double sided, so that a surface facing away still counts as covering.
            material.SetFloat("_Cull", 0f);
            return material;
        }

        /// <summary>
        /// What a player is actually wearing in a set, read off the armour items themselves.
        ///
        /// Taken from the item data rather than from the library, because that is the path a
        /// garment reaches a player by: an armour item names a mesh and a socket, and the kit
        /// hangs that mesh on that socket. Guessing the set from file names instead would
        /// audit a combination nobody can equip, and would miss the pauldron that covers the
        /// shoulder.
        /// </summary>
        private static List<Worn> ArmourSet(string outfit)
        {
            var set = new List<Worn>();
            foreach (string guid in AssetDatabase.FindAssets("t:ScriptableObject", new[] { ItemDir }))
            {
                var item = AssetDatabase.LoadAssetAtPath<ScriptableObject>(
                    AssetDatabase.GUIDToAssetPath(guid)) as ArmorItem;
                if (item == null || !item.name.StartsWith(outfit))
                    continue;
                SerializedProperty models = new SerializedObject(item).FindProperty("equipmentModels");
                if (models == null)
                    continue;
                for (int i = 0; i < models.arraySize; ++i)
                {
                    SerializedProperty entry = models.GetArrayElementAtIndex(i);
                    var mesh = entry.FindPropertyRelative("meshPrefab").objectReferenceValue as GameObject;
                    if (mesh == null)
                        continue;
                    set.Add(new Worn
                    {
                        Socket = entry.FindPropertyRelative("equipSocket").stringValue,
                        Piece = mesh,
                    });
                }
            }
            return set;
        }

        /// <summary>
        /// An outfit straight out of the library, one piece per slot.
        ///
        /// Where a slot has alternatives — the knight's plate and its tabard, its helm and
        /// its horns — the first is taken; this pass is about how cloth meets skin, and
        /// either alternative answers that. The shipped combinations, alternatives and
        /// accessories and all, are covered by the built models instead.
        /// </summary>
        private static List<Worn> LibrarySet(string gender, string outfit)
        {
            var set = new List<Worn>();
            var taken = new HashSet<string>();
            var paths = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { OutfitDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string file = System.IO.Path.GetFileNameWithoutExtension(path);
                if (file.StartsWith($"{gender}_{outfit}_") && !file.Contains("_Acc_"))
                    paths.Add(path);
            }
            paths.Sort();
            foreach (string path in paths)
            {
                string socket = Socket(path);
                if (socket == null || !taken.Add(socket))
                    continue;
                set.Add(new Worn
                {
                    Socket = socket,
                    Piece = AssetDatabase.LoadAssetAtPath<GameObject>(path),
                });
            }
            return set;
        }

        private static string Socket(string path)
        {
            string file = System.IO.Path.GetFileNameWithoutExtension(path);
            foreach (string socket in Sockets)
                if (file.Contains("_" + socket))
                    return socket;
            return null;
        }

        private static void Measure(Scene scene, Camera camera, GameObject prefab, List<Worn> worn,
            string label, Material green, Material grey, out int skinTotal, out int seeTotal)
        {
            skinTotal = 0;
            seeTotal = 0;

            var character = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            PrefabUtility.UnpackPrefabInstance(character, PrefabUnpackMode.Completely,
                InteractionMode.AutomatedAction);

            var model = character.GetComponent<PlayableCharacterModel>();
            var defaults = new Dictionary<string, GameObject>();
            if (model != null)
            {
                foreach (var container in model.EquipmentContainers)
                    if (container.defaultModel != null)
                        defaults[container.equipSocket] = container.defaultModel;
            }

            var bones = new Dictionary<string, Transform>();
            foreach (Transform bone in character.GetComponentsInChildren<Transform>(true))
                if (!bones.ContainsKey(bone.name))
                    bones[bone.name] = bone;

            var replaced = new HashSet<GameObject>();
            foreach (Worn piece in worn)
            {
                if (piece.Piece == null)
                    continue;
                Graft(character, piece.Piece, bones);
                if (defaults.ContainsKey(piece.Socket))
                    replaced.Add(defaults[piece.Socket]);
            }

            var lit = new List<SkinnedMeshRenderer>();
            var shipped = new List<Material[]>();
            var loose = new List<SkinnedMeshRenderer>();
            var bare = new List<SkinnedMeshRenderer>();
            foreach (var renderer in character.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                renderer.updateWhenOffscreen = true;
                if (replaced.Contains(renderer.gameObject))
                {
                    renderer.enabled = false;
                    continue;
                }
                if (IsBodyPart(renderer.sharedMesh))
                    bare.Add(renderer);
                string name = renderer.name.ToLower();
                // Hair and eyebrows are cut out of a transparent sheet, so a solid stand-in
                // fills the whole sheet and every gap between strands reads as a hole.
                if (name.Contains("hair") || name.Contains("brow"))
                    loose.Add(renderer);
                lit.Add(renderer);
                shipped.Add(renderer.sharedMaterials);
            }

            // After grafting, so that the garments come with it.
            foreach (Transform part in character.GetComponentsInChildren<Transform>(true))
                part.gameObject.layer = LonelyLayer;

            AnimationClip pose = DemoAnimationSet.Clip(PoseClip);
            if (pose != null)
                pose.SampleAnimation(character, 0f);

            Transform neck = bones.ContainsKey("neck_01") ? bones["neck_01"] : character.transform;
            Vector3 focus = neck.position + new Vector3(0f, -0.13f, 0f);

            // How far out the character actually reaches, rather than how far out a body is
            // assumed to. A fixed range is not safe: these are T-posed, arm spans differ
            // between the bodies, and standing inside an arm turns every surface behind it
            // into a hole as far as either test is concerned.
            float reach = 0f;
            foreach (KeyValuePair<string, Transform> bone in bones)
            {
                Vector3 out2 = bone.Value.position - focus;
                out2.y = 0f;
                reach = Mathf.Max(reach, out2.magnitude);
            }
            float skinRange = Mathf.Max(SkinRange, reach + Clearance);
            float seeRange = Mathf.Max(SeeRange, reach + Clearance);

            for (int view = 0; view < Views; ++view)
            {
                Quaternion yaw = Quaternion.Euler(0f, view * (360f / Views), 0f);
                int degrees = view * (360 / Views);

                if (bare.Count > 0)
                {
                    Aim(camera, focus, yaw, skinRange, SkinFraming);
                    camera.backgroundColor = new Color(0.6f, 0.62f, 0.68f);
                    var uncovered = new List<Material[]>();
                    foreach (var renderer in bare)
                    {
                        uncovered.Add(renderer.sharedMaterials);
                        renderer.sharedMaterials = Fill(green, renderer.sharedMaterials.Length);
                    }
                    Color32[] painted = Shoot(camera);
                    for (int i = 0; i < bare.Count; ++i)
                        bare[i].sharedMaterials = uncovered[i];
                    bool[] adrift;
                    int count = Adrift(painted, out adrift);
                    skinTotal += count;
                    if (count > 0)
                        Mark(Shoot(camera), adrift, new Color32(255, 0, 255, 255),
                            $"{label}_skin_{degrees}");
                }

                Aim(camera, focus, yaw, seeRange, SeeFraming);
                camera.backgroundColor = new Color(1f, 0f, 1f);
                foreach (var renderer in loose)
                    renderer.enabled = false;
                Color32[] asShipped = Shoot(camera);
                for (int i = 0; i < lit.Count; ++i)
                    if (lit[i].enabled)
                        lit[i].sharedMaterials = Fill(grey, shipped[i].Length);
                Color32[] asSolid = Shoot(camera);
                for (int i = 0; i < lit.Count; ++i)
                    lit[i].sharedMaterials = shipped[i];
                foreach (var renderer in loose)
                    renderer.enabled = true;

                var through = new bool[asShipped.Length];
                int holes = 0;
                for (int i = 0; i < asShipped.Length; ++i)
                {
                    if (!IsBackground(asShipped[i]) || IsBackground(asSolid[i]))
                        continue;
                    // Only where the backdrop shows through properly, not in the hairline
                    // along an outline. Where a surface turns edge-on, the last sliver of it
                    // is rasterised as facing away and culled, while the double-sided
                    // stand-in still draws it — so every single-sided silhouette is trimmed
                    // by about a pixel and reads as a hole. That is the metric's own doing,
                    // not the character's: it showed up on a bare body, which is the only
                    // thing here still culled to one side now that the garments are not.
                    if (!Surrounded(asShipped, i))
                        continue;
                    through[i] = true;
                    ++holes;
                }
                seeTotal += holes;
                if (holes > 0)
                {
                    camera.backgroundColor = new Color(0.6f, 0.62f, 0.68f);
                    Mark(Shoot(camera), through, new Color32(255, 0, 0, 255),
                        $"{label}_see_{degrees}");
                }
            }

            Object.DestroyImmediate(character);
        }

        /// <summary>
        /// Whether a mesh is one of the bare body parts the splitter cuts — the things that
        /// must never be seen through a garment. Garments carry skin of their own, a rolled
        /// sleeve or a bare midriff, and that is part of the outfit rather than under it.
        /// The two are told apart by name: parts are "Body_Male", pieces "Male_Peasant_Body".
        /// </summary>
        private static bool IsBodyPart(Mesh mesh)
        {
            if (mesh == null)
                return false;
            foreach (string socket in Sockets)
                if (mesh.name == $"{socket}_Male" || mesh.name == $"{socket}_Female")
                    return true;
            return false;
        }

        /// <summary>
        /// Moves a garment's renderers onto the character's own skeleton, matching bones by
        /// name. The outfit pack and the bodies are rigged alike, so a name match is a bone
        /// match.
        /// </summary>
        private static void Graft(GameObject character, GameObject piece,
            Dictionary<string, Transform> bones)
        {
            var source = (GameObject)PrefabUtility.InstantiatePrefab(piece);
            PrefabUtility.UnpackPrefabInstance(source, PrefabUnpackMode.Completely,
                InteractionMode.AutomatedAction);
            foreach (var renderer in source.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var rebound = new Transform[renderer.bones.Length];
                for (int i = 0; i < rebound.Length; ++i)
                {
                    Transform bone = renderer.bones[i];
                    rebound[i] = bone != null && bones.ContainsKey(bone.name) ? bones[bone.name] : null;
                }
                renderer.bones = rebound;
                if (renderer.rootBone != null && bones.ContainsKey(renderer.rootBone.name))
                    renderer.rootBone = bones[renderer.rootBone.name];
                renderer.transform.SetParent(character.transform, false);
                renderer.updateWhenOffscreen = true;
            }
            Object.DestroyImmediate(source);
        }

        private static void Aim(Camera camera, Vector3 focus, Quaternion yaw, float range, float framing)
        {
            camera.transform.position = focus + yaw * new Vector3(0f, 0.10f, -1f) * range;
            camera.transform.LookAt(focus);
            camera.fieldOfView = 2f * Mathf.Atan2(framing * 0.5f, range) * Mathf.Rad2Deg;
        }

        private static Material[] Fill(Material material, int count)
        {
            var materials = new Material[count];
            for (int i = 0; i < count; ++i)
                materials[i] = material;
            return materials;
        }

        private static Color32[] Shoot(Camera camera)
        {
            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            var frame = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            frame.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            frame.Apply();
            RenderTexture.active = null;
            camera.targetTexture = null;
            Color32[] pixels = frame.GetPixels32();
            Object.DestroyImmediate(frame);
            target.Release();
            Object.DestroyImmediate(target);
            return pixels;
        }

        private static bool IsBackground(Color32 pixel)
        {
            return pixel.r > 200 && pixel.g < 60 && pixel.b > 200;
        }

        /// <summary>Whether a pixel and all four of its neighbours are backdrop.</summary>
        private static bool Surrounded(Color32[] frame, int index)
        {
            int x = index % Width;
            int y = index / Width;
            if (x == 0 || y == 0 || x == Width - 1 || y == Height - 1)
                return false;
            return IsBackground(frame[index - 1]) && IsBackground(frame[index + 1]) &&
                   IsBackground(frame[index - Width]) && IsBackground(frame[index + Width]);
        }

        /// <summary>
        /// Counts painted skin stranded in the middle of the frame. The neck, and an arm in a
        /// sleeveless outfit, are patches running off an edge of it, so touching an edge is
        /// enough to tell them from a speck of skin surfacing through a shoulder.
        /// </summary>
        private static int Adrift(Color32[] pixels, out bool[] adrift)
        {
            adrift = new bool[pixels.Length];
            var painted = new bool[pixels.Length];
            for (int i = 0; i < pixels.Length; ++i)
                painted[i] = pixels[i].g > 150 && pixels[i].r < 90 && pixels[i].b < 90;

            var visited = new bool[pixels.Length];
            var stack = new Stack<int>();
            var patch = new List<int>();
            int stranded = 0;
            for (int start = 0; start < painted.Length; ++start)
            {
                if (!painted[start] || visited[start])
                    continue;
                int size = 0;
                bool reachesEdge = false;
                patch.Clear();
                stack.Push(start);
                visited[start] = true;
                while (stack.Count > 0)
                {
                    int at = stack.Pop();
                    patch.Add(at);
                    ++size;
                    int x = at % Width;
                    int y = at / Width;
                    if (x == 0 || y == 0 || x == Width - 1 || y == Height - 1)
                        reachesEdge = true;
                    Spread(painted, visited, stack, x + 1, y);
                    Spread(painted, visited, stack, x - 1, y);
                    Spread(painted, visited, stack, x, y + 1);
                    Spread(painted, visited, stack, x, y - 1);
                }
                if (!reachesEdge && size < LimbPatch)
                {
                    stranded += size;
                    foreach (int index in patch)
                        adrift[index] = true;
                }
            }
            return stranded;
        }

        /// <summary>Writes the frame out with the faulty pixels painted, to be looked at.</summary>
        private static void Mark(Color32[] frame, bool[] faults, Color32 colour, string name)
        {
            for (int i = 0; i < frame.Length; ++i)
                if (faults[i])
                    frame[i] = colour;
            var picture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            picture.SetPixels32(frame);
            picture.Apply();
            System.IO.Directory.CreateDirectory(ShotDir);
            System.IO.File.WriteAllBytes($"{ShotDir}/{name.Replace(' ', '_')}.png", picture.EncodeToPNG());
            Object.DestroyImmediate(picture);
        }

        private static void Spread(bool[] painted, bool[] visited, Stack<int> stack, int x, int y)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height)
                return;
            int index = y * Width + x;
            if (!painted[index] || visited[index])
                return;
            visited[index] = true;
            stack.Push(index);
        }
    }
}
