using UnityEngine;

namespace WowSandbox
{
    /// <summary>
    /// The torch or lantern in the player's off hand. It lights itself when it's dark (and
    /// goes out by day, in the water, and when you die), and burns fuel in game time:
    /// - a torch burns <see cref="ItemCatalog.TorchHours"/> and is used up, and the next in the
    ///   stack lights;
    /// - a lantern burns <see cref="ItemCatalog.OilHours"/> per Flask of Oil, filled from the
    ///   bags as it runs dry — no oil, no light.
    /// A lit torch also keeps animals off (<see cref="TorchNear"/>) and is what Torch Swing
    /// strikes with.
    ///
    /// What it looks like is the item's own look from Harvest Gear when there is one (export a
    /// held torch or lantern and add it as Off-hand); until then a stand-in: a plain stick for
    /// the torch, and a small copy of one of the ship's lanterns. Added to the player at Play.
    /// </summary>
    [DisallowMultipleComponent]
    public class HeldLight : MonoBehaviour
    {
        /// <summary>The player's, or null.</summary>
        public static HeldLight Active { get; private set; }

        [Tooltip("Animals within this distance of a lit torch run from it.")]
        public float scareRadius = 8f;
        [Tooltip("Lit once it's this dark (NightLight.Darkness, 0 day to 1 night).")]
        [Range(0f, 1f)] public float lightsAt = 0.15f;

        Inventory _inventory;
        WowCharacterController _movement;
        HealthController _health;
        EquipmentVisuals _visuals;

        Item _item;             // what the holder was built for
        Transform _holder;      // follows the hand, upright; the light (and any stand-in) under it
        NightLight _light;
        Transform _hand;
        Renderer _look;         // the harvested model's mesh in the hand, when there is one
        Vector3 _flameLocal;    // where the flame sits on it, in the mesh's own space
        bool _warnedEmpty;

        /// <summary>A torch or lantern is burning in the player's hand.</summary>
        public static bool IsLit => Active != null && Active._light != null && Active._light.Lit;

        /// <summary>
        /// Whether a lit torch is within <paramref name="radius"/> of <paramref name="position"/> (its
        /// own scare radius if 0) — and where it is, to run from.
        /// </summary>
        public static bool TorchNear(Vector3 position, out Vector3 torch, float radius = 0f)
        {
            torch = default;
            if (!IsLit || Active._item is not { IsTorch: true })
                return false;
            torch = Active._holder.position;
            float reach = radius > 0f ? radius : Active.scareRadius;
            return (position - torch).sqrMagnitude <= reach * reach;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AddToPlayer()
        {
            var player = FindAnyObjectByType<WowCharacterController>();
            if (player != null && player.GetComponent<HeldLight>() == null)
                player.gameObject.AddComponent<HeldLight>();
        }

        void Awake()
        {
            Active = this;
            _inventory = GetComponent<Inventory>();
            _movement = GetComponent<WowCharacterController>();
            _health = GetComponent<HealthController>();
            _visuals = GetComponent<EquipmentVisuals>();
        }

        void OnDestroy()
        {
            if (Active == this)
                Active = null;
            if (_holder != null)
                Destroy(_holder.gameObject);
        }

        void Update()
        {
            var item = _inventory != null ? _inventory.Equipped(EquipSlot.OffHand) : null;
            if (item is not { kind: ItemKind.Light })
                item = null;
            if (item != _item)
                Rebuild(item);
            if (_light == null)
                return;

            bool wanted = NightLight.Darkness >= lightsAt
                          && !(_movement != null && _movement.IsSwimming)
                          && !(_health != null && _health.IsDead);
            _light.Lit = wanted && Fuel(item);
        }

        /// <summary>Burns this frame's fuel, lighting the next torch or filling the lantern as needed. False if there's none.</summary>
        bool Fuel(Item item)
        {
            if (item.fuel <= 0f)
            {
                if (item.IsTorch)
                    item.fuel = ItemCatalog.TorchHours;
                else if (_inventory.TakeOneFromBags("Flask of Oil"))
                {
                    item.fuel = ItemCatalog.OilHours;
                    _warnedEmpty = false;
                    Hud.Info("You fill your lantern with oil.");
                }
                else
                {
                    if (!_warnedEmpty)
                        Hud.Error("Your lantern is out of oil.");
                    _warnedEmpty = true;
                    return false;
                }
            }

            item.fuel -= DayNightCycle.HoursThisFrame;
            if (item.fuel > 0f)
                return true;

            if (item.IsTorch)
            {
                Hud.Info(item.count > 1 ? "Your torch burns out. You light another." : "Your torch burns out.");
                // The stack is the supply: one fewer, and the slot empties with the last.
                _inventory.UseUpEquipped(EquipSlot.OffHand);
            }
            else
                item.fuel = 0f;
            return true;
        }

        void LateUpdate()
        {
            if (_holder == null)
                return;

            // The light where the flame is: the top of the harvested torch, the middle of a
            // lantern; else at the hand, with the stand-in held upright under it.
            if (_look != null && _item.IsTorch)
            {
                _holder.position = _look.transform.TransformPoint(_flameLocal);
            }
            else if (_look != null)
            {
                // A lantern hangs, so low in the world rather than along the model: at the glass,
                // under its handle.
                var bounds = _look.bounds;
                _holder.position = new Vector3(bounds.center.x, bounds.min.y + bounds.size.y * NightLight.LanternLightHeight, bounds.center.z);
            }
            else
            {
                _holder.position = _hand != null ? _hand.position : transform.TransformPoint(new Vector3(-0.3f, 1f, 0.25f));
            }
            _holder.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        }

        void Rebuild(Item item)
        {
            if (_holder != null)
                Destroy(_holder.gameObject);
            _holder = null;
            _light = null;
            _look = null;
            _item = item;
            _warnedEmpty = false;
            if (item == null)
                return;

            _hand = FindHand(item);
            _look = HarvestedLook(item);
            if (_look != null)
                _flameLocal = FlamePoint(_look.GetComponent<MeshFilter>().sharedMesh, item.IsTorch);

            // A plain scene object, not DontSave — that would leak it past the end of Play.
            _holder = new GameObject("HeldLight").transform;
            if (_look == null)
                BuildStandIn(item);

            _light = _holder.gameObject.AddComponent<NightLight>();
            _light.held = true;
            _light.kind = item.IsTorch ? NightLight.Kind.Torch : NightLight.Kind.Lantern;
            // Carried, so a little gentler than one on a wall; the bullseye throws its light furthest.
            bool bullseye = item.baseName == "Bullseye Lantern";
            _light.range = item.IsTorch ? 8f : bullseye ? 11f : 7f;
            _light.intensity = item.IsTorch ? 1.8f : bullseye ? 2.2f : 1.6f;
            LateUpdate();
        }

        /// <summary>
        /// The off hand: the bone the item's own harvested model rides (the left hand), else the
        /// bone a shield rides (the forearm, near enough for a stand-in), else null.
        /// </summary>
        Transform FindHand(Item item)
        {
            var library = _visuals != null ? _visuals.library : null;
            var look = library != null ? library.For(item) : null;
            if (look == null || look.attachedPieces.Length == 0)
                look = library != null ? library.Find("Shield") : null;
            string bone = look != null && look.attachedPieces.Length > 0 ? look.attachedPieces[0].bone : null;
            if (string.IsNullOrEmpty(bone))
                return null;
            var root = _movement != null && _movement.modelRoot != null ? _movement.modelRoot : transform;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == bone)
                    return t;
            }
            return null;
        }

        /// <summary>The item's own mesh in the hand, if Harvest Gear has a look for it (EquipmentVisuals put it there).</summary>
        Renderer HarvestedLook(Item item)
        {
            var library = _visuals != null ? _visuals.library : null;
            var look = library != null ? library.For(item) : null;
            if (look == null || look.attachedPieces.Length == 0 || _hand == null)
                return null;
            string pieceName = look.attachedPieces[0].name;
            foreach (var renderer in _hand.GetComponentsInChildren<MeshRenderer>())
            {
                if (renderer.gameObject.name == pieceName)
                    return renderer;
            }
            return null;
        }

        /// <summary>
        /// Where the flame goes on a held model, in its own space. A torch: just inside the end of
        /// its long axis furthest from the grip — item models have their origin at the hand — so
        /// it stays on the head however the hand turns. (A lantern hangs, so LateUpdate places it by the world instead.)
        /// </summary>
        static Vector3 FlamePoint(Mesh mesh, bool torch)
        {
            if (mesh == null)
                return Vector3.zero;
            var bounds = mesh.bounds;
            if (!torch)
                return bounds.center;

            var size = bounds.size;
            int axis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
            bool towardMax = Mathf.Abs(bounds.max[axis]) >= Mathf.Abs(bounds.min[axis]);
            var point = bounds.center;
            point[axis] = towardMax ? bounds.max[axis] - size[axis] * 0.08f : bounds.min[axis] + size[axis] * 0.08f;
            return point;
        }

        /// <summary>Until there's a real model: a stick for the torch, a small copy of a ship's lantern.</summary>
        void BuildStandIn(Item item)
        {
            if (item.IsTorch)
            {
                var stick = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                stick.name = "Torch (stand-in)";
                Destroy(stick.GetComponent<Collider>());
                stick.transform.SetParent(_holder, false);
                // Half a metre of wood, gripped near the bottom; the flame sits on its top.
                stick.transform.localScale = new Vector3(0.05f, 0.25f, 0.05f);
                stick.transform.localPosition = new Vector3(0f, 0.15f, 0f);
                var renderer = stick.GetComponent<Renderer>();
                renderer.material.color = new Color(0.3f, 0.2f, 0.12f);
                if (renderer.material.HasProperty("_BaseColor"))
                    renderer.material.SetColor("_BaseColor", new Color(0.3f, 0.2f, 0.12f));
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                return;
            }

            // Any lantern in the scene that isn't someone's hand-held one.
            NightLight source = null;
            foreach (var light in FindObjectsByType<NightLight>(FindObjectsSortMode.None))
            {
                if (!light.held && light.kind == NightLight.Kind.Lantern)
                {
                    source = light;
                    break;
                }
            }
            if (source == null)
                return;

            var copy = Instantiate(source.gameObject, _holder);
            copy.name = "Lantern (stand-in)";
            foreach (var extra in copy.GetComponentsInChildren<NightLight>(true))
                Destroy(extra);
            foreach (var collider in copy.GetComponentsInChildren<Collider>(true))
                Destroy(collider);
            foreach (var light in copy.GetComponentsInChildren<Light>(true))
                Destroy(light.gameObject);
            foreach (var particles in copy.GetComponentsInChildren<ParticleSystem>(true))
                Destroy(particles.gameObject);

            // About a hand's span tall, hanging just under the hand.
            copy.transform.localRotation = Quaternion.identity;
            copy.transform.localPosition = Vector3.zero;
            copy.transform.localScale = source.transform.lossyScale;
            var renderers = copy.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return;
            var bounds = renderers[0].bounds;
            foreach (var r in renderers)
                bounds.Encapsulate(r.bounds);
            if (bounds.size.y > 0.01f)
                copy.transform.localScale *= 0.3f / bounds.size.y;
            bounds = renderers[0].bounds;
            foreach (var r in renderers)
                bounds.Encapsulate(r.bounds);
            copy.transform.position += _holder.position - new Vector3(bounds.center.x, bounds.max.y, bounds.center.z) + Vector3.down * 0.05f;
        }
    }
}
