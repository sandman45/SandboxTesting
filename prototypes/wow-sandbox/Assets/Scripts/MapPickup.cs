using UnityEngine;

namespace WowSandbox
{
    /// <summary>
    /// A map lying in the world: click it from close by to reach down and learn it — the
    /// minimap appears and M opens the world map from then on (WorldMap.Found). It takes no
    /// bag space. Placed with WoW Sandbox → Place Ground Item (Map). Gone once taken, until
    /// the next Play.
    /// </summary>
    public class MapPickup : Interactable
    {
        static readonly int LootGrabHash = Animator.StringToHash("LootGrab");

        public override string Verb => "Pick up";

        public override bool CanInteract => !WorldMap.Found;

        void Reset() => displayName = "Tattered Map";

        public override void Interact(GameObject player)
        {
            WorldMap.Found = true;
            Hud.Info("You found a map of Tidegrave Isle. Press M to open it.");

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
