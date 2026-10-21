using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace WowSandbox
{
    /// <summary>
    /// The Journal, on J: an open book. The pages found so far are listed on the left, grouped
    /// by whose hand they're in, with a "? ? ?" for each one still out there; the selected page
    /// reads on the right, on parchment, in its writer's ink (Kell's faded and slanted). Finding
    /// a page opens it here.
    ///
    /// It needs no setup: one is created in every scene at Play.
    /// </summary>
    public class JournalPanel : HudPanel
    {
        const float Width = 800f;
        const float Height = 520f;
        const float Pad = 20f;
        const float ListWidth = 230f;
        const float RowHeight = 24f;

        static readonly Color Parchment = new(0.84f, 0.77f, 0.62f, 1f);
        static readonly Color ParchmentEdge = new(0.45f, 0.33f, 0.18f, 0.8f);
        static readonly Color RowSelected = new(0.55f, 0.42f, 0.18f, 0.7f);
        static readonly Color RowHover = new(0.55f, 0.42f, 0.18f, 0.35f);

        RectTransform _list;
        Text _pageTitle;
        Text _pageText;
        Text _pageHand;
        int _builtVersion = -1;
        int _selected;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void CreateInScene()
        {
            if (FindAnyObjectByType<JournalPanel>() == null)
                new GameObject("Journal").AddComponent<JournalPanel>();
        }

        protected override Vector2 PanelSize => new(Width, Height);
        protected override Vector2 PanelAnchor => new(0.5f, 0.5f);
        protected override Vector2 PanelPosition => Vector2.zero;

        protected override void Awake()
        {
            toggleKey = Key.J;
            base.Awake();
        }

        void OnEnable() => StoryState.PageFound += ShowPage;

        protected override void OnDisable()
        {
            StoryState.PageFound -= ShowPage;
            base.OnDisable();
        }

        protected override bool CanOpen() => !TitleScreen.Showing;

        /// <summary>Opens the Journal on page <paramref name="number"/>.</summary>
        public void ShowPage(int number)
        {
            _selected = number;
            _builtVersion = -1;
            Open();
        }

        protected override void Build()
        {
            var topLeft = new Vector2(0f, 1f);
            Hud.Label("Title", Root, new Vector2(0.5f, 1f), new Vector2(0f, -14f), new Vector2(Width, 28f),
                22, TextAnchor.MiddleCenter, HudTheme.Heading, FontStyle.Bold).text = "Journal";

            _list = Hud.Rect("Pages", Root, topLeft, new Vector2(Pad, -54f), new Vector2(ListWidth, Height - 54f - Pad));
            Hud.Rounded(_list.gameObject, HudTheme.SubPanel, 6f);

            float pageX = Pad + ListWidth + 18f;
            var page = Hud.Rect("Page", Root, topLeft, new Vector2(pageX, -54f), new Vector2(Width - pageX - Pad, Height - 54f - Pad));
            Hud.Rounded(page.gameObject, Parchment, 6f);
            Hud.Ring(Hud.Stretch("Edge", page, 3f).gameObject, ParchmentEdge, 4f);

            float inner = page.sizeDelta.x - 56f;
            _pageTitle = Ink("PageTitle", page, new Vector2(28f, -22f), new Vector2(inner, 30f), 21, FontStyle.Bold);
            _pageText = Ink("PageText", page, new Vector2(28f, -62f), new Vector2(inner, page.sizeDelta.y - 110f), 16, FontStyle.Normal);
            _pageText.lineSpacing = 1.15f;
            _pageHand = Ink("PageHand", page, new Vector2(28f, -(page.sizeDelta.y - 40f)), new Vector2(inner, 20f), 13, FontStyle.Italic);
            _pageHand.alignment = TextAnchor.MiddleRight;
        }

        protected override void Refresh()
        {
            if (_builtVersion == StoryState.Version)
                return;
            _builtVersion = StoryState.Version;

            if (!StoryState.HasPage(_selected))
                _selected = FirstFound();
            RebuildList();
            ShowSelected();
        }

        static int FirstFound()
        {
            foreach (var hand in JournalPages.Hands)
                foreach (var page in JournalPages.InHand(hand))
                    if (StoryState.HasPage(page.number))
                        return page.number;
            return 0;
        }

        void RebuildList()
        {
            for (int i = _list.childCount - 1; i >= 0; i--)
                Destroy(_list.GetChild(i).gameObject);

            var topLeft = new Vector2(0f, 1f);
            float y = -8f;
            foreach (var hand in JournalPages.Hands)
            {
                Hud.Label(hand.ToString(), _list, topLeft, new Vector2(10f, y), new Vector2(ListWidth - 20f, 20f),
                    13, TextAnchor.MiddleLeft, HudTheme.Heading, FontStyle.Bold).text = JournalPages.HandName(hand).ToUpperInvariant();
                y -= 24f;

                foreach (var page in JournalPages.InHand(hand))
                {
                    if (StoryState.HasPage(page.number))
                        PageRow(page, y);
                    else
                        Hud.Label("Unfound", _list, topLeft, new Vector2(18f, y), new Vector2(ListWidth - 28f, RowHeight - 2f),
                            14, TextAnchor.MiddleLeft, HudTheme.TextDim).text = "? ? ?";
                    y -= RowHeight;
                }
                y -= 8f;
            }
        }

        void PageRow(JournalPage page, float y)
        {
            var rect = Hud.Rect(page.title, _list, new Vector2(0f, 1f), new Vector2(8f, y), new Vector2(ListWidth - 16f, RowHeight - 2f));
            // White, coloured by the button's tint, which multiplies: clear unless hovered or chosen.
            var image = Hud.Rounded(rect.gameObject, Color.white, 4f);
            image.raycastTarget = true;
            var label = Hud.Label("Label", rect, 14, TextAnchor.MiddleLeft, HudTheme.Text);
            label.rectTransform.offsetMin = new Vector2(10f, 0f);
            label.text = page.title;

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = page.number == _selected ? RowSelected : Color.clear;
            colors.highlightedColor = page.number == _selected ? RowSelected : RowHover;
            colors.selectedColor = colors.normalColor;
            colors.pressedColor = RowSelected;
            button.colors = colors;
            int number = page.number;
            button.onClick.AddListener(() =>
            {
                _selected = number;
                _builtVersion = -1;
            });
        }

        void ShowSelected()
        {
            if (!JournalPages.TryFind(_selected, out var page))
            {
                _pageTitle.text = "";
                _pageHand.text = "";
                _pageText.color = InkColor(JournalHand.Log);
                _pageText.fontStyle = FontStyle.Italic;
                _pageText.text = "Your journal is empty.\n\nThe wreck took your memories along with everything else. " +
                                 "Perhaps some of your log survived it.";
                return;
            }

            var ink = InkColor(page.hand);
            _pageTitle.color = _pageText.color = _pageHand.color = ink;
            _pageText.fontStyle = page.hand == JournalHand.Kell ? FontStyle.Italic : FontStyle.Normal;
            _pageTitle.text = page.title;
            _pageText.text = page.text;
            _pageHand.text = "— " + JournalPages.HandName(page.hand);
        }

        /// <summary>The Captain's dark ink, Kell's faded brown, the castaways' blue-black.</summary>
        static Color InkColor(JournalHand hand) => hand switch
        {
            JournalHand.Kell => new Color(0.38f, 0.28f, 0.18f),
            JournalHand.Castaway => new Color(0.16f, 0.19f, 0.26f),
            _ => new Color(0.2f, 0.13f, 0.07f),
        };

        /// <summary>Wrapped text for the parchment: no outline, unlike the HUD's labels.</summary>
        static Text Ink(string name, Transform parent, Vector2 position, Vector2 size, int fontSize, FontStyle style)
        {
            var rect = Hud.Rect(name, parent, new Vector2(0f, 1f), position, size);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = Hud.Font;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.supportRichText = true;
            text.raycastTarget = false;
            return text;
        }
    }
}
