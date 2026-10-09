using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// Puts the in-game windows on World of Warcraft's keys: C character, B bag, P spellbook,
    /// L quest log, O friends, J guild. The template's were its own (M for the character, I for
    /// the items, B for the quest log...), and the windows now look like WoW's, so a WoW player's
    /// hands should find them too.
    ///
    /// **The keys are on `UISceneGameplay.toggleUis` in `CanvasGameplay`,** a raw `KeyCode` per
    /// window - not in `GameInstance`'s rebindable key settings, which hold the gameplay keys
    /// (<see cref="DemoControllerBuilder"/>). Matched by the window's object name, so the order
    /// of the list does not matter and a window that is not in the table keeps its key.
    ///
    /// **The party window has no key.** WoW has no party window - the raid tab of the social
    /// frame is the nearest thing, and O is the friends list here - so it is reached from the menu
    /// bar only, rather than squatting on a key a WoW player would expect to do something else.
    ///
    /// **J was taken:** the kit ships building placement's rotate-left/right on J and K. Those
    /// move to [ and ] (see <see cref="DemoControllerBuilder.ConfigureKeys"/>), or placing a
    /// campfire and turning it would open the guild window with every press.
    ///
    /// **Escape is not on the list.** The game menu's entry is left keyless and Escape goes to
    /// <see cref="DemoEscapeKey"/> instead, which cancels a skill being aimed and only otherwise
    /// opens the menu (2026-09-24). On the kit's list every press toggled the menu, so Escape
    /// could never take back an aim without the menu opening over it.
    /// </summary>
    public static class DemoWindowKeysBuilder
    {
        private const string CanvasPath = "Assets/OpenMMORPG/Demo/Prefabs/UI/CanvasGameplay.prefab";

        /// <summary>The game menu, which Escape opens through <see cref="DemoEscapeKey"/>.</summary>
        private const string MenuName = "UISystemDialog";

        private static readonly Dictionary<string, KeyCode> WindowKeys = new Dictionary<string, KeyCode>
        {
            { "UICharacterDialog", KeyCode.C },
            { "UIItemsDialog", KeyCode.B },
            { "UISkillsDialog", KeyCode.P },
            { "UIQuestDialog", KeyCode.L },
            { "UIFriendDialog", KeyCode.O },
            { "UIGuildDialog", KeyCode.J },
            { "UIPartyDialog", KeyCode.None },
            // Escape, but through DemoEscapeKey - see BindEscape.
            { MenuName, KeyCode.None },
        };

        [MenuItem("Open MMORPG/Demo/Bind WoW Window Keys")]
        public static void Bind()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(CanvasPath);
            var bound = new List<string>();
            try
            {
                var scene = root.GetComponent<UISceneGameplay>();
                var serialized = new SerializedObject(scene);
                SerializedProperty toggles = serialized.FindProperty("toggleUis");
                UIBase menu = null;
                for (int i = 0; i < toggles.arraySize; ++i)
                {
                    SerializedProperty entry = toggles.GetArrayElementAtIndex(i);
                    Object ui = entry.FindPropertyRelative("ui").objectReferenceValue;
                    if (ui == null || !WindowKeys.TryGetValue(ui.name, out KeyCode key))
                        continue;
                    entry.FindPropertyRelative("keyCode").intValue = (int)key;
                    if (ui.name == MenuName)
                        menu = ui as UIBase;
                    bound.Add($"{ui.name} {(key == KeyCode.None ? "(menu only)" : key.ToString())}");
                }
                serialized.ApplyModifiedPropertiesWithoutUndo();
                BindEscape(root, menu);
                PrefabUtility.SaveAsPrefabAsset(root, CanvasPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            // The gameplay keys, so building rotation gets off J and K.
            DemoControllerBuilder.ConfigureKeys();
            Debug.Log($"[{nameof(DemoWindowKeysBuilder)}] Window keys: {string.Join(", ", bound)}. " +
                      "Escape cancels an aim, else opens the game menu. Building rotation moved to [ and ].");
        }

        /// <summary>
        /// Gives Escape to <see cref="DemoEscapeKey"/> on the canvas root, pointed at the game menu
        /// the kit's list would otherwise have toggled.
        /// </summary>
        private static void BindEscape(GameObject root, UIBase menu)
        {
            if (menu == null)
                Debug.LogWarning($"[{nameof(DemoWindowKeysBuilder)}] No {MenuName} on the window list; " +
                                 "Escape will cancel an aim but open no menu.");
            var escape = root.GetComponent<DemoEscapeKey>();
            if (escape == null)
                escape = root.AddComponent<DemoEscapeKey>();
            escape.menu = menu;
            escape.key = KeyCode.Escape;
        }
    }
}
