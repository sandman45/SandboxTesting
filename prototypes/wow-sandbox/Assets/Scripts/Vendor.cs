using System.Collections.Generic;
using UnityEngine;

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

        static readonly int WaveHash = Animator.StringToHash("Wave");
        static readonly int TalkHash = Animator.StringToHash("Talk");

        Item[] _stock;
        readonly List<(Item item, int price)> _buyback = new();
        Animator _animator;
        Quaternion _facing;
        bool _turning;

        public IReadOnlyList<Item> Stock => _stock ??= ItemCatalog.VendorStock();
        public IReadOnlyList<(Item item, int price)> Buyback => _buyback;

        void Awake()
        {
            _animator = GetComponentInChildren<Animator>();
            GetComponent<Health>().reaction = Reaction.Friendly;
            HideEyeGlow(gameObject);
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
            if (!_turning)
                return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, _facing, turnSpeed * Time.deltaTime);
            _turning = Quaternion.Angle(transform.rotation, _facing) > 0.5f;
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
