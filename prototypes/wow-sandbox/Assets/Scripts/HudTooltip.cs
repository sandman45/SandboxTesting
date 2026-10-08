using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace WowSandbox
{
    /// <summary>
    /// The item tooltip: name in its rarity colour, rarity and slot, then what the item
    /// does — damage or armour class by 5e's rules, magic bonuses, ability bonuses, any
    /// strength requirement (red if you don't meet it), and its value. Follows the mouse
    /// and flips to stay on screen. One shared instance, created on first use.
    /// </summary>
    public class HudTooltip : MonoBehaviour
    {
        const float Width = 280f;
        const float Pad = 12f;

        static HudTooltip _instance;

        RectTransform _rect;
        RectTransform _layer;
        Text _text;

        public static void Show(Item item, CharacterStats reader, string hint = null)
        {
            if (item == null)
            {
                Hide();
                return;
            }

            var tooltip = Instance();
            tooltip._text.text = Describe(item, reader, hint);
            tooltip._rect.sizeDelta = new Vector2(Width, tooltip._text.preferredHeight + Pad * 2f);
            tooltip.gameObject.SetActive(true);
            tooltip._rect.SetAsLastSibling();
            tooltip.Follow();
        }

        public static void Hide()
        {
            if (_instance != null)
                _instance.gameObject.SetActive(false);
        }

        static HudTooltip Instance()
        {
            if (_instance != null)
                return _instance;

            var layer = Hud.Layer(HudLayer.Panels);
            var rect = Hud.Rect("Tooltip", layer, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(Width, 100f));
            rect.pivot = new Vector2(0f, 1f);
            Hud.Panel(rect);

            var text = Hud.Label("Text", rect, 14, TextAnchor.UpperLeft, HudTheme.Text);
            text.rectTransform.offsetMin = new Vector2(Pad, Pad);
            text.rectTransform.offsetMax = new Vector2(-Pad, -Pad);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.supportRichText = true;
            text.lineSpacing = 1.1f;

            _instance = rect.gameObject.AddComponent<HudTooltip>();
            _instance._rect = rect;
            _instance._layer = layer;
            _instance._text = text;
            return _instance;
        }

        void LateUpdate() => Follow();

        void Follow()
        {
            var mouse = Mouse.current;
            if (mouse == null)
                return;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(_layer, mouse.position.ReadValue(), null, out var local);
            var bounds = _layer.rect;
            var size = _rect.sizeDelta;

            float x = local.x + 18f;
            if (x + size.x > bounds.xMax)
                x = local.x - 18f - size.x;
            float y = local.y - 18f;
            if (y - size.y < bounds.yMin)
                y = bounds.yMin + size.y;

            _rect.anchoredPosition = new Vector2(x, y);
        }

        static string Describe(Item item, CharacterStats reader, string hint)
        {
            var lines = new List<string>();
            string rarity = HudTheme.Hex(HudTheme.RarityColor(item.rarity));
            string dim = HudTheme.Hex(HudTheme.TextDim);
            string good = HudTheme.Hex(HudTheme.Friendly);
            string bad = HudTheme.Hex(HudTheme.Hostile);

            lines.Add($"<size=17><b><color=#{rarity}>{item.name}</color></b></size>");
            lines.Add($"<color=#{rarity}>{HudTheme.RarityName(item.rarity)}</color><color=#{dim}>  ·  {KindLine(item)}</color>");

            switch (item.kind)
            {
                case ItemKind.Weapon:
                    lines.Add($"Damage: {Dice.Format(item.damageDieCount, item.damageDieSides, item.magicBonus)} + STR");
                    if (item.magicBonus > 0)
                        lines.Add($"<color=#{good}>+{item.magicBonus} to attack rolls</color>");
                    break;

                case ItemKind.Armor:
                    int ac = item.armorClass + item.magicBonus;
                    lines.Add(item.armorCategory switch
                    {
                        ArmorCategory.Light => $"Armor Class: {ac} + DEX",
                        ArmorCategory.Medium => $"Armor Class: {ac} + DEX (max 2)",
                        _ => $"Armor Class: {ac}",
                    });
                    if (item.strengthRequirement > 0)
                    {
                        bool weak = reader != null && reader.Score(Ability.Strength) < item.strengthRequirement;
                        lines.Add(weak
                            ? $"<color=#{bad}>Requires Strength {item.strengthRequirement} — you'll be slowed</color>"
                            : $"Requires Strength {item.strengthRequirement}");
                    }
                    break;

                case ItemKind.Shield:
                    lines.Add($"Armor Class: +{item.armorClass + item.magicBonus}");
                    break;

                case ItemKind.TradeGood:
                    lines.Add($"<color=#{dim}>Trade good — worth selling</color>");
                    break;
            }

            if (item.protectionBonus > 0)
                lines.Add($"<color=#{good}>+{item.protectionBonus} Armor Class</color>");
            for (int i = 0; i < 6; i++)
            {
                if (item.abilityBonuses[i] != 0)
                    lines.Add($"<color=#{good}>+{item.abilityBonuses[i]} {(Ability)i}</color>");
            }

            if (item.count > 1)
                lines.Add($"<color=#{dim}>Stack of {item.count}</color>");
            lines.Add($"Value: {Money.Format(item.valueCopper * item.count, rich: true)}");
            if (!string.IsNullOrEmpty(hint))
                lines.Add($"<color=#{dim}><i>{hint}</i></color>");

            return string.Join("\n", lines);
        }

        static string KindLine(Item item) => item.kind switch
        {
            ItemKind.Weapon => item.twoHanded ? "Two-Handed Weapon" : "Main Hand Weapon",
            ItemKind.Armor => $"{item.armorCategory} Armor",
            ItemKind.Shield => "Off Hand Shield",
            ItemKind.TradeGood => "Trade Good",
            _ => item.slot.ToString(),
        };
    }
}
