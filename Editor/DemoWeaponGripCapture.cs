using UnityEditor;
using UnityEngine;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// Saves every grip tuned in the open scene into the weapon game data at once, so a
    /// session spent nudging several weapons does not have to be committed one component at
    /// a time.
    ///
    /// Each <see cref="DemoEquipPreview"/> writes its own item, reading the live preview
    /// object so a scene-view drag is what gets captured. See that component for where the
    /// values land and why they go to two places.
    /// </summary>
    public static class DemoWeaponGripCapture
    {
        [MenuItem("Open MMORPG/Demo/Save Weapon Grips From Scene", priority = 120)]
        public static void SaveAll()
        {
            DemoEquipPreview[] previews = Object.FindObjectsByType<DemoEquipPreview>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            int saved = 0;
            foreach (DemoEquipPreview preview in previews)
            {
                if (preview == null || preview.item == null)
                    continue;
                preview.SaveGripToGameData();
                ++saved;
            }

            if (saved == 0)
            {
                Debug.LogWarning($"[{nameof(DemoWeaponGripCapture)}] No {nameof(DemoEquipPreview)} with an item " +
                                 "in the open scene, so nothing was saved. Open AnimationEditing.unity.");
                return;
            }
            Debug.Log($"[{nameof(DemoWeaponGripCapture)}] Saved {saved} grip(s) from " +
                      $"\"{UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}\" into the weapon game data. " +
                      $"They are held in {DemoWeaponGripOverrides.AssetPath} and survive Build Items.");
        }
    }
}
