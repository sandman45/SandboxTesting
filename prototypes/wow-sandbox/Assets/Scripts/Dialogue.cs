using System;
using System.Collections.Generic;
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

            string greeting = StoryState.Has(Notes) ? "Captain! Come in, come in. Don't touch anything. Well, touch that. Not that."
                            : "Back again? Excellent! Hand me that... no, the other... never mind, I've got it.";
            return Topics(new DialogueNode(greeting));
        }

        static DialogueNode Topics(DialogueNode node) => node
            .Ask("Why don't you leave?", Leave)
            .AskIf(!StoryState.Has(Notes), "Does your compass work?", Compass)
            .AskIf(StoryState.Has(Notes), "Any luck with the compass?", CompassLater)
            .Ask("What do you know about this isle?", Isle)
            .AskIf(StoryState.Has("briggs.met"), "Have you met my quartermaster, Briggs?", Briggs)
            .Goodbye("Good luck with it.");

        static DialogueNode Wrecked() => Topics(new DialogueNode(
            "Another one? Oh, dear. That's the fourth ship this decade. I keep a tally on the wall. A storm, was it? " +
            "Out of a clear sky, with something big underneath it? ... Thought so. It always is."));

        static DialogueNode HowLong() => Topics(new DialogueNode(
            "Eleven years! Or twelve. I stopped counting when I ran out of wall. I came in on the Cogwhistle, a little " +
            "steam tender out of Booty Bay. The isle ate her. Well, the storm did. Same thing, really."));

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

        static DialogueNode CompassLater() => Topics(new DialogueNode(
            "Not yet! Still points at the island like a dog at a sausage. I'll need parts, proper ones, brass and gears " +
            "off the wrecks. I'll tell you which when I've drawn it up."));

        static DialogueNode Isle() => Topics(new DialogueNode(
            "Ships come in. Ships don't go out. The fish are odd, the weather's odder, and there's something big in the " +
            "water that herds ships like a sheepdog. The trolls were here first and left carvings all over. Very gloomy. " +
            "Very artistic."));

        static DialogueNode Briggs() => Topics(new DialogueNode(
            "The big fellow on the beach? He came up yesterday asking if I'd seen his captain. You, I suppose! Bought six " +
            "fathoms of rope off me, too. I'm not sure why. He's very persuasive."));
    }
}
