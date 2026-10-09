using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.UI;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// Turns the kit's character dialog into a WoW-style character sheet: the equipment slots down
    /// both sides of a turnable 3D preview of the character (<see cref="UICharacterPaperDoll"/>),
    /// the weapons under it, and the attribute/stat/resistance tabs underneath. The bag keeps its
    /// own window, now only the bag.
    ///
    /// **The slots are copied out of the Items dialog, not rebuilt.** The template's equipment
    /// block is two halves that point at each other - `--EquipItems` (the `UIEquipItems` logic,
    /// which lists every slot) and `Window/Info/EquipItems` (the slots, weapon-set tabs and the
    /// manager that switches weapon sets) - so the whole dialog is cloned, which keeps every
    /// reference between those halves, and just those two are lifted into the character dialog.
    /// The originals stay in the Items dialog **switched off**, never deleted: the kit holds
    /// serialized references into them, and "Regenerate" copies them again from there.
    ///
    /// **Slot names are kept** (`EquipSlotGloves` holds Arms, `EquipSlotRing-1` holds Legs...),
    /// because `DemoUIWiring` maps armour types by slot name and still has to find them.
    ///
    /// **Three prefabs, three jobs.** The layout lives in the two dialog prefabs; the references
    /// that cross between dialogs - the item-info popup the slots open, and the pair of
    /// "selecting one deselects the other" links between the equipment and the bag - can only be
    /// made in `UIDialogs_Standalone`, which is the first prefab holding both. `CanvasGameplay`
    /// nests that, and inherits.
    ///
    /// `Build` lays the sheet out only when it is not there yet and otherwise just re-wires, so a
    /// hand-nudged layout survives it. `Regenerate` throws the sheet away and lays it out again.
    /// </summary>
    public static class DemoCharacterSheetBuilder
    {
        private const string UiDir = "Assets/OpenMMORPG/Demo/Prefabs/UI";
        private const string CharacterDialogPath = UiDir + "/Player/UICharacterDialog.prefab";
        private const string ItemsDialogPath = UiDir + "/Item/UIItemsDialog.prefab";
        private const string DialogsPath = UiDir + "/GamePlay/Standalone/UIDialogs_Standalone.prefab";

        private const string DollName = "Doll";
        private const string EquipLogicName = "--EquipItems";
        private const string LabelName = "TextLabel";
        private const string RenderName = "Render";

        // The character dialog's window and the regions inside its `Info` block (which the template
        // insets 6 from each side, 35 from the top and 5 from the bottom). All in canvas units,
        // measured down from the top of `Info`.
        private const float WindowWidth = 340f;
        private const float WindowHeight = 548f;
        private const float InfoWidth = WindowWidth - 12f;
        private const float SubtitleHeight = 20f;
        private const float DollTop = 24f;
        private const float DollHeight = 262f;
        private const float SlotSize = 40f;
        private const float SlotInset = 4f;
        private const float WeaponRowTop = DollTop + DollHeight + 6f;
        private const float WeaponRowHeight = 44f;
        private const float TabsTop = WeaponRowTop + WeaponRowHeight + 6f;
        private const float TabsHeight = 25f;
        private const float PanelTop = TabsTop + TabsHeight + 5f;
        private const float ColumnGap = 6f;

        /// <summary>Armour slots by column, top to bottom - WoW's order: head, shoulders, chest; hands, legs, feet.</summary>
        private static readonly string[] LeftSlots = { "EquipSlotHead", "EquipSlotRing-2", "EquipSlotBody" };
        private static readonly string[] RightSlots = { "EquipSlotGloves", "EquipSlotRing-1", "EquipSlotShoes" };

        [MenuItem("Open MMORPG/Demo/Build Character Sheet")]
        public static void Build()
        {
            Run(false);
        }

        [MenuItem("Open MMORPG/Demo/Regenerate Character Sheet (destroys hand edits)")]
        public static void Regenerate()
        {
            Run(true);
        }

        private static void Run(bool regenerate)
        {
            bool laidOut = BuildCharacterDialog(regenerate);
            TrimItemsDialog();
            int links = WireAcrossDialogs();
            AssetDatabase.SaveAssets();
            Debug.Log($"[{nameof(DemoCharacterSheetBuilder)}] Character sheet " +
                      (laidOut ? "laid out" : "kept as it stands (Regenerate lays it out again)") +
                      $"; bag window trimmed to the bag; {links} cross-dialog link(s) wired in UIDialogs_Standalone.");
        }

        // ------------------------------------------------------------------ character dialog

        private static bool BuildCharacterDialog(bool regenerate)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(CharacterDialogPath);
            bool laidOut = false;
            try
            {
                Transform window = root.transform.Find("Window");
                Transform info = window.Find("Info");
                Transform doll = info.Find(DollName);
                Transform logic = root.transform.Find(EquipLogicName);

                if (regenerate || doll == null || logic == null)
                {
                    if (doll != null)
                        Object.DestroyImmediate(doll.gameObject);
                    if (logic != null)
                        Object.DestroyImmediate(logic.gameObject);
                    CopyEquipment(root.transform, info);
                    LayOut(root, info);
                    laidOut = true;
                }
                WirePreview(info.Find(DollName + "/Preview/" + RenderName));
                WireHeader(root, window, info);
                WireAttributes(root, info);
                // The resistance panel's two-column layout came after the sheet itself, so a
                // sheet still in the template's single column gets it now; one already in two
                // is left as it stands.
                var resistances = (RectTransform)info.Find("Resistances");
                if (resistances.GetComponent<GridLayoutGroup>() == null)
                    LayOutResistances(resistances);
                WireResistances(root, resistances);
                if (root.GetComponent<UICharacterStatsReadable>() == null)
                    root.AddComponent<UICharacterStatsReadable>();
                TidySlotNames(info.Find(DollName));
                PrefabUtility.SaveAsPrefabAsset(root, CharacterDialogPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            return laidOut;
        }

        /// <summary>
        /// Each equipment slot prints its name ("Arms", "R-Hand") under its icon, which the
        /// template's opaque square icons covered; the demo's icons are cut-out renders, so the
        /// name showed through the item - an arm piece's thin icon read as a stick labelled
        /// "Arms". Every icon beside a <see cref="LabelName"/> gets a <see cref="HideWhileActive"/>
        /// that hides that name while the icon is up, i.e. while the slot is filled (the kit
        /// switches the icon off when the slot empties). The slot and its draggable icon each
        /// have a pair.
        ///
        /// The names are also best-fitted, from their own size down: "Pauldron" ran past both
        /// edges of its slot. Idempotent.
        /// </summary>
        private static void TidySlotNames(Transform doll)
        {
            if (doll == null)
                return;
            foreach (UICharacterItem slot in doll.GetComponentsInChildren<UICharacterItem>(true))
            {
                Transform label = slot.transform.Find(LabelName);
                Image icon = slot.imageIcon;
                if (label == null || icon == null || icon.transform.parent != slot.transform)
                    continue;
                var text = label.GetComponent<Text>();
                if (text == null)
                    continue;
                HideWhileActive hider = icon.GetComponent<HideWhileActive>();
                if (hider == null)
                    hider = icon.gameObject.AddComponent<HideWhileActive>();
                hider.hide = new Behaviour[] { text };
                EditorUtility.SetDirty(hider);

                if (!text.resizeTextForBestFit)
                {
                    text.resizeTextMaxSize = text.fontSize;
                    text.resizeTextMinSize = Mathf.Min(8, text.fontSize);
                    text.resizeTextForBestFit = true;
                }
                // Best fit has nothing to shrink against while the text may run out sideways.
                text.horizontalOverflow = HorizontalWrapMode.Wrap;
                EditorUtility.SetDirty(text);
            }
        }

        /// <summary>Clones the Items dialog and lifts its equipment block into the character dialog.</summary>
        private static void CopyEquipment(Transform characterRoot, Transform info)
        {
            GameObject items = PrefabUtility.LoadPrefabContents(ItemsDialogPath);
            try
            {
                GameObject clone = Object.Instantiate(items);
                try
                {
                    Transform logic = clone.transform.Find(EquipLogicName);
                    Transform panel = clone.transform.Find("Window/Info/EquipItems");
                    logic.SetParent(characterRoot, false);
                    logic.name = EquipLogicName;
                    logic.gameObject.SetActive(true);
                    logic.SetSiblingIndex(0);
                    panel.SetParent(info, false);
                    panel.name = DollName;
                    panel.gameObject.SetActive(true);

                    // Its "deselect the bag" link pointed at the clone's bag, which is about to go.
                    // The real one is made in UIDialogs_Standalone, where both dialogs exist.
                    var selection = logic.GetComponent<UICharacterItemSelectionManager>();
                    for (int i = selection.eventOnSelect.GetPersistentEventCount() - 1; i >= 0; --i)
                        UnityEventTools.RemovePersistentListener(selection.eventOnSelect, i);
                    // The item popup lives beside the dialogs, not in them; also wired there.
                    logic.GetComponent<UIEquipItems>().uiDialog = null;
                }
                finally
                {
                    Object.DestroyImmediate(clone);
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(items);
            }
        }

        private static void LayOut(GameObject root, Transform info)
        {
            var rootRect = (RectTransform)root.transform;
            rootRect.sizeDelta = new Vector2(WindowWidth, WindowHeight);

            // --- The doll: slots down both sides, the preview between, weapons underneath.
            var doll = (RectTransform)info.Find(DollName);
            SetTopRect(doll, 0f, DollTop, InfoWidth, DollHeight + 6f + WeaponRowHeight);
            // The template's grey well behind the slots would put a box around the whole figure.
            var well = doll.GetComponent<Image>();
            if (well != null)
                well.color = new Color(well.color.r, well.color.g, well.color.b, 0f);

            for (int i = 0; i < LeftSlots.Length; ++i)
            {
                float y = DollHeight * (2 * i + 1) / (2f * LeftSlots.Length);
                PlaceCentred(doll, LeftSlots[i], SlotInset + SlotSize * 0.5f, y);
                PlaceCentred(doll, RightSlots[i], InfoWidth - SlotInset - SlotSize * 0.5f, y);
            }

            float weaponY = DollHeight + 6f + WeaponRowHeight * 0.5f;
            float centre = InfoWidth * 0.5f;
            PlaceCentred(doll, "EquipSlotRightHand-1", centre - SlotSize * 0.5f - 3f, weaponY);
            PlaceCentred(doll, "EquipSlotRightHand-2", centre - SlotSize * 0.5f - 3f, weaponY);
            PlaceCentred(doll, "EquipSlotLeftHand-1", centre + SlotSize * 0.5f + 3f, weaponY);
            PlaceCentred(doll, "EquipSlotLeftHand-2", centre + SlotSize * 0.5f + 3f, weaponY);
            // One pair of weapon-set tabs is enough; both drive the same set. The second is only
            // switched off, so its manager keeps its references.
            PlaceCentred(doll, "SwitchWeaponSetTabs-1", centre - SlotSize - 34f, weaponY);
            Transform secondTabs = doll.Find("SwitchWeaponSetTabs-2");
            if (secondTabs != null)
                secondTabs.gameObject.SetActive(false);

            BuildPreview(doll);

            // --- The header: the old level and experience boxes give way to one centred line.
            SetActive(info, "LevelBG", false);
            SetActive(info, "ExpBG", false);

            // --- Tabs and their three panels underneath.
            var tabs = (RectTransform)info.Find("Tabs");
            tabs.anchorMin = new Vector2(0f, 1f);
            tabs.anchorMax = new Vector2(1f, 1f);
            tabs.pivot = new Vector2(0.5f, 1f);
            tabs.anchoredPosition = new Vector2(0f, -TabsTop);
            tabs.sizeDelta = new Vector2(0f, TabsHeight);

            foreach (string panelName in new[] { "Attributes", "Stats", "Resistances" })
            {
                var panel = (RectTransform)info.Find(panelName);
                panel.anchorMin = Vector2.zero;
                panel.anchorMax = Vector2.one;
                panel.pivot = new Vector2(0.5f, 0.5f);
                panel.offsetMin = Vector2.zero;
                panel.offsetMax = new Vector2(0f, -PanelTop);
            }
            LayOutAttributes((RectTransform)info.Find("Attributes"));
            LayOutStats((RectTransform)info.Find("Stats"));
            LayOutResistances((RectTransform)info.Find("Resistances"));
        }

        /// <summary>The four attribute cards two by two, the stat and battle points under them.</summary>
        private static void LayOutAttributes(RectTransform panel)
        {
            float width = (InfoWidth - ColumnGap) * 0.5f;
            const float height = 48f;
            int index = 0;
            foreach (Transform child in panel)
            {
                if (child.GetComponent<UICharacterAttribute>() == null)
                    continue;
                var rect = (RectTransform)child;
                rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.sizeDelta = new Vector2(width, height);
                rect.anchoredPosition = new Vector2((index % 2) * (width + ColumnGap), -(index / 2) * (height + 4f));
                ++index;
            }
        }

        /// <summary>Nine one-line stats, two columns: a vertical list of them would not fit under the doll.</summary>
        private static void LayOutStats(RectTransform panel)
        {
            var vertical = panel.GetComponent<VerticalLayoutGroup>();
            if (vertical != null)
                Object.DestroyImmediate(vertical);
            var grid = panel.GetComponent<GridLayoutGroup>();
            if (grid == null)
                grid = panel.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2((InfoWidth - ColumnGap) * 0.5f, 22f);
            grid.spacing = new Vector2(ColumnGap, 3f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 2;
            grid.startAxis = GridLayoutGroup.Axis.Horizontal;
            grid.childAlignment = TextAnchor.UpperLeft;
            grid.padding = new RectOffset(0, 0, 0, 0);

            // The template's 14pt labels were cut off at the foot even in its own 17.8-unit rows,
            // and Damage prints one line per hand, which no single row holds. Best fit shrinks just
            // the texts that need it.
            foreach (Text text in panel.GetComponentsInChildren<Text>(true))
            {
                text.resizeTextForBestFit = true;
                text.resizeTextMinSize = 8;
                text.resizeTextMaxSize = 13;
                text.verticalOverflow = VerticalWrapMode.Truncate;
                text.lineSpacing = 0.9f;
            }
        }

        /// <summary>
        /// Armour beside resistance, one row per damage element, in the same two-column grid as the
        /// stats: a header row ("Armor" | "Resistance"), then Physical, Fire and Frost. Down one
        /// column the eight rows would not fit under the doll. The template's Lightning and Poison
        /// rows go last and stay off - the demo has no such elements - and a grid skips them.
        /// </summary>
        private static void LayOutResistances(RectTransform panel)
        {
            var vertical = panel.GetComponent<VerticalLayoutGroup>();
            if (vertical != null)
                Object.DestroyImmediate(vertical);
            var grid = panel.GetComponent<GridLayoutGroup>();
            if (grid == null)
                grid = panel.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2((InfoWidth - ColumnGap) * 0.5f, 22f);
            grid.spacing = new Vector2(ColumnGap, 3f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 2;
            grid.startAxis = GridLayoutGroup.Axis.Horizontal;
            grid.childAlignment = TextAnchor.UpperLeft;
            grid.padding = new RectOffset(0, 0, 0, 0);

            Transform armorTitle = null;
            Transform resistanceTitle = null;
            foreach (Transform child in panel)
            {
                if (child.name != "Title")
                    continue;
                Text label = child.GetComponentInChildren<Text>(true);
                if (label != null && label.text.StartsWith("Armo"))
                    armorTitle = child;
                else
                    resistanceTitle = child;
            }
            var order = new List<Transform> { armorTitle, resistanceTitle };
            foreach (string element in new[] { "Physic", "Fire", "Ice", "Lightning", "Poison" })
            {
                order.Add(panel.Find(element + "ArmorBG"));
                order.Add(panel.Find(element + "ResBG"));
            }
            int index = 0;
            foreach (Transform row in order)
            {
                if (row != null)
                    row.SetSiblingIndex(index++);
            }

            // As the stats panel: best fit shrinks only the texts that need it.
            foreach (Text text in panel.GetComponentsInChildren<Text>(true))
            {
                text.resizeTextForBestFit = true;
                text.resizeTextMinSize = 8;
                text.resizeTextMaxSize = 13;
                text.verticalOverflow = VerticalWrapMode.Truncate;
            }
        }

        private const string DamageElementDir = "Assets/OpenMMORPG/Demo/GameData/Resources/DamageElements";

        /// <summary>
        /// The demo's damage elements and the template rows that show them. The template has rows
        /// for Physical, Fire, Ice, Lightning and Poison; the demo's elements are Physical, Fire and
        /// Frost, and Frost takes the Ice row - its label is set from the element's own title.
        /// </summary>
        private static readonly KeyValuePair<string, string>[] ElementRows =
        {
            new KeyValuePair<string, string>("Physical", "Physic"),
            new KeyValuePair<string, string>("Fire", "Fire"),
            new KeyValuePair<string, string>("Frost", "Ice"),
        };

        /// <summary>
        /// Binds the armour and resistance rows to the demo's damage elements, so the Resistance tab
        /// shows what the character's gear and buffs actually give.
        ///
        /// **They showed a fixed 0.** The template's rows were bound to the kit's own sample
        /// elements; `DemoUIWiring` dropped those bindings when that sample data was deleted - the
        /// demo had no elements then - and hid every row but Physical, whose value text was left as
        /// the template's placeholder with nothing writing to it. The demo has had Physical, Fire
        /// and Frost since 2026-09-22 (DemoCombatDataBuilder); nothing re-bound them.
        ///
        /// Each row's value text takes just the number ("{1}" for armour, "{1}%" for resistance):
        /// the row has its own label, and the kit's default format repeats the element's name.
        /// </summary>
        private static void WireResistances(GameObject root, Transform panel)
        {
            var armors = root.GetComponentInChildren<UIArmorAmounts>(true);
            var resistances = root.GetComponentInChildren<UIResistanceAmounts>(true);
            if (armors == null || resistances == null)
            {
                Debug.LogWarning($"[{nameof(DemoCharacterSheetBuilder)}] The character dialog has no armour or resistance list to bind.");
                return;
            }

            var armorSerialized = new SerializedObject(armors);
            var resistanceSerialized = new SerializedObject(resistances);
            SerializedProperty armorRows = armorSerialized.FindProperty("textAmounts");
            SerializedProperty resistanceRows = resistanceSerialized.FindProperty("textAmounts");
            armorRows.arraySize = 0;
            resistanceRows.arraySize = 0;

            foreach (KeyValuePair<string, string> pair in ElementRows)
            {
                var element = AssetDatabase.LoadAssetAtPath<DamageElement>($"{DamageElementDir}/{pair.Key}.asset");
                if (element == null)
                {
                    Debug.LogWarning($"[{nameof(DemoCharacterSheetBuilder)}] No damage element at {DamageElementDir}/{pair.Key}.asset; its rows stay hidden.");
                    continue;
                }
                BindRow(armorRows, panel.Find(pair.Value + "ArmorBG"), element);
                BindRow(resistanceRows, panel.Find(pair.Value + "ResBG"), element);
            }

            SetFormat(armorSerialized.FindProperty("formatKeySimpleAmount"), "{1}");
            armorSerialized.FindProperty("displayType").enumValueIndex = (int)UIArmorAmounts.DisplayType.Simple;
            SetFormat(resistanceSerialized.FindProperty("formatKeyAmount"), "{1}%");
            resistanceSerialized.FindProperty("numberFormatRate").stringValue = "0.#";
            armorSerialized.ApplyModifiedPropertiesWithoutUndo();
            resistanceSerialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>One row: the element, its value text, and the row it lives in, switched on.</summary>
        private static void BindRow(SerializedProperty rows, Transform row, DamageElement element)
        {
            if (row == null)
                return;
            row.gameObject.SetActive(true);
            TextWrapper value = null;
            foreach (TextWrapper text in row.GetComponentsInChildren<TextWrapper>(true))
            {
                if (text.name == "TextLabel")
                {
                    // The template's labels name the kit's sample elements; a setter there would
                    // throw on a missing one, so it is pointed at ours.
                    var setter = text.GetComponent<TextSetterByGameDataTitle>();
                    if (setter != null)
                        setter.gameData = element;
                    else
                        text.GetComponent<Text>().text = element.Title;
                    continue;
                }
                value = text;
            }
            if (value == null)
                return;
            int index = rows.arraySize;
            rows.arraySize = index + 1;
            SerializedProperty entry = rows.GetArrayElementAtIndex(index);
            entry.FindPropertyRelative("damageElement").objectReferenceValue = element;
            entry.FindPropertyRelative("uiText").objectReferenceValue = value;
            entry.FindPropertyRelative("imageIcon").objectReferenceValue = null;
            entry.FindPropertyRelative("root").objectReferenceValue = row.gameObject;
        }

        private static void SetFormat(SerializedProperty format, string value)
        {
            format.FindPropertyRelative("useCustomValue").boolValue = true;
            format.FindPropertyRelative("customValue").stringValue = value;
        }

        /// <summary>
        /// The preview: a backdrop panel in the field colour, first child of the doll so the slots
        /// draw over it, holding the RawImage the paper doll films into.
        ///
        /// **The backdrop is what gets the border.** `Skin UI` destroys every `--DemoFrame` and
        /// redraws them on the Images it classifies by colour - a RawImage is not one, so a frame
        /// hung on it vanished the next time the skin ran. The field colour is one it frames as a
        /// slot, so the border comes back on every run; the copy made here only covers the gap
        /// until then.
        /// </summary>
        private static void BuildPreview(RectTransform doll)
        {
            var preview = new GameObject("Preview", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<RectTransform>();
            preview.SetParent(doll, false);
            preview.SetAsFirstSibling();
            float left = SlotInset + SlotSize + 8f;
            SetTopRect(preview, left, 0f, InfoWidth - 2f * left, DollHeight);
            var backdrop = preview.GetComponent<Image>();
            backdrop.color = DemoPalette.Field;
            backdrop.raycastTarget = false;

            var render = new GameObject(RenderName, typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage)).GetComponent<RectTransform>();
            render.SetParent(preview, false);
            render.anchorMin = Vector2.zero;
            render.anchorMax = Vector2.one;
            render.offsetMin = render.offsetMax = Vector2.zero;
            var image = render.GetComponent<RawImage>();
            // Stays a raycast target: dragging on the figure is what turns it.
            image.raycastTarget = true;
            image.enabled = false;
            render.gameObject.AddComponent<UICharacterPaperDoll>();

            Transform slotFrame = doll.Find("EquipSlotHead/--DemoFrame");
            if (slotFrame != null)
            {
                var frame = Object.Instantiate(slotFrame.gameObject, preview, false);
                frame.name = slotFrame.name;
                frame.transform.SetAsLastSibling();
                var frameImage = frame.GetComponent<Image>();
                if (frameImage != null)
                    frameImage.raycastTarget = false;
            }
        }

        private static void WirePreview(Transform preview)
        {
            if (preview == null)
                return;
            var doll = preview.GetComponent<UICharacterPaperDoll>();
            if (doll == null)
                doll = preview.gameObject.AddComponent<UICharacterPaperDoll>();
            // The frame's darkest step: a stage in the window's own metal rather than a new colour.
            Color32 dark = DemoPalette.FrameDark;
            doll.background = new Color(dark.r / 255f, dark.g / 255f, dark.b / 255f, 1f);
            EditorUtility.SetDirty(doll);
        }

        /// <summary>
        /// The name in the title bar, level and class centred under it - WoW's header. Wired every
        /// build, since it only binds existing text to the character.
        /// </summary>
        private static void WireHeader(GameObject root, Transform window, Transform info)
        {
            var character = root.GetComponent<UICharacter>();

            Transform titleText = window.Find("Title/Text");
            if (titleText != null)
            {
                var wrapper = titleText.GetComponent<TextWrapper>();
                if (wrapper == null)
                    wrapper = titleText.gameObject.AddComponent<TextWrapper>();
                character.uiTextName = wrapper;
            }

            Transform subtitle = info.Find("Subtitle");
            if (subtitle == null)
            {
                var rect = new GameObject("Subtitle", typeof(RectTransform)).GetComponent<RectTransform>();
                rect.SetParent(info, false);
                SetTopRect(rect, 0f, 0f, InfoWidth, SubtitleHeight);
                var row = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
                row.childAlignment = TextAnchor.MiddleCenter;
                row.spacing = 6f;
                row.childControlWidth = true;
                row.childControlHeight = true;
                row.childForceExpandWidth = false;
                row.childForceExpandHeight = true;
                subtitle = rect;
            }

            Text reference = info.GetComponentInChildren<Text>(true);
            TextWrapper level = SubtitleText(subtitle, "TextLevel", reference);
            character.uiTextLevel = level;
            // "Level 12", as WoW has it, rather than the template's "Lv: 12".
            character.formatKeyLevel = new UILocaleKeySetting(UIFormatKeys.UI_FORMAT_LEVEL)
            {
                useCustomValue = true,
                customValue = "Level {0}",
            };

            TextWrapper cls = SubtitleText(subtitle, "TextClass", reference);
            var classUi = cls.GetComponent<UICharacterClass>();
            if (classUi == null)
                classUi = cls.gameObject.AddComponent<UICharacterClass>();
            classUi.uiTextTitle = cls;
            character.uiCharacterClass = classUi;
            EditorUtility.SetDirty(character);
        }

        private const string AttributeDir = "Assets/OpenMMORPG/Demo/GameData/Resources/Attributes";

        /// <summary>The template's four attribute cards, by object name, and the demo attribute each shows.</summary>
        private static readonly KeyValuePair<string, string>[] AttributeRows =
        {
            new KeyValuePair<string, string>("AttrStrBG", "Strength"),
            new KeyValuePair<string, string>("AttrDexBG", "Dexterity"),
            new KeyValuePair<string, string>("AttrVitBG", "Vitality"),
            new KeyValuePair<string, string>("AttrIntBG", "Intelligence"),
        };

        /// <summary>
        /// Binds the four attribute cards to the demo's attributes.
        ///
        /// **The list was empty, not wrong.** `DemoUIWiring` dropped the template's pairs because
        /// they pointed at deleted kit attributes, back when the demo had none of its own; when
        /// `DemoProgressionBuilder` authored Strength, Dexterity, Vitality and Intelligence nothing
        /// bound them here, so every card read 0 under the template's own titles ("Intelligent")
        /// and threw a `NullReferenceException` from `UICharacterAttribute.UpdateUI` each time the
        /// window opened. Rebuilt every run from the table, so it cannot drift from the assets.
        /// </summary>
        private static void WireAttributes(GameObject root, Transform info)
        {
            var character = root.GetComponent<UICharacter>();
            Transform panel = info.Find("Attributes");
            var pairs = new List<UICharacterAttributePair>();
            foreach (KeyValuePair<string, string> row in AttributeRows)
            {
                Transform card = panel != null ? panel.Find(row.Key) : null;
                var attribute = AssetDatabase.LoadAssetAtPath<Attribute>($"{AttributeDir}/{row.Value}.asset");
                if (card == null || attribute == null)
                {
                    Debug.LogWarning($"[{nameof(DemoCharacterSheetBuilder)}] Attribute card \"{row.Key}\" or " +
                                     $"attribute \"{row.Value}\" is missing, so that card stays unbound.");
                    continue;
                }
                pairs.Add(new UICharacterAttributePair { attribute = attribute, ui = card.GetComponent<UICharacterAttribute>() });
            }
            character.uiCharacterAttributes = pairs.ToArray();
            EditorUtility.SetDirty(character);
        }

        private static TextWrapper SubtitleText(Transform subtitle, string textName, Text reference)
        {
            Transform existing = subtitle.Find(textName);
            GameObject go = existing != null
                ? existing.gameObject
                : new GameObject(textName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(subtitle, false);
            var text = go.GetComponent<Text>();
            if (existing == null)
            {
                text.font = reference != null ? reference.font : text.font;
                text.fontSize = 14;
                text.alignment = TextAnchor.MiddleCenter;
                text.color = reference != null ? reference.color : Color.black;
                text.raycastTarget = false;
            }
            var wrapper = go.GetComponent<TextWrapper>();
            if (wrapper == null)
                wrapper = go.AddComponent<TextWrapper>();
            return wrapper;
        }

        // ------------------------------------------------------------------ bag window

        /// <summary>Switches the equipment off in the Items dialog and gives the bag its room.</summary>
        private static void TrimItemsDialog()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(ItemsDialogPath);
            try
            {
                SetActive(root.transform, EquipLogicName, false);
                Transform info = root.transform.Find("Window/Info");
                SetActive(info, "EquipItems", false);
                var bag = (RectTransform)info.Find("NonEquipItems");
                // Its top edge sat under the 100-unit equipment block; now it starts at the top.
                bag.offsetMax = new Vector2(bag.offsetMax.x, 0f);
                Transform title = root.transform.Find("Window/Title/Text");
                if (title != null)
                    title.GetComponent<Text>().text = "Bag";
                PrefabUtility.SaveAsPrefabAsset(root, ItemsDialogPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ------------------------------------------------------------------ across dialogs

        /// <summary>
        /// The links that need both dialogs: the item popup the slots open, and selecting in one
        /// panel clearing the selection in the other. Also takes back the overrides the canvases
        /// pinned on the character dialog (<see cref="RevertPinned"/>).
        /// </summary>
        private static int WireAcrossDialogs()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(DialogsPath);
            int links = 0;
            try
            {
                Transform character = root.transform.Find("UICharacterDialog");
                // First, before anything is changed: a regenerate replaces the equipment objects,
                // which strands the overrides made on the previous ones, and clearing those
                // re-merges the instance - which throws away any change not yet recorded.
                PrefabUtility.RemoveUnusedOverrides(new[] { character.gameObject }, InteractionMode.AutomatedAction);
                Transform items = root.transform.Find("UIItemsDialog");
                Transform popup = root.transform.Find("UIItemDialogs/UIItemDialog");
                var equip = character.Find(EquipLogicName).GetComponent<UIEquipItems>();
                var equipSelection = equip.GetComponent<UICharacterItemSelectionManager>();
                var bagSelection = items.Find("--NonEquipItems").GetComponent<UICharacterItemSelectionManager>();

                if (popup != null)
                {
                    equip.uiDialog = popup.GetComponent<UICharacterItem>();
                    PrefabUtility.RecordPrefabInstancePropertyModifications(equip);
                    ++links;
                }

                links += Link(equipSelection, bagSelection);
                links += Link(bagSelection, equipSelection);

                RevertPinned(character);
                PrefabUtility.SaveAsPrefabAsset(root, DialogsPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            RevertCanvasOverrides();
            return links;
        }

        /// <summary>Makes selecting in <paramref name="from"/> clear <paramref name="to"/>, replacing whatever it cleared before.</summary>
        private static int Link(UICharacterItemSelectionManager from, UICharacterItemSelectionManager to)
        {
            for (int i = from.eventOnSelect.GetPersistentEventCount() - 1; i >= 0; --i)
            {
                if (from.eventOnSelect.GetPersistentMethodName(i) == nameof(UICharacterItemSelectionManager.DeselectSelectedUI))
                    UnityEventTools.RemovePersistentListener(from.eventOnSelect, i);
            }
            UnityEventTools.AddVoidPersistentListener(from.eventOnSelect, to.DeselectSelectedUI);
            PrefabUtility.RecordPrefabInstancePropertyModifications(from);
            return 1;
        }

        /// <summary>
        /// Takes back what the nesting prefabs pinned on the character dialog: the window size, and
        /// the attribute list, which the old prune of dead template rows emptied at every level.
        /// Either override would hide the dialog prefab's own value. The same prune and the hiding
        /// of rows with dead labels also pinned the armour and resistance panel - empty row lists,
        /// rows switched off - at every level (<see cref="RevertResistancePanel"/>).
        /// </summary>
        private static void RevertPinned(Transform dialog)
        {
            RevertOverride(dialog, "m_SizeDelta");
            RevertOverride(dialog.GetComponent<UICharacter>(), "uiCharacterAttributes");
            RevertResistancePanel(dialog);
        }

        /// <summary>
        /// Every override a nesting prefab holds on the armour and resistance lists, on their rows'
        /// switched-off state and on the rows' label setters, so the dialog prefab's own binding
        /// (<see cref="WireResistances"/>) is what shows. Returns whether anything was reverted.
        /// </summary>
        private static bool RevertResistancePanel(Transform dialog)
        {
            bool reverted = false;
            foreach (UIArmorAmounts armors in dialog.GetComponentsInChildren<UIArmorAmounts>(true))
                reverted |= RevertComponent(armors);
            foreach (UIResistanceAmounts resistances in dialog.GetComponentsInChildren<UIResistanceAmounts>(true))
                reverted |= RevertComponent(resistances);
            Transform panel = dialog.Find("Window/Info/Resistances");
            if (panel == null)
                return reverted;
            foreach (Transform row in panel)
            {
                reverted |= RevertOverride(row.gameObject, "m_IsActive");
                foreach (TextSetterByGameDataTitle setter in row.GetComponentsInChildren<TextSetterByGameDataTitle>(true))
                    reverted |= RevertComponent(setter);
            }
            return reverted;
        }

        private static bool RevertComponent(Component component)
        {
            if (component == null || !PrefabUtility.IsPartOfPrefabInstance(component) ||
                PrefabUtility.GetObjectOverrides(component.gameObject).Count == 0)
                return false;
            bool any = false;
            foreach (var change in PrefabUtility.GetObjectOverrides(component.gameObject))
                any |= change.instanceObject == component;
            if (any)
                PrefabUtility.RevertObjectOverride(component, InteractionMode.AutomatedAction);
            return any;
        }

        private static bool RevertOverride(Object target, string propertyPath)
        {
            if (target == null)
                return false;
            var so = new SerializedObject(target);
            SerializedProperty property = so.FindProperty(propertyPath);
            if (property == null || !property.prefabOverride)
                return false;
            PrefabUtility.RevertPropertyOverride(property, InteractionMode.AutomatedAction);
            return true;
        }

        private static void RevertCanvasOverrides()
        {
            const string canvasPath = UiDir + "/CanvasGameplay.prefab";
            GameObject root = PrefabUtility.LoadPrefabContents(canvasPath);
            try
            {
                Transform dialog = root.transform.Find("UIDialogs_Standalone/UICharacterDialog");
                if (dialog == null)
                    return;
                bool size = RevertOverride(dialog, "m_SizeDelta");
                bool attributes = RevertOverride(dialog.GetComponent<UICharacter>(), "uiCharacterAttributes");
                bool resistances = RevertResistancePanel(dialog);
                if (size || attributes || resistances)
                    PrefabUtility.SaveAsPrefabAsset(root, canvasPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Top-left anchored rect at (x, y) measured down from the parent's top-left.</summary>
        private static void SetTopRect(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }

        /// <summary>Centres a child on (x, y), measured down from the parent's top-left, keeping its size.</summary>
        private static void PlaceCentred(Transform parent, string childName, float x, float y)
        {
            var rect = parent.Find(childName) as RectTransform;
            if (rect == null)
            {
                Debug.LogWarning($"[{nameof(DemoCharacterSheetBuilder)}] No \"{childName}\" under {parent.name}.");
                return;
            }
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(x, -y);
        }

        private static void SetActive(Transform parent, string childName, bool active)
        {
            Transform child = parent.Find(childName);
            if (child != null)
                child.gameObject.SetActive(active);
        }
    }
}
