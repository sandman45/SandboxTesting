using UnityEngine;

namespace WowSandbox
{
    /// <summary>
    /// A place the Captain thinks aloud about the first time he comes near it (Thoughts.Once):
    /// within Radius of its models, and only while swimming if asked (a sunk wreck). Once
    /// someone has told him what it is (StoryState flag Known), he thinks the Known lines
    /// instead, once more, so he names it then even if he saw it before.
    /// </summary>
    public class ThoughtSpot : MonoBehaviour
    {
        [Tooltip("Names the StoryState flags (thought.<id>, thought.<id>.known).")]
        public string id = "palelament";
        [Tooltip("How close, from the edge of its models.")]
        public float radius = 8f;
        public bool swimmingOnly = true;
        [TextArea] public string[] lines =
        {
            "Forsaken colours. A ship of dead men, sunk on the reef...",
            "Even they couldn't get past this place.",
        };
        [Tooltip("The StoryState flag that means he knows what it is. Empty: only Lines.")]
        public string known = CompassQuest.Started;
        [TextArea] public string[] knownLines =
        {
            "Forsaken colours... the Pale Lament. The ship Sprocket chartered.",
            "If his needle's anywhere, it's in her binnacle.",
        };

        WowCharacterController _player;
        Bounds _bounds;
        float _nextCheck;

        void Start()
        {
            _player = FindAnyObjectByType<WowCharacterController>();
            var renderers = GetComponentsInChildren<Renderer>();
            _bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(transform.position, Vector3.one);
            foreach (var renderer in renderers)
                _bounds.Encapsulate(renderer.bounds);
        }

        void Update()
        {
            if (_player == null || TitleScreen.Showing || Time.time < _nextCheck)
                return;
            _nextCheck = Time.time + 0.5f;
            if (swimmingOnly && !_player.IsSwimming)
                return;
            if (_bounds.SqrDistance(_player.transform.position) > radius * radius)
                return;

            bool knows = !string.IsNullOrEmpty(known) && StoryState.Has(known) && knownLines.Length > 0;
            if (knows)
                Thoughts.Once(id + ".known", knownLines);
            else
                Thoughts.Once(id, lines);
            if (knows || string.IsNullOrEmpty(known))
                enabled = false;
        }
    }
}
