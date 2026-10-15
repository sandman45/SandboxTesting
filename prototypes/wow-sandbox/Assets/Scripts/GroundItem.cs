using UnityEngine;

namespace WowSandbox
{
    /// <summary>
    /// An item lying in the world — a sword in the sand, a pouch of coin: click it from close by
    /// to reach down (the looting reach) and put it in your bags. A full bag leaves it where it
    /// is. Its look is whatever model it was placed with (WoW Sandbox → Place Ground Item picks
    /// one by the item's kind). Gone for good once taken, until the next Play.
    /// </summary>
    public class GroundItem : Interactable
    {
        [Tooltip("A catalog item's name, e.g. \"Longsword\". Empty is just coin.")]
        public string item = "Longsword";
        [Min(1)] public int count = 1;
        [Tooltip("Coin, in copper, picked up with it (or alone).")]
        [Min(0)] public int copper;

        static readonly int LootGrabHash = Animator.StringToHash("LootGrab");

        public override string Verb => "Pick up";

        void Reset() => displayName = item;

        public override void Interact(GameObject player)
        {
            var inventory = player.GetComponent<Inventory>();
            if (inventory == null)
                return;

            if (!string.IsNullOrWhiteSpace(item))
            {
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

            gameObject.SetActive(false);
        }
    }
}
