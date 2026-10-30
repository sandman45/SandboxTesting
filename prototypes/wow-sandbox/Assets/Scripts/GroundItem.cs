using System;
using System.Linq;
using UnityEngine;

namespace WowSandbox
{
    /// <summary>
    /// An item lying in the world — a sword in the sand, a pouch of coin: click it from close by
    /// to reach down (the looting reach) and put it in your bags. A full bag leaves it where it
    /// is. Its look is whatever model it was placed with (WoW Sandbox → Place Ground Item picks
    /// one by the item's kind). Gone for good once taken — unless it has respawnHours, when
    /// another turns up in its place that many game hours later (driftwood washing up again).
    /// Drop puts one down at runtime — an item dragged out of the bags — holding that very item
    /// (its enchantments, its stack), and SaveGame keeps those.
    /// </summary>
    public class GroundItem : Interactable
    {
        [Tooltip("A catalog item's name, e.g. \"Longsword\". Empty is just coin.")]
        public string item = "Longsword";
        [Min(1)] public int count = 1;
        [Tooltip("Coin, in copper, picked up with it (or alone).")]
        [Min(0)] public int copper;
        [Tooltip("Game hours till another turns up where this was taken. 0 is gone for good.")]
        [Min(0f)] public float respawnHours;
        [Tooltip("Only for someone without one: carrying it already, it's left lying (one of several flint).")]
        public bool unique;

        float _returnsIn;

        /// <summary>The very item lying here, for one dropped from the bags; null for a placed one (made from <see cref="item"/>).</summary>
        [NonSerialized] public Item exact;

        /// <summary>Put down from the bags at runtime (Drop), not placed in the scene.</summary>
        public bool Dropped { get; private set; }

        /// <summary>Taken, and not back yet (only one that respawns stays in the scene while it's gone).</summary>
        public bool Gone => _returnsIn > 0f;
        public float ReturnsIn => _returnsIn;

        public override bool CanInteract => !Gone;

        void Update()
        {
            if (!Gone)
                return;
            _returnsIn -= DayNightCycle.HoursThisFrame;
            if (_returnsIn <= 0f)
                Show(true);
        }

        /// <summary>Gone for <paramref name="hours"/> more game hours (0: here now) — taking it, or SaveGame.</summary>
        public void ReturnIn(float hours)
        {
            _returnsIn = Mathf.Max(0f, hours);
            Show(!Gone);
        }

        void Show(bool shown)
        {
            if (shown)
                _returnsIn = 0f;
            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
                renderer.enabled = shown;
            foreach (var collider in GetComponentsInChildren<Collider>(true))
                collider.enabled = shown;
        }

        static readonly int LootGrabHash = Animator.StringToHash("LootGrab");

        public override string Verb => "Pick up";

        void Reset() => displayName = item;

        public override void Interact(GameObject player)
        {
            if (Gone)
                return;
            var inventory = player.GetComponent<Inventory>();
            if (inventory == null)
                return;

            if (exact != null)
            {
                if (!inventory.Add(exact))
                {
                    Hud.Error("Inventory is full.");
                    return;
                }
                Hud.Info($"You pick up {exact.name}{(exact.count > 1 ? $" x{exact.count}" : "")}.");
            }
            else if (!string.IsNullOrWhiteSpace(item))
            {
                if (unique && inventory.BestInBags(carried => carried.baseName == item.Trim()) != null)
                {
                    Hud.Error($"You already have {item.Trim()}.");
                    return;
                }
                if (!ItemCatalog.TryFind(item.Trim(), out _))
                {
                    Debug.LogWarning($"[GroundItem] {name}: no item called \"{item}\" in the catalog.", this);
                    return;
                }
                var picked = ItemCatalog.Create(item.Trim(), count);
                if (!inventory.Add(picked))
                {
                    Hud.Error("Inventory is full.");
                    return;
                }
                Hud.Info($"You pick up {picked.name}{(count > 1 ? $" x{count}" : "")}.");
            }
            if (copper > 0)
            {
                inventory.AddCopper(copper);
                Hud.Info($"You pick up {Money.Format(copper, rich: true)}.");
            }

            var movement = player.GetComponent<WowCharacterController>();
            var animator = movement != null ? movement.Animator : null;
            // A standing reach — not while swimming (down at a sunk wreck, say).
            if (animator != null && animator.runtimeAnimatorController != null && !movement.IsSwimming)
                foreach (var parameter in animator.parameters)
                    if (parameter.nameHash == LootGrabHash)
                        animator.SetTrigger(LootGrabHash);

            if (Dropped)
                Destroy(gameObject);
            else if (respawnHours > 0f)
                ReturnIn(respawnHours);
            else
                gameObject.SetActive(false);
        }

        // --- Dropped from the bags ----------------------------------------------------------

        /// <summary>
        /// Lays <paramref name="dropped"/> on the ground at <paramref name="point"/>, turned
        /// <paramref name="yaw"/> degrees, to be picked up again. Its look: its own gear model
        /// laid flat if it has one (weapons, shields, helms), else a copy of one placed in the
        /// scene for that item or its kind, else a small sack-coloured lump.
        /// </summary>
        public static GroundItem Drop(Item dropped, Vector3 point, float yaw)
        {
            var root = new GameObject(dropped.name);
            root.transform.SetPositionAndRotation(point, Quaternion.Euler(0f, yaw, 0f));

            var visuals = FindAnyObjectByType<EquipmentVisuals>();
            var library = visuals != null ? visuals.library : null;
            var look = (library != null ? GearModel(library.For(dropped), root.transform) : null)
                       ?? SceneModel(dropped, root.transform)
                       ?? Lump(root.transform);
            look.name = "Model";

            // Sat on the ground, with a box round it to click.
            var renderers = look.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length > 0)
            {
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers.Skip(1))
                    bounds.Encapsulate(renderer.bounds);
                var lift = Vector3.up * (point.y - bounds.min.y);
                look.transform.position += lift;
                bounds.center += lift;
                var box = root.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.center = root.transform.InverseTransformPoint(bounds.center);
                var size = root.transform.InverseTransformVector(bounds.size);
                box.size = new Vector3(Mathf.Max(Mathf.Abs(size.x), 0.5f), Mathf.Max(Mathf.Abs(size.y), 0.5f),
                                       Mathf.Max(Mathf.Abs(size.z), 0.5f));
            }

            var ground = root.AddComponent<GroundItem>();
            ground.exact = dropped;
            ground.item = dropped.name;
            ground.count = dropped.count;
            ground.displayName = dropped.name;
            ground.Dropped = true;
            if (dropped.kind == ItemKind.Quest)
                root.AddComponent<Glint>();
            return ground;
        }

        /// <summary>
        /// The item's own model from Harvest Gear's library — the meshes a weapon, shield, helm or
        /// shoulder rides a bone with — laid flat, side by side if there are several (a pair of
        /// shoulders). Null when it has none (body armour is painted on or skinned).
        /// </summary>
        public static GameObject GearModel(GearLook look, Transform parent)
        {
            var pieces = look != null ? look.attachedPieces.Where(p => p.mesh != null).ToArray() : null;
            if (pieces == null || pieces.Length == 0)
                return null;

            var root = new GameObject("Model");
            root.transform.SetParent(parent, false);
            float x = 0f;
            foreach (var piece in pieces)
            {
                var child = new GameObject(piece.name);
                child.transform.SetParent(root.transform, false);
                child.AddComponent<MeshFilter>().sharedMesh = piece.mesh;
                child.AddComponent<MeshRenderer>().sharedMaterials = piece.materials;

                // Lie it down along its longest side, then line it up after the last one.
                var mesh = piece.mesh.bounds;
                var rotation = mesh.size.y > mesh.size.x && mesh.size.y > mesh.size.z
                    ? Quaternion.Euler(0f, 0f, 90f) : Quaternion.identity;
                float minX = float.MaxValue, maxX = float.MinValue;
                for (int corner = 0; corner < 8; corner++)
                {
                    var offset = Vector3.Scale(mesh.extents, new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                    float cornerX = (rotation * (mesh.center + offset)).x;
                    minX = Mathf.Min(minX, cornerX);
                    maxX = Mathf.Max(maxX, cornerX);
                }
                child.transform.localRotation = rotation;
                child.transform.localPosition = new Vector3(x - minX, 0f, 0f);
                x += maxX - minX + 0.05f;
            }
            return root;
        }

        /// <summary>
        /// A copy of the model on a ground item placed in the scene: one of this very item first,
        /// then of its base item, then of its kind (Place Ground Item's model for that kind).
        /// </summary>
        static GameObject SceneModel(Item dropped, Transform parent)
        {
            static Item Catalog(GroundItem g) =>
                !string.IsNullOrWhiteSpace(g.item) && ItemCatalog.TryFind(g.item.Trim(), out var found) ? found : null;
            var placed = FindObjectsByType<GroundItem>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(g => !g.Dropped && g.transform.Find("Model") != null && Catalog(g) != null)
                .ToList();
            var source = placed.FirstOrDefault(g => g.item.Trim() == dropped.name)
                      ?? placed.FirstOrDefault(g => Catalog(g).baseName == dropped.baseName)
                      ?? placed.FirstOrDefault(g => Catalog(g) is var kind && kind.kind == dropped.kind && kind.IsPotion == dropped.IsPotion);
            if (source == null)
                return null;

            var model = source.transform.Find("Model");
            var copy = Instantiate(model.gameObject, parent);
            copy.transform.localPosition = Vector3.zero;
            copy.transform.localRotation = model.localRotation;
            copy.transform.localScale = Vector3.Scale(model.localScale, source.transform.lossyScale);
            // A taken one waiting to come back has its renderers off; and the box on the root is what's clicked.
            foreach (var renderer in copy.GetComponentsInChildren<Renderer>(true))
                renderer.enabled = true;
            foreach (var collider in copy.GetComponentsInChildren<Collider>(true))
                Destroy(collider);
            return copy;
        }

        static GameObject Lump(Transform parent)
        {
            var lump = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(lump.GetComponent<Collider>());
            lump.transform.SetParent(parent, false);
            lump.transform.localScale = new Vector3(0.3f, 0.2f, 0.3f);
            var renderer = lump.GetComponent<Renderer>();
            var colour = new Color(0.55f, 0.45f, 0.3f);
            var sack = new Material(renderer.sharedMaterial) { name = "Dropped item (stand-in)", color = colour };
            if (sack.HasProperty("_BaseColor"))
                sack.SetColor("_BaseColor", colour);
            renderer.sharedMaterial = sack;
            return lump;
        }
    }
}
