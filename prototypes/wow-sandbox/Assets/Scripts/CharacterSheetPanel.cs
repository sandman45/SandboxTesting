using UnityEngine;
using UnityEngine.UI;

namespace WowSandbox
{
    /// <summary>
    /// D&D-style character sheet, toggled with C: name, level and class, experience toward
    /// the next level, the six ability scores (modifier large, score small, as on a paper
    /// sheet), and the combat numbers that derive from them.
    /// </summary>
    [RequireComponent(typeof(CharacterStats))]
    public class CharacterSheetPanel : HudPanel
    {
        const float Width = 440f;
        const float Pad = 18f;
        const float ColumnWidth = (Width - Pad * 3f) / 2f;

        static readonly (Ability ability, string label)[] Abilities =
        {
            (Ability.Strength, "STR"),
            (Ability.Dexterity, "DEX"),
            (Ability.Constitution, "CON"),
            (Ability.Intelligence, "INT"),
            (Ability.Wisdom, "WIS"),
            (Ability.Charisma, "CHA"),
        };

        CharacterStats _stats;
        HealthController _health;

        Text _name;
        Text _subtitle;
        HudBar _experienceBar;
        readonly Text[] _modifiers = new Text[6];
        readonly Text[] _scores = new Text[6];

        Text _armorClass, _hitPoints, _proficiency, _initiative;
        Text _attack, _damage, _hitDice, _perception;

        protected override Vector2 PanelSize => new(Width, 352f);

        protected override void Awake()
        {
            _stats = GetComponent<CharacterStats>();
            _health = GetComponent<HealthController>();
            base.Awake();
        }

        protected override void Build()
        {
            var topLeft = new Vector2(0f, 1f);
            float inner = Width - Pad * 2f;

            _name = Hud.Label("Name", Root, topLeft, new Vector2(Pad, -14f), new Vector2(inner - 30f, 26f),
                22, TextAnchor.MiddleLeft, HudTheme.Text, FontStyle.Bold);
            _subtitle = Hud.Label("Subtitle", Root, topLeft, new Vector2(Pad, -40f), new Vector2(inner, 20f),
                15, TextAnchor.MiddleLeft, HudTheme.TextDim);

            _experienceBar = new HudBar("Experience", Root, topLeft, new Vector2(Pad, -66f),
                new Vector2(inner, 16f), labelSize: 12);
            _experienceBar.FillColor = HudTheme.Experience;

            Heading("Abilities", -96f);

            const float boxWidth = 62f;
            float gap = (inner - boxWidth * 6f) / 5f;
            for (int i = 0; i < Abilities.Length; i++)
            {
                var box = Hud.Rect(Abilities[i].label, Root, topLeft,
                    new Vector2(Pad + i * (boxWidth + gap), -118f), new Vector2(boxWidth, 74f));
                Hud.Rounded(box.gameObject, HudTheme.SubPanel, 6f);

                Hud.Label("Label", box, topLeft, new Vector2(0f, -4f), new Vector2(boxWidth, 16f),
                    12, TextAnchor.MiddleCenter, HudTheme.Heading, FontStyle.Bold).text = Abilities[i].label;

                _modifiers[i] = Hud.Label("Modifier", box, topLeft, new Vector2(0f, -20f), new Vector2(boxWidth, 30f),
                    24, TextAnchor.MiddleCenter, HudTheme.Text, FontStyle.Bold);
                _scores[i] = Hud.Label("Score", box, topLeft, new Vector2(0f, -50f), new Vector2(boxWidth, 20f),
                    14, TextAnchor.MiddleCenter, HudTheme.TextDim);
            }

            Heading("Combat", -208f);

            _armorClass = Row(0, 0, "Armor Class");
            _hitPoints = Row(0, 1, "Hit Points");
            _proficiency = Row(0, 2, "Proficiency");
            _initiative = Row(0, 3, "Initiative");
            _attack = Row(1, 0, "Attack Bonus");
            _damage = Row(1, 1, "Damage");
            _hitDice = Row(1, 2, "Hit Dice");
            _perception = Row(1, 3, "Passive Perception");
        }

        void Heading(string title, float y)
        {
            var text = Hud.Label(title, Root, new Vector2(0f, 1f), new Vector2(Pad, y), new Vector2(Width - Pad * 2f, 18f),
                14, TextAnchor.MiddleLeft, HudTheme.Heading, FontStyle.Bold);
            text.text = title.ToUpperInvariant();
        }

        /// <summary>A label-left, value-right line in one of the two combat columns; returns the value.</summary>
        Text Row(int column, int row, string label)
        {
            var position = new Vector2(Pad + column * (ColumnWidth + Pad), -232f - row * 26f);
            var size = new Vector2(ColumnWidth, 22f);
            var line = Hud.Rect(label, Root, new Vector2(0f, 1f), position, size);
            Hud.Rounded(line.gameObject, HudTheme.SubPanel, 4f);

            var name = Hud.Label("Label", line, 14, TextAnchor.MiddleLeft, HudTheme.TextDim);
            name.rectTransform.offsetMin = new Vector2(8f, 0f);
            name.text = label;

            var value = Hud.Label("Value", line, 15, TextAnchor.MiddleRight, HudTheme.Text, FontStyle.Bold);
            value.rectTransform.offsetMax = new Vector2(-8f, 0f);
            return value;
        }

        protected override void Refresh()
        {
            _name.text = _stats.characterName;
            _subtitle.text = $"Level {_stats.level} {_stats.className}";

            _experienceBar.Set(_stats.LevelProgress01);
            _experienceBar.Label.text = _stats.level >= CharacterStats.MaxLevel
                ? $"{_stats.experience} XP (max level)"
                : $"{_stats.experience} / {_stats.NextLevelExperience} XP";

            for (int i = 0; i < Abilities.Length; i++)
            {
                _modifiers[i].text = Signed(_stats.Modifier(Abilities[i].ability));
                _scores[i].text = _stats.Score(Abilities[i].ability).ToString();
            }

            _armorClass.text = _stats.ArmorClass.ToString();
            _hitPoints.text = _health != null
                ? $"{Mathf.CeilToInt(_health.Current)} / {Mathf.CeilToInt(_health.maxHealth)}"
                : _stats.MaxHitPoints.ToString();
            _proficiency.text = Signed(_stats.ProficiencyBonus);
            _initiative.text = Signed(_stats.Initiative);
            _attack.text = Signed(_stats.AttackBonus);
            _damage.text = _stats.DamageDice;
            _hitDice.text = $"{_stats.level}d{_stats.hitDie}";
            _perception.text = _stats.PassivePerception.ToString();
        }

        static string Signed(int value) => value >= 0 ? $"+{value}" : value.ToString();
    }
}
