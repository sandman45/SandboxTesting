using System;
using System.Collections.Generic;
using UnityEngine;

namespace WowSandbox
{
    /// <summary>
    /// Where the player is in the story: which journal pages they've found, and flags for what's
    /// happened ("briggs.met"). Everything that reveals or gates story reads and writes it here.
    /// It starts empty every Play; SaveGame keeps it between sessions.
    /// </summary>
    public static class StoryState
    {
        static readonly HashSet<int> _pages = new();
        static readonly HashSet<string> _flags = new();

        /// <summary>Goes up with every change, so the Journal knows when to rebuild its list.</summary>
        public static int Version { get; private set; }

        /// <summary>A page found for the first time, by number — the Journal opens on it.</summary>
        public static event Action<int> PageFound;

        public static bool HasPage(int number) => _pages.Contains(number);

        public static int PageCount => _pages.Count;

        /// <summary>Whether every one of <paramref name="numbers"/> has been found.</summary>
        public static bool HasPages(params int[] numbers)
        {
            foreach (int number in numbers)
                if (!_pages.Contains(number))
                    return false;
            return true;
        }

        /// <summary>Whether <paramref name="flag"/> has happened — "briggs.met", say.</summary>
        public static bool Has(string flag) => _flags.Contains(flag);

        /// <summary>Marks <paramref name="flag"/> as having happened.</summary>
        public static void Set(string flag)
        {
            if (_flags.Add(flag))
                Version++;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetState()
        {
            _pages.Clear();
            _flags.Clear();
            PageFound = null;
            Version++;
        }

        /// <summary>
        /// Finds journal page <paramref name="number"/>: says so, and opens the Journal on it.
        /// False if it was already found or isn't in JournalPages.
        /// </summary>
        public static bool FindPage(int number, bool announce = true)
        {
            if (!JournalPages.TryFind(number, out var page))
            {
                Debug.LogWarning($"[StoryState] There's no journal page {number}.");
                return false;
            }
            if (!_pages.Add(number))
                return false;
            Version++;
            if (announce)
            {
                Hud.Info($"A memory returns: <i>{page.title}</i>");
                PageFound?.Invoke(number);
            }
            return true;
        }

        /// <summary>Un-happens <paramref name="flag"/> — for the Developer panel's resets.</summary>
        public static void Clear(string flag)
        {
            if (_flags.Remove(flag))
                Version++;
        }

        /// <summary>Everything found and everything that's happened, for SaveGame.</summary>
        public static void Snapshot(out int[] pages, out string[] flags)
        {
            pages = new int[_pages.Count];
            _pages.CopyTo(pages);
            flags = new string[_flags.Count];
            _flags.CopyTo(flags);
        }

        /// <summary>Back to a saved story, quietly: replaces what's been found and what's happened.</summary>
        public static void Restore(int[] pages, string[] flags)
        {
            _pages.Clear();
            _flags.Clear();
            if (pages != null)
                _pages.UnionWith(pages);
            if (flags != null)
                _flags.UnionWith(flags);
            Version++;
        }

        /// <summary>Every page at once, quietly — the Developer panel's All Pages.</summary>
        public static void FindAllPages()
        {
            foreach (var page in JournalPages.All)
                FindPage(page.number, announce: false);
        }
    }
}
