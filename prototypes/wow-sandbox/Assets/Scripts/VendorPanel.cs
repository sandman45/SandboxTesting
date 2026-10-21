using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace WowSandbox
{
    /// <summary>
    /// The shop window. Clicking a Vendor from close by opens it beside your bags.
    ///
    /// Merchant tab: their stock, a page at a time — click (or drag into your bags) to buy
    /// one. Buyback tab: what you've sold them lately, at the price you got. Selling is
    /// right-clicking a bag item while the shop's open, or dragging it onto the shop. Sell
    /// Junk sells every Common trade good at once. Anything Rare or better asks first. Closes when
    /// you walk away.
    /// </summary>
    [RequireComponent(typeof(Inventory), typeof(TargetingController))]
    public class VendorPanel : HudPanel
    {
        const int Columns = 2;
        const int RowsPerColumn = 6;
        const int PerPage = Columns * RowsPerColumn;
        const float SlotSize = 44f;
        const float RowHeight = 52f;
        const float ColumnWidth = 240f;
        const float Pad = 16f;
        const float Top = 92f;

        static readonly Color TabOn = new(0.62f, 0.48f, 0.2f, 1f);
        static readonly Color TabOff = new(0.22f, 0.2f, 0.17f, 1f);

        Inventory _inventory;
        CharacterStats _stats;
        TargetingController _targeting;
        Vendor _vendor;
        bool _buybackTab;
        int _page;

        Text _title;
        Text _greeting;
        Text _pageText;
        Text _money;
        Image _merchantTab;
        Image _buybackTabImage;
        readonly ItemSlotView[] _slots = new ItemSlotView[PerPage];
        readonly Text[] _names = new Text[PerPage];
        readonly Text[] _prices = new Text[PerPage];

        RectTransform _confirm;
        Text _confirmText;
        Action _confirmed;

        protected override Vector2 PanelSize => new(Pad * 2f + Columns * ColumnWidth + 10f,
                                                    Top + RowsPerColumn * RowHeight + 64f);
        protected override Vector2 PanelAnchor => new(0f, 0.5f);
        protected override Vector2 PanelPosition => new(40f, 40f);

        protected override void Awake()
        {
            toggleKey = Key.None;   // opened by clicking a merchant, not a key
            altToggleKey = Key.None;
            _inventory = GetComponent<Inventory>();
            _stats = GetComponent<CharacterStats>();
            _targeting = GetComponent<TargetingController>();
            base.Awake();
        }

        void OnEnable() => _targeting.Clicked += OnClicked;

        protected override void OnDisable()
        {
            _targeting.Clicked -= OnClicked;
            base.OnDisable();
        }

        void OnClicked(Health clicked)
        {
            var vendor = clicked.GetComponent<Vendor>();
            if (vendor == null || clicked.IsDead)
                return;

            if (Vector3.Distance(transform.position, vendor.transform.position) > vendor.interactRange)
            {
                Hud.Error("You are too far away.");
                return;
            }
            if (vendor.Asleep)
            {
                Hud.Info($"{vendor.name} is fast asleep. Come back at dawn.");
                return;
            }

            _vendor = vendor;
            _buybackTab = false;
            _page = 0;
            HideConfirm();
            vendor.Greet(transform.position);
            Open();
            // Your bags open alongside, as in WoW, so there's something to sell from.
            GetComponent<InventoryPanel>()?.Open();
        }

        protected override void Update()
        {
            base.Update();
            if (IsOpen && _vendor != null && _vendor.Asleep)
            {
                Hud.Info($"{_vendor.name} yawns and shuts up shop for the night.");
                Close();
            }
        }

        protected override void Build()
        {
            var topLeft = new Vector2(0f, 1f);
            _title = Hud.Label("Title", Root, topLeft, new Vector2(Pad, -12f), new Vector2(400f, 24f),
                20, TextAnchor.MiddleLeft, HudTheme.Heading, FontStyle.Bold);
            _greeting = Hud.Label("Greeting", Root, topLeft, new Vector2(Pad, -36f), new Vector2(440f, 18f),
                13, TextAnchor.MiddleLeft, HudTheme.TextDim, FontStyle.Italic);

            _merchantTab = Hud.TextButton("Merchant", Root, topLeft, new Vector2(Pad, -60f), new Vector2(100f, 24f),
                () => SwitchTab(false)).targetGraphic as Image;
            _buybackTabImage = Hud.TextButton("Buyback", Root, topLeft, new Vector2(Pad + 106f, -60f), new Vector2(100f, 24f),
                () => SwitchTab(true)).targetGraphic as Image;

            for (int i = 0; i < PerPage; i++)
            {
                int row = i;
                float x = Pad + i / RowsPerColumn * (ColumnWidth + 10f);
                float y = -Top - i % RowsPerColumn * RowHeight;

                var slot = ItemSlotView.Create($"Slot{i}", Root, topLeft, new Vector2(x, y), SlotSize);
                slot.Source = () => Entry(row).item;
                slot.Ref = new SlotRef(SlotArea.Vendor, row);
                slot.Reader = _stats;
                slot.LeftClicked = _ => Buy(row);
                slot.RightClicked = _ => Buy(row);
                slot.DroppedOn = OnDroppedOnShop;
                _slots[i] = slot;

                _names[i] = Hud.Label("Name", Root, topLeft, new Vector2(x + SlotSize + 8f, y - 2f),
                    new Vector2(ColumnWidth - SlotSize - 10f, 20f), 13, TextAnchor.MiddleLeft, HudTheme.Text);
                _names[i].supportRichText = true;
                _names[i].horizontalOverflow = HorizontalWrapMode.Wrap;
                _prices[i] = Hud.Label("Price", Root, topLeft, new Vector2(x + SlotSize + 8f, y - 22f),
                    new Vector2(ColumnWidth - SlotSize - 10f, 18f), 12, TextAnchor.MiddleLeft, HudTheme.Text);
                _prices[i].supportRichText = true;
            }

            var bottomLeft = new Vector2(0f, 0f);
            Hud.TextButton("<", Root, bottomLeft, new Vector2(Pad, 14f), new Vector2(28f, 26f), () => Turn(-1));
            _pageText = Hud.Label("Page", Root, bottomLeft, new Vector2(Pad + 32f, 14f), new Vector2(80f, 26f),
                13, TextAnchor.MiddleCenter, HudTheme.TextDim);
            Hud.TextButton(">", Root, bottomLeft, new Vector2(Pad + 116f, 14f), new Vector2(28f, 26f), () => Turn(1));
            Hud.TextButton("Sell Junk", Root, bottomLeft, new Vector2(Pad + 158f, 14f), new Vector2(100f, 26f), SellJunk);

            _money = Hud.Label("Money", Root, new Vector2(1f, 0f), new Vector2(-Pad, 14f), new Vector2(200f, 26f),
                15, TextAnchor.MiddleRight, HudTheme.Text, FontStyle.Bold);
            _money.supportRichText = true;

            BuildConfirm();
        }

        protected override void Refresh()
        {
            if (_vendor == null || !_vendor.isActiveAndEnabled ||
                Vector3.Distance(transform.position, _vendor.transform.position) > _vendor.interactRange * 1.5f)
            {
                HideConfirm();
                Close();
                return;
            }

            _title.text = Clean(_vendor.name);
            _greeting.text = _vendor.greeting;
            _merchantTab.color = _buybackTab ? TabOff : TabOn;
            _buybackTabImage.color = _buybackTab ? TabOn : TabOff;

            int pages = Pages;
            _page = Mathf.Clamp(_page, 0, pages - 1);
            _pageText.text = $"Page {_page + 1}/{pages}";

            for (int i = 0; i < PerPage; i++)
            {
                var (item, price) = Entry(i);
                _slots[i].Refresh();
                _slots[i].gameObject.SetActive(item != null);
                _names[i].text = item == null ? "" :
                    $"<color=#{HudTheme.Hex(HudTheme.RarityColor(item.rarity))}>{item.name}</color>";
                bool afford = _inventory.copper >= price;
                _prices[i].text = item == null ? "" : afford
                    ? Money.Format(price, rich: true)
                    : $"<color=#{HudTheme.Hex(HudTheme.Hostile)}>{Money.Format(price)}</color>";
                _slots[i].TooltipHint = item == null ? null
                    : _buybackTab ? $"Click to buy back for {Money.Format(price)}" : $"Click to buy for {Money.Format(price)}";
            }

            _money.text = Money.Format(_inventory.copper, rich: true);
        }

        int Count => _vendor == null ? 0 : _buybackTab ? _vendor.Buyback.Count : _vendor.Stock.Count;
        int Pages => Mathf.Max(1, Mathf.CeilToInt(Count / (float)PerPage));

        /// <summary>The item and its price on row <paramref name="row"/> of the current page.</summary>
        (Item item, int price) Entry(int row)
        {
            if (_vendor == null)
                return (null, 0);
            int index = _page * PerPage + row;
            if (_buybackTab)
                return index < _vendor.Buyback.Count ? _vendor.Buyback[index] : (null, 0);
            if (index >= _vendor.Stock.Count)
                return (null, 0);
            var stocked = _vendor.Stock[index];
            return (stocked, stocked.valueCopper);
        }

        void SwitchTab(bool buyback)
        {
            _buybackTab = buyback;
            _page = 0;
        }

        void Turn(int pages) => _page = Mathf.Clamp(_page + pages, 0, Pages - 1);

        /// <summary>For InventoryPanel: a shop item was dragged into the bags.</summary>
        public void BuyFromSlot(ItemSlotView from) => Buy(from.Ref.Index);

        void Buy(int row)
        {
            var (item, price) = Entry(row);
            if (item == null || _confirm.gameObject.activeSelf)
                return;

            if (_inventory.copper < price)
            {
                Hud.Error("You don't have enough money.");
                return;
            }

            var bought = _buybackTab ? item : item.Clone();
            if (!_buybackTab)
                bought.count = 1;
            if (!_inventory.CanAdd(bought))
            {
                Hud.Error("Inventory is full.");
                return;
            }

            _inventory.Spend(price);
            _inventory.Add(bought.Clone());
            if (_buybackTab)
                _vendor.RemoveBuyback(_page * PerPage + row);
            _vendor.Talk();
        }

        /// <summary>Sells the whole stack in bag slot <paramref name="bagIndex"/> — asking first if
        /// it's Rare or better.</summary>
        public void Sell(int bagIndex)
        {
            if (_vendor == null || _confirm.gameObject.activeSelf)
                return;
            var item = _inventory.BagItem(bagIndex);
            if (item == null)
                return;

            int price = Money.SellPrice(item);
            if (price <= 0)
            {
                Hud.Error("The merchant won't buy that.");
                return;
            }

            if (item.rarity >= ItemRarity.Rare)
            {
                string rarity = HudTheme.Hex(HudTheme.RarityColor(item.rarity));
                Confirm($"Sell <color=#{rarity}>{item.name}</color> for {Money.Format(price, rich: true)}?",
                        () => SellNow(bagIndex, item));
                return;
            }

            SellNow(bagIndex, item);
        }

        void SellNow(int bagIndex, Item expected)
        {
            // The bags may have changed while a confirmation was up.
            if (_inventory.BagItem(bagIndex) != expected)
                return;

            int price = Money.SellPrice(expected);
            var sold = _inventory.TakeFromBag(bagIndex);
            _inventory.AddCopper(price);
            _vendor.AddBuyback(sold, price);
            Hud.Info($"Sold {sold.name}{(sold.count > 1 ? $" x{sold.count}" : "")} for {Money.Format(price, rich: true)}.");
        }

        void SellJunk()
        {
            if (_vendor == null || _confirm.gameObject.activeSelf)
                return;

            int total = 0, sold = 0;
            for (int i = 0; i < _inventory.BagSize; i++)
            {
                var item = _inventory.BagItem(i);
                // A prize fish is a trade good too, but it isn't junk.
                if (item == null || item.kind != ItemKind.TradeGood || item.rarity != ItemRarity.Common)
                    continue;
                int price = Money.SellPrice(item);
                _inventory.TakeFromBag(i);
                _inventory.AddCopper(price);
                _vendor.AddBuyback(item, price);
                total += price;
                sold++;
            }

            if (sold == 0)
                Hud.Error("You have no junk to sell.");
            else
                Hud.Info($"Sold {sold} stack{(sold > 1 ? "s" : "")} of junk for {Money.Format(total, rich: true)}.");
        }

        void OnDroppedOnShop(ItemSlotView from, ItemSlotView to)
        {
            if (from.Ref.Area == SlotArea.Bag)
                Sell(from.Ref.Index);
        }

        // --- Confirmation --------------------------------------------------------------------

        void BuildConfirm()
        {
            _confirm = Hud.Stretch("Confirm", Root, 2f);
            var shade = Hud.Rounded(_confirm.gameObject, new Color(0f, 0f, 0f, 0.75f), 8f);
            shade.raycastTarget = true;   // nothing behind it clickable while it's up

            var box = Hud.Rect("Box", _confirm, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(360f, 120f));
            Hud.Panel(box);
            _confirmText = Hud.Label("Text", box, new Vector2(0.5f, 1f), new Vector2(0f, -16f), new Vector2(330f, 50f),
                15, TextAnchor.MiddleCenter, HudTheme.Text);
            _confirmText.supportRichText = true;
            _confirmText.horizontalOverflow = HorizontalWrapMode.Wrap;
            Hud.TextButton("Sell", box, new Vector2(0.5f, 0f), new Vector2(-60f, 16f), new Vector2(100f, 30f), () =>
            {
                var action = _confirmed;
                HideConfirm();
                action?.Invoke();
            });
            Hud.TextButton("Cancel", box, new Vector2(0.5f, 0f), new Vector2(60f, 16f), new Vector2(100f, 30f), HideConfirm);
            HideConfirm();
        }

        void Confirm(string question, Action onYes)
        {
            _confirmText.text = question;
            _confirmed = onYes;
            _confirm.gameObject.SetActive(true);
            _confirm.SetAsLastSibling();
        }

        void HideConfirm()
        {
            _confirmed = null;
            if (_confirm != null)
                _confirm.gameObject.SetActive(false);
        }

        static string Clean(string name) => System.Text.RegularExpressions.Regex.Replace(name, @"_\d+$", "");
    }
}
