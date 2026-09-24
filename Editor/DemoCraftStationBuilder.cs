using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// Turns four places in the village into crafting stations: the cookfire, the smith's
    /// anvil, a fletcher's bench and an alchemist's table.
    ///
    /// The recipes existed before these did, all of them craftable anywhere from the HUD's
    /// Craft button (`canBeCraftedWithoutSource`), which works and says nothing. Tying each
    /// one to a place gives the village a reason to have a smith in it, and gives the player
    /// somewhere to go - which is most of what a crafting system is for in an MMO.
    ///
    /// **`WorkbenchEntity`, not `QueuedWorkbenchEntity`.** The queued one is the richer of
    /// the two - a shared queue, a crafting timer - but it reaches its recipes through
    /// `ItemCraftFormula` assets registered against its `SourceId`, and that registration
    /// happens in `PrepareRelatesData`, which only runs for a building the *database* knows
    /// about. Buildings enter the database through a `BuildingItem`, i.e. through something
    /// a player builds. A station placed in a scene has no build item, so its formulas would
    /// never register and its window would open empty. `WorkbenchEntity` carries its recipes
    /// **embedded**, the UI reads them straight off the entity (`UIItemCrafts`), and the
    /// server resolves the station by live object id - no database entry anywhere.
    ///
    /// The recipes themselves are still read from the `ItemCraftFormula` assets
    /// <see cref="DemoProgressionBuilder"/> writes, so there is one source of truth for what
    /// a stew costs; this copies it onto the station rather than restating it.
    /// </summary>
    public static class DemoCraftStationBuilder
    {
        private const string ScenePath = "Assets/OpenMMORPG/Demo/Scenes/DemoMap.unity";
        private const string PropDir = "Assets/Plugins/Quaternius/Props/Models";
        private const string VillageRoot = "Village";

        /// <summary>
        /// A station is a place, a set of recipes, and the props that say what it is.
        ///
        /// `Anchor` is a **path from the village root** to a prop the village already has,
        /// and the builder hangs the station on it rather than putting a second one beside
        /// it. A path rather than a name because the village has two `Anvil_Log`s - one in
        /// the open and one in the smithy - and the station belongs on the second.
        ///
        /// Three of the four are furniture that was already there and already in the right
        /// place: the cookfire on the green, the anvil in the smithy, the potion seller's
        /// stall in the market row. Only the fletcher has nothing to hang on, so it gets
        /// `Props` and a position and is built.
        /// </summary>
        private struct Station
        {
            public string Name;
            public string Title;
            public string Anchor;
            public Piece[] Props;
            public Vector3 Position;
            public float Yaw;
            public string[] Recipes;
            public bool KeepLit;
        }

        /// <summary>
        /// One prop in a station, and where it sits relative to the station's origin.
        ///
        /// **The offsets are authored rather than spread evenly, because this pack's props
        /// do not all stand on their own pivot.** `Workbench_Vice` and `Workbench_Drawers`
        /// are *parts of* a `Workbench`: their meshes are modelled at bench-top height
        /// (0.62-0.90 and 0.88-1.13 above the pivot) and are meant to be placed at the same
        /// origin as the bench, which then holds them up. Laid out in a row with no bench
        /// under them, as the first version of this did, they hang in the air exactly one
        /// bench-height off the ground and the station looks empty apart from its trimmings.
        ///
        /// So a piece is either furniture standing on the ground at an offset, or a fitting
        /// sharing the bench's origin. There is no rule that tells the two apart from the
        /// model alone - it is measurement, and the table is where the measuring ends up.
        /// </summary>
        private struct Piece
        {
            public string Model;
            public Vector3 Offset;

            public Piece(string model, float x, float y, float z)
            {
                Model = model;
                Offset = new Vector3(x, y, z);
            }
        }

        private static readonly Station[] Stations =
        {
            new Station
            {
                Name = "Station_Cookfire", Title = "Cookfire", Anchor = "Props/Firepit",
                Recipes = new[] { "Stew" },
                KeepLit = true,
            },
            new Station
            {
                // The smithy's own anvil, not the spare one standing in the open: House_2's
                // interior is a furnished forge - anvil, bellows, whetstone, hammer rack -
                // and a forge that is a room reads as a trade rather than as a prop.
                Name = "Station_Forge", Title = "Forge", Anchor = "House_2/Interior/Anvil_Log",
                Recipes = new[] { "IronShortsword", "IronLongsword", "PaintedRoundShield", "KnightHelm" },
            },
            new Station
            {
                Name = "Station_Fletcher", Title = "Fletcher's Bench",
                Props = new[]
                {
                    new Piece("Workbench", 0f, 0f, 0f),
                    new Piece("Workbench_Vice", 0f, 0f, 0f),      // mounted on the bench
                    new Piece("WeaponStand", 1.7f, 0f, 0.1f),
                },
                Position = new Vector3(-27.5f, 5.5f, 17.5f), Yaw = 300f,
                // Not the bracers or the leather: those are field work - see
                // DemoProgressionBuilder.Recipe.
                Recipes = new[] { "HuntingBow", "YewLongbow", "RangerBoots", "RangerHood" },
            },
            new Station
            {
                // The potion seller's stall, which the market row already had. A bench of
                // bottles against somebody's wall read as furniture left outdoors; a stall
                // between the greengrocer and the tavern reads as a shop, and it stays in
                // plain sight, which matters when the other three are a fire, a room and a
                // workbench.
                Name = "Station_Apothecary", Title = "Apothecary's Stall",
                Anchor = "Props/Stall_Potions",
                Recipes = new[] { "MinorHealingPotion", "SpicedWine" },
            },
        };

        /// <summary>How close the player has to stand. The kit's default build distance is 5m.</summary>
        private const float ActivateDistance = 3.5f;

        /// <summary>
        /// Switched off between 2026-09-18 and 2026-09-22, because it killed the map server.
        /// Kept as a record, because the first diagnosis was wrong in a way worth not
        /// repeating.
        ///
        /// A `WorkbenchEntity` is a `BuildingEntity`, and buildings are *saved*: a
        /// scene-placed one persists to the `buildings` table with `isSceneObject = 1`. On
        /// the next start the map server read those rows back, `CreateBuildingEntity`
        /// returned **null**, the caller dereferenced it, and the server died before it
        /// could register - every login saying the map server was not ready. It cost a
        /// day's play.
        ///
        /// **The first reading was "a station has no `BuildingItem`, so the prefab lookup
        /// misses". True, and not the cause.** `MapNetworkManager.PreSpawnEntities` matches
        /// each saved row back to its scene instance through `initializingBuildingDicts`
        /// keyed on `building.Id`, and only falls through to `CreateBuildingEntity` when
        /// that match **fails**. The killer is an **orphan row** - one whose id names a
        /// station the scene no longer has.
        ///
        /// And rows orphaned because a scene building's key is
        /// `ZString.Concat(ChannelId, '_', MapInfo.Id, '_', Identity.SceneObjectId)`, while
        /// the scene object id was whatever `LiteNetLibIdentity` generated - `Firepit_1`,
        /// `Anvil_Log_2` - which renumbers when the village is rebuilt or a prop is added
        /// ahead of it. So the fix is not to give the stations build items (that would
        /// recover each orphan as a *second* station beside the real one): it is to pin the
        /// ids, which <see cref="PinSceneObjectId"/> now does.
        ///
        /// **One-time cost of turning this back on:** rows written under the old generated
        /// ids are still orphans and still fatal. Clear `buildings` where
        /// `isSceneObject = 1` once. Done 2026-09-22.
        /// </summary>
        private const bool Enabled = true;

        [MenuItem("Open MMORPG/Demo/Build Craft Stations", priority = 151)]
        public static void Build()
        {
            if (!Enabled)
            {
                Debug.LogError($"[{nameof(DemoCraftStationBuilder)}] Disabled: scene-placed " +
                               "WorkbenchEntity buildings persist to the `buildings` table and the map " +
                               "server dies loading them back (see the note on `Enabled`). Clear any " +
                               "rows with isSceneObject = 1 before running a server.");
                return;
            }

            bool opened = false;
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            if (!scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
                opened = true;
            }

            GameObject village = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == VillageRoot)
                    village = root;
            }
            if (village == null)
            {
                Debug.LogError($"[{nameof(DemoCraftStationBuilder)}] No \"{VillageRoot}\" root in {ScenePath}.");
                return;
            }

            Transform props = village.transform.Find("Props");
            if (props == null)
            {
                Debug.LogError($"[{nameof(DemoCraftStationBuilder)}] The village has no \"Props\" child.");
                return;
            }

            Dictionary<string, ItemCraftFormula> formulas = LoadFormulas();
            int built = 0, bound = 0;
            var stationed = new HashSet<string>();
            var hosts = new HashSet<GameObject>();

            foreach (Station station in Stations)
            {
                Transform host = Host(props, station);
                if (host == null)
                    continue;

                WorkbenchEntity bench = Attach(host.gameObject, station);
                int written = WriteRecipes(bench, station, formulas, stationed);
                hosts.Add(host.gameObject);
                bound += written;
                ++built;
                Debug.Log($"[{nameof(DemoCraftStationBuilder)}] {station.Title} at " +
                          $"{host.position} with {written} recipe(s).");
            }

            int cleared = ClearOldStations(village, hosts);

            // Whether a recipe also needs a station is DemoProgressionBuilder's to say, not
            // this builder's - see the note on its Recipe table. This one used to clear
            // `canBeCraftedWithoutSource` on everything it bound, which made the answer depend
            // on which of the two builders ran last.

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            if (opened)
                EditorSceneManager.CloseScene(scene, true);
            AssetDatabase.SaveAssets();

            Debug.Log($"[{nameof(DemoCraftStationBuilder)}] {built} station(s) in the village carrying " +
                      $"{bound} recipe(s)" +
                      $"{(cleared > 0 ? $", {cleared} old one(s) cleared away" : "")}. " +
                      "They live under Village/Props, so Regenerate Settled Areas removes them - " +
                      "run this again after one.");
        }

        /// <summary>
        /// Takes the workbench back off anything that used to be a station and is not one
        /// now, and removes the prop groups built for stations that have since moved onto
        /// furniture of their own.
        ///
        /// Without this, moving the forge from the anvil in the open to the anvil in the
        /// smithy leaves **two** forges: the builder writes the new one and never touches
        /// the old, which goes on working because it is a perfectly good scene object. The
        /// same is true of any station whose props it once built.
        /// </summary>
        private static int ClearOldStations(GameObject village, HashSet<GameObject> hosts)
        {
            int cleared = 0;
            var stale = new List<GameObject>();
            foreach (WorkbenchEntity bench in village.GetComponentsInChildren<WorkbenchEntity>(true))
            {
                if (hosts.Contains(bench.gameObject))
                    continue;
                stale.Add(bench.gameObject);
            }

            foreach (GameObject go in stale)
            {
                // A group this builder made has nothing else to be; anything else is village
                // furniture that was borrowed, so only the component comes off.
                if (go.name.StartsWith("Station_"))
                {
                    Debug.Log($"[{nameof(DemoCraftStationBuilder)}] Removed \"{go.name}\", whose station " +
                              "now lives on furniture the village already had.");
                    Object.DestroyImmediate(go);
                }
                else
                {
                    Object.DestroyImmediate(go.GetComponent<WorkbenchEntity>());
                    // The click box Attach put on it, which furniture never has of its own.
                    var box = go.GetComponent<BoxCollider>();
                    if (box != null && box.isTrigger)
                        Object.DestroyImmediate(box);
                    Debug.Log($"[{nameof(DemoCraftStationBuilder)}] \"{go.name}\" is furniture again; " +
                              "its station moved elsewhere.");
                }
                ++cleared;
            }
            return cleared;
        }

        /// <summary>
        /// The object the station's component goes on: the prop the village already has, or
        /// a new group built from <see cref="Station.Props"/>.
        /// </summary>
        private static Transform Host(Transform props, Station station)
        {
            if (!string.IsNullOrEmpty(station.Anchor))
            {
                Transform anchor = props.parent.Find(station.Anchor);
                if (anchor == null)
                {
                    Debug.LogWarning($"[{nameof(DemoCraftStationBuilder)}] {station.Title} wants " +
                                     $"\"{station.Anchor}\", which is not in the village.");
                }
                return anchor;
            }

            Transform existing = props.Find(station.Name);
            if (existing != null)
                Object.DestroyImmediate(existing.gameObject);

            var group = new GameObject(station.Name);
            group.transform.SetParent(props, false);
            group.transform.position = station.Position;
            group.transform.rotation = Quaternion.Euler(0f, station.Yaw, 0f);

            foreach (Piece piece in station.Props)
            {
                GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>($"{PropDir}/{piece.Model}.fbx");
                if (source == null)
                {
                    Debug.LogWarning($"[{nameof(DemoCraftStationBuilder)}] No prop \"{piece.Model}\".");
                    continue;
                }
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(source, group.transform);
                instance.name = piece.Model;
                instance.transform.localPosition = piece.Offset;
                // Composed, never assigned - the import's own axis correction lives in the
                // prefab's rotation, and writing over it stands these models on edge. Same
                // trap as DemoMenuStageBuilder.Place.
                instance.transform.localRotation = instance.transform.localRotation * Quaternion.identity;
            }

            ReportFloaters(group.transform, station);
            return group.transform;
        }

        /// <summary>
        /// Says so when a piece is hanging in the air.
        ///
        /// This is the check that would have caught the alchemist's table being three
        /// trimmings around an invisible bench: a fitting whose model sits at bench height
        /// looks fine in the table and floats in the scene, and nothing about placing it
        /// complains. Anything whose lowest point is well above the station's own ground
        /// and well above whatever is under it is either mounted on something that is not
        /// there, or in the wrong place.
        /// </summary>
        private static void ReportFloaters(Transform group, Station station)
        {
            float ground = group.position.y;
            // The tallest thing standing on the ground is what a fitting could be sitting on.
            float support = ground;
            foreach (Transform child in group)
            {
                Bounds b = Measure(child.gameObject);
                if (b.size == Vector3.zero || b.min.y > ground + 0.05f)
                    continue;
                support = Mathf.Max(support, b.max.y);
            }

            foreach (Transform child in group)
            {
                Bounds b = Measure(child.gameObject);
                if (b.size == Vector3.zero)
                    continue;
                bool onGround = b.min.y <= ground + 0.05f;
                bool onSupport = Mathf.Abs(b.min.y - support) <= 0.25f || b.min.y < support;
                if (onGround || onSupport)
                    continue;
                Debug.LogWarning($"[{nameof(DemoCraftStationBuilder)}] {station.Title}: " +
                                 $"\"{child.name}\" hangs at y={b.min.y:F2} with the ground at " +
                                 $"{ground:F2} and nothing higher than {support:F2} under it. " +
                                 "It is probably a fitting that needs a bench at the same origin.");
            }
        }

        /// <summary>
        /// Puts the workbench component on, with a collider to be activated through.
        ///
        /// **Not attackable and no lifetime.** A `WorkbenchEntity` is a `BuildingEntity`,
        /// which is a `DamageableEntity` - left at its defaults a station has 100 hp, can be
        /// hit, and can be destroyed by a player or a passing bandit's stray swing. These are
        /// village furniture, so `canBeAttacked` goes off and `lifeTime` stays at 0 (forever).
        /// </summary>
        private static WorkbenchEntity Attach(GameObject host, Station station)
        {
            var bench = host.GetComponent<WorkbenchEntity>();
            if (bench == null)
                bench = host.AddComponent<WorkbenchEntity>();

            var serialized = new SerializedObject(bench);
            SetIfPresent(serialized, "entityTitle", station.Title);
            SetIfPresent(serialized, "canBeAttacked", false);
            SetIfPresent(serialized, "notBeingSelectedOnClick", false);
            SerializedProperty distance = serialized.FindProperty("activatableDistance");
            if (distance != null)
                distance.floatValue = ActivateDistance;
            SerializedProperty life = serialized.FindProperty("lifeTime");
            if (life != null)
                life.floatValue = 0f;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            // Something to aim at, on the station's own object. A click resolves to the
            // transform of the collider it hits and looks for the entity *there*, not in its
            // parents (`PlayerCharacterController.UpdateInput`: `GetComponent<ITargetableEntity>`
            // on `GetRaycastTransform`) - so a collider on a child is a wall, and the kit takes
            // the click for a click on the ground. That is how three of the four stations went
            // dead to the mouse (found 2026-09-24): the cookfire, the anvil and the potion stall
            // are Quaternius furniture whose only collider is their `Collision` child, and this
            // used to add a box only when there was no collider anywhere. The Fletcher's Bench,
            // built from bare props, got one and was the only station that answered a click.
            // Same trap as the door handles ([[demo-scene-traversability]]).
            if (host.GetComponent<Collider>() == null)
            {
                bool solidAlready = host.GetComponentInChildren<Collider>() != null;
                Bounds local = MeasureLocal(host);
                var box = host.AddComponent<BoxCollider>();
                box.center = local.center;
                box.size = local.size == Vector3.zero ? Vector3.one : local.size;
                // Furniture that is already solid gets a trigger: this box is only there to
                // be clicked, the click search checks for an entity before it skips a trigger,
                // and a second solid shell round the prop would only widen what the navmesh
                // (baked from physics colliders) carves around it. A group of bare props has
                // nothing else to stand in the way, so its box stays solid.
                box.isTrigger = solidAlready;
            }

            if (station.KeepLit)
                KeepLit(host, station);

            PinSceneObjectId(host, station.Name);

            EditorUtility.SetDirty(host);
            return bench;
        }

        /// <summary>
        /// Gives a station a scene object id chosen by name, which is what keeps its saved
        /// rows from orphaning - and orphaned rows are what used to kill the map server.
        ///
        /// A scene building's database key is
        /// `ZString.Concat(ChannelId, '_', MapInfo.Id, '_', Identity.SceneObjectId)`, so it
        /// is only ever as stable as that last part. Left alone, `LiteNetLibIdentity`
        /// generates one - `Firepit_1`, `Anvil_Log_2` - from the object's name and however
        /// many things it has already numbered, so rebuilding the village or adding a prop
        /// ahead of it in the hierarchy quietly renumbers the station. The row saved under
        /// the old number then matches nothing in the scene, `PreSpawnEntities` falls
        /// through to `CreateBuildingEntity`, which cannot find a prefab for a station that
        /// has no `BuildingItem`, returns null, and the caller dereferences it.
        ///
        /// `Station_Forge` does not renumber. Name the id after the thing rather than after
        /// whatever is hosting it this week and the row survives every rebuild - the same
        /// reason the shrines pin theirs (see DemoShrineBuilder).
        ///
        /// **Rows saved under the old generated ids are still orphans** and still fatal.
        /// They have to be cleared once, from the `buildings` table where
        /// `isSceneObject = 1`; after that the pinning keeps them from coming back.
        /// </summary>
        private static void PinSceneObjectId(GameObject host, string id)
        {
            var identity = host.GetComponent<LiteNetLibManager.LiteNetLibIdentity>();
            if (identity == null)
                return;
            if (identity.SceneObjectId == id)
                return;
            var serialized = new SerializedObject(identity);
            serialized.FindProperty("sceneObjectId").stringValue = id;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(host);
        }

        /// <summary>
        /// Puts a station's fire on `Always`, so it is burning whenever someone might want
        /// to cook on it.
        ///
        /// The village's campfire was lit at dusk and out after dawn with the street
        /// torches, which is right for a fire that is scenery - it is the thing a player
        /// arriving at night walks toward. It is wrong for one that is now a cooking
        /// station: standing at a cold fire pit at noon and being offered a stew reads as
        /// a bug, and lighting it is a great deal simpler than explaining it.
        ///
        /// **This builder owns the schedule rather than `DemoSceneBuilder`**, because the
        /// reason the fire must stay in is that it is a station, and stations are this
        /// builder's business. The two cannot disagree: `Regenerate Settled Areas` puts the
        /// village's own night schedule back *and* destroys the stations, and running this
        /// afterwards - which that already requires - lights it again. A fire on a night
        /// schedule means there is no station on it.
        ///
        /// The bandit camp's fire keeps the old hours. Nobody cooks there.
        /// </summary>
        private static void KeepLit(GameObject host, Station station)
        {
            var torch = host.GetComponentInChildren<MultiplayerARPG.Demo.DemoTorch>(true);
            if (torch == null)
            {
                Debug.LogWarning($"[{nameof(DemoCraftStationBuilder)}] {station.Title} should stay lit, " +
                                 $"but \"{host.name}\" has no DemoTorch under it.");
                return;
            }
            if (torch.schedule == MultiplayerARPG.Demo.DemoTorch.Schedule.Always)
                return;
            torch.schedule = MultiplayerARPG.Demo.DemoTorch.Schedule.Always;
            EditorUtility.SetDirty(torch);
            Debug.Log($"[{nameof(DemoCraftStationBuilder)}] {station.Title}'s fire now burns around the " +
                      "clock; it was on the street torches' night schedule.");
        }

        private static void SetIfPresent(SerializedObject serialized, string field, object value)
        {
            SerializedProperty property = serialized.FindProperty(field);
            if (property == null)
                return;
            if (value is bool b && property.propertyType == SerializedPropertyType.Boolean)
                property.boolValue = b;
            else if (value is string s && property.propertyType == SerializedPropertyType.String)
                property.stringValue = s;
        }

        /// <summary>
        /// The size of a prop's **mesh**, which is not the size of its renderers.
        ///
        /// **`ParticleSystemRenderer` is excluded, and that is not a detail.** The cookfire
        /// carries a flame, and a particle system reports the bounds of the volume its
        /// particles may occupy - 34m by 22m for that one, against 0.8m of actual firepit.
        /// This measurement sizes the collider a player activates the station through, so
        /// including the flame would have wrapped the whole village green in a box. It only
        /// escaped at first because the Quaternius props ship their own `Collision` meshes and
        /// the box was never added to them; since 2026-09-24 it is (see `Attach`), sized by
        /// <see cref="MeasureLocal"/>, which leaves particles out the same way. The same
        /// inflated bounds once made every brazier look buried.
        /// </summary>
        private static Bounds Measure(GameObject root)
        {
            Bounds bounds = default;
            bool any = false;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is ParticleSystemRenderer)
                    continue;
                if (!any) { bounds = renderer.bounds; any = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            return any ? bounds : new Bounds(root.transform.position, Vector3.one);
        }

        /// <summary>
        /// <see cref="Measure"/> in the prop's own frame, for a collider on it. A world-space
        /// box is only right for a prop that is not turned, and the anvil stands in a smithy
        /// that is. Same exclusion of particle renderers, for the same reason.
        /// </summary>
        private static Bounds MeasureLocal(GameObject root)
        {
            Transform frame = root.transform;
            Bounds bounds = default;
            bool any = false;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is ParticleSystemRenderer)
                    continue;
                Bounds own = renderer.localBounds;
                for (int i = 0; i < 8; ++i)
                {
                    var corner = new Vector3(
                        (i & 1) == 0 ? own.min.x : own.max.x,
                        (i & 2) == 0 ? own.min.y : own.max.y,
                        (i & 4) == 0 ? own.min.z : own.max.z);
                    Vector3 point = frame.InverseTransformPoint(renderer.transform.TransformPoint(corner));
                    if (!any) { bounds = new Bounds(point, Vector3.zero); any = true; }
                    else bounds.Encapsulate(point);
                }
            }
            return any ? bounds : new Bounds(Vector3.zero, Vector3.one);
        }

        /// <summary>
        /// Copies each of the station's recipes off its formula asset and onto the bench.
        /// </summary>
        private static int WriteRecipes(WorkbenchEntity bench, Station station,
                                        Dictionary<string, ItemCraftFormula> formulas,
                                        HashSet<string> stationed)
        {
            var serialized = new SerializedObject(bench);
            SerializedProperty crafts = serialized.FindProperty("itemCrafts");
            var chosen = new List<ItemCraftFormula>();
            foreach (string product in station.Recipes)
            {
                if (!formulas.TryGetValue(product, out ItemCraftFormula formula))
                {
                    Debug.LogWarning($"[{nameof(DemoCraftStationBuilder)}] {station.Title} wants a recipe " +
                                     $"for \"{product}\"; no formula makes one. Run Build Progression first.");
                    continue;
                }
                chosen.Add(formula);
                stationed.Add(product);
            }

            crafts.arraySize = chosen.Count;
            for (int i = 0; i < chosen.Count; ++i)
            {
                var source = new SerializedObject(chosen[i]).FindProperty("itemCraft");
                SerializedProperty target = crafts.GetArrayElementAtIndex(i);
                Copy(source, target, "craftingItem");
                Copy(source, target, "amount");
                Copy(source, target, "requireGold");

                SerializedProperty from = source.FindPropertyRelative("requireItems");
                SerializedProperty to = target.FindPropertyRelative("requireItems");
                to.arraySize = from.arraySize;
                for (int j = 0; j < from.arraySize; ++j)
                {
                    SerializedProperty a = from.GetArrayElementAtIndex(j);
                    SerializedProperty b = to.GetArrayElementAtIndex(j);
                    b.FindPropertyRelative("item").objectReferenceValue =
                        a.FindPropertyRelative("item").objectReferenceValue;
                    b.FindPropertyRelative("amount").intValue =
                        a.FindPropertyRelative("amount").intValue;
                }
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(bench);
            return chosen.Count;
        }

        private static void Copy(SerializedProperty from, SerializedProperty to, string field)
        {
            SerializedProperty a = from.FindPropertyRelative(field);
            SerializedProperty b = to.FindPropertyRelative(field);
            if (a == null || b == null)
                return;
            if (a.propertyType == SerializedPropertyType.ObjectReference)
                b.objectReferenceValue = a.objectReferenceValue;
            else if (a.propertyType == SerializedPropertyType.Integer)
                b.intValue = a.intValue;
        }

        private static Dictionary<string, ItemCraftFormula> LoadFormulas()
        {
            var formulas = new Dictionary<string, ItemCraftFormula>();
            foreach (string guid in AssetDatabase.FindAssets("t:ItemCraftFormula", new[] { "Assets/OpenMMORPG/Demo" }))
            {
                var formula = AssetDatabase.LoadAssetAtPath<ItemCraftFormula>(AssetDatabase.GUIDToAssetPath(guid));
                if (formula == null || formula.ItemCraft == null || formula.ItemCraft.CraftingItem == null)
                    continue;
                formulas[formula.ItemCraft.CraftingItem.name] = formula;
            }
            return formulas;
        }

    }
}
