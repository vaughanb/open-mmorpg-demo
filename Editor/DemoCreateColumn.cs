using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// The right-hand column of the character create screen: what is in it, where each piece
    /// sits, and how the demo's own slider panels are made.
    ///
    /// The kit's create screen is built out of option windows - a framed panel with a title
    /// bar and a scrolling grid of tiles - and <see cref="UIBodyPartManager"/> knows how to
    /// fill them. Two of the demo's own choices are not tiles at all: skin tone and size are
    /// ramps, and a ramp wants a slider. There is no slider anywhere in the demo to copy, so
    /// this builds one.
    ///
    /// **The frame is cloned from an existing window rather than built from nothing**, so the
    /// panel image, the title bar and whatever `Build Menu Stage` has done to the menu's
    /// styling all come along. A hand-built window would drift from the six beside it the
    /// first time any of that changed.
    ///
    /// Shared by <see cref="DemoSkinToneBuilder"/> and <see cref="DemoCharacterSizeBuilder"/>,
    /// and that is the whole point. Their two panels sit one above the other in the same
    /// column, so they have to look like one pair rather than like two features - and neither
    /// of them can work out where it goes on its own, because <see cref="Restack"/> has to
    /// place the kit's windows in the same pass.
    /// </summary>
    internal static class DemoCreateColumn
    {
        /// <summary>
        /// The right-hand column of the create screen, top to bottom, and how tall each
        /// window in it is. The left column is the kit's hair and beard and is not touched.
        ///
        /// **The whole column is laid out here, including the kit's own windows**, and that
        /// is not tidiness for its own sake. The screen was full: the body list, the class
        /// list and the skin slider ran from the top of the column to the bottom of it with
        /// three or four pixels between them and nothing spare anywhere. A second slider
        /// only fits if everything else moves, so the positions cannot live one per builder
        /// - they have to be worked out together, from one table, by whichever builder
        /// happens to run.
        ///
        /// **The class description moves here too.** It was a 300-wide block of loose text
        /// stood on its own in the bottom-right corner, white over whatever the menu stage
        /// happened to put behind it, overlapping the name field and sitting four windows
        /// away from the class list it belongs to. In the column, on a panel of its own, it
        /// reads as the caption to that list - and the corner it gives back is what the size
        /// slider is built in.
        ///
        /// The budget: 290 down to a bottom margin of 30 - level with the left column's last
        /// panel - is 560 units, and the five entries plus four 6-unit gaps come to exactly
        /// that. What paid for it was the body list, which was built 170 tall for a scrolling
        /// grid and holds two entries: <see cref="BodyHeight"/> is what those two actually
        /// measure, and the 40 units it gives back are what let the size panel match the skin
        /// panel instead of being a squeezed-down version of it.
        /// </summary>
        private static readonly KeyValuePair<string, float>[] RightColumn =
        {
            new KeyValuePair<string, float>(BodyWindow, BodyHeight),
            new KeyValuePair<string, float>("Window--Class", 170f),
            new KeyValuePair<string, float>(DescriptionPanel, DescriptionHeight),
            new KeyValuePair<string, float>("Window--Size", 84f),
            new KeyValuePair<string, float>("Window--Skin", 84f),
        };

        /// <summary>
        /// The kit's body list. Its title is "Entity", which is the kit's word for the
        /// prefab a character is built from and means nothing to a player choosing between
        /// a man and a woman, so the demo retitles it.
        /// </summary>
        private const string BodyWindow = "Window--Entity";
        private const string BodyTitle = "Gender";

        /// <summary>
        /// The body list, cut to the two bodies it holds.
        ///
        /// Measured off the list itself rather than guessed: an entry's LayoutElement asks
        /// for 40, the vertical group spaces them 2 apart inside 4 of padding, so two of them
        /// are 90 - and a window carries 40 of title bar and margin around its scroll view.
        /// A third body would scroll, which is what the scroll view is for; the number here
        /// would want raising to 172 to show it, the same way the class list above holds
        /// three.
        /// </summary>
        private const float BodyHeight = 130f;

        /// <summary>The panel the demo puts behind the kit's class description.</summary>
        private const string DescriptionPanel = "Window--ClassInfo";
        /// <summary>Four lines at 12pt inside its padding, which is one more than the longest class needs.</summary>
        private const float DescriptionHeight = 68f;

        /// <summary>
        /// The left-hand column: the kit's hair and beard, stacked the same way as the right
        /// so the two agree at the top.
        ///
        /// It used to start 50 units lower, because the Back button sat above it in the
        /// corner. With the button moved to the foot of the screen
        /// (<see cref="PlaceBackButton"/>) the column runs from the same 290 the other one
        /// does, and the 62 units that leaves at its foot are the spare room on this screen -
        /// somewhere a sixth choice can go without another round of this arithmetic.
        ///
        /// The faction window is in the table although the demo has no factions and keeps it
        /// switched off: inactive entries are skipped, so it costs nothing now, and a demo
        /// that ever turns factions on gets it placed rather than overlapping the hair grid
        /// the way it does today.
        /// </summary>
        private static readonly KeyValuePair<string, float>[] LeftColumn =
        {
            new KeyValuePair<string, float>("Window--Faction", 170f),
            new KeyValuePair<string, float>("Window--Hair", 130f),
            new KeyValuePair<string, float>("Window--HairColor", 130f),
            new KeyValuePair<string, float>("Window--Beard", 90f),
            new KeyValuePair<string, float>("Window--BeardColor", 130f),
        };
        /// <summary>The kit's description Text, which carries the UICharacterClass the screen writes into.</summary>
        private const string DescriptionText = "SelectedCharacter";

        /// <summary>Where both columns start: 10 units down from the top of the screen.</summary>
        private const float ColumnTop = 290f;
        private const float ColumnGap = 6f;
        /// <summary>Each column's inset from its own edge, and the Back button's from the left.</summary>
        private const float ColumnX = 20f;
        internal const float ColumnWidth = 200f;

        private const string BackButtonName = "ButtonBack";
        private static readonly Vector2 BackButtonSize = new Vector2(100f, 30f);
        /// <summary>The middle of the Create button's row, measured from the bottom of the screen.</summary>
        private const float BackButtonRow = 50f;

        /// <summary>
        /// Stacks the column again from the table, and does the two pieces of tidying that
        /// belong to the column rather than to either slider: the class description's panel
        /// and the body list's title.
        ///
        /// Entries that are not there yet are simply skipped and the rest close up, so a
        /// screen with only one of the two sliders built still looks deliberate - and
        /// building the other one later puts everything right.
        ///
        /// Call it after adding a window, not before.
        /// </summary>
        internal static void Restack(Transform create)
        {
            SetTitle(create.Find(BodyWindow), BodyTitle);
            BuildDescriptionPanel(create);
            PlaceBackButton(create);

            Stack(create, LeftColumn, 0f, ColumnX);
            Stack(create, RightColumn, 1f, -ColumnX);
        }

        /// <summary>
        /// Lays one column out from its table. <paramref name="edge"/> is the screen edge it
        /// hangs off - 0 for the left, 1 for the right - and <paramref name="inset"/> the
        /// distance in from it, signed to match.
        ///
        /// Entries that are missing or switched off are skipped and the rest close up, which
        /// is what lets a screen with only one of the two sliders built still look deliberate
        /// - and lets the hidden faction window sit in the table costing nothing.
        /// </summary>
        private static void Stack(Transform create, KeyValuePair<string, float>[] column, float edge, float inset)
        {
            float top = ColumnTop;
            foreach (KeyValuePair<string, float> entry in column)
            {
                Transform window = create.Find(entry.Key);
                if (window == null || !window.gameObject.activeSelf)
                    continue;
                var rect = (RectTransform)window;
                rect.anchorMin = new Vector2(edge, 0.5f);
                rect.anchorMax = new Vector2(edge, 0.5f);
                rect.pivot = new Vector2(edge, 0.5f);
                rect.sizeDelta = new Vector2(ColumnWidth, entry.Value);
                rect.anchoredPosition = new Vector2(inset, top - entry.Value * 0.5f);
                top -= entry.Value + ColumnGap;
            }
        }

        /// <summary>
        /// Moves the Back button out of the top-left corner and down to the foot of the
        /// screen, as a square carrying a single `&lt;`.
        ///
        /// It stood above the hair grid, in the corner the eye starts in, holding 50 units the
        /// left column could not use. At the foot it is out of the way and level with the
        /// button that commits the screen - back on one side, forward in the middle.
        ///
        /// It keeps its word and its width. The corner is free now that the version badge has
        /// gone from `CanvasGlobal` (see DemoMenuStageBuilder.BuildTitle), and down here there
        /// is nothing to crowd: "Back" says plainly what an arrow only implies.
        ///
        /// Both character screens get it. The create screen is where the room was needed, but
        /// a player walks select -> create -> select, and a Back button that jumps corner to
        /// corner between the two reads as two different buttons.
        /// </summary>
        internal static void PlaceBackButton(Transform screen)
        {
            if (screen == null)
                return;
            Transform button = screen.Find(BackButtonName);
            if (button == null)
                return;
            var rect = (RectTransform)button;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            rect.sizeDelta = BackButtonSize;
            // Left edge on the column's, and centred on the row the screen's own commit
            // button sits in: the two read as one row of controls that way.
            rect.anchoredPosition = new Vector2(ColumnX, BackButtonRow - BackButtonSize.y * 0.5f);

            Transform text = button.Find("Text");
            var component = text != null ? text.GetComponent<Text>() : null;
            if (component == null)
                return;
            component.text = "Back";
            component.fontSize = 14;
            component.alignment = TextAnchor.MiddleCenter;
        }

        /// <summary>
        /// Puts the class description on a panel of its own and fits it to the column.
        ///
        /// **The text object itself is the kit's and is moved, not replaced.** It carries the
        /// `UICharacterClass` the create screen writes the chosen class into, and the screen
        /// holds a reference to that component - build a new Text for it and the description
        /// goes permanently blank. So it is lifted out, a frame is cloned around it exactly
        /// as the slider windows are, and it is put back inside.
        ///
        /// Restyled for where it now sits: the corner it came from was a dark scene, so it
        /// was white 16pt with a drop shadow, right-aligned, and set to **truncate** - at 200
        /// wide that quietly ate the last line rather than wrapping it. On parchment it wants
        /// the opposite of all of that.
        /// </summary>
        private static void BuildDescriptionPanel(Transform create)
        {
            Transform text = create.Find(DescriptionText);
            if (text == null)
            {
                Transform existing = create.Find(DescriptionPanel);
                text = existing != null ? existing.Find(DescriptionText) : null;
            }
            if (text == null)
            {
                Debug.LogWarning($"[{nameof(DemoCreateColumn)}] No {DescriptionText} on the create screen; " +
                                 "the class description keeps whatever it had.");
                return;
            }

            // Out of the old panel before it is dropped, or Clone takes the kit's text with it.
            text.SetParent(create, false);
            GameObject panel = Clone(create, "Window--Beard", DescriptionPanel, string.Empty, DescriptionHeight);
            if (panel == null)
                return;
            // No title bar: the list it captions is titled "Class" directly above it.
            Transform title = panel.transform.Find("Title");
            if (title != null)
                Object.DestroyImmediate(title.gameObject);

            text.SetParent(panel.transform, false);
            var rect = (RectTransform)text;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(8f, 6f);
            rect.offsetMax = new Vector2(-8f, -6f);

            var component = text.GetComponent<Text>();
            if (component != null)
            {
                component.fontSize = 12;
                component.alignment = TextAnchor.UpperLeft;
                component.horizontalOverflow = HorizontalWrapMode.Wrap;
                component.verticalOverflow = VerticalWrapMode.Overflow;
                // The same brown the title bars are, which is the darkest ink already on this
                // screen and reads on parchment at about five to one.
                component.color = DemoPalette.Header;
            }
            // The shadow was there to lift white text off the scene behind it. Under dark
            // text on a light panel it only muddies the letters.
            var shadow = text.GetComponent<Shadow>();
            if (shadow != null)
                shadow.enabled = false;
        }

        /// <summary>
        /// Clones a window for its frame, throws away the option grid, and returns it ready
        /// for a slider. Any window of the same name left by an earlier run is dropped first,
        /// so this is safe to run again.
        ///
        /// Where it lands is <see cref="Restack"/>'s business, not the caller's - the height
        /// passed here is only the one the table already holds for it.
        /// </summary>
        internal static GameObject Clone(Transform create, string templateName, string windowName,
            string title, float height)
        {
            Transform template = create.Find(templateName);
            if (template == null)
            {
                Debug.LogError($"[{nameof(DemoCreateColumn)}] No {templateName} to take the frame from.");
                return null;
            }

            Transform stale = create.Find(windowName);
            if (stale != null)
                Object.DestroyImmediate(stale.gameObject);

            var window = (GameObject)Object.Instantiate(template.gameObject, create);
            window.name = windowName;
            var rect = (RectTransform)window.transform;
            rect.anchorMin = new Vector2(1f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.sizeDelta = new Vector2(ColumnWidth, height);

            SetTitle(window.transform, title);
            Transform grid = window.transform.Find("Scroll View");
            if (grid != null)
                Object.DestroyImmediate(grid.gameObject);
            return window;
        }

        private static void SetTitle(Transform window, string title)
        {
            if (window == null)
                return;
            Transform text = window.Find("Title/Text");
            if (text == null)
                return;
            var component = text.GetComponent<Text>();
            if (component != null)
                component.text = title;
        }

        /// <summary>
        /// The readout under or beside the slider: which tone, or which height. Stretched
        /// across the window's width and then inset, so it moves with the frame.
        /// </summary>
        internal static Text Label(Transform window, string name, Vector2 offsetMin, Vector2 offsetMax,
            TextAnchor alignment)
        {
            Text source = window.Find("Title/Text") == null ? null : window.Find("Title/Text").GetComponent<Text>();
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(window, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            var text = go.AddComponent<Text>();
            // Borrowed from the window's own title so the new panel cannot drift from the
            // ones beside it; a hand-picked font would.
            if (source != null)
            {
                text.font = source.font;
                text.fontStyle = source.fontStyle;
            }
            // The one thing not borrowed. The title is white because it sits on the bronze
            // bar; these sit on the panel below it, where white on parchment is barely
            // there. Same ink as the class description, which is the other text on this
            // column standing on a panel.
            text.color = DemoPalette.Header;
            text.fontSize = 12;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.raycastTarget = false;
            text.text = string.Empty;
            return text;
        }

        /// <summary>A colour chip, for a ramp whose stops are colours.</summary>
        internal static Image Swatch(Transform window, Vector2 position, Vector2 size)
        {
            var go = new GameObject("Swatch", typeof(RectTransform));
            go.transform.SetParent(window, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var image = go.AddComponent<Image>();
            image.color = Color.white;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>
        /// A plain Unity slider, assembled by hand because there is no prefab of one in the
        /// demo to copy. The range and the whole-number stepping are set again at runtime
        /// from the body's own ramp, not only here, so a change to a ramp needs no rebuild
        /// of the window.
        /// </summary>
        internal static Slider Build(Transform window, string name, Vector2 offsetMin, Vector2 offsetMax,
            Color fill, int steps)
        {
            Sprite background = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");
            Sprite sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            Sprite knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");

            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(window, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;

            var slider = go.AddComponent<Slider>();

            var bg = Child(go.transform, "Background", new Vector2(0f, 0.25f), new Vector2(1f, 0.75f));
            var bgImage = bg.gameObject.AddComponent<Image>();
            bgImage.sprite = background;
            bgImage.type = Image.Type.Sliced;
            bgImage.color = new Color(0.16f, 0.14f, 0.12f, 1f);

            var fillArea = Child(go.transform, "Fill Area", new Vector2(0f, 0.25f), new Vector2(1f, 0.75f));
            fillArea.offsetMin = new Vector2(5f, 0f);
            fillArea.offsetMax = new Vector2(-15f, 0f);
            var fillRect = Child(fillArea, "Fill", new Vector2(0f, 0f), new Vector2(0f, 1f));
            fillRect.sizeDelta = new Vector2(10f, 0f);
            var fillImage = fillRect.gameObject.AddComponent<Image>();
            fillImage.sprite = sprite;
            fillImage.type = Image.Type.Sliced;
            fillImage.color = fill;

            var handleArea = Child(go.transform, "Handle Slide Area", new Vector2(0f, 0f), new Vector2(1f, 1f));
            handleArea.offsetMin = new Vector2(10f, 0f);
            handleArea.offsetMax = new Vector2(-10f, 0f);
            var handle = Child(handleArea, "Handle", new Vector2(0f, 0f), new Vector2(0f, 1f));
            handle.sizeDelta = new Vector2(20f, 0f);
            var handleImage = handle.gameObject.AddComponent<Image>();
            handleImage.sprite = knob;
            handleImage.color = Color.white;

            slider.fillRect = fillRect;
            slider.handleRect = handle;
            slider.targetGraphic = handleImage;
            slider.direction = Slider.Direction.LeftToRight;
            slider.wholeNumbers = true;
            slider.minValue = 1;
            slider.maxValue = steps;
            slider.value = 1;
            return slider;
        }

        private static RectTransform Child(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }

        internal static Transform FindDeep(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name)
                    return t;
            }
            return null;
        }
    }
}
