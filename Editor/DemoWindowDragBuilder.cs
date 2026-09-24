using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// Makes every in-game window movable by its title bar (<see cref="DemoWindowDrag"/>).
    ///
    /// Every dialog the demo's UI inherited from the kit's template has the same bones - a
    /// `Window` panel with a `Title` bar anchored across its top - so the title is found by that
    /// shape rather than by a list of 30-odd dialog names that would go stale.
    ///
    /// **Each title gets the component in the prefab that owns it**, not in the canvases that nest
    /// it. `CanvasGameplay` holds most windows as nested instances of their own dialog prefabs
    /// (`Item/UIItemsDialog`, `Npc/UINpcDialog`...), so a component added there would be an
    /// override on each instance - invisible in the dialog prefab, and lost the day the canvas is
    /// rebuilt. Added to the dialog prefab, it reaches every canvas that nests it.
    ///
    /// The home screens are left alone: their panels are placed by `DemoMenuStageBuilder` against
    /// the title band, and a login form is not something to rearrange. `Global` is included - its
    /// message and input boxes turn up in game too.
    /// </summary>
    public static class DemoWindowDragBuilder
    {
        private const string UiDir = "Assets/OpenMMORPG/Demo/Prefabs/UI";
        private const string HomeDir = UiDir + "/Home";

        [MenuItem("Open MMORPG/Demo/Make Windows Draggable")]
        public static void Build()
        {
            int added = 0, prefabs = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { UiDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.StartsWith(HomeDir + "/"))
                    continue;
                int here = AddToOwnTitles(path);
                if (here > 0)
                {
                    added += here;
                    ++prefabs;
                }
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[{nameof(DemoWindowDragBuilder)}] {added} title bar(s) in {prefabs} prefab(s) made draggable.");
        }

        /// <summary>Adds the drag to the titles this prefab owns, skipping those it only nests.</summary>
        private static int AddToOwnTitles(string path)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            int added = 0;
            try
            {
                foreach (Transform title in Titles(root.transform))
                {
                    if (title.GetComponent<DemoWindowDrag>() != null)
                        continue;
                    if (OwnedElsewhere(title.gameObject, path))
                        continue;
                    var drag = title.gameObject.AddComponent<DemoWindowDrag>();
                    drag.window = (RectTransform)title.parent;
                    ++added;
                }
                if (added > 0)
                    PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            return added;
        }

        /// <summary>
        /// The template's title bar: a `Title` across the top of its `Window` - anchored there, or,
        /// in the small popups (trade and party requests, the player menu, System), stacked first
        /// by the window's vertical layout. A laid-out child keeps the anchors it was saved with
        /// (0,0) in its own prefab and only reads as top-anchored where a canvas has overridden
        /// them, so testing the anchors alone found those six only in the canvas, where they are
        /// nested and not this builder's to change.
        /// </summary>
        private static IEnumerable<Transform> Titles(Transform root)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != "Title" || t.parent == null || t.parent.name != "Window")
                    continue;
                if (!(t is RectTransform rect))
                    continue;
                bool anchoredTop = rect.anchorMin.y >= 0.99f;
                bool stackedFirst = t.parent.GetComponent<UnityEngine.UI.VerticalLayoutGroup>() != null &&
                                    t.GetSiblingIndex() == 0;
                if (anchoredTop || stackedFirst)
                    yield return t;
            }
        }

        /// <summary>
        /// Whether this title belongs to another of the demo's UI prefabs nested in this one, which
        /// is where it gets the component instead.
        /// </summary>
        private static bool OwnedElsewhere(GameObject title, string path)
        {
            if (!PrefabUtility.IsPartOfPrefabInstance(title))
                return false;
            Object source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(title);
            string sourcePath = source == null ? null : AssetDatabase.GetAssetPath(source);
            return !string.IsNullOrEmpty(sourcePath) && sourcePath != path &&
                   sourcePath.StartsWith(UiDir + "/") && !sourcePath.StartsWith(HomeDir + "/");
        }
    }
}
