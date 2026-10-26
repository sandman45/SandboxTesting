using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WowSandbox
{
    /// <summary>
    /// Sprocket's errand for the fog compass (docs/LORE.md, Act II): four parts salvaged from
    /// the isle's wrecks (ItemCatalog.QuestItems), handed in to him one by one or all at once.
    /// Kept as StoryState flags, so it's saved with the story. SprocketConversation does the
    /// talking; this keeps the score and says where each part lies.
    /// </summary>
    public static class CompassQuest
    {
        /// <summary>He's asked you for the parts.</summary>
        public const string Started = "compass.quest";
        /// <summary>All four handed in.</summary>
        public const string PartsIn = "compass.parts";

        public static IEnumerable<string> Parts => ItemCatalog.QuestItems.Select(item => item.name);

        public static bool IsPart(string itemName) => Parts.Contains(itemName);

        static string GivenFlag(string part) => "compass.gave." + part;

        public static bool Given(string part) => StoryState.Has(GivenFlag(part));

        public static IEnumerable<string> Missing => Parts.Where(part => !Given(part));

        /// <summary>Parts in your bags he hasn't had yet.</summary>
        public static IEnumerable<string> Carried(Inventory inventory) =>
            inventory == null ? Enumerable.Empty<string>()
                              : Missing.Where(part => inventory.BestInBags(item => item.baseName == part) != null);

        /// <summary>
        /// Takes every part he still needs out of the bags, and returns which. Sets PartsIn when
        /// that makes four.
        /// </summary>
        public static List<string> HandIn(Inventory inventory)
        {
            var handed = Carried(inventory).ToList();
            foreach (var part in handed)
            {
                inventory.TakeOneFromBags(part);
                StoryState.Set(GivenFlag(part));
            }
            if (!Missing.Any())
                StoryState.Set(PartsIn);
            return handed;
        }

        /// <summary>
        /// Roughly where <paramref name="part"/> lies, the way Sprocket would say it: "on the north
        /// shore", "sunk off the east shore", from where it's placed relative to the isle's middle.
        /// </summary>
        public static string Where(string part)
        {
            var lying = Object.FindObjectsByType<GroundItem>(FindObjectsSortMode.None)
                              .FirstOrDefault(ground => !ground.Gone && ground.item != null && ground.item.Trim() == part);
            var terrain = Terrain.activeTerrain;
            if (lying == null || terrain == null)
                return "somewhere about the isle";

            var size = terrain.terrainData.size;
            var middle = terrain.GetPosition() + new Vector3(size.x * 0.5f, 0f, size.z * 0.5f);
            var away = lying.transform.position - middle;
            string side = Direction(new Vector2(away.x, away.z));
            var water = Object.FindAnyObjectByType<WaterVolume>();
            bool sunk = water != null && lying.transform.position.y < water.SurfaceY - 0.5f;
            return sunk ? $"sunk off the {side} shore. You'll have to dive for it" : $"on the {side} side of the isle";
        }

        static string Direction(Vector2 away)
        {
            if (away.sqrMagnitude < 1f)
                return "middle";
            // Clockwise from north (+Z), in eighths.
            float degrees = Mathf.Repeat(Mathf.Atan2(away.x, away.y) * Mathf.Rad2Deg, 360f);
            string[] names = { "north", "northeast", "east", "southeast", "south", "southwest", "west", "northwest" };
            return names[Mathf.RoundToInt(degrees / 45f) % 8];
        }
    }
}
