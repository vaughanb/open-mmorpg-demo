using System.Collections.Generic;
using MultiplayerARPG.GameData.Model.Playables;
using UnityEditor;
using UnityEngine;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// Assembles the demo's character models out of the Quaternius modular parts.
    ///
    /// A model is one skeleton wearing one bare part per equipment slot. Each slot's
    /// bare part is the container's default model, so equipping a chest piece hides
    /// the bare torso and nothing else — that is what lets the demo show gear
    /// changing on the character rather than only in the inventory.
    ///
    /// Equipment meshes are skinned to their own copy of the same skeleton; the
    /// kit's EquipmentModelBonesSetupManager rebinds them onto the character's bones
    /// by name at runtime, which works because every Quaternius part shares one
    /// bone naming scheme.
    /// </summary>
    public static class DemoCharacterBuilder
    {
        private const string PartDir = "Assets/Plugins/Quaternius/Characters/Prefabs/BodyParts";
        private const string BaseDir = "Assets/Plugins/Quaternius/Characters/Models/Base";
        private const string OutfitDir = "Assets/Plugins/Quaternius/Characters/Models/Outfits";
        private const string HairDir = "Assets/Plugins/Quaternius/Characters/Models/Hair";
        private const string ModelOutDir = "Assets/OpenMMORPG/Demo/Prefabs/GamePlay/CharacterModels";
        private const string LibraryMaterialDir = "Assets/Plugins/Quaternius/Characters/Materials";
        private const string MaterialOutDir = "Assets/OpenMMORPG/Demo/Materials";

        /// <summary>
        /// Multiplied over the pack's greyscale hair texture. See <see cref="TintHair"/>.
        /// </summary>
        private static readonly Color HairTint = new Color(0.42f, 0.27f, 0.16f, 1f);

        /// <summary>Armour slots, in the order the bare parts are grafted on.</summary>
        public static readonly string[] ArmourSockets = { "Head", "Body", "Arms", "Legs", "Feet" };

        public const string SocketRightHand = "RightHand";
        public const string SocketLeftHand = "LeftHand";
        public const string SocketPauldron = "Pauldron";

        /// <summary>
        /// How far along the wrist-to-knuckle line the fist closes, as a fraction.
        /// Half is the middle of the palm.
        /// </summary>
        private const float GripAcrossPalm = 0.5f;

        /// <summary>
        /// Side of the cube every skinned part is given for culling, in metres, standing on
        /// the ground at the character's feet. Comfortably larger than a 1.8m character can
        /// reach in any pose the demo plays. See <see cref="WidenBounds"/>.
        /// </summary>
        private const float MotionBox = 3f;

        /// <summary>
        /// A character model to build. Players are equipment driven: they wear bare
        /// parts that the kit swaps for gear at runtime. Everyone else has an outfit
        /// baked in, because monsters and NPCs never change clothes and baking one in
        /// costs nothing at runtime.
        /// </summary>
        private struct Variant
        {
            public string Name;
            public string Gender;
            public string Outfit;
            public string Hair;
            /// <summary>
            /// Which pieces of the outfit to wear, without the gender and outfit prefix.
            /// Left empty, every piece whose name starts with that prefix is worn, which is
            /// what the peasant and ranger sets want. The knight set cannot do that: it
            /// carries two torsos, two pauldrons and two head pieces as alternatives, and
            /// grafting the lot puts a character in a breastplate and a tabard at once.
            /// </summary>
            public string[] Parts;
            /// <summary>
            /// A body the player can be. It carries every hairstyle and beard that fits it,
            /// switched by the kit's body-part system, instead of one baked hairstyle. See
            /// <see cref="GraftWardrobe"/> and DemoBodyPartBuilder.
            /// </summary>
            public bool Selectable;
        }

        private static readonly Variant[] Variants =
        {
            new Variant { Name = "PlayerCharacterModel_Male", Gender = "Male", Selectable = true },
            new Variant { Name = "PlayerCharacterModel_Female", Gender = "Female", Selectable = true },
            new Variant { Name = "VillagerModel_Male", Gender = "Male", Outfit = "Peasant", Hair = "Hair_SimpleParted" },
            new Variant { Name = "VillagerModel_Female", Gender = "Female", Outfit = "Peasant", Hair = "Hair_Long" },
            new Variant { Name = "BanditModel_Male", Gender = "Male", Outfit = "Ranger" },
            new Variant { Name = "BanditModel_Female", Gender = "Female", Outfit = "Ranger" },

            // The marauders wear the knight plate and the cultists the wizard robes, so
            // that what a player takes off a corpse is what they watched it wearing.
            new Variant { Name = "MarauderModel_Male", Gender = "Male", Outfit = "Knight",
                Parts = new[] { "Body_Armor", "Arms", "Legs_Armor", "Feet_Armor", "Acc_Pauldron_Round", "Acc_Scarf", "Head_Armet" } },
            new Variant { Name = "MarauderModel_Female", Gender = "Female", Outfit = "Knight",
                Parts = new[] { "Body_Armor", "Arms", "Legs_Armor", "Feet_Armor", "Acc_Pauldrons_Round", "Acc_Scarf", "Head_Armet" } },
            new Variant { Name = "CultistModel_Male", Gender = "Male", Outfit = "Wizard", Hair = "Hair_SimpleParted" },
            new Variant { Name = "CultistModel_Female", Gender = "Female", Outfit = "Wizard", Hair = "Hair_Long" },

            // The two outfits no enemy wears go to the townsfolk who are not peasants: the
            // elder in noble dress and the keeper of the strongbox in a warden's tabard.
            // Dressing either of them in ranger, wizard or knight gear would have them
            // wearing the uniform of something the player is hunting.
            new Variant { Name = "ElderModel_Male", Gender = "Male", Outfit = "Noble", Hair = "Hair_SimpleParted",
                Parts = new[] { "Body", "Arms", "Legs", "Feet", "Acc_Gorget" } },
            new Variant { Name = "KeeperModel_Male", Gender = "Male", Outfit = "Knight", Hair = "Hair_SimpleParted",
                Parts = new[] { "Body_Cloth", "Arms", "Legs_Armor", "Feet_Armor" } },

            // The guards wear the keeper's tabard - the town's livery - under a helm and
            // pauldrons, so they read as the keeper's men and not as marauders, who wear
            // the breastplate. No hair: the armet covers the head.
            new Variant { Name = "GuardModel_Male", Gender = "Male", Outfit = "Knight",
                Parts = new[] { "Body_Cloth", "Arms", "Legs_Armor", "Feet_Armor", "Acc_Pauldron_Round", "Head_Armet" } },
        };

        [MenuItem("Open MMORPG/Demo/Build Character Models")]
        public static void BuildAll()
        {
            EnsureFolder(ModelOutDir);
            ShowBothSidesOfCloth();
            foreach (Variant variant in Variants)
                Build(variant);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static void Build(Variant variant)
        {
            string suffix = variant.Gender;
            // The head part carries the skeleton plus the eyes and brows, so it is the
            // one the rest are grafted onto.
            GameObject root = InstantiateUnpacked($"{PartDir}/BareHead_{suffix}.prefab");
            root.name = variant.Name;

            SkinnedMeshRenderer reference = FindRenderer(root, "BareHead");
            Dictionary<string, Transform> bones = MapBones(reference.rootBone);

            // The face is always the bare head; an outfit only ever adds a hood over it.
            var bareParts = new Dictionary<string, GameObject> { { "Head", reference.gameObject } };
            bool equipmentDriven = string.IsNullOrEmpty(variant.Outfit);
            if (equipmentDriven)
            {
                for (int i = 1; i < ArmourSockets.Length; ++i)
                {
                    string socket = ArmourSockets[i];
                    GameObject part = InstantiateUnpacked($"{PartDir}/Bare{socket}_{suffix}.prefab");
                    SkinnedMeshRenderer renderer = FindRenderer(part, $"Bare{socket}");
                    Rebind(renderer, bones);
                    renderer.transform.SetParent(root.transform, false);
                    bareParts[socket] = renderer.gameObject;
                    Object.DestroyImmediate(part);
                }
            }
            else
            {
                GraftOutfit(root, bones, variant);
            }

            // What head gear replaces: the hair, or for a selectable body the holder that
            // every hairstyle hangs under. See BuildContainers for why it is not the head.
            GameObject hair = null;
            var wardrobe = new List<EquipmentContainer>();
            if (variant.Selectable)
            {
                hair = GraftWardrobe(root, bones, variant.Gender, wardrobe);
            }
            else if (!string.IsNullOrEmpty(variant.Hair))
            {
                // Parented to the head part rather than the model root, so that head
                // gear takes the hair with it: the Head container's default model is
                // the bare head, and the kit switches that whole object off when a
                // helmet or hood goes on. Hanging the hair off the root instead would
                // leave it sticking through the hood.
                GraftModel(root, bones, $"{HairDir}/{variant.Hair}.fbx", bareParts["Head"].transform);
                foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (renderer.name.StartsWith("Hair"))
                        hair = renderer.gameObject;
                }
            }

            TintHair(root);

            Animator animator = root.GetComponent<Animator>();
            if (animator == null)
                animator = root.AddComponent<Animator>();
            animator.avatar = LoadAvatar($"{BaseDir}/Superhero_{suffix}_FullBody.fbx");
            animator.applyRootMotion = false;
            // Characters keep animating off-screen: the server drives combat from
            // animation timing, so a culled attack would never land its hit.
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            PlayableCharacterModel model = root.GetComponent<PlayableCharacterModel>();
            if (model == null)
                model = root.AddComponent<PlayableCharacterModel>();
            model.animator = animator;
            // The bone map for equipment is read off this renderer, so it has to be one
            // carrying the full skeleton rather than, say, the eyes. A baked outfit has
            // no bare torso, so fall back to the head, which carries the same skeleton.
            model.skinnedMeshRenderer = equipmentDriven ? FindRenderer(root, "BareBody") : reference;
            model.defaultAnimations = DemoAnimationSet.BuildDefault();
            // Only players can hold a shot: charging comes from the player controller, so a
            // monster's bow has to be wired to fire in one go instead.
            model.weaponAnimations = DemoAnimationSet.BuildWeaponAnimations(equipmentDriven);

            model.EquipmentContainers = BuildContainers(root, bones, bareParts, equipmentDriven, hair, wardrobe);

            WidenBounds(root);

            string path = $"{ModelOutDir}/{root.name}.prefab";
            PrefabUtility.SaveAsPrefabAsset(root, path);
            // Forced, because the editor's loaded copy of a prefab is not refreshed by
            // writing a new file over it. Where a mesh or material this references was
            // itself regenerated earlier in the same run, that loaded copy can be holding
            // a reference the rebuild has since repaired on disk - and every later step
            // reads the loaded copy, so the stale one propagates into everything built
            // from it, silently and with nothing missing in the inspector to show for it.
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            Object.DestroyImmediate(root);
            Debug.Log($"[{nameof(DemoCharacterBuilder)}] Built {path}.");
        }

        /// <summary>
        /// Grafts every part of an outfit onto the skeleton. The parts are found by
        /// name prefix rather than listed, because the pack is not consistent about
        /// them — the male ranger has Feet_Boots and one Acc_Pauldron, the female has
        /// Feet and Acc_Pauldrons — and a prefix match picks all of them up while
        /// skipping the combined "Male_Ranger.fbx" that has no part suffix.
        /// </summary>
        private static void GraftOutfit(GameObject root, Dictionary<string, Transform> bones, Variant variant)
        {
            string prefix = $"{variant.Gender}_{variant.Outfit}_";
            var found = new List<string>();

            if (variant.Parts != null && variant.Parts.Length > 0)
            {
                foreach (string part in variant.Parts)
                {
                    string path = $"{OutfitDir}/{prefix}{part}.fbx";
                    if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
                        Debug.LogError($"[{nameof(DemoCharacterBuilder)}] {variant.Name} wants \"{prefix}{part}\", which is not there.");
                    else
                        found.Add(path);
                }
            }
            else
            {
                foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { OutfitDir }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (System.IO.Path.GetFileNameWithoutExtension(path).StartsWith(prefix))
                        found.Add(path);
                }
                found.Sort();
            }

            if (found.Count == 0)
                Debug.LogError($"[{nameof(DemoCharacterBuilder)}] No outfit parts named \"{prefix}*\" under {OutfitDir}.");

            foreach (string path in found)
                GraftModel(root, bones, path);
        }

        /// <summary>
        /// Moves every skinned renderer out of a model and onto the character's skeleton.
        /// <paramref name="parent"/> only decides what switching that object off will
        /// hide, since a skinned mesh is positioned by its bones and not by its parent.
        /// </summary>
        private static void GraftModel(GameObject root, Dictionary<string, Transform> bones, string modelPath, Transform parent = null)
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (asset == null)
            {
                Debug.LogError($"[{nameof(DemoCharacterBuilder)}] Missing model \"{modelPath}\".");
                return;
            }
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            foreach (SkinnedMeshRenderer renderer in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Rebind(renderer, bones);
                renderer.transform.SetParent(parent != null ? parent : root.transform, false);
            }
            Object.DestroyImmediate(instance);
        }

        /// <summary>
        /// Grafts every hairstyle and beard that fits this body and builds the containers
        /// the kit's body-part system switches them with. Returns the hair holder, which
        /// is what head gear replaces.
        ///
        /// Each socket gets a holder under the model root with one grafted piece per
        /// style, and a container whose instantiated-object groups name them: group i is
        /// option i on the create screen. A hair group also names the eyebrows, so they
        /// take the hair colour and survive the bald option; a beard group is just the
        /// beard, and "none" is an empty group. Only the default is left active, so the
        /// prefab shows a new character's look in the editor and to the outfit audit.
        /// </summary>
        private static GameObject GraftWardrobe(GameObject root, Dictionary<string, Transform> bones, string gender, List<EquipmentContainer> containers)
        {
            SkinnedMeshRenderer brows = FindRenderer(root, DemoBodyPartBuilder.EyebrowsName);
            GameObject always = brows != null ? brows.gameObject : null;
            // Hair: default first, bald last. Beard: none first, so a new character is
            // clean-shaven unless they choose otherwise.
            GameObject hairHolder = Wardrobe(root, bones, gender, DemoBodyPartBuilder.SocketHair, DemoBodyPartBuilder.HairStyles, always, false, containers);
            Wardrobe(root, bones, gender, DemoBodyPartBuilder.SocketBeard, DemoBodyPartBuilder.BeardStyles, null, true, containers);
            return hairHolder;
        }

        private static GameObject Wardrobe(GameObject root, Dictionary<string, Transform> bones, string gender, string socket,
                                           DemoBodyPartBuilder.Style[] styles, GameObject always, bool noneFirst,
                                           List<EquipmentContainer> containers)
        {
            var fitting = new List<DemoBodyPartBuilder.Style>();
            foreach (DemoBodyPartBuilder.Style style in styles)
            {
                if (style.Fits(gender))
                    fitting.Add(style);
            }
            if (fitting.Count == 0)
                return null;

            var holder = new GameObject(socket);
            holder.transform.SetParent(root.transform, false);

            var groups = new List<EquipmentInstantiatedObjectGroup>();
            var none = new EquipmentInstantiatedObjectGroup
            {
                instantiatedObjects = always != null ? new[] { always } : new GameObject[0],
            };
            if (noneFirst)
                groups.Add(none);

            foreach (DemoBodyPartBuilder.Style style in fitting)
            {
                var before = new HashSet<Transform>();
                foreach (Transform child in holder.transform)
                    before.Add(child);
                GraftModel(root, bones, $"{HairDir}/{style.Model}.fbx", holder.transform);

                var pieces = new List<GameObject>();
                foreach (Transform child in holder.transform)
                {
                    if (before.Contains(child))
                        continue;
                    // Named for the style so the entity builder can title the option.
                    child.name = style.Model;
                    // The default is the first group, whichever kind it is.
                    child.gameObject.SetActive(groups.Count == 0);
                    pieces.Add(child.gameObject);
                }
                if (pieces.Count == 0)
                    continue; // Missing model, already logged by GraftModel.
                if (always != null)
                    pieces.Add(always);
                groups.Add(new EquipmentInstantiatedObjectGroup { instantiatedObjects = pieces.ToArray() });
            }
            if (!noneFirst)
                groups.Add(none);

            containers.Add(new EquipmentContainer
            {
                equipSocket = socket,
                transform = holder.transform,
                instantiatedObjectGroups = groups.ToArray(),
            });
            return holder;
        }

        /// <summary>
        /// Gives every skinned part a bounding box big enough for the animations.
        ///
        /// A skinned renderer keeps the bounds of its bind pose unless told otherwise, and
        /// these characters are cut from a T-pose — so the arms' box is a thin horizontal
        /// slab a metre and a half up, about as wide as the character is tall. Animate an
        /// arm down to the hip and it leaves that box entirely. Frame the hand closely
        /// enough that the slab falls outside the view and Unity culls the whole renderer:
        /// the arm disappears while the sword carries on being drawn, because a weapon is
        /// a separate rigid mesh with bounds of its own. What you see is a weapon floating
        /// in mid-air beside a missing hand, which reads as the weapon being attached
        /// wrongly rather than as the arm having been culled.
        ///
        /// Recomputing the bounds every frame would also fix it — that is what
        /// updateWhenOffscreen does — but it costs a skinning evaluation per renderer per
        /// frame whether or not anything is on screen, which is the wrong trade for a game
        /// that expects a crowd of them. A box the character cannot animate out of costs
        /// nothing at all.
        /// </summary>
        private static void WidenBounds(GameObject root)
        {
            // The box wanted, in the character's own space: standing on the ground with
            // the feet at the origin.
            var want = new Bounds(new Vector3(0f, MotionBox * 0.5f, 0f), Vector3.one * MotionBox);
            foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                // Converted rather than assigned directly, and converted through the root
                // bone: a skinned renderer measures localBounds in its root bone's space,
                // not its own. These bodies are authored Z-up and the skeleton carries that
                // quarter turn, so a box handed over as-is comes out lying on its side —
                // which leaves the head outside it and culls the face instead of the arm.
                // Reading the renderer's own transform instead happens to agree for the
                // grafted parts, which inherit the same rotation, and disagree for the hair,
                // which does not.
                Transform space = renderer.rootBone != null ? renderer.rootBone : renderer.transform;
                Matrix4x4 toLocal = space.worldToLocalMatrix * root.transform.localToWorldMatrix;
                renderer.localBounds = Enclose(want, toLocal);
            }
        }

        /// <summary>The smallest axis-aligned box holding <paramref name="box"/> once moved.</summary>
        private static Bounds Enclose(Bounds box, Matrix4x4 transformation)
        {
            var low = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 high = -low;
            for (int corner = 0; corner < 8; ++corner)
            {
                Vector3 point = transformation.MultiplyPoint3x4(new Vector3(
                    (corner & 1) == 0 ? box.min.x : box.max.x,
                    (corner & 2) == 0 ? box.min.y : box.max.y,
                    (corner & 4) == 0 ? box.min.z : box.max.z));
                low = Vector3.Min(low, point);
                high = Vector3.Max(high, point);
            }
            var result = new Bounds();
            result.SetMinMax(low, high);
            return result;
        }

        /// <summary>
        /// Makes every garment in the pack draw its inside as well as its outside.
        ///
        /// These are single-surface garments — a shirt is one sheet of triangles with no
        /// thickness — and a collar that stands away from the neck is looked straight into
        /// from above. Culled to one side, the inside of that collar is not drawn at all
        /// and you see through the character; drawn, it is simply the inside of the shirt,
        /// which is what it should be. The same goes for a sleeve cuff, an open hem, or a
        /// hood.
        ///
        /// Sinking the skin under the collar is the other half of the same problem and does
        /// not replace this: skin far enough in to stay hidden under cloth is by then too
        /// far in to fill the opening.
        ///
        /// Done over the pack's own materials rather than over the characters built here,
        /// because a garment reaches a player through an equipment prefab that this builder
        /// never sees. Three of them were already turned round by hand; this makes the rest
        /// match, and keeps them matching after a reimport.
        /// </summary>
        private static void ShowBothSidesOfCloth()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { LibraryMaterialDir }))
            {
                Material material = AssetDatabase.LoadAssetAtPath<Material>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (material == null || !IsCloth(material.name))
                    continue;
                if (!material.HasProperty("_Cull") ||
                    Mathf.Approximately(material.GetFloat("_Cull"), 0f))
                    continue;
                material.SetFloat("_Cull", 0f);
                material.doubleSidedGI = true;
                EditorUtility.SetDirty(material);
            }
        }

        /// <summary>
        /// Whether a pack material belongs to a garment rather than to the body wearing it.
        ///
        /// Skin, eyes and hair are closed or two-faced shapes and are the ones that must
        /// keep their culling: a head has no inside worth drawing, and hair is cut out of
        /// flat sheets that read worse from behind. Everything else the pack ships is cloth
        /// or plate.
        /// </summary>
        private static bool IsCloth(string materialName)
        {
            foreach (string skin in new[] { "MI_Body_", "MI_Skin_", "MI_Hair_", "MI_Eye" })
            {
                if (materialName.StartsWith(skin))
                    return false;
            }
            return materialName.StartsWith("MI_");
        }

        /// <summary>
        /// Replaces the pack's hair material with a tinted copy the demo owns.
        ///
        /// The hair texture is greyscale so that a game can tint it to whatever colour
        /// it wants, and the material shipped alongside leaves that tint at pure white —
        /// so straight out of the pack every head of hair and every eyebrow renders
        /// mid-grey. It is most obvious on the eyebrows, which read as white against a
        /// tanned face on a character who is plainly not old.
        ///
        /// The tinted copy lives under the demo rather than beside the original because
        /// only Assets/OpenMMORPG ships; an edit to the Quaternius folder would not
        /// travel with the project and would be lost the next time the pack is replaced.
        ///
        /// A selectable body's hair goes through this too, and the body-part colour
        /// options then override the tint per character at runtime; this is what a body
        /// with nothing chosen, and every NPC, wears.
        /// </summary>
        private static void TintHair(GameObject root)
        {
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                Material source = renderer.sharedMaterial;
                // The pack ships two hair materials over two different textures, and
                // which one a character gets depends on the body it was cut from — the
                // male base uses MI_Hair_1 and the female MI_Hair_2 — so both need a
                // tinted counterpart. Matching on the prefix rather than on one name
                // means a third would be picked up too.
                if (source == null || !source.name.StartsWith("MI_Hair_"))
                    continue;
                Material tinted = TintedCopy(source);
                if (tinted != null)
                    renderer.sharedMaterial = tinted;
            }
        }

        /// <summary>
        /// Returns the demo's tinted counterpart of a pack hair material, making it on
        /// first use. The tint is reapplied on every build, so restyling everyone is a
        /// matter of changing <see cref="HairTint"/> and rebuilding rather than deleting
        /// the generated materials by hand first.
        /// </summary>
        private static Material TintedCopy(Material source)
        {
            string path = $"{MaterialOutDir}/{source.name}_Tinted.mat";
            Material tinted = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (tinted == null)
            {
                tinted = new Material(source);
                EnsureFolder(MaterialOutDir);
                AssetDatabase.CreateAsset(tinted, path);
            }
            tinted.SetColor("_BaseColor", HairTint);
            EditorUtility.SetDirty(tinted);
            return tinted;
        }

        private static EquipmentContainer[] BuildContainers(GameObject root, Dictionary<string, Transform> bones, Dictionary<string, GameObject> bareParts, bool equipmentDriven, GameObject hair, List<EquipmentContainer> wardrobe)
        {
            var containers = new List<EquipmentContainer>();
            // The hair and beard sockets come first so that, when the model dresses itself,
            // a chosen hairstyle is switched on before any head gear decides to hide it.
            containers.AddRange(wardrobe);

            // Armour is skinned, so it is parented to the model root and driven purely
            // by the rebound bones. Its bare part is the default shown when empty.
            // Baked-outfit characters have no bare parts to swap, but still get the
            // sockets so a quest can hand one a weapon.
            foreach (string socket in ArmourSockets)
            {
                GameObject bare;
                bareParts.TryGetValue(socket, out bare);
                // The head is the exception: what head gear replaces is the hair, not the
                // head itself. The pack's only head piece is a hood — an open cowl with a
                // face-sized gap in the front — so switching the head off with it leaves a
                // pair of eyes floating in a hollow shell. Hair is what a hood should
                // cover, and the face and neck have to stay whatever goes on over them.
                // For a selectable body "the hair" is the holder every hairstyle hangs
                // under, so a hood hides whichever one is chosen.
                GameObject swappedOut = socket == ArmourSockets[0] ? hair : bare;
                containers.Add(new EquipmentContainer
                {
                    equipSocket = socket,
                    transform = root.transform,
                    // Only an equipment driven character swaps anything out. A baked
                    // outfit has no bare part behind it to reveal.
                    defaultModel = equipmentDriven ? swappedOut : null,
                });
            }

            // Shoulder accessories are skinned too, but have no bare equivalent.
            containers.Add(new EquipmentContainer
            {
                equipSocket = SocketPauldron,
                transform = root.transform,
            });

            // Weapons are rigid props, so they hang off a socket in each hand that can
            // be nudged without touching the hand bone the animations drive.
            containers.Add(new EquipmentContainer
            {
                equipSocket = SocketRightHand,
                transform = CreateSocket(root, bones, "hand_r", SocketRightHand),
            });
            containers.Add(new EquipmentContainer
            {
                equipSocket = SocketLeftHand,
                transform = CreateSocket(root, bones, "hand_l", SocketLeftHand),
            });

            return containers.ToArray();
        }

        /// <summary>The pose the fist is measured from. Any clip where the hand grips a haft will do.</summary>
        private const string GripPoseClip = "Sword_Idle";

        /// <summary>
        /// Hangs a weapon socket in the fist, turned so that a weapon built by
        /// DemoWeaponBuilder — grip at the origin, running along +Y — lies along the axis
        /// the closed hand actually wraps.
        ///
        /// **The socket has to be measured from a gripping pose, not the bind pose.** A
        /// bind-pose hand has straight fingers and an outstretched thumb, so the tunnel a
        /// fist holds a haft in does not exist yet. Placing the socket at mid-palm from the
        /// bind pose — which is what <see cref="GripPoint"/> does — left every weapon 7.5cm
        /// from where the hand closes, riding across the fingers instead of sitting in them.
        /// Sampling a gripping clip and reading the closed hand puts it right, and keeps the
        /// figure measured off the rig rather than typed in, so the male and female hands
        /// still each get their own.
        ///
        /// The rotation is the old convention (+Y across the knuckles) plus the smallest
        /// twist that lands it on the measured knuckle axis — about 14 degrees on this rig.
        /// Applying it as a delta preserves the roll, which is what decides whether a
        /// blade's flat or its edge faces forward.
        /// </summary>
        private static Transform CreateSocket(GameObject model, Dictionary<string, Transform> bones,
                                              string boneName, string socketName)
        {
            Transform bone;
            if (!bones.TryGetValue(boneName, out bone))
            {
                Debug.LogError($"[{nameof(DemoCharacterBuilder)}] No bone \"{boneName}\" to hang \"{socketName}\" from.");
                return null;
            }

            var socket = new GameObject(socketName);
            socket.transform.SetParent(bone, false);

            Quaternion rotation = Quaternion.Euler(90f, 0f, 0f);
            Vector3 position;
            Vector3 gripAxis;
            if (MeasureFist(model, bone, boneName.EndsWith("_r") ? "_r" : "_l", out position, out gripAxis))
                rotation = Quaternion.FromToRotation(rotation * Vector3.up, gripAxis) * rotation;
            else
                position = GripPoint(bone);

            socket.transform.localPosition = position;
            socket.transform.localRotation = rotation;
            return socket.transform;
        }

        /// <summary>
        /// Reads the haft tunnel out of a closed fist: the thumb presses one side of it and
        /// the curled middle phalanges the other, so their midpoint is where a haft sits.
        /// The axis is the knuckle line the fist wraps, pointed thumb-ward so a blade leaves
        /// the hand on that side.
        ///
        /// The pose is sampled onto the model and then undone, because every later build
        /// step reads the bind pose.
        /// </summary>
        private static bool MeasureFist(GameObject model, Transform hand, string side,
                                        out Vector3 grip, out Vector3 axis)
        {
            grip = Vector3.zero;
            axis = Vector3.forward;
            if (model == null)
                return false;

            AnimationClip pose = DemoAnimationSet.Clip(GripPoseClip);
            if (pose == null)
                return false;

            string[] needed = { "thumb_03", "index_02", "middle_02", "ring_02", "pinky_02", "index_01", "pinky_01" };
            var found = new Dictionary<string, Transform>();
            Transform[] all = model.GetComponentsInChildren<Transform>(true);
            foreach (Transform t in all)
            {
                foreach (string n in needed)
                {
                    if (t.name != n + side)
                        continue;
                    found[n] = t;
                    break;
                }
            }
            foreach (string n in needed)
            {
                if (found.ContainsKey(n))
                    continue;
                Debug.LogWarning($"[{nameof(DemoCharacterBuilder)}] \"{n}{side}\" is missing, so \"{hand.name}\" " +
                                 "falls back to the bind-pose palm estimate and anything held will sit off the fist.");
                return false;
            }

            var localPositions = new Vector3[all.Length];
            var localRotations = new Quaternion[all.Length];
            for (int i = 0; i < all.Length; ++i)
            {
                localPositions[i] = all[i].localPosition;
                localRotations[i] = all[i].localRotation;
            }

            pose.SampleAnimation(model, pose.length * 0.5f);

            Vector3 fingers = (found["index_02"].position + found["middle_02"].position +
                               found["ring_02"].position + found["pinky_02"].position) * 0.25f;
            grip = hand.InverseTransformPoint((found["thumb_03"].position + fingers) * 0.5f);
            axis = hand.InverseTransformVector(found["index_01"].position - found["pinky_01"].position).normalized;

            for (int i = 0; i < all.Length; ++i)
            {
                all[i].localPosition = localPositions[i];
                all[i].localRotation = localRotations[i];
            }
            return true;
        }

        /// <summary>
        /// Finds where in the hand a haft actually sits.
        ///
        /// The hand bone's pivot is the wrist, not the palm, so a socket left at the
        /// bone origin hangs the weapon off the end of the forearm — about a hand's
        /// length short of the fist, with the grip out in the open air beside it.
        ///
        /// The four finger roots ring the knuckles, so their centroid is the middle of
        /// the knuckle line and half of it is the middle of the palm, which is where a
        /// closed fist holds something. Measured off the rig rather than typed in, so
        /// the male and female hands each get their own figure and a rig with different
        /// proportions would still be placed correctly.
        /// </summary>
        private static Vector3 GripPoint(Transform hand)
        {
            string[] fingerRoots = { "index_01", "middle_01", "ring_01", "pinky_01" };
            Vector3 sum = Vector3.zero;
            int found = 0;
            foreach (Transform child in hand)
            {
                foreach (string fingerRoot in fingerRoots)
                {
                    if (!child.name.StartsWith(fingerRoot))
                        continue;
                    sum += child.localPosition;
                    ++found;
                    break;
                }
            }
            if (found == 0)
            {
                Debug.LogWarning($"[{nameof(DemoCharacterBuilder)}] No finger bones under \"{hand.name}\", so its " +
                                 "weapon socket stays at the wrist and anything held will look detached.");
                return Vector3.zero;
            }
            return sum / found * GripAcrossPalm;
        }

        private static GameObject InstantiateUnpacked(string prefabPath)
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (asset == null)
            {
                Debug.LogError($"[{nameof(DemoCharacterBuilder)}] Missing \"{prefabPath}\". Run Quaternius > Split Base Bodies Into Equipment Slots first.");
                return null;
            }
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            return instance;
        }

        private static SkinnedMeshRenderer FindRenderer(GameObject root, string name)
        {
            foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer.name == name)
                    return renderer;
            }
            Debug.LogError($"[{nameof(DemoCharacterBuilder)}] \"{root.name}\" has no renderer named \"{name}\".");
            return null;
        }

        private static Dictionary<string, Transform> MapBones(Transform rootBone)
        {
            var map = new Dictionary<string, Transform>();
            foreach (Transform bone in rootBone.GetComponentsInChildren<Transform>(true))
                map[bone.name] = bone;
            return map;
        }

        private static void Rebind(SkinnedMeshRenderer renderer, Dictionary<string, Transform> bones)
        {
            Transform[] rebound = renderer.bones;
            for (int i = 0; i < rebound.Length; ++i)
            {
                if (rebound[i] == null)
                    continue;
                Transform match;
                if (bones.TryGetValue(rebound[i].name, out match))
                    rebound[i] = match;
            }
            renderer.bones = rebound;
            Transform root;
            if (bones.TryGetValue(renderer.rootBone.name, out root))
                renderer.rootBone = root;
        }

        private static Avatar LoadAvatar(string modelPath)
        {
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(modelPath))
            {
                Avatar avatar = asset as Avatar;
                if (avatar != null)
                    return avatar;
            }
            Debug.LogError($"[{nameof(DemoCharacterBuilder)}] \"{modelPath}\" has no avatar; is its rig set to Humanoid?");
            return null;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            int split = path.LastIndexOf('/');
            EnsureFolder(path.Substring(0, split));
            AssetDatabase.CreateFolder(path.Substring(0, split), path.Substring(split + 1));
        }
    }
}
