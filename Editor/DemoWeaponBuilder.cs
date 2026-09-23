using UnityEditor;
using UnityEngine;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// Turns the raw Malagen weapon models into equippable prefabs.
    ///
    /// The source models cannot be attached to a hand as they are. They are authored at
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
        private const string OutputDir = "Assets/OpenMMORPG/Demo/Prefabs/GamePlay/Equipments";

        /// <summary>
        /// How a source model becomes a prefab. <see cref="StandUp"/> brings the model's
        /// long axis to +Y; <see cref="Length"/> is the real-world size to scale it to;
        /// <see cref="GripFromButt"/> is how far along the weapon the hand sits, measured
        /// from the bottom once scaled, or negative to grip at the middle.
        /// </summary>
        private struct Weapon
        {
            public string Name;
            public Vector3 StandUp;
            public float Length;
            public float GripFromButt;
        }

        private static readonly Weapon[] Weapons =
        {
            // Hand-and-a-half sword: a hand's width above the pommel.
            new Weapon { Name = "Longsword",  StandUp = new Vector3(-90f, 0f, 0f), Length = 1.15f, GripFromButt = 0.13f },
            new Weapon { Name = "ShortSword", StandUp = new Vector3(-90f, 0f, 0f), Length = 0.78f, GripFromButt = 0.10f },
            new Weapon { Name = "Axe",        StandUp = new Vector3(-90f, 0f, 0f), Length = 0.85f, GripFromButt = 0.12f },
            // A bow is held at its middle, not its end.
            new Weapon { Name = "Bow",        StandUp = new Vector3(-90f, 0f, 0f), Length = 1.70f, GripFromButt = -1f },
            // A staff is gripped above the midpoint so the head stands clear.
            new Weapon { Name = "MageStaff",  StandUp = new Vector3(-90f, 0f, 0f), Length = 1.65f, GripFromButt = 0.60f },
            new Weapon { Name = "Pickaxe",    StandUp = new Vector3(-90f, 0f, 0f), Length = 0.80f, GripFromButt = 0.12f },
            // Worn and nocked rather than gripped, so both balance at their middle.
            new Weapon { Name = "Quiver",     StandUp = new Vector3(-90f, 0f, 0f), Length = 0.45f, GripFromButt = -1f },
            new Weapon { Name = "Arrow",      StandUp = new Vector3(0f, 0f, 90f),  Length = 0.72f, GripFromButt = -1f },
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
            // After the loop, not inside it: the bow wants the arrow prefab, which is built
            // two entries later.
            DressBow();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        /// <summary>
        /// Gives the bow the component that draws it: <see cref="DemoBow"/>, which bends the
        /// string back to the drawing hand, flexes the limbs with it, and carries, nocks and
        /// looses an arrow.
        ///
        /// It has to be added here rather than by hand, because <see cref="Save"/> writes each
        /// prefab out from a freshly built object - anything added in the inspector lasts
        /// until the next "Build Weapon Prefabs" and no longer.
        ///
        /// <see cref="DemoBow"/> is an <c>EquipmentEntity</c>, which is how it hears the kit's
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
                DemoBow bow = root.GetComponent<DemoBow>();
                if (bow == null)
                    bow = root.AddComponent<DemoBow>();
                bow.arrowPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{OutputDir}/Arrow.prefab");
                if (bow.arrowPrefab == null)
                    Debug.LogWarning($"[{nameof(DemoWeaponBuilder)}] No arrow prefab, so the bow will draw an empty string.");
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
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>($"{SourceDir}/{weapon.Name}.fbx");
            if (source == null)
            {
                Debug.LogError($"[{nameof(DemoWeaponBuilder)}] No model at \"{SourceDir}/{weapon.Name}.fbx\".");
                return;
            }

            var root = new GameObject(weapon.Name);
            var mesh = (GameObject)PrefabUtility.InstantiatePrefab(source);
            PrefabUtility.UnpackPrefabInstance(mesh, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            mesh.name = "Mesh";
            mesh.transform.SetParent(root.transform, false);
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
            mesh.transform.localPosition -= new Vector3(bounds.center.x, gripY, bounds.center.z);

            Save(root, weapon.Name, scale, Measure(root));
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
