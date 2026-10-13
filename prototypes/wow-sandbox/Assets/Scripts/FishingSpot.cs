using UnityEngine;
using UnityEngine.InputSystem;

namespace WowSandbox
{
    /// <summary>
    /// A swirl of fish in the shallows (the fishfrenzy doodad) that you can fish, like a
    /// WoW fishing pool. Click it from within reach — from the beach, the deck or the water —
    /// to cast: the line splashes down, a "Fishing" cast bar fills for castSeconds, and when
    /// it's full either a fish bites — a big splash and it's in your bags — or nothing does.
    /// Moving, wandering out of reach or dying interrupts the cast. The warrior acts it out
    /// (WarriorSetup's FishCast/Fishing/Cheer animator states) unless swimming. After a few catches the school scatters: it vanishes and
    /// comes back a while later.
    ///
    /// The further out a spot is, the better the fish: its distance past the island's edge
    /// blends the odds from nearOdds (at the shore) to farOdds (at farOffshore and beyond),
    /// one weight per rarity — Raw Fish up to the Legendary Gilded Kingfish (ItemCatalog.Fish).
    ///
    /// The click target is an invisible trigger sphere over the swirl (WoW Sandbox → Add Sea
    /// Life sizes it), checked here the same way ShipHelm checks its wheel, since a fishing
    /// spot has no Health for TargetingController to find.
    /// </summary>
    [RequireComponent(typeof(SphereCollider))]
    public class FishingSpot : MonoBehaviour
    {
        [Tooltip("How close you have to be to fish it.")]
        public float useRange = 15f;
        [Range(0f, 1f)] public float catchChance = 0.6f;
        [Tooltip("How long each cast takes — the cast bar's length.")]
        public float castSeconds = 3f;
        [Tooltip("Pause after a cast ends before you can cast again.")]
        public float recastDelay = 0.5f;
        [Tooltip("Moving this far (across the ground, not bobbing on the swell) interrupts the cast.")]
        public float interruptDistance = 0.75f;
        [Tooltip("Catches before the school scatters (min, max).")]
        public Vector2Int catchesBeforeScatter = new(3, 5);
        [Tooltip("How long a scattered school takes to come back.")]
        public float respawnSeconds = 90f;
        [Tooltip("Weights for Common, Uncommon, Rare, Very Rare and Legendary fish at a spot by the shore.")]
        public float[] nearOdds = { 75f, 20f, 4.5f, 0.5f, 0f };
        [Tooltip("The same weights at a spot this far past the island's edge, or further.")]
        public float[] farOdds = { 35f, 33f, 20f, 9f, 3f };
        [Tooltip("How far past the island's edge farOdds is reached.")]
        public float farOffshore = 45f;
        [Tooltip("How far under the waves the swirl rides — it follows the surface up and down.")]
        public float depthBelowSurface = 0.8f;

        SphereCollider _sphere;
        Renderer[] _renderers;
        Inventory _player;
        WaterVolume _water;
        HealthController _playerHealth;
        WowCharacterController _playerMovement;
        Animator _playerAnimator;
        float _nextCast;
        float _castStart;
        Vector3 _castFrom;
        Vector3 _bobber;

        /// <summary>The spot you're fishing right now, if any — you can only fish one at a time.</summary>
        static FishingSpot _casting;
        float _backAt;
        int _catchesLeft;

        public bool Scattered { get; private set; }

        void Awake()
        {
            _sphere = GetComponent<SphereCollider>();
            _sphere.isTrigger = true; // clickable, but nothing bumps into it
            _renderers = GetComponentsInChildren<Renderer>();
            _catchesLeft = Random.Range(catchesBeforeScatter.x, catchesBeforeScatter.y + 1);
        }

        void Start() => _water = FindAnyObjectByType<WaterVolume>();

        void Update()
        {
            if (_water != null)
            {
                var position = transform.position;
                position.y = _water.SurfaceHeightAt(position) - depthBelowSurface;
                transform.position = position;
            }

            if (_casting == this)
                UpdateCast();

            if (Scattered)
            {
                if (Time.time >= _backAt)
                    SetScattered(false);
                return;
            }

            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && !Hud.PointerOverUi && ClickedOn(mouse))
                StartCast();
        }

        void OnDisable()
        {
            if (_casting == this)
            {
                _casting = null;
                CastBarHud.Interrupt();
            }
        }

        void StartCast()
        {
            if (_casting == this || Time.time < _nextCast)
                return;
            if (_player == null)
            {
                _player = FindAnyObjectByType<Inventory>();
                _playerHealth = _player != null ? _player.GetComponent<HealthController>() : null;
                _playerMovement = _player != null ? _player.GetComponent<WowCharacterController>() : null;
                _playerAnimator = _player != null ? _player.GetComponentInChildren<Animator>() : null;
            }
            if (_player == null || _playerHealth != null && _playerHealth.IsDead)
                return;

            if (!InReach())
            {
                Hud.Error("You are too far away.");
                return;
            }

            // Casting at another spot reels in the first line.
            if (_casting != null)
                _casting.EndCast(interrupted: true);

            _casting = this;
            _castStart = Time.time;
            _castFrom = _player.transform.position;
            var drop = Random.insideUnitCircle * _sphere.radius * 0.5f;
            _bobber = transform.position + new Vector3(drop.x, 0f, drop.y);
            WaterSplash.Play(Surface(_bobber), 0.3f);
            CastBarHud.Show("Fishing", 0f, castSeconds);

            // Face the water — you'd cast over your shoulder otherwise. Not while swimming,
            // where the camera steers you.
            var toward = _bobber - _player.transform.position;
            toward.y = 0f;
            bool swimming = _playerMovement != null && _playerMovement.IsSwimming;
            if (!swimming && toward.sqrMagnitude > 0.01f)
                _player.transform.rotation = Quaternion.LookRotation(toward, Vector3.up);
            if (!swimming)
            {
                SetAnimator("Fishing", true);
                SetAnimator("FishCast");
            }
        }

        void UpdateCast()
        {
            var moved = _player.transform.position - _castFrom;
            moved.y = 0f;
            if (moved.magnitude > interruptDistance || !InReach() || Scattered ||
                _playerHealth != null && _playerHealth.IsDead)
            {
                EndCast(interrupted: true);
                return;
            }

            float elapsed = Time.time - _castStart;
            if (elapsed < castSeconds)
            {
                CastBarHud.Show("Fishing", elapsed / castSeconds, castSeconds - elapsed);
                return;
            }

            EndCast(interrupted: false);
            Land();
        }

        void EndCast(bool interrupted)
        {
            if (_casting != this)
                return;
            _casting = null;
            _nextCast = Time.time + recastDelay;
            SetAnimator("Fishing", false);
            if (interrupted)
                CastBarHud.Interrupt();
            else
                CastBarHud.Finish();
        }

        /// <summary>Sets a trigger (no value) or bool on the player's animator, if it has that parameter.</summary>
        void SetAnimator(string parameter, bool? value = null)
        {
            if (_playerAnimator == null || _playerAnimator.runtimeAnimatorController == null)
                return;
            foreach (var p in _playerAnimator.parameters)
            {
                if (p.name != parameter)
                    continue;
                if (value.HasValue)
                    _playerAnimator.SetBool(parameter, value.Value);
                else
                    _playerAnimator.SetTrigger(parameter);
                return;
            }
        }

        bool InReach() => Vector3.Distance(_player.transform.position, transform.position) <= useRange;

        /// <summary><paramref name="point"/> moved up or down onto the waves.</summary>
        Vector3 Surface(Vector3 point)
        {
            if (_water != null)
                point.y = _water.SurfaceHeightAt(point);
            return point;
        }

        /// <summary>The end of a full cast: a bite or not, and the fish if there's room for it.</summary>
        void Land()
        {
            if (Random.value > catchChance)
            {
                Hud.Info("Nothing bites.");
                return;
            }

            WaterSplash.Play(Surface(_bobber), 1f);

            var fish = ItemCatalog.Fish[(int)RollRarity()].Clone();
            if (!_player.Add(fish))
            {
                Hud.Error("Your bags are full.");
                return;
            }

            if (fish.rarity >= ItemRarity.Rare && !(_playerMovement != null && _playerMovement.IsSwimming))
                SetAnimator("Cheer");

            string name = $"<color=#{HudTheme.Hex(HudTheme.RarityColor(fish.rarity))}>{fish.name}</color>";
            Hud.Info(fish.rarity >= ItemRarity.Rare ? $"What a catch! You land a {name}!" : $"You catch a {name}.");
            if (--_catchesLeft <= 0)
            {
                Hud.Info("The school scatters.");
                SetScattered(true);
            }
        }

        /// <summary>How far out this spot is, from 0 at the shore to 1 at farOffshore.</summary>
        public float Remoteness => farOffshore > 0f ? Mathf.Clamp01(AmbientSwimmer.Offshore(transform.position) / farOffshore) : 1f;

        ItemRarity RollRarity()
        {
            float t = Remoteness;
            int tiers = Mathf.Min(ItemCatalog.Fish.Length, nearOdds.Length, farOdds.Length);
            float total = 0f;
            for (int i = 0; i < tiers; i++)
                total += Mathf.Max(Mathf.Lerp(nearOdds[i], farOdds[i], t), 0f);

            float roll = Random.value * total;
            for (int i = 0; i < tiers; i++)
            {
                roll -= Mathf.Max(Mathf.Lerp(nearOdds[i], farOdds[i], t), 0f);
                if (roll < 0f)
                    return (ItemRarity)i;
            }
            return ItemRarity.Common;
        }

        void SetScattered(bool scattered)
        {
            Scattered = scattered;
            _sphere.enabled = !scattered;
            foreach (var r in _renderers)
            {
                if (r != null)
                    r.enabled = !scattered;
            }

            if (scattered)
                _backAt = Time.time + respawnSeconds;
            else
                _catchesLeft = Random.Range(catchesBeforeScatter.x, catchesBeforeScatter.y + 1);
        }

        /// <summary>Did the click land on the swirl, rather than on something solid in front of it?</summary>
        bool ClickedOn(Mouse mouse)
        {
            var cam = Camera.main;
            if (cam == null)
                return false;

            var ray = cam.ScreenPointToRay(mouse.position.ReadValue());
            float spot = float.MaxValue, blocker = float.MaxValue;
            foreach (var hit in Physics.RaycastAll(ray, 500f, ~0, QueryTriggerInteraction.Collide))
            {
                if (hit.collider == _sphere)
                    spot = hit.distance;
                else if (!hit.collider.isTrigger && hit.collider.GetComponentInParent<WowCharacterController>() == null)
                    blocker = Mathf.Min(blocker, hit.distance);
            }

            return spot < blocker;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.4f);
            Gizmos.DrawWireSphere(transform.position, useRange);
        }
    }
}
