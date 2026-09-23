using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// The bronze frame, everywhere - and the reason nothing but the minimap had one.
    ///
    /// **Every `Image` in the demo's UI pointed at a deleted sprite.** One guid,
    /// `f72e834682cb1224d8ee12cc882a9420`, referenced 1,847 times across 90 prefabs, and not a
    /// single asset in the project carries it. Unity draws an `Image` whose sprite reference is
    /// *missing* as a plain white quad - the same trap that made the minimap's own `Frame` a white
    /// square and the gauges refuse to fill - so the whole interface rendered as flat, hard-edged
    /// rectangles in the palette colours and nothing else. There was no border to match the minimap
    /// with because there was no sprite at all.
    ///
    /// So this is two passes, and the first is a repair:
    ///
    /// 1. **Surface.** Every missing reference is re-pointed at a generated nine-slice with a
    ///    darker rim. It is near-white, so the colour each `Image` already carries still tints it
    ///    and <see cref="DemoPalette"/> keeps meaning what it meant - the panels stay parchment,
    ///    the headers stay bronze, the red Delete stays red. What changes is that an element now
    ///    has an *edge* instead of ending abruptly.
    /// 2. **Frame.** Windows and the pieces inside them get a bronze border drawn over the top, as
    ///    a child `Image` rather than a baked-in one.
    ///
    /// **Why an overlay child and not a bordered sprite.** A `Sliced` sprite is tinted as a whole,
    /// so baking the bronze into the panel sprite would tint the border with the panel: parchment
    /// windows would get parchment borders, the red close button a red one, and the frame would be
    /// a different colour on every element it touched. A separate child keeps the bronze at exactly
    /// the bronze, whatever it sits on - which is the entire point of the look being copied here,
    /// where one metal edge runs round every panel on screen regardless of what is inside it. It
    /// also leaves the kit's own tinting alone: `Selectable` drives hover and disabled states
    /// through the *canvas renderer*, which multiplies the child too, so buttons still dim on press.
    ///
    /// The frames are named <see cref="FrameName"/> and are destroyed and rebuilt on every run, so
    /// this is idempotent and re-running it after a retune does not stack borders. Nothing else is
    /// created, moved or deleted: a hand-placed child of a window survives this, because the pass
    /// only ever destroys its own frames.
    ///
    /// Runs **after** `Build HUD` and `Build Menu Stage`, which own the colours. This owns the
    /// sprites. The two do not overlap: nothing here writes an `Image.color`, and nothing there
    /// writes an `Image.sprite` except the minimap's, which now comes from here too.
    /// </summary>
    public static class DemoUiSkin
    {
        private const string TextureDir = "Assets/OpenMMORPG/Demo/Textures/UI";
        private const string SurfacePath = TextureDir + "/UISurface.png";
        private const string FramePath = TextureDir + "/UIFrame.png";
        private const string SlotFramePath = TextureDir + "/UIFrameSlot.png";

        /// <summary>
        /// The frame the minimap used to generate for itself, now shared. Deleted by this pass once
        /// everything pointing at it has been moved onto <see cref="FramePath"/> - see
        /// <see cref="Rebind"/>. <see cref="FramePath"/> is generated at the same size and band, so
        /// the minimap's border does not change; it is the same border every window now gets.
        /// </summary>
        private const string LegacyMinimapFrame = "Assets/OpenMMORPG/Demo/Textures/MinimapFrame.png";

        /// <summary>
        /// The reserved name for a generated border. Anything called this is ours, and is destroyed
        /// and rebuilt on each run; nothing else in the prefab is touched.
        /// </summary>
        public const string FrameName = "--DemoFrame";

        /// <summary>
        /// The width of the metal on a window frame, in canvas pixels - the nine-slice band of
        /// <see cref="FramePath"/>, and the distance a window frame is pushed outside its panel so
        /// it does not cover the panel's first row of content. The two have to be the same number,
        /// which is why it is one.
        /// </summary>
        private const int WindowBand = 8;
        private const int SlotBand = 4;

        /// <summary>The prefab trees this pass owns.</summary>
        private static readonly string[] Roots =
        {
            "Assets/OpenMMORPG/Demo/Prefabs/UI",
            "Assets/OpenMMORPG/Demo/Prefabs/GamePlay/RelatesObjects",
        };

        [MenuItem("Open MMORPG/Demo/Skin UI")]
        public static void Build()
        {
            Sprite surface = EnsureSurface();
            Sprite frame = EnsureFrame(FramePath, 64, WindowBand, 2, 2);
            Sprite slotFrame = EnsureFrame(SlotFramePath, 32, SlotBand, 1, 1);
            Sprite legacy = AssetDatabase.LoadAssetAtPath<Sprite>(LegacyMinimapFrame);

            int repaired = 0, windows = 0, slots = 0, rebound = 0, touched = 0;
            int padded = 0, spaced = 0, grown = 0;
            foreach (string path in Prefabs())
            {
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    bool dirty = false;
                    dirty |= Strip(root);
                    // **To a fixed point, not once.** Growing a window resizes containers that
                    // have already been spaced in this same sweep - `GetComponentsInChildren`
                    // walks parents before children, so a stack two levels down is measured
                    // against a window that is about to change height. One pass therefore leaves
                    // a screen very slightly off, and it only settles the *next* time anyone runs
                    // the tool, which is the sort of thing nobody ever connects back to its cause.
                    // Repeating until nothing moves settles it inside the one run instead.
                    for (int pass = 0; pass < 3 && Breathe(root, ref padded, ref spaced, ref grown); ++pass)
                        dirty = true;
                    dirty |= Rebind(root, legacy, frame, ref rebound);
                    dirty |= Repair(root, surface, ref repaired);
                    dirty |= Frame(root, frame, slotFrame, ref windows, ref slots);
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

            RetireLegacyFrame(legacy);
            AssetDatabase.SaveAssets();
            Debug.Log("[" + nameof(DemoUiSkin) + "] Skinned " + touched + " prefabs: " + repaired +
                      " dead sprite references repaired, " + windows + " window frames and " + slots +
                      " slot frames drawn, " + rebound + " images moved off the old minimap frame. " +
                      "Breathing room: " + padded + " panels padded, " + spaced + " row stacks spaced, " +
                      grown + " windows grown to fit.");
        }

        private static IEnumerable<string> Prefabs()
        {
            var seen = new HashSet<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", Roots))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (seen.Add(path))
                    yield return path;
            }
        }

        // ------------------------------------------------------------------
        // The passes
        // ------------------------------------------------------------------

        /// <summary>
        /// Removes every frame a previous run drew, so the pass is idempotent.
        ///
        /// Done as its own sweep rather than per-element, because the classifier can change between
        /// runs: an element that was framed as a slot and is now framed as a window would otherwise
        /// keep the old border underneath the new one.
        /// </summary>
        private static bool Strip(GameObject root)
        {
            var stale = new List<GameObject>();
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == FrameName && !PrefabUtility.IsPartOfPrefabInstance(t.gameObject))
                    stale.Add(t.gameObject);
            }
            foreach (GameObject go in stale)
                Object.DestroyImmediate(go);
            return stale.Count > 0;
        }

        // ------------------------------------------------------------------
        // Breathing room
        // ------------------------------------------------------------------

        /// <summary>
        /// The gap a border needs around it: between two stacked rows, and between a row and the
        /// panel edge. Six, because a control's border is four and a one-pixel sliver of panel
        /// either side of it reads as a mistake rather than as a margin.
        /// </summary>
        private const int RowGap = 6;

        /// <summary>The most a window may be stretched to fit its own rows, as a fraction of its height.</summary>
        private const float MaxGrowth = 0.4f;

        /// <summary>
        /// Opens up the layouts enough for the borders to read as separate things.
        ///
        /// **The kit packs its rows edge to edge.** A label's bottom is the exact y of the field's
        /// top below it, and a row's left edge is the exact x of the panel's. That is survivable
        /// while everything is a flat quad - two abutting rectangles of the same colour read as
        /// one surface - and stops being survivable the moment each row has a border, because now
        /// two lines of metal touch with nothing between them and the panel looks like a stack of
        /// crates. It is also what made the first two framing attempts clip text whichever way the
        /// border was drawn: there was simply nowhere for it to go.
        ///
        /// **Rows move; they are never shrunk.** The obvious fix - take six pixels off each row's
        /// height and let the gaps appear for free - does not survive contact with the prefabs: the
        /// text inside a field is stretch-anchored to it, so a 30px field with a 17px text area
        /// becomes a 24px field with an 11px text area, and 11px cannot hold a 14pt line. Measured
        /// before assuming, because it looks exactly like the kind of change that would be free.
        ///
        /// So a packed panel has to get bigger, and <see cref="Grow"/> does that - capped, and only
        /// for a window whose height is its own to give.
        /// </summary>
        private static bool Breathe(GameObject root, ref int padded, ref int spaced, ref int grown)
        {
            bool dirty = false;
            foreach (RectTransform container in root.GetComponentsInChildren<RectTransform>(true))
            {
                if (PrefabUtility.IsPartOfPrefabInstance(container.gameObject))
                    continue;
                dirty |= Pad(container, ref padded);
                dirty |= Space(container, ref spaced, ref grown);
            }
            return dirty;
        }

        /// <summary>
        /// Pulls a window's contents in off its own edge.
        ///
        /// Only a window, and only the children stretched across it - which is the content block,
        /// and is not the header. A header is full-bleed on purpose: it runs from frame to frame,
        /// and inset by six it would sit in the panel like a label rather than capping it.
        ///
        /// Clamped rather than added, so running the pass twice does not pad twice.
        /// </summary>
        private static bool Pad(RectTransform container, ref int count)
        {
            var panel = container.GetComponent<Image>();
            if (panel == null || panel.name == FrameName || Classify(panel) != Weight.Window)
                return false;

            bool dirty = false;
            foreach (Transform child in container)
            {
                var rect = child as RectTransform;
                if (rect == null || child.name == FrameName)
                    continue;
                var image = child.GetComponent<Image>();
                if (image != null && DemoPalette.SameAny(image.color, DemoPalette.KnownHeaders))
                    continue;
                // **Never narrow a layout group.** Its width is not decoration, it is the input to
                // how many cells fit across it: the hotkey bar is 442 wide holding ten 44.2-unit
                // cells, so taking six off each side left room for nine and the tenth hotkey
                // wrapped onto a second row, below the bar and off the bottom of the screen. Six
                // pixels of margin is never worth reflowing someone's grid for.
                if (child.GetComponent<LayoutGroup>() != null)
                    continue;
                // Only the ones stretched edge to edge; a centred child has its own margins.
                if (rect.anchorMin.x != 0f || rect.anchorMax.x != 1f)
                    continue;
                Vector2 min = rect.offsetMin, max = rect.offsetMax;
                if (min.x >= RowGap && max.x <= -RowGap)
                    continue;
                rect.offsetMin = new Vector2(Mathf.Max(min.x, RowGap), min.y);
                rect.offsetMax = new Vector2(Mathf.Min(max.x, -RowGap), max.y);
                ++count;
                dirty = true;
            }
            return dirty;
        }

        /// <summary>
        /// Puts <see cref="RowGap"/> between the rows of a vertical stack.
        ///
        /// **Deciding what is a stack is most of this.** A row of ten hotkeys and a column of four
        /// form fields look identical to anything that only counts children, and spacing a hotkey
        /// bar vertically would throw its buttons into a heap. Two tests separate them, and both
        /// are needed: the children must not overlap each other in y beyond a few pixels, and the
        /// stack must stand taller than one and a half of its tallest child. A horizontal bar fails
        /// the first outright - every button occupies the same y - and the second as well, because
        /// its height *is* its tallest child's.
        ///
        /// Rows are then laid out from scratch rather than nudged: each one's `anchoredPosition` is
        /// solved for the y it should sit at, which is the only form that copes with this UI's
        /// anchoring. A single form here mixes rows pinned to the top of the panel with rows pinned
        /// to the bottom, so "move everything below this one down by six" moves half of them the
        /// wrong way. Solving for a position does not care which edge a row hangs from.
        ///
        /// Existing gaps that are already wider than <see cref="RowGap"/> are **kept**. They are
        /// the layout saying something - the gap between the password field and the Login button is
        /// the difference between filling a form and submitting it - and flattening every gap to
        /// one value would cost more than the borders gain.
        /// </summary>
        private static bool Space(RectTransform container, ref int spaced, ref int grown)
        {
            var rows = new List<RectTransform>();
            bool framesSomething = false;
            foreach (Transform child in container)
            {
                var rect = child as RectTransform;
                if (rect == null || child.name == FrameName || !child.gameObject.activeSelf)
                    continue;
                // A row placed by a stretch anchor has no single y to solve for; one is enough to
                // make the whole container unsolvable, so leave it entirely alone.
                if (rect.anchorMin.y != rect.anchorMax.y)
                    return false;
                if (child.GetComponentInChildren<Graphic>(true) == null)
                    continue;
                var image = child.GetComponent<Image>();
                // A header means this is a window, and a window's own children are not a row
                // stack - the rows live inside its content block. Reflowing them would centre
                // the whole window's furniture and pull the title bar down off the top edge,
                // which is the one thing on a window that must not move.
                if (image != null && DemoPalette.SameAny(image.color, DemoPalette.KnownHeaders))
                    return false;
                if (image != null && Classify(image) != Weight.None)
                    framesSomething = true;
                rows.Add(rect);
            }
            if (!framesSomething || rows.Count < 2)
                return false;

            rows.Sort((a, b) => Top(b, container).CompareTo(Top(a, container)));

            // **An unmeasured row means hands off the whole container.** A `RectTransform` driven
            // by a layout group reports 0x0 in a loaded prefab, and the "is this a vertical stack"
            // test below is all measurement: with every row zero-tall, the stack is zero tall, the
            // tallest row is zero tall, and `union < tallest * 1.5` compares 0 against 0 and waves
            // it through. The hotkey bar went through it that way - ten slots the pass believed
            // were stacked on top of each other, dutifully spaced six apart, and the tenth ended
            // up outside the bar. Framing can treat an unknown size as "no opinion" and carry on;
            // laying rows out cannot, because it needs the number.
            float tallest = 0f, total = 0f;
            foreach (RectTransform row in rows)
            {
                if (row.rect.height <= 0f)
                    return false;
                tallest = Mathf.Max(tallest, row.rect.height);
                total += row.rect.height;
            }
            float union = Top(rows[0], container) - (Top(rows[rows.Count - 1], container) - rows[rows.Count - 1].rect.height);
            if (union < tallest * 1.5f)
                return false;

            var gaps = new float[rows.Count - 1];
            bool wanted = false;
            float need = total;
            for (int i = 0; i < gaps.Length; ++i)
            {
                float gap = (Top(rows[i], container) - rows[i].rect.height) - Top(rows[i + 1], container);
                // Rows that badly overlap are stacked on purpose - a badge over a slot, a
                // highlight over a row - and are not a list to be spaced out.
                if (gap < -8f)
                    return false;
                gaps[i] = Mathf.Max(gap, RowGap);
                if (gaps[i] > gap + 0.5f)
                    wanted = true;
                need += gaps[i];
            }
            if (!wanted)
                return false;

            float height = container.rect.height;
            if (need > height + 0.5f && Grow(container, need - height, height, ref grown))
                height = container.rect.height;
            if (need > height + 0.5f)
            {
                // Could not find the room. Share out what there is rather than overflowing the
                // panel: a slightly tight gap is a cosmetic miss, a row pushed out of its window
                // is a broken screen.
                float slack = Mathf.Max(0f, height - total);
                float asked = need - total;
                float scale = asked <= 0f ? 0f : slack / asked;
                for (int i = 0; i < gaps.Length; ++i)
                    gaps[i] *= scale;
                need = height;
            }

            float top = container.rect.center.y + (need * 0.5f);
            for (int i = 0; i < rows.Count; ++i)
            {
                Place(rows[i], container, top);
                top -= rows[i].rect.height;
                if (i < gaps.Length)
                    top -= gaps[i];
            }
            ++spaced;
            return true;
        }

        /// <summary>The y of a row's top edge, in its container's local space.</summary>
        private static float Top(RectTransform row, RectTransform container)
        {
            float anchor = container.rect.yMin + (row.anchorMin.y * container.rect.height);
            float bottom = anchor + row.anchoredPosition.y - (row.pivot.y * row.rect.height);
            return bottom + row.rect.height;
        }

        /// <summary>Solves a row's <c>anchoredPosition</c> for the top edge it should sit at.</summary>
        private static void Place(RectTransform row, RectTransform container, float top)
        {
            float anchor = container.rect.yMin + (row.anchorMin.y * container.rect.height);
            float bottom = top - row.rect.height;
            float wanted = bottom + (row.pivot.y * row.rect.height) - anchor;
            if (Mathf.Abs(wanted - row.anchoredPosition.y) > 0.01f)
                row.anchoredPosition = new Vector2(row.anchoredPosition.x, wanted);
        }

        /// <summary>
        /// Stretches the window a packed stack lives in, so its rows have somewhere to go.
        ///
        /// Capped at <see cref="MaxGrowth"/>, because a window that needs half again its height to
        /// hold its own contents is not crowded, it is something this pass has misread - a list
        /// that scrolls, a stack of overlapping cards - and quietly doubling it would be worse than
        /// leaving the gaps tight. Refuses a window that cannot give the height: one anchored by
        /// stretch takes its size from its parent, and writing `sizeDelta` on it changes its inset
        /// rather than its height.
        ///
        /// **A grow that does not reach the container is undone.** Growing a window only helps if
        /// the stack's own container stretches with it, and several do not - a fixed-height block
        /// inside a window keeps its height however tall the window gets. Left in place, that is
        /// the one shape in this pass that could run away: the rows would still be short of their
        /// gap next run, so the next run would grow the window again, and the run after that, for
        /// as long as anyone kept pressing the menu item. Measuring the container afterwards and
        /// putting the height back is what makes a second run a no-op instead of a ratchet.
        /// </summary>
        private static bool Grow(RectTransform container, float extra, float before, ref int count)
        {
            for (Transform t = container; t != null; t = t.parent)
            {
                var image = t.GetComponent<Image>();
                if (image == null || image.name == FrameName || Classify(image) != Weight.Window)
                    continue;
                var window = (RectTransform)t;
                if (window.anchorMin.y != window.anchorMax.y)
                    return false;
                if (extra > window.rect.height * MaxGrowth)
                    return false;
                Vector2 was = window.sizeDelta;
                Vector2 wasAt = window.anchoredPosition;
                float added = Mathf.Ceil(extra);
                window.sizeDelta = new Vector2(was.x, was.y + added);
                // **Downward, from the top edge.** A centred window grows both ways by default,
                // which put the login panel four units *inside* the title band above it - the
                // home canvas is 600 units tall whatever the screen is, so there was never much
                // room up there to take. Growing down is also the right default in general: a
                // window's header is at its top, that edge is what everything else on the screen
                // is positioned against, and there is always more room below a dialog than above
                // one. Moving the pivot down by the share of the height that lies above it holds
                // the top still at any pivot.
                window.anchoredPosition = new Vector2(wasAt.x, wasAt.y - (added * (1f - window.pivot.y)));
                if (container.rect.height < before + extra - 0.5f)
                {
                    window.sizeDelta = was;
                    window.anchoredPosition = wasAt;
                    return false;
                }
                ++count;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Moves anything still drawing the minimap's private border onto the shared one.
        ///
        /// Without this, deleting <see cref="LegacyMinimapFrame"/> would leave the minimap with a
        /// *missing* sprite reference, which is a white square over the map - the exact fault this
        /// whole pass exists to clear.
        ///
        /// **The one pass that does reach into nested prefab instances.** The minimap's frame is an
        /// override: `DemoHudBuilder` writes the sprite onto the canvas rather than onto whatever
        /// prefab the minimap came from, so skipping instances the way <see cref="Repair"/> and
        /// <see cref="Frame"/> do leaves exactly this one image behind - which is enough to keep
        /// the old asset alive and the retirement blocked. Writing to an override is safe where
        /// adding a child is not: the value is already overridden, so nothing new diverges.
        /// </summary>
        private static bool Rebind(GameObject root, Sprite legacy, Sprite frame, ref int count)
        {
            if (legacy == null || frame == null)
                return false;
            bool dirty = false;
            foreach (Image image in root.GetComponentsInChildren<Image>(true))
            {
                if (image.sprite != legacy)
                    continue;
                image.sprite = frame;
                image.type = Image.Type.Sliced;
                ++count;
                dirty = true;
            }
            return dirty;
        }

        /// <summary>
        /// Re-points every *missing* sprite reference at the generated surface.
        ///
        /// **A missing reference and a deliberately empty one look identical from C#** - both give
        /// `image.sprite == null`. The difference is in the serialized field: a missing asset still
        /// holds an instance id that no longer resolves, where a field that was cleared on purpose
        /// holds zero. `SerializedProperty.objectReferenceInstanceIDValue` is the only thing that
        /// separates them, and getting it wrong matters: 622 of these `Image`s are *meant* to be
        /// blank - item icons, skill icons, the slot that is empty until you put something in it -
        /// and giving those a sprite would paint a panel into every empty inventory square.
        ///
        /// `Filled` images keep their type. They are the gauges, they already carry `UIGageFill`,
        /// and switching one to `Sliced` would stop it filling.
        /// </summary>
        private static bool Repair(GameObject root, Sprite surface, ref int count)
        {
            if (surface == null)
                return false;
            bool dirty = false;
            foreach (Image image in root.GetComponentsInChildren<Image>(true))
            {
                if (PrefabUtility.IsPartOfPrefabInstance(image.gameObject))
                    continue;
                var serialized = new SerializedObject(image);
                SerializedProperty sprite = serialized.FindProperty("m_Sprite");
                if (sprite.objectReferenceValue != null || sprite.objectReferenceInstanceIDValue == 0)
                    continue;
                image.sprite = surface;
                if (image.type != Image.Type.Filled)
                    image.type = Image.Type.Sliced;
                ++count;
                dirty = true;
            }
            return dirty;
        }

        /// <summary>
        /// Draws the borders.
        ///
        /// Two weights, and the split is about what the border is *for*. A window's frame says
        /// where the window ends against the game behind it, so it is the heavy one - the minimap's
        /// own, at the same size and band it has always been. Everything inside a window is
        /// separating itself from the panel it sits on rather than from the world, so it takes the
        /// thin one; a slot grid bordered at the window's weight reads as ten small windows.
        /// </summary>
        private static bool Frame(GameObject root, Sprite window, Sprite slot, ref int windows, ref int slots)
        {
            bool dirty = false;
            foreach (Image image in root.GetComponentsInChildren<Image>(true))
            {
                if (PrefabUtility.IsPartOfPrefabInstance(image.gameObject) || image.name == FrameName)
                    continue;
                Weight weight = Classify(image);
                if (weight == Weight.None)
                    continue;
                Draw(image.transform, weight == Weight.Window ? window : slot, Outset(image, weight));
                if (weight == Weight.Window)
                    ++windows;
                else
                    ++slots;
                dirty = true;
            }
            return dirty;
        }

        private enum Weight { None, Window, Slot }

        /// <summary>
        /// How far outside an element its border is drawn, per axis.
        ///
        /// **A control's border grows sideways and never upwards**, and both halves of that were
        /// learned the hard way, one from each direction.
        ///
        /// Flush first: a button's label is laid out to the button's own edges - "System" and
        /// "Crafting" on the menu bar are within a pixel of theirs - so a border drawn inside the
        /// button clipped the last letter off the narrow ones. Pushing it out fixed that and broke
        /// the login screen instead, because **these forms have no spacing between their rows**:
        /// the username field sits directly under its own label, so four pixels of metal grown
        /// upwards landed squarely on the word "Username" and sheared the bottom off it.
        ///
        /// There is no single number that satisfies both, because they are not the same problem.
        /// A label runs out of room *across* its control, and a form stacks its rows *down* the
        /// panel - so the free space is horizontal and the neighbour is vertical, always, in every
        /// dialog here. Growing on the axis with room and staying flush on the axis with a
        /// neighbour satisfies both without a special case for either. Where the horizontal
        /// neighbour is another button - the menu bar, the hotkey row - the two borders meet and
        /// read as one segmented bar, which is what that row wants to look like anyway.
        ///
        /// Windows grow on both axes: a window has the game behind it on all four sides.
        ///
        /// A slot holding a centred icon gets no outset at all. It has margin of its own, and
        /// slots come in grids where growing every border would run neighbouring squares together.
        /// The `Selectable` separates the two: it is on exactly the things you click and read a
        /// label on, and on none of the things you only look at.
        /// </summary>
        private static Vector2 Outset(Image image, Weight weight)
        {
            if (weight == Weight.Window)
                return new Vector2(WindowBand, WindowBand);
            return image.GetComponent<Selectable>() != null ? new Vector2(SlotBand, 0f) : Vector2.zero;
        }

        /// <summary>
        /// What weight of border - if any - an element gets.
        ///
        /// Matched on **colour and role rather than name**, because the kit's names are not a
        /// taxonomy: the panel of a dialog is called `Window` in twenty prefabs and is the dialog
        /// root itself in six more, and `Info`, `Buttons` and `Content` are all panels too. The
        /// palette is the reliable signal - <see cref="DemoPalette.Panel"/> means "this is
        /// a window body" everywhere in the demo, because a builder put it there.
        ///
        /// The heavy weight goes on a panel that **nothing else already frames** - not on a panel
        /// over some size. Size was the first rule and it was wrong in the one place it mattered:
        /// the player frame is 270 wide and 100 tall, so any height threshold generous enough to
        /// call the inventory a window called the player frame a slot, and the player frame sits
        /// directly opposite the minimap. Two HUD corners, two different borders. Depth in the
        /// hierarchy is the thing actually being asked about - is this a surface laid on the game,
        /// or a surface laid on another surface - and it answers correctly at every size.
        ///
        /// Deliberately **not** framed:
        /// - Anything near-transparent. A frame on an invisible spacer is a frame floating in
        ///   mid-air; several of the kit's layout groups are exactly that.
        /// - Window headers. The window's own frame already runs round the outside of the header;
        ///   a second border inside it boxes the title off from the panel it belongs to.
        /// - Anything smaller than the border it would get, which would be solid border.
        /// </summary>
        private static Weight Classify(Image image)
        {
            Color c = image.color;
            if (c.a < 0.35f)
                return Weight.None;

            // **A zero rect means "not laid out", not "tiny".** A `RectTransform` driven by a
            // layout group has no size at all inside `LoadPrefabContents` - nothing has run a
            // layout pass over a prefab that is not under a Canvas - so a plain `size.x < 12`
            // guard throws away every element in a `HorizontalLayoutGroup`. It did: the chat's six
            // tabs measured 0x0 and were the one row on the HUD that came back unframed, while the
            // panel they sit on, which is not layout-driven, framed correctly. Only trust a
            // measurement that exists.
            var rect = image.transform as RectTransform;
            Vector2 size = rect == null ? Vector2.zero : rect.rect.size;
            bool measured = size.x > 0f && size.y > 0f;
            if (measured && (size.x < 12f || size.y < 12f))
                return Weight.None;

            // Buttons and toggles first, whatever colour they ended up. They are the one thing on
            // screen that has to read as pressable, an edge is what says so, and they are never a
            // window however isolated they sit - a lone parchment button on the login screen has
            // no parchment ancestor and would otherwise be framed as one.
            var selectable = image.GetComponent<Selectable>();
            if (selectable != null && selectable.targetGraphic == image)
                return Weight.Slot;

            // Headers: a "Title" in the header bronze. The window frame covers them.
            if (DemoPalette.SameAny(c, DemoPalette.KnownHeaders))
                return Weight.None;

            // Window bodies: the panel parchment, with nothing above them already framed as one.
            // A parchment strip *inside* a parchment window - the kit's button rows - is a
            // division of the panel, not a second window.
            if (DemoPalette.SameAny(c, DemoPalette.KnownPanels))
            {
                bool nested = image.transform.parent != null && InsidePanel(image.transform.parent);
                bool small = measured && (size.x < 48f || size.y < 48f);
                return nested || small ? Weight.Slot : Weight.Window;
            }

            // Wells, slots and gauge backings: anything a shade down from the panel, plus the
            // template greys the repaint passes never claimed.
            if (DemoPalette.SameAny(c, DemoPalette.KnownFields)
                || DemoPalette.Same(c, DemoPalette.TemplateFieldGrey)
                || DemoPalette.Same(c, TemplateSlotGrey))
                return Weight.Slot;

            // The chat and the quest tracker, which are dark on purpose and stay dark - a chat log
            // with a parchment backing would be the one panel you cannot read the world through.
            // They still get an edge, or they are the two flat rectangles on an otherwise framed
            // screen. Thin: they are backdrops for text, not windows you opened.
            if (DemoPalette.Same(c, TemplateChatPanel) || DemoPalette.Same(c, TemplateTabGrey))
                return Weight.Slot;

            return Weight.None;
        }

        /// <summary>The template's slot grey, a step below <see cref="DemoPalette.Field"/>.</summary>
        private static readonly Color TemplateSlotGrey = new Color(0.741f, 0.741f, 0.741f);

        /// <summary>The chat and quest-tracker backing, and the unselected tabs above them.</summary>
        private static readonly Color TemplateChatPanel = new Color(0.118f, 0.118f, 0.118f);
        private static readonly Color TemplateTabGrey = new Color(0.259f, 0.259f, 0.259f);

        /// <summary>True if any ancestor is itself a parchment panel.</summary>
        private static bool InsidePanel(Transform t)
        {
            while (t != null)
            {
                var image = t.GetComponent<Image>();
                if (image != null && DemoPalette.SameAny(image.color, DemoPalette.KnownPanels))
                    return true;
                t = t.parent;
            }
            return false;
        }

        /// <summary>
        /// Hangs a border on an element, `outset` pixels outside its bounds - see
        /// <see cref="Outset"/> for why that is a per-axis number rather than one.
        ///
        /// **Window frames sit outside the panel.** A window's frame is eight pixels of metal, and
        /// every one of these layouts puts its first label hard against the panel edge - so a flush
        /// frame ate the `Lv:`, the `HP`, the `MP` and the `STA` off the player frame the moment it
        /// was drawn, and would have eaten the first character of something in every dialog.
        /// Pushing it out by exactly the band leaves the panel's usable area untouched and reads
        /// the way the metal on a real frame does, around the parchment rather than over it.
        ///
        /// **Last sibling on purpose**: uGUI draws in hierarchy order, so a frame added anywhere
        /// else would be painted over by the element's own contents - which on a window means every
        /// panel, slot and button inside it.
        ///
        /// Not a raycast target, or the frame would eat the clicks meant for whatever it surrounds.
        /// The minimap's frame has always been set this way and it is the same reason.
        /// </summary>
        private static void Draw(Transform parent, Sprite sprite, Vector2 outset)
        {
            if (sprite == null)
                return;
            var go = new GameObject(FrameName, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.transform.SetAsLastSibling();

            // **`ignoreLayout`, or a layout group treats the frame as one more cell.** Roughly
            // sixty of these land inside a `HorizontalLayoutGroup` or a `VerticalLayoutGroup`, and
            // a layout group positions and sizes every child it has: the frame stopped being an
            // overlay, took a slot in the row, and pushed the real contents along to make room.
            // The menu bar is where it showed - seven buttons and then an empty parchment square
            // where the border should have been wrapped round the bar. Ignored, the frame keeps
            // its own stretched anchors and the group lays out only the things it is meant to.
            go.GetComponent<LayoutElement>().ignoreLayout = true;

            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = -outset;
            rect.offsetMax = outset;
            rect.localScale = Vector3.one;

            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            // Sliced, or the nine-slice borders are ignored and the frame is stretched into a smear.
            image.type = Image.Type.Sliced;
            image.color = Color.white;
            image.raycastTarget = false;
        }

        // ------------------------------------------------------------------
        // The generated art
        // ------------------------------------------------------------------

        /// <summary>
        /// The surface every repaired `Image` gets: white, with a two-step darker rim.
        ///
        /// **White on purpose.** Every colour in this UI arrives as an `Image` tint, so a surface
        /// with any hue of its own would multiply into all of them and shift the whole palette. At
        /// white the tint passes through untouched and the rim becomes a proportionally darker
        /// shade of whatever the element already was - a parchment panel gets a parchment-brown
        /// edge, the red close button a darker red one - which is how a single sprite gives three
        /// dozen differently-coloured elements a consistent edge.
        /// </summary>
        private static Sprite EnsureSurface()
        {
            const int size = 16;
            const int band = 3;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; ++y)
            {
                for (int x = 0; x < size; ++x)
                {
                    int edge = Mathf.Min(Mathf.Min(x, size - 1 - x), Mathf.Min(y, size - 1 - y));
                    byte v = edge == 0 ? (byte)178 : edge == 1 ? (byte)220 : (byte)255;
                    pixels[(y * size) + x] = new Color32(v, v, v, 255);
                }
            }
            return Write(SurfacePath, size, pixels, band);
        }

        /// <summary>
        /// A square bronze border: `outer` dark pixels, a bronze band, `inner` lighter pixels, and
        /// nothing in the middle.
        ///
        /// Lifted verbatim from the minimap's own generator, which is the point - at size 64 and
        /// band 8 this produces the border the minimap already had, so the one that was liked is
        /// the one every window now gets, rather than a new thing that resembles it.
        ///
        /// Generated rather than drawn because it is four straight edges, and because a checked-in
        /// PNG is one more thing that can go missing - which is precisely what happened to the
        /// 1,847 references this pass is repairing. Written only when the bytes would change, so a
        /// build does not reimport it every time; early-returning on "the file exists" instead
        /// would mean editing the border here silently does nothing.
        /// </summary>
        private static Sprite EnsureFrame(string path, int size, int band, int outer, int inner)
        {
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; ++y)
            {
                for (int x = 0; x < size; ++x)
                {
                    int edge = Mathf.Min(Mathf.Min(x, size - 1 - x), Mathf.Min(y, size - 1 - y));
                    Color32 c;
                    if (edge >= band)
                        c = new Color32(0, 0, 0, 0);
                    else if (edge < outer)
                        c = DemoPalette.FrameDark;
                    else if (edge < band - inner)
                        c = DemoPalette.FrameMid;
                    else
                        c = DemoPalette.FrameLit;
                    pixels[(y * size) + x] = c;
                }
            }
            return Write(path, size, pixels, band);
        }

        private static Sprite Write(string path, int size, Color32[] pixels, int band)
        {
            DemoItemBuilder.EnsureFolder(TextureDir);
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply();
            byte[] png = texture.EncodeToPNG();
            Object.DestroyImmediate(texture);

            if (!File.Exists(path) || !SameBytes(File.ReadAllBytes(path), png))
            {
                File.WriteAllBytes(path, png);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.mipmapEnabled = false;
                importer.filterMode = FilterMode.Point;
                importer.wrapMode = TextureWrapMode.Clamp;
                // The nine-slice: the middle stretches, the band does not.
                importer.spriteBorder = new Vector4(band, band, band, band);
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        /// <summary>
        /// Deletes the minimap's private copy of the frame, once nothing draws it any more.
        ///
        /// Guarded rather than unconditional: if <see cref="Rebind"/> missed something - a prefab
        /// outside <see cref="Roots"/>, a scene object - deleting the asset would turn that into a
        /// white square, so the pass says what it found and leaves the file alone.
        /// </summary>
        private static void RetireLegacyFrame(Sprite legacy)
        {
            if (legacy == null)
                return;
            string legacyGuid = AssetDatabase.AssetPathToGUID(LegacyMinimapFrame);
            var holders = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab t:Scene", new[] { "Assets/OpenMMORPG/Demo" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                foreach (string dependency in AssetDatabase.GetDependencies(path, false))
                {
                    if (AssetDatabase.AssetPathToGUID(dependency) == legacyGuid)
                        holders.Add(path);
                }
            }
            if (holders.Count > 0)
            {
                Debug.LogWarning("[" + nameof(DemoUiSkin) + "] Left \"" + LegacyMinimapFrame +
                                 "\" in place: " + holders.Count + " asset(s) still draw it, first \"" +
                                 holders[0] + "\".");
                return;
            }
            AssetDatabase.DeleteAsset(LegacyMinimapFrame);
            Debug.Log("[" + nameof(DemoUiSkin) + "] Retired \"" + LegacyMinimapFrame +
                      "\": the minimap and the windows share \"" + FramePath + "\" now.");
        }

        private static bool SameBytes(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
                return false;
            for (int i = 0; i < left.Length; ++i)
            {
                if (left[i] != right[i])
                    return false;
            }
            return true;
        }
    }
}
