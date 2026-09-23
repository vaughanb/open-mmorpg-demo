using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// Makes the demo self-contained: copies the art it actually uses into the demo
    /// itself, at a resolution the demo actually needs, and repoints every reference.
    ///
    /// Only `Assets/OpenMMORPG` ships, so anything the demo references from the asset
    /// libraries elsewhere in the project arrives broken for whoever imports the kit.
    /// And the libraries are authored at 4K — a single outfit normal map is 14MB — which
    /// the demo throws away at import anyway.
    ///
    /// This is the last step of the demo pipeline, and it is incremental: the builders
    /// keep reading the source libraries, and whatever they leave pointing outside the
    /// demo is brought in here. Three things happen:
    ///
    /// 1. Every external dependency is copied under `Demo/Art`, mirroring its library's
    ///    folder layout, and the copied textures are resampled down. A copy that already
    ///    exists is reused, so rerunning after a rebuild only brings in what is new.
    /// 2. The animation library is not copied — it is 68MB for 188 clips, of which the
    ///    demo plays 50 — but the clips the demo references are extracted as standalone
    ///    `.anim` assets. An instantiated copy keeps the muscle curves, the loop and
    ///    orientation settings and the events, and comes out at about a quarter of a
    ///    megabyte each.
    /// 3. Every text asset under the demo (the copies included, and the copies' importer
    ///    settings) has its references rewritten from the library GUIDs to the copies'.
    ///    Sub-asset ids inside an FBX are derived from the object's name, so a mesh or
    ///    material reference survives the change of GUID; a clip reference is rewritten
    ///    to the extracted `.anim` instead.
    ///
    /// The rewrite is textual, which is why it must not run while a demo scene is open:
    /// the editor would later save its in-memory copy over the file. The tool switches
    /// to an empty scene first and puts the previous one back afterwards.
    ///
    /// One file is not text: Unity writes a scene that holds a Terrain as binary
    /// whatever the project's serialisation mode says (measured root by root - the
    /// island alone turns the file binary, every other root saves as YAML). The map
    /// scene is therefore remapped in memory instead: each placed library prefab has
    /// its source swapped for the copy, keeping every override, and every other
    /// reference is retargeted at the matching object in the copy by its local id.
    /// </summary>
    public static class DemoArtCollector
    {
        public const string DemoDir = "Assets/OpenMMORPG/Demo";
        public const string ArtDir = DemoDir + "/Art";
        /// <summary>
        /// Where extracted library clips land. Deliberately **not** under `Art/`, unlike
        /// every other collected asset: the demo also authors animation of its own - the
        /// Mixamo skill clips and the generated bow shot - and having half the animation
        /// in `Demo/Art/Animations` and half in `Demo/Animations` meant neither folder
        /// answered "where are the animations". They are all in the second one now.
        ///
        /// Nothing about self-containment depends on the split: `Verify` counts anything
        /// under `Demo/` as inside, not anything under `Demo/Art/`.
        /// </summary>
        public const string ClipDir = DemoDir + "/Animations";
        private const string KitDir = "Assets/OpenMMORPG/";

        /// <summary>
        /// Most textures belong to one prop or one outfit and are never seen close up.
        /// </summary>
        private const int TextureSize = 1024;

        /// <summary>
        /// Trim sheets are shared by a couple of hundred props at once, so they carry
        /// far more of the frame than any single texture and keep more resolution.
        /// </summary>
        private const int SharedSheetSize = 2048;

        private static readonly string[] SharedSheetMarkers = { "T_Trim_", "T_Brick", "T_Plaster", "T_Wood", "T_Roof" };

        /// <summary>Where each source library lands under Demo/Art.</summary>
        private static readonly Dictionary<string, string> PackFolders = new Dictionary<string, string>
        {
            { "Assets/Plugins/Quaternius/Characters", ArtDir + "/Characters" },
            { "Assets/Plugins/Quaternius/Nature", ArtDir + "/Nature" },
            { "Assets/Plugins/Quaternius/Village", ArtDir + "/Village" },
            { "Assets/Plugins/Quaternius/Props", ArtDir + "/Props" },
            { "Assets/Plugins/Malagen", ArtDir + "/Weapons" },
            { "Assets/Plugins/Blue Sky Skybox Pack", ArtDir + "/Sky" },
        };

        /// <summary>
        /// Text asset types the rewrite walks. Everything the demo serialises is text
        /// (the project forces text serialisation), and `.meta` files carry an FBX's
        /// material remaps.
        /// </summary>
        private static readonly HashSet<string> TextAssetExtensions = new HashSet<string>
        {
            ".prefab", ".unity", ".mat", ".asset", ".anim", ".controller", ".overridecontroller",
            ".terrainlayer", ".playable", ".mask", ".physicmaterial", ".spriteatlas", ".meta",
        };

        [MenuItem("Open MMORPG/Demo/Collect Demo Art")]
        public static void Collect()
        {
            List<string> external = ExternalDependencies();
            if (external.Count == 0)
            {
                Debug.Log($"[{nameof(DemoArtCollector)}] The demo already references nothing outside itself.");
                return;
            }

            string previousScene = LeaveDemoScenes();
            if (previousScene == null)
                return;

            try
            {
                var guidMap = new Dictionary<string, string>();
                var clipMap = new Dictionary<string, string>();
                var copied = new Dictionary<string, string>();
                long before = 0;

                // The animation libraries are never copied whole - UAL2 alone is 70MB for 134
                // clips - only the clips the demo plays are extracted from them, below.
                var libraries = new List<string>(DemoAnimationSet.LibraryPaths);
                foreach (string source in external)
                {
                    if (libraries.Contains(source))
                        continue;
                    string destination = Destination(source);
                    if (destination == null)
                        continue;
                    var info = new System.IO.FileInfo(source);
                    if (info.Exists)
                        before += info.Length;
                    EnsureFolder(destination.Substring(0, destination.LastIndexOf('/')));
                    if (AssetDatabase.LoadAssetAtPath<Object>(destination) == null)
                    {
                        if (!AssetDatabase.CopyAsset(source, destination))
                        {
                            Debug.LogError($"[{nameof(DemoArtCollector)}] Could not copy \"{source}\".");
                            continue;
                        }
                        copied[source] = destination;
                    }
                    guidMap[AssetDatabase.AssetPathToGUID(source)] = AssetDatabase.AssetPathToGUID(destination);
                }
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                foreach (string library in libraries)
                {
                    if (external.Contains(library))
                        ExtractClips(library, libraries, clipMap);
                }

                Shrink(copied);
                AssetDatabase.SaveAssets();

                int rewritten = Rewrite(guidMap, libraries, clipMap);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                int scenes = RemapScenes(guidMap, libraries, clipMap);

                long after = 0;
                foreach (var pair in copied)
                {
                    var info = new System.IO.FileInfo(pair.Value);
                    if (info.Exists)
                        after += info.Length;
                }
                Debug.Log($"[{nameof(DemoArtCollector)}] Copied {copied.Count} files into {ArtDir} " +
                          $"({before / 1048576.0:F1} MB of source is now {after / 1048576.0:F1} MB), extracted " +
                          $"{clipMap.Count} clips, rewrote references in {rewritten} files and {scenes} scene objects.");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                if (!string.IsNullOrEmpty(previousScene))
                    EditorSceneManager.OpenScene(previousScene, OpenSceneMode.Single);
            }

            Verify();
        }

        /// <summary>
        /// Reports everything the demo still reaches outside itself, and every reference
        /// inside it that points at nothing.
        /// </summary>
        [MenuItem("Open MMORPG/Demo/Verify Demo Is Self-Contained")]
        public static void Verify()
        {
            List<string> external = ExternalDependencies();
            var report = new System.Text.StringBuilder();
            report.AppendLine($"[{nameof(DemoArtCollector)}] {external.Count} external dependencies, {MissingReferences(report)} missing references.");
            foreach (string path in external)
                report.AppendLine("  outside: " + path);
            if (external.Count == 0 && !report.ToString().Contains("  missing"))
                Debug.Log(report.ToString());
            else
                Debug.LogWarning(report.ToString());
        }

        /// <summary>Everything outside the kit that the demo's assets pull in, scripts aside.</summary>
        private static List<string> ExternalDependencies()
        {
            var roots = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("", new[] { DemoDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!AssetDatabase.IsValidFolder(path))
                    roots.Add(path);
            }
            var external = new List<string>();
            foreach (string path in AssetDatabase.GetDependencies(roots.ToArray(), true))
            {
                if (!path.StartsWith("Assets/") || path.StartsWith(KitDir))
                    continue;
                if (path.EndsWith(".cs") || path.EndsWith(".dll") || path.EndsWith(".asmdef"))
                    continue;
                external.Add(path);
            }
            external.Sort();
            return external;
        }

        /// <summary>Mirrors a source path under Demo/Art, keeping its library's folder structure.</summary>
        private static string Destination(string source)
        {
            foreach (var pack in PackFolders)
            {
                if (!source.StartsWith(pack.Key + "/"))
                    continue;
                string relative = source.Substring(pack.Key.Length + 1);
                // Malagen nests its weapons several folders deep; flatten to keep the
                // demo's own tree shallow and readable.
                if (pack.Key == "Assets/Plugins/Malagen")
                    relative = System.IO.Path.GetFileName(source);
                return pack.Value + "/" + relative;
            }
            Debug.LogWarning($"[{nameof(DemoArtCollector)}] \"{source}\" is outside every known pack; leaving it where it is.");
            return null;
        }

        /// <summary>
        /// Copies the clips the demo references out of the animation library as standalone
        /// assets, and records which library sub-asset each one stands in for.
        /// </summary>
        /// <remarks>
        /// **Keyed by library and id together, `"guid:fileId"`.** A clip's id inside an FBX is
        /// derived from its name, so two libraries that share a name share an id: UAL1 and
        /// UAL2 both have `A_TPose`, with the same id in both. Keyed by id alone, the second
        /// library's clip would be looked up as the first's.
        ///
        /// The extracted file is named after the clip, so a shared name would also make the
        /// second extraction reuse the first's `.anim` and point at the wrong motion without
        /// a word. Such a clip is refused instead, with an error, and stays outside the demo
        /// where `Verify` reports it - loud beats wrong.
        /// </remarks>
        private static void ExtractClips(string library, List<string> libraries, Dictionary<string, string> clipMap)
        {
            string libraryGuid = AssetDatabase.AssetPathToGUID(library);
            var elsewhere = new HashSet<string>();
            foreach (string other in libraries)
            {
                if (other == library || AssetDatabase.LoadAssetAtPath<Object>(other) == null)
                    continue;
                foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(other))
                {
                    var clip = asset as AnimationClip;
                    if (clip != null)
                        elsewhere.Add(clip.name);
                }
            }
            var wanted = new HashSet<string>();
            var pattern = new Regex(@"fileID: (-?\d+), guid: " + libraryGuid);
            foreach (string path in TextAssets())
            {
                foreach (Match match in pattern.Matches(System.IO.File.ReadAllText(path)))
                    wanted.Add(match.Groups[1].Value);
            }
            if (wanted.Count == 0)
                return;

            EnsureFolder(ClipDir);
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(library))
            {
                var clip = asset as AnimationClip;
                if (clip == null)
                    continue;
                string guid;
                long fileId;
                if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(clip, out guid, out fileId) || !wanted.Contains(fileId.ToString()))
                    continue;
                if (elsewhere.Contains(clip.name))
                {
                    Debug.LogError($"[{nameof(DemoArtCollector)}] \"{clip.name}\" is in more than one animation " +
                                   $"library, so its extracted copy would be ambiguous. Left in {library}; " +
                                   "rename it on import or play a different clip.");
                    continue;
                }
                string destination = $"{ClipDir}/{clip.name}.anim";
                if (AssetDatabase.LoadAssetAtPath<AnimationClip>(destination) == null)
                {
                    AnimationClip copy = Object.Instantiate(clip);
                    copy.name = clip.name;
                    AssetDatabase.CreateAsset(copy, destination);
                }
                clipMap[libraryGuid + ":" + fileId] = AssetDatabase.AssetPathToGUID(destination);
            }
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Resamples the copied textures down. Unity does the filtering: importing at the
        /// target size and reading the result back is better than resampling by hand, and
        /// it is the same image the demo would have used anyway.
        /// </summary>
        private static void Shrink(Dictionary<string, string> copied)
        {
            int shrunk = 0;
            var textures = new List<string>();
            foreach (var pair in copied)
            {
                if (pair.Value.EndsWith(".png") || pair.Value.EndsWith(".jpg"))
                    textures.Add(pair.Value);
            }

            try
            {
                for (int i = 0; i < textures.Count; ++i)
                {
                    string path = textures[i];
                    EditorUtility.DisplayProgressBar("Shrinking demo textures", path, i / (float)textures.Count);

                    var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                    if (importer == null)
                        continue;

                    int target = TextureSize;
                    foreach (string marker in SharedSheetMarkers)
                    {
                        if (path.Contains(marker))
                            target = SharedSheetSize;
                    }

                    var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    if (existing != null && existing.width <= target && existing.height <= target)
                        continue;

                    bool wasReadable = importer.isReadable;
                    TextureImporterCompression wasCompression = importer.textureCompression;
                    TextureImporterType wasType = importer.textureType;
                    int wasMax = importer.maxTextureSize;

                    // Read it back as plain colour data: a normal map read through its
                    // own importer comes back swizzled, and a compressed one cannot be
                    // read at all.
                    importer.textureType = TextureImporterType.Default;
                    importer.isReadable = true;
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.maxTextureSize = target;
                    importer.SaveAndReimport();

                    var source = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    byte[] encoded = source != null ? source.EncodeToPNG() : null;

                    importer.textureType = wasType;
                    importer.isReadable = wasReadable;
                    importer.textureCompression = wasCompression;
                    importer.maxTextureSize = wasMax;

                    if (encoded == null)
                    {
                        importer.SaveAndReimport();
                        continue;
                    }

                    // A jpg is rewritten as png in place under its own name, so that the
                    // GUID and every reference to it stay put; the extension is then a lie
                    // Unity does not mind, since it sniffs the image format.
                    System.IO.File.WriteAllBytes(path, encoded);
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                    ++shrunk;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            Debug.Log($"[{nameof(DemoArtCollector)}] Resampled {shrunk} textures.");
        }

        /// <summary>
        /// Rewrites every reference in the demo's text assets from a library asset to its
        /// copy, and from a library clip to its extracted `.anim`. Returns how many files
        /// changed.
        /// </summary>
        private static int Rewrite(Dictionary<string, string> guidMap, List<string> libraries, Dictionary<string, string> clipMap)
        {
            var libraryGuids = new List<string>();
            foreach (string library in libraries)
                libraryGuids.Add(AssetDatabase.AssetPathToGUID(library));
            var clipPattern = new Regex(@"\{fileID: (-?\d+), guid: (" + string.Join("|", libraryGuids.ToArray()) + @"), type: 3\}");
            var guidPattern = new Regex(@"guid: ([0-9a-f]{32})");
            int changed = 0;
            foreach (string path in TextAssets())
            {
                if (!IsText(path))
                    continue;
                string text = System.IO.File.ReadAllText(path);
                string updated = text;
                if (clipMap.Count > 0)
                {
                    updated = clipPattern.Replace(updated, match =>
                    {
                        string replacement;
                        return clipMap.TryGetValue(match.Groups[2].Value + ":" + match.Groups[1].Value, out replacement)
                            ? "{fileID: 7400000, guid: " + replacement + ", type: 2}"
                            : match.Value;
                    });
                }
                updated = guidPattern.Replace(updated, match =>
                {
                    string replacement;
                    return guidMap.TryGetValue(match.Groups[1].Value, out replacement)
                        ? "guid: " + replacement
                        : match.Value;
                });
                if (updated == text)
                    continue;
                System.IO.File.WriteAllText(path, updated, new System.Text.UTF8Encoding(false));
                ++changed;
            }
            return changed;
        }

        /// <summary>
        /// Repoints the demo's scenes in memory: placed library prefabs are swapped for
        /// their copies with overrides kept, and any other reference to a library asset
        /// is retargeted at the same object inside the copy. Returns how many objects
        /// changed. Needed for the map scene, which is binary (see the class notes), and
        /// harmless for the text ones, which the rewrite has already covered.
        /// </summary>
        private static int RemapScenes(Dictionary<string, string> guidMap, List<string> libraries, Dictionary<string, string> clipMap)
        {
            var libraryGuids = new HashSet<string>();
            foreach (string library in libraries)
                libraryGuids.Add(AssetDatabase.AssetPathToGUID(library));
            var counterparts = new Dictionary<string, Dictionary<long, Object>>();
            var settings = new PrefabReplacingSettings
            {
                objectMatchMode = ObjectMatchMode.ByHierarchy,
                prefabOverridesOptions = PrefabOverridesOptions.KeepAllPossibleOverrides,
                logInfo = false,
            };
            int changed = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Scene", new[] { DemoDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                int changedHere = 0;

                // Placed prefabs first, outermost roots only: what they inherit is then
                // already pointing at the copies, and the pass below leaves it alone.
                var placed = new List<GameObject>();
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    {
                        if (PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject))
                            placed.Add(t.gameObject);
                    }
                }
                foreach (GameObject instance in placed)
                {
                    if (instance == null)
                        continue;
                    GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(instance);
                    string sourceGuid = source != null ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source)) : null;
                    string copyGuid;
                    if (string.IsNullOrEmpty(sourceGuid) || !guidMap.TryGetValue(sourceGuid, out copyGuid))
                        continue;
                    var replacement = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(copyGuid));
                    if (replacement == null)
                        continue;
                    PrefabUtility.ReplacePrefabAssetOfPrefabInstance(instance, replacement, settings, InteractionMode.AutomatedAction);
                    ++changedHere;
                }

                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (Component component in root.GetComponentsInChildren<Component>(true))
                    {
                        if (component == null)
                            continue;
                        var serialized = new SerializedObject(component);
                        SerializedProperty property = serialized.GetIterator();
                        bool dirty = false;
                        while (property.Next(true))
                        {
                            if (property.propertyType != SerializedPropertyType.ObjectReference || property.objectReferenceValue == null)
                                continue;
                            Object replacement = Counterpart(property.objectReferenceValue, guidMap, libraryGuids, clipMap, counterparts);
                            if (replacement == null)
                                continue;
                            property.objectReferenceValue = replacement;
                            dirty = true;
                        }
                        if (!dirty)
                            continue;
                        serialized.ApplyModifiedPropertiesWithoutUndo();
                        ++changedHere;
                    }
                }

                if (changedHere > 0)
                    EditorSceneManager.SaveScene(scene);
                changed += changedHere;
            }
            return changed;
        }

        /// <summary>
        /// The object in the demo's copy that stands for a library object: the extracted
        /// clip for a library clip, otherwise the object with the same local id inside the
        /// copied asset - which an FBX derives from the object's name, so it survives the
        /// copy. Null when the object is not a library one.
        /// </summary>
        private static Object Counterpart(Object target, Dictionary<string, string> guidMap, HashSet<string> libraryGuids,
                                          Dictionary<string, string> clipMap, Dictionary<string, Dictionary<long, Object>> cache)
        {
            string guid;
            long fileId;
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(target, out guid, out fileId))
                return null;
            string clipGuid;
            if (libraryGuids.Contains(guid) && clipMap.TryGetValue(guid + ":" + fileId, out clipGuid))
                return AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(clipGuid));
            string copyGuid;
            if (!guidMap.TryGetValue(guid, out copyGuid))
                return null;
            string copyPath = AssetDatabase.GUIDToAssetPath(copyGuid);
            Dictionary<long, Object> byId;
            if (!cache.TryGetValue(copyPath, out byId))
            {
                byId = new Dictionary<long, Object>();
                foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(copyPath))
                {
                    string g;
                    long id;
                    if (asset != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out g, out id))
                        byId[id] = asset;
                }
                cache[copyPath] = byId;
            }
            Object counterpart;
            if (byId.TryGetValue(fileId, out counterpart))
                return counterpart;
            Debug.LogWarning($"[{nameof(DemoArtCollector)}] \"{copyPath}\" has nothing with id {fileId} to stand in for \"{target.name}\".");
            return null;
        }

        /// <summary>Whether a file is YAML rather than one Unity wrote in binary.</summary>
        private static bool IsText(string path)
        {
            // A .meta is always text, but it opens with `fileFormatVersion`, not the
            // `%YAML` header the test below looks for - so until 2026-09-23 every .meta was
            // turned away here despite being on TextAssetExtensions, and an FBX's material
            // remaps (`externalObjects`) were never rewritten. It went unnoticed while the
            // copied models happened to remap to nothing; `Pebble_Square_3.fbx` and
            // `Prop_Wagon.fbx` were the first that pointed at library materials, and two
            // collector runs in a row left them there.
            if (path.EndsWith(".meta", System.StringComparison.OrdinalIgnoreCase))
                return true;
            using (var stream = System.IO.File.OpenRead(path))
            {
                int first = stream.ReadByte();
                return first == '%' || first == -1;
            }
        }

        /// <summary>Every text asset under the demo, `.meta` files included.</summary>
        private static IEnumerable<string> TextAssets()
        {
            foreach (string file in System.IO.Directory.GetFiles(DemoDir, "*", System.IO.SearchOption.AllDirectories))
            {
                string path = file.Replace('\\', '/');
                if (TextAssetExtensions.Contains(System.IO.Path.GetExtension(path).ToLowerInvariant()))
                    yield return path;
            }
        }

        /// <summary>
        /// Counts references inside the demo's prefabs, materials, data and scenes that
        /// point at an object which no longer exists, listing each in the report.
        /// </summary>
        private static int MissingReferences(System.Text.StringBuilder report)
        {
            int missing = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab t:Material t:ScriptableObject t:TerrainLayer t:AnimatorController", new[] { DemoDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (asset == null)
                        continue;
                    var prefab = asset as GameObject;
                    if (prefab != null)
                    {
                        foreach (Component component in prefab.GetComponentsInChildren<Component>(true))
                            missing += MissingIn(component, path, report);
                    }
                    else
                    {
                        missing += MissingIn(asset, path, report);
                    }
                }
            }

            string previous = LeaveDemoScenes();
            if (previous != null)
            {
                try
                {
                    foreach (string guid in AssetDatabase.FindAssets("t:Scene", new[] { DemoDir }))
                    {
                        string path = AssetDatabase.GUIDToAssetPath(guid);
                        var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                        foreach (GameObject root in scene.GetRootGameObjects())
                        {
                            foreach (Component component in root.GetComponentsInChildren<Component>(true))
                                missing += MissingIn(component, path, report);
                        }
                    }
                }
                finally
                {
                    if (!string.IsNullOrEmpty(previous))
                        EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
                    else
                        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                }
            }
            return missing;
        }

        private static int MissingIn(Object target, string path, System.Text.StringBuilder report)
        {
            if (target == null)
            {
                report.AppendLine($"  missing: a script on {path}");
                return 1;
            }
            int missing = 0;
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.GetIterator();
            while (property.Next(true))
            {
                if (property.propertyType != SerializedPropertyType.ObjectReference)
                    continue;
                if (property.objectReferenceValue == null && property.objectReferenceInstanceIDValue != 0)
                {
                    report.AppendLine($"  missing: {path} : {target.name} ({target.GetType().Name}) . {property.propertyPath}");
                    ++missing;
                }
            }
            return missing;
        }

        /// <summary>
        /// Moves the editor off any open demo scene so that a textual rewrite cannot be
        /// overwritten by the editor saving its in-memory copy. Returns the scene to put
        /// back (empty when none was open), or null if the open scene has unsaved changes.
        /// </summary>
        private static string LeaveDemoScenes()
        {
            var active = EditorSceneManager.GetActiveScene();
            if (!active.path.StartsWith(DemoDir))
                return string.Empty;
            if (active.isDirty)
            {
                Debug.LogError($"[{nameof(DemoArtCollector)}] \"{active.path}\" has unsaved changes; save or discard them first.");
                return null;
            }
            // Read before the scene is replaced. `Scene` is a handle, and once NewScene has
            // unloaded the scene it names, `path` on it comes back empty - which Collect
            // takes as "leave, silently". Until 2026-09-23 that is exactly what happened
            // whenever a demo scene was open: the collector unloaded it, then stopped with
            // nothing extracted, nothing rewritten and nothing logged. Every run that worked
            // had happened to start from an empty scene.
            string path = active.path;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            return path;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            int split = path.LastIndexOf('/');
            EnsureFolder(path.Substring(0, split));
            AssetDatabase.CreateFolder(path.Substring(0, split), path.Substring(split + 1));
        }
    }
}
