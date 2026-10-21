using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace WowSandbox
{
    /// <summary>Whose hand a journal page is in — the Journal groups its pages by this.</summary>
    public enum JournalHand
    {
        /// <summary>The Stormbreaker's log: the Captain's own voice.</summary>
        Log,
        /// <summary>Captain Mareth Kell's journal, a generation old.</summary>
        Kell,
        /// <summary>Notes left by the isle's other castaways: the gnome, the trolls.</summary>
        Castaway,
    }

    public class JournalPage
    {
        /// <summary>The page's number in the story (docs/LORE.md's table), and how it's placed.</summary>
        public readonly int number;
        public readonly JournalHand hand;
        public readonly string title;
        public readonly string text;

        public JournalPage(int number, JournalHand hand, string title, string text)
        {
            this.number = number;
            this.hand = hand;
            this.title = title;
            this.text = text;
        }
    }

    /// <summary>
    /// Every journal page in the story, by number — the texts from docs/LORE.md. Pages are found
    /// in the world (JournalPagePickup), in chests (TreasureChest.journalPage), and later from
    /// NPCs; StoryState remembers which. Pages 4–11 are first drafts.
    /// </summary>
    public static class JournalPages
    {
        public static readonly JournalHand[] Hands = { JournalHand.Log, JournalHand.Kell, JournalHand.Castaway };

        public static string HandName(JournalHand hand) => hand switch
        {
            JournalHand.Log => "The Stormbreaker's Log",
            JournalHand.Kell => "Captain Kell's Journal",
            _ => "Castaways' Notes",
        };

        public static readonly JournalPage[] All =
        {
            new(1, JournalHand.Log, "Third day out",
                "Third day out of Boralus, and the last of the gulls turned back at noon.\n\n" +
                "Briggs swears the glass is falling faster than he's ever seen it fall. The sky to the west " +
                "has gone the colour of a bruise, and it isn't moving with the wind. It's waiting.\n\n" +
                "The Admiral's chart ends here. So do the shipping lanes. From tomorrow we sail off the edge " +
                "of every map in Kul Tiras.\n\n" +
                "I told the crew it's only weather. I hope I'm right."),
            new(2, JournalHand.Log, "Ship's company",
                "Ship's company of the Stormbreaker, as she sailed:\n\n" +
                "Quartermaster Briggs. If I trust anyone aboard, it's him.\n" +
                "Bosun Tamsin Hale. Navigator Corwin Pell. Surgeon Edric Arden.\n" +
                "Twenty-two able hands besides.\n\n" +
                "Twenty-six souls. I swore to the Admiral I'd bring every one of them home."),
            new(3, JournalHand.Log, "The orders",
                "My orders, in the Admiral's own hand:\n\n" +
                "<i>Proceed west beyond the charted lanes, to the isle the sailors call Tidegrave. Chart it. " +
                "Find Captain Mareth Kell, lost there aboard the Grey Gull, or learn what became of Kell. " +
                "Report to no one but myself.</i>\n\n" +
                "Why me? There are older captains in Boralus, and better ones. When I asked, the Admiral " +
                "only said, \"Because it has to be you.\""),
            new(4, JournalHand.Kell, "She still floats",
                "We struck the reef and still she floats. The hull is holed below the waterline and she " +
                "does not take water.\n\n" +
                "Eleven of us ashore. The storm broke the moment we struck, as if it had finished its work.\n\n" +
                "The isle wants her kept. I do not know yet what it wants with us."),
            new(5, JournalHand.Castaway, "Compass Mk VII",
                "COMPASS MK VII\n\n" +
                "Still points at the island. ALWAYS points at the island. Doesn't matter where you stand. " +
                "Took it out in the rowboat, pointed at the island. Took it apart, put it back together, " +
                "pointed at the island.\n\n" +
                "Island is the problem.\n\n" +
                "Need a compass that points AWAY. Is that a thing? Making it a thing."),
            new(6, JournalHand.Castaway, "The warden's stone",
                "A stone carved by the trolls, worn almost smooth. A great beast coils around a heart that " +
                "glows, and tiny ships circle them both, all pointing inward.\n\n" +
                "Under it, in the trolls' tongue, and again in rough Common below:\n\n" +
                "<i>Da sea keep what it call. Da heart call what it keep.</i>"),
            new(7, JournalHand.Kell, "Nine times",
                "Four years. I have tried the fog nine times.\n\n" +
                "The beast does not attack. It turns us back, like a shepherd turns a stray. Each time it " +
                "rises nearer, and each time I see more of it.\n\n" +
                "It is old, and it is tired. I think it has been doing this for a very long time."),
            new(8, JournalHand.Kell, "The child",
                "I think of the child every day.\n\n" +
                "Daelin swore the navy would raise them, and he keeps his word, whatever else he is. " +
                "They will be walking by now. Talking.\n\n" +
                "I hope they never hear of this place."),
            new(9, JournalHand.Log, "The sealed orders",
                "Briggs's second letter, in the Admiral's hand, sealed:\n\n" +
                "<i>The Captain is Mareth Kell's child, and does not know it. The isle will call to that blood " +
                "as it called to Kell's. Whatever Kell found there, it comes home to Boralus, whatever the " +
                "Captain wishes. See it done.</i>"),
            new(10, JournalHand.Castaway, "How wardens are made",
                "Carvings deep in the drowned cave, beside the Heart.\n\n" +
                "A figure walks into the sea. The next panel shows only the beast. The panel after that shows " +
                "the beast again, and a smaller figure walking into the sea.\n\n" +
                "The trolls knew how wardens are made."),
            new(11, JournalHand.Kell, "The last page",
                "The old one is dying, and the prison with it. If it breaks, the thing inside goes out into " +
                "the world. I can hear it promising me the way home.\n\n" +
                "I will not take it. I will take the old one's place.\n\n" +
                "When I tire, the Heart must be brought to me, and only my blood can lift it.\n\n" +
                "If my child ever reads this: I am sorry. I am still here."),
        };

        public static bool TryFind(int number, out JournalPage page)
        {
            page = All.FirstOrDefault(p => p.number == number);
            return page != null;
        }

        /// <summary>
        /// Whether <paramref name="text"/> names a journal page — "Journal Page 3", "Page 3" — as a
        /// chest's contents can. <paramref name="number"/> is 0 when no number was given.
        /// </summary>
        public static bool IsPageName(string text, out int number)
        {
            var match = Regex.Match(text.Trim(), @"^(journal\s*)?page\s*(\d*)$", RegexOptions.IgnoreCase);
            number = match.Success && match.Groups[2].Length > 0 ? int.Parse(match.Groups[2].Value) : 0;
            return match.Success;
        }

        public static IEnumerable<JournalPage> InHand(JournalHand hand) =>
            All.Where(p => p.hand == hand).OrderBy(p => p.number);
    }
}
