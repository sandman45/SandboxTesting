using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace WowSandbox
{
    /// <summary>
    /// One item square — a bag slot, an equipment slot or a loot row's icon. Draws the
    /// placeholder icon (a tinted tile with the item's letters, framed in its rarity
    /// colour, stack count in the corner), shows the tooltip on hover, and reports clicks
    /// and drag-and-drop to whichever panel owns it; the panel decides what they mean.
    /// </summary>
    public class ItemSlotView : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler,
        IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
    {
        static readonly Color EmptyBorder = new(1f, 1f, 1f, 0.12f);
        static readonly Color SlotBack = new(0f, 0f, 0f, 0.45f);
        static readonly Color SlotHover = new(0.25f, 0.22f, 0.15f, 0.6f);

        /// <summary>The item to show, read fresh on every Refresh.</summary>
        public Func<Item> Source;
        public SlotRef Ref;
        /// <summary>Whose stats the tooltip checks requirements against.</summary>
        public CharacterStats Reader;
        public string TooltipHint;

        public Action<ItemSlotView> LeftClicked;
        public Action<ItemSlotView> RightClicked;
        /// <summary>Something was dropped here: (where it came from, this slot).</summary>
        public Action<ItemSlotView, ItemSlotView> DroppedOn;

        Image _back;
        Image _tile;
        Image _border;
        Text _glyph;
        Text _count;
        Text _emptyLabel;
        bool _hovered;

        static ItemSlotView _dragging;
        static RectTransform _ghost;
        static Image _ghostTile;
        static Text _ghostGlyph;

        public Item Item => Source?.Invoke();

        public static ItemSlotView Create(string name, Transform parent, Vector2 anchor, Vector2 position, float size,
            string emptyLabel = null)
        {
            var rect = Hud.Rect(name, parent, anchor, position, new Vector2(size, size));
            var view = rect.gameObject.AddComponent<ItemSlotView>();

            view._back = Hud.Rounded(rect.gameObject, SlotBack, 7f);
            view._back.raycastTarget = true; // what catches hover, clicks and drops

            view._tile = Hud.Rounded(Hud.Stretch("Icon", rect, 3f).gameObject, Color.white, 5f);
            view._glyph = Hud.Label("Glyph", view._tile.transform, Mathf.RoundToInt(size * 0.36f),
                TextAnchor.MiddleCenter, HudTheme.Text, FontStyle.Bold);

            view._border = Hud.Ring(Hud.Stretch("Border", rect).gameObject, EmptyBorder, 7f);

            view._count = Hud.Label("Count", rect, 12, TextAnchor.LowerRight, HudTheme.Text, FontStyle.Bold);
            view._count.rectTransform.offsetMax = new Vector2(-4f, 0f);
            view._count.rectTransform.offsetMin = new Vector2(0f, 2f);

            view._emptyLabel = Hud.Label("Empty", rect, 10, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.3f));
            view._emptyLabel.text = emptyLabel ?? "";
            return view;
        }

        /// <summary>Re-reads the item and redraws. Owners call this from their panel's Refresh.</summary>
        public void Refresh()
        {
            var item = Item;
            bool has = item != null;

            _tile.enabled = has;
            _glyph.text = has ? item.glyph : "";
            _count.text = has && item.count > 1 ? item.count.ToString() : "";
            _emptyLabel.enabled = !has;
            _back.color = _hovered ? SlotHover : SlotBack;

            if (has)
            {
                _tile.color = item.iconColor;
                var rarity = HudTheme.RarityColor(item.rarity);
                _border.color = item.rarity == ItemRarity.Common ? new Color(rarity.r, rarity.g, rarity.b, 0.35f) : rarity;
            }
            else
            {
                _border.color = EmptyBorder;
            }

            // Dimmed while it's the one being dragged, so it reads as "lifted out".
            float alpha = _dragging == this ? 0.35f : 1f;
            _tile.canvasRenderer.SetAlpha(alpha);
            _glyph.canvasRenderer.SetAlpha(alpha);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _hovered = true;
            if (_dragging == null)
                HudTooltip.Show(Item, Reader, TooltipHint);
            Refresh();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _hovered = false;
            HudTooltip.Hide();
            Refresh();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.dragging || Item == null)
                return;

            if (eventData.button == PointerEventData.InputButton.Right)
                RightClicked?.Invoke(this);
            else if (eventData.button == PointerEventData.InputButton.Left)
                LeftClicked?.Invoke(this);

            // The slot may now hold something else (or nothing) — update the tooltip to match.
            if (_hovered)
                HudTooltip.Show(Item, Reader, TooltipHint);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            var item = Item;
            if (item == null || eventData.button != PointerEventData.InputButton.Left)
                return;

            _dragging = this;
            HudTooltip.Hide();
            var ghost = Ghost();
            _ghostTile.color = item.iconColor;
            _ghostGlyph.text = item.glyph;
            ghost.gameObject.SetActive(true);
            ghost.SetAsLastSibling();
            MoveGhost(eventData);
            Refresh();
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_dragging == this)
                MoveGhost(eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (_dragging != this)
                return;
            _dragging = null;
            if (_ghost != null)
                _ghost.gameObject.SetActive(false);
            Refresh();
        }

        public void OnDrop(PointerEventData eventData)
        {
            var source = _dragging;
            if (source == null || source == this)
                return;
            DroppedOn?.Invoke(source, this);
            source.Refresh();
            Refresh();
        }

        static void MoveGhost(PointerEventData eventData)
        {
            var layer = (RectTransform)_ghost.parent;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(layer, eventData.position, null, out var local);
            _ghost.anchoredPosition = local;
        }

        static RectTransform Ghost()
        {
            if (_ghost != null)
                return _ghost;

            _ghost = Hud.Rect("DragGhost", Hud.Layer(HudLayer.Panels), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(46f, 46f));
            _ghostTile = Hud.Rounded(_ghost.gameObject, Color.white, 5f);
            _ghostGlyph = Hud.Label("Glyph", _ghost, 17, TextAnchor.MiddleCenter, HudTheme.Text, FontStyle.Bold);
            var group = _ghost.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false; // or it would sit under the mouse and swallow the drop
            group.alpha = 0.85f;
            return _ghost;
        }
    }
}
