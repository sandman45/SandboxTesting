using UnityEngine;

namespace WowSandbox
{
    /// <summary>
    /// A journal page lying in the world: click it from close by to reach down and take it —
    /// a memory returns, and the Journal (J) opens on it. It takes no bag space. Placed with
    /// WoW Sandbox → Place Ground Item (Journal Page). Gone once taken, until the next Play.
    /// </summary>
    public class JournalPagePickup : Interactable
    {
        static readonly int LootGrabHash = Animator.StringToHash("LootGrab");

        [Tooltip("Which page, by its number in JournalPages (docs/LORE.md's table).")]
        [Min(1)] public int page = 1;

        public override string Verb => "Pick up";

        public override bool CanInteract => !StoryState.HasPage(page);

        void Reset() => displayName = "Water-stained Page";

        void Start()
        {
            if (!JournalPages.TryFind(page, out _))
                Debug.LogWarning($"[JournalPagePickup] {name}: there's no journal page {page}.", this);
        }

        public override void Interact(GameObject player)
        {
            StoryState.FindPage(page);

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
