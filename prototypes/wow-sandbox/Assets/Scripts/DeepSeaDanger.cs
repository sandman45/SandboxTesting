using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace WowSandbox
{
    /// <summary>
    /// The open sea is deadly, like WoW's fatigue. Swim far enough past the island's edge
    /// and a "Dangerous Waters" bar fills; when it's full something comes for you — usually
    /// a pack of sharks set on hunting you, very rarely the Ancient Leviathan, which rises
    /// from below and drags you down into the dark. Swimming back toward the island drains
    /// the bar again.
    ///
    /// The sharks are copies of the hostile sharks already in the scene (spawn some with
    /// Spawn Sea Creatures first); the leviathan is the hidden one Setup Leviathan places.
    /// Without either, that outcome is skipped.
    /// </summary>
    [RequireComponent(typeof(HealthController), typeof(WowCharacterController))]
    public class DeepSeaDanger : MonoBehaviour
    {
        [Tooltip("How far past the edge of the terrain the open sea begins.")]
        public float dangerMargin = 60f;
        [Tooltip("Seconds in open water before something comes for you.")]
        public float secondsUntilAttack = 15f;
        [Tooltip("Seconds back in safe water to drain a full bar.")]
        public float secondsToRecover = 5f;
        [Range(0f, 1f)] public float leviathanChance = 0.05f;
        public Vector2Int packSize = new(2, 3);
        [Tooltip("Never more summoned sharks alive than this at once.")]
        public int maxSummoned = 6;
        [Tooltip("Summoned sharks still alive this long after you leave the water are sent away.")]
        public float dismissAfter = 20f;

        [Header("Leviathan")]
        public float dragDepth = 60f;
        public float dragSeconds = 3.5f;

        HealthController _health;
        WowCharacterController _movement;
        CharacterController _body;

        readonly List<GameObject> _templates = new();
        readonly List<GameObject> _summoned = new();
        Leviathan _leviathan;

        float _danger;
        bool _inDanger;
        bool _eventRunning;
        float _safeSince;

        CanvasGroup _barGroup;
        HudBar _bar;
        Image _fade;

        void Awake()
        {
            _health = GetComponent<HealthController>();
            _movement = GetComponent<WowCharacterController>();
            _body = GetComponent<CharacterController>();
            BuildHud();
        }

        void Start()
        {
            // Stash an inactive copy of one hostile shark per kind, so summoning still works
            // after the originals have been killed and despawned.
            var stash = new GameObject("SummonableSharks");
            var kinds = new HashSet<string>();
            foreach (var shark in FindObjectsByType<SwimmingCreature>())
            {
                var health = shark.GetComponent<Health>();
                var stats = shark.GetComponent<CharacterStats>();
                if (health == null || health.reaction != Reaction.Hostile || stats == null || !kinds.Add(stats.characterName))
                    continue;

                shark.gameObject.SetActive(false);
                var copy = Instantiate(shark.gameObject, stash.transform);
                shark.gameObject.SetActive(true);
                copy.name = stats.characterName;
                _templates.Add(copy);
            }

            _leviathan = FindAnyObjectByType<Leviathan>(FindObjectsInactive.Include);
        }

        void Update()
        {
            bool wasInDanger = _inDanger;
            _inDanger = !_eventRunning && !_health.IsDead && _movement.IsSwimming && PastTheShelf();

            if (_inDanger && !wasInDanger)
                Hud.Error("You are swimming into dangerous waters.");

            if (_inDanger)
                _danger += Time.deltaTime / Mathf.Max(secondsUntilAttack, 0.1f);
            else if (!_eventRunning)
                _danger -= Time.deltaTime / Mathf.Max(secondsToRecover, 0.1f);
            _danger = Mathf.Clamp01(_danger);

            if (_danger >= 1f && !_eventRunning)
            {
                // Part-full again, so staying out there brings the next one sooner.
                _danger = 0.4f;
                StartCoroutine(Attack());
            }

            DismissSharksIfSafe();
            UpdateHud();
        }

        bool PastTheShelf()
        {
            var terrain = Terrain.activeTerrain;
            if (terrain == null)
                return false;
            var origin = terrain.GetPosition();
            var size = terrain.terrainData.size;
            var rect = new Rect(origin.x, origin.z, size.x, size.z);
            var flat = new Vector2(transform.position.x, transform.position.z);
            var nearest = new Vector2(Mathf.Clamp(flat.x, rect.xMin, rect.xMax), Mathf.Clamp(flat.y, rect.yMin, rect.yMax));
            return Vector2.Distance(flat, nearest) > dangerMargin;
        }

        IEnumerator Attack()
        {
            bool canLeviathan = _leviathan != null;
            if (canLeviathan && (Random.value < leviathanChance || _templates.Count == 0))
                yield return LeviathanGrab();
            else if (_templates.Count > 0)
                SummonSharks();
            else
                Debug.LogWarning("[DeepSeaDanger] Nothing to send — spawn hostile sharks (Spawn Sea Creatures) " +
                                 "or run Setup Leviathan.", this);
        }

        void SummonSharks()
        {
            int alive = 0;
            foreach (var existing in _summoned)
            {
                if (existing != null && !existing.GetComponent<Health>().IsDead)
                    alive++;
            }
            int count = Mathf.Min(Random.Range(packSize.x, packSize.y + 1), maxSummoned - alive);
            if (count <= 0)
                return;

            Hud.Error("Sharks have caught your scent!");
            for (int i = 0; i < count; i++)
            {
                var template = _templates[Random.Range(0, _templates.Count)];
                if (!TryFindSpawnPoint(out var point))
                    continue;

                var shark = Instantiate(template, point, Quaternion.LookRotation(transform.position - point));
                shark.name = template.name;
                shark.SetActive(true);
                shark.GetComponent<SwimmingCreature>().Hunt();
                _summoned.Add(shark);
            }
        }

        /// <summary>Open water 25-40 units away, a little below you.</summary>
        bool TryFindSpawnPoint(out Vector3 point)
        {
            for (int i = 0; i < 20; i++)
            {
                var direction = Random.insideUnitCircle.normalized * Random.Range(25f, 40f);
                point = transform.position + new Vector3(direction.x, -Random.Range(4f, 10f), direction.y);
                var water = WaterVolume.Containing(point);
                if (water != null && point.y > Seabed.HeightAt(point, water) + 3f &&
                    point.y < water.SurfaceHeightAt(point) - 2f)
                    return true;
            }
            point = default;
            return false;
        }

        void DismissSharksIfSafe()
        {
            _summoned.RemoveAll(shark => shark == null);
            bool safe = _health.IsDead || !_movement.IsSwimming;
            if (!safe)
            {
                _safeSince = Time.time;
                return;
            }
            if (Time.time - _safeSince < dismissAfter)
                return;

            foreach (var shark in _summoned)
            {
                var health = shark.GetComponent<Health>();
                if (health != null && !health.IsDead)
                    Destroy(shark);
            }
        }

        IEnumerator LeviathanGrab()
        {
            _eventRunning = true;
            Hud.Error("The water darkens... something vast rises beneath you.");
            yield return new WaitForSeconds(1.5f);

            // You're not swimming away from this — and you won't drown first, either, or the
            // respawn would happen mid-drag and get dragged straight back down.
            var breath = GetComponent<BreathController>();
            if (breath != null)
                breath.Refill();
            _movement.StopAutoRun();
            _movement.enabled = false;
            _body.enabled = false;

            Vector3 caught = transform.position;
            var water = WaterVolume.Containing(caught);
            var view = Camera.main != null ? Camera.main.transform.forward : transform.forward;
            yield return _leviathan.RiseAndStrike(caught, view, water != null ? water.SurfaceY : caught.y);

            Vector3 leviathanStart = _leviathan.transform.position;
            for (float t = 0f; t < dragSeconds; t += Time.deltaTime)
            {
                float k = t / dragSeconds;
                Vector3 down = Vector3.down * (dragDepth * k * k);
                transform.position = caught + down;
                _leviathan.transform.position = leviathanStart + down;
                SetFade(Mathf.Clamp01(k * 1.3f));
                yield return null;
            }
            SetFade(1f);

            // The body stays down there in the dark; the death screen takes it from here.
            // Movement stays off — dying keeps it off until you choose to respawn.
            _body.enabled = true;
            _health.Kill(DeathCause.Leviathan, "Ancient Leviathan");
            _leviathan.Hide();
            _danger = 0f;

            while (_health.IsDead)
                yield return null;

            for (float t = 0f; t < 1.5f; t += Time.deltaTime)
            {
                SetFade(1f - t / 1.5f);
                yield return null;
            }
            SetFade(0f);
            _eventRunning = false;
        }

        void BuildHud()
        {
            var row = Hud.Rect("DangerousWaters", Hud.Layer(HudLayer.Frames), new Vector2(0.5f, 1f),
                new Vector2(0f, -150f), new Vector2(280f, 34f));
            _barGroup = row.gameObject.AddComponent<CanvasGroup>();
            _barGroup.alpha = 0f;

            Hud.Label("Label", row, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(280f, 16f),
                13, TextAnchor.MiddleCenter, HudTheme.Heading, FontStyle.Bold).text = "Dangerous Waters";
            _bar = new HudBar("Bar", row, new Vector2(0.5f, 1f), new Vector2(0f, -18f), new Vector2(280f, 14f));
            _bar.Snap(0f);

            var fade = Hud.Stretch("Fade", Hud.Layer(HudLayer.Overlay));
            _fade = fade.gameObject.AddComponent<Image>();
            _fade.color = new Color(0f, 0f, 0f, 0f);
            _fade.raycastTarget = false;
        }

        void UpdateHud()
        {
            _barGroup.alpha = Mathf.MoveTowards(_barGroup.alpha, _danger > 0.01f ? 1f : 0f, 4f * Time.deltaTime);
            _bar.Set(_danger);
            _bar.FillColor = Color.Lerp(new Color(1f, 0.75f, 0.2f), HudTheme.HealthLow, _danger);
        }

        void SetFade(float alpha) => _fade.color = new Color(0f, 0f, 0f, alpha);

        [ContextMenu("Summon Sharks Now")]
        void DebugSharks()
        {
            if (Application.isPlaying)
                SummonSharks();
        }

        [ContextMenu("Summon Leviathan Now")]
        void DebugLeviathan()
        {
            if (Application.isPlaying && _leviathan != null && !_eventRunning)
                StartCoroutine(LeviathanGrab());
        }
    }
}
