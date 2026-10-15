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
        /// <summary>Sharks: teeth and fins, and now and then whatever's in its stomach.</summary>
        Sea,
    }

    /// <summary>
    /// Rolls an NPC's loot the moment it dies and holds it on the corpse until it's taken
    /// or the corpse despawns. LootPanel shows it when the player clicks the body. Without a
    /// Health it's a container instead — a TreasureChest fills it with <see cref="Fill"/>.
    /// </summary>
    public class LootDrop : MonoBehaviour
    {
        public LootTable table = LootTable.Humanoid;
        [Tooltip("Humanoids: chance of dropping an item at all, on top of their coin.")]
        [Range(0f, 1f)] public float itemChance = 0.85f;

        // Weights for what a humanoid's item is: about 1 drop in 20 is Rare and 1 in 100 Very
        // Rare, like WoW's blues and purples, and about 1 in 25 is a Pirate Hat. Legendaries
        // never drop.
        static readonly (Func<Item> make, int weight)[] HumanoidDrops =
        {
            (ItemCatalog.RandomTradeGood, 40),
            (ItemCatalog.RandomMundane, 35),
            (() => ItemCatalog.Create("Pirate Hat"), 4),
            (() => ItemCatalog.RandomMagic(ItemRarity.Uncommon), 18),
            (() => ItemCatalog.RandomMagic(ItemRarity.Rare), 5),
            (() => ItemCatalog.RandomMagic(ItemRarity.VeryRare), 1),
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
            if (_health != null)
                _health.Died += Roll;
        }

        void OnDestroy()
        {
            if (_health != null)
                _health.Died -= Roll;
        }

        /// <summary>Sets what's in it, for a container: these items and this much coin.</summary>
        public void Fill(IEnumerable<Item> items, int copper)
        {
            _items.Clear();
            _items.AddRange(items);
            Copper = Mathf.Max(0, copper);
            Changed?.Invoke();
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

                case LootTable.Sea:
                    var shark = GetComponent<CharacterStats>();
                    bool giant = shark != null && shark.level >= 8;
                    if (Random.value < 0.7f)
                        _items.Add(ItemCatalog.Create(giant ? "Giant Shark Tooth" : "Shark Tooth", Random.Range(1, 3)));
                    if (Random.value < 0.4f)
                        _items.Add(ItemCatalog.Create("Shark Fin"));
                    // Something it swallowed — the same odds a thief carries anything.
                    if (Random.value < 0.15f)
                        _items.Add(PickWeighted(HumanoidDrops)());
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
