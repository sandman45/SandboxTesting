using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.InputSystem;

namespace WowSandbox
{
    /// <summary>
    /// Left-click to target anything with a Health component — chickens, the thief,
    /// whatever else NpcSpawner places. Clicking empty ground clears the target, same as
    /// clicking off a unit in WoW; clicking yourself does too, since the player uses
    /// HealthController rather than Health and so never resolves as a hit here.
    ///
    /// A CapsuleCollider is what the raycast actually hits — NavMeshAgent alone gives an
    /// NPC no physics presence, so ChickenSetup and NpcSpawner both add one sized to match
    /// the agent.
    /// </summary>
    public class TargetingController : MonoBehaviour
    {
        [Tooltip("How far the click ray reaches.")]
        public float range = 100f;

        Camera _camera;
        Health _target;
        string _targetName;

        /// <summary>
        /// The currently targeted NPC, or null. Reads back as null on its own once the
        /// target dies — Unity's fake-null equality covers a destroyed Health same as a
        /// never-set one, no extra bookkeeping needed here.
        /// </summary>
        public Health Target => _target != null ? _target : null;

        /// <summary>Display name of the current target, or null if there isn't one.</summary>
        public string TargetName => Target != null ? _targetName : null;

        void Update()
        {
            if (_camera == null)
                _camera = Camera.main;

            var mouse = Mouse.current;
            if (_camera == null || mouse == null || !mouse.leftButton.wasPressedThisFrame)
                return;

            // A click on a HUD panel is for the panel, not the world behind it.
            if (Hud.PointerOverUi)
                return;

            Ray ray = _camera.ScreenPointToRay(mouse.position.ReadValue());
            if (Physics.Raycast(ray, out RaycastHit hit, range))
            {
                var health = hit.collider.GetComponentInParent<Health>();
                if (health != null)
                {
                    _target = health;
                    _targetName = CleanName(health.name);
                    return;
                }
            }

            _target = null;
            _targetName = null;
        }

        /// <summary>Strips the "_00" spawn index ChickenSetup/NpcSpawner name instances with.</summary>
        static string CleanName(string rawName) => Regex.Replace(rawName, @"_\d+$", "");
    }
}
