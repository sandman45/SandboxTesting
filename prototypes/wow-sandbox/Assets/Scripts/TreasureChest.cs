using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace WowSandbox
{
    /// <summary>
    /// A treasure chest with hand-picked contents: click it to swing it open (the model's own
    /// Open clip, then Opened) and loot it in the loot window, like a corpse. What you leave
    /// stays inside; once it's empty it can't be used again until the next Play. Set the
    /// contents in the Inspector by catalog name ("Longsword", "Potion of Healing",
    /// "Ashbringer") — unknown names are listed in the Console at Play. A chest can also hold a
    /// journal page (journalPage), always first in its loot. WoW Sandbox → Place Treasure
    /// Chest puts one in the scene.
    /// </summary>
    [RequireComponent(typeof(LootDrop))]
    public class TreasureChest : Interactable
    {
        [Serializable]
        public class Content
        {
            [Tooltip("A catalog item's name, e.g. \"Longsword\" — or \"Journal Page 3\" for a journal page.")]
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
        [Tooltip("A journal page inside, by its number in JournalPages: always there, listed first in the " +
                 "loot. 0 for none.")]
        [Min(0)] public int journalPage;

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
            if (journalPage > 0)
            {
                if (JournalPages.TryFind(journalPage, out _))
                    items.Add(ItemCatalog.JournalPage(journalPage));
                else
                    Debug.LogWarning($"[TreasureChest] {name}: there's no journal page {journalPage} — left out.", this);
            }
            foreach (var content in contents)
            {
                if (string.IsNullOrWhiteSpace(content.item))
                    continue;
                if (JournalPages.IsPageName(content.item, out int number))
                {
                    if (JournalPages.TryFind(number, out _))
                        items.Add(ItemCatalog.JournalPage(number));
                    else
                        Debug.LogWarning($"[TreasureChest] {name}: \"{content.item}\" — say which page, e.g. " +
                                         $"\"Journal Page 3\" (1–{JournalPages.All.Length}). Left out.", this);
                    continue;
                }
                if (!ItemCatalog.TryFind(content.item.Trim(), out _))
                {
                    Debug.LogWarning($"[TreasureChest] {name}: no item called \"{content.item}\" in the catalog — left out.", this);
                    continue;
                }
                items.Add(ItemCatalog.Create(content.item.Trim(), content.count));
            }
            for (int i = 0; i < bonusTreasure; i++)
                items.Add(ItemCatalog.RandomTreasure());
            // Supplies for the dark, on top of whatever it holds.
            if (Random.value < 0.4f)
                items.Add(ItemCatalog.Create("Torch", Random.Range(1, 4)));
            if (Random.value < 0.25f)
                items.Add(ItemCatalog.Create("Flask of Oil", Random.Range(1, 3)));
            _loot.Fill(items, copper);
        }

        /// <summary>Whether it's been opened.</summary>
        public bool Opened => _opened;

        /// <summary>
        /// Back as it was saved: open (lid already up) or shut, holding what was left in it.
        /// </summary>
        public void Restore(bool opened, IEnumerable<Item> items, int coin)
        {
            _loot.Fill(items, coin);
            _opened = opened;
            if (opened && _animator != null && _animator.runtimeAnimatorController != null)
            {
                int state = _animator.HasState(0, OpenedStateHash) ? OpenedStateHash : OpenStateHash;
                if (_animator.HasState(0, state))
                    _animator.Play(state, 0, 1f);
            }
        }

        static readonly int OpenedStateHash = Animator.StringToHash("Opened");
        static readonly int OpenStateHash = Animator.StringToHash("Open");

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
