using UnityEngine;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// The demo's UI palette, in one place because four builders paint with it: the menu stage
    /// owns the home screens, the HUD builder owns everything in game, the skin pass generates the
    /// borders, and two column builders write headers of their own. A colour that lives in only
    /// one of them drifts the first time somebody retunes it.
    ///
    /// **This is a kit demo, so the theme is a switch rather than a decision.** Whoever builds on
    /// this will want their own look, and the first thing an opinionated one invites is "how do I
    /// get rid of it". Everything that makes the UI *work* - that sprites exist at all, the
    /// nine-slice frames, the spacing, the window hierarchy - is independent of the six values
    /// below. Change <see cref="Active"/>, re-run the three builders, and the entire interface
    /// including the minimap moves together.
    ///
    /// The shipped default is <see cref="Theme.Stone"/>: a warm neutral that reads as a starting
    /// point rather than as art direction. <see cref="Theme.Bronze"/> is the fantasy set it
    /// replaced, kept because it is a worked example of what retheming looks like.
    /// </summary>
    internal static class DemoPalette
    {
        internal enum Theme
        {
            /// <summary>Warm greys. The default: neutral without being cold against the demo's sunset.</summary>
            Stone,

            /// <summary>Bronze and parchment, modelled on World of Warcraft.</summary>
            Bronze,
        }

        /// <summary>
        /// The theme the demo ships in. One line, and it moves every panel, slot, button, tab,
        /// gauge and the minimap's border with it.
        /// </summary>
        internal const Theme Active = Theme.Stone;

        private static bool Stone { get { return Active == Theme.Stone; } }

        // ------------------------------------------------------------------
        // The theme
        // ------------------------------------------------------------------

        /// <summary>
        /// The bar across the top of every window, at roughly a third lightness.
        ///
        /// **It is the frame's middle step**, deliberately: a window's border and its title bar
        /// are then visibly the same material, which is most of why the two read as one object
        /// rather than as a panel with a coloured strip on it.
        ///
        /// Dark enough for white labels either way - stone measures about 7:1, bronze about 8:1.
        /// The first bronze attempt was 25% lightness and became the darkest thing in the frame
        /// when every timber in the scene is 45-50%, which read as a hole punched in the panel.
        /// </summary>
        internal static Color Header { get { return Stone ? StoneFrameMidF : BronzeFrameMidF; } }

        /// <summary>
        /// Panel bodies. Deliberately **light**: the labels on them are near-black, and going dark
        /// would mean restyling every label in every dialog to keep contrast.
        /// </summary>
        internal static Color Panel
        {
            get { return Stone ? new Color(0.925f, 0.915f, 0.900f) : new Color(0.94f, 0.90f, 0.81f); }
        }

        /// <summary>Input wells and slots, a shade down from the panel so they still read as recessed.</summary>
        internal static Color Field
        {
            get { return Stone ? new Color(0.845f, 0.835f, 0.820f) : new Color(0.86f, 0.82f, 0.74f); }
        }

        /// <summary>
        /// The three steps of the border, outermost first: a dark lip, the metal, a lit inner line.
        /// <see cref="DemoUiSkin"/> generates every frame in the UI from these - the minimap's
        /// included, which is the point. They used to be the minimap's alone, which is exactly why
        /// it was the only thing on screen with a border.
        ///
        /// `Color32` because they are written straight into texture pixels, where a float round
        /// trip would only cost the exact byte the border was tuned at.
        /// </summary>
        internal static Color32 FrameDark { get { return Stone ? new Color32(42, 40, 37, 255) : new Color32(46, 30, 20, 255); } }
        internal static Color32 FrameMid { get { return Stone ? StoneFrameMid : BronzeFrameMid; } }
        internal static Color32 FrameLit { get { return Stone ? new Color32(138, 133, 125, 255) : new Color32(163, 118, 84, 255); } }

        private static readonly Color32 StoneFrameMid = new Color32(95, 91, 85, 255);
        private static readonly Color32 BronzeFrameMid = new Color32(120, 78, 52, 255);
        private static readonly Color StoneFrameMidF = new Color(95f / 255f, 91f / 255f, 85f / 255f);
        private static readonly Color BronzeFrameMidF = new Color(120f / 255f, 78f / 255f, 52f / 255f);

        // ------------------------------------------------------------------
        // Every value the demo has ever painted, so a retheme can find them
        // ------------------------------------------------------------------

        /// <summary>
        /// **Colour matching works exactly once.** A repaint that only knows the template's
        /// placeholder finds nothing to do the second time it runs, because there is no
        /// placeholder left - which is how the first bronze pass silently stopped responding to
        /// edits. Switching <see cref="Active"/> has the same problem one step further out: the
        /// panels are no longer white, they are the *old theme's* parchment.
        ///
        /// So every theme's values stay listed here, and the repaint passes move any of them onto
        /// the active one. That is what makes <see cref="Active"/> a switch you can flip twice.
        /// </summary>
        internal static readonly Color[] KnownHeaders = { BronzeFrameMidF, StoneFrameMidF };

        internal static readonly Color[] KnownPanels =
        {
            new Color(0.94f, 0.90f, 0.81f),
            new Color(0.925f, 0.915f, 0.900f),
        };

        internal static readonly Color[] KnownFields =
        {
            new Color(0.86f, 0.82f, 0.74f),
            new Color(0.845f, 0.835f, 0.820f),
        };

        // ------------------------------------------------------------------
        // What the kit's template shipped
        // ------------------------------------------------------------------

        /// <summary>The placeholder the template shipped every window header in.</summary>
        internal static readonly Color PlaceholderMagenta = new Color(0.710f, 0f, 1f);

        /// <summary>The template's field grey, a shade under its white panels.</summary>
        internal static readonly Color TemplateFieldGrey = new Color(0.878f, 0.878f, 0.878f);

        /// <summary>The kit's two greens: the ordinary action, and the one that commits.</summary>
        internal static readonly Color KitGreen = new Color(0.333f, 0.545f, 0.184f);
        internal static readonly Color KitDeepGreen = new Color(0.200f, 0.412f, 0.118f);

        /// <summary>
        /// The same two steps, moved onto the logo's hue - but **only on the home screens**.
        ///
        /// The kit's green is hue 95, the only thing in the frame anywhere near it, at 46%
        /// saturation against verges knocked down to 14%: the button was greener than the grass.
        /// The logo is hue 184 and nothing else supported it, and a colour used once reads as
        /// foreign where a colour used twice reads as the game's.
        ///
        /// **Darker than the logo on purpose**: it is 52% lightness and these labels are white,
        /// which would be 2.5:1. At 31% and 22% they measure 5.3:1 off the render.
        ///
        /// In game the greens stay green - see <see cref="DemoHudBuilder"/>. There they really do
        /// carry meaning next to a red Delete, which is the distinction the home screens did not
        /// have. Neither is themed: they are the call to action and the logo, not the furniture.
        /// </summary>
        internal static readonly Color ActionTeal = new Color(0.141f, 0.447f, 0.482f);
        internal static readonly Color CommitTeal = new Color(0.094f, 0.322f, 0.349f);

        /// <summary>The minimap's backing, so an unfilled corner reads as night rather than paper.</summary>
        internal static readonly Color MinimapBacking = new Color(0.055f, 0.065f, 0.085f, 0.92f);

        /// <summary>True if two colours match closely enough to be the same authored value.</summary>
        internal static bool Same(Color a, Color b)
        {
            return Mathf.Abs(a.r - b.r) < 0.02f
                && Mathf.Abs(a.g - b.g) < 0.02f
                && Mathf.Abs(a.b - b.b) < 0.02f;
        }

        /// <summary>True if a colour matches any value in a set - see <see cref="KnownHeaders"/>.</summary>
        internal static bool SameAny(Color a, Color[] set)
        {
            for (int i = 0; i < set.Length; ++i)
            {
                if (Same(a, set[i]))
                    return true;
            }
            return false;
        }
    }
}
