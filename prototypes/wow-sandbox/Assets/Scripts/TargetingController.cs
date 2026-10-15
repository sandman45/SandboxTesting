using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.InputSystem;

namespace WowSandbox
{
    /// <summary>
    /// Tab targets the nearest enemy in view, and each press after moves on to the next
    /// nearest, as in WoW. Left-click to target anything with a Health component — chickens, the thief,
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
        [Tooltip("How far Tab looks for enemies.")]
        public float tabRange = 40f;
        [Tooltip("Tab also cycles through neutral creatures (chickens), after the hostile ones.")]
        public bool tabIncludesNeutral = false;

        Camera _camera;
        Health _target;
        string _targetName;

        /// <summary>
        /// The currently targeted NPC, or null. Reads back as null on its own once the
        /// target dies — Unity's fake-null equality covers a destroyed Health same as a
        /// never-set one, no extra bookkeeping needed here.
        /// </summary>
        public Health Target => _target != null ? _target : null;

        /// <summary>
        /// Raised after a click selects something, dead or alive — LootPanel listens for
        /// clicks on corpses.
        /// </summary>
        public event Action<Health> Clicked;

        /// <summary>Display name of the current target, or null if there isn't one.</summary>
        public string TargetName => Target != null ? _targetName : null;

        void Update()
        {
            if (_camera == null)
                _camera = Camera.main;

            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.tabKey.wasPressedThisFrame && !TitleScreen.Showing)
                TabTarget();

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
                    Clicked?.Invoke(health);
                    return;
                }
            }

            _target = null;
            _targetName = null;
        }

        /// <summary>
        /// The next enemy for Tab: living, not friendly, within tabRange and on screen, nearest
        /// first. With an enemy already targeted it moves on to the next one further out, and
        /// wraps around to the nearest after the last.
        /// </summary>
        void TabTarget()
        {
            var candidates = new List<(Health health, float distance)>();
            foreach (var health in FindObjectsByType<Health>())
            {
                if (health == null || health.IsDead || health.reaction == Reaction.Friendly)
                    continue;
                if (health.reaction == Reaction.Neutral && !tabIncludesNeutral)
                    continue;
                float distance = Vector3.Distance(transform.position, health.transform.position);
                if (distance > tabRange || !OnScreen(health.transform.position + Vector3.up))
                    continue;
                candidates.Add((health, distance));
            }
            if (candidates.Count == 0)
            {
                Hud.Error("No enemies nearby.");
                return;
            }

            candidates.Sort((a, b) => a.distance.CompareTo(b.distance));
            int current = candidates.FindIndex(c => c.health == Target);
            var next = candidates[(current + 1) % candidates.Count].health;
            _target = next;
            _targetName = CleanName(next.name);
        }

        bool OnScreen(Vector3 point)
        {
            if (_camera == null)
                return true;
            var view = _camera.WorldToViewportPoint(point);
            return view.z > 0f && view.x >= 0f && view.x <= 1f && view.y >= 0f && view.y <= 1f;
        }

        /// <summary>Strips the "_00" spawn index ChickenSetup/NpcSpawner name instances with.</summary>
        static string CleanName(string rawName) => Regex.Replace(rawName, @"_\d+$", "");
    }
}
