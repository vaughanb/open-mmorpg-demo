using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// Checks the furniture in every house and reports what is wrong with it.
    ///
    /// Interiors are the one part of the demo that cannot be judged from outside, and
    /// looking at them through the editor camera is a poor way to find the faults that
    /// actually occur: a chest a hand's width off the wall, a shelf hanging in the air,
    /// two crates in the same place, a door that swings through a chair. Each of those
    /// is a number, so each of them is checked here rather than looked at.
    ///
    /// Everything is measured in the house's own space through
    /// <see cref="DemoVillageBuilder.LocalBounds"/>, because the houses are turned to
    /// face the green and a world-space box around a rotated room means nothing.
    /// </summary>
    public static class DemoInteriorAudit
    {
        /// <summary>Above this, a piece is judged to be standing away from its wall.</summary>
        private const float WallGapLimit = 0.12f;

        /// <summary>Below this, an intersection is two models touching rather than clashing.</summary>
        private const float OverlapLimit = 0.03f;

        /// <summary>How far a piece may sit off the floor before it counts as floating.</summary>
        private const float FloatLimit = 0.05f;

        [MenuItem("Open MMORPG/Demo/Audit House Interiors")]
        public static void Audit()
        {
            GameObject village = GameObject.Find("Village");
            if (village == null)
            {
                Debug.LogError($"[{nameof(DemoInteriorAudit)}] No Village in the open scene.");
                return;
            }

            var report = new System.Text.StringBuilder();
            int faults = 0;

            foreach (Transform house in village.transform)
            {
                if (!house.name.StartsWith("House"))
                    continue;
                Transform interior = house.Find("Interior");
                if (interior == null)
                {
                    report.AppendLine($"{house.name}: no Interior.");
                    ++faults;
                    continue;
                }

                Bounds room = DemoVillageBuilder.InteriorBounds(house);
                var names = new List<string>();
                var boxes = new List<Bounds>();
                foreach (Transform piece in interior)
                {
                    if (piece.GetComponentInChildren<Renderer>() == null)
                        continue;
                    names.Add(piece.name);
                    boxes.Add(DemoVillageBuilder.LocalBounds(house, piece));
                }

                var lines = new List<string>();

                for (int i = 0; i < boxes.Count; ++i)
                {
                    Bounds b = boxes[i];

                    if (b.min.x < room.min.x - 0.01f || b.max.x > room.max.x + 0.01f ||
                        b.min.z < room.min.z - 0.01f || b.max.z > room.max.z + 0.01f)
                        lines.Add($"  through a wall: {names[i]}");

                    // A piece is floating if nothing is under it: not the floor, and not
                    // another piece it could be standing on. Flat things are exempt -
                    // a rug lies on the floor and has no business having legs.
                    if (b.size.y < 0.06f || b.min.y <= room.min.y + FloatLimit)
                        continue;
                    bool supported = false;
                    for (int j = 0; j < boxes.Count && !supported; ++j)
                    {
                        if (j == i)
                            continue;
                        Bounds s = boxes[j];
                        supported = Mathf.Abs(s.max.y - b.min.y) < 0.06f &&
                            Mathf.Min(s.max.x, b.max.x) > Mathf.Max(s.min.x, b.min.x) &&
                            Mathf.Min(s.max.z, b.max.z) > Mathf.Max(s.min.z, b.min.z);
                    }
                    if (supported)
                        continue;
                    // Or it is hung on a wall, which is support enough.
                    float toWall = Mathf.Min(
                        Mathf.Min(b.min.x - room.min.x, room.max.x - b.max.x),
                        Mathf.Min(b.min.z - room.min.z, room.max.z - b.max.z));
                    if (toWall > WallGapLimit)
                        lines.Add($"  floating: {names[i]} base {b.min.y - room.min.y:F2} up, {toWall:F2} from the nearest wall");
                }

                for (int i = 0; i < boxes.Count; ++i)
                {
                    for (int j = i + 1; j < boxes.Count; ++j)
                    {
                        Bounds a = boxes[i], b = boxes[j];
                        float ox = Mathf.Min(a.max.x, b.max.x) - Mathf.Max(a.min.x, b.min.x);
                        float oy = Mathf.Min(a.max.y, b.max.y) - Mathf.Max(a.min.y, b.min.y);
                        float oz = Mathf.Min(a.max.z, b.max.z) - Mathf.Max(a.min.z, b.min.z);
                        if (ox > OverlapLimit && oy > OverlapLimit && oz > OverlapLimit)
                            lines.Add($"  inside each other: {names[i]} / {names[j]} by {Mathf.Min(ox, Mathf.Min(oy, oz)):F2}");
                    }
                }

                lines.AddRange(SweptByDoor(house, interior, names, boxes));

                report.AppendLine($"{house.name}: {(lines.Count == 0 ? "clean" : lines.Count + " fault(s)")}");
                foreach (string line in lines)
                    report.AppendLine(line);
                faults += lines.Count;
            }

            if (faults == 0)
                Debug.Log($"[{nameof(DemoInteriorAudit)}] All interiors clean.\n{report}");
            else
                Debug.LogWarning($"[{nameof(DemoInteriorAudit)}] {faults} fault(s).\n{report}");
        }

        /// <summary>
        /// Anything the door would sweep through as it opens.
        ///
        /// The leaf turns about its hinge, so the floor it needs is a disc of the leaf's
        /// own length centred on that hinge - checked against the piece's footprint, not
        /// its middle, since a bench only has to catch the door with one end.
        /// </summary>
        private static IEnumerable<string> SweptByDoor(Transform house, Transform interior, List<string> names, List<Bounds> boxes)
        {
            Transform hinge = house.Find("Doorway/Hinge");
            if (hinge == null)
                yield break;
            Vector3 pivot = house.InverseTransformPoint(hinge.position);
            Bounds leaf = DemoVillageBuilder.LocalBounds(hinge, hinge);
            float reach = Mathf.Max(leaf.size.x, leaf.size.z);

            for (int i = 0; i < boxes.Count; ++i)
            {
                Bounds b = boxes[i];
                // The leaf hangs clear of the floor's flat furnishings and stops short of
                // the ceiling, so only what stands in its height band is in its way.
                if (b.size.y < 0.1f || b.min.y > leaf.max.y)
                    continue;
                float x = Mathf.Clamp(pivot.x, b.min.x, b.max.x);
                float z = Mathf.Clamp(pivot.z, b.min.z, b.max.z);
                float distance = new Vector2(x - pivot.x, z - pivot.z).magnitude;
                if (distance < reach)
                    yield return $"  in the door's swing: {names[i]} at {distance:F2} of {reach:F2}";
            }
        }
    }
}
