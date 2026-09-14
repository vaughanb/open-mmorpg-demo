using System.Collections.Generic;
using MultiplayerARPG.GameData.Model.Playables;
using UnityEditor;
using UnityEngine;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// What a player gets to choose about their looks, and how that is handed to the kit.
    ///
    /// The kit's body-part system (PlayerCharacterBodyPartComponent on the entity,
    /// UIBodyPartManager on the create screen) treats a choice as a fake piece of
    /// equipment: every option is an EquipmentModel aimed at a socket, and every colour is
    /// a material plus a tint applied to whatever that option shows. The demo uses the
    /// socket's "instantiated object groups" mode, where the alternatives are already in
    /// the model prefab and picking an option switches one group on. Nothing is
    /// instantiated and no bones are rebound at runtime, and the prefab shows its default
    /// look in the editor, so the existing outfit audit still sees what a new character
    /// sees.
    ///
    /// DemoCharacterBuilder grafts the alternatives and builds the groups;
    /// <see cref="AddComponents"/> here reads them back off the finished model and writes
    /// the matching options onto the entity. The tables below are the only place either
    /// side needs to agree on.
    /// </summary>
    public static class DemoBodyPartBuilder
    {
        /// <summary>Socket the hairstyles are switched in. Its holder is what head gear replaces.</summary>
        public const string SocketHair = "Hair";
        /// <summary>Socket the beards are switched in. A hood is an open cowl, so a beard stays visible under it.</summary>
        public const string SocketBeard = "Beard";

        /// <summary>
        /// The object every hair option switches on alongside the hair. The brows are cut
        /// from the same greyscale sheet as the hair, so this is what keeps them the same
        /// colour - and keeps them on a bald head.
        /// </summary>
        public const string EyebrowsName = "Eyebrows";

        public struct Style
        {
            public string Model;
            public string Title;
            public string[] Genders;

            public bool Fits(string gender)
            {
                return System.Array.IndexOf(Genders, gender) >= 0;
            }
        }

        /// <summary>
        /// Every hairstyle the pack ships, and whose head it fits. Decided by rendering each
        /// one on both heads: the female skull is the smaller, so Parted and Buzzed float
        /// a finger's width above it, and BuzzedFemale sits inside the male one. The first
        /// entry that fits a gender is that gender's default, and "Bald" is added last.
        /// </summary>
        public static readonly Style[] HairStyles =
        {
            new Style { Model = "Hair_SimpleParted", Title = "Parted", Genders = new[] { "Male" } },
            new Style { Model = "Hair_Long", Title = "Long", Genders = new[] { "Male", "Female" } },
            new Style { Model = "Hair_Buzzed", Title = "Buzzed", Genders = new[] { "Male" } },
            new Style { Model = "Hair_BuzzedFemale", Title = "Buzzed", Genders = new[] { "Female" } },
            new Style { Model = "Hair_Buns", Title = "Buns", Genders = new[] { "Male", "Female" } },
        };

        /// <summary>
        /// The pack's one beard. It is shaped to the male jaw and sits inside the female
        /// face, so the female entity gets no beard part at all - the create screen hides
        /// the beard windows for a body that has none.
        /// </summary>
        public static readonly Style[] BeardStyles =
        {
            new Style { Model = "Hair_Beard", Title = "Beard", Genders = new[] { "Male" } },
        };

        public struct Shade
        {
            public string Title;
            /// <summary>
            /// Multiplied over the pack's greyscale hair sheet, which averages about 0.56 grey.
            /// Anything above white is allowed: a tint over 1 is how a mid-grey sheet becomes
            /// blonde or white, and the shader clamps only where the sheet's own highlights
            /// are brightest.
            /// </summary>
            public Color Tint;
        }

        /// <summary>
        /// The colours on offer, for hair and beard alike. Chestnut is first because it is
        /// the tint every character wore before there was a choice, so index 0 - which is
        /// what a character with nothing saved gets - looks exactly as it did.
        /// </summary>
        public static readonly Shade[] Shades =
        {
            new Shade { Title = "Chestnut", Tint = new Color(0.42f, 0.27f, 0.16f) },
            new Shade { Title = "Dark Brown", Tint = new Color(0.24f, 0.15f, 0.10f) },
            new Shade { Title = "Black", Tint = new Color(0.08f, 0.07f, 0.07f) },
            new Shade { Title = "Auburn", Tint = new Color(0.55f, 0.20f, 0.10f) },
            new Shade { Title = "Ginger", Tint = new Color(0.85f, 0.42f, 0.16f) },
            new Shade { Title = "Blonde", Tint = new Color(1.60f, 1.35f, 0.85f) },
            new Shade { Title = "Grey", Tint = new Color(0.90f, 0.90f, 0.90f) },
            new Shade { Title = "White", Tint = new Color(1.70f, 1.70f, 1.70f) },
        };

        /// <summary>Average brightness of the pack's hair sheets, measured; the swatches are the tints over it.</summary>
        private const float HairSheetGrey = 0.56f;

        /// <summary>
        /// Writes one PlayerCharacterBodyPartComponent per wardrobe socket onto the entity
        /// root, with an option per group the model carries and every shade under each.
        ///
        /// The root and not the model: the component reaches its entity with GetComponent on
        /// its own object, and SetModel writes the choice into that entity's PublicInts, so
        /// anywhere else it would throw the first time a player clicked a hairstyle. The
        /// kit's UIBodyPartManager finds it there; SetupModelBodyParts does not, which
        /// DemoSavedBodyParts is for.
        ///
        /// Options carry no references into the model - only socket names, group indices
        /// and materials - so the same entity could point at a different model later.
        /// </summary>
        public static void AddComponents(GameObject entity, GameObject modelInstance)
        {
            var model = modelInstance.GetComponent<PlayableCharacterModel>();
            if (model == null)
            {
                Debug.LogError($"[{nameof(DemoBodyPartBuilder)}] \"{modelInstance.name}\" has no character model to read body parts from.");
                return;
            }

            // Rebuildable: the template carries none, but a rerun over a built entity would.
            foreach (PlayerCharacterBodyPartComponent stale in entity.GetComponents<PlayerCharacterBodyPartComponent>())
                Object.DestroyImmediate(stale);

            Sprite swatch = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            int written = 0;
            foreach (EquipmentContainer container in model.EquipmentContainers)
            {
                if (container.equipSocket != SocketHair && container.equipSocket != SocketBeard)
                    continue;
                if (container.instantiatedObjectGroups == null || container.instantiatedObjectGroups.Length == 0)
                    continue;

                var part = entity.AddComponent<PlayerCharacterBodyPartComponent>();
                // These two ids are what the choice is saved under, hashed, in the
                // character's PublicInts. The create screen's UIBodyPartManager pairs itself
                // with the component by the model id.
                part.modelSettingId = container.equipSocket.ToUpperInvariant();
                part.colorSettingId = part.modelSettingId + "_COLOR";
                for (int i = 0; i < container.instantiatedObjectGroups.Length; ++i)
                    part.options.Add(Option(container, i, swatch));
                ++written;
            }
            if (written == 0)
                Debug.LogWarning($"[{nameof(DemoBodyPartBuilder)}] \"{modelInstance.name}\" has no {SocketHair} or {SocketBeard} container; was it built as a selectable variant?");
        }

        private static PlayerCharacterBodyPartComponent.ModelOption Option(EquipmentContainer container, int index, Sprite swatch)
        {
            EquipmentInstantiatedObjectGroup group = container.instantiatedObjectGroups[index];
            GameObject[] objects = group.instantiatedObjects ?? new GameObject[0];

            var option = new PlayerCharacterBodyPartComponent.ModelOption
            {
                defaultTitle = Title(container.equipSocket, objects),
                models = new[]
                {
                    new EquipmentModel
                    {
                        equipSocket = container.equipSocket,
                        useInstantiatedObject = true,
                        instantiatedObjectIndex = index,
                    },
                },
                colors = new PlayerCharacterBodyPartComponent.ColorOption[Shades.Length],
            };

            for (int s = 0; s < Shades.Length; ++s)
            {
                Shade shade = Shades[s];
                // One material group per object the option switches on, in the same order:
                // the kit pairs materialGroups[k] with instantiatedObjects[k]. Each keeps the
                // material the object already wears - the pack's two hair sheets have
                // different layouts, so a mesh cut for one cannot take the other - and the
                // tint is pushed down from the colour option through the two
                // "use upper level" switches.
                var groups = new PlayerCharacterBodyPartComponent.MaterialGroup[objects.Length];
                for (int k = 0; k < objects.Length; ++k)
                {
                    Renderer renderer = objects[k] != null ? objects[k].GetComponent<Renderer>() : null;
                    groups[k] = new PlayerCharacterBodyPartComponent.MaterialGroup
                    {
#if UNITY_EDITOR
                        name = objects[k] != null ? objects[k].name : "(missing)",
#endif
                        materials = renderer != null ? new[] { renderer.sharedMaterial } : new Material[0],
                        properties = new[]
                        {
                            new PlayerCharacterBodyPartComponent.MaterialPropertiesSetting
                            {
                                applyMaterialColor = true,
                                materialColorProperty = "_BaseColor",
                                useUpperLevelMaterialColorSetting = true,
                            },
                        },
                    };
                }

                option.colors[s] = new PlayerCharacterBodyPartComponent.ColorOption
                {
                    defaultTitle = shade.Title,
                    icon = swatch,
                    iconColor = Swatch(shade.Tint),
                    materialColor = shade.Tint,
                    modelColorSettings = new[]
                    {
                        new PlayerCharacterBodyPartComponent.ModelColorSetting
                        {
#if UNITY_EDITOR
                            name = shade.Title,
#endif
                            useUpperLevelMaterialColorSetting = true,
                            materialGroups = groups,
                        },
                    },
                };
            }
            return option;
        }

        /// <summary>The option's name in the list: the style's title, or what having none of it is called.</summary>
        private static string Title(string socket, GameObject[] objects)
        {
            foreach (GameObject o in objects)
            {
                if (o == null)
                    continue;
                foreach (Style style in HairStyles)
                    if (style.Model == o.name) return style.Title;
                foreach (Style style in BeardStyles)
                    if (style.Model == o.name) return style.Title;
            }
            return socket == SocketHair ? "Bald" : "None";
        }

        /// <summary>
        /// What the tint will look like on the hair, for the colour button. The shader
        /// multiplies in linear space, so the swatch has to go through linear too - a
        /// straight multiply of the sRGB values reads far too light.
        /// </summary>
        private static Color Swatch(Color tint)
        {
            float sheet = Mathf.GammaToLinearSpace(HairSheetGrey);
            Color lit = new Color(
                Mathf.GammaToLinearSpace(tint.r) * sheet,
                Mathf.GammaToLinearSpace(tint.g) * sheet,
                Mathf.GammaToLinearSpace(tint.b) * sheet);
            return new Color(
                Mathf.Clamp01(Mathf.LinearToGammaSpace(lit.r)),
                Mathf.Clamp01(Mathf.LinearToGammaSpace(lit.g)),
                Mathf.Clamp01(Mathf.LinearToGammaSpace(lit.b)),
                1f);
        }
    }
}
