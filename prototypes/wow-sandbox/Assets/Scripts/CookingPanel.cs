using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace WowSandbox
{
    /// <summary>
    /// Cooking at a campfire: clicking a lit one opens this, a row per recipe — what it makes,
    /// what it takes and how many he has — with Cook (one) and Cook All. Each takes CookSeconds
    /// on the cast bar with the skillet over the fire (Campfire.Cooking) and him knelt beside it
    /// (the looting pose); walking off, the fire going out or a blow stops it. What's cooked goes
    /// in the bags, the raw food comes out of them.
    ///
    /// Added to the player the first time a fire is clicked.
    /// </summary>
    [RequireComponent(typeof(Inventory))]
    public class CookingPanel : HudPanel
    {
        /// <summary>What can be cooked: raw food in, a meal (ItemCatalog.Meals) out.</summary>
        public static readonly (string raw, string meal)[] Recipes =
        {
            ("Raw Fish", "Grilled Fish"),
            ("Raw Chicken", "Roast Chicken"),
        };

        const float CookSeconds = 2.5f;
        const float Reach = 4.5f;
        const float Width = 420f;
        const float Pad = 14f;
        const float RowHeight = 54f;

        Inventory _inventory;
        CharacterStats _stats;
        WowCharacterController _movement;
        HealthController _health;
        Campfire _fire;

        Text _status;
        readonly Text[] _counts = new Text[Recipes.Length];
        readonly Button[] _cookOne = new Button[Recipes.Length];
        readonly Button[] _cookAll = new Button[Recipes.Length];

        // The batch on the go: which recipe, how many still to cook, and when this one started.
        int _recipe = -1;
        int _left;
        int _done;
        float _started;

        protected override Vector2 PanelSize => new(Width, 78f + Recipes.Length * RowHeight + 16f);
        protected override Vector2 PanelAnchor => new(0.5f, 0.5f);
        protected override Vector2 PanelPosition => new(-280f, 40f);

        bool Cooking => _recipe >= 0;

        /// <summary>The window for <paramref name="fire"/> — on the player clicking it, adding the window if it's new.</summary>
        public static void OpenAt(Campfire fire, GameObject player)
        {
            var panel = player.GetComponent<CookingPanel>();
            if (panel == null)
                panel = player.AddComponent<CookingPanel>();
            panel._fire = fire;
            panel.Face(fire.transform);
            panel.Open();
        }

        protected override void Awake()
        {
            toggleKey = Key.None;
            altToggleKey = Key.None;
            _inventory = GetComponent<Inventory>();
            _stats = GetComponent<CharacterStats>();
            _movement = GetComponent<WowCharacterController>();
            _health = GetComponent<HealthController>();
            base.Awake();
        }

        void OnEnable() => Combat.PlayerAttacked += OnAttacked;

        protected override void OnDisable()
        {
            Combat.PlayerAttacked -= OnAttacked;
            base.OnDisable();
        }

        void OnAttacked(HealthController target, AttackResult result)
        {
            if (result.Hit && Cooking)
                Stop(interrupted: true);
        }

        protected override void Build()
        {
            var topLeft = new Vector2(0f, 1f);
            Hud.Label("Title", Root, topLeft, new Vector2(Pad, -12f), new Vector2(Width - 60f, 24f),
                18, TextAnchor.MiddleLeft, HudTheme.Text, FontStyle.Bold).text = "Cooking";
            _status = Hud.Label("Status", Root, topLeft, new Vector2(Pad, -38f), new Vector2(Width - Pad * 2f, 20f),
                13, TextAnchor.MiddleLeft, HudTheme.TextDim);

            for (int i = 0; i < Recipes.Length; i++)
            {
                int index = i;
                var (raw, meal) = Recipes[i];
                var row = Hud.Rect($"Recipe{i}", Root, topLeft, new Vector2(Pad, -66f - i * RowHeight),
                    new Vector2(Width - Pad * 2f, RowHeight - 6f));
                Hud.Rounded(row.gameObject, HudTheme.SubPanel, 6f);

                var slot = ItemSlotView.Create("Icon", row, topLeft, Vector2.zero, RowHeight - 6f);
                var made = ItemCatalog.TryFind(meal, out var item) ? item : null;
                slot.Source = () => made;
                slot.Reader = _stats;

                // Up to just short of the Cook buttons, which take the row's last 130 px.
                float textWidth = Width - Pad * 2f - (RowHeight + 2f) - 146f;
                Hud.Label("Meal", row, topLeft, new Vector2(RowHeight + 2f, -4f), new Vector2(textWidth, 20f),
                    15, TextAnchor.MiddleLeft, HudTheme.Text, FontStyle.Bold).text = meal;
                _counts[i] = Hud.Label("Takes", row, topLeft, new Vector2(RowHeight + 2f, -24f), new Vector2(textWidth, 18f),
                    12, TextAnchor.MiddleLeft, HudTheme.TextDim);

                var right = new Vector2(1f, 0.5f);
                _cookOne[i] = Hud.TextButton("Cook", row, right, new Vector2(-72f, 0f), new Vector2(58f, 26f),
                    () => Begin(index, 1));
                _cookAll[i] = Hud.TextButton("Cook All", row, right, new Vector2(-8f, 0f), new Vector2(60f, 26f),
                    () => Begin(index, Have(Recipes[index].raw)));
                _cookAll[i].GetComponentInChildren<Text>().fontSize = 12;
            }
        }

        protected override void Refresh()
        {
            if (_fire == null || !_fire.IsLit || Distance() > Reach)
            {
                Close();
                return;
            }

            for (int i = 0; i < Recipes.Length; i++)
            {
                int have = Have(Recipes[i].raw);
                _counts[i].text = $"1 {Recipes[i].raw} · you have {have}";
                _counts[i].color = have > 0 ? HudTheme.TextDim : HudTheme.Hostile;
                _cookOne[i].interactable = _cookAll[i].interactable = have > 0 && !Cooking;
            }
            _status.text = Cooking
                ? $"Cooking {Recipes[_recipe].meal}: {_done + 1} of {_done + _left}"
                : $"The fire has {Mathf.Max(0f, _fire.HoursLeft):0.0} game hours left in it.";
        }

        protected override void Closed()
        {
            if (Cooking)
                Stop(interrupted: true);
        }

        protected override void Update()
        {
            base.Update();
            if (!Cooking)
                return;

            if (_fire == null || !_fire.IsLit || Distance() > Reach || _health != null && _health.IsDead)
            {
                Stop(interrupted: true);
                return;
            }

            float elapsed = Time.time - _started;
            var (raw, meal) = Recipes[_recipe];
            if (elapsed < CookSeconds)
            {
                CastBarHud.Show($"Cooking {meal}", elapsed / CookSeconds, CookSeconds - elapsed);
                return;
            }

            var cooked = ItemCatalog.Create(meal);
            if (!_inventory.CanAdd(cooked))
            {
                Hud.Error("Inventory is full.");
                Stop(interrupted: true);
                return;
            }
            if (!_inventory.TakeOneFromBags(raw))
            {
                Stop(interrupted: true);
                return;
            }
            _inventory.Add(cooked);
            Hud.Info($"You cook {meal}.");
            if (Hunger.StillStarving)
                Thoughts.Once("cooked", "That smells... wonderful. I should eat.");

            _done++;
            if (--_left <= 0 || Have(raw) <= 0)
            {
                Stop(interrupted: false);
                return;
            }
            _started = Time.time;
            CastBarHud.Show($"Cooking {meal}", 0f, CookSeconds);
        }

        void Begin(int recipe, int count)
        {
            if (Cooking || count <= 0 || _fire == null || !_fire.IsLit)
                return;
            if (!_inventory.CanAdd(ItemCatalog.Create(Recipes[recipe].meal)))
            {
                Hud.Error("Inventory is full.");
                return;
            }
            _recipe = recipe;
            _left = count;
            _done = 0;
            _started = Time.time;
            _fire.Cooking = true;
            // Up off the ground first, to kneel over the pan.
            GetComponent<PlayerSit>()?.Stand();
            Face(_fire.transform);
            Kneel(true);
            CastBarHud.Show($"Cooking {Recipes[recipe].meal}", 0f, CookSeconds);
        }

        void Stop(bool interrupted)
        {
            _recipe = -1;
            _left = 0;
            if (_fire != null)
                _fire.Cooking = false;
            Kneel(false);
            if (interrupted)
                CastBarHud.Interrupt();
            else
                CastBarHud.Finish();
        }

        int Have(string itemName)
        {
            int count = 0;
            for (int i = 0; i < _inventory.BagSize; i++)
            {
                var item = _inventory.BagItem(i);
                if (item != null && item.baseName == itemName)
                    count += item.count;
            }
            return count;
        }

        float Distance()
        {
            var offset = transform.position - _fire.transform.position;
            offset.y = 0f;
            return offset.magnitude;
        }

        void Face(Transform target)
        {
            var toward = target.position - transform.position;
            toward.y = 0f;
            if (toward.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.LookRotation(toward, Vector3.up);
        }

        /// <summary>Down on one knee by the fire while it cooks — the loot window's pose, which it keeps.</summary>
        void Kneel(bool down)
        {
            var loot = GetComponent<LootPanel>();
            if (loot != null)
                loot.HeldDown = down;
        }
    }
}
