using UnityEngine;

namespace WowSandbox
{
    /// <summary>
    /// An item lying in the world — a sword in the sand, a pouch of coin: click it from close by
    /// to reach down (the looting reach) and put it in your bags. A full bag leaves it where it
    /// is. Its look is whatever model it was placed with (WoW Sandbox → Place Ground Item picks
    /// one by the item's kind). Gone for good once taken — unless it has respawnHours, when
    /// another turns up in its place that many game hours later (driftwood washing up again).
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

            if (!string.IsNullOrWhiteSpace(item))
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
            if (animator != null && animator.runtimeAnimatorController != null)
                foreach (var parameter in animator.parameters)
                    if (parameter.nameHash == LootGrabHash)
                        animator.SetTrigger(LootGrabHash);

            if (respawnHours > 0f)
                ReturnIn(respawnHours);
            else
                gameObject.SetActive(false);
        }
    }
}
