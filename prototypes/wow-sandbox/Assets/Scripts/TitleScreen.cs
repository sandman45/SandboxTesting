using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace WowSandbox
{
    /// <summary>
    /// The opening screen. Play opens on the warrior standing where the game begins (by the
    /// shipwreck — WoW Sandbox → Place Player at Shipwreck), the camera slowly circling them,
    /// with "Tidegrave Isle", a pulsing "Click to Start", and Settings and Quit buttons.
    /// Clicking anywhere else (or Enter/Space) swoops the camera in behind the warrior and
    /// hands over the controls and the HUD.
    ///
    /// With a saved game there's Continue and New Game instead: Continue fades to black, puts
    /// the save back (SaveGame.Load — no shipwreck opening) and fades in where the Captain left
    /// off; New Game asks first, then wipes the save and starts at the wreck as above.
    ///
    /// While it's up the world is calm and safe: the storm cycle waits (StormWeather checks
    /// Showing), nothing aggros (NpcCombat), and the controls are locked. Created in every
    /// scene with a player, like the settings menu; the developer panel's Skip Title goes
    /// straight in (into the save, if there is one).
    /// </summary>
    [DefaultExecutionOrder(1000)] // after WowOrbitCamera, so the swoop can blend from its pose
    public class TitleScreen : MonoBehaviour
    {
        public string title = "Tidegrave Isle";
        [Tooltip("How far out the camera circles, and how high.")]
        public float orbitRadius = 7f;
        public float orbitHeight = 2.2f;
        [Tooltip("Degrees per second.")]
        public float orbitSpeed = 7f;
        public float swoopSeconds = 1.6f;

        /// <summary>True from Play starting until the title is dismissed.</summary>
        public static bool Showing { get; private set; }

        WowCharacterController _player;
        WowOrbitCamera _orbitCamera;
        TargetingController _targeting;
        Camera _camera;
        float _angle;
        float _swoopStart = -1f;
        Vector3 _swoopFromPosition;
        Quaternion _swoopFromRotation;

        CanvasGroup _group;
        Text _prompt;

        // The saved game on offer, or null; and Continue's black-out.
        SaveGame.SaveData _save;
        RectTransform _confirm;
        Image _fade;
        bool _continuing;
        bool _orbitDone;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void CreateInScene()
        {
            Showing = false;
            if (DevOptions.Enabled && DevOptions.SkipTitle)
                return;
            if (FindAnyObjectByType<WowCharacterController>() == null || FindAnyObjectByType<TitleScreen>() != null)
                return;
            new GameObject("Title Screen").AddComponent<TitleScreen>();
        }

        // Awake, not Start: it runs as soon as this is created, before any scene object's
        // Start — so StormWeather sees Showing when it decides whether to build a storm.
        void Awake()
        {
            Showing = true;
            _player = FindAnyObjectByType<WowCharacterController>();
            _targeting = _player.GetComponent<TargetingController>();
            _camera = Camera.main;
            _orbitCamera = _camera != null ? _camera.GetComponent<WowOrbitCamera>() : null;

            _player.SetLocked(true);
            if (_targeting != null)
                _targeting.enabled = false;
            if (_orbitCamera != null)
                _orbitCamera.enabled = false;
            // Start the circle facing the warrior's front.
            _angle = _player.transform.eulerAngles.y;

            SetHudVisible(false);
            _save = SaveGame.Peek();
            Build();
        }

        void OnDestroy()
        {
            Showing = false;
            if (_fade != null)
                Destroy(_fade.gameObject);
        }

        void Update()
        {
            if (_swoopStart >= 0f || _continuing)
                return;

            // Pulse the prompt.
            _prompt.color = new Color(_prompt.color.r, _prompt.color.g, _prompt.color.b,
                0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 3f));

            var keyboard = Keyboard.current;
            if (keyboard != null && (keyboard.enterKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame) &&
                !HudPanel.AnyOpen && (_confirm == null || !_confirm.gameObject.activeSelf))
            {
                if (_save != null)
                    StartCoroutine(ContinueGame());
                else
                    BeginGame();
            }
        }

        void LateUpdate()
        {
            if (_camera == null || _orbitDone)
                return;

            if (_swoopStart < 0f)
            {
                Orbit();
                return;
            }

            // WowOrbitCamera has just placed the camera where play wants it; ease from where
            // the title left it to there.
            float t = Mathf.Clamp01((Time.time - _swoopStart) / swoopSeconds);
            float ease = t * t * (3f - 2f * t);
            var cameraTransform = _camera.transform;
            cameraTransform.SetPositionAndRotation(
                Vector3.Lerp(_swoopFromPosition, cameraTransform.position, ease),
                Quaternion.Slerp(_swoopFromRotation, cameraTransform.rotation, ease));

            if (t >= 1f)
                Destroy(gameObject);
        }

        void Orbit()
        {
            _angle += orbitSpeed * Time.deltaTime;
            var body = _player.GetComponent<CharacterController>();
            var focus = _player.transform.position + Vector3.up * (body != null ? body.height * 0.6f : 1.2f);
            var offset = Quaternion.Euler(0f, _angle, 0f) * Vector3.forward * orbitRadius + Vector3.up * (orbitHeight - 1f);
            var position = focus + offset;

            // Pulled in rather than hidden behind a palm or the wreck.
            if (Physics.Raycast(focus, offset.normalized, out var hit, offset.magnitude, ~0, QueryTriggerInteraction.Ignore) &&
                !hit.collider.transform.IsChildOf(_player.transform))
                position = hit.point - offset.normalized * 0.3f;

            _camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(focus - position));
        }

        void BeginGame()
        {
            if (_swoopStart >= 0f)
                return;

            _swoopStart = Time.time;
            _swoopFromPosition = _camera != null ? _camera.transform.position : Vector3.zero;
            _swoopFromRotation = _camera != null ? _camera.transform.rotation : Quaternion.identity;
            _group.gameObject.SetActive(false);
            Showing = false;

            if (_orbitCamera != null)
                _orbitCamera.enabled = true;
            if (_targeting != null)
                _targeting.enabled = true;
            _player.SetLocked(false);
            SetHudVisible(true);
            if (StormWeather.Active != null)
                StormWeather.Active.BeginPlay();
        }

        /// <summary>
        /// Continue: to black, the saved game put back, the controls handed over, and back in —
        /// no swoop, since he's no longer where the title was circling.
        /// </summary>
        System.Collections.IEnumerator ContinueGame()
        {
            if (_continuing || _swoopStart >= 0f)
                yield break;
            _continuing = true;
            _group.interactable = false;

            yield return Fade(0f, 1f, 0.5f);
            _group.gameObject.SetActive(false);
            if (!SaveGame.Load())
                Hud.Error("The saved game couldn't be loaded.");
            _orbitDone = true;
            Showing = false;
            if (_orbitCamera != null)
                _orbitCamera.enabled = true;
            if (_targeting != null)
                _targeting.enabled = true;
            _player.SetLocked(false);
            SetHudVisible(true);

            // A moment in the dark for the camera to settle behind him.
            yield return new WaitForSecondsRealtime(0.25f);
            yield return Fade(1f, 0f, 0.8f);
            Destroy(gameObject);
        }

        System.Collections.IEnumerator Fade(float from, float to, float seconds)
        {
            if (_fade == null)
            {
                var rect = Hud.Stretch("Fade", Hud.Layer(HudLayer.Overlay));
                _fade = rect.gameObject.AddComponent<Image>();
                _fade.raycastTarget = false;
            }
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                _fade.color = new Color(0f, 0f, 0f, Mathf.Lerp(from, to, t / seconds));
                yield return null;
            }
            _fade.color = new Color(0f, 0f, 0f, to);
        }

        void NewGame()
        {
            if (_save == null)
            {
                BeginGame();
                return;
            }
            _confirm.gameObject.SetActive(true);
        }

        void ConfirmNewGame()
        {
            SaveGame.Delete();
            _save = null;
            _confirm.gameObject.SetActive(false);
            BeginGame();
        }

        /// <summary>"Level 3 Fighter · 14:20 on the isle · saved 2026-10-21 18:05".</summary>
        string SaveSummary()
        {
            int minutes = Mathf.FloorToInt(Mathf.Repeat(_save.hour, 24f) * 60f);
            var stats = _player.GetComponent<CharacterStats>();
            string who = $"Level {(_save.player != null ? _save.player.level : 1)} {(stats != null ? stats.className : "")}".Trim();
            return $"{who}  ·  {minutes / 60:00}:{minutes % 60:00} on the isle  ·  saved {_save.savedAt}";
        }

        static void SetHudVisible(bool visible)
        {
            foreach (var layer in new[] { HudLayer.Nameplates, HudLayer.CombatText, HudLayer.Frames })
                Hud.Layer(layer).gameObject.SetActive(visible);
        }

        void Build()
        {
            // In the Panels layer, under any panel opened later — so Settings opens on top.
            var root = Hud.Stretch("TitleScreen", Hud.Layer(HudLayer.Panels));
            root.SetAsFirstSibling();
            _group = root.gameObject.AddComponent<CanvasGroup>();

            // The whole screen is the start button; the real buttons sit on top of it.
            var catcher = root.gameObject.AddComponent<Image>();
            catcher.color = new Color(0f, 0f, 0f, 0f);
            catcher.raycastTarget = true;
            // With a save to continue, it's the buttons that choose.
            root.gameObject.AddComponent<Button>().onClick.AddListener(() =>
            {
                if (!HudPanel.AnyOpen && _save == null)
                    BeginGame();
            });

            // A soft darkening behind the words, top and bottom.
            Shade(root, new Vector2(0f, 0.62f), Vector2.one, 0.45f);
            Shade(root, Vector2.zero, new Vector2(1f, 0.28f), 0.4f);

            var titleText = Hud.Label("Title", root, new Vector2(0.5f, 0.82f), Vector2.zero, new Vector2(1000f, 90f),
                72, TextAnchor.MiddleCenter, HudTheme.Heading, FontStyle.Bold);
            titleText.text = title;
            titleText.GetComponent<Outline>().effectDistance = new Vector2(2f, -2f);

            _prompt = Hud.Label("Prompt", root, new Vector2(0.5f, 0.14f), Vector2.zero, new Vector2(600f, 34f),
                24, TextAnchor.MiddleCenter, HudTheme.Text, FontStyle.Bold);
            _prompt.text = "Click to Start";

            if (_save != null)
            {
                _prompt.gameObject.SetActive(false);
                var low = new Vector2(0.5f, 0.14f);
                var size = new Vector2(190f, 44f);
                var continueButton = Hud.TextButton("Continue", root, low, new Vector2(-105f, 18f), size,
                    () => StartCoroutine(ContinueGame()));
                continueButton.GetComponentInChildren<Text>().fontSize = 20;
                var newButton = Hud.TextButton("New Game", root, low, new Vector2(105f, 18f), size, NewGame);
                newButton.GetComponentInChildren<Text>().fontSize = 20;
                Hud.Label("Saved", root, low, new Vector2(0f, -32f), new Vector2(800f, 24f),
                    15, TextAnchor.MiddleCenter, HudTheme.TextDim).text = SaveSummary();
                BuildConfirm(root);
            }

            var corner = new Vector2(1f, 0f);
            Hud.TextButton("Settings", root, corner, new Vector2(-136f, 24f), new Vector2(110f, 32f), () =>
            {
                var settings = FindAnyObjectByType<SettingsPanel>();
                if (settings != null)
                    settings.Open();
            });
            Hud.TextButton("Quit", root, corner, new Vector2(-18f, 24f), new Vector2(110f, 32f), Quit);
        }

        /// <summary>New Game's "are you sure", hidden till it's asked.</summary>
        void BuildConfirm(Transform root)
        {
            var centre = new Vector2(0.5f, 0.5f);
            _confirm = Hud.Rect("Confirm New Game", root, centre, Vector2.zero, new Vector2(480f, 150f));
            Hud.Panel(_confirm);
            // Soaks up clicks, so one on the box doesn't reach the screen behind it.
            _confirm.GetComponent<Image>().raycastTarget = true;
            var text = Hud.Label("Question", _confirm, new Vector2(0.5f, 1f), new Vector2(0f, -22f), new Vector2(440f, 56f),
                17, TextAnchor.MiddleCenter, HudTheme.Text);
            text.text = "Start a new game?\nYour saved game will be lost.";
            var bottom = new Vector2(0.5f, 0f);
            Hud.TextButton("New Game", _confirm, bottom, new Vector2(-75f, 20f), new Vector2(130f, 34f), ConfirmNewGame);
            Hud.TextButton("Cancel", _confirm, bottom, new Vector2(75f, 20f), new Vector2(130f, 34f),
                () => _confirm.gameObject.SetActive(false));
            _confirm.gameObject.SetActive(false);
        }

        static void Shade(Transform parent, Vector2 anchorMin, Vector2 anchorMax, float alpha)
        {
            var shade = Hud.Stretch("Shade", parent);
            shade.anchorMin = anchorMin;
            shade.anchorMax = anchorMax;
            var image = shade.gameObject.AddComponent<Image>();
            image.color = new Color(0f, 0f, 0f, alpha);
            image.raycastTarget = false;
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
