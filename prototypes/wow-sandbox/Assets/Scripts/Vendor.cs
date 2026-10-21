using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace WowSandbox
{
    /// <summary>
    /// Makes an NPC a merchant. Click them from close by and VendorPanel opens their shop:
    /// every mundane piece of gear at its list price and healing potions, in unlimited supply,
    /// and they'll buy anything with a value for half of it. What you sell goes on their
    /// buyback list — the last dozen items, buyable again at the price you got.
    ///
    /// They turn to face you and wave when you open the shop, and talk as you trade (if the
    /// model has those clips — WoW Sandbox → Add Vendor sets them up). Friendly, so they can't
    /// be attacked.
    ///
    /// Closed overnight (<see cref="NightLife"/>): with a <see cref="bed"/> (WoW Sandbox → Place
    /// Vendor Hut) they walk to it at bedtime, sleep (the Asleep animator bool, from Add Night
    /// Animations), and at dawn walk back to where they stand to trade; without one they sleep
    /// on the spot. The NavMeshAgent for the walk is added at Play and only on while walking, so
    /// it never nudges them off their stall.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class Vendor : MonoBehaviour
    {
        public const int BuybackSize = 12;

        [Tooltip("Shown under their name at the top of the shop.")]
        public string greeting = "Ahoy! Take a look at me wares.";
        [Tooltip("How close you have to be to trade.")]
        public float interactRange = 6f;
        [Tooltip("Degrees per second they turn to face a customer.")]
        public float turnSpeed = 240f;
        [Tooltip("Where they sleep — a spot inside their hut. Empty: they sleep where they stand.")]
        public Transform bed;
        public float walkSpeed = 1.6f;

        static readonly int WaveHash = Animator.StringToHash("Wave");
        static readonly int TalkHash = Animator.StringToHash("Talk");
        static readonly int AsleepHash = Animator.StringToHash("Asleep");
        static readonly int SpeedHash = Animator.StringToHash("Speed");

        enum Night { AtStall, ToBed, InBed, GettingUp, ToStall }

        /// <summary>Last to bed and first up, as shopkeepers are.</summary>
        const float BedtimeJitter = 0.5f;

        Item[] _stock;
        readonly List<(Item item, int price)> _buyback = new();
        Animator _animator;
        Quaternion _facing;
        bool _turning;
        NavMeshAgent _agent;
        Vector3 _stallPosition;
        Quaternion _stallRotation;
        Night _night;
        float _upAt;
        bool _started;

        public IReadOnlyList<Item> Stock => _stock ??= ItemCatalog.VendorStock();
        public IReadOnlyList<(Item item, int price)> Buyback => _buyback;

        /// <summary>Lying down asleep.</summary>
        public bool Asleep { get; private set; }

        /// <summary>The shop's shut: asleep, or on the way to bed or back.</summary>
        public bool Closed => _night != Night.AtStall;

        void Awake()
        {
            _animator = GetComponentInChildren<Animator>();
            GetComponent<Health>().reaction = Reaction.Friendly;
            HideEyeGlow(gameObject);
            _stallPosition = transform.position;
            _stallRotation = transform.rotation;
        }

        void Start()
        {
            if (bed == null)
                return;
            _agent = GetComponent<NavMeshAgent>();
            if (_agent == null)
            {
                _agent = gameObject.AddComponent<NavMeshAgent>();
                _agent.radius = 0.35f;
                _agent.height = 1.8f;
                _agent.angularSpeed = 300f;
            }
            _agent.speed = walkSpeed;
            _agent.enabled = false;
        }

        /// <summary>
        /// HD character exports include an EyeGlow geoset textured with the eye-reflection map.
        /// In WoW that's blended faintly over the eyes; drawn as an ordinary opaque mesh it's a
        /// solid white sheet over them. The real eyes are a separate geoset underneath.
        /// </summary>
        public static void HideEyeGlow(GameObject character)
        {
            foreach (var renderer in character.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.name.Contains("EyeGlow"))
                    renderer.enabled = false;
            }
        }

        /// <summary>Remembers a sold item for buyback, newest first; the oldest drops off the end.</summary>
        public void AddBuyback(Item item, int price)
        {
            _buyback.Insert(0, (item, price));
            if (_buyback.Count > BuybackSize)
                _buyback.RemoveAt(_buyback.Count - 1);
        }

        public void RemoveBuyback(int index) => _buyback.RemoveAt(index);

        /// <summary>Turns to the customer and waves.</summary>
        public void Greet(Vector3 customer)
        {
            var toCustomer = customer - transform.position;
            toCustomer.y = 0f;
            if (toCustomer.sqrMagnitude > 0.01f)
            {
                _facing = Quaternion.LookRotation(toCustomer);
                _turning = true;
            }
            Trigger(WaveHash);
        }

        /// <summary>A word or two over a sale.</summary>
        public void Talk() => Trigger(TalkHash);

        void Update()
        {
            UpdateNight();
            if (!_turning)
                return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, _facing, turnSpeed * Time.deltaTime);
            _turning = Quaternion.Angle(transform.rotation, _facing) > 0.5f;
        }

        /// <summary>Off to bed at bedtime and back to the stall at dawn.</summary>
        void UpdateNight()
        {
            bool bedtime = NightLife.IsBedtime(BedtimeJitter);
            if (!_started)
            {
                _started = true;
                // The game opens before dawn: already in bed, not setting off for it.
                if (bedtime && _agent != null)
                {
                    transform.SetPositionAndRotation(BedPosition, bed.rotation);
                    LieDown();
                }
            }
            switch (_night)
            {
                case Night.AtStall:
                    if (!bedtime)
                        break;
                    _turning = false;
                    if (Walk(BedPosition))
                        _night = Night.ToBed;
                    else
                        LieDown();
                    break;
                case Night.ToBed:
                    if (Arrived())
                    {
                        Stop(BedPosition, bed.rotation);
                        LieDown();
                    }
                    break;
                case Night.InBed:
                    if (bedtime)
                        break;
                    Asleep = false;
                    SetBool(AsleepHash, false);
                    _upAt = Time.time + 2f;    // the get-up plays out first
                    _night = Night.GettingUp;
                    break;
                case Night.GettingUp:
                    if (Time.time < _upAt)
                        break;
                    _night = bed != null && Walk(_stallPosition) ? Night.ToStall : Night.AtStall;
                    break;
                case Night.ToStall:
                    if (Arrived())
                    {
                        Stop(_stallPosition, _stallRotation);
                        _night = Night.AtStall;
                    }
                    break;
            }

            if (_agent != null && _agent.enabled)
                SetFloat(SpeedHash, _agent.velocity.magnitude / Mathf.Max(_agent.speed, 0.01f));
        }

        Vector3 BedPosition => bed != null ? bed.position : transform.position;

        void LieDown()
        {
            _night = Night.InBed;
            Asleep = true;
            SetBool(AsleepHash, true);
        }

        /// <summary>Sets off for <paramref name="target"/>; false if there's no way there (they stay put).</summary>
        bool Walk(Vector3 target)
        {
            if (_agent == null)
                return false;
            _agent.enabled = true;
            if (!_agent.isOnNavMesh && NavMesh.SamplePosition(transform.position, out var near, 2f, NavMesh.AllAreas))
                _agent.Warp(near.position);
            if (!_agent.isOnNavMesh || !_agent.SetDestination(target))
            {
                Debug.LogWarning($"[Vendor] {name} can't walk to {(target == _stallPosition ? "the stall" : "bed")} — " +
                                 "no NavMesh there? Re-run WoW Sandbox → Bake NavMesh.", this);
                _agent.enabled = false;
                return false;
            }
            return true;
        }

        bool Arrived() => !_agent.pathPending && _agent.remainingDistance <= 0.25f;

        /// <summary>Agent off, and exactly on the spot.</summary>
        void Stop(Vector3 position, Quaternion rotation)
        {
            _agent.enabled = false;
            SetFloat(SpeedHash, 0f);
            transform.SetPositionAndRotation(position, rotation);
        }

        void SetFloat(int hash, float value)
        {
            if (_animator == null || _animator.runtimeAnimatorController == null)
                return;
            foreach (var parameter in _animator.parameters)
            {
                if (parameter.nameHash == hash)
                {
                    _animator.SetFloat(hash, value);
                    return;
                }
            }
        }

        void SetBool(int hash, bool value)
        {
            if (_animator == null || _animator.runtimeAnimatorController == null)
                return;
            foreach (var parameter in _animator.parameters)
            {
                if (parameter.nameHash == hash)
                {
                    _animator.SetBool(hash, value);
                    return;
                }
            }
        }

        void Trigger(int hash)
        {
            if (_animator == null || _animator.runtimeAnimatorController == null)
                return;
            foreach (var parameter in _animator.parameters)
            {
                if (parameter.nameHash == hash)
                {
                    _animator.SetTrigger(hash);
                    return;
                }
            }
        }
    }
}
