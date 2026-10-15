using System;
using System.Collections.Generic;
using UnityEngine;

namespace WowSandbox
{
    /// <summary>
    /// A treasure chest with hand-picked contents: click it to swing it open (the model's own
    /// Open clip, then Opened) and loot it in the loot window, like a corpse. What you leave
    /// stays inside; once it's empty it can't be used again until the next Play. Set the
    /// contents in the Inspector by catalog name ("Longsword", "Potion of Healing",
    /// "Ashbringer") — unknown names are listed in the Console at Play. WoW Sandbox → Place
    /// Treasure Chest puts one in the scene.
    /// </summary>
    [RequireComponent(typeof(LootDrop))]
    public class TreasureChest : Interactable
    {
        [Serializable]
        public class Content
        {
            [Tooltip("A catalog item's name, e.g. \"Longsword\".")]
            public string item;
            [Min(1)] public int count = 1;
        }

        [Tooltip("What's inside, by catalog name.")]
        public List<Content> contents = new();
        [Tooltip("Coin inside, in copper (100 = 1 silver, 10,000 = 1 gold).")]
        [Min(0)] public int copper;
        [Tooltip("Random treasure added on top of the contents, rolled each Play: Rare 35%, epic 18%, " +
                 "Legendary 7%, else Uncommon. 0 is just the hand-picked contents.")]
        [Min(0)] public int bonusTreasure;

        static readonly int OpenHash = Animator.StringToHash("Open");

        LootDrop _loot;
        Animator _animator;
        bool _opened;

        public override string Verb => "Open";
        public override bool CanInteract => !_opened || _loot.HasLoot;

        void Awake()
        {
            _loot = GetComponent<LootDrop>();
            _animator = GetComponentInChildren<Animator>();

            var items = new List<Item>();
            foreach (var content in contents)
            {
                if (string.IsNullOrWhiteSpace(content.item))
                    continue;
                if (!ItemCatalog.TryFind(content.item.Trim(), out _))
                {
                    Debug.LogWarning($"[TreasureChest] {name}: no item called \"{content.item}\" in the catalog — left out.", this);
                    continue;
                }
                items.Add(ItemCatalog.Create(content.item.Trim(), content.count));
            }
            for (int i = 0; i < bonusTreasure; i++)
                items.Add(ItemCatalog.RandomTreasure());
            _loot.Fill(items, copper);
        }

        /// <summary>Replaces what's inside — the Developer panel's test loot.</summary>
        public void Restock(IEnumerable<Item> items, int coin)
        {
            _loot.Fill(items, coin);
        }

        public override void Interact(GameObject player)
        {
            if (!_opened)
            {
                _opened = true;
                if (_animator != null && _animator.runtimeAnimatorController != null)
                    _animator.SetTrigger(OpenHash);
            }
            if (!_loot.HasLoot)
            {
                Hud.Error("It's empty.");
                return;
            }
            var panel = player.GetComponent<LootPanel>();
            if (panel != null)
                panel.OpenFor(_loot, displayName);
        }
    }
}
