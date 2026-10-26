using UnityEngine;

namespace WowSandbox
{
    /// <summary>
    /// A campfire the Captain builds: right-click Flint & Steel in the bags with WoodNeeded
    /// Driftwood, and after a few seconds' work (the cast bar — moving or a blow stops it) a fire
    /// stands in front of him. Only on open ground: not in the sea, on a ship, under a roof or
    /// on a steep slope.
    ///
    /// It burns BurnHours game hours, dying down over the last half hour, then goes out, leaving
    /// cold coals. Only one at a time — building another clears the old one, burning or cold.
    /// While he's cooking at it (Cooking) the skillet's over it. Sitting by it lit (X, PlayerSit)
    /// opens the rest window (RestPanel: sleep a few hours away) and the cooking one, and rests him (Hunger.Rest: how he
    /// gets his strength back after his first cooked meal), and the spot he
    /// built it from is where he respawns from then on. SaveGame keeps the fire and that spot.
    ///
    /// Its looks — burning, cooking and cold, WoW's own campfires — are the prefab the editor
    /// builds into a Resources folder (CampfireBuilder); without those models, a stand-in of
    /// stones and logs. The light and flame are a held NightLight, lit day or night while it burns.
    /// </summary>
    public class Campfire : Interactable
    {
        public const string FlintName = "Flint & Steel";
        public const string WoodName = "Driftwood";
        public const int WoodNeeded = 3;
        public const string ResourcePath = "Campfire/Campfire";
        // The prefab's three looks, by child name.
        public const string BurningLook = "Burning";
        public const string CookingLook = "Cooking";
        public const string ColdLook = "Cold";
        const float BuildSeconds = 3f;
        const float BurnHours = 4f;
        const float DyingHours = 0.5f;
        const float RestReach = 4f;
        const float EmberSeconds = 4f;
        const string SitHintFlag = "hint.sit";

        /// <summary>His fire — burning, or the cold coals of the last one — or null.</summary>
        public static Campfire Current { get; private set; }

        float _hoursLeft;
        bool _lit = true;
        bool _cooking;
        float _outAt = -1f;
        NightLight _light;
        Transform _player;
        PlayerSit _sit;
        bool _sittingHere;

        public float HoursLeft => _hoursLeft;
        public bool IsLit => _lit;

        public override string Verb => "Sit by the fire";

        /// <summary>Only while it burns: cold coals are nothing to cook on.</summary>
        public override bool CanInteract => _lit;

        /// <summary>
        /// Sits him down beside it, which opens the rest and cooking windows (Update). Sat already,
        /// it opens them again; where he can't sit, just the cooking.
        /// </summary>
        public override void Interact(GameObject player)
        {
            var sit = player.GetComponent<PlayerSit>();
            if (sit != null && sit.IsSitting)
            {
                RestPanel.OpenAt(this, player);
                CookingPanel.OpenAt(this, player);
            }
            else if (sit == null || !sit.Sit())
                CookingPanel.OpenAt(this, player);
        }

        /// <summary>He's cooking at it: the skillet goes over the fire.</summary>
        public bool Cooking
        {
            get => _cooking;
            set
            {
                _cooking = value;
                ShowLook();
            }
        }

        /// <summary>
        /// A fire at <paramref name="position"/> with <paramref name="hours"/> to burn, or cold coals
        /// (<paramref name="lit"/> false) — building one, or SaveGame. Clears the one before.
        /// </summary>
        public static Campfire Place(Vector3 position, float yaw, float hours = BurnHours, bool lit = true)
        {
            if (Current != null)
                Destroy(Current.gameObject);

            var prefab = Resources.Load<GameObject>(ResourcePath);
            var fire = prefab != null ? Instantiate(prefab) : StandIn();
            fire.name = "Campfire";
            fire.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            var campfire = fire.AddComponent<Campfire>();
            campfire._hoursLeft = hours;
            campfire._lit = lit;
            campfire._light.Lit = lit;
            campfire.ShowLook();
            Current = campfire;
            return campfire;
        }

        /// <summary>Puts it out now, leaving the coals — the Developer panel. Cold already, it's cleared away.</summary>
        public void Extinguish()
        {
            if (_lit)
                GoOut();
            else
                Destroy(gameObject);
        }

        void ShowLook()
        {
            string look = !_lit ? ColdLook : _cooking ? CookingLook : BurningLook;
            foreach (Transform child in transform)
                if (child.name is BurningLook or CookingLook or ColdLook)
                    child.gameObject.SetActive(child.name == look);
        }

        bool HasLook(string look) => transform.Find(look) != null;

        void Awake()
        {
            displayName = "Campfire";
            range = 3f;
            var flame = new GameObject("Fire");
            flame.transform.SetParent(transform, false);
            flame.transform.localPosition = new Vector3(0f, 0.3f, 0f);
            _light = flame.AddComponent<NightLight>();
            _light.kind = NightLight.Kind.Campfire;
            _light.held = true;
            _light.Lit = true;
            _light.castShadows = true;
            _light.lightOffset = Vector3.zero;
        }

        void Start()
        {
            var player = FindAnyObjectByType<WowCharacterController>();
            _player = player != null ? player.transform : null;
            _sit = player != null ? player.GetComponent<PlayerSit>() : null;
        }

        void OnDestroy()
        {
            if (Current == this)
                Current = null;
        }

        void Update()
        {
            if (_outAt >= 0f && Time.time >= _outAt)
            {
                Destroy(gameObject);
                return;
            }
            if (!_lit)
                return;

            _hoursLeft -= DayNightCycle.HoursThisFrame;
            _light.heldLevel = Mathf.Lerp(0.3f, 1f, Mathf.Clamp01(_hoursLeft / DyingHours));
            if (_hoursLeft <= 0f)
            {
                Hud.Info("Your campfire burns out.");
                GoOut();
                return;
            }

            // Sitting down beside it opens the rest window, and the cooking one beside it.
            bool sittingHere = _player != null && _sit != null && _sit.IsSitting &&
                               Vector3.ProjectOnPlane(_player.position - transform.position, Vector3.up).magnitude <= RestReach;
            if (sittingHere && !_sittingHere)
            {
                RestPanel.OpenAt(this, _player.gameObject);
                CookingPanel.OpenAt(this, _player.gameObject);
            }
            _sittingHere = sittingHere;

            if (_player != null && Hunger.Active != null)
            {
                var offset = _player.position - transform.position;
                offset.y = 0f;
                if (offset.magnitude <= RestReach)
                {
                    // Resting is sitting by it.
                    if (_sit == null || _sit.IsSitting)
                        Hunger.Active.Rest(Time.deltaTime);
                    else if (Hunger.Weak && !Hunger.StillStarving && !StoryState.Has(SitHintFlag))
                    {
                        // Eaten, still weak: show him how.
                        StoryState.Set(SitHintFlag);
                        Thoughts.Say("I should sit by the fire a while, and rest.");
                        Hud.Info("Press X to sit down by the fire and rest.");
                    }
                    // Starving, with something raw in the bags: a nudge toward cooking it.
                    if (Hunger.StillStarving && HasRawFood())
                        Thoughts.Once("cook.hint", "A fire at last... I could cook something over it.");
                }
            }
        }

        /// <summary>Out: cold coals where it was — or, the stand-in having none, gone after its embers die.</summary>
        void GoOut()
        {
            _lit = false;
            _hoursLeft = 0f;
            _cooking = false;
            _light.Lit = false;
            ShowLook();
            if (!HasLook(ColdLook))
                _outAt = Time.time + EmberSeconds;
        }

        bool HasRawFood()
        {
            var inventory = _player.GetComponent<Inventory>();
            if (inventory == null)
                return false;
            for (int i = 0; i < inventory.BagSize; i++)
            {
                var item = inventory.BagItem(i);
                if (item == null)
                    continue;
                foreach (var (raw, _) in CookingPanel.Recipes)
                    if (item.baseName == raw)
                        return true;
            }
            return false;
        }

        // --- Building one ----------------------------------------------------------------------

        /// <summary>Right-clicking the flint: starts the work, or says why he can't.</summary>
        public static void StartBuilding(Inventory inventory)
        {
            var player = inventory.GetComponent<WowCharacterController>();
            var health = inventory.GetComponent<HealthController>();
            if (player == null || health != null && health.IsDead || player.IsLocked)
                return;
            if (Count(inventory, WoodName) < WoodNeeded)
            {
                Hud.Error($"You need {WoodNeeded} {WoodName} to build a fire.");
                return;
            }
            if (!SpotFor(player, out _, out var error))
            {
                Hud.Error(error);
                return;
            }
            if (inventory.GetComponent<Kindling>() == null)
                inventory.gameObject.AddComponent<Kindling>();
        }

        static int Count(Inventory inventory, string itemName)
        {
            int count = 0;
            for (int i = 0; i < inventory.BagSize; i++)
            {
                var item = inventory.BagItem(i);
                if (item != null && item.baseName == itemName)
                    count += item.count;
            }
            return count;
        }

        /// <summary>
        /// Where a fire would go: on the ground a step in front of him — if that's somewhere a fire
        /// can be. False, with why, if it isn't.
        /// </summary>
        public static bool SpotFor(WowCharacterController player, out Vector3 point, out string error)
        {
            point = default;
            error = null;
            if (player.IsSwimming)
                return Fail("You can't build a fire in the water.", out error);
            if (player.IsClimbing)
                return Fail("You can't build a fire here.", out error);
            if (player.Platform != null || player.GetComponentInParent<ShipBuoyancy>() != null)
                return Fail("You can't build a fire on a ship.", out error);

            var from = player.transform.position + player.transform.forward * 1.4f + Vector3.up * 2f;
            var hits = Physics.RaycastAll(from, Vector3.down, 5f, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            RaycastHit ground = default;
            bool found = false;
            foreach (var hit in hits)
            {
                if (hit.collider.transform.IsChildOf(player.transform))
                    continue;
                ground = hit;
                found = true;
                break;
            }
            if (!found)
                return Fail("There's no ground to build on there.", out error);

            var body = ground.collider.attachedRigidbody;
            if (body != null && body.isKinematic || ground.collider.GetComponentInParent<ShipBuoyancy>() != null)
                return Fail("You can't build a fire on a ship.", out error);
            var water = WaterVolume.Containing(ground.point + Vector3.up * 0.05f);
            if (water != null && ground.point.y < water.SurfaceHeightAt(ground.point) + 0.05f)
                return Fail("You can't build a fire in the water.", out error);
            if (ground.normal.y < 0.75f)
                return Fail("The ground's too steep here.", out error);
            // A roof over it: indoors.
            if (Physics.Raycast(ground.point + Vector3.up * 0.5f, Vector3.up, 12f, ~0, QueryTriggerInteraction.Ignore))
                return Fail("You can't build a fire indoors.", out error);

            point = ground.point;
            return true;
        }

        static bool Fail(string why, out string error)
        {
            error = why;
            return false;
        }

        /// <summary>
        /// The few seconds' work of building it, on the player, knelt down to it (the loot
        /// window's kneel): the cast bar, broken off by moving, a blow or dying. Done, it takes
        /// the driftwood and lights the fire.
        /// </summary>
        class Kindling : MonoBehaviour
        {
            Inventory _inventory;
            WowCharacterController _player;
            HealthController _health;
            Vector3 _from;
            float _started;

            void Start()
            {
                _inventory = GetComponent<Inventory>();
                _player = GetComponent<WowCharacterController>();
                _health = GetComponent<HealthController>();
                _from = transform.position;
                _started = Time.time;
                Kneel(true);
                CastBarHud.Show("Building a fire", 0f, BuildSeconds);
                Combat.PlayerAttacked += OnAttacked;
            }

            void OnDestroy() => Combat.PlayerAttacked -= OnAttacked;

            void OnAttacked(HealthController target, AttackResult result)
            {
                if (result.Hit)
                    Stop(interrupted: true);
            }

            void Update()
            {
                var moved = transform.position - _from;
                moved.y = 0f;
                if (moved.magnitude > 0.3f || _health != null && _health.IsDead || _player.IsLocked)
                {
                    Stop(interrupted: true);
                    return;
                }

                float elapsed = Time.time - _started;
                if (elapsed < BuildSeconds)
                {
                    CastBarHud.Show("Building a fire", elapsed / BuildSeconds, BuildSeconds - elapsed);
                    return;
                }

                // Checked again: he may have turned to face somewhere a fire can't go.
                if (!SpotFor(_player, out var point, out var error) || Count(_inventory, WoodName) < WoodNeeded)
                {
                    Hud.Error(error ?? $"You need {WoodNeeded} {WoodName} to build a fire.");
                    Stop(interrupted: true);
                    return;
                }
                for (int i = 0; i < WoodNeeded; i++)
                    _inventory.TakeOneFromBags(WoodName);

                var toward = transform.position - point;
                toward.y = 0f;
                Place(point, Random.Range(0f, 360f));
                // Here's where he comes back to now.
                if (_health != null)
                    _health.SetSpawn(transform.position, toward.sqrMagnitude > 0.01f
                        ? Quaternion.LookRotation(-toward) : transform.rotation);
                Hud.Info("You build a campfire.");
                Thoughts.Once("campfire", "There. Warmth, at last.");
                Stop(interrupted: false);
            }

            void Kneel(bool down)
            {
                var loot = GetComponent<LootPanel>();
                if (loot != null)
                    loot.HeldDown = down;
            }

            void Stop(bool interrupted)
            {
                Kneel(false);
                if (interrupted)
                    CastBarHud.Interrupt();
                else
                    CastBarHud.Finish();
                Destroy(this);
            }
        }

        /// <summary>Without the exported models: a ring of grey stones round a few crossed logs.</summary>
        static GameObject StandIn()
        {
            var root = new GameObject("Campfire");
            // Something to click, for cooking.
            var box = root.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = new Vector3(0f, 0.3f, 0f);
            box.size = new Vector3(1.4f, 0.6f, 1.4f);
            var stone = new Color(0.42f, 0.41f, 0.4f);
            var wood = new Color(0.33f, 0.22f, 0.13f);
            for (int i = 0; i < 9; i++)
            {
                float angle = i * Mathf.PI * 2f / 9f;
                var piece = Part(PrimitiveType.Sphere, root.transform, stone);
                piece.localPosition = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 0.55f;
                piece.localScale = new Vector3(0.26f, 0.16f, 0.22f);
                piece.localRotation = Quaternion.Euler(0f, angle * Mathf.Rad2Deg, 0f);
            }
            for (int i = 0; i < 3; i++)
            {
                var log = Part(PrimitiveType.Cylinder, root.transform, wood);
                log.localPosition = new Vector3(0f, 0.09f + i * 0.04f, 0f);
                log.localRotation = Quaternion.Euler(0f, i * 60f, 90f);
                log.localScale = new Vector3(0.12f, 0.4f, 0.12f);
            }
            return root;
        }

        static Transform Part(PrimitiveType shape, Transform parent, Color color)
        {
            var go = GameObject.CreatePrimitive(shape);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            var renderer = go.GetComponent<Renderer>();
            renderer.material.color = color;
            if (renderer.material.HasProperty("_BaseColor"))
                renderer.material.SetColor("_BaseColor", color);
            return go.transform;
        }
    }
}
