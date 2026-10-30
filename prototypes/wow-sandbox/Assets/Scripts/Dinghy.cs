using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace WowSandbox
{
    /// <summary>
    /// The old dinghy Sprocket tells you about, for rowing out to the Pale Lament (docs/LORE.md).
    /// It lies stove in on the beach (Broken) until you patch it with his hammer and flotsam from
    /// the floating wreckage — a few seconds' work, knelt down to it. Then the whole boat (Boat)
    /// rides in the shallows at its launch spot: click it to climb in and sit, W/S to row, A/D to
    /// turn, Space to stand up in her (step or jump over the side from there). It's a shore boat: it runs aground in
    /// the shallows and won't go out past the Dangerous Waters line. Left alone, it stays put.
    ///
    /// Repaired is a StoryState flag; where the boat's left is saved (SaveGame). WoW Sandbox →
    /// Add Dinghy builds it where the Scene view is looking.
    /// </summary>
    // After the player (0) has moved, so he's sat in the boat where it is this frame (like ShipHelm).
    [DefaultExecutionOrder(10)]
    public class Dinghy : Interactable
    {
        public const string Repaired = "dinghy.repaired";
        /// <summary>Sprocket's told you about it (and lent you the hammer).</summary>
        public const string Told = "dinghy.told";
        public const string Hammer = "Tinker's Hammer";

        /// <summary>What patching it takes, besides the hammer (kept): flotsam off the floating wreckage.</summary>
        public static readonly (string item, int count)[] Patch =
        {
            ("Splintered Plank", 2),
            ("Rusted Nails", 1),
            ("Sodden Sailcloth", 1),
        };

        const float RepairSeconds = 6f;

        /// <summary>The one in the scene.</summary>
        public static Dinghy Current { get; private set; }

        [Tooltip("The stove-in boat on the beach, shown until it's repaired.")]
        public Transform broken;
        [Tooltip("The whole boat, at its launch spot in the shallows. The worgen rowboat's bow is its back (-Z).")]
        public Transform boat;
        [Tooltip("Where he sits, under Boat.")]
        public Transform seat;

        [Header("Rowing")]
        public float rowSpeed = 4.5f;
        [Tooltip("Seconds to get up to speed, and to drift to a stop.")]
        public float pull = 1.5f;
        public float turnRate = 60f;
        [Tooltip("Half the boat's length, for running aground and the waves' pitch.")]
        public float halfLength = 1.8f;
        [Tooltip("How far inside the Dangerous Waters line she'll go.")]
        public float lineMargin = 3f;

        WaterVolume _water;
        WowCharacterController _player;
        float _draft;
        float _speed;
        float _yaw;
        bool _aboard;
        float _warnedAt = -10f;

        public bool IsRepaired => StoryState.Has(Repaired);
        public bool Aboard => _aboard;

        public override string Verb => IsRepaired ? "Climb in" : "Repair";
        public override bool CanInteract => !_aboard;

        protected override void OnEnable()
        {
            base.OnEnable();
            Current = this;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (Current == this)
                Current = null;
            GetOut();
        }

        void Start()
        {
            _water = FindAnyObjectByType<WaterVolume>();
            if (boat != null)
            {
                // However high it sits over calm water as placed is the draft it keeps.
                _draft = _water != null ? boat.position.y - _water.SurfaceY : 0f;
                _yaw = boat.eulerAngles.y;
                MakeSolid();
            }
            Refresh();
        }

        /// <summary>
        /// Solid, so he can't swim or fall through her: a collider baked from each of her meshes
        /// (an M2's are skinned, but never deform), if she hasn't any — and a kinematic body, so
        /// they move with her and he rides her when he stands in her, like a ship's deck.
        /// </summary>
        void MakeSolid()
        {
            bool solid = boat.GetComponentsInChildren<Collider>(true).Any(c => !c.isTrigger);
            if (!solid)
            {
                foreach (var skinned in boat.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var baked = new Mesh { name = skinned.name + " (collider)" };
                    // Baked in the renderer's own space, unscaled, to match where a collider on it sits.
                    skinned.BakeMesh(baked, true);
                    skinned.gameObject.AddComponent<MeshCollider>().sharedMesh = baked;
                }
                foreach (var filter in boat.GetComponentsInChildren<MeshFilter>(true))
                    if (filter.sharedMesh != null && filter.GetComponent<Collider>() == null)
                        filter.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
            }

            var body = boat.GetComponent<Rigidbody>();
            if (body == null)
                body = boat.gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.None;

        }

        /// <summary>Shows the broken boat or the whole one, by the story.</summary>
        public void Refresh()
        {
            bool whole = IsRepaired;
            if (broken != null)
                broken.gameObject.SetActive(!whole);
            if (boat != null)
                boat.gameObject.SetActive(whole);
            displayName = whole ? "Dinghy" : "Broken Dinghy";
        }

        /// <summary>Back where it was saved.</summary>
        public void Restore(Vector3 position, float yaw)
        {
            GetOut();
            Refresh();
            if (boat == null)
                return;
            _yaw = yaw;
            _speed = 0f;
            boat.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        }

        public override void Interact(GameObject player)
        {
            if (IsRepaired)
                ClimbIn(player.GetComponent<WowCharacterController>());
            else
                TryRepair(player);
        }

        // --- Repairing ----------------------------------------------------------------------

        void TryRepair(GameObject player)
        {
            var inventory = player.GetComponent<Inventory>();
            if (inventory == null || player.GetComponent<Patching>() != null)
                return;
            Thoughts.Once("dinghy.broken", "Stove in... but the keel's sound. She'd float, with a few planks.");
            string missing = Missing(inventory);
            if (missing != null)
            {
                Hud.Error(missing);
                return;
            }
            player.AddComponent<Patching>().dinghy = this;
        }

        /// <summary>What's still needed to patch it, said the way the error says it — or null if he has it all.</summary>
        public static string Missing(Inventory inventory)
        {
            var lacking = Patch.Where(need => Count(inventory, need.item) < need.count)
                               .Select(need => $"{need.count} {need.item}").ToList();
            bool hammer = inventory.BestInBags(item => item.baseName == Hammer) != null;
            if (hammer && lacking.Count == 0)
                return null;
            if (!hammer)
                lacking.Insert(0, "a hammer");
            return "To patch it you need " + (lacking.Count == 1 ? lacking[0]
                : string.Join(", ", lacking.Take(lacking.Count - 1)) + " and " + lacking[^1]) + ".";
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

        void FinishRepair(Inventory inventory)
        {
            foreach (var (item, count) in Patch)
                for (int i = 0; i < count; i++)
                    inventory.TakeOneFromBags(item);
            StoryState.Set(Repaired);
            Refresh();
            Hud.Info("You patch up the dinghy and push her out into the shallows.");
            Thoughts.Once("dinghy.repaired", "There. She'll float. Now, out to that wreck.");
        }

        /// <summary>
        /// The few seconds' work of patching it, on the player, knelt down (like Campfire's
        /// kindling): the cast bar, broken off by moving, a blow or dying.
        /// </summary>
        class Patching : MonoBehaviour
        {
            public Dinghy dinghy;

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
                CastBarHud.Show("Patching the dinghy", 0f, RepairSeconds);
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
                if (dinghy == null || moved.magnitude > 0.3f || _health != null && _health.IsDead || _player.IsLocked)
                {
                    Stop(interrupted: true);
                    return;
                }

                float elapsed = Time.time - _started;
                if (elapsed < RepairSeconds)
                {
                    CastBarHud.Show("Patching the dinghy", elapsed / RepairSeconds, RepairSeconds - elapsed);
                    return;
                }

                // Checked again: he may have dropped something meanwhile.
                string missing = Missing(_inventory);
                if (missing != null)
                {
                    Hud.Error(missing);
                    Stop(interrupted: true);
                    return;
                }
                dinghy.FinishRepair(_inventory);
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

        // --- Rowing -------------------------------------------------------------------------

        static readonly int SittingHash = Animator.StringToHash("Sitting");
        static readonly int SitDownHash = Animator.StringToHash("SitDown");

        void ClimbIn(WowCharacterController player)
        {
            if (player == null || _aboard || boat == null || seat == null)
                return;
            if (player.IsLocked || player.IsClimbing)
                return;
            _player = player;
            _aboard = true;
            _speed = 0f;
            player.SetLocked(true);
            SetSitting(true);
            PlacePlayer();
            Thoughts.Once("dinghy.aboard", "Easy now. It's no Stormbreaker, but it floats.");
            Hud.Info("W/S to row, A/D to turn. Space to stand up.");
        }

        /// <summary>Up off the seat, standing in her — riding her like a deck, free to step over the side.</summary>
        public void GetOut()
        {
            if (!_aboard)
                return;
            _aboard = false;
            _speed = 0f;
            if (_player == null)
                return;
            SetSitting(false);
            // A touch above the seat, so he settles onto her floor rather than starting in it.
            if (seat != null)
                _player.transform.position = seat.position + Vector3.up * 0.1f;
            Physics.SyncTransforms();
            _player.SetLocked(false);
            _player = null;
        }

        void SetSitting(bool sitting)
        {
            var animator = _player != null ? _player.Animator : null;
            if (animator == null || animator.runtimeAnimatorController == null)
                return;
            bool has = animator.parameters.Any(p => p.nameHash == SittingHash);
            if (!has)
                return;
            if (sitting)
                animator.SetTrigger(SitDownHash);
            animator.SetBool(SittingHash, sitting);
        }

        void Update()
        {
            if (!IsRepaired || boat == null || _water == null)
                return;

            float throttle = 0f, steer = 0f;
            if (_aboard)
            {
                var health = _player != null ? _player.GetComponent<HealthController>() : null;
                var keyboard = Keyboard.current;
                if (_player == null || health != null && health.IsDead)
                {
                    GetOut();
                    return;
                }
                if (keyboard != null && !HudPanel.ModalOpen)
                {
                    if (keyboard.spaceKey.wasPressedThisFrame)
                    {
                        GetOut();
                        return;
                    }
                    if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) throttle += 1f;
                    if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) throttle -= 0.5f;
                    if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) steer += 1f;
                    if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) steer -= 1f;
                }
            }

            float blend = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(pull, 0.05f));
            _speed = Mathf.Lerp(_speed, throttle * rowSpeed, blend);
            // Turns best with way on, but a dinghy can spin on the spot with one oar.
            _yaw += steer * turnRate * Mathf.Lerp(0.5f, 1f, Mathf.Abs(_speed) / rowSpeed) * Time.deltaTime;

            // The worgen rowboat as Add Dinghy turns it has her bow to the Boat's -Z.
            var bow = Quaternion.Euler(0f, _yaw, 0f) * Vector3.back;
            var position = boat.position;
            var next = position + bow * (_speed * Time.deltaTime);
            if (Mathf.Abs(_speed) > 0.01f && !CanGo(next, bow * Mathf.Sign(_speed)))
            {
                _speed = 0f;
                next = position;
            }
            // Past the line all the same (saved there, say): the swell pushes her back in.
            if (PastLine(next))
                next += InTowardIsle(next) * (1.5f * Time.deltaTime);

            float surface = _water.SurfaceHeightAt(next);
            next.y = surface + _draft;
            // Pitch with the waves along her length.
            float ahead = _water.SurfaceHeightAt(next + bow * halfLength);
            float astern = _water.SurfaceHeightAt(next - bow * halfLength);
            float pitch = Mathf.Clamp(-Mathf.Atan2(ahead - astern, halfLength * 2f) * Mathf.Rad2Deg, -12f, 12f);
            boat.SetPositionAndRotation(next, Quaternion.Euler(pitch, _yaw, Mathf.Sin(Time.time * 1.3f) * 2f));
            // Her colliders where she is now, before anyone swims into them or stands in her.
            Physics.SyncTransforms();

            if (_aboard)
                PlacePlayer();
        }

        /// <summary>Whether she can move on to <paramref name="next"/>, heading <paramref name="way"/> — afloat, and inside the line. Says why not.</summary>
        bool CanGo(Vector3 next, Vector3 way)
        {
            var end = next + way * halfLength;
            if (Seabed.HeightAt(end, _water) > _water.SurfaceY - 0.3f || !_water.Covers(end))
            {
                Warn("She's run aground.");
                return false;
            }
            if (PastLine(end) && !CloserIn(end, boat.position))
            {
                Warn("The swell's too big out here for a little dinghy.");
                return false;
            }
            return true;
        }

        void Warn(string message)
        {
            if (Time.time - _warnedAt < 3f)
                return;
            _warnedAt = Time.time;
            Hud.Error(message);
        }

        float Line()
        {
            var danger = _player != null ? _player.GetComponent<DeepSeaDanger>() : FindAnyObjectByType<DeepSeaDanger>();
            return (danger != null ? danger.dangerMargin : 60f) - lineMargin;
        }

        bool PastLine(Vector3 point) => AmbientSwimmer.Offshore(point) > Line();

        static bool CloserIn(Vector3 a, Vector3 b) => AmbientSwimmer.Offshore(a) < AmbientSwimmer.Offshore(b);

        static Vector3 InTowardIsle(Vector3 point)
        {
            var terrain = Terrain.activeTerrain;
            if (terrain == null)
                return Vector3.zero;
            var size = terrain.terrainData.size;
            var middle = terrain.GetPosition() + new Vector3(size.x * 0.5f, 0f, size.z * 0.5f);
            var inward = middle - point;
            inward.y = 0f;
            return inward.normalized;
        }

        void PlacePlayer()
        {
            if (_player == null || seat == null)
                return;
            // Facing the bow, which is her -Z.
            _player.transform.SetPositionAndRotation(seat.position, Quaternion.Euler(0f, _yaw + 180f, 0f));
            Physics.SyncTransforms();
        }
    }
}
