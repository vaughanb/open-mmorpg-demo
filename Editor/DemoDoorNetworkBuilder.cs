using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// Gives every scenery door in a scene its network half, so every player sees the same
    /// door open and shut: a child object under the doorway, <see cref="SyncChildName"/>, with a
    /// <see cref="LiteNetLibManager.LiteNetLibIdentity"/> and a <see cref="SceneryDoorSync"/>.
    ///
    /// **A child, not the doorway**, because LiteNetLib switches a scene object off on a client
    /// until it is spawned to them and off again out of range - the doorway would take its leaf
    /// with it. See <see cref="SceneryDoorSync"/>.
    ///
    /// **Its own scene object id**, `Door@&lt;doorway path&gt;`, as the chests are given
    /// `Chest@` ones (DemoTreasureBuilder): a scene object is spawned by the hash of that id,
    /// so it must be unique in the scene and the same in the server's build and the client's.
    /// Suffixed `#2`... only where two doorways share a path.
    ///
    /// Idempotent: a door that already has its network half keeps it, and only the id is
    /// checked. Called at the end of each scene regenerate (the village is a frozen area, so its
    /// doors are rebuilt only by Regenerate Settled Areas) and from the menu. **After it, run
    /// Build Map Server**: new scene objects the running server does not know make every scene
    /// object after them fail to spawn on a client.
    /// </summary>
    public static class DemoDoorNetworkBuilder
    {
        /// <summary>The name of the doorway's child that carries the network half.</summary>
        public const string SyncChildName = "DoorSync";

        private const string IdPrefix = "Door@";

        private static readonly string[] ScenePaths =
        {
            "Assets/OpenMMORPG/Demo/Scenes/DemoMap.unity",
        };

        [MenuItem("Open MMORPG/Demo/Network Village Doors (writes DemoMap)", priority = 160)]
        public static void Build()
        {
            int added = 0;
            foreach (string scenePath in ScenePaths)
                added += NetworkSceneFile(scenePath);
            Debug.Log($"[{nameof(DemoDoorNetworkBuilder)}] Gave {added} door(s) a network half. Every door is a " +
                      "scene object the map server has to know, so run Build Map Server.");
        }

        /// <summary>
        /// Gives every <see cref="SceneryDoor"/> in the scene its network half and its own scene
        /// object id. Returns how many doors were given one that had none.
        /// </summary>
        public static int NetworkDoors(Scene scene)
        {
            var taken = new HashSet<string>();
            var doors = new List<SceneryDoor>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (SceneryDoor door in root.GetComponentsInChildren<SceneryDoor>(true))
                    doors.Add(door);
                foreach (var identity in root.GetComponentsInChildren<LiteNetLibManager.LiteNetLibIdentity>(true))
                {
                    if (identity.GetComponent<SceneryDoorSync>() == null && !string.IsNullOrEmpty(identity.SceneObjectId))
                        taken.Add(identity.SceneObjectId);
                }
            }
            // Hierarchy order, so the suffix a duplicate path gets is the same every run.
            doors.Sort((a, b) => string.CompareOrdinal(HierarchyPath(a.transform), HierarchyPath(b.transform)));

            int added = 0;
            foreach (SceneryDoor door in doors)
            {
                Transform child = door.transform.Find(SyncChildName);
                if (child == null)
                {
                    var go = new GameObject(SyncChildName);
                    child = go.transform;
                    child.SetParent(door.transform, false);
                    ++added;
                }
                if (child.GetComponent<LiteNetLibManager.LiteNetLibIdentity>() == null)
                    child.gameObject.AddComponent<LiteNetLibManager.LiteNetLibIdentity>();
                if (child.GetComponent<SceneryDoorSync>() == null)
                    child.gameObject.AddComponent<SceneryDoorSync>();

                string id = IdPrefix + HierarchyPath(door.transform);
                string candidate = id;
                for (int n = 2; taken.Contains(candidate); ++n)
                    candidate = $"{id}#{n}";
                taken.Add(candidate);

                var identity = child.GetComponent<LiteNetLibManager.LiteNetLibIdentity>();
                if (identity.SceneObjectId != candidate)
                {
                    var serialized = new SerializedObject(identity);
                    serialized.FindProperty("sceneObjectId").stringValue = candidate;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                EditorUtility.SetDirty(child.gameObject);
            }
            return added;
        }

        private static int NetworkSceneFile(string scenePath)
        {
            Scene scene = default;
            bool wasOpen = false;
            for (int i = 0; i < SceneManager.sceneCount; ++i)
            {
                Scene loaded = SceneManager.GetSceneAt(i);
                if (loaded.isLoaded && loaded.path == scenePath)
                {
                    scene = loaded;
                    wasOpen = true;
                }
            }
            if (!wasOpen)
            {
                if (!System.IO.File.Exists(scenePath))
                {
                    Debug.LogWarning($"[{nameof(DemoDoorNetworkBuilder)}] No scene at \"{scenePath}\"; no doors to network there.");
                    return 0;
                }
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            }

            int added = NetworkDoors(scene);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            if (!wasOpen)
                EditorSceneManager.CloseScene(scene, true);
            return added;
        }

        private static string HierarchyPath(Transform t)
        {
            string path = t.name;
            for (Transform up = t.parent; up != null; up = up.parent)
                path = up.name + "/" + path;
            return path;
        }
    }
}
