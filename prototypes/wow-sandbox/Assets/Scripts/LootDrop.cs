using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace WowSandbox
{
    public enum LootTable
    {
        None,
        /// <summary>Chickens and the like: feathers and meat, no coin.</summary>
        Beast,
        /// <summary>Thieves and other people: coin, trade goods, gear, sometimes magic.</summary>
        Humanoid,
    }

    /// <summary>
    /// Rolls an NPC's loot the moment it dies and holds it on the corpse until it's taken
    /// or the corpse despawns. LootPanel shows it when the player clicks the body.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class LootDrop : MonoBehaviour
    {
        public LootTable table = LootTable.Humanoid;
        [Tooltip("Humanoids: chance of dropping an item at all, on top of their coin.")]
        [Range(0f, 1f)] public float itemChance = 0.85f;

        // Weights for what a humanoid's item is. Rare and Very Rare are switched off for now
        // (2026-10-08) — give them weight again to bring them back into drops.
        static readonly (Func<Item> make, int weight)[] HumanoidDrops =
        {
            (ItemCatalog.RandomTradeGood, 40),
            (ItemCatalog.RandomMundane, 35),
            (() => ItemCatalog.RandomMagic(ItemRarity.Uncommon), 18),
            (() => ItemCatalog.RandomMagic(ItemRarity.Rare), 0),
            (() => ItemCatalog.RandomMagic(ItemRarity.VeryRare), 0),
        };

        readonly List<Item> _items = new();
        Health _health;

        public IReadOnlyList<Item> Items => _items;
        public int Copper { get; private set; }
        public bool HasLoot => _items.Count > 0 || Copper > 0;

        public event Action Changed;

        void Awake()
        {
            _health = GetComponent<Health>();
            _health.Died += Roll;
        }

        void OnDestroy()
        {
            if (_health != null)
                _health.Died -= Roll;
        }

        void Roll()
        {
            _items.Clear();
            Copper = 0;

            switch (table)
            {
                case LootTable.Beast:
                    float roll = Random.value;
                    if (roll < 0.6f)
                        _items.Add(ItemCatalog.Create("Chicken Feather", Random.Range(1, 4)));
                    else if (roll < 0.9f)
                        _items.Add(ItemCatalog.Create("Raw Chicken"));
                    break;

                case LootTable.Humanoid:
                    var stats = GetComponent<CharacterStats>();
                    int level = stats != null ? stats.level : 1;
                    Copper = Dice.Roll(2, 6) * Money.Silver * level;
                    if (Random.value < itemChance)
                        _items.Add(PickWeighted(HumanoidDrops)());
                    break;
            }

            Changed?.Invoke();
        }

        public Item Take(int index)
        {
            if (index < 0 || index >= _items.Count)
                return null;
            var item = _items[index];
            _items.RemoveAt(index);
            Changed?.Invoke();
            return item;
        }

        public int TakeCopper()
        {
            int amount = Copper;
            Copper = 0;
            Changed?.Invoke();
            return amount;
        }

        static T PickWeighted<T>((T value, int weight)[] entries)
        {
            int total = 0;
            foreach (var entry in entries)
                total += entry.weight;

            int roll = Random.Range(0, total);
            foreach (var (value, weight) in entries)
            {
                if (roll < weight)
                    return value;
                roll -= weight;
            }
            return entries[0].value;
        }
    }
}
