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
}
