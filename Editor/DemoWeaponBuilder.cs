using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// Turns the raw weapon models into equippable prefabs: the swords, axes and pick from
    /// Quaternius' Fantasy Props MegaKit, the shield from the Malagen set, and the bow, staff,
    /// arrow, quiver and short sword, which the user supplied as .glb files (2026-10-06) and which were
    /// retopologised, baked and put into this convention in Blender
    /// (`Demo/Art/Weapons/Source~`, see its README), so they are used as they come.
    ///
    /// The other source models cannot be attached to a hand as they are. They are authored at
    /// varying scales — the longsword measures 1.67m and the bow 2.01m against a 1.75m
    /// character, which reads as a greatsword and a bow taller than its archer — they
    /// point down different axes, and their pivots sit at the middle of the mesh (or, for
    /// the quiver, more than a metre away from it) rather than where a hand would hold
    /// them.
    ///
    /// Every prefab this produces follows one convention: the grip is at the origin and
    /// the weapon runs along +Y. A single socket rotation then places any of them in a
    /// hand, instead of each needing its own hand-tuned offset.
    /// </summary>
    public static class DemoWeaponBuilder
    {
        private const string SourceDir = "Assets/Plugins/Malagen/Characters/HumanMale/Weapons";

        /// <summary>
        /// The Fantasy Props MegaKit (CC0). Its weapons stand up the right way already, tip or head
        /// along +Y, and the axes' import root carries that as a quarter turn about Z.
        /// </summary>
        private const string PropsDir = "Assets/Plugins/Quaternius/Props/Models";
        private const string OutputDir = "Assets/OpenMMORPG/Demo/Prefabs/GamePlay/Equipments";

        /// <summary>
        /// Where the demo's own weapon models and their textures live. The Blender sources and the scripts that
        /// make the bow, staff, arrow and quiver from them are in `Source~` beside them.
        /// </summary>
        private const string ArtDir = "Assets/OpenMMORPG/Demo/Art/Weapons";

        /// <summary>
        /// How a source model becomes a prefab. <see cref="StandUp"/> brings the model's
        /// long axis to +Y; <see cref="Length"/> is the real-world size to scale it to;
        /// <see cref="GripFromButt"/> is how far along the weapon the hand sits, measured
        /// from the bottom once scaled, or negative to grip at the middle. <see cref="Source"/> is
        /// the model to build from; empty means the Malagen model of the same name.
        /// </summary>
        private struct Weapon
        {
            public string Name;
            public string Source;
            public Vector3 StandUp;
            public float Length;
            public float GripFromButt;
            /// <summary>
            /// Centre the grip on the haft where the hand holds it, not on the whole outline. For a
            /// weapon whose head hangs off to one side - an axe - the outline's middle is out in the
            /// air beside the haft.
            /// </summary>
            public bool CentreOnHaft;
            /// <summary>
            /// The model was built to the convention in Blender (`Art/Weapons/Source~`): real size, grip at the
            /// origin, length along +Y. Nothing is stood up, resized or re-centred; the prefab is the model.
            /// </summary>
            public bool Authored;
            /// <summary>
            /// A trim the user made by hand to an authored model's `Mesh` child, kept here so a rebuild
            /// reproduces it instead of resetting it to the FBX's size. Zero means none.
            /// </summary>
            public Vector3 MeshScale;
        }

        private static readonly Weapon[] Weapons =
        {
            // Hand-and-a-half sword: a hand's width above the pommel.
            new Weapon { Name = "Longsword",  Source = $"{PropsDir}/Sword_Steel.fbx", StandUp = Vector3.zero, Length = 1.15f, GripFromButt = 0.13f },
            // The user's own short sword (2026-10-06), built to the convention in Blender (Authored): tip up
            // +Y, flat facing Z, the middle of the grip at the origin, 0.12m above the pommel's end. It
            // replaced the pack's dagger, which at this length read as a dagger.
            new Weapon { Name = "ShortSword", Source = $"{ArtDir}/ShortSword.fbx", Length = 0.80f, Authored = true },
            // The bandits' axe, and the Bandit Axe item: bronze, the rougher of the pack's two.
            new Weapon { Name = "Axe",        Source = $"{PropsDir}/Axe_Bronze.fbx", StandUp = new Vector3(0f, 0f, 90f), Length = 0.85f, GripFromButt = 0.12f, CentreOnHaft = true },
            // The woodcutter's axe: the same model in steel.
            new Weapon { Name = "WoodAxe",    Source = $"{PropsDir}/Axe_Steel.fbx", StandUp = new Vector3(0f, 0f, 90f), Length = 0.85f, GripFromButt = 0.12f, CentreOnHaft = true },
            // The bow, staff and arrow are the user's own models (2026-10-06), retopologised and baked in
            // Blender to the convention, so they are used as they come (Authored). A bow is held at its
            // handle, which is where its origin is; its string stays 0.14m off it, which is what the draw
            // clips and `BowEquipmentEntity.nockRadius` were tuned for. The staff is gripped 0.60m above
            // its butt so the head stands clear. The arrow balances at its middle, head toward +Y.
            // The user trimmed the bow's thickness to 70% on the prefab by hand (2026-10-07); MeshScale keeps it.
            // It thins the limbs and the string in Z only: the draw maths still nocks (closest approach 2.6cm)
            // and the hand stays within 15cm of the bow's plane (limit 30).
            new Weapon { Name = "Bow",        Source = $"{ArtDir}/Bow.fbx",       Length = 1.70f, Authored = true, MeshScale = new Vector3(1f, 1f, 0.7f) },
            new Weapon { Name = "MageStaff",  Source = $"{ArtDir}/MageStaff.fbx", Length = 1.65f, Authored = true },
            new Weapon { Name = "Pickaxe",    Source = $"{PropsDir}/Pickaxe_Steel.fbx", StandUp = Vector3.zero, Length = 0.80f, GripFromButt = 0.12f, CentreOnHaft = true },
            // The quiver is built on its own (BuildQuiver): it is worn, not held.
            new Weapon { Name = "Arrow",      Source = $"{ArtDir}/Arrow.fbx",     Length = 0.72f, Authored = true },
        };

        /// <summary>Diameter to scale the round shield to.</summary>
        private const float ShieldDiameter = 0.85f;

        /// <summary>
        /// How far behind the shield's face the grip sits. The hand belongs in the hollow
        /// behind the boss; putting the origin on the disc's centre instead pushes the
        /// fingers out through the painted front.
        /// </summary>
        private const float ShieldGripDepth = 0.10f;

        [MenuItem("Open MMORPG/Demo/Build Weapon Prefabs")]
        public static void BuildAll()
        {
            if (!AssetDatabase.IsValidFolder(OutputDir))
                AssetDatabase.CreateFolder("Assets/OpenMMORPG/Demo/Prefabs/GamePlay", "Equipments");

            foreach (Weapon weapon in Weapons)
                Build(weapon);
            BuildShield();
            // After the loop, not inside it: the bow and the quiver want the arrow prefab, which
            // is built last.
            DressBow();
            BuildQuiver();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        // ---- the quiver ---------------------------------------------------------

        /// <summary>
        /// How many arrows the quiver shows when it is full. <see cref="QuiverArrows"/> shows one
        /// fewer for each arrow under this the character has, so it empties over the last dozen.
        /// </summary>
        private const int QuiverArrowSlots = 12;

        /// <summary>How far below the mouth an arrow's head rests (the foot is 0.44m down), so no head shows through the leather.</summary>
        private const float QuiverArrowSeat = 0.37f;

        /// <summary>
        /// The leather and its lip, which the arrows' shafts must stay clear of when they are laid out inside the
        /// opening: the shell is about 6mm thick, a shaft 5mm wide.
        /// </summary>
        private const float QuiverWall = 0.0125f;

        /// <summary>
        /// Builds `Quiver.prefab`: the quiver model with a dozen arrows standing in it under a
        /// <see cref="QuiverArrows"/>.
        ///
        /// **The model is authored to the convention** (Blender, `Art/Weapons/Source~/quiver.py`): its origin is on
        /// the axis of the opening at the height of its middle, +Y runs up the quiver, and +Z is the outer face,
        /// so the prefab is the model, nothing is measured or turned. The flat face of the shell, against the
        /// wearer's back, sits 3cm behind the origin - where the old, flat pouch's back sat on the socket - and
        /// the quiver is 0.44m from its foot to the mouth and about 13cm across and deep there. (The previous model
        /// was authored worn, leaning 40 degrees in the back socket's frame, and had to be measured and stood up.)
        ///
        /// The arrows are nested instances of `Arrow.prefab`. Their heads converge on the quiver's axis near
        /// the foot and their nocks fan out above the mouth, laid out in rings (a centre arrow, five, six)
        /// sized to the opening, which is read off the model's own cross-sections at the mouth and at the
        /// seat. Each is turned at random about its shaft so the fletching does not line up. The order
        /// <see cref="QuiverArrows"/> keeps them in is shuffled too (seeded, so a rebuild is identical): the
        /// quiver thins from all over, not row by row.
        /// </summary>
        [MenuItem("Open MMORPG/Demo/Build Quiver")]
        public static void BuildQuiverMenu()
        {
            BuildQuiver();
            int items = DemoItemBuilder.RefreshSheathModels();
            // The Bandit Archers wear one too, full and for show (DemoEntityBuilder.WearQuiver).
            int archers = DemoEntityBuilder.WearArcherQuivers();
            AssetDatabase.SaveAssets();
            Debug.Log($"[{nameof(DemoWeaponBuilder)}] Quiver built, carried by {items} item(s) and worn by {archers} archer(s). " +
                      "Run Build Map Server if the MMO flow is in use: the arrow count it shows is synced by the server.");
        }

        public static void BuildQuiver()
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>($"{ArtDir}/Quiver.fbx");
            GameObject arrowPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{OutputDir}/Arrow.prefab");
            if (source == null || arrowPrefab == null)
            {
                Debug.LogError($"[{nameof(DemoWeaponBuilder)}] The quiver needs \"{ArtDir}/Quiver.fbx\" and \"{OutputDir}/Arrow.prefab\".");
                return;
            }

            var root = new GameObject("Quiver");
            var mesh = (GameObject)PrefabUtility.InstantiatePrefab(source);
            PrefabUtility.UnpackPrefabInstance(mesh, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            mesh.name = "Mesh";
            mesh.transform.SetParent(root.transform, false);

            MeshFilter filter = mesh.GetComponentInChildren<MeshFilter>();
            Vector3[] points = filter != null ? ReadVertices(filter, root.transform) : null;
            if (points == null || points.Length < 3)
            {
                Debug.LogError($"[{nameof(DemoWeaponBuilder)}] Could not read the quiver's mesh.");
                Object.DestroyImmediate(root);
                return;
            }

            // The shell's middle at the mouth and at the seat, and the opening's half-sizes. A little under the
            // mouth: the rim is cut on a slant, so the ring is only complete below its lowest point.
            Vector2 mouthCentre = Section(points, -0.025f, out Vector2 mouthHalf);
            Vector2 seatCentre = Section(points, -QuiverArrowSeat, out Vector2 _);
            float room = Mathf.Min(mouthHalf.x, mouthHalf.y) - QuiverWall;

            var arrows = new GameObject("Arrows").transform;
            arrows.SetParent(root.transform, false);
            float arrowLength = ArrowLength(arrowPrefab);
            var random = new System.Random(20261006);
            var placed = new GameObject[QuiverArrowSlots];
            // 1 + 5 + 6, in rings a little under half and four fifths of the way to the leather.
            var layout = new List<Vector2> { Vector2.zero };
            for (int i = 0; i < 5; ++i)
            {
                float a = (i + 0.25f) * Mathf.PI * 2f / 5f;
                layout.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * room * 0.42f);
            }
            for (int i = 0; i < 6; ++i)
            {
                float a = (i + 0.6f) * Mathf.PI * 2f / 6f;
                layout.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * room * 0.82f);
            }
            for (int i = 0; i < QuiverArrowSlots; ++i)
            {
                Vector2 offset = layout[i];
                float nockHeight = arrowLength - QuiverArrowSeat + (float)(random.NextDouble() * 0.03 - 0.01);
                Vector3 nock = new Vector3(mouthCentre.x + offset.x * 1.45f, nockHeight, mouthCentre.y + offset.y * 1.45f);
                Vector3 head = new Vector3(seatCentre.x + offset.x * 0.2f, -QuiverArrowSeat, seatCentre.y + offset.y * 0.2f);
                Vector3 shaft = (nock - head).normalized;
                var arrow = (GameObject)PrefabUtility.InstantiatePrefab(arrowPrefab, arrows);
                arrow.name = $"Arrow{i + 1:00}";
                // The arrow prefab runs head-first along +Y with its origin at the middle.
                arrow.transform.localRotation = Quaternion.FromToRotation(Vector3.up, -shaft) *
                                                Quaternion.AngleAxis((float)random.NextDouble() * 360f, Vector3.up);
                arrow.transform.localPosition = nock - shaft * (arrowLength * 0.5f);
                placed[i] = arrow;
            }
            for (int i = placed.Length - 1; i > 0; --i)
            {
                int j = random.Next(i + 1);
                GameObject swap = placed[i];
                placed[i] = placed[j];
                placed[j] = swap;
            }
            root.AddComponent<QuiverArrows>().arrows = placed;

            Save(root, "Quiver", 1f, Measure(root));
        }

        /// <summary>The model's vertices in <paramref name="root"/>'s space. Readable in the editor whatever Read/Write says.</summary>
        private static Vector3[] ReadVertices(MeshFilter filter, Transform root)
        {
            if (filter.sharedMesh == null)
                return null;
            Matrix4x4 toRoot = root.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            using (Mesh.MeshDataArray data = Mesh.AcquireReadOnlyMeshData(filter.sharedMesh))
            {
                var vertices = new Unity.Collections.NativeArray<Vector3>(data[0].vertexCount, Unity.Collections.Allocator.Temp);
                data[0].GetVertices(vertices);
                var points = new Vector3[vertices.Length];
                for (int i = 0; i < points.Length; ++i)
                    points[i] = toRoot.MultiplyPoint3x4(vertices[i]);
                vertices.Dispose();
                return points;
            }
        }

        /// <summary>
        /// The middle (x, z) and half-sizes of the model's outline within 1cm of <paramref name="height"/>:
        /// where the shell is at that height, outside leather included.
        /// </summary>
        private static Vector2 Section(Vector3[] points, float height, out Vector2 half)
        {
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
            foreach (Vector3 p in points)
            {
                if (Mathf.Abs(p.y - height) > 0.01f)
                    continue;
                min = Vector2.Min(min, new Vector2(p.x, p.z));
                max = Vector2.Max(max, new Vector2(p.x, p.z));
            }
            if (min.x > max.x)
            {
                half = Vector2.zero;
                return Vector2.zero;
            }
            half = (max - min) * 0.5f;
            return (min + max) * 0.5f;
        }

        /// <summary>The built arrow's length along its shaft.</summary>
        private static float ArrowLength(GameObject arrowPrefab)
        {
            var probe = (GameObject)Object.Instantiate(arrowPrefab);
            try
            {
                return Measure(probe).size.y;
            }
            finally
            {
                Object.DestroyImmediate(probe);
            }
        }

        /// <summary>
        /// Gives the bow the component that draws it: <see cref="BowEquipmentEntity"/>, which bends the
        /// string back to the drawing hand, flexes the limbs with it, and carries, nocks and
        /// looses an arrow.
        ///
        /// It has to be added here rather than by hand, because <see cref="Save"/> writes each
        /// prefab out from a freshly built object - anything added in the inspector lasts
        /// until the next "Build Weapon Prefabs" and no longer.
        ///
        /// <see cref="BowEquipmentEntity"/> is an <c>EquipmentEntity</c>, which is how it hears the kit's
        /// <c>PlayLaunch</c>; the bow is the first demo weapon to carry one, so nothing else
        /// needs this treatment.
        /// </summary>
        private static void DressBow()
        {
            string path = $"{OutputDir}/Bow.prefab";
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null)
            {
                Debug.LogError($"[{nameof(DemoWeaponBuilder)}] No bow prefab at \"{path}\" to dress.");
                return;
            }

            // Before the prefab is opened for editing: the reimport this can trigger would
            // invalidate anything already loaded.
            MakeMeshReadable(asset);

            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                BowEquipmentEntity bow = root.GetComponent<BowEquipmentEntity>();
                if (bow == null)
                    bow = root.AddComponent<BowEquipmentEntity>();
                bow.arrowPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{OutputDir}/Arrow.prefab");
                if (bow.arrowPrefab == null)
                    Debug.LogWarning($"[{nameof(DemoWeaponBuilder)}] No arrow prefab, so the bow will draw an empty string.");
                // What the ranger's skills set off at the loose. Empty until Build Skills fills it,
                // and filled in place, so this reference holds across rebuilds.
                bow.releaseEffects = DemoSkillBuilder.ReleaseTable();
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            Debug.Log($"[{nameof(DemoWeaponBuilder)}] Bow: string and nocked arrow wired up.");
        }

        /// <summary>
        /// Turns Read/Write on for whichever model the bow prefab actually points at.
        ///
        /// The string is bent by moving vertices, which needs the mesh readable at run time,
        /// and the FBX importers ship with it off. Resolved through the prefab rather than by
        /// path on purpose: <c>DemoArtCollector</c> repoints the prefab from the Malagen
        /// library to the collected copy under <c>Demo/Art</c>, so the file that matters
        /// depends on whether art has been collected yet.
        /// </summary>
        private static void MakeMeshReadable(GameObject prefab)
        {
            MeshFilter filter = prefab.GetComponentInChildren<MeshFilter>(true);
            if (filter == null || filter.sharedMesh == null)
                return;
            string meshPath = AssetDatabase.GetAssetPath(filter.sharedMesh);
            var importer = AssetImporter.GetAtPath(meshPath) as ModelImporter;
            if (importer == null || importer.isReadable)
                return;
            importer.isReadable = true;
            importer.SaveAndReimport();
            Debug.Log($"[{nameof(DemoWeaponBuilder)}] Read/Write turned on for \"{meshPath}\" so the bow string can bend.");
        }

        private static void Build(Weapon weapon)
        {
            string sourcePath = string.IsNullOrEmpty(weapon.Source) ? $"{SourceDir}/{weapon.Name}.fbx" : weapon.Source;
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (source == null)
            {
                Debug.LogError($"[{nameof(DemoWeaponBuilder)}] No model at \"{sourcePath}\".");
                return;
            }

            var root = new GameObject(weapon.Name);
            var mesh = (GameObject)PrefabUtility.InstantiatePrefab(source);
            PrefabUtility.UnpackPrefabInstance(mesh, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            mesh.name = "Mesh";
            mesh.transform.SetParent(root.transform, false);
            if (weapon.Authored)
            {
                if (weapon.MeshScale != Vector3.zero)
                    mesh.transform.localScale = Vector3.Scale(mesh.transform.localScale, weapon.MeshScale);
                CheckAuthored(mesh, weapon);
                Save(root, weapon.Name, 1f, Measure(root));
                return;
            }
            mesh.transform.localRotation = Quaternion.Euler(weapon.StandUp);
            // These models are authored in centimetres, so the import puts a large scale
            // on the model's root. Resizing has to multiply that, not replace it.
            Vector3 importScale = mesh.transform.localScale;

            Bounds bounds = Measure(root);
            float scale = weapon.Length / bounds.size.y;
            mesh.transform.localScale = importScale * scale;

            bounds = Measure(root);
            float gripY = weapon.GripFromButt < 0f
                ? bounds.center.y
                : bounds.min.y + weapon.GripFromButt;
            // Centre the weapon on the hand across its width too, so it does not hang
            // off to one side of the grip.
            Vector3 across = bounds.center;
            if (weapon.CentreOnHaft && HaftCentre(root, gripY, out Vector3 haft))
                across = haft;
            mesh.transform.localPosition -= new Vector3(across.x, gripY, across.z);

            Save(root, weapon.Name, scale, Measure(root));
        }

        /// <summary>
        /// An authored model is trusted to be the right size and the right way up, so only checks it is: its
        /// length along +Y against the one the table gives, to 2%. Measured off the mesh rather than the
        /// renderer, whose bounds are stale for a moment after an edit-mode instantiate.
        /// </summary>
        private static void CheckAuthored(GameObject mesh, Weapon weapon)
        {
            MeshFilter filter = mesh.GetComponentInChildren<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
            {
                Debug.LogError($"[{nameof(DemoWeaponBuilder)}] {weapon.Name}: the model has no mesh.");
                return;
            }
            Bounds bounds = filter.sharedMesh.bounds;
            Vector3 scale = filter.transform.lossyScale;
            float length = bounds.size.y * scale.y;
            if (Mathf.Abs(length - weapon.Length) > weapon.Length * 0.02f)
                Debug.LogWarning($"[{nameof(DemoWeaponBuilder)}] {weapon.Name} measures {length:F3}m along +Y, not the {weapon.Length:F2}m the table expects. " +
                                 "Rebuild it from Art/Weapons/Source~, or change Length.");
        }

        private static void BuildShield()
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>($"{SourceDir}/VikingShield.fbx");
            if (source == null)
            {
                Debug.LogError($"[{nameof(DemoWeaponBuilder)}] No shield model to build.");
                return;
            }

            var root = new GameObject("VikingShield");
            var mesh = (GameObject)PrefabUtility.InstantiatePrefab(source);
            PrefabUtility.UnpackPrefabInstance(mesh, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            mesh.name = "Mesh";
            mesh.transform.SetParent(root.transform, false);

            Material material = AssetDatabase.LoadAssetAtPath<Material>($"{SourceDir}/Materials/VikingShield.mat");
            foreach (MeshRenderer renderer in mesh.GetComponentsInChildren<MeshRenderer>(true))
                renderer.sharedMaterial = material;

            Vector3 importScale = mesh.transform.localScale;
            Bounds bounds = Measure(root);
            float scale = ShieldDiameter / Mathf.Max(bounds.size.x, bounds.size.y);
            mesh.transform.localScale = importScale * scale;

            // Origin on the grip: centred on the disc, then pushed forward so the whole
            // disc sits in front of the hand and the fingers stay in the hollow behind
            // the boss rather than on the painted face.
            bounds = Measure(root);
            mesh.transform.localPosition -= bounds.center - new Vector3(0f, 0f, ShieldGripDepth);

            // A weapon socket is turned so a haft laid along +Y sits in the fist. A
            // shield is not a haft: its face has to end up along the palm normal
            // instead, which is a quarter turn from there. Rotating about the grip,
            // now at the origin, keeps the grip where it is.
            Quaternion toPalm = Quaternion.Euler(0f, 90f, 0f);
            mesh.transform.localRotation = toPalm * mesh.transform.localRotation;
            mesh.transform.localPosition = toPalm * mesh.transform.localPosition;

            Save(root, "VikingShield", scale, Measure(root));
        }

        /// <summary>
        /// The middle of the weapon's cross-section within 4cm of <paramref name="height"/>, in the
        /// root's space: where the haft is at the hand.
        /// </summary>
        private static bool HaftCentre(GameObject root, float height, out Vector3 centre)
        {
            centre = Vector3.zero;
            Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null)
                    continue;
                // The model is not readable at run time and need not be: the editor can still read it this way.
                using (Mesh.MeshDataArray data = Mesh.AcquireReadOnlyMeshData(filter.sharedMesh))
                {
                    var vertices = new Unity.Collections.NativeArray<Vector3>(data[0].vertexCount, Unity.Collections.Allocator.Temp);
                    data[0].GetVertices(vertices);
                    Matrix4x4 toRoot = root.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                    foreach (Vector3 vertex in vertices)
                    {
                        Vector3 p = toRoot.MultiplyPoint3x4(vertex);
                        if (Mathf.Abs(p.y - height) > 0.04f)
                            continue;
                        min = Vector3.Min(min, p);
                        max = Vector3.Max(max, p);
                    }
                    vertices.Dispose();
                }
            }
            if (min.x > max.x)
                return false;
            centre = (min + max) * 0.5f;
            return true;
        }

        private static Bounds Measure(GameObject root)
        {
            Bounds bounds = new Bounds();
            bool first = true;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (first)
                {
                    bounds = renderer.bounds;
                    first = false;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }
            return bounds;
        }

        private static void Save(GameObject root, string name, float scale, Bounds bounds)
        {
            string path = $"{OutputDir}/{name}.prefab";
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            Debug.Log($"[{nameof(DemoWeaponBuilder)}] {name}: scaled x{scale:F3}, size {bounds.size.magnitude:F2}m, grip offset {(-bounds.center).ToString("F3")}.");
        }
    }
}
