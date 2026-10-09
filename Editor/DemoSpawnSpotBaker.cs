using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// Bakes the spots each monster spawn area on the island stands its monsters on, so that
    /// no area ever has to look for the ground itself.
    ///
    /// **Why.** An area left to itself picks a point in its disc and finds the ground under it
    /// with a ray, and with the kit's default `findGroundUpOffsetsRate` of 1 that ray starts
    /// `groundDetectionOffsets` (100m) above the area and stops **at the area's own height**. It
    /// can never find ground lower than the area stands. Every island area stands at the
    /// ground height of its centre, so everything downhill of the centre was out of reach:
    /// measured on 2026-09-24, every miss in all seventeen areas was ground below the centre,
    /// from a third of the outskirts to nine tenths of the woods. The bandit camp was the worst.
    /// Its pad is flattened to exactly 7.5m and its three areas stand at exactly 7.5m, and the
    /// terrain comes out a hair under that, so 99% of the camp's points found nothing and its
    /// marauders, bandits and archers waited minutes each to appear. The kit retries a miss five
    /// seconds later, for ever: 390 warnings in a three-minute session in the editor, and in a
    /// build, which does not log them, a camp that simply stood half empty.
    ///
    /// **The fix is the kit's own.** An area with baked `randomedPosition3Ds` takes a spot from
    /// that list and skips its ground search entirely (GameArea.GetRandomPosition), so the
    /// spots are worked out here, once, where the editor can afford to be thorough. A spot is:
    ///
    /// * bare ground - the first thing a ray from the sky meets is the terrain. The kit's ray
    ///   takes a roof, a tent, a fence or a cliff's rocks for ground and stands a monster on it;
    /// * walkable - on the baked navmesh where it is, not snapped to it from a distance. This is
    ///   also what keeps the deer grounds' spots out of the sea, which a third of them covers;
    /// * room to stand - a body-sized capsule there touches nothing but the ground;
    /// * a way off it - a path runs from the spot to the village green, or at least out of the
    ///   area, so nothing spawns on a ledge or in a hollow it cannot leave.
    ///
    /// Spots are stored in the area's own space, which is how the kit reads them, at the exact
    /// height of the ground. They are taken in order, so the first monsters of an area stand
    /// in different places rather than two on one spot, and one that comes back after a kill
    /// comes back somewhere new. Each area draws its own spots (seeded by its name), so the
    /// camp's three families mingle instead of taking turns on the same ground.
    ///
    /// The areas are baked where they stand, so an area moved or resized by hand keeps the
    /// change. Rerun this after moving one: its spots move with it and then no longer sit on
    /// the ground. It runs by itself after every navmesh bake (Regenerate Island Scene, Rebake
    /// Island Navmesh and so Regenerate Settled Areas), after Add Missing Spawners and after
    /// Place Wildlife, because a spot is only as good as the ground it was checked against.
    ///
    /// Areas under the `Authored` root are the author's and are left alone.
    /// </summary>
    public static class DemoSpawnSpotBaker
    {
        /// <summary>
        /// How many spots each area gets. Several times the most any island area holds at once
        /// (eight), so a wide area is covered rather than sampled, and the monsters that come
        /// back after a kill do not come back to where the last ones fell.
        /// </summary>
        private const int SpotsPerArea = 32;

        /// <summary>How hard to look before settling for fewer spots: tries per spot wanted.</summary>
        private const int TriesPerSpot = 60;

        /// <summary>
        /// The least distance between two spots of one area, as a share of its radius. An open
        /// disc has room for all its spots at this spacing with some to spare, so they spread
        /// over the whole area instead of bunching where the first few happened to land.
        /// </summary>
        private const float SpacingPerRadius = 0.22f;
        private const float LeastSpacing = 1.5f;

        /// <summary>A spot further than this off the navmesh, sideways, is not on it.</summary>
        private const float OnNavMesh = 0.3f;

        /// <summary>
        /// The body a spot has to have room for: a person, lifted clear of anything laid flat
        /// on the ground such as paving.
        /// </summary>
        private const float BodyRadius = 0.4f;
        private const float BodyHeight = 1.8f;
        private const float BodyLift = 0.2f;

        /// <summary>Where the ray that looks for the ground starts: well above anything on the island.</summary>
        private const float SkyHeight = 500f;

        /// <summary>
        /// Written onto every baked area for the day its spot list is emptied. The ray then
        /// reaches half its length above the area and half below, rather than stopping at the
        /// area's own height - which is also what the kit's Bake Random Positions button reads.
        /// </summary>
        private const float FallbackUpOffsetsRate = 0.5f;

        private const int SpotSeed = 3307;

        private const string GameInstancePath = "Assets/OpenMMORPG/Demo/Prefabs/GameInstance.prefab";

        [MenuItem("Open MMORPG/Demo/Bake Monster Spawn Spots (writes DemoMap)", priority = 111)]
        public static void BakeIslandSpots()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            Scene scene = EditorSceneManager.OpenScene(DemoSceneBuilder.ScenePath, OpenSceneMode.Single);
            int changed = Bake(scene);
            if (changed == 0)
            {
                Debug.Log($"[{nameof(DemoSpawnSpotBaker)}] Every spawn area's spots were already up to date, " +
                          $"so {DemoSceneBuilder.ScenePath} was left as it was.");
                return;
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[{nameof(DemoSpawnSpotBaker)}] Rewrote the spots of {changed} spawn area(s) in {DemoSceneBuilder.ScenePath}. " +
                      "A map server running from builds/ needs Build Map Server before it has them.");
        }

        /// <summary>
        /// Bakes the spots of every monster spawn area in the island scene, except those under
        /// the Authored root, and returns how many areas it changed. Checks spots against the
        /// navmesh, so without one it changes nothing and says so. Does not save the scene.
        /// </summary>
        public static int Bake(Scene scene)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GameInstancePath);
            GameInstance game = prefab != null ? prefab.GetComponent<GameInstance>() : null;
            if (game == null)
            {
                Debug.LogError($"[{nameof(DemoSpawnSpotBaker)}] No GameInstance at {GameInstancePath} to read the " +
                               "ground layers from, so no spawn spots were baked.");
                return 0;
            }
            // What the kit itself counts as ground under an entity: every layer but the
            // characters', the item drops', the water and the ignore-raycast ones.
            int ground = game.GetGameEntityGroundDetectionLayerMask();

            Vector2 green = DemoIslandBuilder.VillageCentre;
            if (!NavMesh.SamplePosition(new Vector3(green.x, DemoIslandBuilder.VillageHeight, green.y),
                    out NavMeshHit village, 8f, NavMesh.AllAreas))
            {
                Debug.LogError($"[{nameof(DemoSpawnSpotBaker)}] No navmesh on the village green, so there is nothing to " +
                               "check a spawn spot against and none were baked. Run Rebake Island Navmesh first.");
                return 0;
            }

            // Anything placed or moved earlier in the same editor call has not reached the
            // physics scene yet, and the rays below have to see it.
            Physics.SyncTransforms();

            var report = new System.Text.StringBuilder();
            int areas = 0, changed = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == DemoSceneBuilder.AuthoredRootName)
                {
                    foreach (MonsterSpawnArea theirs in root.GetComponentsInChildren<MonsterSpawnArea>(true))
                        report.AppendLine($"  {theirs.name}: under {DemoSceneBuilder.AuthoredRootName}, left alone - it still finds its own ground");
                    continue;
                }
                foreach (MonsterSpawnArea area in root.GetComponentsInChildren<MonsterSpawnArea>(true))
                {
                    ++areas;
                    if (BakeArea(area, ground, village.position, report))
                        ++changed;
                }
            }
            Debug.Log($"[{nameof(DemoSpawnSpotBaker)}] Baked the spots of {areas} monster spawn area(s), {changed} of them changed:\n{report}");
            return changed;
        }

        /// <summary>Finds and writes one area's spots, and says whether anything about the area changed.</summary>
        private static bool BakeArea(MonsterSpawnArea area, int ground, Vector3 village, System.Text.StringBuilder report)
        {
            var random = new System.Random(SpotSeed ^ StableHash(area.name));
            float reach = area.type == GameAreaType.Square
                ? Mathf.Sqrt(area.squareSizeX * area.squareSizeZ / Mathf.PI)
                : area.randomRadius;
            float spacing = Mathf.Max(LeastSpacing, reach * SpacingPerRadius);

            var spots = new List<Vector3>();
            var path = new NavMeshPath();
            var touching = new Collider[8];
            int covered = 0, offMesh = 0, cramped = 0, cutOff = 0;
            for (int attempt = 0; attempt < SpotsPerArea * TriesPerSpot && spots.Count < SpotsPerArea; ++attempt)
            {
                Vector3 point = area.LocalToWorldPosition(RandomLocalPoint(area, random));
                if (Crowded(spots, point, spacing))
                    continue;

                if (!Physics.Raycast(new Vector3(point.x, SkyHeight, point.z), Vector3.down, out RaycastHit hit,
                        SkyHeight * 2f, ground, QueryTriggerInteraction.Ignore) ||
                    !(hit.collider is TerrainCollider))
                {
                    ++covered;
                    continue;
                }
                if (!NavMesh.SamplePosition(hit.point, out NavMeshHit onMesh, 1f, NavMesh.AllAreas) ||
                    new Vector2(onMesh.position.x - hit.point.x, onMesh.position.z - hit.point.z).magnitude > OnNavMesh)
                {
                    ++offMesh;
                    continue;
                }
                if (Blocked(hit.point, ground, touching))
                {
                    ++cramped;
                    continue;
                }
                if (!Reaches(onMesh.position, village, reach, path))
                {
                    ++cutOff;
                    continue;
                }
                spots.Add(hit.point);
            }

            var local = new List<Vector3>(spots.Count);
            foreach (Vector3 spot in spots)
                local.Add(area.enableRotation ? area.transform.InverseTransformPoint(spot) : spot - area.transform.position);

            bool changed = !Same(area.randomedPosition3Ds, local) ||
                           area.randomPositionMode != GameAreaRandomPositionMode.ByOrder ||
                           !Mathf.Approximately(area.findGroundUpOffsetsRate, FallbackUpOffsetsRate) ||
                           !area.excludeFromAllAreaBaking;
            if (changed)
            {
                var serialized = new SerializedObject(area);
                SerializedProperty baked = serialized.FindProperty("randomedPosition3Ds");
                baked.arraySize = local.Count;
                for (int i = 0; i < local.Count; ++i)
                    baked.GetArrayElementAtIndex(i).vector3Value = local[i];
                serialized.FindProperty("randomPositionAmount").intValue = local.Count;
                serialized.FindProperty("randomPositionMode").enumValueIndex = (int)GameAreaRandomPositionMode.ByOrder;
                serialized.FindProperty("findGroundUpOffsetsRate").floatValue = FallbackUpOffsetsRate;
                // The kit's Bake Random Positions (All Areas) button, pressed on any area in the
                // scene, would otherwise replace these spots with ones found by its own ray.
                serialized.FindProperty("excludeFromAllAreaBaking").boolValue = true;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            report.AppendLine($"  {area.name}: {spots.Count} spots at least {spacing:F1}m apart " +
                              $"(turned away: {covered} under something, {offMesh} off the navmesh, " +
                              $"{cramped} too tight to stand in, {cutOff} with no way out)");
            if (spots.Count == 0)
                Debug.LogWarning($"[{nameof(DemoSpawnSpotBaker)}] {area.name} has no bare, walkable ground to stand a monster on, " +
                                 "so it falls back to the kit's own ground search, which will stand them on rock or roofs. " +
                                 "Move it or widen it, then bake again.", area);
            else if (spots.Count < area.maxAmount)
                Debug.LogWarning($"[{nameof(DemoSpawnSpotBaker)}] {area.name} found only {spots.Count} spots for up to " +
                                 $"{area.maxAmount} monsters, so some will spawn on top of each other.", area);
            return changed;
        }

        /// <summary>A point in the area's own space, spread evenly over it as the kit spreads its own.</summary>
        private static Vector3 RandomLocalPoint(GameArea area, System.Random random)
        {
            if (area.type == GameAreaType.Square)
                return new Vector3(((float)random.NextDouble() - 0.5f) * area.squareSizeX, 0f,
                                   ((float)random.NextDouble() - 0.5f) * area.squareSizeZ);
            float angle = (float)random.NextDouble() * Mathf.PI * 2f;
            float distance = Mathf.Sqrt((float)random.NextDouble()) * area.randomRadius;
            return new Vector3(Mathf.Cos(angle) * distance, 0f, Mathf.Sin(angle) * distance);
        }

        private static bool Crowded(List<Vector3> spots, Vector3 point, float spacing)
        {
            foreach (Vector3 spot in spots)
            {
                float x = spot.x - point.x;
                float z = spot.z - point.z;
                if (x * x + z * z < spacing * spacing)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Whether a monster standing here would be inside something: a wall, a fence, a
        /// villager. The navmesh already keeps its distance from what it was baked around, but
        /// the characters and the animals are taken out of the bake, and the doors are baked
        /// open.
        /// </summary>
        private static bool Blocked(Vector3 foot, int ground, Collider[] touching)
        {
            int count = Physics.OverlapCapsuleNonAlloc(
                foot + Vector3.up * (BodyLift + BodyRadius),
                foot + Vector3.up * (BodyHeight - BodyRadius),
                BodyRadius, touching, ground, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; ++i)
            {
                if (!(touching[i] is TerrainCollider))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Whether a monster standing here can walk off the spot: a path to the village, or
        /// at least one that leaves the area. A path across the island can come back partial
        /// only because the search gave up on the distance, and then it still ends far along
        /// the way; a ledge or a walled-in hollow keeps the path inside the area.
        /// </summary>
        private static bool Reaches(Vector3 from, Vector3 village, float reach, NavMeshPath path)
        {
            if (!NavMesh.CalculatePath(from, village, NavMesh.AllAreas, path))
                return false;
            if (path.status == NavMeshPathStatus.PathComplete)
                return true;
            Vector3[] corners = path.corners;
            return path.status == NavMeshPathStatus.PathPartial && corners.Length > 0 &&
                   Vector3.Distance(from, corners[corners.Length - 1]) > reach;
        }

        private static bool Same(List<Vector3> baked, List<Vector3> spots)
        {
            if (baked == null || baked.Count != spots.Count)
                return false;
            for (int i = 0; i < spots.Count; ++i)
            {
                if ((baked[i] - spots[i]).sqrMagnitude > 1e-6f)
                    return false;
            }
            return true;
        }

        /// <summary>FNV-1a over the name: the same seed in every editor session, which string.GetHashCode does not promise.</summary>
        private static int StableHash(string text)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (char c in text)
                    hash = (hash ^ c) * 16777619;
                return (int)hash;
            }
        }
    }
}
