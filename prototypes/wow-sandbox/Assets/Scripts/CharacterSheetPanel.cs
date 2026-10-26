using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace WowSandbox
{
    /// <summary>
    /// D&D-style character sheet, toggled with C: name, level and class, experience toward
    /// the next level, the six ability scores (modifier large, score small, as on a paper
    /// sheet), the combat numbers that derive from them, and — with an Inventory — WoW's
    /// paper doll: his model in the middle (drag to turn him), the equipment slots down
    /// both sides and the weapons under it. Scores raised by gear show green. Drag items
    /// from the bags onto a slot to wear them; right-click a worn item to take it off.
    /// </summary>
    [RequireComponent(typeof(CharacterStats))]
    public class CharacterSheetPanel : HudPanel
    {
        const float Pad = 18f;
        const float Top = -96f;

        // WoW's paper doll: down the left, down the right, and the weapons along the bottom.
        static readonly (EquipSlot slot, string label)[] LeftSlots =
        {
            (EquipSlot.Head, "Head"),
            (EquipSlot.Neck, "Neck"),
            (EquipSlot.Shoulders, "Shldr"),
            (EquipSlot.Back, "Back"),
            (EquipSlot.Chest, "Chest"),
        };
        static readonly (EquipSlot slot, string label)[] RightSlots =
        {
            (EquipSlot.Hands, "Hands"),
            (EquipSlot.Legs, "Legs"),
            (EquipSlot.Feet, "Feet"),
            (EquipSlot.Ring, "Ring"),
        };
        static readonly (EquipSlot slot, string label)[] WeaponSlots =
        {
            (EquipSlot.MainHand, "Main"),
            (EquipSlot.OffHand, "Off"),
        };

        const float SlotSize = 46f;
        const float SlotGap = 8f;
        const float ModelGap = 10f;
        const float ModelWidth = 180f;
        // As tall as the left column of slots.
        const float ModelHeight = 5 * SlotSize + 4 * SlotGap;
        const float DollWidth = SlotSize * 2f + ModelGap * 2f + ModelWidth;
        const float DollBottom = Top - ModelHeight - ModelGap - SlotSize;

        // The stats, to the right of the doll (or alone, with no Inventory).
        const float StatsWidth = 360f;
        const float ColumnGap = 12f;
        const float ColumnWidth = (StatsWidth - ColumnGap) / 2f;
        float StatsX => _inventory != null ? Pad + DollWidth + Pad : Pad;
        float Width => StatsX + StatsWidth + Pad;

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
        Inventory _inventory;
        ItemSlotView[] _equipment;
        CharacterPreview _preview;
        bool _previewShown;

        Text _name;
        Text _subtitle;
        HudBar _experienceBar;
        readonly Text[] _modifiers = new Text[6];
        readonly Text[] _scores = new Text[6];

        Text _armorClass, _hitPoints, _proficiency, _initiative;
        Text _attack, _damage, _hitDice, _perception;
        Text _speed, _armor;

        protected override Vector2 PanelSize => new(Width, _inventory != null ? -DollBottom + Pad : 384f);

        protected override void Awake()
        {
            _stats = GetComponent<CharacterStats>();
            _health = GetComponent<HealthController>();
            _inventory = GetComponent<Inventory>();
            base.Awake();
        }

        void OnEnable()
        {
            if (_inventory != null)
                _inventory.Changed += OnInventoryChanged;
        }

        protected override void OnDisable()
        {
            if (_inventory != null)
                _inventory.Changed -= OnInventoryChanged;
            base.OnDisable();
        }

        void OnDestroy() => _preview?.Dispose();

        // His gear may have changed: the doll dresses again.
        void OnInventoryChanged()
        {
            if (_previewShown)
                _preview.MarkDirty();
        }

        protected override void Closed()
        {
            _preview?.Hide();
            _previewShown = false;
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

            Heading("Abilities", Top);

            const float boxWidth = 54f;
            float gap = (StatsWidth - boxWidth * 6f) / 5f;
            for (int i = 0; i < Abilities.Length; i++)
            {
                var box = Hud.Rect(Abilities[i].label, Root, topLeft,
                    new Vector2(StatsX + i * (boxWidth + gap), Top - 22f), new Vector2(boxWidth, 74f));
                Hud.Rounded(box.gameObject, HudTheme.SubPanel, 6f);

                Hud.Label("Label", box, topLeft, new Vector2(0f, -4f), new Vector2(boxWidth, 16f),
                    12, TextAnchor.MiddleCenter, HudTheme.Heading, FontStyle.Bold).text = Abilities[i].label;

                _modifiers[i] = Hud.Label("Modifier", box, topLeft, new Vector2(0f, -20f), new Vector2(boxWidth, 30f),
                    24, TextAnchor.MiddleCenter, HudTheme.Text, FontStyle.Bold);
                _scores[i] = Hud.Label("Score", box, topLeft, new Vector2(0f, -50f), new Vector2(boxWidth, 20f),
                    14, TextAnchor.MiddleCenter, HudTheme.TextDim);
            }

            Heading("Combat", Top - 112f);

            _armorClass = Row(0, 0, "Armor Class");
            _hitPoints = Row(0, 1, "Hit Points");
            _proficiency = Row(0, 2, "Proficiency");
            _initiative = Row(0, 3, "Initiative");
            _attack = Row(1, 0, "Attack Bonus");
            _damage = Row(1, 1, "Damage");
            _hitDice = Row(1, 2, "Hit Dice");
            _perception = Row(1, 3, "Passive Perception");
            _speed = Row(0, 4, "Speed");
            _armor = Row(1, 4, "Armor");

            if (_inventory != null)
                BuildEquipment();
        }

        void BuildEquipment()
        {
            const float modelX = Pad + SlotSize + ModelGap;
            const float rightX = modelX + ModelWidth + ModelGap;
            const float weaponsY = Top - ModelHeight - ModelGap;
            const float weaponsX = modelX + (ModelWidth - SlotSize * 2f - ModelGap) / 2f;
            var slots = LeftSlots.Select((entry, i) => (entry.slot, entry.label, position: new Vector2(Pad, Top - i * (SlotSize + SlotGap))))
                .Concat(RightSlots.Select((entry, i) => (entry.slot, entry.label, position: new Vector2(rightX, Top - i * (SlotSize + SlotGap)))))
                .Concat(WeaponSlots.Select((entry, i) => (entry.slot, entry.label, position: new Vector2(weaponsX + i * (SlotSize + ModelGap), weaponsY))))
                .ToArray();
            _equipment = new ItemSlotView[slots.Length];

            // Him, between the two columns.
            var frame = Hud.Rect("Model", Root, new Vector2(0f, 1f), new Vector2(modelX, Top), new Vector2(ModelWidth, ModelHeight));
            Hud.Rounded(frame.gameObject, HudTheme.SubPanel, 6f);
            var picture = Hud.Stretch("Picture", frame, 2f).gameObject.AddComponent<RawImage>();
            _preview = new CharacterPreview(picture, new Vector2(ModelWidth, ModelHeight),
                GetComponent<WowCharacterController>(), GetComponent<EquipmentVisuals>());

            for (int i = 0; i < slots.Length; i++)
            {
                var (slot, label, position) = slots[i];
                var view = ItemSlotView.Create(label, Root, new Vector2(0f, 1f), position, SlotSize, label);
                view.Source = () => _inventory.Equipped(slot);
                view.Ref = SlotRef.Equip(slot);
                view.Reader = _stats;
                view.TooltipHint = "Right-click to unequip";
                view.RightClicked = _ =>
                {
                    if (!_inventory.Unequip(slot, out var error) && error != null)
                        Hud.Error(error);
                };
                view.DroppedOn = (from, to) =>
                {
                    if (from.Ref.Area == SlotArea.Loot)
                        Hud.Error("Loot it into your bags first.");
                    else if (!_inventory.Move(from.Ref, to.Ref, out var error) && error != null)
                        Hud.Error(error);
                };
                _equipment[i] = view;
            }
        }

        void Heading(string title, float y)
        {
            var text = Hud.Label(title, Root, new Vector2(0f, 1f), new Vector2(StatsX, y), new Vector2(StatsWidth, 18f),
                14, TextAnchor.MiddleLeft, HudTheme.Heading, FontStyle.Bold);
            text.text = title.ToUpperInvariant();
        }

        /// <summary>A label-left, value-right line in one of the two combat columns; returns the value.</summary>
        Text Row(int column, int row, string label)
        {
            var position = new Vector2(StatsX + column * (ColumnWidth + ColumnGap), Top - 136f - row * 26f);
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
                var ability = Abilities[i].ability;
                int score = _stats.Score(ability);
                int gained = score - _stats.BaseScore(ability);
                int modifier = _stats.Modifier(ability);
                // Gear adds to the score; the modifier only moves every 2 points, so it's green
                // only when the gear actually raised it, and the score shows what gear added.
                bool modifierRaised = modifier > CharacterStats.ModifierFor(_stats.BaseScore(ability));
                _modifiers[i].text = Signed(modifier);
                _modifiers[i].color = modifierRaised ? HudTheme.Friendly : HudTheme.Text;
                _scores[i].text = gained > 0 ? $"{score} (+{gained})" : score.ToString();
                _scores[i].color = gained > 0 ? HudTheme.Friendly : HudTheme.TextDim;
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
            _speed.text = $"{_stats.SpeedFeet} ft";
            _speed.color = _stats.ArmorTooHeavy ? HudTheme.Hostile : HudTheme.Text;
            _armor.text = _stats.ArmorWorn == ArmorCategory.None ? "None" : _stats.ArmorWorn.ToString();

            if (_equipment != null)
            {
                foreach (var view in _equipment)
                    view.Refresh();
            }

            // Shown from the first refresh after opening (Open refreshes straight away).
            if (_preview != null)
            {
                if (!_previewShown)
                {
                    _preview.Show();
                    _previewShown = true;
                }
                _preview.Tick();
            }
        }

        static string Signed(int value) => value >= 0 ? $"+{value}" : value.ToString();
    }
}
