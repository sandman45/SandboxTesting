using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WowSandbox
{
    /// <summary>
    /// One thing an NPC says, and the replies you can give. Built fresh each time it's shown, so
    /// what's on offer follows StoryState as it is right then. Plain text: the line is typed out a
    /// letter at a time, which would show rich-text tags half-written.
    /// </summary>
    public class DialogueNode
    {
        public string text;
        public readonly List<DialogueOption> options = new();
        /// <summary>Runs when the line is shown — usually setting a story flag.</summary>
        public Action shown;

        public DialogueNode(string text, Action shown = null)
        {
            this.text = text;
            this.shown = shown;
        }

        /// <summary>A reply that leads to another line.</summary>
        public DialogueNode Ask(string reply, Func<DialogueNode> next)
        {
            options.Add(new DialogueOption { text = reply, next = next });
            return this;
        }

        /// <summary>A reply that leads to another line, offered only while <paramref name="when"/> holds.</summary>
        public DialogueNode AskIf(bool when, string reply, Func<DialogueNode> next)
        {
            if (when)
                Ask(reply, next);
            return this;
        }

        /// <summary>Opens the NPC's shop, if they're a vendor and still trading.</summary>
        public DialogueNode Trade(string reply = "Let me see your wares.")
        {
            options.Add(new DialogueOption { text = reply, kind = DialogueOption.Kind.Trade });
            return this;
        }

        /// <summary>Ends the conversation.</summary>
        public DialogueNode Goodbye(string reply = "Goodbye.")
        {
            options.Add(new DialogueOption { text = reply, kind = DialogueOption.Kind.Goodbye });
            return this;
        }
    }

    public class DialogueOption
    {
        public enum Kind { Talk, Trade, Goodbye }

        public string text;
        public Kind kind = Kind.Talk;
        /// <summary>The line it leads to, for Talk.</summary>
        public Func<DialogueNode> next;
    }

    /// <summary>
    /// An NPC's side of things: what they say when you click them, what they shout the first
    /// time they see you, and what they say as you leave.
    /// </summary>
    public abstract class Conversation
    {
        /// <summary>The name over the dialogue box — "Quartermaster Briggs".</summary>
        public abstract string Title { get; }

        /// <summary>What they say when you start talking, by where the story is.</summary>
        public abstract DialogueNode Start(Speaker speaker);

        /// <summary>A line to shout when you first come near, or null for none. Asked each time
        /// you come in range; it's shouted once per Play at most.</summary>
        public virtual string CallOut => null;

        /// <summary>A parting line said over their head as you go — one picked at random.</summary>
        public virtual string[] Farewells => Array.Empty<string>();
    }

    /// <summary>Every NPC with something to say, found by the name of their GameObject.</summary>
    public static class Dialogues
    {
        static readonly (string name, Conversation conversation)[] All =
        {
            ("Briggs", new BriggsConversation()),
            ("Sprocket", new SprocketConversation()),
        };

        public static Conversation For(string npcName)
        {
            foreach (var (name, conversation) in All)
                if (npcName.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                    return conversation;
            return null;
        }
    }

    /// <summary>
    /// Quartermaster Briggs, stages 0 and 1 of docs/LORE.md: relieved and careful on first
    /// meeting, then helpful about the voyage and the Admiral's orders once the Captain's found
    /// their log (pages 1–3) — and quiet whenever it comes to why the Captain was chosen.
    /// </summary>
    public class BriggsConversation : Conversation
    {
        const string Met = "briggs.met";
        const string Orders = "briggs.orders";

        public override string Title => "Quartermaster Briggs";

        public override string CallOut => StoryState.Has(Met) ? null : "Captain! Over here! Captain!";

        public override string[] Farewells => new[]
        {
            "Mind the tide, Captain.",
            "I'll keep the books straight.",
            "Don't go swimming out past the reef, now.",
        };

        public override DialogueNode Start(Speaker speaker)
        {
            if (!StoryState.Has(Met))
                return new DialogueNode(
                        "Captain! Light be praised, you're alive! I'd near given up hope. ... You're looking at me " +
                        "like a stranger. Don't you know me? Briggs. Your quartermaster these six years.",
                        () => StoryState.Set(Met))
                    .Ask("I... don't remember anything.", Nothing)
                    .Ask("Where are we?", Isle)
                    .Goodbye("I need a moment.");

            string greeting = StoryState.Has(Orders) ? "Captain. Buying or selling? Or just keeping an old sailor company?"
                            : LogFound ? "Captain! You've a look about you. Something's come back, hasn't it?"
                            : "Captain. Any of it coming back yet?";
            return Topics(new DialogueNode(greeting));
        }

        static bool LogFound => StoryState.HasPages(1, 2, 3);

        static DialogueNode Topics(DialogueNode node) => node
            .AskIf(LogFound && !StoryState.Has(Orders), "I found my log. The Admiral sent us to find Kell.", TheOrders)
            .Ask("What happened to the ship?", Ship)
            .Ask("Where are we?", Isle)
            .Ask("Who else made it?", Survivors)
            .AskIf(!LogFound, "Did anything of mine survive?", Belongings)
            .Trade()
            .Goodbye();

        static DialogueNode Nothing() => Topics(new DialogueNode(
            "Nothing? Not the ship, not the storm? ... Well. You took a knock on the rocks, and the sea took the " +
            "rest. It'll come back, Captain. Don't strain yourself."));

        static DialogueNode Ship() => Topics(new DialogueNode(
            "The Stormbreaker, Captain. Finest little ship out of Boralus. The storm came up three days west, " +
            "out of a clear sky, and there was something in it. Under the water. Big as a reef. It put us on " +
            "the rocks. She's there on the beach, what's left of her."));

        static DialogueNode Isle() => Topics(new DialogueNode(
            "Tidegrave, the sailors call it, when they call it anything. It's on no chart I ever saw. The " +
            "Admiral's own chart ran out a day before we struck."));

        static DialogueNode Survivors() => Topics(new DialogueNode(
            "Just the two of us, far as I've found. I've walked the beach twice. ... Some of the others might " +
            "have washed up further round. I keep hoping for Hale, or Pell. Or old Arden and his bag of saws."));

        static DialogueNode Belongings() => Topics(new DialogueNode(
            "Your sea chest went down with the stern, but the wreck's still giving things up with every tide. " +
            "Have a look through her. You always kept your log close, Captain."));

        static DialogueNode TheOrders() => new DialogueNode(
                "Aye, the Admiral's orders. Chart the isle, find Kell. You were proud as a gull when he picked you.",
                () => StoryState.Set(Orders))
            .Ask("Why me, though? There were older captains.", WhyMe)
            .Goodbye();

        static DialogueNode WhyMe() => Topics(new DialogueNode(
            "(He busies himself with a crate.) ... The Admiral had his reasons, Captain. He always does."));
    }

    /// <summary>
    /// Sprocket Wizzlecrank, the gnome in the gnome hut (docs/LORE.md): delighted to have
    /// company, stuck on the isle for years, and certain the way out is an engineering problem.
    /// Asking after his compass gets you his notes on it — journal page 5.
    /// </summary>
    public class SprocketConversation : Conversation
    {
        const string Met = "sprocket.met";
        const string Notes = "sprocket.notes";

        public override string Title => "Sprocket Wizzlecrank";

        public override string CallOut => StoryState.Has(Met) ? null : "Hello? Who's there? Mind the floor, there are springs EVERYWHERE!";

        public override string[] Farewells => new[]
        {
            "Mind the springs on the way out!",
            "Back to it, back to it...",
            "Come again! Bring gears!",
        };

        public override DialogueNode Start(Speaker speaker)
        {
            if (!StoryState.Has(Met))
                return new DialogueNode(
                        "Oh! A person! A NEW person! You're not a thief, are you? No, thieves don't knock. You didn't " +
                        "knock either, but you've an honest sort of face. Sprocket Wizzlecrank: engineer, inventor, and " +
                        "sole proprietor of this establishment!",
                        () => StoryState.Set(Met))
                    .Ask("I'm a ship's captain. We were wrecked on the beach.", Wrecked)
                    .Ask("How long have you been here?", HowLong)
                    .Goodbye("I'll leave you to it.");

            string greeting = StoryState.Has(CompassQuest.PartsIn) ? "Captain! Mind the bench, the compass is in bits. On purpose, this time."
                            : StoryState.Has(CompassQuest.Started) ? "Captain! Any luck with my parts? Tell me you've brought gears."
                            : StoryState.Has(Notes) ? "Captain! Come in, come in. Don't touch anything. Well, touch that. Not that."
                            : "Back again? Excellent! Hand me that... no, the other... never mind, I've got it.";
            return Topics(new DialogueNode(greeting));
        }

        static DialogueNode Topics(DialogueNode node)
        {
            node.Ask("Why don't you leave?", Leave);
            if (!StoryState.Has(Notes))
                node.Ask("Does your compass work?", Compass);
            else if (!StoryState.Has(CompassQuest.Started))
                node.Ask("Any luck with the compass?", CompassLater);
            else if (StoryState.Has(CompassQuest.PartsIn))
                node.Ask("How's the compass coming?", Building);
            else if (CompassQuest.Carried(Player).Any())
                node.Ask("I found something for your compass.", HandIn);
            else
                node.Ask("What do you still need for the compass?", StillNeeded);
            return node
                .Ask("What do you know about this isle?", Isle)
                .AskIf(StoryState.Has("briggs.met"), "Have you met my quartermaster, Briggs?", Briggs)
                .Goodbye("Good luck with it.");
        }

        static Inventory Player => UnityEngine.Object.FindAnyObjectByType<WowCharacterController>()?.GetComponent<Inventory>();

        static DialogueNode Wrecked() => Topics(new DialogueNode(
            "Another one? Oh, dear. That's the fourth ship this decade. I keep a tally on the wall. A storm, was it? " +
            "Out of a clear sky, with something big underneath it? ... Thought so. It always is."));

        static DialogueNode HowLong() => Topics(new DialogueNode(
            "Eleven years! Or twelve. I stopped counting when I ran out of wall. I chartered the Pale Lament out of " +
            "Booty Bay. Forsaken crew. Nobody else would sail me out this way, and they didn't mind the storm at all. " +
            "The reef they minded. She went down off the shore with every one of them still aboard."));

        static DialogueNode Leave() => Topics(new DialogueNode(
            "Oh, I've tried! Rowboat, raft, a sort of flying barrel. The fog turns you round. Sail straight out and you " +
            "sail straight back in, every time. So I thought: what I need is a compass that knows the way out."));

        static DialogueNode Compass() => new DialogueNode(
                "Works? It works PERFECTLY. It just points the wrong way. Always at the island, wherever you stand. Here, " +
                "read my notes, you'll see the problem at once. I've only been at it six years.")
            .Ask("I'll read them.", TakeNotes);

        /// <summary>Hands over page 5 and ends the conversation, so the Journal opens on it.</summary>
        static DialogueNode TakeNotes()
        {
            StoryState.Set(Notes);
            StoryState.FindPage(5);
            return null;
        }

        static DialogueNode CompassLater() => new DialogueNode(
                "Not yet! Still points at the island like a dog at a sausage. I've drawn up a new one, the Mark EIGHT, " +
                "but I need parts. Proper ones, off the wrecks. Four of them. My legs aren't what they were, and the " +
                "sharks have opinions about gnomes.")
            .Ask("I'll find them for you. What do you need?", TakeErrand)
            .Ask("Maybe later.", () => Topics(new DialogueNode("Later's fine! The island isn't going anywhere. That's rather the problem.")));

        static DialogueNode TakeErrand()
        {
            StoryState.Set(CompassQuest.Started);
            return Needed("Splendid! A brass gimbal, a lodestone needle, a dial glass and a good mainspring. Every wreck " +
                          "round this isle has something worth having, if you know where to look. Ask me about any of them.");
        }

        static DialogueNode StillNeeded()
        {
            var missing = CompassQuest.Missing.ToList();
            string list = missing.Count == 1 ? "the " + missing[0]
                        : string.Join(", ", missing.Take(missing.Count - 1).Select(part => "the " + part)) + " and the " + missing[^1];
            return Needed($"Still need {list}. Ask me about any of them.");
        }

        /// <summary>A line, then a reply to ask where each part he still needs is.</summary>
        static DialogueNode Needed(string text)
        {
            var node = new DialogueNode(text);
            foreach (var part in CompassQuest.Missing)
            {
                string asked = part;
                node.Ask($"Where would I find the {asked}?", () => Where(asked));
            }
            return node.Ask("I'll get looking.", () => Topics(new DialogueNode("Off you go! Mind the sharks. And the crabs. And the trees, some of them.")));
        }

        static DialogueNode Where(string part)
        {
            string what = part switch
            {
                "Brass Gimbal" => "A ship's compass sits in a gimbal, so it keeps level when she rolls. Your Stormbreaker " +
                                  "had a fine brass one in her binnacle, I'd wager.",
                "Lodestone Needle" => "A needle cut from lodestone. The Pale Lament had a beauty in her binnacle, the " +
                                      "ship I came in on. She's still down there, crew and all.",
                "Cracked Dial Glass" => "Any ship's compass has a glass over the dial. Cracked is fine. Cracked is " +
                                        "practically traditional.",
                _ => "A good strong mainspring, the clockwork kind. Ship's chronometers have them, and some of these " +
                     "wrecks are old enough to have proper ones.",
            };
            string where = $"{what} Look {CompassQuest.Where(part)}.";
            if (part == "Lodestone Needle" && Dinghy.Current != null && !Dinghy.Current.IsRepaired)
                return DinghyTip(where);
            return Needed(where);
        }

        /// <summary>
        /// The needle's in the Pale Lament, out on the reef: he tells you about the old dinghy on
        /// the beach, and lends you his hammer for it (once).
        /// </summary>
        static DialogueNode DinghyTip(string where)
        {
            string side = CompassQuest.SideOf(Dinghy.Current.transform.position);
            bool lent = StoryState.Has(Dinghy.Told);
            if (!lent)
            {
                StoryState.Set(Dinghy.Told);
                var inventory = Player;
                if (inventory == null || !inventory.Add(ItemCatalog.Create(Dinghy.Hammer)))
                    Hud.Error("Inventory is full.");
                else
                    Hud.Info($"Sprocket hands you his {Dinghy.Hammer}.");
            }
            return Needed(where + (lent
                ? $" Or row out! The dinghy's still on the {side} beach. Two planks, some nails and a scrap of sailcloth. " +
                  "The flotsam out on the water's full of them, if you've a line to fish it up."
                : $" Long swim, mind. But there's an old dinghy stove in on the {side} beach. Patch her up and you can row " +
                  "out. Here, take my hammer. Two planks, some nails and a scrap of sailcloth should do it. Fish them up " +
                  "off the floating wreckage."));
        }

        static DialogueNode HandIn()
        {
            var handed = CompassQuest.HandIn(Player);
            int left = CompassQuest.Missing.Count();
            string got = handed.Count == 1 ? $"The {handed[0]}!" : $"{handed.Count} of them at once!";
            if (handed.Contains("Lodestone Needle"))
                got += " You dove the Pale Lament? Did you see... no. Don't tell me about the crew. I'd rather think of " +
                       "them sailing.";
            if (left == 0)
                return Topics(new DialogueNode(
                    $"{got} That's all four! Oh, Captain. Give me a day at the bench. Possibly two. Possibly an " +
                    "explosion, but a small one."));
            return Topics(new DialogueNode(
                $"{got} Oh, that's lovely, look at that. {left} more to go." +
                (left == 1 ? " Just the one!" : "")));
        }

        static DialogueNode Building() => Topics(new DialogueNode(
            "Coming along! I've had it apart twice and together three times, which is a record. Give me a little longer."));

        static DialogueNode Isle() => Topics(new DialogueNode(
            "Ships come in. Ships don't go out. The fish are odd, the weather's odder, and there's something big in the " +
            "water that herds ships like a sheepdog. The trolls were here first and left carvings all over. Very gloomy. " +
            "Very artistic."));

        static DialogueNode Briggs() => Topics(new DialogueNode(
            "The big fellow on the beach? He came up yesterday asking if I'd seen his captain. You, I suppose! Bought six " +
            "fathoms of rope off me, too. I'm not sure why. He's very persuasive."));
    }
}
