using UnityEngine;

namespace WowSandbox
{
    /// <summary>
    /// An NPC you can talk to: click them from close by and DialoguePanel opens their
    /// Conversation (Dialogues, by name). The first time you come near, they may shout a line in
    /// a speech bubble and turn to you; as you leave they say goodbye the same way.
    ///
    /// It needs no setup: one is added at Play to every NPC that Dialogues has lines for.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class Speaker : MonoBehaviour
    {
        [Tooltip("How close you have to be to talk.")]
        public float talkRange = 5f;
        [Tooltip("How close you have to come before they call out to you.")]
        public float callOutRange = 15f;
        [Header("Portrait")]
        [Tooltip("Where the dialogue portrait looks, as a fraction of their height: their face.")]
        [Range(0.5f, 1f)] public float portraitHeight = 0.89f;
        [Tooltip("How far in front of their face the portrait camera stands, as a fraction of their height.")]
        [Range(0.2f, 1.5f)] public float portraitDistance = 0.62f;

        public Conversation Conversation { get; private set; }
        public Health Health { get; private set; }
        /// <summary>Their shop, if they keep one — the dialogue's Trade reply opens it.</summary>
        public Vendor Vendor { get; private set; }

        Transform _player;
        bool _calledOut;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AddToSpeakers()
        {
            foreach (var health in FindObjectsByType<Health>(FindObjectsInactive.Include))
                if (health.GetComponent<Speaker>() == null && Dialogues.For(health.name) != null)
                    health.gameObject.AddComponent<Speaker>();
        }

        void Awake()
        {
            Health = GetComponent<Health>();
            Vendor = GetComponent<Vendor>();
            Conversation = Dialogues.For(name);
        }

        /// <summary>Asleep for the night, so there's no talking to them.</summary>
        public bool Asleep => Vendor != null && Vendor.Asleep;

        void Update()
        {
            if (_calledOut || Conversation == null || Health.IsDead || Asleep ||
                TitleScreen.Showing || ShipwreckWake.Lying)
                return;
            if (_player == null)
            {
                var player = FindAnyObjectByType<WowCharacterController>();
                if (player == null)
                    return;
                _player = player.transform;
            }

            if (Vector3.Distance(_player.position, transform.position) > callOutRange)
                return;
            string line = Conversation.CallOut;
            if (line == null)
                return;
            _calledOut = true;
            Say(line);
            Face(_player.position);
        }

        /// <summary>Turns to <paramref name="position"/> with a wave, if they can.</summary>
        public void Face(Vector3 position)
        {
            if (Vendor != null)
            {
                Vendor.Greet(position);
                return;
            }
            if (TryGetComponent<Tinkerer>(out var tinkerer))
            {
                tinkerer.Greet(position);
                return;
            }
            var toward = position - transform.position;
            toward.y = 0f;
            if (toward.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.LookRotation(toward);
        }

        /// <summary>Says <paramref name="line"/> in a bubble over their head.</summary>
        public void Say(string line) => SpeechBubble.Say(transform, Health.Height, line);

        /// <summary>A parting line, as the conversation ends.</summary>
        public void SayFarewell()
        {
            var lines = Conversation?.Farewells;
            if (lines != null && lines.Length > 0)
                Say(lines[Random.Range(0, lines.Length)]);
        }
    }
}
