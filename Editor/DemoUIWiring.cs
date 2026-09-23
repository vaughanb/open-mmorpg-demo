using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// Points the demo's UI prefabs at the demo's own data.
    ///
    /// The UI under `Demo/Prefabs/UI` began as a copy of the kit's template, and a copy brings
    /// the layout but not the references: several fields that have to name *our* armour types,
    /// *our* combat text and a sprite were simply left empty. Nothing else in the demo builder
    /// owned these prefabs, so they were never re-wired after the game data was built. This is
    /// that step.
    ///
    /// Three fixes, all found 2026-09-16 from a player report of "the HP meters are not going
    /// down" and an NRE on opening the equipment panel:
    ///
    /// 1. **A `Filled` Image with no sprite ignores `fillAmount` completely.** `Image` bails to
    ///    `Graphic.OnPopulateMesh` - a plain full-rect quad - the moment `sprite` is null, so
    ///    the `type` and the fill are never consulted. Every gauge in the demo was in that
    ///    state, which is why the HP *numbers* moved while the *bar* stayed full: the number is
    ///    a separate Text component. Measured, same `fillAmount = 0.25`: 66px wide with a
    ///    sprite, the full 400px without. They get `Demo/Textures/UIGageFill.png`, a plain white
    ///    square, so the flat look is unchanged and only the clipping starts working.
    /// 2. **Every equipment slot had a null `armorType`.** The template ships Head/Body/Gloves/
    ///    Shoes/Ring/Ring; the demo's armour is Head/Body/Arms/Feet/Legs/Pauldron. Nothing
    ///    matched, so `UIEquipItems.CacheEquipItemSlots` dereferenced null on the first slot and
    ///    the panel threw every time it opened. The labels are rewritten to suit, and the second
    ///    ring's `equipSlotIndex` is reset to 0 - it was 1, for a second ring of the same type,
    ///    which would ask for a position "Pauldron_1" that no item can occupy.
    /// 3. **`uiCombatTextHpDecrease` was null**, so no damage number ever appeared, while the
    ///    healing one was wired. `NormalDamageText` was sitting in the folder unused.
    /// </summary>
    public static class DemoUIWiring
    {
        private const string UiDir = "Assets/OpenMMORPG/Demo/Prefabs/UI";
        private const string PrefabRoot = "Assets/OpenMMORPG/Demo/Prefabs";
        private const string TextureDir = "Assets/OpenMMORPG/Demo/Textures";
        private const string ArmorTypeDir = "Assets/OpenMMORPG/Demo/GameData/Resources/ArmorTypes";
        private const string GageSpritePath = TextureDir + "/UIGageFill.png";

        /// <summary>
        /// Which armour type each of the template's slots should hold, and what to call it.
        /// Keyed by the slot object's name, which is what the template left behind.
        /// </summary>
        private static readonly Dictionary<string, KeyValuePair<string, string>> SlotArmor =
            new Dictionary<string, KeyValuePair<string, string>>
        {
            { "EquipSlotHead", new KeyValuePair<string, string>("Head", "Head") },
            { "EquipSlotBody", new KeyValuePair<string, string>("Body", "Body") },
            { "EquipSlotGloves", new KeyValuePair<string, string>("Arms", "Arms") },
            { "EquipSlotShoes", new KeyValuePair<string, string>("Feet", "Feet") },
            { "EquipSlotRing-1", new KeyValuePair<string, string>("Legs", "Legs") },
            { "EquipSlotRing-2", new KeyValuePair<string, string>("Pauldron", "Pauldron") },
        };

        [MenuItem("Open MMORPG/Demo/Wire Demo UI")]
        public static void Wire()
        {
            Sprite gage = EnsureGageSprite();
            int gauges = 0, slots = 0, texts = 0, rows = 0, hidden = 0, prefabs = 0;

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    bool dirty = false;
                    dirty |= FillGauges(root, gage, ref gauges);
                    dirty |= WireEquipSlots(root, ref slots);
                    dirty |= WireCombatText(root, ref texts);
                    dirty |= PruneDeadDataRows(root, ref rows);
                    dirty |= HideDeadGameDataRows(root, ref hidden);
                    if (dirty)
                    {
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                        ++prefabs;
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[{nameof(DemoUIWiring)}] Wired {prefabs} prefabs: {gauges} gauge images given a sprite, " +
                      $"{slots} equipment slots given an armour type, {texts} combat texts assigned, " +
                      $"{rows} dead data rows dropped, {hidden} rows hidden for want of game data.");
        }

        /// <summary>
        /// A plain white square for the gauges to clip.
        ///
        /// Deliberately featureless: the demo's bars are flat colour blocks and the point of
        /// this is to make `fillAmount` work, not to restyle them. Unity's own
        /// `UI/Skin/UISprite.psd` would do the job but carries rounded corners the gauge
        /// backgrounds do not have.
        /// </summary>
        private static Sprite EnsureGageSprite()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Sprite>(GageSpritePath);
            if (existing != null)
                return existing;

            DemoItemBuilder.EnsureFolder(TextureDir);
            var texture = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            var pixels = new Color32[8 * 8];
            for (int i = 0; i < pixels.Length; ++i)
                pixels[i] = new Color32(255, 255, 255, 255);
            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(GageSpritePath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(GageSpritePath, ImportAssetOptions.ForceUpdate);

            var importer = (TextureImporter)AssetImporter.GetAtPath(GageSpritePath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(GageSpritePath);
        }

        /// <summary>
        /// Rows that point at game data the demo does not have.
        ///
        /// The template's character dialog lists four attributes, a currency, and a row per
        /// damage element for the armour and resistance panels. **The demo defines none of
        /// those** - no `Attribute`, no `Currency`, no `DamageElement` asset exists - and the
        /// references it shipped with point at the kit's own, which were deleted when the demo
        /// was trimmed. That leaves not nulls but *missing* references, and a row whose key is
        /// missing is dereferenced exactly the way the equipment slots were, so those panels
        /// throw the moment they open.
        ///
        /// Dropping the rows is the honest fix while the demo has no such data: an empty panel
        /// rather than a broken one. If attributes are ever added, this stops removing them -
        /// it only ever deletes a row whose key does not resolve.
        /// </summary>
        private static readonly KeyValuePair<string, string>[] DataRows =
        {
            new KeyValuePair<string, string>("uiCharacterAttributes", "attribute"),
            new KeyValuePair<string, string>("uiCharacterCurrencies", "currency"),
            new KeyValuePair<string, string>("textAmounts", "damageElement"),
            new KeyValuePair<string, string>("textAmounts", "currency"),
        };

        private static bool PruneDeadDataRows(GameObject root, ref int count)
        {
            bool dirty = false;
            foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null)
                    continue;
                var serialized = new SerializedObject(behaviour);
                bool wrote = false;
                foreach (KeyValuePair<string, string> row in DataRows)
                {
                    SerializedProperty array = serialized.FindProperty(row.Key);
                    if (array == null || !array.isArray)
                        continue;
                    for (int i = array.arraySize - 1; i >= 0; --i)
                    {
                        SerializedProperty key = array.GetArrayElementAtIndex(i).FindPropertyRelative(row.Value);
                        if (key == null || key.propertyType != SerializedPropertyType.ObjectReference)
                            continue;
                        if (key.objectReferenceValue != null)
                            continue;
                        array.DeleteArrayElementAtIndex(i);
                        ++count;
                        wrote = true;
                    }
                }
                if (wrote)
                {
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    dirty = true;
                }
            }
            return dirty;
        }

        /// <summary>
        /// Rows labelled from game data the demo does not define.
        ///
        /// The template's character dialog carries eight resistance rows - fire, ice, lightning
        /// and poison, armour and resistance each - and every label is a
        /// `TextSetterByGameDataTitle` pointing at one of the kit's `DamageElement` assets. Those
        /// were deleted with the rest of the kit's sample data, and **the component does not null
        /// guard its `gameData`**: its `Update` checks `textWrapper` and then dereferences
        /// `gameData.Title` regardless, so each one throws once as soon as the dialog first
        /// shows. It is a Core script, so the guard cannot be added there - see
        /// [[demo-no-core-changes]].
        ///
        /// The row is **deactivated rather than deleted**: an inactive object gets no `Update`,
        /// so the throw goes away, the layout group skips it so the panel closes up neatly, and
        /// the day the demo grows damage elements it is one toggle to bring back. Clearing the
        /// reference would not help - null throws just as hard as missing.
        /// </summary>
        private static bool HideDeadGameDataRows(GameObject root, ref int count)
        {
            bool dirty = false;
            foreach (TextSetterByGameDataTitle setter in root.GetComponentsInChildren<TextSetterByGameDataTitle>(true))
            {
                if (setter.gameData != null)
                    continue;
                // The label sits inside the row; hide the row, not just its caption.
                Transform row = setter.name == "TextLabel" && setter.transform.parent != null
                    ? setter.transform.parent
                    : setter.transform;
                if (!row.gameObject.activeSelf)
                    continue;
                row.gameObject.SetActive(false);
                EditorUtility.SetDirty(row.gameObject);
                ++count;
                dirty = true;
            }
            return dirty;
        }

        /// <summary>A `Filled` Image without a sprite renders full and ignores the fill - see the class note.</summary>
        private static bool FillGauges(GameObject root, Sprite gage, ref int count)
        {
            bool dirty = false;
            foreach (Image image in root.GetComponentsInChildren<Image>(true))
            {
                if (image.type != Image.Type.Filled || image.sprite != null)
                    continue;
                image.sprite = gage;
                EditorUtility.SetDirty(image);
                ++count;
                dirty = true;
            }
            return dirty;
        }

        private static bool WireEquipSlots(GameObject root, ref int count)
        {
            bool dirty = false;
            foreach (UIEquipItems ui in root.GetComponentsInChildren<UIEquipItems>(true))
            {
                var serialized = new SerializedObject(ui);
                SerializedProperty others = serialized.FindProperty("otherEquipSlots");
                if (others == null)
                    continue;
                bool wrote = false;
                for (int i = 0; i < others.arraySize; ++i)
                {
                    SerializedProperty entry = others.GetArrayElementAtIndex(i);
                    SerializedProperty slotUi = entry.FindPropertyRelative("ui");
                    if (slotUi == null || slotUi.objectReferenceValue == null)
                        continue;
                    string slotName = slotUi.objectReferenceValue.name;
                    KeyValuePair<string, string> wanted;
                    if (!SlotArmor.TryGetValue(slotName, out wanted))
                    {
                        Debug.LogWarning($"[{nameof(DemoUIWiring)}] An equipment slot named \"{slotName}\" is not " +
                                         "in the table, so it keeps whatever it had - and a null one throws.");
                        continue;
                    }
                    var armorType = AssetDatabase.LoadAssetAtPath<ArmorType>($"{ArmorTypeDir}/{wanted.Key}.asset");
                    if (armorType == null)
                    {
                        Debug.LogError($"[{nameof(DemoUIWiring)}] No armour type \"{wanted.Key}\".");
                        continue;
                    }
                    entry.FindPropertyRelative("armorType").objectReferenceValue = armorType;
                    // One slot per type: the template's "second ring" index would ask for a
                    // position no item can be equipped to.
                    entry.FindPropertyRelative("equipSlotIndex").intValue = 0;
                    ++count;
                    wrote = true;

                    Relabel(slotUi.objectReferenceValue as Component, wanted.Value);
                }
                if (wrote)
                {
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    dirty = true;
                }
            }
            return dirty;
        }

        /// <summary>The slot still says "Gloves" over what is now the arms slot.</summary>
        private static void Relabel(Component slot, string label)
        {
            if (slot == null)
                return;
            foreach (Text text in slot.GetComponentsInChildren<Text>(true))
            {
                if (text.name != "TextLabel")
                    continue;
                text.text = label;
                EditorUtility.SetDirty(text);
            }
        }

        private static bool WireCombatText(GameObject root, ref int count)
        {
            bool dirty = false;
            foreach (UISceneGameplay scene in root.GetComponentsInChildren<UISceneGameplay>(true))
            {
                var serialized = new SerializedObject(scene);
                SerializedProperty decrease = serialized.FindProperty("uiCombatTextHpDecrease");
                if (decrease == null || decrease.objectReferenceValue != null)
                    continue;
                var damageText = AssetDatabase.LoadAssetAtPath<GameObject>(
                    $"{UiDir}/GamePlay/CombatText/NormalDamageText.prefab");
                if (damageText == null)
                {
                    Debug.LogError($"[{nameof(DemoUIWiring)}] NormalDamageText.prefab is missing.");
                    continue;
                }
                // The field takes the component, not the GameObject; the healing one beside it
                // is wired that way already.
                var combatText = damageText.GetComponent<UICombatText>();
                if (combatText == null)
                {
                    Debug.LogError($"[{nameof(DemoUIWiring)}] NormalDamageText has no UICombatText.");
                    continue;
                }
                decrease.objectReferenceValue = combatText;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                ++count;
                dirty = true;
            }
            return dirty;
        }
    }
}
