using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace WowSandbox
{
    /// <summary>
    /// Talking to someone: a box along the bottom of the screen with a live portrait of them on
    /// the left (a small camera on their head and shoulders), their name, what they say typed out
    /// a letter at a time, and your replies underneath. Click a reply or press its number; a click
    /// or Space shows the rest of a line at once. You stand still while talking; Esc or a goodbye
    /// ends it, and they say a parting line as you go.
    ///
    /// Click a Speaker within their talk range to start. It needs no setup: one is created in
    /// every scene at Play.
    /// </summary>
    public class DialoguePanel : HudPanel
    {
        const float Width = 880f;
        const float Height = 244f;
        const float Pad = 14f;
        const float PortraitWidth = 180f;
        const float RowHeight = 22f;
        const int MaxReplies = 5;
        const float LettersPerSecond = 55f;

        static readonly Color RowHover = new(0.55f, 0.42f, 0.18f, 0.45f);
        static readonly Color TradeColor = new(1f, 0.83f, 0.3f);

        TargetingController _targeting;
        WowCharacterController _player;
        Speaker _speaker;
        DialogueNode _node;
        readonly List<DialogueOption> _replies = new();
        float _letters;
        int _openedFrame = -1;
        bool _farewell;

        Text _name;
        Text _line;
        readonly Button[] _rows = new Button[MaxReplies];
        readonly Text[] _rowLabels = new Text[MaxReplies];
        DialoguePortrait _portrait;

        /// <summary>True while a conversation is open — the Captain's thoughts wait till it's over.</summary>
        public static bool Talking { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void CreateInScene()
        {
            Talking = false;
            if (FindAnyObjectByType<DialoguePanel>() == null)
                new GameObject("Dialogue").AddComponent<DialoguePanel>();
        }

        protected override Vector2 PanelSize => new(Width, Height);
        protected override Vector2 PanelAnchor => new(0.5f, 0f);
        protected override Vector2 PanelPosition => new(0f, 130f);

        protected override void Awake()
        {
            toggleKey = Key.None;    // opened by clicking someone, not a key
            altToggleKey = Key.None;
            base.Awake();
        }

        void Start()
        {
            _player = FindAnyObjectByType<WowCharacterController>();
            _targeting = _player != null ? _player.GetComponent<TargetingController>() : null;
            if (_targeting != null)
                _targeting.Clicked += OnClicked;
        }

        void OnDestroy()
        {
            if (_targeting != null)
                _targeting.Clicked -= OnClicked;
            _portrait?.Dispose();
        }

        protected override void Build()
        {
            var topLeft = new Vector2(0f, 1f);
            var frame = Hud.Rect("Portrait", Root, topLeft, new Vector2(Pad, -Pad), new Vector2(PortraitWidth, Height - Pad * 2f));
            Hud.Rounded(frame.gameObject, Color.black, 6f);
            var picture = Hud.Stretch("Picture", frame, 2f).gameObject.AddComponent<RawImage>();
            picture.raycastTarget = false;
            Hud.Ring(Hud.Stretch("Border", frame, -1f).gameObject, HudTheme.PanelBorder, 6f);
            _portrait = new DialoguePortrait(picture, transform);

            float x = Pad + PortraitWidth + 18f;
            float textWidth = Width - x - Pad - 30f;
            _name = Hud.Label("Name", Root, topLeft, new Vector2(x, -12f), new Vector2(textWidth, 24f),
                19, TextAnchor.MiddleLeft, HudTheme.Heading, FontStyle.Bold);

            _line = Hud.Label("Line", Root, topLeft, new Vector2(x, -42f), new Vector2(textWidth, 80f),
                16, TextAnchor.UpperLeft, HudTheme.Text);
            _line.horizontalOverflow = HorizontalWrapMode.Wrap;
            _line.lineSpacing = 1.1f;
            _line.supportRichText = false;

            for (int i = 0; i < MaxReplies; i++)
            {
                var rect = Hud.Rect("Reply " + (i + 1), Root, topLeft, new Vector2(x - 6f, -128f - i * RowHeight),
                    new Vector2(textWidth + 6f, RowHeight - 1f));
                // White, coloured by the button's tint, which multiplies: clear until hovered.
                var image = Hud.Rounded(rect.gameObject, Color.white, 4f);
                image.raycastTarget = true;
                _rowLabels[i] = Hud.Label("Label", rect, 15, TextAnchor.MiddleLeft, HudTheme.Text);
                _rowLabels[i].rectTransform.offsetMin = new Vector2(8f, 0f);

                var button = rect.gameObject.AddComponent<Button>();
                button.targetGraphic = image;
                var colors = button.colors;
                colors.normalColor = colors.selectedColor = Color.clear;
                colors.highlightedColor = RowHover;
                colors.pressedColor = RowHover;
                button.colors = colors;
                int index = i;
                button.onClick.AddListener(() => Choose(index));
                _rows[i] = button;
            }
        }

        void OnClicked(Health clicked)
        {
            var speaker = clicked.GetComponent<Speaker>();
            if (speaker == null || speaker.Conversation == null || clicked.IsDead || IsOpen)
                return;
            if (_player == null || _player.IsLocked || TitleScreen.Showing)
                return;

            if (Vector3.Distance(_player.transform.position, speaker.transform.position) > speaker.talkRange)
            {
                Hud.Error("You are too far away.");
                return;
            }
            if (speaker.Asleep)
            {
                Hud.Info($"{speaker.Conversation.Title} is fast asleep. Come back at dawn.");
                return;
            }

            _speaker = speaker;
            _farewell = true;
            _openedFrame = Time.frameCount;
            speaker.Face(_player.transform.position);
            _player.SetLocked(true);
            _name.text = speaker.Conversation.Title;
            Show(speaker.Conversation.Start(speaker));
            Open();
            Talking = true;
            _portrait.Begin(speaker);
        }

        void Show(DialogueNode node)
        {
            _node = node;
            _node?.shown?.Invoke();
            _letters = 0f;
            _replies.Clear();
            if (_node != null)
                foreach (var option in _node.options)
                    if (option.kind != DialogueOption.Kind.Trade || CanTrade)
                        _replies.Add(option);
            for (int i = 0; i < MaxReplies; i++)
            {
                bool used = i < _replies.Count;
                _rows[i].gameObject.SetActive(false);
                if (!used)
                    continue;
                var reply = _replies[i];
                _rowLabels[i].text = $"{i + 1}.  {reply.text}";
                _rowLabels[i].color = reply.kind switch
                {
                    DialogueOption.Kind.Trade => TradeColor,
                    DialogueOption.Kind.Goodbye => HudTheme.TextDim,
                    _ => HudTheme.Text,
                };
            }
            if (_replies.Count > MaxReplies)
                Debug.LogWarning($"[DialoguePanel] {_name.text}: {_replies.Count} replies, only {MaxReplies} fit.");
        }

        bool CanTrade => _speaker != null && _speaker.Vendor != null && !_speaker.Vendor.Closed;

        bool Typing => _node != null && _letters < _node.text.Length;

        void Choose(int index)
        {
            if (Typing || index >= _replies.Count)
                return;
            var reply = _replies[index];
            switch (reply.kind)
            {
                case DialogueOption.Kind.Talk:
                    var next = reply.next?.Invoke();
                    if (next != null)
                    {
                        Show(next);
                        _speaker.Talk(next.text);
                    }
                    else
                        Close();
                    break;

                case DialogueOption.Kind.Trade:
                    var vendor = _speaker.Vendor;
                    _farewell = false;
                    Close();
                    FindVendorPanel()?.OpenShop(vendor);
                    break;

                default:
                    Close();
                    break;
            }
        }

        VendorPanel FindVendorPanel() => _player != null ? _player.GetComponent<VendorPanel>() : null;

        protected override void Update()
        {
            base.Update();
            if (!IsOpen)
                return;

            // They've gone to bed, died or wandered off mid-sentence.
            if (_speaker == null || _speaker.Health.IsDead || _speaker.Asleep || _player == null ||
                Vector3.Distance(_player.transform.position, _speaker.transform.position) > _speaker.talkRange * 2f)
            {
                _farewell = false;
                Close();
                return;
            }

            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (Typing)
            {
                bool skip = Time.frameCount != _openedFrame &&
                            ((keyboard != null && keyboard.spaceKey.wasPressedThisFrame) ||
                             (mouse != null && mouse.leftButton.wasPressedThisFrame));
                _letters = skip ? _node.text.Length : _letters + LettersPerSecond * Time.deltaTime;
                _line.text = _node.text.Substring(0, Mathf.Min(_node.text.Length, Mathf.FloorToInt(_letters)));
                if (!Typing)
                    for (int i = 0; i < MaxReplies; i++)
                        _rows[i].gameObject.SetActive(i < _replies.Count);
                return;
            }

            if (keyboard == null)
                return;
            for (int i = 0; i < Mathf.Min(_replies.Count, 9); i++)
                if (keyboard[Key.Digit1 + i].wasPressedThisFrame)
                {
                    Choose(i);
                    return;
                }
        }

        // After the speaker's turned to you this frame, so the portrait keeps their face.
        void LateUpdate()
        {
            if (IsOpen)
                _portrait.Follow();
        }

        protected override void Refresh() { }

        protected override void Closed()
        {
            Talking = false;
            _portrait?.End();
            if (_player != null && _player.IsLocked)
                _player.SetLocked(false);
            if (_farewell && _speaker != null)
                _speaker.SayFarewell();
            _speaker = null;
            _node = null;
        }
    }

    /// <summary>
    /// The dialogue box's live portrait: a camera of its own, on the speaker's head and shoulders,
    /// drawing into a small texture only while a conversation is open. Follow keeps it on them
    /// as they turn; Speaker.portraitHeight and portraitDistance frame it.
    /// </summary>
    public class DialoguePortrait
    {
        readonly RawImage _picture;
        readonly Camera _camera;
        readonly RenderTexture _texture;
        Speaker _speaker;

        public DialoguePortrait(RawImage picture, Transform parent)
        {
            _picture = picture;
            _texture = new RenderTexture(360, 432, 24) { name = "Dialogue Portrait" };
            picture.texture = _texture;

            var go = new GameObject("Portrait Camera");
            go.transform.SetParent(parent, false);
            _camera = go.AddComponent<Camera>();
            _camera.targetTexture = _texture;
            _camera.fieldOfView = 28f;
            _camera.nearClipPlane = 0.05f;
            _camera.farClipPlane = 300f;
            _camera.enabled = false;
        }

        public void Begin(Speaker speaker)
        {
            // The same sky and layers as the view you're playing in.
            var main = Camera.main;
            if (main != null)
            {
                _camera.clearFlags = main.clearFlags;
                _camera.backgroundColor = main.backgroundColor;
                _camera.cullingMask = main.cullingMask;
            }
            _speaker = speaker;
            Follow();
            _camera.enabled = true;
            _picture.enabled = true;
        }

        public void End()
        {
            _camera.enabled = false;
            _speaker = null;
        }

        public void Dispose()
        {
            if (_texture != null)
                _texture.Release();
        }

        /// <summary>Stands the camera in front of the speaker's face, looking at it.</summary>
        public void Follow()
        {
            if (_speaker == null)
                return;
            float height = _speaker.Health.Height;
            var face = _speaker.transform.position + Vector3.up * height * _speaker.portraitHeight;
            var forward = _speaker.transform.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 0.001f ? forward.normalized : Vector3.forward;
            var camera = _camera.transform;
            camera.position = face + forward * height * _speaker.portraitDistance + Vector3.up * height * 0.02f;
            camera.LookAt(face - Vector3.up * height * 0.04f);
        }
    }
}
