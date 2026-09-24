using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// The in-game HUD: what it shows, where it sits, and what colour it is.
    ///
    /// The kit's gameplay canvas is a **template**, not a design. It ships every feature the kit
    /// has turned on at once - PvP counters, a vending button, a party panel standing open, the
    /// network round-trip time - in placeholder magenta, and a demo inherits all of it. This trims
    /// it to what the demo actually does and paints the rest to match the menu.
    ///
    /// Modelled loosely on World of Warcraft, which is worth naming because it settles arguments:
    /// player frame top-left, minimap top-right on its own, action bar centred along the bottom
    /// with the experience bar under it, chat bottom-left, and **nothing else on screen until the
    /// player asks for it**.
    ///
    /// **Nothing is deleted.** Everything trimmed is deactivated instead. The kit holds serialized
    /// references to these objects, and a deleted one leaves a *missing* reference rather than a
    /// null - which the kit dereferences without checking, exactly as the equipment slots and data
    /// rows did before `DemoUIWiring` had to go and prune them. Deactivating leaves every reference
    /// intact, and turning a feature back on is one checkbox.
    /// </summary>
    public static class DemoHudBuilder
    {
        private const string CanvasPath = "Assets/OpenMMORPG/Demo/Prefabs/UI/CanvasGameplay.prefab";
        private const string PrefabRoot = "Assets/OpenMMORPG/Demo/Prefabs/UI";
        private const string HomeFolder = "Assets/OpenMMORPG/Demo/Prefabs/UI/Home";
        private const string TextureDir = "Assets/OpenMMORPG/Demo/Textures";
        private const string SharedFramePath = TextureDir + "/UI/UIFrame.png";
        private const string IslandSpritePath = TextureDir + "/MinimapIsland.png";
        private const string MinimapMaterial = "Assets/OpenMMORPG/Demo/Materials/MinimapGround.mat";
        private const string MapScenePath = "Assets/OpenMMORPG/Demo/Scenes/DemoMap.unity";

        /// <summary>The kit's own MiniMap layer. The minimap camera renders this and nothing else.</summary>
        private const int MinimapLayer = 10;

        /// <summary>The root the island plane lives under, so a rebuild replaces rather than stacks.</summary>
        private const string MinimapRoot = "MinimapGround";

        /// <summary>
        /// Everything the demo does not demonstrate, by path from the canvas root.
        ///
        /// Each of these is a feature the kit supports and the demo has no content for, so it shows
        /// as a control that does nothing or a counter that is always zero - which reads as broken
        /// rather than as unused.
        /// </summary>
        private static readonly string[] Hide =
        {
            // Round-trip time and a raw server timestamp, across the top of the screen. Debug
            // telemetry; the single most unfinished-looking thing on the HUD.
            "UIGameMessageHandler/NetworkTime",

            // PK toggle, PK points and kills. The demo has no PvP, so these are three zeroes.
            "UIGenericLayout/PK",

            // Player vending. No content behind it.
            "UIGenericLayout/Button--Vending",

            // NOTE: UIPartyAndQuest is deliberately **not** on this list. It was, and that
            // hid the quest tracker along with the party panel - see SplitTrackers, which
            // separates the two and lets each hide itself when it has nothing to show. That
            // is the behaviour hiding it here was reaching for, without losing the quests.
        };

        /// <summary>
        /// The two buttons that floated under the minimap, and where they belong instead.
        ///
        /// They are the only HUD controls the kit puts outside the menu bar, which left the
        /// top-right corner with a minimap and two unrelated buttons stuck to it. Moved into the
        /// bar they read as what they are - two more places to go - and the corner becomes the
        /// minimap alone, which is the WoW arrangement.
        /// </summary>
        private static readonly string[] IntoMenuBar =
        {
            "UICraftingLayout/ButtonCrafting",
            "UIMailLayout/ButtonMail",
        };

        [MenuItem("Open MMORPG/Demo/Build HUD")]
        public static void Build()
        {
            Sprite frame = EnsureFrameSprite();

            GameObject canvas = PrefabUtility.LoadPrefabContents(CanvasPath);
            try
            {
                int hidden = HideUnusedFeatures(canvas);
                int moved = FoldIntoMenuBar(canvas);
                CentreActionBar(canvas);
                AutoAssignSkills(canvas);
                int sliders = AddMissingVolumeSliders(canvas);
                int options = AddClickToMoveSetting(canvas);
                bool tracker = SplitTrackers(canvas);
                DressMinimap(canvas, frame);
                PrefabUtility.SaveAsPrefabAsset(canvas, CanvasPath);
                Debug.Log($"[{nameof(DemoHudBuilder)}] HUD trimmed: {hidden} unused features hidden, " +
                          $"{moved} buttons folded into the menu bar, {sliders} volume sliders and " +
                          $"{options} option rows added, action bar centred, minimap dressed, " +
                          $"{(tracker ? "trackers split left and right, each hiding when empty" : "TRACKERS NOT SPLIT")}.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(canvas);
            }

            int attributes = SyncAttributeRows();
            int painted = Repaint();
            if (attributes > 0)
                Debug.Log($"[{nameof(DemoHudBuilder)}] Showed {attributes} attribute rows: the demo defines Attribute assets now.");
            else if (attributes < 0)
                Debug.Log($"[{nameof(DemoHudBuilder)}] Hid {-attributes} attribute rows: the demo defines no Attribute assets.");
            AssetDatabase.SaveAssets();
            Debug.Log($"[{nameof(DemoHudBuilder)}] Repainted {painted} in-game prefabs onto the demo palette. " +
                      "This wrote prefabs only - DemoMap is untouched; the minimap's ground plane is " +
                      "\"Build Minimap Ground\", which is separate because it re-saves the map scene.");
        }

        // ------------------------------------------------------------------
        // Trimming
        // ------------------------------------------------------------------

        /// <summary>
        /// Leaves the party/quest panel open on its **Quest** tab, so the demo's four quests
        /// are tracked on screen instead of being remembered.
        ///
        /// This panel was hidden outright until now, and that was the wrong call. The
        /// problem it was hiding is real - the template opens the panel on the **Party**
        /// tab, so a solo player is permanently told they are not in a party - but hiding
        /// the whole thing took the quest tracker with it, and quests are content the demo
        /// actually has. Switching the tab fixes the empty-party complaint without costing
        /// anything: the Party tab is still there, one click away, for when there is a
        /// second player to put in one.
        ///
        /// Both halves of a tab have to be set. The toggle drives the content through
        /// `onValueChanged`, which does not run on a prefab being written to disk - so
        /// setting only the toggle leaves the wrong panel showing until something clicks
        /// it, and setting only the panel leaves the tab drawn unselected over the right
        /// content.
        /// </summary>
        /// <summary>The root the quest half is rehoused in, so a rebuild replaces rather than stacks.</summary>
        private const string QuestTrackerName = "UIQuestTracker";

        /// <summary>
        /// Under the minimap on the right, and under the player frame on the left.
        ///
        /// The 10px margins are the minimap's own, which is the only HUD element the kit already
        /// held off the screen edge; everything placed since has matched it.
        /// </summary>
        private static readonly Vector2 QuestTrackerPosition = new Vector2(-10f, -175f);
        private static readonly Vector2 QuestTrackerSize = new Vector2(230f, 220f);
        private static readonly Vector2 PartyPanelPosition = new Vector2(10f, -140f);

        /// <summary>
        /// Splits the kit's tabbed party/quest panel into two trackers, one per side of the screen,
        /// and lets each disappear when it has nothing to show.
        ///
        /// The kit ships them as **one window with two tabs**, which makes them mutually exclusive:
        /// you cannot watch your quest objectives and your party's health at the same time, which
        /// is precisely what you want to do in a party. They are also both in the top-left corner,
        /// on top of the player frame. Separated, each goes where its subject already is - the
        /// quest tracker under the minimap on the right, next to the map it sends you to read, and
        /// the party under the player frame on the left, next to the health bar it repeats.
        ///
        /// **The quest half is copied, not moved.** `Content` carries the dark backing and the
        /// layout that both halves sit in, so the new tracker needs one of its own; cloning it and
        /// switching off the half that does not belong gives each panel a complete, independent
        /// copy in one step. Unity remaps a clone's internal references as it copies, so the
        /// cloned `UICharacterQuests` points at the cloned list container rather than reaching
        /// back into the original.
        ///
        /// **Switched off, not deleted** - the rule this whole file follows. The tab toggles still
        /// hold `onValueChanged` references to both halves, and deleting one would leave a
        /// *missing* reference where a null was expected. The tabs themselves are deactivated,
        /// which is what stops those calls ever firing.
        /// </summary>
        private static bool SplitTrackers(GameObject canvas)
        {
            Transform panel = FindByPath(canvas.transform, "UIPartyAndQuest");
            Transform content = panel == null ? null : panel.Find("Content");
            if (content == null)
            {
                Debug.LogWarning($"[{nameof(DemoHudBuilder)}] No UIPartyAndQuest/Content to split; " +
                                 "has the kit's layout changed?");
                return false;
            }
            panel.gameObject.SetActive(true);

            Transform stale = canvas.transform.Find(QuestTrackerName);
            if (stale != null)
                Object.DestroyImmediate(stale.gameObject);

            var tracker = new GameObject(QuestTrackerName, typeof(RectTransform), typeof(CanvasGroup));
            tracker.transform.SetParent(canvas.transform, false);
            // Beside the party half it came from, which is under every window. A new child goes
            // to the end of the canvas, and the end is drawn last and hit-tested first: the
            // tracker sat over the whole of UIDialogs_Standalone, the system menu and the
            // settings, and its quest rows - clickable images 256px wide down the right edge -
            // took the clicks meant for whatever opened there. The inventory opens right there,
            // and its title bar and close button were under the rows (found 2026-09-24). Here,
            // a window drawn over the tracker hides it and gets the clicks, as WoW's bags do.
            tracker.transform.SetSiblingIndex(panel.GetSiblingIndex() + 1);
            var trackerRect = (RectTransform)tracker.transform;
            trackerRect.anchorMin = trackerRect.anchorMax = new Vector2(1f, 1f);
            trackerRect.pivot = new Vector2(1f, 1f);
            trackerRect.anchoredPosition = QuestTrackerPosition;
            trackerRect.sizeDelta = QuestTrackerSize;

            GameObject copy = Object.Instantiate(content.gameObject, tracker.transform);
            copy.name = "Content";
            var copyRect = (RectTransform)copy.transform;
            copyRect.anchorMin = Vector2.zero;
            copyRect.anchorMax = Vector2.one;
            copyRect.offsetMin = Vector2.zero;
            copyRect.offsetMax = Vector2.zero;

            Show(copy.transform, "PartyComponents", false);
            Show(copy.transform, "QuestListComponents", true);
            Show(content, "PartyComponents", true);
            Show(content, "QuestListComponents", false);

            Transform tabs = panel.Find("Tabs");
            if (tabs != null)
                tabs.gameObject.SetActive(false);

            var panelRect = (RectTransform)panel;
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(0f, 1f);
            panelRect.pivot = new Vector2(0f, 1f);
            panelRect.anchoredPosition = PartyPanelPosition;

            StripQuestTrackerChrome(copy.transform);

            bool quest = AutoHide(tracker, copy.transform, "QuestListComponents/Info/NoTrackedQuests");
            bool party = AutoHide(panel.gameObject, content, "PartyComponents/Info/NotInParty");
            if (!quest || !party)
                Debug.LogWarning($"[{nameof(DemoHudBuilder)}] A tracker has no empty-state object to " +
                                 "watch, so it will stay on screen with nothing in it.");
            return true;
        }

        /// <summary>The quest entry and its task lines, which are spawned into the tracker at runtime.</summary>
        private const string QuestEntryPrefab = "Assets/OpenMMORPG/Demo/Prefabs/UI/Quest/UICharacterQuest-Tiny.prefab";
        private const string QuestTaskPrefab = "Assets/OpenMMORPG/Demo/Prefabs/UI/Quest/UIQuestTask-Tiny.prefab";

        /// <summary>
        /// Takes the panel away from the quest tracker and leaves the writing.
        ///
        /// A tracker is not a window you opened, it is a note pinned over the world, and WoW draws
        /// it exactly that way: no border, no backing, just text. Giving it the panel treatment was
        /// wrong twice over - it boxed off the one piece of HUD that should read as part of the
        /// scene, and the border then sat **on top of the first line**, because the tracker's list
        /// starts flush against its own top-left corner and the frame's bands are eight pixels
        /// wide. That is why the quest title came out as "hin the Camp": the metal ate "wit".
        ///
        /// **Transparency is the whole mechanism.** `DemoUiSkin` skips anything under 35% alpha,
        /// so clearing the backing removes the fill and stops a border ever being drawn on it -
        /// one value, not a special case in the skinner.
        ///
        /// What the panel was doing - separating light text from whatever is behind it - now falls
        /// to an `Outline` on the text itself. Over a night-time camp lit by braziers, white-on-
        /// nothing is unreadable the moment it crosses a flame; a one-pixel black outline holds on
        /// both. It is drawn in all four diagonals rather than the cheaper single `Shadow`, which
        /// only saves the two edges it falls on.
        ///
        /// The title also stops overflowing. It was `Overflow`, so a long quest name ran out past
        /// the tracker and off the screen edge rather than wrapping; with no panel to hide behind
        /// there is nothing to stop it, so it wraps.
        /// </summary>
        private static void StripQuestTrackerChrome(Transform trackerContent)
        {
            var backing = trackerContent.GetComponent<Image>();
            if (backing != null)
                backing.color = new Color(0f, 0f, 0f, 0f);

            StyleQuestText(QuestEntryPrefab, true);
            StyleQuestText(QuestTaskPrefab, false);
        }

        private static void StyleQuestText(string path, bool isEntry)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                // The entry carries the row's own dark backing; the task line is bare text.
                var backing = root.GetComponent<Image>();
                if (isEntry && backing != null)
                    backing.color = new Color(0f, 0f, 0f, 0f);

                foreach (Text text in root.GetComponentsInChildren<Text>(true))
                {
                    text.horizontalOverflow = HorizontalWrapMode.Wrap;
                    var outline = text.GetComponent<Outline>();
                    if (outline == null)
                        outline = text.gameObject.AddComponent<Outline>();
                    outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
                    outline.effectDistance = new Vector2(1f, 1f);
                    outline.useGraphicAlpha = true;
                }
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void Show(Transform parent, string child, bool show)
        {
            Transform target = parent.Find(child);
            if (target != null)
                target.gameObject.SetActive(show);
        }

        /// <summary>
        /// Points a tracker at the "nothing here" object the kit already toggles, so it can hide
        /// itself. See <see cref="DemoTrackerPanel"/> for why that object rather than the party
        /// and quest data behind it.
        /// </summary>
        private static bool AutoHide(GameObject target, Transform root, string emptyStatePath)
        {
            Transform empty = FindByPath(root, emptyStatePath);
            if (empty == null)
                return false;
            if (target.GetComponent<CanvasGroup>() == null)
                target.AddComponent<CanvasGroup>();
            var hider = target.GetComponent<DemoTrackerPanel>();
            if (hider == null)
                hider = target.AddComponent<DemoTrackerPanel>();
            hider.emptyState = empty.gameObject;
            return true;
        }

        private static int HideUnusedFeatures(GameObject canvas)
        {
            int hidden = 0;
            foreach (string path in Hide)
            {
                Transform target = FindByPath(canvas.transform, path);
                if (target == null)
                {
                    Debug.LogWarning($"[{nameof(DemoHudBuilder)}] Nothing at \"{path}\" to hide.");
                    continue;
                }
                if (!target.gameObject.activeSelf)
                    continue;
                target.gameObject.SetActive(false);
                ++hidden;
            }
            return hidden;
        }

        /// <summary>
        /// Lines the stray buttons up as a continuation of the menu bar.
        ///
        /// **They are not reparented into it, because Unity will not allow it.** They live inside
        /// `UICraftingLayout` and `UIMailLayout`, which are *nested prefab instances*, and moving a
        /// child out of one would break its link to its prefab - Unity refuses with "Setting the
        /// parent of a transform which resides in a Prefab instance is not possible" and carries
        /// on. The first version of this did exactly that, and because the refusal is a log line
        /// rather than an exception it went on to apply the layout-group anchors anyway: both
        /// buttons became full-screen stretch and would have swallowed every click on the HUD.
        /// **A refused reparent is not an error you will be handed; check the result.**
        ///
        /// Repositioning is allowed, because a property override on a prefab instance is fine -
        /// it is only re-parenting that is not. All three layouts are full-screen stretch
        /// containers matching the canvas, so a top-left anchor inside any of them lands in the
        /// same place, and the bar sits at 0,0 with a top-left pivot and is 275 wide. Butting the
        /// buttons up against 275 makes one continuous row, and the repaint has already given them
        /// the same parchment as the bar.
        /// </summary>
        private static int FoldIntoMenuBar(GameObject canvas)
        {
            Transform bar = FindByPath(canvas.transform, "UIGenericLayout/Menu");
            if (bar == null)
            {
                Debug.LogWarning($"[{nameof(DemoHudBuilder)}] No menu bar to line the buttons up with.");
                return 0;
            }
            var barRect = (RectTransform)bar;
            float x = barRect.anchoredPosition.x + barRect.sizeDelta.x + Gap;
            // Take the bar's own width-per-button and label size rather than naming numbers here:
            // these two only look like part of the row if they match whatever the row is.
            float width = barRect.sizeDelta.x / Mathf.Max(1, bar.childCount);
            int labelSize = MenuLabelSize(bar);

            int moved = 0;
            foreach (string path in IntoMenuBar)
            {
                Transform button = FindByPath(canvas.transform, path);
                if (button == null)
                {
                    Debug.LogWarning($"[{nameof(DemoHudBuilder)}] Nothing at \"{path}\" to move.");
                    continue;
                }
                var rect = (RectTransform)button;
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.sizeDelta = new Vector2(width, barRect.sizeDelta.y);
                rect.anchoredPosition = new Vector2(x, barRect.anchoredPosition.y);
                x += width + Gap;
                // They shipped at 14pt against the bar's 9, which at twice the width read as two
                // different pieces of UI sitting next to each other rather than one row.
                if (labelSize > 0)
                {
                    foreach (Text label in button.GetComponentsInChildren<Text>(true))
                        label.fontSize = labelSize;
                }
                ++moved;
            }
            return moved;
        }

        private const float Gap = 2f;

        /// <summary>The label size the menu bar's own buttons use, or 0 if it has none to copy.</summary>
        private static int MenuLabelSize(Transform bar)
        {
            foreach (Text label in bar.GetComponentsInChildren<Text>(true))
                return label.fontSize;
            return 0;
        }

        /// <summary>
        /// Moves the hotkey bar from the bottom-right corner to the middle, above the experience bar.
        ///
        /// The container inside it already stretches edge to edge; what pinned it to the corner was
        /// its **parent**, anchored bottom-right in a 442px box. So the parent moves and the bar
        /// inside it needs no changes at all - which is the usual shape of a uGUI layout bug, and
        /// the reason to read the parent before touching the thing that looks wrong.
        /// </summary>
        private static void CentreActionBar(GameObject canvas)
        {
            Transform hotkeys = canvas.transform.Find("UIHotkeys_Standalone");
            if (hotkeys == null)
            {
                Debug.LogWarning($"[{nameof(DemoHudBuilder)}] No hotkey bar to centre.");
                return;
            }
            var rect = (RectTransform)hotkeys;
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            // Clear of the experience bar, which is 20 tall along the very bottom.
            rect.anchoredPosition = new Vector2(0f, 24f);
        }

        /// <summary>
        /// The volume sliders the settings dialog never had.
        ///
        /// The template ships **BGM and SFX only**. The other two sliders in that dialog are mouse
        /// sensitivity - so the `Master` and `Ambient` settings existed on the `AudioManager`, were
        /// read every frame by everything that plays, and could not be changed by the player at
        /// all. The island's ambience was the visible casualty: `DemoAmbientLoop` multiplies by the
        /// **ambient** level, faithfully, and nothing could move it.
        ///
        /// Cloned from the Sfx row rather than built from parts, so the new rows inherit whatever
        /// the dialog's styling happens to be and stay in step if it changes. The parent is a
        /// `GridLayoutGroup` with a `ContentSizeFitter`, so position and size need no thought -
        /// adding a child is the whole job, and **adding** a child to a nested prefab instance is
        /// allowed where reparenting one out of it is not.
        /// </summary>
        private static int AddMissingVolumeSliders(GameObject canvas)
        {
            Transform model = null;
            foreach (Transform t in canvas.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "Sfx" && t.GetComponentInChildren<Insthync.AudioManager.AudioSlider>(true) != null)
                    model = t;
            }
            if (model == null)
            {
                Debug.LogWarning($"[{nameof(DemoHudBuilder)}] No Sfx row to copy the missing volume sliders from.");
                return 0;
            }

            int added = 0;
            // Master first, because it scales the others; Ambient last, next to the two it joins.
            added += AddVolumeSlider(model, "Master", "Master", 0, 0);
            added += AddVolumeSlider(model, "Ambient", "Ambient", 3, model.GetSiblingIndex() + 1);
            return added;
        }

        private static int AddVolumeSlider(Transform model, string name, string label, int settingType, int siblingIndex)
        {
            foreach (Transform sibling in model.parent)
            {
                if (sibling.name == name)
                    return 0;
            }
            var row = Object.Instantiate(model.gameObject, model.parent);
            row.name = name;
            row.transform.SetSiblingIndex(siblingIndex);

            foreach (var slider in row.GetComponentsInChildren<Insthync.AudioManager.AudioSlider>(true))
            {
                slider.gameObject.name = name + "Slider";
                var serialized = new SerializedObject(slider);
                serialized.FindProperty("type").enumValueIndex = settingType;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            foreach (Text text in row.GetComponentsInChildren<Text>(true))
            {
                if (text.name != "TextLabel")
                    continue;
                text.text = label;
                break;
            }
            return 1;
        }

        /// <summary>
        /// Puts <see cref="MultiplayerARPG.Demo.DemoAutoHotkeys"/> on the hotkey bar, so a skill
        /// lands on a key the moment it is learned.
        ///
        /// On the bar rather than on the player or a manager, because that is the thing it is
        /// about and the thing that is guaranteed to exist: the canvas is in the map scene from
        /// the start, where the player character is spawned some time later and replaced on every
        /// respawn. The component copes with there being no character yet.
        /// </summary>
        private static void AutoAssignSkills(GameObject canvas)
        {
            Transform hotkeys = canvas.transform.Find("UIHotkeys_Standalone");
            if (hotkeys == null)
            {
                Debug.LogWarning($"[{nameof(DemoHudBuilder)}] No hotkey bar to auto-assign skills on.");
                return;
            }
            if (hotkeys.GetComponent<MultiplayerARPG.Demo.DemoAutoHotkeys>() == null)
                hotkeys.gameObject.AddComponent<MultiplayerARPG.Demo.DemoAutoHotkeys>();
        }

        /// <summary>
        /// A Click To Move row in the settings dialog, off by default.
        ///
        /// Cloned from the **Shadows** row, which is already the shape wanted: a label, and a
        /// `ToggleGroup` of two radio buttons reading Off and On. Cloning means the row inherits
        /// whatever the dialog's styling is and stays in step with it; building one from parts
        /// would drift the first time the dialog changed.
        ///
        /// The kit's `ShadowsSetting` comes with the clone and has to go, or the new row would
        /// quietly drive the shadow quality as well as the thing it claims to. Its replacement
        /// stores the choice in `PlayerPrefs`, which is the only way the dialog can reach a
        /// controller that does not exist until a character spawns - see <see cref="DemoSettings"/>.
        ///
        /// Placed after Zoom Mouse Sensitivity, next to the other mouse settings rather than at
        /// the end, because that is where somebody looking for it would look.
        /// </summary>
        private static int AddClickToMoveSetting(GameObject canvas)
        {
            Transform model = null;
            Transform grid = null;
            foreach (Transform t in canvas.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "Shadows" && t.GetComponentInChildren<Toggle>(true) != null)
                    model = t;
                if (t.name == "Zoom Mouse Sensitivity")
                    grid = t.parent;
            }
            if (model == null || grid == null)
            {
                Debug.LogWarning($"[{nameof(DemoHudBuilder)}] No Shadows row to copy the Click To Move setting from.");
                return 0;
            }
            foreach (Transform sibling in grid)
            {
                if (sibling.name == "ClickToMove")
                    return 0;
            }

            var row = Object.Instantiate(model.gameObject, model.parent);
            row.name = "ClickToMove";
            foreach (Transform sibling in grid)
            {
                if (sibling.name != "Zoom Mouse Sensitivity")
                    continue;
                row.transform.SetSiblingIndex(sibling.GetSiblingIndex() + 1);
                break;
            }

            foreach (Text text in row.GetComponentsInChildren<Text>(true))
            {
                if (text.name == "TextLabel")
                {
                    text.text = "Click To Move";
                    break;
                }
            }

            // The kit's shadow driver came with the copy; it must not survive it, or the new
            // row would quietly set the shadow quality as well as the thing it claims to.
            foreach (var stale in row.GetComponentsInChildren<Insthync.GraphicSettings.ShadowsSetting>(true))
                Object.DestroyImmediate(stale);

            foreach (Toggle toggle in row.GetComponentsInChildren<Toggle>(true))
            {
                bool on = toggle.name.EndsWith("On");
                toggle.gameObject.AddComponent<MultiplayerARPG.Demo.DemoClickToMoveSetting>().SetValue(on);
                toggle.isOn = !on;
            }
            return 1;
        }

        private const string CharacterDialogPath = "Assets/OpenMMORPG/Demo/Prefabs/UI/Player/UICharacterDialog.prefab";

        /// <summary>
        /// Switches off the character dialog's four attribute rows, because there is nothing for
        /// them to show.
        ///
        /// `UICharacterAttribute.UpdateUI` dereferences its `Attribute` without checking, and the
        /// demo defines **no `Attribute` assets at all** - so opening the character dialog threw a
        /// `NullReferenceException` per row, from `OnEnable`, every time. Strength, Dexterity,
        /// Vitality and Intelligence are the kit template's, not the demo's.
        ///
        /// **Conditional on there being none.** If attributes are ever added this stops hiding
        /// them, which is the difference between a fix and a scar. That is also why it cannot be a
        /// line in <see cref="Hide"/>: those are features the demo has decided against, this is a
        /// feature waiting on data.
        ///
        /// Only the four rows, not their `Attributes` parent - that also holds the stat-point and
        /// battle-point readouts, which work. And the **source** prefab is edited rather than the
        /// copies in `CanvasGameplay` and `UIDialogs_Standalone`, so the nested instances inherit
        /// it instead of collecting three identical overrides.
        /// </summary>
        private static int SyncAttributeRows()
        {
            // **Symmetric on purpose.** This only ever hid, and early-returned the moment an
            // Attribute existed - so once DemoProgressionBuilder authored four of them, the
            // rows it had hidden on an earlier run stayed hidden and the Attributes tab went
            // on looking empty with the data sitting right there. A switch that cannot be
            // switched back is not a switch.
            bool show = AssetDatabase.FindAssets("t:Attribute").Length > 0;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(CharacterDialogPath) == null)
            {
                Debug.LogWarning($"[{nameof(DemoHudBuilder)}] No character dialog at \"{CharacterDialogPath}\".");
                return 0;
            }

            GameObject dialog = PrefabUtility.LoadPrefabContents(CharacterDialogPath);
            try
            {
                int changed = 0;
                foreach (UICharacterAttribute row in dialog.GetComponentsInChildren<UICharacterAttribute>(true))
                {
                    if (row.gameObject.activeSelf == show)
                        continue;
                    row.gameObject.SetActive(show);
                    ++changed;
                }
                if (changed > 0)
                    PrefabUtility.SaveAsPrefabAsset(dialog, CharacterDialogPath);
                return show ? changed : -changed;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(dialog);
            }
        }

        // ------------------------------------------------------------------
        // The minimap
        // ------------------------------------------------------------------

        /// <summary>
        /// Repairs the minimap's own furniture.
        ///
        /// **`Frame` was the white square.** It is a full-size Image sitting on top of the masked
        /// render texture, and its sprite is one of the deleted kit assets - a *missing* reference,
        /// which Unity draws as an opaque white quad covering the entire map. The same trap as the
        /// gauges that would not fill: an Image without a sprite is not invisible, it is a
        /// rectangle. It gets a generated border instead.
        ///
        /// `Map` behind it gets a dark backing, so anything the render texture does not cover reads
        /// as night rather than as paper.
        /// </summary>
        private static void DressMinimap(GameObject canvas, Sprite frame)
        {
            Transform map = FindByPath(canvas.transform, "UIGenericLayout/Map");
            if (map == null)
            {
                Debug.LogWarning($"[{nameof(DemoHudBuilder)}] No minimap to dress.");
                return;
            }
            var backing = map.GetComponent<Image>();
            if (backing != null)
            {
                backing.sprite = null;
                backing.color = DemoPalette.MinimapBacking;
            }
            Transform frameObject = map.Find("Frame");
            var border = frameObject == null ? null : frameObject.GetComponent<Image>();
            if (border != null && frame != null)
            {
                border.sprite = frame;
                // Sliced, or the nine-slice borders on the sprite are ignored and the frame is
                // stretched into a smear.
                border.type = Image.Type.Sliced;
                border.color = Color.white;
                // It sits over the map; a frame that ate clicks would block anything the kit ever
                // puts on it.
                border.raycastTarget = false;
            }
        }

        /// <summary>
        /// The bronze border, from <see cref="DemoUiSkin"/>.
        ///
        /// It used to be generated here, into a `MinimapFrame.png` only the minimap ever used, and
        /// that turned out to be the whole complaint: the minimap was the one thing on screen with
        /// a border, because it was the one thing with a generator that drew it one. The generator
        /// moved to <see cref="DemoUiSkin"/> unchanged - same size, same band, same three browns -
        /// and now every window gets the identical asset. **Literally identical**, not a matching
        /// pair: two generators drawing "the same" border is how they stop being the same the first
        /// time one is retuned.
        ///
        /// Null until `Skin UI` has run at least once, which <see cref="DressMinimap"/> reports
        /// rather than writing a missing reference into the prefab.
        /// </summary>
        private static Sprite EnsureFrameSprite()
        {
            var frame = AssetDatabase.LoadAssetAtPath<Sprite>(SharedFramePath);
            if (frame == null)
                Debug.LogWarning($"[{nameof(DemoHudBuilder)}] No frame at \"{SharedFramePath}\" - run " +
                                 "\"Open MMORPG/Demo/Skin UI\" first; the minimap keeps the border it has.");
            return frame;
        }

        /// <summary>
        /// Puts something on the MiniMap layer for the minimap camera to film.
        ///
        /// **This is the other half of why the minimap was blank**, and it survived the first fix
        /// because the wiring all checked out: the render texture exists, the camera targets it, the
        /// RawImage displays it. What nothing checked was whether the camera could *see* anything -
        /// its culling mask is the MiniMap layer alone, and the scene had **no objects on that layer
        /// at all**. The camera is not in the scene either, and should not be: the kit spawns it
        /// from `DemoPlayerController`'s `minimapCameraPrefab` and has it follow the player.
        ///
        /// `MinimapIsland.png` was already rendered by <see cref="DemoMinimapBuilder"/>, but it was
        /// only written to the MapInfo asset, which feeds the **world map window** rather than the
        /// minimap. So it goes on a plane here as well.
        ///
        /// The plane sits **below** the terrain. Nothing else is on this layer, so the terrain
        /// cannot occlude it, and being underneath means the markers the kit spawns on entities -
        /// also on this layer, at ground height - always draw in front of it.
        /// </summary>
        /// <summary>
        /// **Its own menu item, because it writes `DemoMap.unity` and `Build HUD` must not.**
        ///
        /// This used to run at the end of every HUD build, which made a UI task silently re-save
        /// the gameplay scene. That is worse than untidy: a saved scene no longer matches any
        /// build made before it, and the way that surfaces is not a warning but
        /// `Unable to spawn object for spawn game state` on the client, followed by every scene
        /// object after the failed one going missing - because one bad read leaves the baseline
        /// stream mis-positioned. Nothing about "I restyled the inventory window" suggests the
        /// NPCs will stop loading, so the two jobs are separated and the map-writing one says so
        /// in its name.
        /// </summary>
        [MenuItem("Open MMORPG/Demo/Build Minimap Ground (writes DemoMap)", priority = 143)]
        public static void BuildMinimapGround()
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Texture2D>(IslandSpritePath);
            if (sprite == null)
            {
                Debug.LogError($"[{nameof(DemoHudBuilder)}] No island image at \"{IslandSpritePath}\" - " +
                               "run Build Minimap first.");
                return;
            }

            bool opened = false;
            Scene scene = SceneManager.GetSceneByPath(MapScenePath);
            if (!scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(MapScenePath, OpenSceneMode.Additive);
                opened = true;
            }

            Terrain terrain = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == MinimapRoot)
                    Object.DestroyImmediate(root);
                else if (terrain == null)
                    terrain = root.GetComponentInChildren<Terrain>(true);
            }
            if (terrain == null)
            {
                Debug.LogError($"[{nameof(DemoHudBuilder)}] No terrain in \"{MapScenePath}\" to size the minimap to.");
                if (opened)
                    EditorSceneManager.CloseScene(scene, true);
                return;
            }

            DemoItemBuilder.EnsureFolder("Assets/OpenMMORPG/Demo/Materials");
            var material = AssetDatabase.LoadAssetAtPath<Material>(MinimapMaterial);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(material, MinimapMaterial);
            }
            material.SetTexture("_BaseMap", sprite);
            EditorUtility.SetDirty(material);

            Vector3 origin = terrain.transform.position;
            Vector3 size = terrain.terrainData.size;

            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = MinimapRoot;
            EditorSceneManager.MoveGameObjectToScene(quad, scene);
            // Face up: a Quad looks along its local -Z, and Euler(90,0,0) turns that to +Y.
            quad.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            quad.transform.position = new Vector3(origin.x + (size.x * 0.5f), origin.y - 5f, origin.z + (size.z * 0.5f));
            quad.transform.localScale = new Vector3(size.x, size.z, 1f);
            quad.GetComponent<MeshRenderer>().sharedMaterial = material;
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.layer = MinimapLayer;
            quad.isStatic = true;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            if (opened)
                EditorSceneManager.CloseScene(scene, true);
            Debug.Log($"[{nameof(DemoHudBuilder)}] Minimap ground laid: {size.x:0}x{size.z:0}m on layer " +
                      $"{MinimapLayer} ({LayerMask.LayerToName(MinimapLayer)}), 5m under the terrain.");
        }

        // ------------------------------------------------------------------
        // Repaint
        // ------------------------------------------------------------------

        /// <summary>
        /// The same repaint the home screens got, across every in-game prefab.
        ///
        /// **The greens and the red stay put here**, which is the opposite of the choice made on the
        /// home screens - and the difference is real rather than inconsistency. In the menu, green
        /// was on Login, Connect and Register: primary actions with nothing to contrast against. In
        /// game it sits next to a red Delete and a red Drop, where green genuinely means "yes" and
        /// red means "you cannot undo this". Recolouring those to fit a palette costs more than it
        /// gains.
        ///
        /// The home canvas is skipped because the menu stage builder owns it, and two builders
        /// writing the same prefab is how one quietly saves over the other.
        /// </summary>
        /// <summary>
        /// The template's other panel white, a shade under pure white. Only the HUD uses it.
        /// </summary>
        private static readonly Color TemplatePanelWhite = new Color(0.933f, 0.933f, 0.933f);

        /// <summary>
        /// The HUD's own panels, which the first repaint missed and which is why half the screen
        /// stayed clinical white while every dialog went parchment.
        ///
        /// They need a list where the dialogs did not, and the reason is that the dialogs are
        /// built from a common template - their panel is always called `Window` - while these are
        /// one-off layouts with one-off names. Matching them on colour alone is not available
        /// either: they are plain white, and so is every icon, mask and gauge fill on the canvas.
        /// A name is the only thing that separates the eight panels from the four hundred whites.
        ///
        /// The **target frames** are on this list even though they are switched off in the prefab:
        /// they come on the instant you click a monster, and a target frame in template white
        /// hanging next to a parchment player frame is the single most visible way to leak the
        /// unfinished palette back onto the screen.
        /// </summary>
        private static readonly HashSet<string> HudPanels = new HashSet<string>
        {
            "UICharacterHpMp",
            "UIExpbar",
            "UIHotkeys_Standalone",
            "UIHotkeyAssigner_Standalone",
            "UITargetCharacterHp",
            "UITargetGameEntity",
            "UITargetDamageableHp",
            "NameBg",
        };

        private static int Repaint()
        {
            int touched = 0, headers = 0, panels = 0, fields = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.StartsWith(HomeFolder))
                    continue;

                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    bool dirty = false;
                    foreach (Image image in root.GetComponentsInChildren<Image>(true))
                    {
                        Transform holder = image.transform.parent;
                        bool isHeader = image.name == "Title" && holder != null && holder.name.StartsWith("Window");
                        if (isHeader || DemoPalette.Same(image.color, DemoPalette.PlaceholderMagenta)
                            || DemoPalette.SameAny(image.color, DemoPalette.KnownHeaders))
                        {
                            image.color = DemoPalette.Header;
                            ++headers;
                            dirty = true;
                            continue;
                        }
                        bool templateWhite = (DemoPalette.Same(image.color, Color.white) || DemoPalette.Same(image.color, TemplatePanelWhite))
                            && (image.name.StartsWith("Window") || image.name.StartsWith("Button") || HudPanels.Contains(image.name));
                        // A panel already painted in *some* theme moves to the active one: that is
                        // what makes DemoPalette.Active a switch rather than a one-way door.
                        if (templateWhite || DemoPalette.SameAny(image.color, DemoPalette.KnownPanels))
                        {
                            image.color = DemoPalette.Panel;
                            ++panels;
                            dirty = true;
                            continue;
                        }
                        if (DemoPalette.Same(image.color, DemoPalette.TemplateFieldGrey)
                            || DemoPalette.SameAny(image.color, DemoPalette.KnownFields))
                        {
                            image.color = DemoPalette.Field;
                            ++fields;
                            dirty = true;
                        }
                    }
                    if (dirty)
                    {
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                        ++touched;
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
            Debug.Log($"[{nameof(DemoHudBuilder)}] Repaint: {headers} headers, {panels} panels, {fields} fields.");
            return touched;
        }

        // ------------------------------------------------------------------

        private static Transform FindByPath(Transform root, string path)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                string full = t.name;
                Transform p = t.parent;
                while (p != null && p != root)
                {
                    full = p.name + "/" + full;
                    p = p.parent;
                }
                if (full == path)
                    return t;
            }
            return null;
        }

    }
}
