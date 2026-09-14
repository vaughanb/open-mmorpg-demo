using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// Checks everything standing outside the houses: that it rests on the ground, that
    /// it is not inside something else, and that a player cannot walk through it.
    ///
    /// The outdoor faults are not the indoor ones. Out here the ground is not a floor at
    /// a known height — it is terrain that rises away from the village — so a prop
    /// placed at a fixed height is buried on one side of the green and floating on the
    /// other. And several of the models hang from their origin rather than stand on it,
    /// which puts them underground entirely.
    /// </summary>
    public static class DemoSceneAudit
    {
        /// <summary>How far a prop may sit off the ground before it is worth reporting.</summary>
        private const float GroundLimit = 0.10f;

        /// <summary>Below this an intersection is two models touching rather than clashing.</summary>
        private const float OverlapLimit = 0.05f;

        /// <summary>
        /// Props that belong in the same place as each other: a tripod stands over a
        /// firepit and a cauldron hangs in it, so those are arrangements, not clashes.
        /// </summary>
        private static readonly string[][] Nested =
        {
            new[] { "Firepit", "CampfireTripod" },
            new[] { "Firepit", "Cauldron" },
            new[] { "CampfireTripod", "Cauldron" },
        };

        /// <summary>
        /// Things meant to have no collision: the sea is a surface to look at, and the
        /// shore pebbles are ankle-high litter that should not trip anyone.
        /// </summary>
        private static readonly string[] Walkable = { "Sea", "Shore" };

        [MenuItem("Open MMORPG/Demo/Audit Scene Placement")]
        public static void Audit()
        {
            var report = new System.Text.StringBuilder();
            int faults = 0;

            faults += AuditGroup(GameObject.Find("Village/Props"), report, true);
            faults += AuditGroup(GameObject.Find("BanditCamp"), report, true);
            faults += AuditGroup(GameObject.Find("Cliffs"), report, false);
            faults += AuditGroup(GameObject.Find("Village/Ground"), report, false);
            faults += AuditGroup(GameObject.Find("Nature/Rocks"), report, false);
            faults += AuditGroup(GameObject.Find("Nature/Shore"), report, false);
            faults += AuditColliders(report);

            if (faults == 0)
                Debug.Log($"[{nameof(DemoSceneAudit)}] Scene placement clean.\n{report}");
            else
                Debug.LogWarning($"[{nameof(DemoSceneAudit)}] {faults} fault(s).\n{report}");
        }

        /// <summary>An object's own box, kept oriented rather than flattened to world axes.</summary>
        private struct Box
        {
            public string Name;
            public Vector3 Centre;
            public Vector3 Extents;
            public float Yaw;
        }

        /// <summary>
        /// Checks one group of props: ground contact for all of them, and for the ones
        /// placed by hand, whether they stand in each other or in a house.
        ///
        /// Scattered rock is held to a looser standard: a boulder half sunk into the hill
        /// looks like it belongs there, while one balanced exactly on the surface looks
        /// dropped. Only rock left hanging in the air is a fault.
        /// </summary>
        private static int AuditGroup(GameObject group, System.Text.StringBuilder report, bool handPlaced)
        {
            if (group == null)
                return 0;

            var lines = new List<string>();
            var boxes = new List<Box>();

            foreach (Transform prop in group.transform)
            {
                if (prop.GetComponentInChildren<Renderer>() == null)
                    continue;

                Bounds local = DemoVillageBuilder.LocalBounds(prop, prop);
                Vector3 scale = prop.lossyScale;
                float baseY = prop.position.y + local.min.y * scale.y;
                float ground = DemoIslandBuilder.HeightAt(prop.position.x, prop.position.z);
                float clearance = baseY - ground;
                if (clearance > GroundLimit)
                    lines.Add($"  floating: {prop.name} by {clearance:F2}");
                else if (handPlaced && clearance < -GroundLimit)
                    lines.Add($"  buried: {prop.name} by {-clearance:F2}");

                boxes.Add(new Box
                {
                    Name = prop.name,
                    Centre = prop.TransformPoint(local.center),
                    Extents = Vector3.Scale(local.extents, scale),
                    Yaw = prop.eulerAngles.y,
                });
            }

            if (handPlaced)
            {
                for (int i = 0; i < boxes.Count; ++i)
                {
                    for (int j = i + 1; j < boxes.Count; ++j)
                    {
                        if (IsNested(boxes[i].Name, boxes[j].Name))
                            continue;
                        float depth = Penetration(boxes[i], boxes[j]);
                        if (depth > OverlapLimit)
                            lines.Add($"  inside each other: {boxes[i].Name} / {boxes[j].Name} by {depth:F2}");
                    }
                }

                GameObject village = GameObject.Find("Village");
                if (village != null)
                {
                    foreach (Transform house in village.transform)
                    {
                        if (!house.name.StartsWith("House"))
                            continue;
                        Box shell = HouseShell(house);
                        foreach (Box box in boxes)
                        {
                            if (Penetration(shell, box) > OverlapLimit)
                                lines.Add($"  through {house.name}: {box.Name}");
                        }
                    }
                }
            }

            report.AppendLine($"{group.name}: {(lines.Count == 0 ? "clean" : lines.Count + " fault(s)")} ({boxes.Count} objects)");
            foreach (string line in lines)
                report.AppendLine(line);
            return lines.Count;
        }

        private static bool IsNested(string a, string b)
        {
            foreach (string[] pair in Nested)
            {
                if ((a.StartsWith(pair[0]) && b.StartsWith(pair[1])) ||
                    (a.StartsWith(pair[1]) && b.StartsWith(pair[0])))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Reports anything a player could walk through that they should not.
        ///
        /// Checked by walking up from each renderer, because collision is often put on a
        /// parent rather than on the piece that is drawn.
        /// </summary>
        private static int AuditColliders(System.Text.StringBuilder report)
        {
            var counts = new Dictionary<string, int>();
            foreach (Renderer renderer in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (IsWalkable(renderer.transform))
                    continue;

                bool solid = false;
                for (Transform t = renderer.transform; t != null && !solid; t = t.parent)
                    solid = t.GetComponent<Collider>() != null;
                // Or on a sibling under the same prop, which is where a swapped-in
                // collision mesh ends up.
                if (!solid)
                {
                    Transform prop = renderer.transform;
                    while (prop.parent != null && prop.parent.GetComponent<Collider>() == null && prop.parent.parent != null)
                        prop = prop.parent;
                    solid = prop.GetComponentInChildren<Collider>(true) != null;
                }
                if (solid)
                    continue;

                // Litter too low to walk into is not worth collision. The same figure the
                // builder uses to decide that, so a stone it deliberately left bare is not
                // then reported here as a stone somebody forgot.
                if (renderer.bounds.size.y < DemoSceneBuilder.LitterHeight)
                    continue;

                Transform root = renderer.transform;
                while (root.parent != null)
                    root = root.parent;
                string key = $"{root.name}/{renderer.transform.name}";
                counts.TryGetValue(key, out int seen);
                counts[key] = seen + 1;
            }

            report.AppendLine($"Colliders: {(counts.Count == 0 ? "clean" : counts.Count + " kind(s) missing")}");
            foreach (KeyValuePair<string, int> pair in counts)
                report.AppendLine($"  can be walked through: {pair.Key} x{pair.Value}");
            return counts.Count;
        }

        private static bool IsWalkable(Transform t)
        {
            for (; t != null; t = t.parent)
            {
                if (System.Array.IndexOf(Walkable, t.name) >= 0)
                    return true;
                // A wall torch is fixed above head height and deliberately has no
                // collision: a bracket that stops a player walking along a wall is worse
                // than one they can put their head through. See DemoSceneBuilder.Mount.
                if (t.name.StartsWith("Torch_"))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// How far two boxes overlap, or zero if they are apart.
        ///
        /// A separating-axis test on the two boxes as they are turned, rather than on
        /// world-aligned boxes round them: everything out here is turned to some angle,
        /// and the upright box around a turned wagon is half again its size, which
        /// reports clashes between things that are nowhere near each other.
        /// </summary>
        private static float Penetration(Box a, Box b)
        {
            float y = a.Extents.y + b.Extents.y - Mathf.Abs(a.Centre.y - b.Centre.y);
            if (y <= 0f)
                return 0f;

            var offset = new Vector2(b.Centre.x - a.Centre.x, b.Centre.z - a.Centre.z);
            Vector2[] axes =
            {
                Direction(a.Yaw), Direction(a.Yaw + 90f),
                Direction(b.Yaw), Direction(b.Yaw + 90f),
            };

            float least = y;
            foreach (Vector2 axis in axes)
            {
                float reach = Spread(a, axis) + Spread(b, axis);
                float apart = Mathf.Abs(Vector2.Dot(offset, axis));
                if (reach - apart <= 0f)
                    return 0f;
                least = Mathf.Min(least, reach - apart);
            }
            return least;
        }

        /// <summary>How far a box reaches along an axis, from its middle.</summary>
        private static float Spread(Box box, Vector2 axis)
        {
            return Mathf.Abs(Vector2.Dot(Direction(box.Yaw), axis)) * box.Extents.x +
                   Mathf.Abs(Vector2.Dot(Direction(box.Yaw + 90f), axis)) * box.Extents.z;
        }

        /// <summary>A turned object's local +X, flattened onto the ground.</summary>
        private static Vector2 Direction(float yaw)
        {
            float radians = yaw * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(radians), -Mathf.Sin(radians));
        }

        /// <summary>A house's walls and floor, without its furniture, in its own frame.</summary>
        private static Box HouseShell(Transform house)
        {
            var shell = new Bounds();
            bool any = false;
            foreach (Transform part in house)
            {
                if (!part.name.StartsWith("Wall_") && !part.name.StartsWith("Floor_"))
                    continue;
                Bounds local = DemoVillageBuilder.LocalBounds(house, part);
                if (any)
                    shell.Encapsulate(local);
                else
                {
                    shell = local;
                    any = true;
                }
            }
            return new Box
            {
                Name = house.name,
                Centre = house.TransformPoint(shell.center),
                Extents = Vector3.Scale(shell.extents, house.lossyScale),
                Yaw = house.eulerAngles.y,
            };
        }
    }
}
