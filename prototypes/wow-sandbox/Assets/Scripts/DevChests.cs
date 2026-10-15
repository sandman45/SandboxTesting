using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WowSandbox
{
    /// <summary>
    /// The chests the Developer panel can spawn: one prefab per exported chest model, built by
    /// the editor into a Resources folder each time Play starts (DevChestBuilder). Each spawned
    /// chest lands in front of you with random test loot.
    /// </summary>
    public static class DevChests
    {
        public const string ResourceFolder = "DevChests";

        /// <summary>The chests to offer, by model name.</summary>
        public static GameObject[] Load() =>
            Resources.LoadAll<GameObject>(ResourceFolder).OrderBy(g => g.name).ToArray();

        /// <summary>A short button label for a chest model: "treasurechest01hd" → "Chest 01 HD".</summary>
        public static string Label(GameObject chest)
        {
            string name = chest.name.ToLowerInvariant();
            if (name.StartsWith("treasurechest"))
            {
                string rest = name.Substring("treasurechest".Length).Trim('_');
                bool hd = rest.EndsWith("hd");
                if (hd) rest = rest.Substring(0, rest.Length - 2);
                return $"Chest {rest}{(hd ? " HD" : "")}".Trim();
            }
            return name.Contains("treasuretracker") ? "Earthen" : chest.name;
        }

        /// <summary>A copy of <paramref name="chest"/> on the ground a few steps in front of the player, facing them.</summary>
        public static void Spawn(GameObject chest)
        {
            var player = Object.FindAnyObjectByType<WowCharacterController>();
            if (player == null || chest == null)
                return;

            var ahead = player.transform.position + player.transform.forward * 3f + Vector3.up * 3f;
            Vector3 ground = player.transform.position + player.transform.forward * 3f;
            foreach (var hit in Physics.RaycastAll(ahead, Vector3.down, 10f, ~0, QueryTriggerInteraction.Ignore)
                                       .OrderBy(h => h.distance))
            {
                if (hit.collider.GetComponentInParent<WowCharacterController>() != null)
                    continue;
                ground = hit.point;
                break;
            }

            var toPlayer = player.transform.position - ground;
            toPlayer.y = 0f;
            var rotation = toPlayer.sqrMagnitude > 0.01f ? Quaternion.LookRotation(toPlayer) : Quaternion.identity;
            var spawned = Object.Instantiate(chest, ground, rotation);
            spawned.name = Label(chest);
            var treasure = spawned.GetComponent<TreasureChest>();
            if (treasure != null)
            {
                treasure.displayName = "Treasure Chest";
                treasure.Restock(TestLoot(out int copper), copper);
            }
        }

        /// <summary>
        /// Something worth opening: a treasure roll (ItemCatalog.RandomTreasure — Rare, epic
        /// or Legendary more often than not), a 1 in 4 chance of a second, only sometimes a
        /// potion, and gold.
        /// </summary>
        static List<Item> TestLoot(out int copper)
        {
            var items = new List<Item> { ItemCatalog.RandomTreasure() };
            if (Random.value < 0.25f)
                items.Add(ItemCatalog.RandomTreasure());
            if (Random.value < 0.3f)
                items.Add(ItemCatalog.Create("Potion of Healing", Random.Range(1, 3)));
            copper = Dice.Roll(2, 6) * Money.Gold;
            return items;
        }
    }
}
