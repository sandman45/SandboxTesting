using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WowSandbox.EditorTools
{
    /// <summary>
    /// Hangs a rope ladder down each side of the floating ship, midway along it, from the rail
    /// into the sea, and marks where each lands you on deck. Found by raycasting the ship's own
    /// colliders: horizontal rays from outside find the hull's side up to the top of the rail,
    /// then downward rays inboard of the rail find the deck. Re-running replaces the pair.
    ///
    /// The ropes and rungs are plain primitives with their colliders removed — only the
    /// ShipLadder on each matters to climbing — so you can move or rotate a ladder (and its
    /// StepOff child) by hand if the guess is off.
    /// </summary>
    public static class ShipLadderSetup
    {
        const string MaterialDir = "Assets/WowExports/_Generated/ShipLadder";

        /// <summary>Where along the ship to hang a pair, as a fraction of half its length from
        /// the middle toward the stern (negative is toward the bow).</summary>
        static readonly (float fraction, string name)[] Stations =
        {
            (0f, "Midship"),
            (0.45f, "Aft"),
        };

        /// <summary>
        /// Which way along the ship is the stern: the end with the higher rail, since the
        /// captain's quarterdeck stands above the rest. Falls back to "along" if neither end
        /// can be measured.
        /// </summary>
        internal static Vector3 SternDirection(Transform ship, Vector3 midship, Vector3 along, Vector3 across,
                                      float halfBeam, float halfLength, float waterY, float playerHeight)
        {
            float RailHeight(Vector3 direction)
            {
                var point = midship + direction * (halfLength * 0.6f);
                return FindSide(ship, point, across, halfBeam, waterY, playerHeight, out var rail, out _)
                    ? rail.y : float.MinValue;
            }

            return RailHeight(-along) > RailHeight(along) ? -along : along;
        }

        [MenuItem("WoW Sandbox/Add Ship Ladders")]
        static void AddLadders()
        {
            var ship = Object.FindAnyObjectByType<ShipBuoyancy>();
            if (ship == null)
            {
                Debug.LogWarning("[ShipLadderSetup] No ShipBuoyancy in the scene — this hangs ladders on the floating ship.");
                return;
            }

            var water = Object.FindAnyObjectByType<WaterVolume>();
            if (water == null)
            {
                Debug.LogWarning("[ShipLadderSetup] No WaterVolume in the scene, so there's no waterline to hang down to.");
                return;
            }

            var player = Object.FindAnyObjectByType<WowCharacterController>();
            var capsule = player != null ? player.GetComponent<CharacterController>() : null;
            float playerHeight = capsule != null ? capsule.height : 2f;

            foreach (var old in ship.GetComponentsInChildren<ShipLadder>(true))
                Undo.DestroyObjectImmediate(old.gameObject);

            Physics.SyncTransforms();
            var rope = LoadOrCreateMaterial("Rope", new Color(0.55f, 0.43f, 0.27f));
            var wood = LoadOrCreateMaterial("Wood", new Color(0.36f, 0.24f, 0.14f));

            if (!ShipFrame(ship.transform, out Vector3 midship, out Vector3 across, out float halfBeam,
                           out Vector3 along, out float halfLength))
            {
                Debug.LogWarning("[ShipLadderSetup] The ship has no renderers to measure.", ship);
                return;
            }

            Vector3 aft = SternDirection(ship.transform, midship, along, across, halfBeam, halfLength,
                                         water.SurfaceY, playerHeight);

            int placed = 0;
            foreach (var (fraction, station) in Stations)
            {
                Vector3 point = midship + aft * (halfLength * fraction);
                foreach (float side in new[] { 1f, -1f })
                {
                    Vector3 outward = across * side;
                    string name = $"ShipLadder ({(side > 0 ? "Starboard" : "Port")}, {station})";
                    if (!FindSide(ship.transform, point, outward, halfBeam, water.SurfaceY, playerHeight,
                                  out Vector3 railTop, out Vector3 deck))
                    {
                        Debug.LogWarning($"[ShipLadderSetup] Couldn't find the hull and deck for {name} — does " +
                                         "the ship have colliders (WoW Sandbox → Add Mesh Colliders)?", ship);
                        continue;
                    }

                    BuildLadder(ship.transform, railTop, outward, deck, water.SurfaceY, playerHeight, rope, wood, name);
                    placed++;
                }
            }

            if (placed == 0)
                return;

            EditorSceneManager.MarkSceneDirty(ship.gameObject.scene);
            Debug.Log($"[ShipLadderSetup] Hung {placed} ladder(s) on {ship.name}. Swim up facing one and hold W " +
                      "to climb; select a ladder to see its line and landing spot.", ship);
        }

        /// <summary>
        /// The ship's middle at the waterline, the level direction across its beam (its shorter
        /// horizontal axis), and roughly half its beam, from its renderers' bounds in its own space.
        /// </summary>
        internal static bool ShipFrame(Transform ship, out Vector3 midship, out Vector3 across, out float halfBeam,
                              out Vector3 along, out float halfLength)
        {
            midship = across = along = Vector3.zero;
            halfBeam = halfLength = 0f;
            var renderers = ship.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return false;

            var local = new Bounds();
            bool first = true;
            foreach (var renderer in renderers)
            {
                if (renderer.GetComponentInParent<ShipLadder>() != null)
                    continue;
                var b = renderer.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3(i % 2 == 0 ? b.min.x : b.max.x, (i / 2) % 2 == 0 ? b.min.y : b.max.y,
                                             i / 4 == 0 ? b.min.z : b.max.z);
                    var point = ship.InverseTransformPoint(corner);
                    if (first) { local = new Bounds(point, Vector3.zero); first = false; }
                    else local.Encapsulate(point);
                }
            }

            bool lengthIsX = local.size.x * Mathf.Abs(ship.lossyScale.x) >= local.size.z * Mathf.Abs(ship.lossyScale.z);
            across = ship.TransformDirection(lengthIsX ? Vector3.forward : Vector3.right);
            across.y = 0f;
            across.Normalize();
            midship = ship.TransformPoint(local.center);
            halfBeam = (lengthIsX ? local.extents.z * Mathf.Abs(ship.lossyScale.z)
                                  : local.extents.x * Mathf.Abs(ship.lossyScale.x));
            along = ship.TransformDirection(lengthIsX ? Vector3.right : Vector3.forward);
            along.y = 0f;
            along.Normalize();
            halfLength = (lengthIsX ? local.extents.x * Mathf.Abs(ship.lossyScale.x)
                                    : local.extents.z * Mathf.Abs(ship.lossyScale.z));
            return true;
        }

        /// <summary>
        /// Walks up the hull's side from the waterline with rays aimed in from outside. The
        /// hull answers each one at about the same distance; the first height where nothing
        /// answers (or only something far inboard, like rigging) is above the rail. Then a ray
        /// down just inboard of the rail finds its top, and rays further in find the deck.
        /// </summary>
        internal static bool FindSide(Transform ship, Vector3 midship, Vector3 outward, float halfBeam, float waterY,
                             float playerHeight, out Vector3 railTop, out Vector3 deck)
        {
            railTop = deck = Vector3.zero;
            float start = halfBeam + 10f;
            float step = playerHeight * 0.2f;
            float outermost = float.MaxValue;
            Vector3 lastHit = Vector3.zero;
            bool anyHit = false;

            for (float y = waterY; y < waterY + playerHeight * 15f; y += step)
            {
                var origin = new Vector3(midship.x, y, midship.z) + outward * start;
                if (!RaycastShip(ship, origin, -outward, start + halfBeam, out var hit))
                {
                    if (anyHit) break;
                    continue;
                }

                if (anyHit && hit.distance > outermost + playerHeight * 1.5f)
                    break;  // rigging or a mast well inboard — we've passed the rail

                anyHit = true;
                outermost = Mathf.Min(outermost, hit.distance);
                lastHit = hit.point;
            }

            if (!anyHit)
                return false;

            // The rail's top, just inboard of its outer face.
            var above = lastHit - outward * 0.1f + Vector3.up * step * 1.5f;
            railTop = RaycastShip(ship, above, Vector3.down, step * 4f, out var railHit) ? railHit.point : lastHit;

            // The deck: the first flat surface inboard of the rail that's clearly below its top.
            // Searched from the rail itself, before the ladder is moved out to the hull's bulge,
            // so a hull that swells out below the rail isn't mistaken for the deck.
            var railFace = new Vector3(lastHit.x, railTop.y, lastHit.z);
            float railY = railTop.y;

            // Hang at the outermost bulge of the hull below, so the ladder clears it all the way down.
            var hullFace = new Vector3(midship.x, 0f, midship.z) + outward * (start - outermost);
            railTop = new Vector3(hullFace.x, railY, hullFace.z) + outward * 0.15f;

            for (float inboard = 0.1f; inboard < halfBeam; inboard += 0.2f)
            {
                var origin = railFace - outward * inboard + Vector3.up * 0.5f;
                if (!RaycastShip(ship, origin, Vector3.down, playerHeight * 3f, out var deckHit))
                    continue;
                if (deckHit.point.y > railY - 0.25f || deckHit.normal.y < 0.7f)
                    continue;   // still on the rail, or something sloping
                deck = deckHit.point - outward * (playerHeight * 0.4f);
                if (RaycastShip(ship, deck + Vector3.up * playerHeight, Vector3.down, playerHeight * 2f, out var landing))
                    deck = landing.point;
                deck += Vector3.up * 0.05f;
                return true;
            }

            return false;
        }

        /// <summary>A raycast that only counts the ship's own colliders.</summary>
        static bool RaycastShip(Transform ship, Vector3 origin, Vector3 direction, float distance, out RaycastHit nearest)
        {
            nearest = default;
            float best = float.MaxValue;
            foreach (var hit in Physics.RaycastAll(origin, direction, distance, ~0, QueryTriggerInteraction.Ignore))
            {
                if (!hit.collider.transform.IsChildOf(ship) || hit.distance >= best)
                    continue;
                best = hit.distance;
                nearest = hit;
            }
            return best < float.MaxValue;
        }

        static void BuildLadder(Transform ship, Vector3 railTop, Vector3 outward, Vector3 deck, float waterY,
                                float playerHeight, Material rope, Material wood, string name)
        {
            var root = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(root, "Add Ship Ladders");
            root.transform.SetPositionAndRotation(railTop, Quaternion.LookRotation(outward, Vector3.up));
            root.transform.SetParent(ship, true);
            // World units throughout, whatever the ship's scale.
            var lossy = ship.lossyScale;
            root.transform.localScale = new Vector3(1f / lossy.x, 1f / lossy.y, 1f / lossy.z);

            var ladder = root.AddComponent<ShipLadder>();
            ladder.length = railTop.y - (waterY - playerHeight * 0.6f);
            ladder.width = playerHeight * 0.45f;

            var stepOff = new GameObject("StepOff").transform;
            stepOff.SetParent(root.transform, false);
            stepOff.position = deck;
            ladder.stepOff = stepOff;

            float ropeRadius = playerHeight * 0.03f;
            foreach (float x in new[] { -0.5f, 0.5f })
            {
                var strand = Primitive(PrimitiveType.Cylinder, "Rope", root.transform, rope);
                strand.localPosition = new Vector3(x * ladder.width, -ladder.length * 0.5f, 0f);
                // Unity's cylinder is 2 units tall.
                strand.localScale = new Vector3(ropeRadius * 2f, ladder.length * 0.5f, ropeRadius * 2f);
            }

            float spacing = playerHeight * 0.18f;
            for (float y = spacing * 0.5f; y < ladder.length; y += spacing)
            {
                var rung = Primitive(PrimitiveType.Cube, "Rung", root.transform, wood);
                rung.localPosition = new Vector3(0f, -y, 0f);
                rung.localScale = new Vector3(ladder.width, playerHeight * 0.035f, playerHeight * 0.06f);
            }
        }

        static Transform Primitive(PrimitiveType type, string name, Transform parent, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        static Material LoadOrCreateMaterial(string name, Color color)
        {
            string path = $"{MaterialDir}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
                return material;

            System.IO.Directory.CreateDirectory(MaterialDir);
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            material = new Material(shader) { name = name };
            material.SetColor("_BaseColor", color);
            material.color = color;
            material.SetFloat("_Smoothness", 0.1f);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}
