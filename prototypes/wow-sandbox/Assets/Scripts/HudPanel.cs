using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace WowSandbox
{
    /// <summary>
    /// Base for the toggleable windows — character sheet now, inventory and map later. Each
    /// opens and closes on its own key, Esc closes whichever was opened last, and opening
    /// one brings it to the front. The panel's backing catches the mouse, so clicks on it
    /// don't fall through to targeting or the camera (they check Hud.PointerOverUi).
    ///
    /// Subclasses build their contents into Root in Build, and refresh them in Refresh,
    /// which only runs while the panel is open.
    /// </summary>
    public abstract class HudPanel : MonoBehaviour
    {
        public Key toggleKey = Key.C;
        [Tooltip("A second key that also toggles it. None for no second key.")]
        public Key altToggleKey = Key.None;

        static readonly List<HudPanel> _open = new();
        static int _escapeHandledFrame = -1;

        /// <summary>True while any panel is open.</summary>
        public static bool AnyOpen => _open.Count > 0;

        public bool IsOpen { get; private set; }

        protected RectTransform Root { get; private set; }

        protected virtual void Awake()
        {
            Root = Hud.Rect(GetType().Name, Hud.Layer(HudLayer.Panels), PanelAnchor, PanelPosition, PanelSize);
            Hud.Panel(Root);
            Root.GetComponent<Image>().raycastTarget = true;
            Hud.CloseButton(Root, Close);
            Build();
            Root.gameObject.SetActive(false);
        }

        protected abstract Vector2 PanelSize { get; }
        protected virtual Vector2 PanelAnchor => new(0f, 0.5f);
        protected virtual Vector2 PanelPosition => new(40f, 40f);

        protected abstract void Build();
        protected abstract void Refresh();

        protected virtual void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                // Key.None isn't a real key — indexing the keyboard with it throws.
                if ((toggleKey != Key.None && keyboard[toggleKey].wasPressedThisFrame) ||
                    (altToggleKey != Key.None && keyboard[altToggleKey].wasPressedThisFrame))
                {
                    Toggle();
                }
                else if (IsOpen && keyboard.escapeKey.wasPressedThisFrame &&
                         _escapeHandledFrame != Time.frameCount && _open[^1] == this)
                {
                    // One Esc closes one panel, not the whole stack as each Update sees itself on top.
                    _escapeHandledFrame = Time.frameCount;
                    Close();
                }
            }

            if (IsOpen)
                Refresh();
        }

        public void Toggle()
        {
            if (IsOpen)
                Close();
            else
                Open();
        }

        public void Open()
        {
            if (IsOpen)
                return;
            IsOpen = true;
            _open.Add(this);
            Root.gameObject.SetActive(true);
            Root.SetAsLastSibling();
            Refresh();
        }

        public void Close()
        {
            if (!IsOpen)
                return;
            IsOpen = false;
            _open.Remove(this);
            // The pointer may be resting on one of this panel's slots, which won't get its
            // exit event once hidden.
            HudTooltip.Hide();
            if (Root != null)
                Root.gameObject.SetActive(false);
        }

        protected virtual void OnDisable() => Close();
    }
}
