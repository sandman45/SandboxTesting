using UnityEngine;
using UnityEngine.InputSystem;

namespace WowSandbox
{
    /// <summary>
    /// The player's side of <see cref="Interactable"/>s: under the mouse, one shows its name and
    /// what a click does ("Treasure Chest — click to open"); a left click within its reach turns
    /// you to it and uses it, and from further away says "You are too far away." Lives on the
    /// player, added at Play by the first interactable. Clicks on the HUD, and anything while
    /// dead, locked (the helm), or on the title screen, are ignored.
    /// </summary>
    [DisallowMultipleComponent]
    public class Interactor : MonoBehaviour
    {
        [Tooltip("How far the mouse ray reaches.")]
        public float rayRange = 100f;

        Camera _camera;
        WowCharacterController _movement;
        HealthController _health;
        Interactable _hovered;

        /// <summary>Puts an Interactor on the player if there isn't one.</summary>
        public static void EnsureOnPlayer()
        {
            var player = FindAnyObjectByType<WowCharacterController>();
            if (player != null && player.GetComponent<Interactor>() == null)
                player.gameObject.AddComponent<Interactor>();
        }

        void Awake()
        {
            _movement = GetComponent<WowCharacterController>();
            _health = GetComponent<HealthController>();
        }

        void Update()
        {
            if (_camera == null)
                _camera = Camera.main;
            var mouse = Mouse.current;
            bool blocked = _camera == null || mouse == null || Hud.PointerOverUi || TitleScreen.Showing ||
                           (_movement != null && _movement.IsLocked) || (_health != null && _health.IsDead);

            var under = blocked ? null : Under(mouse.position.ReadValue());
            if (under != _hovered)
            {
                if (_hovered != null)
                    HudTooltip.Hide();
                _hovered = under;
            }
            if (_hovered != null)
                HudTooltip.Show($"<size=16><b>{_hovered.displayName}</b></size>\n" +
                                $"<color=#{HudTheme.Hex(HudTheme.TextDim)}>Click to {_hovered.Verb.ToLowerInvariant()}</color>");

            if (_hovered != null && mouse.leftButton.wasPressedThisFrame)
                Use(_hovered);
        }

        Interactable Under(Vector2 screen)
        {
            var ray = _camera.ScreenPointToRay(screen);
            // Ground items have trigger colliders, so triggers count here.
            if (!Physics.Raycast(ray, out var hit, rayRange, ~0, QueryTriggerInteraction.Collide))
                return null;
            var interactable = hit.collider.GetComponentInParent<Interactable>();
            return interactable != null && interactable.isActiveAndEnabled && interactable.CanInteract ? interactable : null;
        }

        void Use(Interactable interactable)
        {
            if (interactable.DistanceFrom(transform.position) > interactable.range)
            {
                Hud.Error("You are too far away.");
                return;
            }
            var toward = interactable.transform.position - transform.position;
            toward.y = 0f;
            if (toward.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.LookRotation(toward, Vector3.up);
            HudTooltip.Hide();
            _hovered = null;
            interactable.Interact(gameObject);
        }

        void OnDisable()
        {
            if (_hovered != null)
                HudTooltip.Hide();
            _hovered = null;
        }
    }
}
